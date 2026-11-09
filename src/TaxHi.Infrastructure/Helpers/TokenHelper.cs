using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaxHi.Infrastructure.Helpers;

public static class TokenHelper
{
    public static string CreateOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public static string Hash(string value, string signingKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{signingKey}:{value}")));
}
