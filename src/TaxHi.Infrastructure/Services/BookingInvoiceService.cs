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

            var logo = LoadLogo();
            var vehicleImage = await LoadVehicleImageAsync(data.VehicleImageUrl, cancellationToken);

            // The customer invoice is the one tracked on the booking; it must succeed for the booking to have an invoice.
            await WritePdfAsync(fullPath, new CustomerInvoiceDocument(data, configuration, logo, vehicleImage).GeneratePdf(), cancellationToken);

            await repository.SaveInvoicePathAsync(bookingId, relativePath, cancellationToken);

            // The vendor statement is best effort and sits beside the customer file (<BookingNo>-vendor.pdf).
            await TryWriteVendorInvoiceAsync(data, logo, vehicleImage, cancellationToken);

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

    public static string VendorInvoiceFileName(string bookingNo) => $"{SafeFileName(bookingNo)}-vendor.pdf";

    private async Task TryWriteVendorInvoiceAsync(BookingInvoiceDto data, byte[]? logo, byte[]? vehicleImage, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolve(VendorInvoiceFileName(data.BookingNo), out var vendorPath)) return;
            await WritePdfAsync(vendorPath, new VendorInvoiceDocument(data, configuration, logo, vehicleImage).GeneratePdf(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vendor invoice generation failed for booking {BookingNo}; the customer invoice is unaffected.", data.BookingNo);
        }
    }

    /// <summary>Write to a temp file, then swap it in, so a reader never sees a half-written PDF.</summary>
    private static async Task WritePdfAsync(string fullPath, byte[] pdf, CancellationToken cancellationToken)
    {
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
    }

    private static readonly HttpClient ImageClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Best-effort vehicle photo for the PDFs; any problem yields null and the PDF shows a placeholder.</summary>
    private async Task<byte[]?> LoadVehicleImageAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
            using var response = await ImageClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 5_000_000) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length is > 0 and <= 5_000_000 ? bytes : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vehicle image could not be loaded for the invoice; using the placeholder.");
            return null;
        }
    }

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
