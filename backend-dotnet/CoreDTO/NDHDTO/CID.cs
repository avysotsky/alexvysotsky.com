using System;

namespace CoreDTO.NDHDTO
{
    public class CID
    {
        private int precision = 2;

        public CIDInputData InputData { get; set; }
        public CIDOutputData OutputData { get; set; }

        public void CalcOutput(Record owner)
        {
            var i = InputData;
            var o = OutputData;

            o.TotallyInvestedUSD = i.InvestmentUSD + i.ReinvestedUSD;
            o.ClientEquityUSD = owner.equityUSD * ((double)i.ClientSharePercents / 100.0);

            o.PnLUSD = o.ClientEquityUSD - o.TotallyInvestedUSD;

            // Если комиссия только с положительного PnL — так безопаснее:
            var pnlForFee = o.PnLUSD > 0.00000001 ? o.PnLUSD : 0;
            o.FeeUSD = pnlForFee * (i.FeeRatePercents * 0.01);

            o.ProfitUSD = o.PnLUSD > 0 ? Math.Round(o.PnLUSD - o.FeeUSD, precision) : 0;

            o.ProfitPercents = o.TotallyInvestedUSD == 0 ? 0 : o.PnLUSD * 100 / o.TotallyInvestedUSD;

            // IMPORTANT:
            // o.LastMutationPnLPercents НЕ считается тут.
            // Его выставляет AccMutationsPocessor ТОЛЬКО в момент Deposit/Withdrawal.
        }
    }

    public class CIDInputData
    {
        public DateTime Timestamp { get; set; } = DateTime.MinValue;
        public long Id { get; set; } = 0;
        public string Name { get; set; } = "";
        public double InvestmentUSD { get; set; } = 0.0;
        public double ReinvestedUSD { get; set; } = 0.0;
        public decimal ClientSharePercents { get; set; } = 0;
        public double DepositWithdrawalValueUSD { get; set; } = 0.0;
        public double FeeRatePercents { get; set; } = 0.0;
        public bool ShowInTable { get; set; } = true;
        public double? LastMutationPnLPercents { get; set; } = null;
    }

    public class CIDOutputData
    {
        public double TotallyInvestedUSD { get; set; } = 0.0;                 // = InvestmentUSD + ReinvestedUSD
        public double ClientEquityUSD { get; set; } = 0.0;                    // = EquityUSD * ClientSharePercents * 0.01
        public double ClientEquityBeforeMutationUSD { get; set; } = 0.0;      // = ClientEquityUSD + DepositWithdrawalValueUSD
        public double PnLUSD { get; set; } = 0.0;                             // = ClientEquityUSD - TotallyInvestedUSD
        public double FeeUSD { get; set; } = 0.0;                             // = PnLUSD * FeeRatePercents * 0.01
        public double ProfitUSD { get; set; } = 0.0;                          // = PnLUSD - FeeUSD
        public double ProfitPercents { get; set; } = 0.0;                     // = TotallyInvestedUSD == 0 ? 0 : ProfitUSD * 100 / TotallyInvestedUSD
        public double TotalProfitPercents { get; set; } = 0.0;                // = InitInvestmentUSD == 0 ? 0 : (TotallyInvestedUSD + ProfitUSD - InitInvestmentUSD) * 100 / InitInvestmentUSD
        public double NewInvestmentValueUSD { get; set; } = 0.0;              // = TotallyInvestedUSD + ProfitUSD
        public decimal NewClientSharePercents { get; set; } = 0;              // = NewInvestmentValueUSD * 100 / NewTotallyInvestedUSD
    }
}
