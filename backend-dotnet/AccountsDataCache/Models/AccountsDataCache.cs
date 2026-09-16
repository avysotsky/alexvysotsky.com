using CoreDTO.NDHDTO;
using static CoreDTO.Logger.Enums;

namespace NDHWebService.Models;

public class AccountsDataCache : IObserver<AccountInfoDTO>,
                                 IObservable<AccountsDataCacheDTO>
{
    private readonly List<IObserver<AccountsDataCacheDTO>> accountsDataCacheDTOObservers = new();
    protected Thread thread;
    protected bool is_running = false;
    AccountsDataCacheDTO accountsDataCacheDTO;
    LogAction logAction;

    public AccountsDataCache(LogAction logAction)
    {
        accountsDataCacheDTO = new AccountsDataCacheDTO();
        this.logAction = logAction;
        thread = new Thread(MainLoop)
        {
            Priority = ThreadPriority.Lowest
        };
    }

    private void MainLoop()
    {
        while (is_running)
        {
            Publish(accountsDataCacheDTO);
            Thread.Sleep(1000);
        }
    }

    public void OnNext(AccountInfoDTO dto)
    {
        try
        {
            if (dto.accId == null)
            {
                logAction("[AccountsDataCache][SetEquityUSDValue][Exception] dto.AccId = null! Ignore this dto.", LogLevel.llExceptions);
                return;
            }
            if (dto.equityUSD == null)
            {
                logAction("[AccountsDataCache][SetEquityUSDValue][Exception] dto.EquityUSD = null! Ignore this dto.", LogLevel.llExceptions);
                return;
            }
            accountsDataCacheDTO.equityUSD[(int)dto.accId] = (double)dto.equityUSD;
        }
        catch (Exception ex)
        {
            logAction($"[AccountsDataCache][Exception] {ex}", LogLevel.llExceptions);
        }
    }

    public void Start()
    {
        is_running = true;
        thread.Start();
    }

    public void Stop()
    {
        is_running = false;
    }

    private void Publish(AccountsDataCacheDTO dto)
    {
        foreach (var obs in accountsDataCacheDTOObservers) obs.OnNext(dto);
    }

    public IDisposable Subscribe(IObserver<AccountsDataCacheDTO> observer)
    {
        if (!accountsDataCacheDTOObservers.Contains(observer))
            accountsDataCacheDTOObservers.Add(observer);
        return new AccountsDataCacheDTOUnsubscriber(accountsDataCacheDTOObservers, observer);
    }

    private class AccountsDataCacheDTOUnsubscriber : IDisposable
    {
        private readonly List<IObserver<AccountsDataCacheDTO>> _observers;
        private readonly IObserver<AccountsDataCacheDTO> _observer;

        public AccountsDataCacheDTOUnsubscriber(List<IObserver<AccountsDataCacheDTO>> observers, IObserver<AccountsDataCacheDTO> observer)
        {
            _observers = observers;
            _observer = observer;
        }

        public void Dispose()
        {
            if (_observers.Contains(_observer)) _observers.Remove(_observer);
        }
    }

    public void OnCompleted()
    {
        throw new NotImplementedException();
    }

    public void OnError(Exception error)
    {
        throw new NotImplementedException();
    }
}
