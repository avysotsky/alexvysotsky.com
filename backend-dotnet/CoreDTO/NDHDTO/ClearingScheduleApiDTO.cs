namespace CoreDTO.NDHDTO
{
    public class SetAccountScheduleRequestDTO
    {
        public int accountId { get; set; }
        public bool clearingEnabled { get; set; }
        public string clearingPeriod { get; set; } = "None";
        public string clearingTimeUtc { get; set; } = "00:00:00";
        public int? clearingDayOfWeek { get; set; }
        public int? clearingDayOfMonth { get; set; }
    }

    public class DoClearingNowRequestDTO
    {
        public int accountId { get; set; }
    }
}