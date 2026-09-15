namespace CoreDTO.NDHDTO
{
    public class MutationResponseDTO
    {
        public bool status { get; set; }
        public int accId { get; set; }
        public string accName { get; set; }
        public int cidId { get; set; }
        public string cidName { get; set; }
        public string resultText { get; set; }
        public double equityUSD { get; set; }
    }
}
