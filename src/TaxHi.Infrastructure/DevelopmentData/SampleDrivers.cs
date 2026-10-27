using System;
using TaxHi.Domain.Entities;

namespace TaxHi.Infrastructure.DevelopmentData;

public static class SampleDrivers
{
    public static IReadOnlyList<Driver> All { get; } = new List<Driver>
    {
        new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Ravi Kumar", "+919999999991", "DL001", new[] { "hatchback", "sedan" }),
        new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Suresh Nair", "+919999999992", "DL002", new[] { "suv", "innova" }),
    }.AsReadOnly();
}