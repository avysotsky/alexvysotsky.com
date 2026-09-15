namespace CoreDTO
{
    public class Result
    {
        public bool Status { get; set; } = false;
        public string Message { get; set; } = "";
        public double? DoubleValue { get; set; } = null;
        public int? IntValue { get; set; } = null;
    }
}
