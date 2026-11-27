using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TaxHi.Infrastructure.Common;

/// <summary>
/// Removes sensitive data from request/response payloads, query strings and headers BEFORE they are written to the
/// audit table: passwords, OTPs, tokens, signatures, payment secrets and card data are replaced; e-mail and phone
/// numbers are masked. Anything that is not JSON is not stored at all.
/// </summary>
public static partial class AuditRedactor
{
    public const string Redacted = "***REDACTED***";
    public const string NotStored = "[non-JSON payload not stored]";
    private const string TruncatedSuffix = "...[truncated]";

    // Whole words (a key is split on camelCase / underscores / dashes) that mark a value as secret.
    private static readonly HashSet<string> SecretWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "pwd", "otp", "token", "secret", "signature", "authorization", "cvv", "cvc", "pan", "pin",
        "aadhaar", "aadhar", "passport", "cookie", "jwt", "apikey", "card"
    };

    // Fragments that are secret wherever they appear in the lower-cased, separator-free key.
    private static readonly string[] SecretFragments =
        ["password", "passwd", "secret", "token", "signature", "authorization", "apikey", "cvv", "cardnumber", "accountnumber", "cookie"];

    private static readonly HashSet<string> EmailWords = new(StringComparer.OrdinalIgnoreCase) { "email", "mail" };
    private static readonly HashSet<string> PhoneWords = new(StringComparer.OrdinalIgnoreCase) { "mobile", "phone", "msisdn", "whatsapp", "contact" };

    // Only these headers are ever stored; everything else (Authorization, Cookie, API keys, signatures) is dropped.
    private static readonly HashSet<string> SafeHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "accept-language", "content-type", "content-length", "host", "origin", "referer", "x-correlation-id",
        "x-requested-with", "x-razorpay-event-id", "vendorid"
    };

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex WordPattern();

    public static bool IsSecretKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var compact = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (SecretFragments.Any(compact.Contains)) return true;
        return WordPattern().Matches(key).Any(m => SecretWords.Contains(m.Value));
    }

    private static bool HasWord(string key, HashSet<string> words) => WordPattern().Matches(key).Any(m => words.Contains(m.Value));

    /// <summary>Redacted JSON text capped at <paramref name="maxChars"/>; null when empty, <see cref="NotStored"/> when not JSON.</summary>
    public static string? RedactJson(string? json, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(json) || maxChars <= 0) return null;

        JsonNode? node;
        try { node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { return NotStored; }
        if (node is null) return null;

        Walk(node);
        return Truncate(node.ToJsonString(), maxChars);
    }

    private static void Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (child is null) continue;

                    if (IsSecretKey(key))
                        obj[key] = Redacted;
                    else if (child is JsonValue value && value.TryGetValue<string>(out var text))
                        obj[key] = MaskPersonal(key, text);
                    else
                        Walk(child);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    if (item is not null) Walk(item);
                break;
        }
    }

    private static string MaskPersonal(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (HasWord(key, EmailWords))
        {
            var at = value.IndexOf('@');
            return at > 0 ? value[0] + "***" + value[at..] : Redacted;
        }
        if (HasWord(key, PhoneWords))
        {
            var digits = value.Count(char.IsDigit);
            return digits <= 2 ? Redacted : new string('*', digits - 2) + new string(value.Where(char.IsDigit).TakeLast(2).ToArray());
        }
        return value;
    }

    /// <summary>"/path?a=1&amp;token=***": sensitive query values are replaced.</summary>
    public static string RedactUrl(string path, string? queryString, int maxChars = 1000)
    {
        if (string.IsNullOrEmpty(queryString)) return Truncate(path, maxChars);

        var parts = queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) return pair;
            var key = Uri.UnescapeDataString(pair[..eq]);
            return IsSecretKey(key) ? pair[..eq] + "=" + Redacted : MaskedQueryValue(key, pair);
        });
        return Truncate(path + "?" + string.Join('&', parts), maxChars);
    }

    private static string MaskedQueryValue(string key, string pair)
    {
        var eq = pair.IndexOf('=');
        var masked = MaskPersonal(key, Uri.UnescapeDataString(pair[(eq + 1)..]));
        return pair[..(eq + 1)] + Uri.EscapeDataString(masked);
    }

    /// <summary>Safe headers only, as "Name: value" lines.</summary>
    public static string? SafeHeaderText(IEnumerable<KeyValuePair<string, string>> headers, int maxChars = 2000)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in headers)
            if (SafeHeaders.Contains(name))
                sb.Append(name).Append(": ").Append(value.Replace('\r', ' ').Replace('\n', ' ')).Append('\n');
        return sb.Length == 0 ? null : Truncate(sb.ToString().TrimEnd(), maxChars);
    }

    public static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..Math.Max(0, maxChars - TruncatedSuffix.Length)] + TruncatedSuffix;
}
