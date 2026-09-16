using CoreDTO.NDHDTO;
using static CoreDTO.Logger.Enums;
using LogLevel = CoreDTO.Logger.Enums.LogLevel;

namespace NDHWebService.Models;

public class AccMutationsPocessor :
                                 IObserver<AccMutationDTO>,
                                 IObserver<AccountsDataCacheDTO>,

                                 IObservable<DepositErrorResponseDTO>,
                                 IObservable<SaveDepositRecordToDbDTO>,
                                 IObservable<CreateCidDTO>
{
    private readonly List<IObserver<DepositErrorResponseDTO>> depositErrorResponseObservers = new();
    private readonly List<IObserver<SaveDepositRecordToDbDTO>> saveDepositReportDTOObservers = new();
    private readonly List<IObserver<CreateCidDTO>> createCidDTOObservers = new();

    private AccountsDataCacheDTO? accountsDataCacheDTO = new();
    private readonly LogAction logAction;

    public AccMutationsPocessor(LogAction logAction)
    {
        this.logAction = logAction;
    }

    private CID? GetCidById(Account acc, int cidId)
    {
        return acc.listOfCIDs?.FirstOrDefault(c => c != null && c.InputData != null && c.InputData.Id == cidId);
    }

    private double GetEquityUSD(AccMutationDTO dto)
    {
        return dto.equityUsd == null ? accountsDataCacheDTO.equityUSD[dto.acc.accId] : (double)dto.equityUsd;
    }

    private void CreateCid(AccMutationDTO dto)
    {
        logAction($"[AccMutationsProcessor]        [CreateCid]  [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] UsdValue = {dto.usdValue} USD, dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}", LogLevel.llBaselogic);
        Publish(new CreateCidDTO() { accId = dto.acc.accId, accName = dto.acc.accName, cidId = dto.cidId, cidName = dto.cidName, feeRatePercents = dto.feeRatePercents });
    }

    private static double CalcLastMutationPnLPercents(CID cid, Record record)
    {
        var i = cid.InputData;
        var o = cid.OutputData;

        double InvestmentUSD = i.InvestmentUSD;

        Console.WriteLine($"[CalcLastMutationPnLPercents] i.InvestmentUSD = {i.InvestmentUSD}, InvestmentUSD = {InvestmentUSD}, i.ReinvestedUSD = {i.ReinvestedUSD}, i.Timestamp = {i.Timestamp}");

        i.InvestmentUSD = i.InvestmentUSD + i.ReinvestedUSD;
        i.ReinvestedUSD = 0;

        Console.WriteLine($"[CalcLastMutationPnLPercents] i.InvestmentUSD = {i.InvestmentUSD}, InvestmentUSD = {InvestmentUSD}, i.ReinvestedUSD = {i.ReinvestedUSD}, i.Timestamp = {i.Timestamp}, o.ClientEquityUSD = {o.ClientEquityUSD}");

        if (i.InvestmentUSD <= 0.00000001) return 0.0;

        double result = 100.0 * (o.ClientEquityUSD - InvestmentUSD) / InvestmentUSD;
        Console.WriteLine($"[CalcLastMutationPnLPercents] result = {result}");
        return result;
    }

    private void ClearLastMutationPnLForAllCids(Account acc)
    {
        if (acc?.listOfCIDs == null) return;
        foreach (var c in acc.listOfCIDs)
        {
            if (c?.InputData == null) continue;
            c.InputData.LastMutationPnLPercents = null;
        }
    }

    private void CalcDeposit(AccMutationDTO dto)
    {
        logAction(
            $"[AccMutationsProcessor]        [Deposit]    [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] UsdValue = {dto.usdValue} USD, dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.",
            LogLevel.llBaselogic
        );

        string message = "";

        var record = new Record()
        {
            accId = dto.acc.accId,
            cidId = dto.cidId,
            date = dto.dt,
            equityUSD = GetEquityUSD(dto),
            type = MutationType.Deposit
        };

        var cid = GetCidById(dto.acc, dto.cidId);
        if (cid == null)
        {
            message = $"[AccMutationsProcessor]        [Deposit][ERROR] GetCidById({dto.acc.accName}, {dto.cidId}) = null!";
            logAction(message, LogLevel.llBaselogic);
            Publish(new DepositErrorResponseDTO() { acc = dto.acc, cidId = dto.cidId, message = message });
            return;
        }

        record.listOfCIDs = dto.acc.listOfCIDs;

        // ✅ чтобы в БД/на UI не тащилось старое значение (в текущем состоянии аккаунта)
        ClearLastMutationPnLForAllCids(dto.acc);

        record.CopyClientShareFromPrevRecord();

        cid.InputData.DepositWithdrawalValueUSD = dto.usdValue;

        record.CalcOutputs();

        // --------------------------------------------------------------------
        // ✅ prevLastMut по "record_id" порядку:
        // listOfRecords загружается из БД как: order by dt_utc, record_id
        // => последний подходящий элемент = максимальный record_id
        // И НЕ фильтруем по record.cidId, потому что у Clearing cid_id = NULL.
        // --------------------------------------------------------------------
        double? prevLastMut = null;
        Record? prevRec = null;

        var recs = dto.acc.listOfRecords;
        if (recs != null && recs.Count > 0)
        {
            for (int ri = recs.Count - 1; ri >= 0; ri--)
            {
                var r = recs[ri];
                if (r == null) continue;
                if (r.accId != dto.acc.accId) continue;

                var rcids = r.listOfCIDs;
                if (rcids == null || rcids.Count == 0) continue;

                var prevCid = rcids.FirstOrDefault(x => x?.InputData?.Id == (long)dto.cidId);
                if (prevCid == null) continue;

                prevRec = r;
                prevLastMut = prevCid.InputData?.LastMutationPnLPercents;
                break;
            }
        }

        logAction(
            $"[LastMutationPnL][PrevRecordByRecordId] acc={dto.acc.accId} cid={dto.cidId} " +
            $"prevRecDt={(prevRec == null ? "NULL" : prevRec.date.ToString("yyyy-MM-dd HH:mm:ss.fff"))} " +
            $"prevLastMut={(prevLastMut.HasValue ? prevLastMut.Value.ToString("0.######") : "NULL")}",
            LogLevel.llBaselogic
        );

        // ✅ Пишем новый LastMutationPnLPercents в InputData
        var add = CalcLastMutationPnLPercents(cid, record);

        // ✅ НОВАЯ ФОРМУЛА: (100 + old)*(100 + new)/100 - 100
        var oldv = prevLastMut ?? 0.0;
        var newv = (100.0 + oldv) * (100.0 + add) / 100.0 - 100.0;

        //cid.InputData.LastMutationPnLPercents = (prevLastMut ?? 0.0) + add;
        cid.InputData.LastMutationPnLPercents = newv;

        logAction(
            $"[LastMutationPnL][Result] acc={dto.acc.accId} cid={dto.cidId} prev={(prevLastMut ?? 0.0):0.######} add={add:0.######} new={cid.InputData.LastMutationPnLPercents:0.######}",
            LogLevel.llBaselogic
        );

        message =
            $"[AccMutationsProcessor]        [Deposit]    [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] Deposit of {dto.usdValue} USD was Successful.";
        logAction(message, LogLevel.llBaselogic);

        Publish(new SaveDepositRecordToDbDTO() { record = record });
    }


    private void CalcWithdrawal(AccMutationDTO dto)
    {
        logAction(
            $"[AccMutationsProcessor]        [Withdrawal] [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] UsdValue = -{dto.usdValue} USD.",
            LogLevel.llBaselogic
        );

        string message = "";

        var record = new Record()
        {
            accId = dto.acc.accId,
            cidId = dto.cidId,
            date = dto.dt,
            equityUSD = GetEquityUSD(dto),
            type = MutationType.Withdrawal
        };

        var cid = GetCidById(dto.acc, dto.cidId);
        if (cid == null)
        {
            message = $"[AccMutationsProcessor]        [Withdrawal][ERROR] GetCidById({dto.acc.accName}, {dto.cidId}) = null!";
            logAction(message, LogLevel.llBaselogic);
            Publish(new DepositErrorResponseDTO() { acc = dto.acc, cidId = dto.cidId, message = message });
            return;
        }

        record.listOfCIDs = dto.acc.listOfCIDs;

        // ✅ чтобы в БД/на UI не тащилось старое значение (в текущем состоянии аккаунта)
        ClearLastMutationPnLForAllCids(dto.acc);

        record.CopyClientShareFromPrevRecord();

        cid.InputData.DepositWithdrawalValueUSD = -dto.usdValue;

        record.CalcOutputs();

        // --------------------------------------------------------------------
        // ✅ Берём prevLastMut по "record_id" порядку:
        // listOfRecords у тебя загружается из БД как: order by dt_utc, record_id
        // => последний подходящий элемент = максимальный record_id
        // И НЕ фильтруем по record.cidId, потому что у Clearing cid_id = NULL.
        // --------------------------------------------------------------------
        double? prevLastMut = null;
        Record? prevRec = null;

        var recs = dto.acc.listOfRecords;
        if (recs != null && recs.Count > 0)
        {
            for (int ri = recs.Count - 1; ri >= 0; ri--)
            {
                var r = recs[ri];
                if (r == null) continue;
                if (r.accId != dto.acc.accId) continue;

                var rcids = r.listOfCIDs;
                if (rcids == null || rcids.Count == 0) continue;

                var prevCid = rcids.FirstOrDefault(x => x?.InputData?.Id == (long)dto.cidId);
                if (prevCid == null) continue;

                prevRec = r;
                prevLastMut = prevCid.InputData?.LastMutationPnLPercents;
                break; // ✅ это и есть "последняя" запись по record_id
            }
        }

        logAction(
            $"[LastMutationPnL][PrevRecordByRecordId] acc={dto.acc.accId} cid={dto.cidId} " +
            $"prevRecDt={(prevRec == null ? "NULL" : prevRec.date.ToString("yyyy-MM-dd HH:mm:ss.fff"))} " +
            $"prevLastMut={(prevLastMut.HasValue ? prevLastMut.Value.ToString("0.######") : "NULL")}",
            LogLevel.llBaselogic
        );

        // ✅ Записываем новый LastMutationPnLPercents в InputData (ты перенёс поле сюда)
        // ВАЖНО: после ClearLastMutationPnLForAllCids() тут почти всегда null, поэтому ветка else обычно не нужна.
        var add = CalcLastMutationPnLPercents(cid, record);

        // ✅ НОВАЯ ФОРМУЛА: (100 + old)*(100 + new)/100 - 100
        var oldv = prevLastMut ?? 0.0;
        var newv = (100.0 + oldv) * (100.0 + add) / 100.0 - 100.0;

        //cid.InputData.LastMutationPnLPercents = (prevLastMut ?? 0.0) + add;
        cid.InputData.LastMutationPnLPercents = newv;

        logAction(
            $"[LastMutationPnL][Result] acc={dto.acc.accId} cid={dto.cidId} prev={(prevLastMut ?? 0.0):0.######} add={add:0.######} new={cid.InputData.LastMutationPnLPercents:0.######}",
            LogLevel.llBaselogic
        );

        message =
            $"[AccMutationsProcessor]        [Withdrawal] [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] Withdrawal -{dto.usdValue} USD was Successful.";
        logAction(message, LogLevel.llBaselogic);

        Publish(new SaveDepositRecordToDbDTO() { record = record });
    }


    private void CalcClearing(AccMutationDTO dto)
    {
        logAction(
            $"[AccMutationsProcessor]        [Clearing]   [{dto.acc.accId}:{dto.acc.accName}]        EquityUSD = {dto.equityUsd} USD.",
            LogLevel.llBaselogic
        );

        if (dto.acc.listOfCIDs == null || dto.acc.listOfCIDs.Count == 0)
        {
            logAction(
                $"[AccMutationsProcessor][CalcClearing] acc.listOfCIDs is empty, nothing to clear.",
                LogLevel.llExceptions
            );
            return;
        }

        // ✅ Сохраняем last_mutation_pnl_percents из текущего состояния аккаунта ДО CalcOutputs()
        var lastMutByCidId = new Dictionary<long, double?>();
        foreach (var c in dto.acc.listOfCIDs)
        {
            if (c == null || c.InputData == null || c.OutputData == null) continue;
            lastMutByCidId[c.InputData.Id] = c.InputData.LastMutationPnLPercents;
        }

        var record = new Record()
        {
            accId = dto.acc.accId,
            cidId = -1,
            date = dto.dt,
            equityUSD = GetEquityUSD(dto),
            type = MutationType.Clearing,
            listOfCIDs = dto.acc.listOfCIDs
        };

        // ❌ УБРАТЬ: это затирает то, что мы как раз хотим протащить в clearing
        // ClearLastMutationPnLForAllCids(dto.acc);

        record.CopyClientShareFromPrevRecord();
        record.CalcOutputs();

        // ✅ Восстанавливаем last_mutation_pnl_percents после CalcOutputs()
        foreach (var cid in record.listOfCIDs)
        {
            if (cid == null || cid.InputData == null || cid.OutputData == null) continue;

            // Если CalcOutputs() перезаписал/обнулил — возвращаем предыдущее
            if (lastMutByCidId.TryGetValue(cid.InputData.Id, out var prev))
            {
                cid.InputData.LastMutationPnLPercents = prev;
            }
        }

        Publish(new SaveDepositRecordToDbDTO() { record = record });
    }


    public void OnNext(AccMutationDTO dto)
    {
        if (dto.mutationType == MutationType.CreateCid)
        {
            CreateCid(dto);
        }
        else if (dto.mutationType == MutationType.Deposit)
        {
            CalcDeposit(dto);
        }
        else if (dto.mutationType == MutationType.Withdrawal)
        {
            CalcWithdrawal(dto);
        }
        else if (dto.mutationType == MutationType.Clearing)
        {
            CalcClearing(dto);
        }
        else
        {
            logAction($"[AccMutationsProcessor][WARNING] Unexpected dto.mutationType = {dto.mutationType}. AccName = '{dto.acc.accName}', AccId = {dto.acc.accId}, CidId = {dto.cidId}, usdValue = {dto.usdValue} USD, EquityUSD = {dto.equityUsd} USD). dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.", LogLevel.llBaselogic);
        }
    }

    public void OnNext(AccountsDataCacheDTO dto)
    {
        accountsDataCacheDTO = dto;
    }

    public void OnCompleted()
    {
        throw new NotImplementedException();
    }

    public void OnError(Exception error)
    {
        throw new NotImplementedException();
    }

    private void Publish(DepositErrorResponseDTO dto)
    {
        foreach (var obs in depositErrorResponseObservers) obs.OnNext(dto);
    }

    private void Publish(SaveDepositRecordToDbDTO dto)
    {
        foreach (var obs in saveDepositReportDTOObservers) obs.OnNext(dto);
    }

    private void Publish(CreateCidDTO dto)
    {
        foreach (var obs in createCidDTOObservers) obs.OnNext(dto);
    }

    public IDisposable Subscribe(IObserver<DepositErrorResponseDTO> observer)
    {
        if (!depositErrorResponseObservers.Contains(observer))
            depositErrorResponseObservers.Add(observer);
        return new DepositErrorResponseDTOUnsubscriber(depositErrorResponseObservers, observer);
    }

    public IDisposable Subscribe(IObserver<SaveDepositRecordToDbDTO> observer)
    {
        if (!saveDepositReportDTOObservers.Contains(observer))
            saveDepositReportDTOObservers.Add(observer);
        return new SaveDepositReportDTOUnsubscriber(saveDepositReportDTOObservers, observer);
    }

    public IDisposable Subscribe(IObserver<CreateCidDTO> observer)
    {
        if (!createCidDTOObservers.Contains(observer))
            createCidDTOObservers.Add(observer);
        return new CreateCidDTOUnsubscriber(createCidDTOObservers, observer);
    }

    private class DepositErrorResponseDTOUnsubscriber : IDisposable
    {
        private readonly List<IObserver<DepositErrorResponseDTO>> _observers;
        private readonly IObserver<DepositErrorResponseDTO> _observer;

        public DepositErrorResponseDTOUnsubscriber(List<IObserver<DepositErrorResponseDTO>> observers, IObserver<DepositErrorResponseDTO> observer)
        { _observers = observers; _observer = observer; }

        public void Dispose()
        { if (_observer != null && _observers.Contains(_observer)) _observers.Remove(_observer); }
    }

    private class SaveDepositReportDTOUnsubscriber : IDisposable
    {
        private readonly List<IObserver<SaveDepositRecordToDbDTO>> _observers;
        private readonly IObserver<SaveDepositRecordToDbDTO> _observer;

        public SaveDepositReportDTOUnsubscriber(List<IObserver<SaveDepositRecordToDbDTO>> observers, IObserver<SaveDepositRecordToDbDTO> observer)
        { _observers = observers; _observer = observer; }

        public void Dispose()
        { if (_observer != null && _observers.Contains(_observer)) _observers.Remove(_observer); }
    }

    private class CreateCidDTOUnsubscriber : IDisposable
    {
        private readonly List<IObserver<CreateCidDTO>> _observers;
        private readonly IObserver<CreateCidDTO> _observer;

        public CreateCidDTOUnsubscriber(List<IObserver<CreateCidDTO>> observers, IObserver<CreateCidDTO> observer)
        { _observers = observers; _observer = observer; }

        public void Dispose()
        { if (_observer != null && _observers.Contains(_observer)) _observers.Remove(_observer); }
    }
}
