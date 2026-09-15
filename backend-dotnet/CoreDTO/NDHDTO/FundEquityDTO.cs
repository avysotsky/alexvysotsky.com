using System;
using System.Collections.Generic;

namespace CoreDTO.NDHDTO
{
    public class FundEquityPointDTO
    {
        public DateTime dt { get; set; }
        public decimal equityUsd { get; set; }
        public decimal? ethUsd { get; set; }
    }

    public class FundEquityCurveResponseDTO
    {
        public List<FundEquityPointDTO> points { get; set; } = new();
    }
}