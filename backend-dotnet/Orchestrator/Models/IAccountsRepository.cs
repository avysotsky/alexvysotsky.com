using CoreDTO.NDHDTO;

namespace NDHWebService.Models;

public interface IAccountsRepository
{
    Task<List<Account>> GetAccountsAsync(CancellationToken ct = default);

    Task<(bool Ok, string Message, int AccId)> CreateAccountAsync(
        string accName,
        string? apiPublicKey,
        string? apiSecretKey,
        CancellationToken ct = default);

    Task<(bool Ok, string Message)> DeleteAccountAsync(
        int accId,
        CancellationToken ct = default);

    Task<(bool Ok, string Message)> CreateCidAsync(
        int accId,
        CID cid,
        CancellationToken ct = default);

    Task<(bool Ok, string Message)> DeleteCidAsync(
        int accId,
        long cidId,
        CancellationToken ct = default);

    Task<(bool Ok, string Message, DateTime? NearestClearingTimestampUtc)> SetAccountClearingScheduleAsync(
        int accId,
        bool clearingEnabled,
        string clearingPeriod,
        TimeSpan clearingTimeUtc,
        int? clearingDayOfWeek,
        int? clearingDayOfMonth,
        DateTime? nearestClearingTimestampUtc,
        CancellationToken ct = default);
}
