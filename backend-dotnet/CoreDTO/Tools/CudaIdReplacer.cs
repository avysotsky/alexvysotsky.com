namespace CoreDTO.Tools
{
    public static class CudaIdReplacer
    {
        public static string ReplaceCudaId(this string source, string value)
        {
            return source.Replace("[cudaId]", value);
        }
    }
}
