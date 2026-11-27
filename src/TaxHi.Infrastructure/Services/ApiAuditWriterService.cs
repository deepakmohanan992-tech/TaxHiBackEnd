using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxHi.Application.Models.Audit;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Services;

/// <summary>
/// Persists queued audit records off the request path. A database failure is logged and the record skipped:
/// auditing is best-effort and must never change an API outcome.
/// </summary>
public sealed class ApiAuditWriterService(
    ApiAuditQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ApiAuditWriterService> logger) : BackgroundService
{
    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var record in queue.Reader.ReadAllAsync(stoppingToken))
                await WriteAsync(record, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down: flush what is already queued, within a short budget
            using var drain = new CancellationTokenSource(DrainBudget);
            while (!drain.IsCancellationRequested && queue.Reader.TryRead(out var record))
                await WriteAsync(record, drain.Token);
        }
    }

    private async Task WriteAsync(ApiAuditRecord record, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IApiAuditRepository>().InsertAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never rethrow: one bad row (or an unavailable audit table) must not stop the writer or touch any request.
            logger.LogWarning(ex, "Could not write the API audit record for {Method} {Endpoint} (correlation {CorrelationId}).",
                record.HttpMethod, record.ApiEndpoint, record.CorrelationId);
        }
    }
}
