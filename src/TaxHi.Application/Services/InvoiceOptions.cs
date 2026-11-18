namespace TaxHi.Application.Services;

/// <summary>Bound from the "Invoice" configuration section.</summary>
public sealed class InvoiceOptions
{
    /// <summary>Folder for generated PDFs. Relative paths resolve against the application content root.</summary>
    public string StoragePath { get; set; } = "wwwroot/invoices";

    /// <summary>Optional PNG/JPG logo. Relative paths resolve against the content root; a text logo is drawn when absent.</summary>
    public string? LogoPath { get; set; }

    public string CompanyName { get; set; } = "TaxHi";
    public string CompanyAddress { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = "support@taxhi.com";
    public string SupportPhone { get; set; } = string.Empty;
    public string Website { get; set; } = "www.taxhi.com";
}
