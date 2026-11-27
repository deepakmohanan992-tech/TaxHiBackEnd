using TaxHi.Application.Models.Audit;

namespace TaxHi.Application.Services;

/// <summary>Hand-off from the request pipeline to the background audit writer. Never blocks and never throws.</summary>
public interface IApiAuditQueue
{
    /// <summary>False when the record was dropped (queue full / auditing off); the request is never affected.</summary>
    bool TryEnqueue(ApiAuditRecord record);
}
