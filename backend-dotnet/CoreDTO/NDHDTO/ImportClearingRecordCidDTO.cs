namespace CoreDTO.NDHDTO
{
    public sealed class ImportClearingRecordCidDTO
    {
        public int AccountId { get; set; }
        public double DepositWithdrawalUsd { get; set; }
        public long CidId { get; set; }
        public string CidName { get; set; } = string.Empty;
        public double InvestmentUsd { get; set; }
        public double ReinvestedUsd { get; set; }
        public decimal ClientSharePercents { get; set; }
        public double ClientEquityUsd { get; set; }
        public double PnLUsd { get; set; }
        public double FeePercents { get; set; }
        public double FeeUsd { get; set; }
        public double ProfitUsd { get; set; }
        public double NewInvestmentValueUsd { get; set; }
        public decimal NewClientSharePercents { get; set; }
    }
}