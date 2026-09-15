const fs = require('fs');
const vm = require('vm');
const assert = require('assert');

const htmlPath = process.argv[2] || 'coincall-spread-workstation.html';
const html = fs.readFileSync(htmlPath, 'utf8');
const start = html.indexOf('const CC_SPREADBOT_ORDERS_NET_DEFAULT_AMOUNT');
const end = html.indexOf('function ccSpreadsResetLegDetailsAutomation', start);
assert(start > 0 && end > start, 'Orders Net implementation block not found');
const impl = html.slice(start, end);
const uiStart = html.indexOf('const CC_SPREADBOT_UI_STATE_KEY');
const uiEnd = html.indexOf('function ccSpreadBotFastTrailingReason', uiStart);
assert(uiStart > 0 && uiEnd > uiStart, 'SpreadBot UI persistence block not found');
const uiImpl = html.slice(uiStart, uiEnd);

const calls = [];
const context = {
  console,
  setTimeout: () => 0,
  clearTimeout: () => {},
  Date,
  Number,
  Math,
  String,
  RegExp,
  ccSpreadBotOrdersNetLevels: '4',
  ccSpreadBotOrdersNetToggleOn: true,
  ccSpreadBotOrdersNetLegOrderMode: 'long-first',
  ccSpreadBotLegOrderMode: 'long-first',
  ccSpreadBotOrdersNetSpreadSide: 'sell',
  ccSpreadBotOrdersNetAmount: '0.001',
  ccSpreadBotOrdersNetState: 'Idle',
  ccSpreadBotExecutionMode: 'orders-net',
  ccSpreadBotOrdersNetSpreadSizeUsd: '25',
  ccSpreadBotOrdersNetL1Price: '100000',
  ccSpreadBotOrdersNetL1Mode: 'price',
  ccSpreadBotOrdersNetL1TrailSizeUsd: '50',
  ccSpreadBotOrdersNetStepUsd: '100',
  ccSpreadBotDdhShortLeg: 'BTCUSD',
  ccSpreadBotDdhLongLeg: 'BTCUSDT',
  ccSpreadBotDdhAmount: '0.001',
  ccSpreadBotDdhToggleOn: false,
  ccSpreadBotDdhAsset: 'BTC',
  ccSpreadBotPanelMode: 'chart',
  localStorage: {
    data: {
      cc_spreadbot_ui_state_v1: JSON.stringify({
        pairs: {
          default: {
            executionMode: 'orders-net',
            ordersNetToggleOn: false,
            ordersNetAmount: '0.001',
            ordersNetLevels: '1',
            ordersNetSpreadSizeUsd: '25',
            ordersNetL1Price: '100000',
            ordersNetL1Mode: 'price',
            ordersNetL1TrailSizeUsd: '50',
            ordersNetStepUsd: '100',
            panelMode: 'info'
          }
        }
      })
    },
    getItem(key) { return this.data[key] || null; },
    setItem(key, value) { this.data[key] = String(value); }
  },
  CC_SPREADBOT_TIMEOUTS: { placeApiMs: 4000, placeReconcileMs: 0, cancelApiMs: 4000, cancelReconcileMs: 0, failedVerifyMs: 0, tickMs: 0 },
  CC_SPREADS_LEG_DETAILS_AUTOMATION: { shutdownRequested: false },
  CC_SPOT_REST_REPLACE_GUARD_MS: 0,
  CC_FUTURES_REST_REPLACE_GUARD_MS: 0,
  CC_FUTURES_ORDER_REFRESH: {},
  ccSpotProtectOpenOrdersUntil: 0,
  ccLastSpotOpenOrders: [],
  ccLastSpotTradeHistory: [],
  ccLastFuturesOpenOrders: [],
  ccLastFuturesTradeHistory: [],
  ccLastFuturesInstruments: [],
  CC_SPREADS_BOOK_STATE: { items: { spot: { symbol: 'BTCUSDT' }, row: { symbol: 'BTCUSD' }, col: null }, symbols: { spot: 'BTCUSDT', row: 'BTCUSD', col: '' }, types: { spot: 'spot', row: 'futures', col: 'futures' } },
  ccSpreadsMarkPrice(row) {
    const symbol = String(row && row.symbol || '').toUpperCase();
    return symbol.includes('BTCUSDT') ? context.bestSpotBid : context.bestFuturesAsk;
  },
  ccCollectObjects(node, out = []) {
    if (Array.isArray(node)) { node.forEach(x => context.ccCollectObjects(x, out)); return out; }
    if (node && typeof node === 'object') { out.push(node); Object.values(node).forEach(x => { if (x && typeof x === 'object') context.ccCollectObjects(x, out); }); }
    return out;
  },
  ccFuturesOrderKey(o) { return String(o && (o.orderId || o.clientOrderId || o.id || '')); },
  ccOrderFilledQty(o) { return Number(o && (o.fillQty || o.filledQty || 0)) || 0; },
  ccFirstValue(o, keys) { for (const k of keys) if (o && o[k] !== undefined && o[k] !== null && o[k] !== '') return o[k]; },
  ccOrderIdString(v) { if (v === undefined || v === null) return ''; const s = String(v).trim(); return s && s !== '—' ? s : ''; },
  ccOrderFirstId(o, names) { return context.ccOrderIdString(context.ccFirstValue(o, names)); },
  ccSpotSymbolNorm(v) { return String(v || '').toUpperCase().replace(/[^A-Z0-9]/g, ''); },
  ccSpotCanonicalSymbol(value) { return context.ccSpotSymbolNorm(value); },
  ccSpotOrderSymbol(o) { return context.ccSpotCanonicalSymbol(context.ccFirstValue(o, ['symbol', 'instId', 'displaySymbol', 'instrument', 'instrumentId', 'pair'])); },
  ccSpotOrderSideRaw(o) {
    const raw = context.ccFirstValue(o || {}, ['side', 'tradeSide', 'orderSide', 'direction', 'sd', 'si']);
    const key = String(raw || '').trim().toUpperCase().replace(/[\s_-]+/g, '');
    if (['BUY', 'BID', 'LONG', 'OPENLONG', 'CLOSESHORT', '1'].includes(key)) return 'buy';
    if (['SELL', 'ASK', 'SHORT', 'OPENSHORT', 'CLOSELONG', '2'].includes(key)) return 'sell';
    return String(raw || '').toLowerCase();
  },
  ccSpotOrderIdentity(o) { return { orderId: context.ccOrderFirstId(o, ['orderId', 'ordId', 'order_id', 'id', 'oid', 'orderNo']), clientOrderId: context.ccOrderFirstId(o, ['clientOrderId', 'clientOid', 'clOrdId', 'coid']) }; },
  ccSpotOrderIdentityKeys(o) { const ids = context.ccSpotOrderIdentity(o), keys = []; if (ids.orderId) { keys.push('oid:' + ids.orderId); keys.push('id:' + ids.orderId); } if (ids.clientOrderId) { keys.push('cid:' + ids.clientOrderId); keys.push('id:' + ids.clientOrderId); } return keys; },
  ccSpotOrderHasIdentity(o) { const ids = context.ccSpotOrderIdentity(o); return !!(ids.orderId || ids.clientOrderId); },
  ccCanonicalOrderNumberString(v) { const n = Number(v); return Number.isFinite(n) ? String(Number(n.toPrecision(15))) : String(v ?? '').trim(); },
  ccSpotOrderAmountRaw(o) { return context.ccFirstValue(o, ['qty', 'quantity', 'amount', 'orderQty', 'origQty', 'volume', 'q', 'sz']); },
  ccSpotOrderPrice(o) { const n = Number(context.ccFirstValue(o, ['price', 'px', 'orderPrice', 'limitPrice'])); return Number.isFinite(n) ? n : null; },
  ccSpotOrderTypeKey(o) {
    const raw = context.ccFirstValue(o || {}, ['tradeType', 'orderType', 'type', 'ot', 'orderTypeName', 'tradeTypeName']);
    const key = String(raw ?? '').trim().toUpperCase().replace(/[\s_-]+/g, '');
    if (key === '1' || key === 'LIMIT') return 'LIMIT';
    if (key === '2' || key === 'MARKET') return 'MARKET';
    if (key === '3' || key === 'POSTONLY') return 'POST_ONLY';
    return key;
  },
  ccSpotOrderShapeKey(o) { const sym = context.ccSpotOrderSymbol(o), side = context.ccSpotOrderSideRaw(o), priceValue = context.ccSpotOrderPrice(o), price = context.ccCanonicalOrderNumberString(priceValue), qty = context.ccCanonicalOrderNumberString(context.ccSpotOrderAmountRaw(o)), type = context.ccSpotOrderTypeKey(o) || (Number.isFinite(priceValue) ? 'LIMIT' : ''); return sym && side && price && qty ? ['shape', sym, side, price, qty, type].join('|') : ''; },
  ccSpotOrderSame(a, b) { const ak = context.ccSpotOrderIdentityKeys(a), bk = context.ccSpotOrderIdentityKeys(b); if (ak.length && bk.length) return ak.some(k => bk.includes(k)); return !!(context.ccSpotOrderShapeKey(a) && context.ccSpotOrderShapeKey(a) === context.ccSpotOrderShapeKey(b)); },
  ccSpotTradePrice(t) { const n = Number(context.ccFirstValue(t, ['fillPrice', 'filledPrice', 'dealPrice', 'execPrice', 'matchPrice', 'price', 'px'])); return Number.isFinite(n) ? n : NaN; },
  ccSpotTradeTs(t) { const v = Number(context.ccFirstValue(t, ['ts', 'tradeTime', 'fillTime', 'createdTime', 'createTime', 'updateTime', 'time'])); if (!Number.isFinite(v)) return NaN; return v > 0 && v < 1e12 ? v * 1000 : v; },
  ccSpotTradeQty(t) { const n = Number(context.ccFirstValue(t, ['qty', 'quantity', 'amount', 'fillQty', 'filledQty', 'filledQuantity', 'dealQty', 'baseQty'])); return Number.isFinite(n) ? n : NaN; },
  ccFuturesBookSymbolKey(v) { return String(v || '').toUpperCase().replace(/[^A-Z0-9]/g, '').replace(/USDT(?=\d)/, 'USD').replace(/PERP$/, '').replace(/USDT$/, '').replace(/USD$/, ''); },
  ccFuturesOrderSymbolKey(o) { return context.ccFuturesBookSymbolKey(context.ccFirstValue(o, ['displaySymbol', 'displayName', 'symbol', 'instId', 'instrument', 'ticker_id', 'baseToken', 'base_currency', 's'])); },
  ccFuturesOrderIdentity(o) { return { orderId: context.ccOrderFirstId(o, ['orderId', 'ordId', 'order_id', 'id', 'oid']), clientOrderId: context.ccOrderFirstId(o, ['clientOrderId', 'clientOid', 'clOrdId', 'coid']) }; },
  ccFuturesOrderIdentityKeys(o) { const ids = context.ccFuturesOrderIdentity(o), keys = []; if (ids.orderId) { keys.push('oid:' + ids.orderId); keys.push('id:' + ids.orderId); } if (ids.clientOrderId) { keys.push('cid:' + ids.clientOrderId); keys.push('id:' + ids.clientOrderId); } return keys; },
  ccFuturesOrderHasIdentity(o) { const ids = context.ccFuturesOrderIdentity(o); return !!(ids.orderId || ids.clientOrderId); },
  ccFuturesOrderAmountRaw(o) { return context.ccFirstValue(o, ['qty', 'quantity', 'amount', 'size', 'orderQty', 'origQty', 'q', 'sz']); },
  ccFuturesOrderPrice(o) { const n = Number(context.ccFirstValue(o, ['price', 'px', 'orderPrice', 'limitPrice'])); return Number.isFinite(n) ? n : null; },
  ccFuturesOrderShapeKey(o) { const sym = context.ccFuturesOrderSymbolKey(o), side = context.ccSpotOrderSideRaw(o), priceValue = context.ccFuturesOrderPrice(o), price = context.ccCanonicalOrderNumberString(priceValue), qty = context.ccCanonicalOrderNumberString(context.ccFuturesOrderAmountRaw(o)), type = context.ccSpotOrderTypeKey(o) || (Number.isFinite(priceValue) ? 'LIMIT' : ''); return sym && side && price && qty ? ['shape', sym, side, price, qty, type].join('|') : ''; },
  ccFuturesOrderSame(a, b) { const ak = context.ccFuturesOrderIdentityKeys(a), bk = context.ccFuturesOrderIdentityKeys(b); if (ak.length && bk.length) return ak.some(k => bk.includes(k)); return !!(context.ccFuturesOrderShapeKey(a) && context.ccFuturesOrderShapeKey(a) === context.ccFuturesOrderShapeKey(b)); },
  ccFuturesTradePrice(t) { const n = Number(context.ccFirstValue(t, ['price', 'tradePrice', 'dealPrice', 'lastPrice', 'px', 'pr', 'matchPrice', 'mpr'])); return Number.isFinite(n) ? n : NaN; },
  ccFuturesTradeTs(t) { const v = Number(context.ccFirstValue(t, ['ts', 'time', 'tradeTime', 'createdTime', 'createTime'])); if (!Number.isFinite(v)) return NaN; return v > 0 && v < 1e12 ? v * 1000 : v; },
  ccFuturesTradeSideRaw(t) { return context.ccSpotOrderSideRaw(t); },
  ccSpreadBotOrderFilledQty(o) { return context.ccOrderFilledQty(o); },
  ccSpreadBotOrderTotalQty(o) { return Number(o && (o.qty || o.quantity || '0.001')); },
  ccSpreadBotIsFullyFilled(order, amount) { return context.ccSpreadBotOrderFilledQty(order) >= Number(amount) * 0.999; },
  ccSpreadBotFindFuturesOrder(ref) { return (context.ccLastFuturesOpenOrders || []).find(o => o === ref || context.ccFuturesOrderSame(o, ref)) || null; },
  ccSpreadBotFindSpotOrder(ref) { return (context.ccLastSpotOpenOrders || []).find(o => o === ref || context.ccSpotOrderSame(o, ref)) || null; },
  ccSpreadBotBookBest(role, side) {
    if (role === 'spot' && side === 'bid') return context.bestSpotBid;
    if (role === 'spot' && side === 'ask') return context.bestSpotAsk;
    if (role === 'row' && side === 'bid') return context.bestFuturesBid;
    if (role === 'row' && side === 'ask') return context.bestFuturesAsk;
    return 101000;
  },
  bestSpotBid: 100500,
  bestSpotAsk: 100520,
  bestFuturesBid: 100080,
  bestFuturesAsk: 100100,
  ccSpreadBotFuturesRole() { return 'row'; },
  ccSpreadBotDdhPatchState() {},
  ccPick(o, keys) { for (const k of keys) if (o && o[k] !== undefined && o[k] !== null && o[k] !== '') return o[k]; },
  ccBalanceRows() { return [{ ccy: 'BTC', equity: context.ddhSpotBalance ?? 0 }]; },
  ccRowEquityValue(r) { return Number(r && r.equity); },
  ccOrderBaseAsset(o) { return o && (o.base || o.baseToken || 'BTC'); },
  ccLastAssetsSummary: {},
  ccLastFuturesPositions: [],
  ccSpreadsAssetState() { return { perp: { symbol: 'BTCUSD', displayName: 'BTCUSDT-Perp', baseToken: 'BTC' }, columns: [] }; },
  ccSpreadsFuturesBase() { return 'BTC'; },
  ccFuturesQuoteSymbolRows() { return []; },
  ccSpreadBotSpotSymbol() { return 'BTCUSDT'; },
  ccSpreadBotFuturesSymbol() { return 'BTCUSD'; },
  ccSpreadBotSpotLabel() { return 'BTC/USDT'; },
  ccSpreadBotFuturesLabel() { return 'BTCUSDT-Perp'; },
  ccOrderSubmitPendingKey(m, p) { return m + ':' + p.symbol + ':' + p.tradeSide + ':' + p.price; },
  ccOrderSubmitLock() { return true; },
  ccOrderSubmitUnlock() {},
  ccEnsureSpotPrivateSocket() { return { catch() {} }; },
  ccEnsureFuturesPrivateSocket() { return { catch() {} }; },
  ccSpreadsLatencyStart() { return {}; },
  ccSpreadsLatencyNow() { return 0; },
  ccSpreadsLatencyFinish() {},
  ccAddOptimisticSpotOpenOrder(p, res) { context.ccLastSpotOpenOrders = [{ ...p, orderId: res.data.orderId, fillQty: 0 }]; },
  ccAddOptimisticFuturesOpenOrder(p, res) { context.ccLastFuturesOpenOrders = [{ ...p, orderId: res.data.orderId, fillQty: 0 }]; },
  ccSpreadBotReconcileSpotOrder(p, before, res) { return Promise.resolve(context.ccLastSpotOpenOrders[0]); },
  ccSpreadBotReconcileFuturesOrder(p, before, res) { return Promise.resolve(context.ccLastFuturesOpenOrders[0]); },
  ccFuturesOpenOrderKeySet() { return new Set(); },
  ccReconcileSpotOpenOrdersSoon() {},
  ccScheduleSpotTradeRefreshBurst() {},
  ccScheduleFuturesOrderRefreshBurst() {},
  ccSpreadsScheduleFastPrivateRefresh() {},
  loadCcSpotTradeHistory() { calls.push({ loadSpotTradeHistory: true }); return Promise.resolve(); },
  loadCcFuturesTradeHistory() { calls.push({ loadFuturesTradeHistory: true }); return Promise.resolve(context.ccLastFuturesTradeHistory); },
  ccSpreadBotApi(url, opt) { calls.push({ url, payload: JSON.parse(opt.body) }); return Promise.resolve({ data: { orderId: calls.length > 1 ? '9007199254740993124' : '9007199254740993123' } }); },
  ccSpreadBotLog(action, message, state) { calls.push({ log: action, message, state }); },
  ccSpreadsRenderLegDetailsAutomationState() {},
  ccSpreadsSyncLegDetailsGate() {},
  ccSpreadBotPairKey() { return 'default'; },
  ccSpreadsLegDetailsAutomationWord() { return 'Idle'; },
  ccSpreadBotCancelSpot() { calls.push({ cancelSpot: true }); return Promise.resolve({ ok: true }); },
  ccSpreadBotCancelFutures() { calls.push({ cancelFutures: true }); return Promise.resolve({ ok: true }); },
  ccSpreadBotVerifySpotAbsent() { return Promise.resolve(true); },
  ccSpreadBotVerifyFuturesAbsent() { return Promise.resolve(true); },
  ccSleep() { return Promise.resolve(); },
  ccEsc(v) { return String(v ?? ''); },
  ccFmt(v) { return String(v); },
  ccPriceDecimals() { return 2; },
  ccSpreadBotAmountLabel() { return 'Amount (BTC)'; }
};
vm.createContext(context);
vm.runInContext(uiImpl + impl + '\nthis.CC_SPREADBOT_ORDERS_NET = CC_SPREADBOT_ORDERS_NET; this.CC_SPREADBOT_DDH = CC_SPREADBOT_DDH;', context);

