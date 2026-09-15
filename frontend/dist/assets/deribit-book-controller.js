(function(){
  if(window.DeribitBookController) return;

  const ids = ['ccSpotInst','ccSpotBar','ccFuturesInst','ccFuturesBar'];
  const on = (el, type, fn, opts) => el && el.addEventListener(type, fn, opts);

  function marketFromControlId(id){
    return id.indexOf('Futures') >= 0 ? 'futures' : 'spot';
  }

  window.DeribitBookController = {
    bindPrecision(){
      on(cc('ccFuturesBookPrecision'), 'change', () => {
        const value = cc('ccFuturesBookPrecision')?.value || '';
        ccFuturesSaveBookSegPref(ccFuturesBookSegBase(), value);
        if(ccActiveMarket() === 'futures') loadCcBook('futures');
      });
    },

    bindMarketSelection(){
      ids.forEach(id => {
        const el = cc(id);
        on(el, 'change', () => {
          const market = marketFromControlId(id);
          if(id === 'ccFuturesInst'){
            CC_BOOK_STATE.requestSeq.futures = (CC_BOOK_STATE.requestSeq.futures || 0) + 1;
            ccClearBook('futures', '...');
          }
          if(ccActiveMarket() === market) ccStartBookRefresh(market);
          else loadCcBook(market);
        });
      });
      on(window, 'deribit:reload-instruments', event => {
        const market = event?.detail?.market || 'spot';
        if(ccActiveMarket() === market) ccStartBookRefresh(market);
        else loadCcBook(market);
      });
    },

    bindPriceClickToTicket(){
      document.addEventListener('click', e => {
        const price = e.target.closest('[data-cc-book-price]');
        if(!price) return;
        const market = price.closest('#ccFuturesAsks,#ccFuturesBids') ? 'futures' : 'spot';
        const input = cc(market === 'futures' ? 'ccFuturesPx' : 'ccSpotPx');
        if(input){
          input.value = price.dataset.ccBookPrice || price.textContent.trim();
          input.dispatchEvent(new Event('input', { bubbles: true }));
          input.focus();
        }
      });
    },

    bindVisibilityRefresh(){
      document.addEventListener('visibilitychange', () => {
        const market = CC_BOOK_STATE.active;
        if(document.hidden || !market) return;
        if(market === 'futures'){
          loadCcBook('futures');
          ccScheduleChartRedraw('ccFuturesChart', 0);
          return;
        }
        if(CC_BOOK_STATE.wantReconnect[market] && !ccBookSocketOpen(market)) ccEnsureBookSocket(market, true);
      });
    },

    initialLoad(){
      ccStartBookRefresh(ccActiveMarket());
    }
  };
})();
