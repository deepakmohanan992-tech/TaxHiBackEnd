using System.Security.Cryptography;

namespace TaxHi.Infrastructure.Services;

public static class OtpService
{
    public static string CreateNumericCode(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        return string.Concat(Enumerable.Range(0, length)
            .Select(_ => RandomNumberGenerator.GetInt32(0, 10)));
    }
}
