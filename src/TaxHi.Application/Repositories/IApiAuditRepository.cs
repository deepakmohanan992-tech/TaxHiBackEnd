using TaxHi.Application.Models.Audit;

namespace TaxHi.Application.Repositories;

public interface IApiAuditRepository
{
    Task InsertAsync(ApiAuditRecord record, CancellationToken cancellationToken = default);
}
