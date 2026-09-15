using System;

namespace CoreDTO.NDHDTO
{
    public class DepositCalcDTO
    {
        public Account acc { get; set; } = null;
        public int cidId { get; set; } = -1;
        public DateTime dt { get; set; } = DateTime.MinValue;
        public double usdValue { get; set; } = 0.0;
    }
}
