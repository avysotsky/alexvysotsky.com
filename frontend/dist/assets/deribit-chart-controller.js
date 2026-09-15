(function(){
  if(window.DeribitChartController) return;

  const ids = ['ccSpotInst','ccSpotBar','ccFuturesInst','ccFuturesBar'];
  const on = (el, type, fn, opts) => el && el.addEventListener(type, fn, opts);

  function marketFromControlId(id){
    return id.indexOf('Futures') >= 0 ? 'futures' : 'spot';
  }

  function persistCurrentSelection(id){
    if(id === 'ccSpotInst' || id === 'ccSpotBar'){
      ccSpotViewportPersistActive();
      ccUiStateSave({
        spotSymbol: String(cc('ccSpotInst')?.value || '').trim().toUpperCase(),
        spotTimeframe: cc('ccSpotBar')?.value || ''
      });
      return;
    }
    ccFuturesViewportPersistActive();
    const sym = String(cc('ccFuturesInst')?.value || '').trim();
    const tf = cc('ccFuturesBar')?.value || '';
    if(sym) ccUiStateSave({ futuresSymbol: sym, futuresTimeframe: tf });
    else ccUiStateSave({ futuresTimeframe: tf });
  }

  function onMarketControlChange(id, el){
    const market = marketFromControlId(id);
    const dataPanelActive = !!cc('ccMarketDataPanel')?.classList.contains('active');
    const dataMarket = ccMarketDataActiveTab();
    const dataTimeframeBefore = dataPanelActive && dataMarket === market
      ? String(cc('ccMarketDataBar')?.value || ccMarketDataSavedValue(market, 'timeframe') || '').trim()
      : '';

    if(id === 'ccSpotInst' && dataPanelActive && dataMarket === 'spot') ccMarketDataViewportPersistActive();
    if(id === 'ccFuturesInst'){
      const prevSymbol = String(el.dataset.ccPrevValue || CC_CHART_STATE.ccFuturesChart?.chartKey?.split(':')[1] || '').trim();
      if(prevSymbol){
        ccFuturesViewportPersistActive();
        ccFuturesSymbolStateSave(prevSymbol, { timeframe: cc('ccFuturesBar')?.value || '' });
      }
      if(dataPanelActive && dataMarket === 'futures') ccMarketDataViewportPersistActive();
    }
    if(id === 'ccFuturesBar'){
      ccFuturesViewportPersistActive();
      ccFuturesSymbolStateSave(cc('ccFuturesInst')?.value || '', { timeframe: cc('ccFuturesBar')?.value || '' });
      if(dataPanelActive && dataMarket === 'futures') ccMarketDataViewportPersistActive();
    }
    if(id === 'ccSpotBar' && dataPanelActive && dataMarket === 'spot') ccMarketDataViewportPersistActive();

    if(ccMarketDataActiveTab() === market){
      ccSyncMarketDataSymbolOptions(market, ccMarketDataSourceSelect(market, 'symbol')?.value || '');
      ccSyncMarketDataTimeframeOptions(
        market,
        dataTimeframeBefore || ccMarketDataSavedValue(market, 'timeframe') || ccMarketDataSourceSelect(market, 'timeframe')?.value || ''
      );
      ccSaveMarketDataSelection(market);
      if(dataPanelActive) loadCcMarketDataHistory();
    }

    if(market === 'futures' && typeof ccFuturesChartPrimeLoading === 'function') ccFuturesChartPrimeLoading();
    if(ccActiveMarket() === market) loadCcChart(market);
    else loadCcChart(market);
  }

  window.DeribitChartController = {
    bindTradeChartMode(){
      ccTradeSetChartMode(ccUiStateLoad().tradeChartMode || 'candles', false);
      document.querySelectorAll('[data-cc-trade-chart-mode]').forEach(btn => {
        on(btn, 'click', () => ccTradeSetChartMode(btn.dataset.ccTradeChartMode || 'candles'));
      });
    },

    bindViewportPersistence(){
      ccSpotViewportBindPersistence();
      ccSpotViewportCleanCorrupt();
      ccFuturesViewportBindPersistence();
      ccFuturesViewportCleanCorrupt();

      ['ccSpotInst','ccSpotBar'].forEach(id => on(cc(id), 'change', () => persistCurrentSelection(id), true));
      ['ccFuturesInst','ccFuturesBar'].forEach(id => on(cc(id), 'change', () => persistCurrentSelection(id), true));
      document.querySelectorAll('[data-cc-trade],[data-cc-layer]').forEach(el => {
        on(el, 'click', () => {
          ccSpotViewportPersistActive();
          ccFuturesViewportPersistActive();
          if(typeof ccMarketDataViewportPersistActive === 'function') ccMarketDataViewportPersistActive();
        }, true);
      });
      on(window, 'beforeunload', () => {
        ccSpotViewportPersistActive();
        ccFuturesViewportPersistActive();
        if(typeof ccMarketDataViewportPersistActive === 'function') ccMarketDataViewportPersistActive();
      });
      setInterval(() => { if(ccActiveMarket && ccActiveMarket() === 'spot') ccSpotViewportPersistSoon(); }, 1000);
    },

    bindMarketSelection(){
      ids.forEach(id => {
        const el = cc(id);
        on(el, 'change', () => onMarketControlChange(id, el));
      });
      on(window, 'deribit:reload-instruments', event => {
        const market = event?.detail?.market || 'spot';
        if(market === 'futures' && typeof ccFuturesChartPrimeLoading === 'function') ccFuturesChartPrimeLoading();
        loadCcChart(market);
      });
      on(cc('ccSpotBar'), 'change', () => ccUiStateSave({ spotTimeframe: cc('ccSpotBar')?.value || '' }));
      on(cc('ccFuturesBar'), 'change', () => {
        const value = cc('ccFuturesBar')?.value || '';
        ccUiStateSave({ futuresTimeframe: value });
        ccFuturesSymbolStateSave(cc('ccFuturesInst')?.value || '', { timeframe: value });
      });
    },

    bindResize(){
      on(window, 'resize', () => {
        clearTimeout(window.__ccResize);
        window.__ccResize = setTimeout(loadCcCharts, 150);
      });
    },

    bindMainViewportWheel(){
      ccBindViewportWheelToMain();
    },

    bindVisibilityObservers(){
      ccBindChartVisibilityObservers('ccSpotChart', 'ccSpotShell');
      ccBindChartVisibilityObservers('ccFuturesChart', 'ccFuturesShell');
    },

    initialLoad(){
      if(typeof ccFuturesChartPrimeLoading === 'function') ccFuturesChartPrimeLoading();
      loadCcChart('spot');
      loadCcChart('futures');
      if(ccActiveMarket() === 'futures'){
        ccScheduleChartRedraw('ccFuturesChart', 0);
        ccScheduleChartRedraw('ccFuturesChart', 120);
      }
    },

    startPeriodicRefresh(){
      setInterval(loadCcCharts, 60000);
      ccChartUpdateUtcLabels();
      setInterval(ccChartUpdateUtcLabels, 1000);
    }
  };
})();
