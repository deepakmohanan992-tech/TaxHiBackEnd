using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using TaxHi.Application.Models.Audit;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Infrastructure.Services;

/// <summary>
/// In-memory hand-off between the request pipeline and <see cref="ApiAuditWriterService"/>. The bounded channel drops
/// new records when full, so a slow or unavailable audit database can never slow down or fail an API request.
/// </summary>
public sealed class ApiAuditQueue : IApiAuditQueue
{
    private readonly Channel<ApiAuditRecord> _channel;
    private long _dropped;

    public ApiAuditQueue(IConfiguration configuration)
    {
        _channel = Channel.CreateBounded<ApiAuditRecord>(new BoundedChannelOptions(AuditSettings.QueueCapacity(configuration))
        {
            FullMode = BoundedChannelFullMode.Wait,   // TryWrite returns false when full instead of blocking
            SingleReader = true
        });
    }

    public ChannelReader<ApiAuditRecord> Reader => _channel.Reader;

    /// <summary>Records dropped because the queue was full (for diagnostics).</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(ApiAuditRecord record)
    {
        if (_channel.Writer.TryWrite(record)) return true;
        Interlocked.Increment(ref _dropped);
        return false;
    }
}
