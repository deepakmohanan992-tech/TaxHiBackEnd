namespace TaxHi.Application.Models;

/// <summary>One active airport terminal, as returned by USP_GetAirportTerminals.</summary>
public sealed class AirportTerminal
{
    public int AirportTerminalId { get; set; }
    public string AirportName { get; set; } = string.Empty;

}
