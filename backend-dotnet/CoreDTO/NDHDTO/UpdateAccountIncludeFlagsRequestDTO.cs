namespace CoreDTO.NDHDTO
{
    public sealed class UpdateAccountIncludeFlagsRequestDTO
    {
        public int accId { get; set; }
        public bool includeUsdcInTotal { get; set; }
        public bool includeBtcInTotal { get; set; }
        public bool includeEthInTotal { get; set; }
    }
}
