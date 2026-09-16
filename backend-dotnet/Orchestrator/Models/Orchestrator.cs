using static CoreDTO.Logger.Enums;
using CoreDTO.Configs;
using CoreDTO.NDHDTO;
using CoreDTO.Threads;
using static CoreDTO.NDHDTO.Enums;
using System.Text.Json;

namespace NDHWebService.Models;

public class Orchestrator : ThreadAdapter<AccMutationDTO>,
                            IObserver<AccMutationDTO>,
                            IObserver<SaveDepositRecordToDbDTO>,
                            IObserver<DepositErrorResponseDTO>
{
    public AccountsDataCache accountsDataCache;
    public AccMutationsPocessor accMutationsProcessor;
    public LogAction logAction;

    private static readonly object locker = new();

    private readonly List<AccMutationDTO> listOfMutations = new();

    private readonly IAccountsRepository _accountsRepo;
    private readonly IRecordsRepository _recordsRepo;

    // ===== In-memory кэш аккаунтов вместо DBEmulator =====
    private readonly List<Account> _accountsCache = new();

    public MutationStatus mutationStatus = MutationStatus.Idle;
    public MutationState mutationState = MutationState.Idle;
    public bool mutationResultStatus = false;
    public double equityUSD = 0;
    public string mutationResultText = "";
    private int mutationProcessingAccId = -1;
    private int mutationProcessingCidId = -1;
    private DateTime _currentMutationDtUtc = DateTime.MinValue;


    public Orchestrator(OrchestratorConfig config,
                        IAccountsRepository accountsRepo,
                        IRecordsRepository recordsRepo,
                        LogAction logAction)
    {
        _accountsRepo = accountsRepo;
        _recordsRepo = recordsRepo;
        this.logAction = logAction;

        Start();
        Init(config);
    }

    public void Init(OrchestratorConfig config)
    {
        accountsDataCache = new AccountsDataCache(logAction);

        // 1) Загружаем аккаунты из БД
        var accounts = _accountsRepo.GetAccountsAsync().GetAwaiter().GetResult();
        _accountsCache.Clear();
        if (accounts != null)
            _accountsCache.AddRange(accounts);

        // 2) Для каждого аккаунта подтягиваем записи и восстанавливаем CIDs
        foreach (var acc in _accountsCache)
        {
            try
            {
                var records = _recordsRepo
                    .GetAccountRecordsAsync(acc.accId)
                    .GetAwaiter()
                    .GetResult();

                acc.listOfRecords = records ?? new List<Record>();

                if (acc.listOfRecords.Count > 0)
                {
                    var last = acc.listOfRecords[^1];              // последняя запись
                    acc.listOfCIDs = last.listOfCIDs ?? new List<CID>();
                }
                else
                {
                    acc.listOfCIDs ??= new List<CID>();
                }

                logAction(
                    $"[Orchestrator.Init] accId={acc.accId} records={acc.listOfRecords.Count}, cids={acc.listOfCIDs.Count}",
                    LogLevel.llBaselogic);
            }
            catch (Exception ex)
            {
                logAction(
                    $"[Orchestrator.Init][ERROR] can't load records for accId={acc.accId}: {ex}",
                    LogLevel.llExceptions);

                acc.listOfRecords = new List<Record>();
                acc.listOfCIDs ??= new List<CID>();
            }
        }

        // 3) Подключаемся к AccMutationsPocessor
        accMutationsProcessor = new AccMutationsPocessor(logAction);
        accMutationsProcessor.Subscribe((IObserver<SaveDepositRecordToDbDTO>)this);
        accMutationsProcessor.Subscribe((IObserver<DepositErrorResponseDTO>)this);
    }



    // ===== Публичные методы кэша аккаунтов (замена DBEmulator.*) =====

    public List<Account> GetAccounts() => _accountsCache.ToList();

    public Account? GetAccountById(int accId) =>
        _accountsCache.FirstOrDefault(a => a.accId == accId);


    public bool UpdateAccountClearingSchedule(
        int accId,
        bool clearingEnabled,
        string clearingPeriod,
        string clearingTimeUtc,
        int? clearingDayOfWeek,
        int? clearingDayOfMonth,
        DateTime? nearestClearingTimestampUtc)
    {
        lock (locker)
        {
            var acc = _accountsCache.FirstOrDefault(a => a.accId == accId);
            if (acc == null) return false;

            acc.clearingEnabled = clearingEnabled;
            acc.clearingPeriod = clearingPeriod;
            acc.clearingTimeUtc = clearingTimeUtc;
            acc.clearingDayOfWeek = clearingDayOfWeek;
            acc.clearingDayOfMonth = clearingDayOfMonth;
            acc.nearestClearingTimestampUtc = nearestClearingTimestampUtc;
            return true;
        }
    }

    public void AddAccount(Account acc)
    {
        if (acc == null) return;
        _accountsCache.Add(acc);
    }

    public void RemoveAccount(int accId)
    {
        var acc = GetAccountById(accId);
        if (acc != null)
            _accountsCache.Remove(acc);
    }

    // ===== Основной цикл обработки мутаций =====

    protected override async void ProcessNextItem(AccMutationDTO dto)
    {
        mutationState = MutationState.Idle;
        string message = "";

        if (dto.acc == null)
        {
            message = $"[Orchestrator][ProcessNextItem][ERROR]  dto.acc = null! Request is ignored.";
            logAction(message, LogLevel.llBaselogic);
            logAction("", LogLevel.llBaselogic);
            mutationStatus = MutationStatus.Idle;
            mutationResultText = message;
            return;
        }

        mutationStatus = MutationStatus.Idle;
        mutationResultStatus = false;
        mutationResultText = "";
        mutationProcessingAccId = dto.acc.accId;
        mutationProcessingCidId = dto.cidId;
        _currentMutationDtUtc = dto.dt;

        listOfMutations.Add(dto);

        if (dto.mutationType == MutationType.CreateCid)
        {
            logAction(
                $"[Orchestrator][ProcessNextItem][CreateCid]  [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] " +
                $"CidName = {dto.cidName}, feeRatePercents = {dto.feeRatePercents}. dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.",
                LogLevel.llBaselogic);
        }
        else if (dto.mutationType == MutationType.Deposit)
        {
            logAction(
                $"[Orchestrator][ProcessNextItem][Deposit]    [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] " +
                $"usdValue = {dto.usdValue} USD. dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.",
                LogLevel.llBaselogic);
        }
        else if (dto.mutationType == MutationType.Withdrawal)
        {
            logAction(
                $"[Orchestrator][ProcessNextItem][Withdrawal] [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] " +
                $"usdValue = {dto.usdValue} USD. dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.",
                LogLevel.llBaselogic);
        }
        else if (dto.mutationType == MutationType.Clearing)
        {
            logAction(
                $"[Orchestrator][ProcessNextItem][Clearing]   [{dto.acc.accId}:{dto.acc.accName}] " +
                $"EquityUSD = {dto.equityUsd} USD. dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.",
                LogLevel.llBaselogic);
        }
        else
        {
            logAction(
                $"[Orchestrator][ProcessNextItem][WARNING]    [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] " +
                $"Unexpected dto.mutationType = {dto.mutationType}. usdValue = {dto.usdValue} USD, " +
                $"EquityUSD = {dto.equityUsd} USD. dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.",
                LogLevel.llBaselogic);
        }

        accMutationsProcessor.OnNext(dto);

        while (mutationStatus != MutationStatus.Success)
        {
            if (mutationStatus == MutationStatus.Idle)
            {
                await Task.Delay(100);
                continue;
            }
            else if (mutationStatus == MutationStatus.Fail)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][Fail]        " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] Deposit record was NOT saved to Db! FAIL!";
            }
            else if (mutationStatus == MutationStatus.WrongAccId)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][WrongAccId]  " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] WrongAccId!";
            }
            else if (mutationStatus == MutationStatus.WrongCidId)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][WrongCidId]  " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] WrongCidId!";
            }
            else
            {
                message =
                    $"[Orchestrator][ProcessNextItem][ERROR] [{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] " +
                    $"Unexpected mutationStatus = {mutationStatus}!";
            }

            if (mutationStatus != MutationStatus.Success)
            {
                logAction(message, LogLevel.llBaselogic);
                logAction("", LogLevel.llBaselogic);
                break;
            }
        }

        if (mutationStatus == MutationStatus.Success)
        {
            mutationResultStatus = true;

            if (dto.mutationType == MutationType.CreateCid)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][CreateCid]  " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] [Success] '{dto.cidName}' " +
                    $"with feeRatePercents = {dto.feeRatePercents}% is created.";
            }
            else if (dto.mutationType == MutationType.Deposit)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][Deposit]    " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] Deposit of {dto.usdValue} USD was Successful.";
            }
            else if (dto.mutationType == MutationType.Withdrawal)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][Withdrawal] " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] [Success] UsdValue = -{dto.usdValue} USD. " +
                    $"dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.";
            }
            else if (dto.mutationType == MutationType.Clearing)
            {
                message =
                    $"[Orchestrator][ProcessNextItem][Clearing]   " +
                    $"[{dto.acc.accId}:{dto.acc.accName}] [Success] Clearing is completed Successfully.";
            }
            else
            {
                message =
                    $"[Orchestrator][ProcessNextItem][WARNING]    " +
                    $"[{dto.acc.accId}:{dto.acc.accName}::CID_{dto.cidId}] Unexpected dto.mutationType = {dto.mutationType}. " +
                    $"usdValue = {dto.usdValue} USD, EquityUSD = {dto.equityUsd} USD. dt = {dto.dt:yyyy-MM-dd HH:mm:ss.fff}.";
            }

            mutationResultText = message;
            logAction(message, LogLevel.llBaselogic);
        }

        mutationStatus = MutationStatus.Idle;
        mutationState = MutationState.Completed;
    }

    // ===== IObserver реализация =====

    public void OnNext(AccMutationDTO mutationDTO) => AddItem(mutationDTO);


    // Orchestrator получает финальный Record и пишет его в Postgres + обновляет кэш

    public async void OnNext(SaveDepositRecordToDbDTO dto)
    {
        var rec = dto.record;
        if (rec == null)
        {
            logAction("[Orchestrator][OnNext(SaveDepositRecordToDbDTO)][ERROR] dto.record = null", LogLevel.llExceptions);
            mutationStatus = MutationStatus.Fail;
            return;
        }

        // Пробрасываем дату из последней мутации (AccMutationDTO.dt) в Record.date
        if (_currentMutationDtUtc != DateTime.MinValue)
        {
            rec.date = _currentMutationDtUtc;
        }

        string message;

        if (mutationProcessingAccId != rec.accId)
        {
            message =
                $"[Orchestrator][SaveDepositRecordDTO][ERROR] rec.accId = {rec.accId} " +
                $"!= mutationProcessingAccId = {mutationProcessingAccId}!";
            logAction(message, LogLevel.llBaselogic);
            mutationStatus = MutationStatus.WrongAccId;
            return;
        }

        // Для Clearing по всему аккаунту rec.cidId может быть -1 – тогда не проверяем
        if (rec.cidId >= 0 && mutationProcessingCidId != rec.cidId)
        {
            message =
                $"[Orchestrator][SaveDepositRecordDTO][ERROR] rec.cidId = {rec.cidId} " +
                $"!= mutationProcessingCidId = {mutationProcessingCidId}!";
            logAction(message, LogLevel.llBaselogic);
            mutationStatus = MutationStatus.WrongCidId;
            return;
        }

        try
        {
            // Пересчёт общей суммы инвестиций и equity по записи
            rec.newTotallyInvestedUSD = 0.0;

            // На всякий случай защищаемся от null
            rec.listOfCIDs ??= new List<CID>();

            foreach (var cid in rec.listOfCIDs)
            {
                // Здесь OutputData.NewInvestmentValueUSD уже должен быть рассчитан
                rec.newTotallyInvestedUSD += cid.OutputData.NewInvestmentValueUSD;
            }

            rec.equityUSD = rec.newTotallyInvestedUSD;

            // Сохраняем запись в БД
            await _recordsRepo.SaveRecordAsync(rec);

            // Обновляем in-memory кэш, чтобы фронт видел и Records, и CIDs
            var acc = GetAccountById(rec.accId);
            if (acc != null)
            {
                acc.listOfRecords ??= new List<Record>();
                acc.listOfRecords.Add(rec);

                // Обновляем список CIDs аккаунта последним состоянием из записи
                acc.listOfCIDs = rec.listOfCIDs ?? new List<CID>();
            }

            equityUSD = rec.equityUSD;

            //-------------------------------------------------------------------
            var rec2 = DeepClone(dto.record);
            rec2.equityUSD = 0;
            rec2.type = MutationType.CopyOutToInDataValues;

            for (int i = 0; i < rec2.listOfCIDs.Count; i++)
            {
                var input = rec2.listOfCIDs[i].InputData;
                var output = rec2.listOfCIDs[i].OutputData;

                rec2.equityUSD += output.NewInvestmentValueUSD;

                if (input.DepositWithdrawalValueUSD != 0)
                {
                    //rec2.equityUSD += input.DepositWithdrawalValueUSD;
                    input.InvestmentUSD += input.DepositWithdrawalValueUSD;
                    input.DepositWithdrawalValueUSD = 0;

                }
                input.ClientSharePercents = output.NewClientSharePercents;
                //input.ReinvestedUSD += output.ProfitUSD;
                input.ReinvestedUSD = output.NewInvestmentValueUSD - input.InvestmentUSD;
            }

            for (int i = 0; i < rec2.listOfCIDs.Count; i++)
            {
                var input = rec2.listOfCIDs[i].InputData;
                var output = rec2.listOfCIDs[i].OutputData;
                var share = (double)output.NewClientSharePercents / 100.0;
                output.ClientEquityUSD = Math.Round(rec2.equityUSD * share, 2);
            }

            // Сохраняем запись в БД
            await _recordsRepo.SaveRecordAsync(rec2);

            logAction($"[DBEmulator][ProcessNextItem][CopyOutToInDataValues][{dto.record.accId}::CID_{dto.record.cidId}] Copy Output CID data to Input CID data.", LogLevel.llBaselogic);

            if (acc != null)
            {
                acc.listOfRecords ??= new List<Record>();
                acc.listOfRecords.Add(rec2);
                // Обновляем список CIDs аккаунта последним состоянием из записи
                acc.listOfCIDs = rec2.listOfCIDs ?? new List<CID>();
            }
            //-----------------------------------------------------------------------


            mutationStatus = MutationStatus.Success;

            message =
                $"[Orchestrator][SaveDepositRecordDTO] Record saved to DB. " +
                $"accId={rec.accId}, cidId={rec.cidId}, equityUSD={rec.equityUSD}.";
            logAction(message, LogLevel.llBaselogic);
        }
        catch (Exception ex)
        {
            mutationStatus = MutationStatus.Fail;
            logAction($"[Orchestrator][SaveDepositRecordDTO][EXCEPTION] {ex}", LogLevel.llExceptions);
        }
    }


    private static T DeepClone<T>(T obj) =>
JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj))!;

    public void OnNext(DepositErrorResponseDTO dto)
    {
        var msg =
            $"[Orchestrator][DepositError] AccName = '{dto.acc?.accName}', " +
            $"AccId = {dto.acc?.accId}, CidId = {dto.cidId}, message = '{dto.message}'.";
        logAction(msg, LogLevel.llExceptions);
        mutationStatus = MutationStatus.Fail;
    }

    public void OnCompleted()
    {
        // not used
    }

    public void OnError(Exception error)
    {
        logAction($"[Orchestrator][OnError] {error}", LogLevel.llExceptions);
    }

    protected override void Open() { }

    protected override void Close() { }
}
