using TaxHi.Application.Models;
using TaxHi.Application.Models.Authentication;

namespace TaxHi.Application.Services;

public interface IAuthenticationService
{
    Task<CommonResponse<object>> SendOtpAsync(SendOtpRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<TokenResponseDto>> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<SessionContextResponse>> GetSessionContextAsync(long? userId, CancellationToken cancellationToken = default);
    Task<CommonResponse<TokenResponseDto>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<object>> LogoutAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<object>> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<object>> SendGuestCancellationOtpAsync(GuestCancellationOtpRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<object>> VerifyGuestCancellationOtpAsync(VerifyGuestCancellationOtpRequest request, CancellationToken cancellationToken = default);
    Task<CommonResponse<GuestBookingAccessDto>> ValidateGuestBookingAccessAsync(ValidateGuestBookingAccessRequest request, CancellationToken cancellationToken = default);
}
