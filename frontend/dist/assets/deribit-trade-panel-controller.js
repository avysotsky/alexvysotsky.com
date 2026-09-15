(function(){
  if(window.DeribitTradePanelController) return;

  const ids = ['ccSpotInst','ccSpotBar','ccFuturesInst','ccFuturesBar'];
  const on = (el, type, fn, opts) => el && el.addEventListener(type, fn, opts);

  window.DeribitTradePanelController = {
    bindSettingsAndTicket(){
      on(cc('ccTradeSettingsBtn'), 'click', ccOpenTradeSettings);
      on(cc('ccFuturesTradeSettingsBtn'), 'click', ccOpenTradeSettings);
      on(cc('ccTradeSettingsClose'), 'click', ccCloseTradeSettings);
      on(cc('ccTradeSettingsModal'), 'click', e => { if(e.target?.id === 'ccTradeSettingsModal') ccCloseTradeSettings(); });
      on(cc('ccSettingOrderConfirmation'), 'click', () => ccToggleTradeSetting('orderConfirmation'));
      on(cc('ccSettingFilledNotification'), 'click', () => ccToggleTradeSetting('filledNotification'));
      ccSyncTradeSettingsUi();

      on(cc('ccTradeMarketInst'), 'change', () => ccTradeProxySelect('inst'));
      on(cc('ccTradeMarketBar'), 'change', () => ccTradeProxySelect('bar'));
      on(cc('ccSpotOrdType'), 'change', ccUpdateSpotTradeUi);
      on(cc('ccSpotPostOnlyBtn'), 'click', () => { CC_TRADE_STATE.spotPostOnly = !CC_TRADE_STATE.spotPostOnly; ccUpdateSpotTradeUi(); });
      on(cc('ccSpotAmountMin'), 'click', ccSetSpotAmountMin);
      on(cc('ccSpotAmountMax'), 'click', ccSetSpotAmountMax);
      on(cc('ccFuturesOrdType'), 'change', ccRenderFuturesOrderTypeUi);
      on(cc('ccFuturesPxUp'), 'click', () => ccStepFuturesPx(1));
      on(cc('ccFuturesPxDown'), 'click', () => ccStepFuturesPx(-1));
      on(cc('ccFuturesAmountUnit'), 'change', ccRenderFuturesAmountUi);
      on(cc('ccFuturesAdjustLeverageBtn'), 'click', ccOpenLeverageModal);
      on(cc('ccLeverageModalClose'), 'click', ccCloseLeverageModal);
      on(cc('ccLeverageModal'), 'click', e => { if(e.target?.id === 'ccLeverageModal') ccCloseLeverageModal(); });
      on(cc('ccLeverageSlider'), 'input', ccUpdateLeverageModal);
      on(cc('ccLeverageMinus'), 'click', () => {
        const slider = cc('ccLeverageSlider');
        if(!slider) return;
        slider.value = String(Math.max(Number(slider.min || 1), Number(slider.value || 1) - 1));
        ccUpdateLeverageModal();
      });
      on(cc('ccLeveragePlus'), 'click', () => {
        const slider = cc('ccLeverageSlider');
        if(!slider) return;
        slider.value = String(Math.min(Number(slider.max || 125), Number(slider.value || 1) + 1));
        ccUpdateLeverageModal();
      });
      on(cc('ccLeverageConfirm'), 'click', async () => {
        const slider = cc('ccLeverageSlider');
        const btn = cc('ccLeverageConfirm');
        if(!slider || !btn) return;
        btn.disabled = true;
        const ok = await ccSetFuturesLeverageDirect(slider.value);
        btn.disabled = false;
        if(ok) ccCloseLeverageModal();
      });
      document.addEventListener('keydown', e => {
        if(e.key === 'Escape'){
          ccCloseTradeSettings();
          ccCloseLeverageModal();
        }
      });
    },

    bindMarketSelection(){
      ids.forEach(id => {
        const el = cc(id);
        on(el, 'change', () => {
          if(id === 'ccSpotInst'){
            ccUiStateSave({ spotSymbol: cc('ccSpotInst')?.value || '' });
            ccUpdateSpotTradeUi();
            loadCcSpotTradeHistory();
          }
          if(id === 'ccFuturesInst'){
            ccFuturesApplySavedSymbolState(cc('ccFuturesInst')?.value || '');
            el.dataset.ccPrevValue = cc('ccFuturesInst')?.value || '';
            ccRenderFuturesAmountUi();
            ccRenderFuturesContractStrip();
            loadCcFuturesLeverage();
            loadCcFuturesOpenOrders();
            ccRefreshActiveFuturesPanel();
          }
          ccTradeSyncTopControls();
        });
        on(el, 'change', ccTradeSyncTopControls);
      });
      on(cc('ccFuturesLeverageSelect'), 'change', setCcFuturesLeverage);
    },

    bindGlobalClicks(){
      document.addEventListener('click', e => {
        const a = e.target.closest('[data-cc-action]');
        if(a){
          const act = a.dataset.ccAction;
          if(act === 'loadInstruments') return;
        }
        const futuresCancel = e.target.closest('[data-cc-futures-cancel-index]');
        if(futuresCancel){
          e.preventDefault();
          cancelCcFuturesOrder(futuresCancel.dataset.ccFuturesCancelIndex);
          return;
        }
        const cancel = e.target.closest('[data-cc-spot-cancel-index]');
        if(cancel){
          cancelCcSpotOrder(cancel.dataset.ccSpotCancelIndex);
          return;
        }
        const o = e.target.closest('[data-cc-order]');
        if(o) placeOrder(o.dataset.ccOrder, o.dataset.side);
      });
    },

    initialUi(){
      ccUpdateSpotTradeUi();
      ccRenderFuturesAmountUi();
      ccTradeSyncTopControls();
      ccPlaceContractToolbar();
      ccSyncFuturesMarketChips();
      ccTradeSyncTopControls();
    }
  };
})();
