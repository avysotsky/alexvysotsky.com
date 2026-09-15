namespace CoreDTO.NDHDTO
{
    public class DbReportDTO
    {
        public int accId { get; set; } = -1;
        public int cidId { get; set; } = -1;
        public RecordStatus status { get; set; } = RecordStatus.Idle;
        public string message { get; set; } = string.Empty;
        public double equityUSD { get; set; }
    }

    public enum RecordStatus
    {
        Idle,
        UncompletedSuccess,
        Success,
        Fail,
        WrongAccId,
        WrongCidId
    }
}


