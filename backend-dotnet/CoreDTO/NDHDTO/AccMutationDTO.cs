using System;

namespace CoreDTO.NDHDTO
{
    public class AccMutationDTO
    {
        public MutationType mutationType { get; set; } = MutationType.Idle;

        public Account acc { get; set; }

        public int accId { get; set; } = -1;

        public string accName { get; set; } = string.Empty;

        public int cidId { get; set; } = -1;

        public string cidName { get; set; } = string.Empty;

        public double feeRatePercents { get; set; } = 0.0;

        public DateTime dt { get; set; } = DateTime.MinValue;

        public double usdValue { get; set; } = 0.0;

        public double? equityUsd { get; set; } = null;

        // 🔹 НОВОЕ: публичный API-ключ (как есть, без хеша)
        public string? apiPublicKey { get; set; }

        // 🔹 НОВОЕ: сырой секретный ключ, который бэкенд будет хешировать
        public string? apiSecretKeyPlain { get; set; }
    }

    public enum MutationType
    {
        Idle,
        CreateAccount,
        CreateCid,
        DeleteAccount,
        DeleteCid,
        Clearing,
        SubClearing,
        Deposit,
        Withdrawal,
        CopyOutToInDataValues
    }

    public enum MutationStatus
    {
        Idle,
        Success,
        Fail,
        WrongAccId,
        WrongCidId
    }
}
