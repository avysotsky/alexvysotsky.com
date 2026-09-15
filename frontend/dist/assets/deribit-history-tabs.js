(function(){
  function h(name){
    return window[name];
  }
  function sideStyle(sideRaw){
    return sideRaw === 'sell' ? ' style="color:#F04E60"' : (sideRaw === 'buy' ? ' style="color:#45C4A5"' : '');
  }
  function orderHistoryRow(o){
    const ccEsc = h('ccEsc'), ccPick = h('ccPick'), ccFmtSignedNumberOrDash = h('ccFmtSignedNumberOrDash');
    const sideRaw = h('ccSpotOrderSideRaw')(o);
    const style = sideStyle(sideRaw);
    return `<tr><td>${h('ccFuturesOrderTimeCell')(o)}</td><td>${ccEsc(h('ccFuturesOrderSymbolText')(o))}</td><td><span${style}>${ccEsc(h('ccOrderSide')(o))}</span></td><td>${h('ccFuturesOrderAvgPriceCell')(o)}</td><td>${h('ccFuturesOrderFilledAmountCell')(o)}</td><td>${ccEsc(h('ccOrderStatusLabel')(o))}</td><td>${ccEsc(h('ccFuturesOrderDisplayId')(o))}</td><td>${ccEsc(h('ccOrderFees')(o))}</td><td>${ccEsc(ccFmtSignedNumberOrDash(ccPick(o,['rpnl','realizedPnl','realizedPNL']),2))}</td><td>${ccEsc(h('ccFuturesOrderTypeLabel')(o))}</td><td>${ccEsc(h('ccFmtBoolText')(ccPick(o,['reduceOnly','isReduceOnly'])))}</td><td>${ccEsc(h('ccFuturesOrderTriggerConditionsText')(o))}</td><td>${ccEsc(h('ccFuturesOrderTifText')(o))}</td></tr>`;
  }
  function tradeHistoryRow(t){
    const ccEsc = h('ccEsc'), ccPick = h('ccPick'), ccFmtSignedNumberOrDash = h('ccFmtSignedNumberOrDash');
    const sideRaw = h('ccSpotOrderSideRaw')(t);
    const style = sideStyle(sideRaw);
    const orderId = ccPick(t,['orderId','ordId','clientOrderId']);
    const tradeId = ccPick(t,['tradeId','dealId','fillId','id']);
    return `<tr><td>${ccEsc(h('ccFormatOrderTime')(ccPick(t,['time','ts','createTime','createdTime','updateTime'])))}</td><td>${ccEsc(ccPick(t,['displaySymbol','displayName','symbol','instId']))}</td><td><span${style}>${ccEsc(h('ccOrderSide')(t))}</span></td><td>${ccEsc(h('ccFuturesTradeAmountCell')(t))}</td><td>${ccEsc(h('ccFuturesTradeValueCell')(t))}</td><td>${ccEsc(h('ccFuturesTradePriceText')(t,['price','fillPrice','filledPrice','px']))}</td><td>${ccEsc(h('ccFuturesTradePriceText')(t,['markPrice']))}</td><td>${ccEsc(h('ccFuturesTradePriceText')(t,['indexPrice']))}</td><td>${ccEsc(orderId)}｜${ccEsc(tradeId)}</td><td>${ccEsc(h('ccTradeFeeText')(t))}</td><td>${ccEsc(ccFmtSignedNumberOrDash(ccPick(t,['rpnl','realizedPnl','realizedPNL']),2))}</td><td>${ccEsc(h('ccFuturesTradeRoleText')(t))}</td></tr>`;
  }
  window.DeribitHistoryTabs = {
    orderHistoryRow,
    tradeHistoryRow
  };
})();
