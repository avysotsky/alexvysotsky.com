using CoreDTO.Configs;

namespace CoreDTO
{
    public class ResponseDTO : JsonConfig<ResponseDTO>
    {
        public bool status { get; set; } = true;
        public string? message { get; set; }
    }
}
