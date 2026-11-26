using TaxHi.Application.Models;

namespace TaxHi.Application.Services;

public interface IAirportTerminalService
{
    /// <summary>Paginated, searchable active airport terminals under the sector of the given vendor.</summary>
    Task<CommonResponse<PagedResult<AirportTerminalItem>>> GetAirportTerminalsAsync(
        int? vendorId,
        PaginationRequest request,
        CancellationToken cancellationToken = default);
}
