using System;

namespace CoreDTO.NDHDTO
{
    public class ClearingRequestDTO
    {
        //public DateTime? ClearingTime { get; set; } = null;
        public int accId { get; set; } = -1;
        public double equityUSD { get; set; } = -1;
        public Account acc { get; set; } = null;
    }
}
