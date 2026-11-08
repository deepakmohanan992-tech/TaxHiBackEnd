using System;
using System.Collections.Generic;
using System.Text;

namespace TaxHi.Application.Models
{
    public sealed class SeatingCapacity
    {
        public int CapacityId { get; set; }
        public string CapacityName { get; init; } = string.Empty;
    }
}