(async () => {
  context.ddhSpotBalance = 0.002;
  context.ccLastFuturesPositions = [];
  context.ccSpreadBotDdhToggleOn = true;
  context.ccSpreadBotDdhShortLeg = 'BTCUSD';
  context.ccSpreadBotDdhLongLeg = 'BTCUSDT';
  context.ccSpreadBotDdhAsset = 'BTC';
  context.ccSpreadBotDdhAmount = '0.002';
  context.bestFuturesBid = 100080;
  context.bestFuturesAsk = 100100;
  assert.strictEqual(context.ccSpreadBotDdhTargetPrice('sell', context.ccSpreadBotDdhLegMeta('BTCUSD')), 100110, 'DDH positive delta sell price uses max(MarkPrice + 10, bid + 1)');
  calls.length = 0;
  context.CC_SPREADBOT_DDH.state = 'Init';
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '100110', 'DDH positive delta places Short Leg sell');
  context.ccLastFuturesOpenOrders[0].fillQty = '0.002';
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(context.CC_SPREADBOT_DDH.state, 'Init', 'DDH fill restarts bot');
  context.ccSpreadBotDdhToggleOn = true;
  context.CC_SPREADBOT_DDH.state = 'Init';
  context.ccSpreadBotApplyPersistedUiState();
  assert.strictEqual(context.ccSpreadBotDdhToggleOn, true, 'active DDH runtime stays ON across modal rerender');
  context.CC_SPREADBOT_DDH.state = 'Idle';
  context.ccSpreadBotDdhToggleOn = true;
  context.ccSpreadBotApplyPersistedUiState();
  assert.strictEqual(context.ccSpreadBotDdhToggleOn, false, 'Idle DDH does not restore ON from persisted UI state');
  context.ccSpreadBotDdhToggleOn = true;
  context.ddhSpotBalance = -0.002;
  context.ccLastFuturesOpenOrders = [];
  context.CC_SPREADBOT_DDH.state = 'Init';
  context.bestSpotBid = 100500;
  context.bestSpotAsk = 100520;
  assert.strictEqual(context.ccSpreadBotDdhTargetPrice('buy', context.ccSpreadBotDdhLegMeta('BTCUSDT')), 100490, 'DDH negative delta buy price uses min(MarkPrice - 10, ask - 1)');
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.price, '100490', 'DDH negative delta places Long Leg buy');
  assert.strictEqual(context.ccSpreadBotDdhPanelStateText(), 'BuyLimitOrder_Placed', 'DDH panel state shows placed buy order side');
  const ddhBuyPlacedAt = Date.now();
  context.CC_SPREADBOT_DDH.placedAt = ddhBuyPlacedAt;
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [{ orderId: context.CC_SPREADBOT_DDH.activeOrder.orderId, symbol: 'BTCUSDT', side: 'BUY', price: '100490', qty: '0.002', ts: ddhBuyPlacedAt + 1000 }];
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(context.CC_SPREADBOT_DDH.state, 'Init', 'DDH spot buy disappeared from open orders but matched trade history full fill restarts');
  context.ccLastSpotTradeHistory = [];
  context.CC_SPREADBOT_DDH.state = 'Order_Placed';
  context.CC_SPREADBOT_DDH.activeLeg = context.ccSpreadBotDdhLegMeta('BTCUSDT');
  context.CC_SPREADBOT_DDH.activeSide = 'buy';
  context.CC_SPREADBOT_DDH.activePrice = 100490;
  context.CC_SPREADBOT_DDH.activeOrder = { orderId: 'ddh-buy-open', symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.002', price: '100490', fillQty: 0 };
  context.CC_SPREADBOT_DDH.activePayload = { symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.002', price: '100490' };
  context.ccLastSpotOpenOrders = [context.CC_SPREADBOT_DDH.activeOrder];
  context.bestSpotBid = 100470;
  context.bestSpotAsk = 100485;
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(calls.some(c => c.cancelSpot), true, 'DDH cancels before moving a buy order');
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.price, '100460', 'DDH moves buy order when target is 10 USD lower');
  calls.length = 0;
  context.CC_SPREADBOT_DDH.state = 'Order_Placed';
  context.CC_SPREADBOT_DDH.activeLeg = context.ccSpreadBotDdhLegMeta('BTCUSDT');
  context.CC_SPREADBOT_DDH.activeSide = 'buy';
  context.CC_SPREADBOT_DDH.activePrice = 100490;
  context.CC_SPREADBOT_DDH.activeOrder = context.ccLastSpotOpenOrders[0];
  context.bestSpotBid = 100550;
  context.bestSpotAsk = 100570;
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(calls.some(c => c.cancelSpot), true, 'DDH cancels before moving a buy order upward');
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.price, '100540', 'DDH moves buy order when target is 10 USD higher');
  calls.length = 0;
  context.CC_SPREADBOT_DDH.state = 'Order_Placed';
  context.CC_SPREADBOT_DDH.activeLeg = context.ccSpreadBotDdhLegMeta('BTCUSD');
  context.CC_SPREADBOT_DDH.activeSide = 'sell';
  context.CC_SPREADBOT_DDH.activePrice = 100110;
  context.CC_SPREADBOT_DDH.activeOrder = { orderId: 'ddh-sell-open', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.002', price: '100110', fillQty: 0 };
  context.ccLastFuturesOpenOrders = [context.CC_SPREADBOT_DDH.activeOrder];
  context.bestFuturesBid = 100020;
  context.bestFuturesAsk = 100060;
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(calls.some(c => c.cancelFutures), true, 'DDH cancels before moving a sell order downward');
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '100070', 'DDH moves sell order when target is 10 USD lower');
  assert.strictEqual(context.ccSpreadBotDdhPanelStateText(), 'SellLimitOrder_Placed', 'DDH panel state shows placed sell order side');
  const ddhSellPlacedAt = Date.now();
  context.CC_SPREADBOT_DDH.state = 'Order_Placed';
  context.CC_SPREADBOT_DDH.activeLeg = context.ccSpreadBotDdhLegMeta('BTCUSD');
  context.CC_SPREADBOT_DDH.activeSide = 'sell';
  context.CC_SPREADBOT_DDH.activePrice = 100070;
  context.CC_SPREADBOT_DDH.activeOrder = { orderId: 'ddh-sell-fill', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.002', price: '100070', fillQty: 0 };
  context.CC_SPREADBOT_DDH.activePayload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.002', price: '100070' };
  context.CC_SPREADBOT_DDH.placedAt = ddhSellPlacedAt;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [{ orderId: 'ddh-sell-fill', symbol: 'BTCUSD', side: 'SELL', price: '100070', qty: '0.002', ts: ddhSellPlacedAt + 1000 }];
  await context.ccSpreadBotDdhTick();
  assert.strictEqual(context.CC_SPREADBOT_DDH.state, 'Init', 'DDH futures sell disappeared from open orders but matched trade history full fill restarts');
  context.ccSpreadBotDdhToggleOn = false;
  context.ccSpreadBotDdhResetVariables();
  calls.length = 0;

  assert.strictEqual(JSON.stringify(context.ccSpreadBotOrdersNetLevelPrices(4, 100000, 100)), JSON.stringify([100000, 99900, 99800, 99700]), 'level price formula');
  context.ccSpreadBotOrdersNetLevels = '4';
  let table = context.ccSpreadBotAlgoInfoHtml ? context.ccSpreadBotAlgoInfoHtml({}, {}, 0) : '';
  if (!table) {
    const renderStart = html.indexOf('function ccSpreadBotAlgoInfoHtml');
    const renderEnd = html.indexOf('function ccSpreadBotOrdersNetFooterHtml', renderStart);
    vm.runInContext(html.slice(renderStart, renderEnd), context);
    table = context.ccSpreadBotAlgoInfoHtml({}, {}, 0);
  }
  assert.strictEqual((table.match(/<tr>/g) || []).length - 1, 4, 'Algo Info row count');
  assert(table.includes('<th>Level</th><th>Symbol</th><th>Side</th><th>Amount</th><th>Price</th>'), 'Algo Info columns');
  assert(table.includes('<tr><td>1</td><td>BTC/USDT</td><td>Buy</td><td>0.001 BTC</td><td>100000</td></tr>'), 'Algo Info L1 planned spot row');
  assert(table.includes('<tr><td>4</td><td>BTC/USDT</td><td>Buy</td><td>0.001 BTC</td><td>99700</td></tr>'), 'Algo Info L4 planned spot row');

  const renderStart = html.indexOf('function ccSpreadBotOrdersNetSideInputsHtml');
  const renderEnd = html.indexOf('function ccSpreadBotOrdersNetStateWord', renderStart);
  vm.runInContext(html.slice(renderStart, renderEnd), context);
  context.ccSpreadBotOrdersNetToggleOn = false;
  context.ccSpreadBotOrdersNetAmount = '0.002';
  const unlocked = context.ccSpreadBotOrdersNetSideInputsHtml();
  assert(unlocked.includes('data-cc-spreadbot-orders-net-amount value="0.002"') && !unlocked.match(/data-cc-spreadbot-orders-net-amount[^>]* disabled/), 'Orders Net Amount is editable while OFF');
  context.ccSpreadBotOrdersNetToggleOn = true;
  const locked = context.ccSpreadBotOrdersNetSideInputsHtml();
  assert(locked.match(/data-cc-spreadbot-orders-net-amount[^>]* disabled/) && locked.includes('data-cc-spreadbot-orders-net-spread-size-usd') && locked.includes(' disabled'), 'ON locks Amount, Spread Size and inputs');
  context.ccSpreadBotOrdersNetAmount = '0.002';
  await context.ccSpreadBotOrdersNetPlaceSpotBuy(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.qty, '0.002', 'Orders Net spot buy uses editable amount');
  await context.ccSpreadBotOrdersNetPlaceFuturesSell(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.qty, '0.002', 'Orders Net futures sell uses editable amount');
  context.ccSpreadBotOrdersNetAmount = '0.001';
  calls.length = 0;

  context.bestSpotBid = 100500;
  assert.strictEqual(context.ccSpreadBotOrdersNetSpotBuyPrice(1), 100000, 'bid >= level uses level price');
  context.bestSpotBid = 99950;
  assert.strictEqual(context.ccSpreadBotOrdersNetSpotBuyPrice(1), 99950, 'bid < level uses best bid');
  await context.ccSpreadBotOrdersNetPlaceSpotBuy(1);
  assert.strictEqual(calls.find(c => c.payload && c.payload.symbol === 'BTCUSDT').payload.price, '99950', 'L1 first attempt price selection');
  context.bestSpotBid = 100500;
  await context.ccSpreadBotOrdersNetPlaceSpotBuy(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.price, '100000', 'L1 retry uses same price rule');
  const expiredSpotRetrySlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy');
  expiredSpotRetrySlot.retry = 1;
  expiredSpotRetrySlot.lastError = 'CoinCall API error 10540: Order has expired';
  context.bestSpotBid = 99980;
  await context.ccSpreadBotOrdersNetPlaceSpotBuy(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.price, '99930', '10540 spot buy retry moves to MarkPrice - 50 when first price is too close');
  expiredSpotRetrySlot.retry = 0;
  expiredSpotRetrySlot.lastError = '';
  context.bestSpotBid = 100500;
  await context.ccSpreadBotOrdersNetPlaceFuturesSell(1);
  assert.strictEqual(calls.find(c => c.payload && c.payload.symbol === 'BTCUSD').payload.price, '100025', 'futures price level plus spread');
  context.ccSpreadBotOrdersNetSpreadSizeUsd = '999';
  await context.ccSpreadBotOrdersNetPlaceFuturesSell(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '100025', 'futures retry reuses first price');
  const expiredRetrySlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  expiredRetrySlot.retry = 1;
  expiredRetrySlot.lastError = 'CoinCall API error 10540: Order has expired';
  context.bestFuturesAsk = 100000;
  await context.ccSpreadBotOrdersNetPlaceFuturesSell(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '100050', '10540 futures sell retry moves to MarkPrice + 50 when first price is too close');
  expiredRetrySlot.retry = 0;
  expiredRetrySlot.lastError = '';
  context.bestFuturesAsk = 100100;
  assert.strictEqual(typeof context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell').id, 'string', 'long ids preserved as strings');

  context.ccSpreadBotOrdersNetToggleOn = true;
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 1;
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  context.ccSpreadBotApplyPersistedUiState();
  context.ccSpreadsSyncLegDetailsGate();
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'NumOfLevels=1 waits for futures fill');
  assert.strictEqual(context.ccSpreadBotOrdersNetToggleOn, true, 'UI restore/modal refresh keeps active Orders Net runtime ON');
  assert.strictEqual(context.ccSpreadBotOrdersNetPanelStateText(context.ccSpreadBotOrdersNetState), 'L1_SellLimitOrder_Placed_Successfully', 'panel display trims Orders Net LLF_SS prefix');
  assert.strictEqual(calls.filter(c => c.log === 'test').at(-1).state, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'logs keep full internal Orders Net state');

  const spotPlacedBlock = impl.indexOf("state.match(/^(SpreadBot_OrNet_(?:LLF|SLF)_SS)_L(\\d+)_BuyLimitOrder_Placed_Successfully$/)");
  const spotCacheCheck = impl.indexOf("if(ccSpreadBotOrdersNetOrderPartialFill(slot,'spot'))", spotPlacedBlock);
  const spotRestRefresh = impl.indexOf('await ccSpreadBotOrdersNetRefreshSpotFillHistory(slot);', spotPlacedBlock);
  assert(spotCacheCheck > 0 && spotRestRefresh > spotCacheCheck, 'Orders Net checks WS-fed spot cache before REST trade-history reconciliation');
  const futuresPlacedBlock = impl.indexOf("state.match(/^(SpreadBot_OrNet_(?:LLF|SLF)_SS)_L(\\d+)_SellLimitOrder_Placed_Successfully$/)");
  const futuresCacheCheck = impl.indexOf("if(ccSpreadBotOrdersNetOrderPartialFill(slot,'futures'))", futuresPlacedBlock);
  const futuresRestRefresh = impl.indexOf('await ccSpreadBotOrdersNetRefreshFuturesFillHistory(slot);', futuresPlacedBlock);
  assert(futuresCacheCheck > 0 && futuresRestRefresh > futuresCacheCheck, 'Orders Net checks WS-fed futures cache before REST trade-history reconciliation');
  assert(html.includes("ccSpreadBotOrdersNetPrivateEventTick('spot-ws/trade')"), 'spot private trade WS wakes Orders Net tick');
  assert(html.includes("ccSpreadBotOrdersNetPrivateEventTick('futures-ws/trade')"), 'futures private trade WS wakes Orders Net tick');
  assert(html.includes('if(!ccSpreadBotFindFuturesOrder(order)) return true;\n    await loadCcFuturesOpenOrders();'), 'futures cancel verification checks local WS-updated cache before REST');
  assert(html.includes('if(!ccSpreadBotFindSpotOrder(order)) return true;\n    await loadCcSpotOpenOrders'), 'spot cancel verification checks local WS-updated cache before REST');

  context.ccSpreadBotOrdersNetToggleOn = true;
  context.CC_SPREADBOT_ORDERS_NET.state = 'Idle';
  context.ccSpreadBotOrdersNetState = 'Idle';
  context.ccSpreadBotApplyPersistedUiState();
  assert.strictEqual(context.ccSpreadBotOrdersNetToggleOn, false, 'fresh Idle restore remains OFF by default');

  context.ccSpreadBotOrdersNetToggleOn = true;
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 2;
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L2_BuyLimitOrder_Placement', 'NumOfLevels>1 advances to L2 spot buy');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.state = 'SpreadBot_OrNet_SLF_SS_Init';
  context.ccSpreadBotOrdersNetState = 'SpreadBot_OrNet_SLF_SS_Init';
  context.ccSpreadBotOrdersNetToggleOn = true;
  context.ccSpreadBotOrdersNetLevels = '2';
  context.ccSpreadBotOrdersNetSpreadSizeUsd = '25';
  context.ccSpreadBotOrdersNetL1Price = '100000';
  context.ccSpreadBotOrdersNetStepUsd = '100';
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Init', 'SLF_SS init starts with futures sell');
  assert.strictEqual(JSON.stringify(context.CC_SPREADBOT_ORDERS_NET.levelPrices), JSON.stringify([100000, 100100]), 'SLF_SS level prices go upward from L1');
  const startupParamsLog = calls.find(c => c.log === 'params' && String(c.state || '').includes('SpreadBot_OrNet_SLF_SS_Init'));
  assert(startupParamsLog && startupParamsLog.message.includes('spreadSizeUsd=25'), 'Orders Net startup log includes Spread Size');
  assert(startupParamsLog.message.includes('spotMark=100500') && startupParamsLog.message.includes('futuresMark=100100'), 'Orders Net startup log includes spot/futures MarkPrice');
  assert(startupParamsLog.message.includes('L1:level=100000,futSell=100000,spotBuy=99975'), 'Orders Net startup log includes derived SLF_SS level targets');
  assert.strictEqual(context.ccSpreadBotOrdersNetPanelStateText(context.ccSpreadBotOrdersNetState), 'L1_SellLimitOrder_Init', 'panel display trims Orders Net SLF_SS prefix');
  context.bestFuturesAsk = 100100;
  assert.strictEqual(context.ccSpreadBotOrdersNetFuturesSellFirstPrice(1), 100100, 'SLF_SS futures sell uses MarkPrice when target is below MarkPrice');
  context.bestFuturesAsk = 99900;
  assert.strictEqual(context.ccSpreadBotOrdersNetFuturesSellFirstPrice(1), 100000, 'SLF_SS futures sell uses L1 target when target is above MarkPrice');
  context.bestSpotBid = 100500;
  assert.strictEqual(context.ccSpreadBotOrdersNetSpotBuyPrice(1), 99975, 'SLF_SS spot buy uses futures level minus spread when bid is above target');
  context.bestSpotBid = 99950;
  assert.strictEqual(context.ccSpreadBotOrdersNetSpotBuyPrice(1), 99950, 'SLF_SS spot buy uses best bid when bid is below computed target');
  context.bestFuturesAsk = 100100;
  await context.ccSpreadBotOrdersNetPlaceFuturesSell(1);
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '100100', 'SLF_SS futures first leg places at ask-side safe price');
  context.ccLastFuturesOpenOrders = [{ orderId: 'slf-futures-full-1', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100100', fillQty: '0.001' }];
  context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell').order = context.ccLastFuturesOpenOrders[0];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Filled', 'SLF_SS waits for futures sell fill');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L1_BuyLimitOrder_Placement', 'SLF_SS futures fill transitions to spot buy');
  context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy').order = { orderId: 'slf-spot-open-1', symbol: 'BTCUSDT', tradeSide: 'BUY', qty: '0.001', price: '99950', fillQty: '0' };
  context.ccLastSpotOpenOrders = [context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy').order];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_SLF_SS_L1_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L2_SellLimitOrder_Init', 'SLF_SS advances next futures sell after spot buy is placed, without waiting for spot fill');
  context.ccLastSpotOpenOrders[0].fillQty = '0.001';
  context.ccLastFuturesOpenOrders = [{ orderId: 'slf-futures-open-2', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100125', fillQty: '0' }];
  context.ccSpreadBotOrdersNetOrderSlot(2, 'futuresSell').order = context.ccLastFuturesOpenOrders[0];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_SLF_SS_L2_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Init', 'SLF_SS cancels current futures sell and returns after previous spot buy fills');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 2;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [];
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [];

  context.ccSpreadBotOrdersNetOrderSlot(2, 'spotBuy').order = { orderId: 's2', qty: '0.001', fillQty: '0.0004' };
  context.ccLastSpotOpenOrders = [context.ccSpreadBotOrdersNetOrderSlot(2, 'spotBuy').order];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L2_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L2_BuyLimitOrder_PartlyFilled', 'partial fill enters stub');

  const placedAt = Date.now();
  context.ccSpreadBotOrdersNetL1Mode = 'trail';
  context.ccSpreadBotOrdersNetL1TrailSizeUsd = '50';
  context.bestSpotBid = 62134;
  assert.strictEqual(context.ccSpreadBotOrdersNetLevelPricesForBranch(1, '', '50', 'SpreadBot_OrNet_LLF_SS')[0], 62100, 'L1 Trail buy rounds best bid down to 50');
  context.bestSpotBid = 62151;
  assert.strictEqual(context.ccSpreadBotOrdersNetLevelPricesForBranch(1, '', '50', 'SpreadBot_OrNet_LLF_SS')[0], 62150, 'L1 Trail buy rounds higher best bid down to 50');
  context.bestFuturesAsk = 62324;
  assert.strictEqual(context.ccSpreadBotOrdersNetLevelPricesForBranch(1, '', '50', 'SpreadBot_OrNet_SLF_SS')[0], 62350, 'L1 Trail sell rounds best ask up to 50');
  context.bestFuturesAsk = 62351;
  assert.strictEqual(context.ccSpreadBotOrdersNetLevelPricesForBranch(1, '', '50', 'SpreadBot_OrNet_SLF_SS')[0], 62400, 'L1 Trail sell rounds higher best ask up to 50');

  calls.length = 0;
  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.levelPrices = [62250];
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 1;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [];
  context.bestFuturesAsk = 62199;
  let futuresSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  futuresSlot.payload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '62250' };
  futuresSlot.order = { orderId: 'trail-futures-1', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '62250', fillQty: 0 };
  futuresSlot.firstPrice = 62250;
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(calls.some(c => c.cancelFutures), true, 'L1 Trail sell cancels old futures order before replace');
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '62200', 'L1 Trail sell replaces at rounded lower ask');
  assert.strictEqual(context.CC_SPREADBOT_ORDERS_NET.levelPrices[0], 62200, 'L1 Trail sell updates runtime L1 level');

  calls.length = 0;
  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.levelPrices = [62750];
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 1;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [];
  context.bestFuturesAsk = 62580;
  futuresSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  futuresSlot.payload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '62750' };
  futuresSlot.order = { orderId: 'trail-futures-10540', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '62750', fillQty: 0 };
  futuresSlot.firstPrice = 62750;
  const originalOrdersNetApi = context.ccSpreadBotApi;
  context.ccSpreadBotApi = (url, opt) => {
    if (url.includes('/futures/orders/place')) {
      calls.push({ url, payload: JSON.parse(opt.body), injectedError: '10540' });
      return Promise.reject(new Error('CoinCall API error 10540: Order has expired'));
    }
    return originalOrdersNetApi(url, opt);
  };
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(calls.some(c => c.cancelFutures), true, 'L1 Trail sell cancels old futures order before failed replace');
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_Placement_Failed', 'L1 Trail futures replace 10540 leaves Placed_Successfully state');
  assert.strictEqual(context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell').lastError.includes('10540'), true, 'L1 Trail futures replace stores 10540 for retry pricing');
  context.ccSpreadBotApi = originalOrdersNetApi;
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_SLF_SS_L1_SellLimitOrder_2ndAttempt', 'L1 Trail futures replace failure enters second attempt');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSD').at(-1).payload.price, '62630', 'L1 Trail futures 10540 retry uses MarkPrice + 50');

  calls.length = 0;
  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.levelPrices = [62100];
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [];
  context.bestSpotBid = 62151;
  let spotSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy');
  spotSlot.payload = { symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '62100' };
  spotSlot.order = { orderId: 'trail-spot-1', symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '62100', fillQty: 0 };
  spotSlot.firstPrice = 62100;
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(calls.some(c => c.cancelSpot), true, 'L1 Trail buy cancels old spot order before replace');
  assert.strictEqual(calls.filter(c => c.payload && c.payload.symbol === 'BTCUSDT').at(-1).payload.price, '62150', 'L1 Trail buy replaces at rounded higher bid');

  context.ccSpreadBotOrdersNetL1Mode = 'price';
  context.ccSpreadBotOrdersNetSpreadSizeUsd = '25';
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [];
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [];

  spotSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy');
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 2;
  context.CC_SPREADBOT_ORDERS_NET.levelPrices = [100000, 99900];
  spotSlot.payload = { symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '99950' };
  spotSlot.order = { orderId: 'spot-full-1', symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '99950', fillQty: 0 };
  spotSlot.placedAt = placedAt;
  spotSlot.fillQty = 0;
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [{ orderId: 'spot-full-1', symbol: 'BTCUSDT', side: 'BUY', price: '99940', qty: '0.001', ts: placedAt + 1000 }];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_Filled', 'spot buy disappeared from open orders but matched trade history full fill advances');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(JSON.stringify(context.CC_SPREADBOT_ORDERS_NET.levelPrices), JSON.stringify([99940, 99840]), 'LLF_SS rebuilds remaining level grid from real L1 spot fill price');
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Init', 'L1 fill still transitions to futures sell after regrid');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  spotSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy');
  spotSlot.payload = { symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '99950' };
  spotSlot.order = { orderId: 'spot-partial-1', symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '99950', fillQty: 0 };
  spotSlot.placedAt = placedAt;
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [{ orderId: 'spot-partial-1', symbol: 'BTCUSDT', side: 'BUY', price: '99950', qty: '0.0004', ts: placedAt + 1000 }];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_PartlyFilled', 'spot buy disappeared from open orders but matched partial trade history enters stub');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  spotSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'spotBuy');
  spotSlot.payload = { symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '99950' };
  spotSlot.order = { symbol: 'BTCUSDT', tradeSide: 'BUY', tradeType: 'POST_ONLY', qty: '0.001', price: '99950', fillQty: 0 };
  spotSlot.placedAt = placedAt;
  context.ccLastSpotOpenOrders = [];
  context.ccLastSpotTradeHistory = [{ symbol: 'BTCUSDT', side: 'BUY', price: '99950', qty: '0.001', ts: placedAt - 120000 }];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_BuyLimitOrder_Placed_Successfully', 'old unrelated shape-only trade history row does not trigger fill');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 1;
  futuresSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  futuresSlot.payload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025' };
  futuresSlot.order = { orderId: 'futures-full-1', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025', fillQty: 0 };
  futuresSlot.placedAt = placedAt;
  futuresSlot.fillQty = 0;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [{ orderId: 'futures-full-1', symbol: 'BTCUSD', side: 'SELL', price: '100025', qty: '0.001', ts: placedAt + 1000 }];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Filled', 'futures sell disappeared from open orders but matched trade history full fill advances');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  futuresSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  futuresSlot.payload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025' };
  futuresSlot.order = { orderId: 'futures-partial-1', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025', fillQty: 0 };
  futuresSlot.placedAt = placedAt;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [{ orderId: 'futures-partial-1', symbol: 'BTCUSD', side: 'SELL', price: '100025', qty: '0.0004', ts: placedAt + 1000 }];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_PartlyFilled', 'futures sell disappeared from open orders but matched partial trade history enters stub');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  futuresSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  futuresSlot.payload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025' };
  futuresSlot.order = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025', fillQty: 0 };
  futuresSlot.placedAt = placedAt;
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [{ symbol: 'BTCUSD', side: 'SELL', price: '100025', qty: '0.001', ts: placedAt - 120000 }];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Placed_Successfully', 'old unrelated futures shape-only trade history row does not trigger fill');

  context.CC_SPREADBOT_ORDERS_NET.orders = {};
  context.CC_SPREADBOT_ORDERS_NET.numLevels = 2;
  futuresSlot = context.ccSpreadBotOrdersNetOrderSlot(1, 'futuresSell');
  futuresSlot.payload = { symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025' };
  futuresSlot.order = { orderId: 'futures-prev-full-1', symbol: 'BTCUSD', tradeSide: '2', tradeType: '3', qty: '0.001', price: '100025', fillQty: 0 };
  futuresSlot.placedAt = placedAt;
  spotSlot = context.ccSpreadBotOrdersNetOrderSlot(2, 'spotBuy');
  spotSlot.order = { orderId: 'spot-l2-open', symbol: 'BTCUSDT', tradeSide: 'BUY', qty: '0.001', price: '99900', fillQty: 0 };
  context.ccLastFuturesOpenOrders = [];
  context.ccLastFuturesTradeHistory = [{ orderId: 'futures-prev-full-1', symbol: 'BTCUSD', side: 'SELL', price: '100025', qty: '0.001', ts: placedAt + 1000 }];
  context.ccLastSpotOpenOrders = [spotSlot.order];
  context.ccLastSpotTradeHistory = [];
  context.ccSpreadBotOrdersNetSetState('SpreadBot_OrNet_LLF_SS_L2_BuyLimitOrder_Placed_Successfully', 'test', 'test');
  await context.ccSpreadBotOrdersNetTick();
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L1_SellLimitOrder_Filled', 'previous futures sell trade-history full fill wins L2 race detection');

  context.ccSpreadBotOrdersNetOrderSlot(2, 'spotBuy').order = { orderId: 's2', qty: '0.001', fillQty: '0.001' };
  context.ccLastSpotOpenOrders = [context.ccSpreadBotOrdersNetOrderSlot(2, 'spotBuy').order];
  await context.ccSpreadBotOrdersNetCancelSpotAfterPreviousFutures(2);
  assert.strictEqual(context.ccSpreadBotOrdersNetState, 'SpreadBot_OrNet_LLF_SS_L2_BuyLimitOrder_Filled', 'cancellation race fill wins');

  assert(!impl.includes('CC_SPREADS_LEG_DETAILS_AUTOMATION.state ='), 'Orders Net block does not write Limit-Limit state');
  assert(calls.every(c => !c.url || c.url.startsWith('/api/admin/coincall/')), 'all order APIs are stubbed in harness');
  console.log('orders_net_regression: ok');
})().catch(err => { console.error(err); process.exit(1); });
