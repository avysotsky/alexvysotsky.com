namespace CoreDTO.NDHDTO
{
    public sealed class PublicContactUsRequestDTO
    {
        public string name { get; set; } = "";
        public string email { get; set; } = "";
        public string message { get; set; } = "";
    }

    public sealed class PublicContactUsResponseDTO
    {
        public bool ok { get; set; }
        public string? error { get; set; }
    }
}