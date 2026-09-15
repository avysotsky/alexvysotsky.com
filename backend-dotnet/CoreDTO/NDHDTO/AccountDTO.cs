using System.Collections.Generic;
using System;

namespace CoreDTO.NDHDTO
{
    public class Account
    {
        public int accId { get; set; } = 0;
        public string accName { get; set; } = "";

        // ✅ include flags moved from ndh_user → ndh_account
        public bool includeUsdcInTotal { get; set; } = true;
        public bool includeBtcInTotal { get; set; } = true;
        public bool includeEthInTotal { get; set; } = true;

        // --- Clearing schedule ---
        public bool clearingEnabled { get; set; } = false;

        /// <summary>None | Daily | Weekly | Monthly</summary>
        public string clearingPeriod { get; set; } = "None";

        /// <summary>Time of day in UTC, format HH:mm:ss (e.g., "09:30:00")</summary>
        public string clearingTimeUtc { get; set; } = "00:00:00";

        /// <summary>Weekly only: 1=Mon ... 7=Sun</summary>
        public int? clearingDayOfWeek { get; set; } = null;

        /// <summary>Monthly only: 1..31 (if month has fewer days -> clamped to last day)</summary>
        public int? clearingDayOfMonth { get; set; } = null;

        /// <summary>Next scheduled clearing timestamp (UTC)</summary>
        public DateTime? nearestClearingTimestampUtc { get; set; } = null;

        public List<CID> listOfCIDs { get; set; } = new();
        public List<Record> listOfRecords { get; set; } = new();
    }

    public class Record
    {
        public int accId { get; set; } = -1;
        public int cidId { get; set; } = -1;
        public DateTime date { get; set; } = DateTime.MinValue;
        public double equityUSD { get; set; } = 0.0;
        public MutationType type { get; set; } = MutationType.Idle;
        public List<CID> listOfCIDs { get; set; } = new();
        public double newTotallyInvestedUSD { get; set; } = 0.0;    // = Sum of all CIDs NewInvestmentValueUSD

        public void CopyClientShareFromPrevRecord()
        {
            foreach (var cid_ in listOfCIDs)
            {
                var i = cid_.InputData;
                var o = cid_.OutputData;

                if (i.DepositWithdrawalValueUSD != 0)
                {
                    i.InvestmentUSD += i.DepositWithdrawalValueUSD;
                    i.DepositWithdrawalValueUSD = 0;
                }

                i.ClientSharePercents = o.NewClientSharePercents;
                i.ReinvestedUSD = o.NewInvestmentValueUSD - i.InvestmentUSD;
            }
        }

        public void CalcOutputs()
        {
            const double eps = 1e-9; // epsilon for double
            const int precision = 2;

            try
            {
                // Calculate the output for each CID
                foreach (var cid in listOfCIDs)
                    cid.CalcOutput(this);

                // SubClearing
                foreach (var cid in listOfCIDs)
                {
                    var i = cid.InputData;
                    var o = cid.OutputData;
                    o.NewInvestmentValueUSD = o.TotallyInvestedUSD + o.PnLUSD - o.FeeUSD + i.DepositWithdrawalValueUSD;
                    newTotallyInvestedUSD += o.NewInvestmentValueUSD;
                }
                foreach (var cid in listOfCIDs)
                {
                    var o = cid.OutputData;
                    if (newTotallyInvestedUSD > 0.0)
                    {
                        var share = (decimal)o.NewInvestmentValueUSD * 100m / (decimal)newTotallyInvestedUSD;
                        o.NewClientSharePercents = Math.Round(share, 13); // overload для decimal
                    }
                    else
                    {
                        o.NewClientSharePercents = 0m;
                    }
                }
            }
            catch (Exception ex)
            {
                int f = 2;
            }
        }
    }
}
