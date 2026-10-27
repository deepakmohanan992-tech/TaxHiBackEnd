using Microsoft.Extensions.Options;
using TaxHi.Application.Exceptions;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.FareQuotes;
using TaxHi.Application.Features.FareQuotes.Dtos;
using TaxHi.Domain.Enums;

namespace TaxHi.UnitTests;

public class FareCalculatorTests
{
    [Fact]
    public void Calculate_UsesMinimumBillableDistance_WhenNoDistanceProvided()
    {
        var options = Options.Create(new PricingOptions { MinimumBillableDistanceKm = 130, PerKmRate = 12, BaseFare = 500, TaxRate = 0.05m });
        var calc = new DefaultFareCalculator(options);
        var result = calc.Calculate(new FareCalculationRequest("A", "B", TripType.OneWay, "sedan", DateTimeOffset.UtcNow, null, 2, null, null, true));
        Assert.True(result.Breakdown.Total > 0);
        Assert.Equal(130m * 12m * 1.2m, result.Breakdown.DistanceCharge);
    }

    [Fact]
    public void Calculate_AppliesRoundTripDriverAllowance()
    {
        var options = Options.Create(new PricingOptions { DriverAllowancePerDay = 300, MinimumBillableDistanceKm = 10, PerKmRate = 1, BaseFare = 100, TaxRate = 0 });
        var calc = new DefaultFareCalculator(options);
        var oneWay = calc.Calculate(new FareCalculationRequest("A", "B", TripType.OneWay, "hatchback", DateTimeOffset.UtcNow, null, 1, 20, null, true));
        var round = calc.Calculate(new FareCalculationRequest("A", "B", TripType.RoundTrip, "hatchback", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), 1, 20, null, true));
        Assert.True(round.Breakdown.DriverAllowance > oneWay.Breakdown.DriverAllowance);
    }
}