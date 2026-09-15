using System;

namespace NDHWebService.Models
{
    public sealed class EquityPoint
    {
        public DateTime DtUtc { get; set; }
        public double EquityUsd { get; set; }
    }
}
