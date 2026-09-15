const fs = require('fs');
const assert = require('assert');

const htmlPath = process.argv[2] || 'webclient/dist/coincall-spread-workstation.html';
const html = fs.readFileSync(htmlPath, 'utf8');

function mustContain(snippet, label) {
  assert(html.includes(snippet), label);
}

mustContain("function ccSpreadBotOrdersNetSellKind()", 'Orders Net has a market-aware sell leg helper');
mustContain("if(/^(BTC|ETH)USDT[-_]?SPOT$/.test(token)) return token.slice(0, 3) + 'USDT';", 'workstation pair parser accepts BTCUSDT-SPOT / ETHUSDT-SPOT suffixes');
mustContain("return (ccSpreadBotOrdersNetLegs().sellLeg || {}).market === 'spot' ? 'spot' : 'futures';", 'sell leg resolves to spot when the selected sell leg is spot');
mustContain("const p = sellKind === 'spot' ? { symbol, tradeSide:'SELL', tradeType:'POST_ONLY', qty:amount, price:String(price) }", 'spot sell leg builds a CoinCall spot SELL POST_ONLY payload');
mustContain("'/api/admin/coincall/' + sellKind + '/orders/place'", 'sell leg placement uses the selected market API');
mustContain("sellKind === 'spot' ? await ccSpreadBotReconcileSpotOrder", 'spot sell leg reconciles through spot open orders');
mustContain("const wantedSide = String(ccFirstValue(payload,['tradeSide','side'])).toUpperCase() === 'SELL' ? 'sell' : 'buy';", 'spot fill-history matching supports SELL fills');
mustContain("return leg === 'SellLimitOrder' ? ccSpreadBotOrdersNetSellKind() : ccSpreadBotOrdersNetBuyKind();", 'partial-fill handling uses the selected sell-leg market');
mustContain("if(sellKind === 'spot') await ccSpreadBotCancelSpot", 'sell-leg cancellation supports spot orders');
mustContain("const gone = sellKind === 'spot' ? await ccSpreadBotVerifySpotAbsent", 'sell-leg absent verification supports spot orders');
mustContain("function ccSpreadsPrimeLegBidAskFilter(leg)", 'spot/futures leg BidAskMedian can be primed immediately from live book data');
mustContain("for(let i = 0; i < CC_BIDASK_FILTER_WINDOW; i++)", 'BidAskMedian priming fills the full filter window instead of waiting for 20 future ticks');
mustContain("ccSpreadsPrimeLegBidAskFilter(CC_SPREADS_BOOK_STATE.items[role] || { symbol, market:'spot', type:'spot' });", 'spot book ticks prime the filtered BidAskMedian source used by Orders Net L1 Trail');
mustContain("const modalRowSymbol = String(ccSpreadsTradeModalState?.rowSymbol || '').toUpperCase();", 'Orders Net BidAskMedian fallback maps legs to the 1Min Spread Chart row symbol');
mustContain("const chartRole = symbol && modalRowSymbol && ccSpreadBotOrdersNetSymbolsMatch(symbol, modalRowSymbol) ? 'row'", 'Orders Net uses rowBidAskMedian/colBidAskMedian from the active chart state by symbol');
mustContain("retry same L1 target because L1 Trail never moves sell orders up", 'sell L1 Trail expired replacement retries the same target instead of restarting higher');
mustContain("retry same L1 target because L1 Trail never moves buy orders down", 'buy L1 Trail expired replacement retries the same target instead of restarting lower');
mustContain("function ccSpreadBotOrdersNetSpotIndexForLeg(leg)", 'Orders Net can read Index for spot-leg trail trigger');
mustContain("if(Number.isFinite(spotIndex) && spotIndex > 0) return current - spotIndex > size;", 'spot sell L1 Trail triggers from Index distance');
mustContain("if((leg || {}).market === 'spot') return false;\n    return current - raw > size;", 'futures sell L1 Trail stays on BidAskMedian while spot sell requires Index');
mustContain("const bid = ccSpreadBotOrdersNetBestForLeg(leg, 'bid');\n  if(Number.isFinite(bid) && bid > 0) return bid - current > size;", 'buy L1 Trail triggers only when bid moved away above the buy order');
mustContain("function ccSpreadBotOrdersNetSpotBidAskMedian(){\n  const leg = ccSpreadBotOrdersNetLegs().buyLeg || {};\n  return ccSpreadBotOrdersNetLegBidAskMedian(leg, 'buy');\n}", 'Orders Net buy-leg BidAskMedian keeps chart fallback in L1 Trail');
mustContain("function ccSpreadBotOrdersNetFuturesBidAskMedian(){\n  const leg = ccSpreadBotOrdersNetLegs().sellLeg || {};\n  return ccSpreadBotOrdersNetLegBidAskMedian(leg, 'sell');\n}", 'Orders Net sell-leg BidAskMedian keeps chart fallback in L1 Trail');

console.log('orders_net_spot_dated_static: ok');
