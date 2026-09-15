namespace CoreDTO.NDHDTO
{
    public class CreateCidDTO
    {
        public int accId { get; set; } = -1;
        public string accName { get; set; } = "";
        public int cidId { get; set; } = -1;
        public string cidName { get; set; } = "";
        public double feeRatePercents { get; set; } = 0;
    }
}
