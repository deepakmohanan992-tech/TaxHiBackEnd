using System.ComponentModel;

namespace TaxHi.Application.Enums;

public enum Recommended
{
    [Description("Price: Low to High")]
    PriceLowToHigh,

    [Description("Price: High to Low")]
    PriceHighToLow
}
