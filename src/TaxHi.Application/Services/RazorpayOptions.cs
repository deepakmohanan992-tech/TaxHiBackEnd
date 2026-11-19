namespace TaxHi.Application.Services;

/// <summary>"Razorpay" configuration section. KeySecret / WebhookSecret must never leave the backend.</summary>
public sealed class RazorpayOptions
{
    /// <summary>"Test" or "Live". Informational guard: a Live environment must use an rzp_live_ key and vice versa.</summary>
    public string Environment { get; set; } = "Test";
    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    /// <summary>Secret configured on the Razorpay dashboard webhook. Webhooks are rejected while this is empty.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.razorpay.com/v1/";
    public int HttpTimeoutSeconds { get; set; } = 20;
}
