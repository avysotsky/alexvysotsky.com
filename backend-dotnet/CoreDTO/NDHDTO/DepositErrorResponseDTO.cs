using CoreDTO.Configs;

namespace CoreDTO.NDHDTO
{
    public class DepositErrorResponseDTO
    {
        public Account acc { get; set; } = null;
        public int cidId { get; set; } = -1;
        public string message { get; set; } = "";
    }
}
