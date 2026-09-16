using CoreDTO.NDHDTO;
namespace NDHWebService.Models;

public interface IRecordsRepository
{
    Task<long> SaveRecordAsync(
        Record record,
        CancellationToken ct = default);

    Task<List<EquityPoint>> GetAccountEquityCurveAsync(
        int accId,
        DateTime? fromUtc = null,
        CancellationToken ct = default);

    Task<List<EquityPoint>> GetFundEquityCurveAsync(
        DateTime? fromUtc = null,
        CancellationToken ct = default);

    /// <summary>
    /// Все Record по аккаунту (ndh_clearing_record + ndh_clearing_record_cid).
    /// </summary>
    Task<List<Record>> GetAccountRecordsAsync(
        int accId,
        CancellationToken ct = default);
}
