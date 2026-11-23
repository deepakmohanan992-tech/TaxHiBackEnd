using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;
using TaxHi.Infrastructure.Pdf;

namespace TaxHi.Infrastructure.Services;

public sealed class BookingInvoiceService(
    IBookingInvoiceRepository repository,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<BookingInvoiceService> logger) : IBookingInvoiceService
{
    static BookingInvoiceService() => QuestPDF.Settings.License = LicenseType.Community;

    // Read from the "Invoice" section of appsettings.json.
    private string StoragePath => configuration["Invoice:StoragePath"] is { Length: > 0 } p ? p : "wwwroot/invoices";
    private string? LogoPath => configuration["Invoice:LogoPath"];

    public static string DownloadUrl(long bookingId) => $"/api/Booking/DownloadInvoice/{bookingId}";

    public async Task<BookingInvoiceResult> EnsureInvoiceAsync(
        long bookingId, long userId, string? bookingNo = null, bool regenerate = false,
        CancellationToken cancellationToken = default)
    {
        var result = new BookingInvoiceResult { BookingId = bookingId, BookingNo = bookingNo };

        try
        {
            // Reuse an invoice that is already stored and still on disk (no duplicate files on duplicate/retried requests).
            if (!regenerate)
            {
                var existing = await repository.GetInvoiceFileInfoAsync(bookingId, userId, cancellationToken);
                if (existing is not null)
                {
                    result.BookingNo = existing.BookingNo;
                    if (TryResolve(existing.InvoiceFilePath, out var existingPath) && File.Exists(existingPath))
                    {
                        result.InvoiceGenerated = true;
                        result.InvoiceUrl = DownloadUrl(bookingId);
                        return result;
                    }
                }
            }

            var data = await repository.GetInvoiceDataAsync(bookingId, userId, cancellationToken);
            if (data is null)
            {
                logger.LogWarning("Invoice requested for unknown or foreign booking {BookingId} by user {UserId}.", bookingId, userId);
                return result;
            }

            result.BookingNo = data.BookingNo;
            var relativePath = $"{SafeFileName(data.BookingNo)}.pdf";
            if (!TryResolve(relativePath, out var fullPath))
                throw new InvalidOperationException($"Invoice path for booking {data.BookingNo} is outside the storage folder.");

            var pdf = new BookingInvoiceDocument(data, configuration, LoadLogo()).GeneratePdf();

            // Write to a temp file, then swap it in, so a reader never sees a half-written PDF.
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, pdf, cancellationToken);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            await repository.SaveInvoicePathAsync(bookingId, relativePath, cancellationToken);

            result.InvoiceGenerated = true;
            result.InvoiceUrl = DownloadUrl(bookingId);
            return result;
        }
        catch (Exception ex)
        {
            // The booking is already committed; an invoice problem must never surface as a booking failure.
            logger.LogError(ex, "Invoice generation failed for booking {BookingId} (BookingNo {BookingNo}).", bookingId, result.BookingNo);
            result.InvoiceGenerated = false;
            result.InvoiceUrl = null;
            return result;
        }
    }

    public string? ResolveExistingInvoicePath(string? storedRelativePath) =>
        TryResolve(storedRelativePath, out var path) && File.Exists(path) ? path : null;

    public async Task<CommonResponse<BookingInvoiceResult>> RetryInvoiceAsync(
        long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        var info = await repository.GetInvoiceFileInfoAsync(bookingId, userId, cancellationToken);
        if (info is null)
            return ResponseFactory.Failure<BookingInvoiceResult>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");

        // Idempotent: keeps a healthy existing file, only regenerates when it is missing.
        var result = await EnsureInvoiceAsync(bookingId, userId, info.BookingNo, regenerate: false, cancellationToken);
        return result.InvoiceGenerated
            ? ResponseFactory.Success(result, "Invoice is ready.")
            : ResponseFactory.Failure<BookingInvoiceResult>(StatusCodes.Status500InternalServerError,
                "Unable to generate the invoice right now. Please try again.", "INVOICE_GENERATION_FAILED");
    }

    public async Task<CommonResponse<BookingInvoiceFile>> GetInvoiceFileAsync(
        long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        // Missing and foreign bookings are indistinguishable on purpose.
        var info = await repository.GetInvoiceFileInfoAsync(bookingId, userId, cancellationToken);
        if (info is null)
            return ResponseFactory.Failure<BookingInvoiceFile>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");

        if (!(TryResolve(info.InvoiceFilePath, out var path) && File.Exists(path)))
        {
            // Not generated yet (e.g. earlier failure) or file lost: rebuild once.
            logger.LogWarning("Invoice file missing for booking {BookingId}; regenerating.", bookingId);
            var regenerated = await EnsureInvoiceAsync(bookingId, userId, info.BookingNo, regenerate: true, cancellationToken);
            var refreshed = regenerated.InvoiceGenerated
                ? await repository.GetInvoiceFileInfoAsync(bookingId, userId, cancellationToken)
                : null;
            if (refreshed is null || !TryResolve(refreshed.InvoiceFilePath, out path) || !File.Exists(path))
                return ResponseFactory.Failure<BookingInvoiceFile>(StatusCodes.Status404NotFound,
                    "The invoice for this booking is not available yet. Please try again shortly.", "INVOICE_NOT_AVAILABLE");
        }

        return ResponseFactory.Success(
            new BookingInvoiceFile { FullPath = path, FileName = $"Invoice-{SafeFileName(info.BookingNo)}.pdf" },
            "Invoice ready.");
    }

    // ---------------------------------------------------------------- helpers

    private string StorageRoot =>
        Path.GetFullPath(Path.IsPathRooted(StoragePath)
            ? StoragePath
            : Path.Combine(environment.ContentRootPath, StoragePath));

    /// <summary>Maps a stored relative path to a full path, refusing anything that escapes the storage folder.</summary>
    private bool TryResolve(string? relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var root = StorageRoot;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        fullPath = candidate;
        return true;
    }

    private static string SafeFileName(string bookingNo)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(bookingNo.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "booking" : cleaned;
    }

    private byte[]? LoadLogo()
    {
        if (string.IsNullOrWhiteSpace(LogoPath)) return null;
        try
        {
            var path = Path.IsPathRooted(LogoPath)
                ? LogoPath
                : Path.Combine(environment.ContentRootPath, LogoPath);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invoice logo could not be loaded; using the text logo.");
            return null;
        }
    }
}
