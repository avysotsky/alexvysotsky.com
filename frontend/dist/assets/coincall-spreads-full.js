
function cc(id){return document.getElementById(id)}
function ccOut(id,x){const el=cc(id); if(el) el.textContent=typeof x==='string'?x:JSON.stringify(x,null,2); if(typeof ccSpreadsLogStatusOutput==='function')ccSpreadsLogStatusOutput(id,x)}
function ccApiOk(res){return res&&String(res.code)==='0'}
function ccOrderResultSideLabel(raw){const v=String(raw??'').trim().toUpperCase();if(v==='1'||v==='BUY')return 'Buy';if(v==='2'||v==='SELL')return 'Sell';return String(raw??'').trim()}
function ccOrderResultText(action,res,ctx={}){if(ccApiOk(res)){const id=res.data&&typeof res.data==='object'?(res.data.orderId||res.data.clientOrderId||res.data.id||res.data.order):res.data;const side=ccOrderResultSideLabel(ctx.side),type=ccOrderTypeLabel(ctx.type);const parts=[ctx.symbol,side,type,ctx.qty||'',ctx.price?('@ '+ctx.price):''].filter(x=>x&&x!=='—').join(' ');return action+' successful'+(parts?': '+parts:'')+(id?' · order '+id:'')}const msg=res&&(res.msg||res.message);return action+' failed'+(msg?': '+msg:'')}
function ccEsc(s){return String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
const CC_TOKEN_KEY='van_token';const CC_SESSION_KEY='van_session';const CC_UI_STATE_KEY='cc_ui_state_v1';function ccToken(){const direct=localStorage.getItem(CC_TOKEN_KEY);if(direct)return direct;try{const raw=localStorage.getItem(CC_SESSION_KEY);if(!raw)return '';const s=JSON.parse(raw);return s?.token||s?.Token||'';}catch{return ''}}function ccParseCoincallApiText(text){const safe=String(text??'').replace(/("(?:orderId|clientOrderId|transferId|recordId|id|oid|coid|ordId|order_id|orderNo|orderNumber|clientOid|clOrdId|positionId|tradeId|data)"\s*:\s*)(-?\d{16,})(\s*[,}])/g,'$1"$2"$3');try{return JSON.parse(safe)}catch{return text}}async function ccApi(url,opt={}){const t=ccToken();const headers=Object.assign(t?{'Authorization':'Bearer '+t}:{},opt.body?{'Content-Type':'application/json'}:{},opt.headers||{});const r=await fetch(url,{...opt,headers});const text=await r.text();const j=ccParseCoincallApiText(text);if(r.status===401||r.status===403){location.replace('/app?logout=1');throw new Error('Unauthorized')}if(!r.ok)throw new Error((j&&j.message)||text||r.statusText||('HTTP '+r.status));return j}function ccUiStateLoad(){try{const raw=localStorage.getItem(CC_UI_STATE_KEY);if(!raw)return {};const parsed=JSON.parse(raw);return parsed&&typeof parsed==='object'?parsed:{}}catch{return {}}}function ccUiStateSave(patch){try{const next={...ccUiStateLoad(),...(patch&&typeof patch==='object'?patch:{})};localStorage.setItem(CC_UI_STATE_KEY,JSON.stringify(next));return next}catch{return null}}function ccUiStateOptionExists(id,value){const sel=cc(id);const want=String(value||'').trim();return !!(sel&&want&&Array.from(sel.options||[]).some(opt=>String(opt.value||'').trim()===want))}function ccUiStateApplySelect(id,value){if(ccUiStateOptionExists(id,value))cc(id).value=String(value).trim()}function ccActiveLayer(){return document.querySelector('[data-cc-layer].active')?.dataset?.ccLayer||'trade'}function ccActiveAssetTab(){return document.querySelector('[data-cc-asset-tab].active')?.dataset?.ccAssetTab||'overview'}function ccHideTradeOnlyPanels(){for(const id of ['ccSpotBottomPanel','ccFuturesBottomPanel']){const el=cc(id);if(el){el.hidden=true;el.classList.remove('active')}}const toolbar=document.querySelector('.cc-futures-contract-toolbar');if(toolbar)toolbar.hidden=true}function ccApplyLayerState(layer){const target=String(layer||'trade').trim();document.querySelectorAll('[data-cc-layer]').forEach(x=>x.classList.toggle('active',x.dataset.ccLayer===target));document.querySelectorAll('.okx-layer-panel').forEach(x=>x.classList.toggle('active',x.id==='cc-layer-'+target));if(target!=='trade')ccHideTradeOnlyPanels();else ccApplyTradeState(ccTradeActiveMode())}function ccApplyTradeState(trade){const target=String(trade||'futures').trim();const marketData=target==='market-data';const spreads=target==='spreads';const options=target==='options';document.querySelectorAll('[data-cc-trade]').forEach(x=>x.classList.toggle('active',x.dataset.ccTrade===target));const spot=!marketData&&!spreads&&!options&&target!=='futures';cc('ccMarketDataPanel')?.classList.toggle('active',marketData);if(cc('ccSpreadsPanel')){cc('ccSpreadsPanel').hidden=!spreads;cc('ccSpreadsPanel').classList.toggle('active',spreads)}if(cc('ccOptionsPanel')){cc('ccOptionsPanel').hidden=!options;cc('ccOptionsPanel').classList.toggle('active',options)}cc('ccSpotShell').hidden=!spot;cc('ccSpotBottomPanel').hidden=!spot;cc('ccFuturesShell').hidden=spot||marketData||spreads||options;cc('ccFuturesBottomPanel').hidden=spot||marketData||spreads||options;cc('ccSpotShell')?.classList.toggle('active',spot);cc('ccSpotBottomPanel')?.classList.toggle('active',spot);cc('ccFuturesShell')?.classList.toggle('active',!spot&&!marketData&&!spreads&&!options);cc('ccFuturesBottomPanel')?.classList.toggle('active',!spot&&!marketData&&!spreads&&!options);cc('cc-layer-trade')?.classList.toggle('cc-spreads-active',spreads);cc('cc-layer-trade')?.classList.toggle('cc-options-active',options);const toolbar=document.querySelector('.cc-futures-contract-toolbar');if(toolbar)toolbar.hidden=target!=='futures';if(options)ccLoadOptionsExpirations().catch(()=>{})}function ccApplyAssetTabState(tab){const target=String(tab||'overview').trim();document.querySelectorAll('[data-cc-asset-tab]').forEach(x=>x.classList.toggle('active',x.dataset.ccAssetTab===target));document.querySelectorAll('#cc-layer-assets > .tab-panel').forEach(x=>x.classList.toggle('active',x.id==='cc-tab-'+target))}function ccRestoreUiState(){const state=ccUiStateLoad();const layer=state.layer||'trade';ccUiStateApplySelect('ccSpotInst',state.spotSymbol);ccUiStateApplySelect('ccFuturesInst',state.futuresSymbol);ccApplyLayerState(layer);if(layer==='trade')ccApplyTradeState(state.trade||'futures');else ccHideTradeOnlyPanels();ccApplyAssetTabState(state.assetTab||'overview');return state}function ccCoincallSignStripNulls(v){if(v!==null&&v!==undefined){if(Array.isArray(v))return v.filter(x=>x!==null&&x!==undefined).map(ccCoincallSignStripNulls);if(typeof v==='object'){const out={};for(const k in v){const x=v[k];if(x!==null&&x!==undefined)out[k]=ccCoincallSignStripNulls(x)}return out}return v}}function ccCoincallSignNormalize(params){const out={...params};for(const k in out){const v=out[k];if(v===undefined||v==='')out[k]=null;else if(v!==null&&typeof v==='object')out[k]=JSON.stringify(ccCoincallSignStripNulls(v))}return out}function ccCoincallSignedQuery(params){const out=[];const src=ccCoincallSignNormalize(params||{});for(const k of Object.keys(src).sort((a,b)=>a.localeCompare(b))){const v=src[k];if(v===null||v===undefined)continue;out.push(k+'='+String(v))}return out.join('&')}async function ccHmacSha256Hex(message,key){const enc=new TextEncoder();const cryptoKey=await crypto.subtle.importKey('raw',enc.encode(key),{name:'HMAC',hash:'SHA-256'},false,['sign']);const sig=await crypto.subtle.sign('HMAC',cryptoKey,enc.encode(message));return Array.from(new Uint8Array(sig)).map(b=>b.toString(16).padStart(2,'0')).join('')}async function ccCoincallBrowserAuthStart(){const r=await fetch('https://www.coincall.com/auth/start/v1',{method:'GET',credentials:'include',headers:{'Accept':'application/json, text/plain, */*'}});const text=await r.text();const j=ccParseCoincallApiText(text);if(!r.ok)throw new Error((j&&j.message)||text||r.statusText||('HTTP '+r.status));if(j&&j.code!==undefined&&String(j.code)!=='0')throw new Error((j&&j.msg)||('CoinCall auth/start failed '+j.code));const d=j&&j.data!==undefined?j.data:j;const serverTs=Number(d&&d.serverTs||j&&j.serverTs||0);return {uuid:String(d&&d.uuid||''),key:String(d&&d.key||''),token:String(d&&d.token||''),subauth:String(d&&((d.subauth!==undefined?d.subauth:d.subAuth)!==undefined?(d.subauth!==undefined?d.subauth:d.subAuth):d.subToken)||''),tsdiff:String(serverTs?Date.now()-serverTs:Number(d&&d.tsdiff||j&&j.tsdiff||0)||0)}}async function ccCoincallBrowserApi(uri,opt={}){if(!uri||uri[0]!=='/')throw new Error('CoinCall browser API uri must start with /');const auth=await ccCoincallBrowserAuthStart();if(!auth.uuid||!auth.key||!auth.token)throw new Error('CoinCall browser session auth is unavailable');const method=String(opt.method||'GET').toUpperCase();const params=Object.assign({},opt.params||{},opt.body&&typeof opt.body==='object'?opt.body:{});const ts=Date.now();const signed=ccCoincallSignedQuery(params);const suffix='uuid='+auth.uuid+'&ts='+ts+'&tsdiff='+auth.tsdiff;const prehash=method+uri+'?'+(signed?signed+'&':'')+suffix;const sign=await ccHmacSha256Hex(prehash,auth.key);const headers={'Accept':'application/json, text/plain, */*','Authorization':'Bearer '+auth.token,'uuid':auth.uuid,'ts':String(ts),'tsdiff':String(auth.tsdiff),'key':auth.key,'source':'PC','signType':'HMAC-SHA256','sign':sign};if(auth.subauth)headers['SubAuth']='Bearer '+auth.subauth;const r=await fetch('https://www.coincall.com/api'+uri,{method,mode:'cors',credentials:'include',headers,body:opt.rawBody!==undefined?opt.rawBody:(opt.body?JSON.stringify(opt.body):undefined)});const text=await r.text();const j=ccParseCoincallApiText(text);if(!r.ok)throw new Error((j&&j.message)||text||r.statusText||('HTTP '+r.status));if(j&&j.code!==undefined&&String(j.code)!=='0')throw new Error((j&&j.msg)||('CoinCall API error '+j.code));return j}async function ccCancelFuturesOrderViaCoincallBrowser(payload){const orderId=payload&&payload.orderId?String(payload.orderId).trim():'';if(!orderId)throw new Error('CoinCall browser cancel requires orderId');return await ccCoincallBrowserApi('/futures/trade/order/cancel/v1/'+encodeURIComponent(orderId),{method:'POST'})}
function ccTransferRecordsPickArray(res){if(Array.isArray(res))return res;if(!res||typeof res!=='object')return[];const candidates=[res.data,res.rows,res.items,res.list,res.data&&res.data.rows,res.data&&res.data.items,res.data&&res.data.list,res.data&&res.data?.list,res.data&&res.data?.rows,res.data&&res.data?.items];for(const candidate of candidates){if(Array.isArray(candidate))return candidate}return[]}
function ccTransferRecordsValue(v){if(v===null||v===undefined)return '—';if(typeof v==='string')return v;if(typeof v==='number'||typeof v==='boolean')return String(v);return JSON.stringify(v,null,2)}
function ccTransferRecordsRenderTable(rows){if(!rows.length)return'<div class="okx-nitro-empty-state">No transfer rows returned.</div>';const keys=Array.from(new Set(rows.flatMap(row=>row&&typeof row==='object'&&!Array.isArray(row)?Object.keys(row):[])));if(!keys.length)return'<div class="okx-nitro-empty-state">Transfer records response is not tabular.</div>';return'<div class="table-wrap"><table><thead><tr>'+keys.map(k=>'<th>'+ccEsc(k)+'</th>').join('')+'</tr></thead><tbody>'+rows.map(row=>'<tr>'+keys.map(k=>'<td>'+ccEsc(ccTransferRecordsValue(row&&row[k]))+'</td>').join('')+'</tr>').join('')+'</tbody></table></div>'}
function ccTransferRecordsRenderTopLevel(res){if(!res||typeof res!=='object'||Array.isArray(res))return'';return'<div class="table-wrap"><table><thead><tr><th>Field</th><th>Value</th></tr></thead><tbody>'+Object.entries(res).map(([k,v])=>'<tr><td>'+ccEsc(k)+'</td><td>'+ccEsc(ccTransferRecordsValue(v))+'</td></tr>').join('')+'</tbody></table></div>'}
function ccRenderTransferRecords(res){const meta=cc('ccTransferRecordsMeta');const body=cc('ccTransferRecordsBody');const out=cc('ccTransferRecordsOut');if(out)out.textContent=JSON.stringify(res,null,2);const rows=ccTransferRecordsPickArray(res);if(meta)meta.textContent=rows.length?('Loaded '+rows.length+' transfer row'+(rows.length===1?'':'s')+'.'):'Loaded transfer response.';if(body)body.innerHTML=ccTransferRecordsRenderTopLevel(res)+(rows.length?'<div style="display:flex;flex-direction:column;gap:8px">'+ccTransferRecordsRenderTable(rows)+'</div>':'');}
async function loadCcTransferRecords(){try{const res=await ccApi('/api/admin/coincall/transfer-records');ccRenderTransferRecords(res)}catch(e){const meta=cc('ccTransferRecordsMeta');if(meta)meta.textContent='Transfer records failed: '+e.message;ccOut('ccTransferRecordsOut','Transfer records failed: '+e.message);const body=cc('ccTransferRecordsBody');if(body)body.innerHTML='<div class="okx-nitro-empty-state">Transfer records failed: '+ccEsc(e.message)+'</div>'}}
const CC_OPTIONS_STATE={asset:'BTC',expiry:'',items:[],quoteRows:[],quoteError:null,underlying:null,loaded:false,loading:null,chainLoading:null};
function ccSetHidden(id,hidden){const el=cc(id);if(el)el.hidden=!!hidden}
function ccApplyTradeState(trade){const target=String(trade||'futures').trim();const marketData=target==='market-data';const spreads=target==='spreads';const options=target==='options';document.querySelectorAll('[data-cc-trade]').forEach(x=>x.classList.toggle('active',x.dataset.ccTrade===target));const spot=!marketData&&!spreads&&!options&&target!=='futures';cc('ccMarketDataPanel')?.classList.toggle('active',marketData);if(cc('ccSpreadsPanel')){cc('ccSpreadsPanel').hidden=!spreads;cc('ccSpreadsPanel').classList.toggle('active',spreads)}if(cc('ccOptionsPanel')){cc('ccOptionsPanel').hidden=!options;cc('ccOptionsPanel').classList.toggle('active',options)}ccSetHidden('ccSpotShell',!spot);ccSetHidden('ccSpotBottomPanel',!spot);ccSetHidden('ccFuturesShell',spot||marketData||spreads||options);ccSetHidden('ccFuturesBottomPanel',spot||marketData||spreads||options);cc('ccSpotShell')?.classList.toggle('active',spot);cc('ccSpotBottomPanel')?.classList.toggle('active',spot);cc('ccFuturesShell')?.classList.toggle('active',!spot&&!marketData&&!spreads&&!options);cc('ccFuturesBottomPanel')?.classList.toggle('active',!spot&&!marketData&&!spreads&&!options);cc('cc-layer-trade')?.classList.toggle('cc-spreads-active',spreads);cc('cc-layer-trade')?.classList.toggle('cc-options-active',options);const toolbar=document.querySelector('.cc-futures-contract-toolbar');if(toolbar)toolbar.hidden=target!=='futures';if(options)ccLoadOptionsExpirations().catch(()=>{})}
function ccOptionsExpiryLabel(ts){const d=new Date(Number(ts));if(Number.isNaN(d.getTime()))return '';const months=['JAN','FEB','MAR','APR','MAY','JUN','JUL','AUG','SEP','OCT','NOV','DEC'];return String(d.getUTCDate()).padStart(2,'0')+months[d.getUTCMonth()]+String(d.getUTCFullYear()).slice(-2)}
function ccOptionsExpirySortValue(label){const row=(CC_OPTIONS_STATE.items||[]).find(x=>ccOptionsExpiryLabel(x.expirationTimestamp)===label);return Number(row&&row.expirationTimestamp)||0}
function ccOptionsRows(raw){const rows=raw&&raw.data!==undefined?raw.data:(raw&&raw.items!==undefined?raw.items:raw);return Array.isArray(rows)?rows.filter(x=>x&&String(x.baseCurrency||'').toUpperCase()===CC_OPTIONS_STATE.asset&&x.isActive!==false):[]}
async function ccOptionsLoadUnderlying(asset){try{const symbol=asset==='ETH'?'ETHUSDT':'BTCUSDT';const r=await ccApi('/api/admin/coincall/public/spot/klines?symbol='+encodeURIComponent(symbol)+'&tf=1m');const rows=Array.isArray(r&&r.data)?r.data:[];for(let i=rows.length-1;i>=0;i--){const n=ccNum(rows[i]&&rows[i].close);if(n!==null)return n}}catch(e){}return null}
async function ccLoadOptionsExpirations(){const panel=cc('ccOptionsPanel');if(!panel||panel.hidden)return;if(CC_OPTIONS_STATE.loading)return CC_OPTIONS_STATE.loading;const bar=cc('ccOptionsExpiryBar'),wrap=cc('ccOptionsChainWrap'),meta=cc('ccOptionsMeta');if(bar)bar.innerHTML='<span class="spinner"></span>';if(wrap)wrap.innerHTML='<div class="no-data"><span class="spinner"></span> Loading chain...</div>';if(meta)meta.textContent='Loading...';CC_OPTIONS_STATE.loading=(async()=>{try{const asset=CC_OPTIONS_STATE.asset;const raw=await ccApi('/api/admin/coincall/public/options/instruments?baseCurrency='+encodeURIComponent(asset));const items=ccOptionsRows(raw);CC_OPTIONS_STATE.items=items;CC_OPTIONS_STATE.underlying=await ccOptionsLoadUnderlying(asset);const exps=Array.from(new Set(items.map(x=>ccOptionsExpiryLabel(x.expirationTimestamp)).filter(Boolean))).sort((a,b)=>ccOptionsExpirySortValue(a)-ccOptionsExpirySortValue(b));if(!exps.includes(CC_OPTIONS_STATE.expiry))CC_OPTIONS_STATE.expiry=exps[0]||'';if(bar)bar.innerHTML=exps.length?exps.map(exp=>'<button type="button" class="cc-options-expiry-btn'+(exp===CC_OPTIONS_STATE.expiry?' active':'')+'" data-cc-options-expiry="'+ccEsc(exp)+'">'+ccEsc(exp)+'</button>').join(''):'<span class="cc-options-val-dim">No expirations</span>';await ccLoadOptionsChain()}catch(e){if(bar)bar.innerHTML='<span style="color:#f87171;font-size:12px">Error: '+ccEsc(e.message||e)+'</span>';if(wrap)wrap.innerHTML='<div class="no-data">Error: '+ccEsc(e.message||e)+'</div>';if(meta)meta.textContent='CoinCall options unavailable'}finally{CC_OPTIONS_STATE.loading=null}})();return CC_OPTIONS_STATE.loading}
function ccOptionsLeg(row,side){return row&&row[side]?row[side]:null}
function ccOptionsDash(){return '<span class="cc-options-val-dim">-</span>'}
function ccOptionsRealMeta(leg){if(!leg)return ccOptionsDash();const parts=[];if(leg.minQty!==undefined&&leg.minQty!==null)parts.push('Min '+ccEsc(leg.minQty));if(leg.tickSize!==undefined&&leg.tickSize!==null)parts.push('Tick '+ccEsc(leg.tickSize));return parts.length?parts.join(' / '):ccOptionsDash()}
function ccOptionsChainRows(raw){const rows=raw&&raw.data!==undefined?raw.data:(raw&&raw.items!==undefined?raw.items:raw);return Array.isArray(rows)?rows:[]}
function ccOptionsEndTimeForExpiry(expiry){const row=(CC_OPTIONS_STATE.items||[]).find(x=>ccOptionsExpiryLabel(x.expirationTimestamp)===expiry);const n=Number(row&&row.expirationTimestamp);return Number.isFinite(n)&&n>0?n:null}
async function ccLoadOptionsChain(){const expiry=CC_OPTIONS_STATE.expiry;if(!expiry){CC_OPTIONS_STATE.quoteRows=[];ccRenderOptionsChain();return}if(CC_OPTIONS_STATE.chainLoading)return CC_OPTIONS_STATE.chainLoading;const wrap=cc('ccOptionsChainWrap');if(wrap)wrap.innerHTML='<div class="no-data"><span class="spinner"></span> Loading live option prices...</div>';CC_OPTIONS_STATE.chainLoading=(async()=>{try{const asset=CC_OPTIONS_STATE.asset;const endTime=ccOptionsEndTimeForExpiry(expiry);const qs=new URLSearchParams({baseCurrency:asset});if(endTime)qs.set('endTime',String(endTime));const raw=await ccApi('/api/admin/coincall/public/options/chain?'+qs.toString());CC_OPTIONS_STATE.quoteRows=ccOptionsChainRows(raw);CC_OPTIONS_STATE.quoteError=null;const liveUnderlying=ccOptionsFirstFinite(CC_OPTIONS_STATE.quoteRows.flatMap(r=>[r.callOption&&r.callOption.underlyingPrice,r.putOption&&r.putOption.underlyingPrice]));if(Number.isFinite(liveUnderlying))CC_OPTIONS_STATE.underlying=liveUnderlying}catch(e){CC_OPTIONS_STATE.quoteRows=[];CC_OPTIONS_STATE.quoteError=e}finally{CC_OPTIONS_STATE.chainLoading=null;ccRenderOptionsChain()}})();return CC_OPTIONS_STATE.chainLoading}
function ccOptionsFirstFinite(values){for(const value of values){const n=Number(value);if(Number.isFinite(n))return n}return NaN}
function ccOptionsSymbol(leg){return String(leg&&((leg.symbol!==undefined?leg.symbol:leg.symbolName)||leg.displayName)||'').trim()}
function ccOptionsMergeLeg(instrument,quote){return {...(instrument||{}),...(quote||{}),symbolName:ccOptionsSymbol(quote)||ccOptionsSymbol(instrument)}}
function ccOptionsBuildChain(expiry){const byStrike=new Map();const putLeg=(strike,side,leg)=>{if(strike===null)return;const key=String(strike);const row=byStrike.get(key)||{strike};row[side]=ccOptionsMergeLeg(row[side],leg);byStrike.set(key,row)};(CC_OPTIONS_STATE.items||[]).filter(x=>ccOptionsExpiryLabel(x.expirationTimestamp)===expiry).forEach(x=>{const strike=ccNum(x.strike);const side=String(x.symbolName||x.symbol||'').endsWith('-P')?'put':'call';putLeg(strike,side,x)});(CC_OPTIONS_STATE.quoteRows||[]).forEach(x=>{const strike=ccNum(x.strike);if(x.callOption)putLeg(strike,'call',x.callOption);if(x.putOption)putLeg(strike,'put',x.putOption)});return Array.from(byStrike.values()).sort((a,b)=>a.strike-b.strike)}
function ccOptionsNumText(v,digits=2){const n=Number(v);return Number.isFinite(n)?ccEsc(ccFmtDynamic?ccFmtDynamic(n):n.toLocaleString(undefined,{maximumFractionDigits:digits})):ccOptionsDash()}
function ccOptionsIvText(v){const n=Number(v);if(!Number.isFinite(n))return ccOptionsDash();const pct=Math.abs(n)<=5?n*100:n;return ccEsc(ccFmtNumber(pct,2)+'%')}
function ccOptionsCell(leg,keys,kind){if(!leg)return ccOptionsDash();for(const key of keys){if(leg[key]!==undefined&&leg[key]!==null){if(kind==='iv')return ccOptionsIvText(leg[key]);return ccOptionsNumText(leg[key],kind==='greek'?4:2)}}return ccOptionsDash()}
function ccOptionsLegCells(leg,side){const cls=side==='call'?'call-side':'put-side';const cell=(html)=>'<td class="'+cls+'">'+html+'</td>';const values=side==='call'?[['openInterest'],['volume','volume24h'],['bidIv'],['markIv','iv'],['lastPrice','markPrice'],['bid'],['ask'],['delta']]:[['delta'],['bid'],['ask'],['lastPrice','markPrice'],['markIv','iv'],['bidIv'],['volume','volume24h'],['openInterest']];return values.map(keys=>cell(ccOptionsCell(leg,keys,keys.some(k=>/iv/i.test(k))?'iv':(keys.includes('delta')?'greek':'num')))).join('')}
function ccRenderOptionsChain(){const wrap=cc('ccOptionsChainWrap'),meta=cc('ccOptionsMeta');if(!wrap)return;const expiry=CC_OPTIONS_STATE.expiry;const chain=ccOptionsBuildChain(expiry);let atmStrike=null;if(CC_OPTIONS_STATE.underlying!==null&&chain.length){let best=Infinity;chain.forEach(row=>{const d=Math.abs(row.strike-CC_OPTIONS_STATE.underlying);if(d<best){best=d;atmStrike=row.strike}})}if(meta){const und=CC_OPTIONS_STATE.underlying!==null?'$'+Math.round(CC_OPTIONS_STATE.underlying).toLocaleString():'-';const src=CC_OPTIONS_STATE.quoteError?'CoinCall chain failed: '+(CC_OPTIONS_STATE.quoteError.message||CC_OPTIONS_STATE.quoteError):'CoinCall option chain';meta.textContent=CC_OPTIONS_STATE.asset+' · '+(expiry||'-')+' · Underlying: '+und+' · '+chain.length+' strikes · '+src}if(!chain.length){wrap.innerHTML='<div class="no-data">'+(CC_OPTIONS_STATE.quoteError?'CoinCall option chain failed: '+ccEsc(CC_OPTIONS_STATE.quoteError.message||CC_OPTIONS_STATE.quoteError):'No CoinCall option instruments')+'</div>';return}let html='<table class="cc-options-chain"><thead><tr><th class="call-hdr" colspan="8">CALLS</th><th class="strike-hdr">STRIKE</th><th class="put-hdr" colspan="8">PUTS</th></tr><tr><th class="call-hdr">OI</th><th class="call-hdr">Vol</th><th class="call-hdr">Bid IV</th><th class="call-hdr">Mark IV</th><th class="call-hdr">Last</th><th class="call-hdr">Bid</th><th class="call-hdr">Ask</th><th class="call-hdr">Delta</th><th class="strike-hdr">-</th><th class="put-hdr">Delta</th><th class="put-hdr">Bid</th><th class="put-hdr">Ask</th><th class="put-hdr">Last</th><th class="put-hdr">Mark IV</th><th class="put-hdr">Bid IV</th><th class="put-hdr">Vol</th><th class="put-hdr">OI</th></tr></thead><tbody>';chain.forEach(row=>{const c=ccOptionsLeg(row,'call'),p=ccOptionsLeg(row,'put');const cls='clickable'+(row.strike===atmStrike?' atm':'');const title=[ccOptionsSymbol(c),ccOptionsSymbol(p)].filter(Boolean).join(' / ');html+='<tr class="'+cls+'" data-cc-options-strike="'+ccEsc(row.strike)+'" title="'+ccEsc(title)+'">'+ccOptionsLegCells(c,'call')+'<td class="strike-cell">'+ccEsc(row.strike.toLocaleString())+'</td>'+ccOptionsLegCells(p,'put')+'</tr>'});html+='</tbody></table>';wrap.innerHTML=html;const atm=wrap.querySelector('tr.atm');if(atm)atm.scrollIntoView({block:'center',behavior:'smooth'})}
function ccSelectOptionsStrike(tr){if(!tr)return;document.querySelectorAll('#ccOptionsChainWrap tr.selected').forEach(r=>r.classList.remove('selected'));tr.classList.add('selected');const strike=ccNum(tr.dataset.ccOptionsStrike);const row=ccOptionsBuildChain(CC_OPTIONS_STATE.expiry).find(x=>ccNum(x.strike)===strike)||{};const call=ccOptionsLeg(row,'call'),put=ccOptionsLeg(row,'put');const metaPart=x=>x?('Last '+ccOptionsCell(x,['lastPrice','markPrice'],'num').replace(/<[^>]*>/g,'-')+' · Vol '+ccOptionsCell(x,['volume','volume24h'],'num').replace(/<[^>]*>/g,'-')+' · OI '+ccOptionsCell(x,['openInterest'],'num').replace(/<[^>]*>/g,'-')):'-';const box=cc('ccOptionsSelected');if(box)box.style.display='block';cc('ccOptionsSelectedLabel').textContent=CC_OPTIONS_STATE.asset+' '+CC_OPTIONS_STATE.expiry+' '+(strike!==null?strike.toLocaleString():'-');cc('ccOptionsSelectedMeta').textContent='Call: '+(ccOptionsSymbol(call)||'-')+' · '+metaPart(call)+' · Put: '+(ccOptionsSymbol(put)||'-')+' · '+metaPart(put)}
document.addEventListener('click',function(e){const tradeBtn=e.target.closest&&e.target.closest('[data-cc-trade="options"]');if(tradeBtn){e.preventDefault();e.stopPropagation();e.stopImmediatePropagation();ccApplyTradeState('options');ccUiStateSave({trade:'options'});try{ccTradeSyncTopControls()}catch(_){}try{ccStopBookRefresh('spot');ccStopBookRefresh('futures')}catch(_){}ccLoadOptionsExpirations().catch(()=>{});return}const assetBtn=e.target.closest&&e.target.closest('[data-cc-options-asset]');if(assetBtn){CC_OPTIONS_STATE.asset=String(assetBtn.dataset.ccOptionsAsset||'BTC').toUpperCase();CC_OPTIONS_STATE.expiry='';CC_OPTIONS_STATE.quoteRows=[];document.querySelectorAll('[data-cc-options-asset]').forEach(b=>{const active=b===assetBtn;b.classList.toggle('active',active);b.setAttribute('aria-selected',active?'true':'false')});cc('ccOptionsSelected')&&(cc('ccOptionsSelected').style.display='none');ccLoadOptionsExpirations().catch(()=>{});return}const expiryBtn=e.target.closest&&e.target.closest('[data-cc-options-expiry]');if(expiryBtn){CC_OPTIONS_STATE.expiry=String(expiryBtn.dataset.ccOptionsExpiry||'');CC_OPTIONS_STATE.quoteRows=[];document.querySelectorAll('[data-cc-options-expiry]').forEach(b=>b.classList.toggle('active',b===expiryBtn));cc('ccOptionsSelected')&&(cc('ccOptionsSelected').style.display='none');ccLoadOptionsChain().catch(()=>{});return}const row=e.target.closest&&e.target.closest('#ccOptionsChainWrap tr[data-cc-options-strike]');if(row)ccSelectOptionsStrike(row)},true);
const CC_INTERVAL={spot:{'1m':'1min','5m':'5min','15m':'15min','1H':'60min','4H':'4hour','1D':'1day'},futures:{'1m':'m1','5m':'m5','15m':'m15','1H':'h1','4H':'h4','1D':'d1'}};
const CC_INTERVAL_MS={'1m':60000,'5m':300000,'15m':900000,'1H':3600000,'4H':14400000,'1D':86400000};
const CC_BOOK_DEPTH=7,CC_BOOK_FETCH_DEPTH=10,CC_FUTURES_BOOK_FETCH_DEPTH=150,CC_BOOK_REFRESH_MS=1000,CC_BOOK_STALE_MS=15000,CC_BOOK_RECONNECT_MS=1500,CC_SPOT_BOOK_LEVEL=50,CC_FUTURES_BOOK_STEP='step0';
const CC_BOOK_STATE={active:'spot',timers:{spot:null,futures:null},top:{spot:{ask:NaN,bid:NaN},futures:{ask:NaN,bid:NaN}},requestSeq:{spot:0,futures:0},raw:{spot:null,futures:null},transport:{spot:'',futures:''},display:{spot:'',futures:''},sockets:{spot:null,futures:null},socketSymbol:{spot:'',futures:''},reconnect:{spot:null,futures:null},stale:{spot:null,futures:null},heartbeat:{spot:null,futures:null},wantReconnect:{spot:false,futures:false},connectSeq:{spot:0,futures:0},spotCache:{channel:'',asks:new Map(),bids:new Map()},spotLastTrade:{symbol:'',price:NaN,side:'',ts:0},futuresLastTrade:{symbol:'',price:NaN,side:'',ts:0},futuresMidTrack:{symbol:'',price:NaN,side:''},expectSnapshot:{spot:false,futures:false},lastMessageTs:{spot:0,futures:0},futuresAuth:{url:'',expiresAt:0,promise:null}};
const CC_BIDASK_FILTER_WINDOW=20;
const CC_BIDASK_FILTER_MAX_REL_DEV=0.01;
const CC_BIDASK_FILTER_STATE={};
function ccBidAskFilterAverage(values){
  const rows=(Array.isArray(values)?values:[]).map(Number).filter(v=>Number.isFinite(v)&&v>0);
  return rows.length?rows.reduce((sum,v)=>sum+v,0)/rows.length:NaN;
}
function ccBidAskFilterEntry(key,side){
  const mapKey=String(key||'default')+'::'+String(side||'');
  if(!CC_BIDASK_FILTER_STATE[mapKey])CC_BIDASK_FILTER_STATE[mapKey]={values:[],lastAccepted:NaN};
  return CC_BIDASK_FILTER_STATE[mapKey];
}
function ccBidAskFilterPush(entry,value){
  entry.values.push(value);
  if(entry.values.length>CC_BIDASK_FILTER_WINDOW)entry.values=entry.values.slice(-CC_BIDASK_FILTER_WINDOW);
}
function ccBidAskFilteredValue(key,side,raw){
  const value=Number(raw);
  if(!Number.isFinite(value)||value<=0)return NaN;
  const entry=ccBidAskFilterEntry(key,side);
  if(entry.values.length<CC_BIDASK_FILTER_WINDOW){
    ccBidAskFilterPush(entry,value);
    entry.lastAccepted=value;
    return entry.values.length===CC_BIDASK_FILTER_WINDOW?value:NaN;
  }
  const average=ccBidAskFilterAverage(entry.values);
  ccBidAskFilterPush(entry,value);
  if(!Number.isFinite(average)||average<=0)return NaN;
  const maxDev=Math.max(Math.abs(average)*CC_BIDASK_FILTER_MAX_REL_DEV,1e-8);
  const sideKey=String(side||'').toLowerCase();
  const outlier=sideKey==='bid'?value<average-maxDev:(sideKey==='ask'?value>average+maxDev:Math.abs(value-average)>maxDev);
  if(outlier){
    return Number.isFinite(entry.lastAccepted)&&entry.lastAccepted>0?entry.lastAccepted:NaN;
  }
  entry.lastAccepted=value;
  return value;
}
function ccFilteredBidAskMedian(key,bidRaw,askRaw){
  const bid=ccBidAskFilteredValue(key,'bid',bidRaw);
  const ask=ccBidAskFilteredValue(key,'ask',askRaw);
  return Number.isFinite(bid)&&bid>0&&Number.isFinite(ask)&&ask>0?(bid+ask)/2:NaN;
}
const CC_WS_CHART_CACHE_KEY='cc_ws_chart_samples_v5';
const CC_DB_CHART_CACHE_KEY='cc_db_chart_minutes_v1';
function ccNum(v){const n=Number(v);return Number.isFinite(n)?n:null}
function ccFmt(v,d=2){const n=Number(v);return Number.isFinite(n)?n.toLocaleString(undefined,{maximumFractionDigits:d}):'—'}
function ccUtcDate(v){const d=new Date(v);return Number.isNaN(d.getTime())?'—':d.getUTCFullYear()+'-'+String(d.getUTCMonth()+1).padStart(2,'0')+'-'+String(d.getUTCDate()).padStart(2,'0')}
function ccUtcHm(v){const d=new Date(v);return Number.isNaN(d.getTime())?'—':String(d.getUTCHours()).padStart(2,'0')+':'+String(d.getUTCMinutes()).padStart(2,'0')}
function ccUtcHms(v){const d=new Date(v);return Number.isNaN(d.getTime())?'—':String(d.getUTCHours()).padStart(2,'0')+':'+String(d.getUTCMinutes()).padStart(2,'0')+':'+String(d.getUTCSeconds()).padStart(2,'0')}
function ccUtcMdHms(v){const d=new Date(v);return Number.isNaN(d.getTime())?'—':String(d.getUTCMonth()+1).padStart(2,'0')+'-'+String(d.getUTCDate()).padStart(2,'0')+' '+ccUtcHms(d)+' UTC'}
function ccUtcDateTime(v){const d=new Date(v);return Number.isNaN(d.getTime())?'—':ccUtcDate(d)+' '+ccUtcHms(d)+' UTC'}
function ccChartRows(raw,market){const rows=raw&&raw.data?raw.data:[];return rows.map(r=>({t:ccNum(market==='spot'?r.endTime:r.time),o:ccNum(r.open),h:ccNum(r.high),l:ccNum(r.low),c:ccNum(r.close),v:ccNum(r.volume)})).filter(r=>r.t&&r.o!==null&&r.h!==null&&r.l!==null&&r.c!==null).sort((a,b)=>a.t-b.t)}
const CC_CHART_STATE={ccSpotChart:{rows:[],visibleCandles:86,panOffset:-8,priceRange:null,crosshair:null,drag:null,wheelBound:false},ccFuturesChart:{rows:[],visibleCandles:86,panOffset:-8,priceRange:null,crosshair:null,drag:null,wheelBound:false}};const CC_MARKET_DATA_CHART_STATE={rows:[],visibleCandles:86,panOffset:-8,priceRange:null,crosshair:null,drag:null,wheelBound:false,view:null,chartKey:'',persistTimer:0,lastPersistSig:''};const CC_LINEAR_CLOSE_COLOR='#60A5FA';const CC_LINEAR_MARK_COLOR='#94A3B8';const CC_LINEAR_CLOSE_FILL='rgba(96,165,250,.22)';const CC_LINEAR_ASK_COLOR='#EF4444';const CC_LINEAR_BID_COLOR='#22C55E';const CC_LINEAR_SIDE_WIDTH=1;const CC_LINEAR_PRICE_WIDTH=2;const CC_BUY_MARKER_IMG=new Image();CC_BUY_MARKER_IMG.onload=()=>drawCcChart('ccSpotChart');CC_BUY_MARKER_IMG.src='/assets/coincall-buy-marker.png';const CC_SELL_MARKER_IMG=new Image();CC_SELL_MARKER_IMG.onload=()=>drawCcChart('ccSpotChart');CC_SELL_MARKER_IMG.src='/assets/coincall-sell-marker.png';
function ccChartSampleCacheLoad(){try{const parsed=JSON.parse(localStorage.getItem(CC_WS_CHART_CACHE_KEY)||'{}');return parsed&&typeof parsed==='object'?parsed:{}}catch(e){return {}}}
function ccChartSampleCacheSave(cache){try{localStorage.setItem(CC_WS_CHART_CACHE_KEY,JSON.stringify(cache))}catch(e){}}
function ccChartMinuteCacheLoad(){try{const parsed=JSON.parse(localStorage.getItem(CC_DB_CHART_CACHE_KEY)||'{}');return parsed&&typeof parsed==='object'?parsed:{}}catch(e){return {}}}
function ccChartMinuteCacheSave(cache){try{localStorage.setItem(CC_DB_CHART_CACHE_KEY,JSON.stringify(cache))}catch(e){}}
function ccChartSampleSymbol(market){return String(cc(market==='futures'?'ccFuturesInst':'ccSpotInst')?.value||'').trim()}
function ccChartTimeframe(market){return String(cc(market==='futures'?'ccFuturesBar':'ccSpotBar')?.value||'1m')}
function ccTradeChartMode(){return String(CC_TRADE_STATE.chartMode||'candles').toLowerCase()==='linear'?'linear':'candles'}
function ccTradeChartUsesLinear(){return ccTradeChartMode()==='linear'}
function ccTradeChartModeLabel(){return ccTradeChartUsesLinear()?'line':'candles'}
function ccChartCanvasId(market){return market==='futures'?'ccFuturesChart':'ccSpotChart'}
function ccChartStatusId(market){return market==='futures'?'ccFuturesChartStatus':'ccSpotChartStatus'}
function ccChartLastId(market){return market==='futures'?'ccFuturesLast':'ccSpotLast'}
function ccChartLegendId(market){return market==='futures'?'ccFuturesChartLegend':'ccSpotChartLegend'}
function ccChartUtcNowText(){const d=new Date();return d.getUTCFullYear()+'-'+String(d.getUTCMonth()+1).padStart(2,'0')+'-'+String(d.getUTCDate()).padStart(2,'0')+' '+String(d.getUTCHours()).padStart(2,'0')+':'+String(d.getUTCMinutes()).padStart(2,'0')+':'+String(d.getUTCSeconds()).padStart(2,'0')+' UTC'}
function ccChartUpdateUtcLabels(){['spot','futures'].forEach(m=>{const el=cc(ccChartLastId(m));if(el)el.textContent=m==='futures'?ccChartUtcNowText().replace(' UTC',''):ccChartUtcNowText()})}
function ccChartLegendHtml(row){if(!row)return'';const hi=Number.isFinite(Number(row.hi))?Number(row.hi):Number(row.h),lo=Number.isFinite(Number(row.lo))?Number(row.lo):Number(row.l),delta=Number(row.c)-Number(row.o),pct=Number(row.o)?delta/Number(row.o)*100:0,color=delta>0?'#45C4A5':(delta<0?'#F04E60':'#CBD5E1'),sign=delta>0?'+':'';const v=n=>'<span style="color:'+color+';margin-left:3px">'+ccChartFmt(n)+'</span>';return 'O'+v(row.o)+' H'+v(hi)+' L'+v(lo)+' C'+v(row.c)+' <span style="color:'+color+'">'+sign+ccChartFmt(delta)+' ('+sign+ccFmt(pct,2)+'%)</span>'}
function ccChartSetLegend(market,row){const html=ccChartLegendHtml(row);const el=cc(ccChartLegendId(market));if(el)el.innerHTML=market==='futures'?'':html;const status=cc(ccChartStatusId(market));if(status&&market==='futures')status.innerHTML=html}
function ccChartMarketLabel(market,symbol){return market==='futures'?ccSelectedText('ccFuturesInst'):(symbol||'')}
function ccChartNormalizeTs(v){if(typeof v==='string'){const s=v.trim();if(s){const parsed=Date.parse(s);if(Number.isFinite(parsed)&&parsed>0)return parsed;const num=Number(s);if(Number.isFinite(num)&&num>0)return num<1e12?num*1000:num}}const n=Number(v);if(!Number.isFinite(n)||n<=0)return Date.now();return n<1e12?n*1000:n}
function ccChartSampleList(cache,market,symbol){if(!cache[market]||typeof cache[market]!=='object')cache[market]={};if(!Array.isArray(cache[market][symbol]))cache[market][symbol]=[];return cache[market][symbol]}
function ccChartMinuteList(cache,market,symbol){if(!cache[market]||typeof cache[market]!=='object')cache[market]={};if(!Array.isArray(cache[market][symbol]))cache[market][symbol]=[];return cache[market][symbol]}
function ccChartAppendSample(market,symbol,price,ts=Date.now()){const px=Number(price),at=ccChartNormalizeTs(ts);if(!symbol||!Number.isFinite(px)||px<=0)return false;const cache=ccChartSampleCacheLoad();const list=ccChartSampleList(cache,market,symbol);const prev=list[list.length-1];if(prev&&Number(prev.ts)===at&&Number(prev.p)===px)return false;list.push({ts:at,p:px});const cap=5000;if(list.length>cap)list.splice(0,list.length-cap);ccChartSampleCacheSave(cache);return true}
function ccChartRowsFromSamples(samples,tf){const bucketMs=CC_INTERVAL_MS[tf]||60000;const ordered=(Array.isArray(samples)?samples:[]).map(item=>({ts:ccChartNormalizeTs(item&&item.ts),p:Number(item&&item.p)})).filter(item=>Number.isFinite(item.ts)&&Number.isFinite(item.p)&&item.p>0).sort((a,b)=>a.ts-b.ts);const byBucket=new Map();for(const item of ordered){const bucket=Math.floor(item.ts/bucketMs)*bucketMs;const prev=byBucket.get(bucket);if(!prev)byBucket.set(bucket,{t:bucket,o:item.p,h:item.p,l:item.p,c:item.p,v:null});else{prev.h=Math.max(prev.h,item.p);prev.l=Math.min(prev.l,item.p);prev.c=item.p}}return Array.from(byBucket.values()).sort((a,b)=>a.t-b.t).slice(-180)}
function ccChartMinuteBucket(ts){const bucketMs=CC_INTERVAL_MS['1m']||60000;return Math.floor(ccChartNormalizeTs(ts)/bucketMs)*bucketMs}
function ccChartRowsFromMinuteRows(rows,tf){const ordered=(Array.isArray(rows)?rows:[]).map(r=>({t:ccChartMinuteBucket(r&&r.t),o:Number(r&&r.o),h:Number(r&&r.h),l:Number(r&&r.l),c:Number(r&&r.c),v:Number(r&&r.v)})).filter(r=>Number.isFinite(r.t)&&[r.o,r.h,r.l,r.c].every(Number.isFinite)).sort((a,b)=>a.t-b.t);if(tf==='1m')return ordered.slice(-180);const bucketMs=CC_INTERVAL_MS[tf]||60000;const byBucket=new Map();for(const row of ordered){const bucket=Math.floor(row.t/bucketMs)*bucketMs;const prev=byBucket.get(bucket);if(!prev)byBucket.set(bucket,{t:bucket,o:row.o,h:row.h,l:row.l,c:row.c,v:Number.isFinite(row.v)?row.v:null});else{prev.h=Math.max(prev.h,row.h);prev.l=Math.min(prev.l,row.l);prev.c=row.c;if(Number.isFinite(row.v))prev.v=(Number.isFinite(prev.v)?prev.v:0)+row.v}}return Array.from(byBucket.values()).sort((a,b)=>a.t-b.t).slice(-180)}
function ccChartOverlayLiveSamples(rows,samples,tf){const base=(Array.isArray(rows)?rows:[]).map(r=>({t:Number(r.t),o:Number(r.o),h:Number(r.h),l:Number(r.l),c:Number(r.c),v:r.v??null})).filter(r=>Number.isFinite(r.t)&&[r.o,r.h,r.l,r.c].every(Number.isFinite));const ordered=(Array.isArray(samples)?samples:[]).map(item=>({ts:ccChartNormalizeTs(item&&item.ts),p:Number(item&&item.p)})).filter(item=>Number.isFinite(item.ts)&&Number.isFinite(item.p)&&item.p>0).sort((a,b)=>a.ts-b.ts);if(!ordered.length)return base;const bucketMs=CC_INTERVAL_MS[tf]||60000;const byBucket=new Map(base.map(r=>[Number(r.t),{...r}]));const lastBaseBucket=base.length?Number(base[base.length-1].t):NaN;for(const item of ordered){const bucket=Math.floor(item.ts/bucketMs)*bucketMs;if(Number.isFinite(lastBaseBucket)&&bucket<lastBaseBucket)continue;const prev=byBucket.get(bucket);if(!prev){const seed=base.length?Number(base[base.length-1].c):item.p;byBucket.set(bucket,{t:bucket,o:seed,h:Math.max(seed,item.p),l:Math.min(seed,item.p),c:item.p,v:null});continue}prev.h=Math.max(prev.h,item.p);prev.l=Math.min(prev.l,item.p);prev.c=item.p}return Array.from(byBucket.values()).sort((a,b)=>a.t-b.t).slice(-180)}
function ccChartCurrentPrice(market){if(market==='spot'){const live=ccSpotLiveLastTradeInfo();if(Number.isFinite(live.price)&&live.price>0)return {price:live.price,ts:live.ts||Date.now()};const ask=Number(CC_BOOK_STATE.top.spot.ask),bid=Number(CC_BOOK_STATE.top.spot.bid);const mid=Number.isFinite(ask)&&Number.isFinite(bid)?(ask+bid)/2:NaN;return {price:mid,ts:Date.now()}}const live=ccFuturesLiveLastTradeInfo();if(Number.isFinite(live.price)&&live.price>0)return {price:live.price,ts:live.ts||Date.now()};return {price:NaN,ts:0}}
function ccChartRowsForSelection(market,symbol,tf){const minuteCache=ccChartMinuteCacheLoad();const sampleCache=ccChartSampleCacheLoad();const minuteRows=ccChartRowsFromMinuteRows(ccChartMinuteList(minuteCache,market,symbol),tf);if(minuteRows.length)return ccChartOverlayLiveSamples(minuteRows,ccChartSampleList(sampleCache,market,symbol),tf);return ccChartRowsFromSamples(ccChartSampleList(sampleCache,market,symbol),tf)}
function ccChartStateBaseRows(market,symbol,tf){const st=CC_CHART_STATE[ccChartCanvasId(market)];const chartKey=market+':'+symbol+':'+tf;return st&&st.baseKey===chartKey&&Array.isArray(st.baseRows)?st.baseRows:[]}
async function ccChartFetchBaseRows(market){const isF=market==='futures';const symbol=ccChartSampleSymbol(market);const tf=ccChartTimeframe(market);if(!symbol)return [];const url=isF?(()=>{const end=Date.now(),span=(CC_INTERVAL_MS[tf]||60000)*300,start=end-span;return '/coincall-public-api/open/futures/market/kline/history/v2/'+encodeURIComponent(symbol)+'?period='+(CC_INTERVAL.futures[tf]||'m1')+'&start='+start+'&end='+end+'&limit=300'})():('/coincall-public-api/open/spot/market/klines?symbol='+encodeURIComponent(symbol)+'&interval='+(CC_INTERVAL.spot[tf]||'1min')+'&limit=180');const raw=await ccApi(url);return ccChartRows(raw,market)}
function ccChartBackfillLimit(tf){return Math.max(240,Math.min(2000,Math.ceil((CC_INTERVAL_MS[tf]||60000)/60000)*720))}
function ccChartMergeMinuteCandles(market,symbol,rows){if(!symbol)return false;const cache=ccChartMinuteCacheLoad();const list=ccChartMinuteList(cache,market,symbol);const byBucket=new Map((Array.isArray(list)?list:[]).map(r=>[ccChartMinuteBucket(r&&r.t),{t:ccChartMinuteBucket(r&&r.t),o:Number(r&&r.o),h:Number(r&&r.h),l:Number(r&&r.l),c:Number(r&&r.c),v:Number(r&&r.v)}]));let changed=false;for(const row of Array.isArray(rows)?rows:[]){const minuteUtc=ccChartMinuteBucket(row&&row.minuteUtc),open=Number(row&&row.open),high=Number(row&&row.high),low=Number(row&&row.low),close=Number(row&&row.close),volume=Number(row&&row.volume);if(!Number.isFinite(minuteUtc)||![open,high,low,close].every(Number.isFinite))continue;const next={t:minuteUtc,o:open,h:high,l:low,c:close,v:Number.isFinite(volume)?volume:null};const prev=byBucket.get(minuteUtc);if(!prev||prev.o!==next.o||prev.h!==next.h||prev.l!==next.l||prev.c!==next.c||prev.v!==next.v){byBucket.set(minuteUtc,next);changed=true}}if(changed){cache[market]=cache[market]||{};cache[market][symbol]=Array.from(byBucket.values()).sort((a,b)=>a.t-b.t).slice(-5000);ccChartMinuteCacheSave(cache)}return changed}
async function ccChartBackfillFromDb(market){const symbol=ccChartSampleSymbol(market);const tf=ccChartTimeframe(market);if(!symbol)return false;try{const res=await ccApi('/api/admin/coincall/'+market+'/candles/minute?symbol='+encodeURIComponent(symbol)+'&limit='+ccChartBackfillLimit(tf));return ccChartMergeMinuteCandles(market,symbol,res&&res.rows)}catch(e){return false}}
function ccTradeLinearRowsForSelection(market,rows){const symbol=ccChartSampleSymbol(market),tf=ccChartTimeframe(market),st=CC_CHART_STATE[ccChartCanvasId(market)],chartKey=market+':'+symbol+':'+tf;const cached=st&&st.tradeLinearKey===chartKey&&Array.isArray(st.tradeLinearRows)?st.tradeLinearRows:[];if(cached.length)return cached;return (Array.isArray(rows)?rows:[]).map(r=>({t:Number(r.t),o:Number(r.o),h:Number(r.h),l:Number(r.l),c:Number(r.c),bid:NaN,ask:NaN})).filter(r=>Number.isFinite(r.t)&&[r.o,r.h,r.l,r.c].every(Number.isFinite))}
async function ccTradeLoadLinearRows(market){const symbol=ccChartSampleSymbol(market),tf=ccChartTimeframe(market),st=CC_CHART_STATE[ccChartCanvasId(market)],chartKey=market+':'+symbol+':'+tf;if(!symbol||!st)return [];if(st.tradeLinearKey===chartKey&&Array.isArray(st.tradeLinearRows)&&st.tradeLinearRows.length)return st.tradeLinearRows;try{const res=await ccApi('/api/admin/coincall/'+market+'/candles/minute?symbol='+encodeURIComponent(symbol)+'&limit='+ccChartBackfillLimit(tf));const rows=ccMarketDataRowsForTimeframe(ccMarketDataNormalizeRows(res&&res.rows),tf);st.tradeLinearRows=rows;st.tradeLinearKey=chartKey;return rows}catch(e){st.tradeLinearRows=[];st.tradeLinearKey=chartKey;return []}}
function ccRenderWsChart(market,opts={}){const isF=market==='futures';const symbol=ccChartSampleSymbol(market);const tf=ccChartTimeframe(market);const canvasId=ccChartCanvasId(market);const st=CC_CHART_STATE[canvasId];const chartKey=market+':'+symbol+':'+tf;if(st&&st.chartKey!==chartKey){st.priceRange=null;st.panOffset=-8;st.visibleCandles=86;st.crosshair=null;st.chartKey=chartKey;if(isF)ccFuturesViewportRestore(chartKey)}const live=ccChartCurrentPrice(market);const sampleCache=ccChartSampleCacheLoad();const baseRows=ccChartStateBaseRows(market,symbol,tf);let rows=baseRows.length?ccChartOverlayLiveSamples(baseRows,ccChartSampleList(sampleCache,market,symbol),tf):ccChartRowsForSelection(market,symbol,tf);if(!rows.length&&opts.ensureCurrent&&Number.isFinite(live.price)&&live.price>0){ccChartAppendSample(market,symbol,live.price,live.ts||Date.now());rows=ccChartRowsForSelection(market,symbol,tf)}const nextRows=rows.length?rows:ccChartRowsForSelection(market,symbol,tf);drawCcChart(canvasId,nextRows);const status=cc(ccChartStatusId(market));const label=ccChartMarketLabel(market,symbol);const last=nextRows[nextRows.length-1]||null;ccChartUpdateUtcLabels();if(status&&!isF){if(last){status.textContent=`${label} · ${tf} · ${nextRows.length} ${ccTradeChartModeLabel()} · ${ccUtcHm(last.t)} UTC`}else{status.textContent='Waiting for chart data…'}}if(isF&&CC_BOOK_STATE.raw.futures&&CC_BOOK_STATE.transport.futures===symbol)ccRenderBook('futures',CC_BOOK_STATE.raw.futures)}
function ccPushWsChartPrice(market,price,ts=Date.now(),symbolOverride=''){const symbol=String(symbolOverride||ccChartSampleSymbol(market)).trim();if(!ccChartAppendSample(market,symbol,price,ts))return;if(symbol===ccChartSampleSymbol(market))ccRenderWsChart(market)}
function ccPriceDecimals(v){const n=Number(v);return Number.isFinite(n)&&Math.abs(n)>100?2:5}
function ccChartFmt(v){return ccFmt(v,ccPriceDecimals(v))}
function ccChartMaxVisibleCandles(total,chartWidthPx){const dataMax=Math.max(1,total||0);const widthMax=Math.max(1,Math.floor(Number(chartWidthPx)||0));return Math.min(dataMax,widthMax)}
function ccChartClampVisible(st,total,chartWidthPx){const max=ccChartMaxVisibleCandles(total,chartWidthPx),min=Math.min(max,20);let v=Number(st.visibleCandles);if(!Number.isFinite(v)||v<=0)v=max;v=Math.round(Math.max(min,Math.min(max,v)));st.visibleCandles=v;return v}
function ccChartRightBlankCap(visible){const count=Math.max(1,Math.round(Number(visible)||0));return count<12?0:Math.max(8,Math.floor(count*.45))}
function ccDrawPriceTag(ctx,x,y,label,bg,fg,w,padT,chartH,opts){ctx.font='13px Segoe UI';ctx.textBaseline='middle';ctx.textAlign='left';const prefix=String(opts&&opts.prefix||'').trim(),text=prefix?prefix+' '+label:label,maxW=Math.max(56,Math.min(140,Math.max(56,Number(w)-Number(x)-2||140))),labelW=Math.max(50,Math.min(maxW,ctx.measureText(text).width+12));const tagH=20;const labelY=Math.max(padT,Math.min(padT+chartH-tagH,y-tagH/2));ctx.fillStyle=bg;ctx.fillRect(x,labelY,labelW,tagH);ctx.fillStyle=fg;ctx.fillText(text,x+6,labelY+tagH/2)}
function ccDrawStackedPriceMarkers(ctx,markers,layout){const list=(Array.isArray(markers)?markers:[]).filter(m=>m&&Number.isFinite(m.value)&&Number.isFinite(m.y));if(!list.length)return;const tagH=20,half=tagH/2,minGap=22,minY=layout.padT+half,maxY=layout.padT+layout.chartH-half,placed=list.map(m=>({...m,targetY:Math.max(minY,Math.min(maxY,m.y)),labelY:Math.max(minY,Math.min(maxY,m.y))})).sort((a,b)=>a.targetY-b.targetY);for(let i=1;i<placed.length;i++)placed[i].labelY=Math.max(placed[i].labelY,placed[i-1].labelY+minGap);for(let i=placed.length-1;i>=0;i--){if(placed[i].labelY>maxY){const delta=placed[i].labelY-maxY;for(let j=0;j<=i;j++)placed[j].labelY-=delta}}for(let i=placed.length-2;i>=0;i--)placed[i].labelY=Math.min(placed[i].labelY,placed[i+1].labelY-minGap);for(const marker of placed){ctx.save();ctx.strokeStyle=marker.lineColor||marker.bg;ctx.lineWidth=1;ctx.setLineDash(marker.dash||[4,3]);const startX=Math.max(layout.padL,Math.min(layout.lineRight,Number.isFinite(marker.startX)?marker.startX:layout.padL));ctx.beginPath();ctx.moveTo(startX,marker.targetY);ctx.lineTo(layout.lineRight,marker.targetY);ctx.stroke();ctx.setLineDash([]);if(Math.abs(marker.labelY-marker.targetY)>.5){ctx.beginPath();ctx.moveTo(layout.lineRight,marker.targetY);ctx.lineTo(layout.lineRight,marker.labelY);ctx.stroke()}ccDrawPriceTag(ctx,layout.tagX,marker.labelY,ccChartFmt(marker.value),marker.bg,marker.fg||'#FFFFFF',layout.w,layout.padT,layout.chartH,{prefix:marker.prefix});ctx.restore()}}
function ccSpotSymbolNorm(v){return String(v||'').toUpperCase().replace(/[^A-Z0-9]/g,'')}
function ccSpotCanonicalSymbol(value){return ccSpotSymbolNorm(value)}
function ccSpotOrderDisplaySymbol(o){const symbol=ccSpotCanonicalSymbol(ccFirstValue(o,['symbol','instId','displaySymbol','instrument','instrumentId','pair']));return symbol||ccFirst(ccPick(o,['displaySymbol','symbol','instId','instrument','pair']))}
function ccNormalizeOrderIdFields(o){if(!o||typeof o!=='object')return o;const out={...o};['orderId','ordId','order_id','id','oid','orderNo','clientOrderId','clientOid','clOrdId','coid'].forEach(k=>{const s=ccOrderIdString(out[k]);if(s)out[k]=s});return out}
function ccNormalizeSpotOrderSymbol(o){o=ccNormalizeOrderIdFields(o);const symbol=ccSpotOrderDisplaySymbol(o);return symbol&&symbol!=='—'?{...o,displaySymbol:symbol,symbol:symbol,instId:symbol}:o}
function ccSpotOrderPrice(o){return ccNum(ccPick(o,['price','px','orderPrice','limitPrice']))}
function ccSpotOrderSymbol(o){return ccSpotCanonicalSymbol(ccFirstValue(o,['symbol','instId','displaySymbol','instrument','instrumentId','pair']))}
function ccSpotOrderSideRaw(o){const raw=ccPick(o,['side','tradeSide','orderSide','direction','sd','si']);const n=Number(raw);if(n===1)return 'buy';if(n===2)return 'sell';const key=String(raw||'').trim().toUpperCase().replace(/[\s_-]+/g,'');if(['BUY','BID','LONG','OPENLONG','CLOSESHORT'].includes(key))return 'buy';if(['SELL','ASK','SHORT','OPENSHORT','CLOSELONG'].includes(key))return 'sell';return String(raw||'').toLowerCase()}
function ccSpotOrderQtyLeft(o){const qty=Number(ccPick(o,['remainQty','remainingQty','leavesQty','leftQty','qty','quantity','amount']));const filled=Number(ccPick(o,['fillQty','filledQty','filledQuantity','filled']));const left=(Number.isFinite(qty)?qty:NaN)-(Number.isFinite(filled)?filled:0);return Number.isFinite(left)&&left>0?left:qty}
function ccOrderIdString(v){if(v===undefined||v===null)return '';const s=String(v).trim();return s&&s!=='—'?s:''}
function ccOrderFirstId(o,names){return ccOrderIdString(ccFirstValue(o,names))}
function ccCanonicalOrderNumberString(v){const n=Number(v);if(!Number.isFinite(n))return String(v??'').trim();return String(Number(n.toPrecision(15)))}
function ccSpotOpenOrderId(o){return ccOrderFirstId(o,['orderId','id','clientOrderId','clientOid','clOrdId'])}
function ccSpotOrderIdentity(o){return {orderId:ccOrderFirstId(o,['orderId','ordId','order_id','id','oid','orderNo']),clientOrderId:ccOrderFirstId(o,['clientOrderId','clientOid','clOrdId','coid'])}}
function ccSpotOrderAmountRaw(o){return ccFirstValue(o,['qty','quantity','amount','orderQty','origQty','volume','q','sz'])}
function ccSpotOrderTypeKey(o){const raw=ccFirstValue(o,['tradeType','orderType','type','ot','orderTypeName','tradeTypeName']);const key=String(raw??'').trim().toUpperCase().replace(/[\s_-]+/g,'');if(key==='1'||key==='LIMIT')return 'LIMIT';if(key==='2'||key==='MARKET')return 'MARKET';if(key==='3'||key==='POSTONLY')return 'POST_ONLY';return key}
function ccSpotOrderShapeKey(o){const sym=ccSpotOrderSymbol(o),side=ccSpotOrderSideRaw(o),priceValue=ccSpotOrderPrice(o),price=ccCanonicalOrderNumberString(priceValue),qty=ccCanonicalOrderNumberString(ccSpotOrderAmountRaw(o)),type=ccSpotOrderTypeKey(o)||(Number.isFinite(priceValue)?'LIMIT':'');return sym&&side&&price&&qty?['shape',sym,side,price,qty,type].join('|'):''}
function ccSpotOpenOrderDedupeKey(o){const ids=ccSpotOrderIdentity(o);return ids.orderId?('oid:'+ids.orderId):(ids.clientOrderId?('cid:'+ids.clientOrderId):ccSpotOrderShapeKey(o))}
function ccSpotOrderIdentityKeys(o){const ids=ccSpotOrderIdentity(o),keys=[];if(ids.orderId){keys.push('oid:'+ids.orderId);keys.push('id:'+ids.orderId)}if(ids.clientOrderId){keys.push('cid:'+ids.clientOrderId);keys.push('id:'+ids.clientOrderId)}return keys}
function ccSpotOrderSame(a,b){const ak=ccSpotOrderIdentityKeys(a),bk=ccSpotOrderIdentityKeys(b);if(ak.length&&bk.length)return ak.some(k=>bk.includes(k));if(ak.length||bk.length)return false;const sa=ccSpotOrderShapeKey(a),sb=ccSpotOrderShapeKey(b);return !!(sa&&sb&&sa===sb)}
function ccSpotOrderShapeSame(a,b){const sa=ccSpotOrderShapeKey(a),sb=ccSpotOrderShapeKey(b);return !!(sa&&sb&&sa===sb)}
function ccSpotOrderHasIdentity(o){const ids=ccSpotOrderIdentity(o);return !!(ids.orderId||ids.clientOrderId)}
function ccSpotOrderCanShapeDedupe(a,b){return ccSpotOrderShapeSame(a,b)&&((a&&a.ccOptimistic)||(b&&b.ccOptimistic)||!ccSpotOrderHasIdentity(a)||!ccSpotOrderHasIdentity(b))}
function ccSpotMergeOpenOrderRows(existing,incoming){const a=ccSpotOrderIdentity(existing),b=ccSpotOrderIdentity(incoming),prefer=(!incoming?.ccOptimistic&&(b.orderId||b.clientOrderId))?b:a,out={...existing,...incoming};if(prefer.orderId)out.orderId=prefer.orderId;if(prefer.clientOrderId)out.clientOrderId=prefer.clientOrderId;return ccNormalizeSpotOrderSymbol(out)}
function ccSpotOpenOrderLooksValid(o){const side=ccSpotOrderSideRaw(o),price=ccSpotOrderPrice(o),qty=Number(ccSpotOrderAmountRaw(o));return !!o&&!ccSpotPrivateOrderIsTerminal(o)&&(side==='buy'||side==='sell')&&Number.isFinite(price)&&price>0&&Number.isFinite(qty)&&qty>0}
function ccSpotOpenOrdersSig(list){return (Array.isArray(list)?list:[]).map(o=>[ccSpotOpenOrderId(o),ccSpotOrderSymbol(o),ccSpotOrderSideRaw(o),ccSpotOrderPrice(o),ccSpotOrderQtyLeft(o)].join(':')).sort().join('|')}
function ccSpotPriceSame(a,b){const x=Number(a),y=Number(b);return Number.isFinite(x)&&Number.isFinite(y)&&Math.abs(x-y)<=Math.max(1e-10,Math.abs(y)*1e-8)}
function ccSpotBookOrderMarkers(side,price,symbol){const sym=ccSpotSymbolNorm(symbol||cc('ccSpotInst')?.value||'');const want=side==='bid'?'buy':'sell';const matches=(Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders:[]).filter(o=>(!sym||ccSpotOrderSymbol(o)===sym)&&ccSpotOrderSideRaw(o)===want&&ccSpotPriceSame(ccSpotOrderPrice(o),price));return matches.slice(0,3).map(o=>{const qty=ccSpotOrderQtyLeft(o),id=ccSpotOpenOrderId(o);const title=(want==='buy'?'Buy':'Sell')+' limit'+(Number.isFinite(qty)?' '+ccFmt(qty,8):'')+' @ '+ccChartFmt(price)+(id&&id!=='—'?' · '+id:'');return '<span class="okx-spot-order-marker '+ccEsc(want)+'" title="'+ccEsc(title)+'"></span>'}).join('')}
function ccSpotChartOpenOrders(){const sym=ccSpotSymbolNorm(cc('ccSpotInst')?.value||'');return (Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders:[]).filter(o=>!sym||ccSpotOrderSymbol(o)===sym).filter(o=>Number.isFinite(ccSpotOrderPrice(o))).slice(0,12)}
function ccFuturesChartSymbolKeys(){const transport=String(cc('ccFuturesInst')?.value||'').trim();const display=String(ccSelectedText('ccFuturesInst')||'').trim();return new Set([transport,display,ccFuturesBookSymbolKey(transport),ccFuturesBookSymbolKey(display)].filter(Boolean))}
function ccFuturesOrderMatchesCurrentSymbol(o){const keys=ccFuturesChartSymbolKeys();if(!keys.size)return true;const candidates=[ccPick(o,['symbol','instId','instrument','displayName','displaySymbol','ticker_id'])].map(v=>String(v||'').trim()).filter(Boolean);for(const raw of candidates){if(keys.has(raw)||keys.has(ccFuturesBookSymbolKey(raw)))return true}return false}
function ccFuturesOrderPrice(o){return ccNum(ccPick(o,['price','px','orderPrice','limitPrice','triggerPrice','stopPrice','triggerPx']))}
function ccFuturesOrderQtyLeft(o){const qty=Number(ccPick(o,['remainQty','remainingQty','leavesQty','leftQty','qty','quantity','amount','size','orderQty']));const filled=Number(ccPick(o,['fillQty','filledQty','filledQuantity','filled','dealQty']));const left=(Number.isFinite(qty)?qty:NaN)-(Number.isFinite(filled)?filled:0);return Number.isFinite(left)&&left>0?left:qty}
function ccFuturesChartOpenOrders(){return (Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[]).filter(ccFuturesOrderMatchesCurrentSymbol).filter(o=>Number.isFinite(ccFuturesOrderPrice(o)))}
function ccFuturesOrderBookMarkers(side,price,symbol){const sym=ccFuturesBookSymbolKey(symbol||cc('ccFuturesInst')?.value||'');const want=side==='bid'?'buy':'sell';const bucket=ccFuturesBookBucketPrice(price,side);const matches=(Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[]).filter(o=>{const orderSymbol=ccFuturesOrderSymbolKey(o);const orderPrice=ccFuturesOrderPrice(o);return (!sym||orderSymbol===sym)&&ccSpotOrderSideRaw(o)===want&&ccSpotPriceSame(ccFuturesBookBucketPrice(orderPrice,side),bucket)});return matches.slice(0,3).map(o=>{const qty=ccFuturesOrderQtyLeft(o),id=ccFuturesOrderDisplayId(o);const title=(want==='buy'?'Buy':'Sell')+' '+ccFuturesOrderTypeLabel(o).toLowerCase()+(Number.isFinite(qty)?' '+ccFmt(qty,8):'')+' @ '+ccFuturesBookPriceValue(ccFuturesOrderPrice(o))+(id&&id!=='—'?' · '+id:'');return '<span class="okx-spot-order-marker '+ccEsc(want)+'" title="'+ccEsc(title)+'"></span>'}).join('')}
function ccFuturesPositionSideRaw(p){const raw=ccPick(p,['tradeSide','side','direction']);const n=Number(raw);if(n===1)return 'buy';if(n===2)return 'sell';const text=String(raw||'').toLowerCase();if(text==='buy'||text==='long')return 'buy';if(text==='sell'||text==='short')return 'sell';return ''}
function ccFuturesPositionPrice(p){return ccNum(ccPick(p,['avgPrice','entryPrice','openPrice','avgOpenPrice','averagePrice','positionPrice']))}
function ccFuturesPositionQtyAbs(p){const signed=ccFuturesSignedQty(p);if(Number.isFinite(signed))return Math.abs(signed);const qty=Number(ccPick(p,['qty','quantity','amount','size','positionAmt']));return Number.isFinite(qty)?Math.abs(qty):NaN}
function ccFuturesPositionMatchesCurrentSymbol(p){const keys=ccFuturesChartSymbolKeys();if(!keys.size)return true;const candidates=[ccPick(p,['symbol','instId','instrument','displayName','displaySymbol','ticker_id'])].map(v=>String(v||'').trim()).filter(Boolean);for(const raw of candidates){if(keys.has(raw)||keys.has(ccFuturesBookSymbolKey(raw)))return true}return false}
function ccFuturesChartPositions(){return (Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[]).filter(p=>p&&typeof p==='object'&&!Array.isArray(p)).filter(ccFuturesPositionMatchesCurrentSymbol).filter(p=>Number.isFinite(ccFuturesPositionPrice(p))&&Number.isFinite(ccFuturesPositionQtyAbs(p))&&ccFuturesPositionQtyAbs(p)>0).slice(0,8)}
function ccSpotTradePrice(t){const v=Number(ccPick(t,['fillPrice','filledPrice','dealPrice','execPrice','matchPrice','price','px']));return Number.isFinite(v)?v:NaN}
function ccSpotTradeTs(t){const v=Number(ccPick(t,['ts','tradeTime','fillTime','createdTime','createTime','updateTime','time']));if(!Number.isFinite(v))return NaN;return v>0&&v<1e12?v*1000:v}
function ccSpotTradeQty(t){const v=Number(ccPick(t,['qty','quantity','amount','fillQty','filledQty','filledQuantity','dealQty','baseQty']));return Number.isFinite(v)?v:NaN}
function ccSpotTradeValueRaw(t,qty,px){const explicit=Number(ccPick(t,['value','filledValue','fillValue','dealValue','amountValue','quoteQty','quoteAmount','turnover']));if(Number.isFinite(explicit))return explicit;return Number.isFinite(qty)&&Number.isFinite(px)?qty*px:NaN}
function ccSpotLastTradeInfo(){const sym=ccSpotSymbolNorm(cc('ccSpotInst')?.value||'');let bestTs=NaN,bestPx=NaN,bestSide='';for(const [idx,t] of (Array.isArray(ccLastSpotTradeHistory)?ccLastSpotTradeHistory:[]).entries()){if(sym&&ccSpotOrderSymbol(t)!==sym)continue;const ts=ccSpotTradeTs(t),px=ccSpotTradePrice(t),side=ccSpotOrderSideRaw(t);if(!Number.isFinite(px))continue;if(Number.isFinite(ts)){if(!Number.isFinite(bestTs)||ts>bestTs){bestTs=ts;bestPx=px;bestSide=side||bestSide}}else if(!Number.isFinite(bestPx)&&idx===0){bestPx=px;bestSide=side||bestSide}}return {price:bestPx,side:bestSide}}
function ccSpotLastTradePrice(){return ccSpotLastTradeInfo().price}
function ccSpotLiveLastTradeInfo(){const symbol=String(CC_BOOK_STATE.transport.spot||'').trim();const live=CC_BOOK_STATE.spotLastTrade||{};return live.symbol===symbol&&Number.isFinite(live.price)?live:{price:NaN,side:'',ts:0}}
function ccSpotTradeDetailChannel(symbol){return 'market.'+symbol+'.trade.detail'}
function ccFuturesTradePrice(t){const v=Number(ccPick(t,['price','tradePrice','dealPrice','lastPrice','px','pr','matchPrice','mpr']));return Number.isFinite(v)?v:NaN}
function ccFuturesTradeTs(t){const v=Number(ccPick(t,['ts','time','tradeTime','createdTime','createTime']));if(!Number.isFinite(v))return NaN;return v>0&&v<1e12?v*1000:v}
function ccFuturesTradeSideRaw(t){const raw=ccPick(t,['side','tradeSide','direction','sd','si']);const n=Number(raw);if(n===1)return 'buy';if(n===2)return 'sell';const text=String(raw||'').toLowerCase();if(text==='buy'||text==='bid'||text==='b')return 'buy';if(text==='sell'||text==='ask'||text==='s')return 'sell';return ''}
function ccFuturesLiveLastTradeInfo(){const symbol=String(CC_BOOK_STATE.transport.futures||cc('ccFuturesInst')?.value||'').trim();const live=CC_BOOK_STATE.futuresLastTrade||{};return live.symbol===symbol&&Number.isFinite(live.price)?live:{price:NaN,side:'',ts:0}}
function ccScheduleChartRedraw(canvasId,delay=0){setTimeout(()=>{const st=CC_CHART_STATE[canvasId];if(st&&Array.isArray(st.rows)&&st.rows.length)drawCcChart(canvasId)},delay)}
function ccBindChartVisibilityObservers(canvasId,shellId){const canvas=cc(canvasId);const shell=cc(shellId);if(!canvas||canvas.dataset.ccVisibilityBound==='1')return;canvas.dataset.ccVisibilityBound='1';const redraw=()=>{ccScheduleChartRedraw(canvasId,0);ccScheduleChartRedraw(canvasId,80);ccScheduleChartRedraw(canvasId,220);ccScheduleChartRedraw(canvasId,500)};const wrap=canvas.parentElement;if(typeof ResizeObserver==='function'&&wrap){const ro=new ResizeObserver(()=>{const w=wrap.clientWidth||canvas.clientWidth||0;const h=wrap.clientHeight||canvas.clientHeight||0;if(w>0&&h>0)redraw()});ro.observe(wrap);if(shell&&shell!==wrap)ro.observe(shell)}if(typeof MutationObserver==='function'&&shell){const mo=new MutationObserver(()=>{if(!shell.hidden&&shell.classList.contains('active'))redraw()});mo.observe(shell,{attributes:true,attributeFilter:['hidden','class','style']})}window.addEventListener('load',redraw,{once:true})}
function ccSpotBookUnitLabels(){const parts=ccSpotParts();return {price:'Price ('+parts.quote+')',amount:'Amount ('+parts.base+')',total:'Total ('+parts.base+')'}}
function ccRenderSpotBookHeadUi(){const labels=ccSpotBookUnitLabels();const price=cc('ccSpotBookPriceHead'),amount=cc('ccSpotBookAmountHead'),total=cc('ccSpotBookTotalHead');if(price)price.textContent=labels.price;if(amount)amount.textContent=labels.amount;if(total)total.textContent=labels.total}
function ccCanvasRoundRect(ctx,x,y,w,h,r){r=Math.max(0,Math.min(r,Math.min(w,h)/2));ctx.beginPath();ctx.moveTo(x+r,y);ctx.lineTo(x+w-r,y);ctx.quadraticCurveTo(x+w,y,x+w,y+r);ctx.lineTo(x+w,y+h-r);ctx.quadraticCurveTo(x+w,y+h,x+w-r,y+h);ctx.lineTo(x+r,y+h);ctx.quadraticCurveTo(x,y+h,x,y+h-r);ctx.lineTo(x,y+r);ctx.quadraticCurveTo(x,y,x+r,y);ctx.closePath()}
function ccCanvasTradePin(ctx,cx,cy,w,h,r,noseUp=false){const nose=3,half=w/2,top=cy-h/2,bottom=cy+h/2,bodyTop=noseUp?top+nose:top,bodyBottom=noseUp?bottom:bottom-nose;r=Math.max(0,Math.min(r,(bodyBottom-bodyTop)/2,half));ctx.beginPath();if(noseUp){ctx.moveTo(cx,top);ctx.lineTo(cx+nose,bodyTop);ctx.lineTo(cx+half-.6,bodyTop);ctx.quadraticCurveTo(cx+half,bodyTop,cx+half,bodyTop+.6)}else{ctx.moveTo(cx-half+r,bodyTop);ctx.lineTo(cx+half-r,bodyTop);ctx.quadraticCurveTo(cx+half,bodyTop,cx+half,bodyTop+r)}ctx.lineTo(cx+half,bodyBottom-r);ctx.quadraticCurveTo(cx+half,bodyBottom,cx+half-r,bodyBottom);ctx.lineTo(cx-half+r,bodyBottom);ctx.quadraticCurveTo(cx-half,bodyBottom,cx-half,bodyBottom-r);ctx.lineTo(cx-half,bodyTop+(noseUp?.6:r));if(noseUp){ctx.quadraticCurveTo(cx-half,bodyTop,cx-half+.6,bodyTop);ctx.lineTo(cx-nose,bodyTop);ctx.lineTo(cx,top)}else{ctx.quadraticCurveTo(cx-half,bodyTop,cx-half+r,bodyTop);ctx.moveTo(cx-half+r,bodyBottom);ctx.lineTo(cx-nose,bodyBottom);ctx.lineTo(cx,bottom);ctx.lineTo(cx+nose,bodyBottom);ctx.lineTo(cx+half-r,bodyBottom)}ctx.closePath()}
function ccChartAutoFitPriceRange(canvasId){const st=CC_CHART_STATE[canvasId];if(!st||!Array.isArray(st.rows)||!st.rows.length)return false;const total=st.rows.length;const visible=Math.max(1,Math.round(Number(st.view?.visible)||Number(st.visibleCandles)||total));const panOffset=Math.max(-Math.max(8,Math.floor(visible*.45)),Math.min(Math.max(0,total-visible),Math.round(Number(st.panOffset)||0)));const rightBlank=Math.max(0,-panOffset);const end=total-Math.max(0,panOffset);const rowsShown=st.rows.slice(Math.max(0,end-Math.max(1,visible-rightBlank)),end);let vals=rowsShown.flatMap(r=>[Number(r.lo),Number(r.hi)]).filter(Number.isFinite);if(!vals.length)return false;const baseMin=Math.min(...vals),baseMax=Math.max(...vals),baseSpan=baseMax-baseMin||Math.max(Math.abs(baseMax)*.001,1e-8);const nearbyPrice=v=>Number.isFinite(v)&&v>=baseMin-baseSpan*.25&&v<=baseMax+baseSpan*.25;if(canvasId==='ccSpotChart'){vals=vals.concat(ccSpotChartOpenOrders().map(ccSpotOrderPrice).filter(nearbyPrice).slice(0,8));vals=vals.concat(ccSpotChartTradeRows().map(x=>x.px).filter(nearbyPrice).slice(0,20))}else if(canvasId==='ccFuturesChart'){vals=vals.concat(ccFuturesChartOpenOrders().map(ccFuturesOrderPrice).filter(nearbyPrice));vals=vals.concat(ccFuturesChartPositions().map(ccFuturesPositionPrice).filter(nearbyPrice).slice(0,6))}const min=Math.min(...vals),max=Math.max(...vals),span=max-min||Math.max(Math.abs(max)*.001,1e-8),pad=Math.max(span*.045,Math.abs((min+max)/2)*1e-7,1e-8);st.priceRange={min:min-pad,max:max+pad};st.panOffset=panOffset;return true}
function ccCanvasPentagon(ctx,cx,cy,r,pointUp=true){const pts=pointUp?[[cx,cy-r],[cx+r*.95,cy-r*.25],[cx+r*.58,cy+r],[cx-r*.58,cy+r],[cx-r*.95,cy-r*.25]]:[[cx,cy+r],[cx+r*.95,cy+r*.25],[cx+r*.58,cy-r],[cx-r*.58,cy-r],[cx-r*.95,cy+r*.25]];ctx.beginPath();pts.forEach((p,i)=>i?ctx.lineTo(p[0],p[1]):ctx.moveTo(p[0],p[1]));ctx.closePath()}
function ccSpotTradeTooltipLines(x){const parts=ccSpotParts();const side=x.side==='sell'?'Sell':'Buy';const lines=[side+' trade'];lines.push('Price: '+ccFmt(x.px,x.px>100?2:6)+' '+parts.quote);if(Number.isFinite(x.qty))lines.push('Amount: '+ccFmt(x.qty,8)+' '+parts.base);if(Number.isFinite(x.value))lines.push('Value: '+ccMoney(x.value,parts.quote));if(Number.isFinite(x.ts))lines.push('Time: '+ccFormatOrderTime(x.ts));return lines}
function ccSpotChartTradeRows(){const sym=ccSpotSymbolNorm(cc('ccSpotInst')?.value||'');return (Array.isArray(ccLastSpotTradeHistory)?ccLastSpotTradeHistory:[]).filter(t=>!sym||ccSpotOrderSymbol(t)===sym).map((t,i)=>{const px=ccSpotTradePrice(t),ts=ccSpotTradeTs(t),qty=ccSpotTradeQty(t);return {t,i,px,ts,side:ccSpotOrderSideRaw(t),qty,value:ccSpotTradeValueRaw(t,qty,px)}}).filter(x=>Number.isFinite(x.px)&&Number.isFinite(x.ts)).slice(0,80)}
function ccSpotSelectedIntervalMs(){const tf=cc('ccSpotBar')?.value||'1m';return {'1m':60000,'5m':300000,'15m':900000,'1H':3600000,'4H':14400000,'1D':86400000}[tf]||60000}
function ccSpotTradeCandleIndex(x,rowsShown){if(!rowsShown||!rowsShown.length||!Number.isFinite(x.ts))return -1;const bar=ccSpotSelectedIntervalMs();for(let i=0;i<rowsShown.length;i++){const t=Number(rowsShown[i].ts),next=Number(rowsShown[i+1]?.ts);const end=Number.isFinite(next)&&next>t?next:t+bar;if(x.ts>=t&&x.ts<end)return i;if(x.ts>t-bar&&x.ts<=t)return i}return -1}
function ccDrawOverlayLabel(ctx,text,color,y,labelX,lineEnd,padT,chartH,maxLabelW){const labelH=20;const safeMax=Math.max(72,Number(maxLabelW)||72);const labelW=Math.min(safeMax,Math.max(72,ctx.measureText(text).width+14));const labelY=Math.max(padT,Math.min(padT+chartH-labelH,y-labelH/2));ctx.beginPath();ctx.moveTo(labelX+labelW,y);ctx.lineTo(lineEnd,y);ctx.stroke();ctx.fillStyle=color;ctx.fillRect(labelX,labelY,labelW,labelH);ctx.fillStyle='#FFFFFF';ctx.fillText(text,labelX+7,labelY+labelH/2);return labelW}
function ccCanvasTraceStepPath(ctx,points){if(!Array.isArray(points)||!points.length)return 0;ctx.moveTo(points[0].x,points[0].y);for(let i=1;i<points.length;i++){const prev=points[i-1],cur=points[i];ctx.lineTo(cur.x,prev.y);ctx.lineTo(cur.x,cur.y)}return points.length}
function drawCcChart(canvasId,rows){const canvas=cc(canvasId);const st=CC_CHART_STATE[canvasId];if(!canvas||!st)return;if(Array.isArray(rows))st.rows=rows.map(r=>({ts:Number(r.t),o:Number(r.o),hi:Number(r.h),lo:Number(r.l),c:Number(r.c)})).filter(r=>[r.ts,r.o,r.hi,r.lo,r.c].every(Number.isFinite));bindCcChartWheel(canvasId);const wrap=canvas.parentElement;const dpr=window.devicePixelRatio||1;const w=Math.max(320,(wrap?.clientWidth||canvas.clientWidth||0)-16),h=Number(canvas.getAttribute('height'))||340;canvas.style.width=w+'px';canvas.width=Math.floor(w*dpr);canvas.height=Math.floor(h*dpr);const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);ctx.fillStyle='#090B0D';ctx.fillRect(0,0,w,h);const allRows=st.rows;const market=canvasId==='ccFuturesChart'?'futures':'spot';const statusId=market==='futures'?'ccFuturesChartStatus':'ccSpotChartStatus';const status=cc(statusId);if(!allRows.length){ccChartSetLegend(market,null);if(status)status.textContent='No chart data';return}const padL=12,padR=82,padT=12,padB=38;const chartW=Math.max(1,w-padL-padR),chartH=h-padT-padB;const visible=ccChartClampVisible(st,allRows.length,chartW);const maxPan=Math.max(0,allRows.length-visible);const minPan=-Math.max(8,Math.floor(visible*.45));const panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));st.panOffset=panOffset;const rightBlank=Math.max(0,-panOffset);const end=allRows.length-Math.max(0,panOffset);const rowsShown=allRows.slice(Math.max(0,end-Math.max(1,visible-rightBlank)),end);let baseVals=rowsShown.flatMap(r=>[r.lo,r.hi]);const auto0Min=Math.min(...baseVals),auto0Max=Math.max(...baseVals),auto0Span=auto0Max-auto0Min||1;const nearbyPrice=v=>Number.isFinite(v)&&v>=auto0Min-auto0Span*.25&&v<=auto0Max+auto0Span*.25;const openOrders=market==='futures'?ccFuturesChartOpenOrders():ccSpotChartOpenOrders();const positionRows=market==='futures'?ccFuturesChartPositions():[];const tradeRows=market==='spot'?ccSpotChartTradeRows():[];const orderPriceGetter=market==='futures'?ccFuturesOrderPrice:ccSpotOrderPrice;const orderQtyGetter=market==='futures'?ccFuturesOrderQtyLeft:ccSpotOrderQtyLeft;const orderBase=market==='futures'?ccFuturesParts().base:ccSpotParts().base;const orderPricesAll=openOrders.map(orderPriceGetter).filter(nearbyPrice);const orderPrices=market==='futures'?orderPricesAll:orderPricesAll.slice(0,8);const positionPrices=positionRows.map(ccFuturesPositionPrice).filter(nearbyPrice).slice(0,6);const tradePrices=tradeRows.map(x=>x.px).filter(nearbyPrice).slice(0,20);baseVals=baseVals.concat(orderPrices,positionPrices,tradePrices);const autoMin=Math.min(...baseVals),autoMax=Math.max(...baseVals);const manual=st.priceRange;const useManual=manual&&Number.isFinite(manual.min)&&Number.isFinite(manual.max)&&manual.max>manual.min;const min=useManual?manual.min:autoMin,max=useManual?manual.max:autoMax,span=max-min||1;st.view={padL,padR,padT,padB,chartW,chartH,w,h,visible,total:allRows.length,panOffset,rightBlank,min,max,span,step:chartW/visible};const crisp=v=>Math.round(v)+0.5;ctx.strokeStyle='rgba(42,46,53,.55)';ctx.lineWidth=1;ctx.font='13px Segoe UI';ctx.textBaseline='middle';for(let i=0;i<5;i++){const y=crisp(padT+chartH*i/4);ctx.beginPath();ctx.moveTo(padL,y);ctx.lineTo(w-padR,y);ctx.stroke();const val=max-span*i/4;ctx.fillStyle='#8A8F98';ctx.textAlign='left';ctx.fillText(ccChartFmt(val),w-padR+8,y)}ctx.textBaseline='alphabetic';ctx.font='13px Segoe UI';ctx.textAlign='center';const tickCount=Math.min(5,rowsShown.length);const yForPrice=v=>padT+(max-v)/span*chartH;const step=chartW/visible;for(let i=0;i<tickCount;i++){const idx=tickCount===1?0:Math.round(i*(rowsShown.length-1)/(tickCount-1));const x=crisp(padL+(idx+.5)*step);const t=new Date(rowsShown[idx].ts);const label=ccUtcHm(t);ctx.strokeStyle='rgba(42,46,53,.55)';ctx.beginPath();ctx.moveTo(x,padT);ctx.lineTo(x,padT+chartH);ctx.stroke();ctx.fillStyle='#8A8F98';ctx.fillText(label,x,h-10)}const overlayBaseX=Math.max(4,padL-18);const futuresPositionReserve=market==='futures'?(()=>{ctx.save();ctx.font='11px Segoe UI';const labels=['Short 0.00000000 @ 000000.00',...positionRows.map(p=>{const qty=ccFuturesPositionQtyAbs(p);const px=ccFuturesPositionPrice(p);const side=ccFuturesPositionSideRaw(p);const labelQty=Number.isFinite(qty)?ccFmt(qty,8):'—';return (side==='sell'?'Short':'Long')+' '+labelQty+' @ '+ccChartFmt(px)})];const reserve=Math.min(Math.max(72,w-padR-overlayBaseX-92),Math.max(72,labels.reduce((m,label)=>Math.max(m,ctx.measureText(label).width+14),72)));ctx.restore();return reserve})():0;const futuresOrderLabelX=overlayBaseX+Math.max(18,futuresPositionReserve-22);rowsShown.forEach((r,i)=>{const x=crisp(padL+i*step+step/2);const up=r.c>=r.o;const color=up?'#45C4A5':'#F04E60';ctx.strokeStyle=color;ctx.fillStyle=color;ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(x,crisp(yForPrice(r.hi)));ctx.lineTo(x,crisp(yForPrice(r.lo)));ctx.stroke();const top=Math.round(Math.min(yForPrice(r.o),yForPrice(r.c)));const bottom=Math.round(Math.max(yForPrice(r.o),yForPrice(r.c)));const bodyH=Math.max(1,bottom-top);let bodyW=Math.max(3,Math.min(11,Math.round(step*.72)));if(bodyW%2===0)bodyW+=1;const bodyX=Math.round(x-bodyW/2);ctx.fillRect(bodyX,top,bodyW,bodyH)});const chartOpenOrders=openOrders.map((o,i)=>({o,i,px:orderPriceGetter(o),side:ccSpotOrderSideRaw(o)})).filter(x=>Number.isFinite(x.px)&&x.px>=min&&x.px<=max).sort((a,b)=>(b.side==='sell')-(a.side==='sell')||a.i-b.i);(market==='futures'?chartOpenOrders:chartOpenOrders.slice(0,8)).forEach(({o,px,side})=>{const y=yForPrice(px);const color=side==='buy'?'#166534':'#991B1B';const qty=orderQtyGetter(o);if(market==='futures'){ctx.save();ctx.strokeStyle=color;ctx.lineWidth=1.4;ctx.setLineDash([6,4]);ctx.font='11px Segoe UI';ctx.textAlign='left';ctx.textBaseline='middle';const orderTypeLabel=ccFuturesOrderTypeLabel(o);const orderLabel=(side==='sell'?'Sell':'Buy')+(orderTypeLabel&&orderTypeLabel!=='—'?' '+orderTypeLabel:'')+' @ '+ccChartFmt(px);ccDrawOverlayLabel(ctx,orderLabel,color,y,futuresOrderLabelX,w-padR,padT,chartH,Math.max(72,w-padR-futuresOrderLabelX-10));ctx.setLineDash([]);ctx.restore();return}ctx.strokeStyle=color;ctx.setLineDash([6,4]);ctx.beginPath();ctx.moveTo(padL,y);ctx.lineTo(w-padR,y);ctx.stroke();ctx.setLineDash([]);ctx.fillStyle=color;ctx.beginPath();ctx.arc(padL+8,y,4,0,Math.PI*2);ctx.fill();ccDrawPriceTag(ctx,w-padR+4,y,ccChartFmt(px),color,'#FFFFFF',w,padT,chartH);if(Number.isFinite(qty)){ctx.font='11px Segoe UI';ctx.textAlign='left';ctx.textBaseline='middle';ctx.fillStyle='#FFFFFF';const orderLabel=(side==='sell'?'Sell':'Buy')+' '+ccFmt(qty,8)+' '+orderBase;ctx.fillText(orderLabel,padL+18,Math.max(padT+8,Math.min(padT+chartH-8,y-10)))}});positionRows.map((p,i)=>({p,i,px:ccFuturesPositionPrice(p),side:ccFuturesPositionSideRaw(p),qty:ccFuturesPositionQtyAbs(p)})).filter(x=>Number.isFinite(x.px)&&x.px>=min&&x.px<=max).sort((a,b)=>(b.side==='sell')-(a.side==='sell')||a.i-b.i).slice(0,6).forEach(({p,px,side,qty})=>{const y=yForPrice(px);const color=side==='sell'?'#7F1D1D':'#14532D';ctx.save();ctx.strokeStyle=color;ctx.lineWidth=1.4;ctx.font='11px Segoe UI';ctx.textAlign='left';ctx.textBaseline='middle';const labelQty=Number.isFinite(qty)?ccFmt(qty,8):'—';const label=(side==='sell'?'Short':'Long')+' '+labelQty+' @ '+ccChartFmt(px);ccDrawOverlayLabel(ctx,label,color,y,overlayBaseX,w-padR,padT,chartH,Math.max(72,w-padR-overlayBaseX-10));ctx.restore()});let hoveredTrade=null;const mouse=st.crosshair;tradeRows.filter(x=>x.px>=min&&x.px<=max).slice(0,40).forEach(x=>{const nearest=ccSpotTradeCandleIndex(x,rowsShown);if(nearest<0)return;const candle=rowsShown[nearest]||null;const tx=padL+(nearest+.5)*step;const buy=x.side!=='sell';const color=buy?'#22C55E':'#EF4444';const tradeY=yForPrice(x.px);ctx.fillStyle=buy?'#00ff66':'#ff2d2d';ctx.beginPath();ctx.arc(tx,tradeY,2.5,0,Math.PI*2);ctx.fill();const anchorY=candle?yForPrice(buy?candle.lo:candle.hi):tradeY;const markerImg=buy?CC_BUY_MARKER_IMG:CC_SELL_MARKER_IMG;const exactMarker=markerImg.complete&&markerImg.naturalWidth>0;const markerW=exactMarker?markerImg.naturalWidth:14,markerH=exactMarker?markerImg.naturalHeight:16,markerHalfW=markerW/2,markerHalfH=markerH/2,markerGap=5;let markerY=buy?anchorY+markerGap+markerHalfH:anchorY-markerGap-markerHalfH;markerY=Math.max(padT+markerHalfH+1,Math.min(padT+chartH-markerHalfH-1,markerY));ctx.save();if(exactMarker){ctx.drawImage(markerImg,Math.round(tx-markerHalfW),Math.round(markerY-markerHalfH),markerW,markerH)}else{ctx.shadowColor='rgba(15,23,42,.50)';ctx.shadowBlur=2;ccCanvasTradePin(ctx,tx,markerY,markerW,markerH,3,buy);ctx.fillStyle=color;ctx.fill();ctx.shadowBlur=0;ctx.lineWidth=1;ctx.strokeStyle=buy?'#15803D':'#B91C1C';ctx.stroke();ctx.font='800 9px Segoe UI';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle='#FFFFFF';ctx.fillText(buy?'B':'S',tx,markerY-1.5)}ctx.restore();const markerX=tx-markerHalfW;if(mouse&&Number.isFinite(mouse.x)&&Number.isFinite(mouse.y)&&mouse.x>=tx-markerHalfW-5&&mouse.x<=tx+markerHalfW+5&&mouse.y>=markerY-markerHalfH-5&&mouse.y<=markerY+markerHalfH+5)hoveredTrade={...x,markerX,markerY,tx}});if(hoveredTrade){const lines=ccSpotTradeTooltipLines(hoveredTrade);ctx.save();ctx.font='12px Segoe UI';const tw=Math.max(...lines.map(line=>ctx.measureText(line).width))+18,th=lines.length*17+12;let bx=Math.min(w-padR-tw-4,hoveredTrade.markerX+20),by=Math.max(padT+4,hoveredTrade.markerY-th-8);if(bx<padL+4)bx=padL+4;if(by<padT+4)by=Math.min(padT+chartH-th-4,hoveredTrade.markerY+22);ccCanvasRoundRect(ctx,bx,by,tw,th,6);ctx.fillStyle='rgba(15,23,42,.96)';ctx.fill();ctx.strokeStyle='rgba(148,163,184,.45)';ctx.stroke();ctx.fillStyle='#E5E7EB';ctx.textAlign='left';ctx.textBaseline='top';lines.forEach((line,i)=>{ctx.fillStyle=i===0?(hoveredTrade.side==='sell'?'#FCA5A5':'#86EFAC'):'#E5E7EB';ctx.fillText(line,bx+9,by+7+i*17)});ctx.restore()}const last=allRows[allRows.length-1]?.c;if(Number.isFinite(last)){const y=yForPrice(last);ctx.strokeStyle='rgba(148,163,184,.75)';ctx.setLineDash([4,3]);ctx.beginPath();ctx.moveTo(padL,y);ctx.lineTo(w-padR,y);ctx.stroke();ctx.setLineDash([]);ccDrawPriceTag(ctx,w-padR+4,y,ccChartFmt(last),'#4B5563','#FFFFFF',w,padT,chartH)}let legendRow=rowsShown[rowsShown.length-1]||allRows[allRows.length-1]||null;const ch=st.crosshair;if(ch&&Number.isFinite(ch.x)&&Number.isFinite(ch.y)&&ch.x>=padL&&ch.x<=w-padR&&ch.y>=padT&&ch.y<=padT+chartH){const rawCx=Math.max(padL,Math.min(w-padR,ch.x)),cy=Math.max(padT,Math.min(padT+chartH,ch.y));const price=max-(cy-padT)/chartH*span;const idx=Math.max(0,Math.min(rowsShown.length-1,Math.round((rawCx-padL-step/2)/step)));const cx=padL+(idx+.5)*step;legendRow=rowsShown[idx]||legendRow;const time=new Date(rowsShown[idx].ts);const timeLabel=ccUtcMdHms(time);ctx.save();ctx.strokeStyle='rgba(203,213,225,.82)';ctx.lineWidth=1;ctx.setLineDash([3,3]);ctx.beginPath();ctx.moveTo(cx,padT);ctx.lineTo(cx,padT+chartH);ctx.moveTo(padL,cy);ctx.lineTo(w-padR,cy);ctx.stroke();ctx.setLineDash([]);ccDrawPriceTag(ctx,w-padR+4,cy,ccChartFmt(price),'#64748B','#F8FAFC',w,padT,chartH);ctx.font='700 12px Segoe UI';const tw=Math.min(148,Math.max(100,ctx.measureText(timeLabel).width+12));const tx=Math.max(padL,Math.min(w-padR-tw,cx-tw/2));ctx.fillStyle='#64748B';ctx.fillRect(tx,h-padB+8,tw,20);ctx.fillStyle='#F8FAFC';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(timeLabel,tx+tw/2,h-padB+18);ctx.restore()}ccChartSetLegend(market,legendRow)}
function ccTradeDrawLinearChart(canvasId,rows){const canvas=cc(canvasId);const st=CC_CHART_STATE[canvasId];if(!canvas||!st)return;const market=canvasId==='ccFuturesChart'?'futures':'spot';const linearRows=ccTradeLinearRowsForSelection(market,rows);if(Array.isArray(linearRows))st.rows=linearRows.map(r=>({ts:Number(r.t),o:Number(r.o),hi:Number(r.h),lo:Number(r.l),c:Number(r.c),bid:Number(r.bid),ask:Number(r.ask)})).filter(r=>[r.ts,r.o,r.hi,r.lo,r.c].every(Number.isFinite));bindCcChartWheel(canvasId);const wrap=canvas.parentElement;const dpr=window.devicePixelRatio||1;const w=Math.max(320,(wrap?.clientWidth||canvas.clientWidth||0)-16),h=Number(canvas.getAttribute('height'))||340;canvas.style.width=w+'px';canvas.width=Math.floor(w*dpr);canvas.height=Math.floor(h*dpr);const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);ctx.fillStyle='#090B0D';ctx.fillRect(0,0,w,h);const allRows=st.rows;const status=cc(ccChartStatusId(market));if(!allRows.length){ccChartSetLegend(market,null);if(status)status.textContent='No chart data';return}const padL=12,padR=82,padT=12,padB=38;const chartW=Math.max(1,w-padL-padR),chartH=h-padT-padB;const visible=ccChartClampVisible(st,allRows.length,chartW);const maxPan=Math.max(0,allRows.length-visible);const minPan=-Math.max(8,Math.floor(visible*.45));const panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));st.panOffset=panOffset;const rightBlank=Math.max(0,-panOffset);const end=allRows.length-Math.max(0,panOffset);const rowsShown=allRows.slice(Math.max(0,end-Math.max(1,visible-rightBlank)),end);let baseVals=rowsShown.flatMap(r=>[r.c,Number.isFinite(r.ask)?r.ask:NaN,Number.isFinite(r.bid)?r.bid:NaN]).filter(Number.isFinite);const openOrders=(market==='futures'?ccFuturesChartOpenOrders():ccSpotChartOpenOrders()).map(market==='futures'?ccFuturesOrderPrice:ccSpotOrderPrice).filter(Number.isFinite);const positionPrices=(market==='futures'?ccFuturesChartPositions():[]).map(ccFuturesPositionPrice).filter(Number.isFinite);baseVals=baseVals.concat(openOrders,positionPrices);const min=Math.min(...baseVals),max=Math.max(...baseVals),span=max-min||1;const step=chartW/visible;st.view={padL,padR,padT,padB,chartW,chartH,w,h,visible,total:allRows.length,panOffset,rightBlank,min,max,span,step};const yForPrice=v=>padT+(max-v)/span*chartH;const crisp=v=>Math.round(v)+0.5;ctx.strokeStyle='rgba(42,46,53,.55)';ctx.lineWidth=1;ctx.font='13px Segoe UI';ctx.textBaseline='middle';for(let i=0;i<5;i++){const y=crisp(padT+chartH*i/4);ctx.beginPath();ctx.moveTo(padL,y);ctx.lineTo(w-padR,y);ctx.stroke();ctx.fillStyle='#8A8F98';ctx.textAlign='left';ctx.fillText(ccChartFmt(max-span*i/4),w-padR+8,y)}ctx.textBaseline='alphabetic';ctx.textAlign='center';const tickCount=Math.min(5,rowsShown.length);for(let i=0;i<tickCount;i++){const idx=tickCount===1?0:Math.round(i*(rowsShown.length-1)/(tickCount-1));const x=crisp(padL+(idx+.5)*step);ctx.strokeStyle='rgba(42,46,53,.55)';ctx.beginPath();ctx.moveTo(x,padT);ctx.lineTo(x,padT+chartH);ctx.stroke();ctx.fillStyle='#8A8F98';ctx.fillText(ccUtcHm(rowsShown[idx].ts),x,h-10)}const line=(key,color,width=CC_LINEAR_SIDE_WIDTH)=>{ctx.save();ctx.strokeStyle=color;ctx.fillStyle=color;ctx.lineWidth=width;ctx.beginPath();let points=[],count=0,last=null;rowsShown.forEach((r,i)=>{const v=r[key];if(!Number.isFinite(v)){if(points.length)ccCanvasTraceStepPath(ctx,points);points=[];return}const point={x:crisp(padL+i*step+step/2),y:yForPrice(v)};points.push(point);last=point;count++});if(points.length)ccCanvasTraceStepPath(ctx,points);ctx.stroke();if(count===1&&last){ctx.beginPath();ctx.arc(last.x,last.y,3,0,Math.PI*2);ctx.fill()}ctx.restore()};const closePoints=rowsShown.map((r,i)=>({x:crisp(padL+i*step+step/2),y:yForPrice(r.c)}));line('ask',CC_LINEAR_ASK_COLOR);line('bid',CC_LINEAR_BID_COLOR);ctx.save();if(closePoints.length>1){ctx.beginPath();ccCanvasTraceStepPath(ctx,closePoints);ctx.lineTo(closePoints[closePoints.length-1].x,padT+chartH);ctx.lineTo(closePoints[0].x,padT+chartH);ctx.closePath();const fill=ctx.createLinearGradient(0,padT,0,padT+chartH);fill.addColorStop(0,CC_LINEAR_CLOSE_FILL);fill.addColorStop(1,'rgba(96,165,250,0)');ctx.fillStyle=fill;ctx.fill()}ctx.restore();line('c',CC_LINEAR_CLOSE_COLOR,CC_LINEAR_PRICE_WIDTH);const last=rowsShown[rowsShown.length-1]||allRows[allRows.length-1]||null;if(last&&Number.isFinite(last.c)){const y=yForPrice(last.c);ctx.strokeStyle='rgba(148,163,184,.75)';ctx.setLineDash([4,3]);ctx.beginPath();ctx.moveTo(padL,y);ctx.lineTo(w-padR,y);ctx.stroke();ctx.setLineDash([]);ccDrawPriceTag(ctx,w-padR+4,y,ccChartFmt(last.c),'#4B5563','#FFFFFF',w,padT,chartH)}let legendRow=rowsShown[rowsShown.length-1]||allRows[allRows.length-1]||null;const ch=st.crosshair;if(ch&&Number.isFinite(ch.x)&&Number.isFinite(ch.y)&&ch.x>=padL&&ch.x<=w-padR&&ch.y>=padT&&ch.y<=padT+chartH){const rawCx=Math.max(padL,Math.min(w-padR,ch.x)),cy=Math.max(padT,Math.min(padT+chartH,ch.y));const price=max-(cy-padT)/chartH*span;const idx=Math.max(0,Math.min(rowsShown.length-1,Math.round((rawCx-padL-step/2)/step)));const cx=padL+(idx+.5)*step;legendRow=rowsShown[idx]||legendRow;const timeLabel=ccUtcMdHms(rowsShown[idx].ts);ctx.save();ctx.strokeStyle='rgba(203,213,225,.82)';ctx.lineWidth=1;ctx.setLineDash([3,3]);ctx.beginPath();ctx.moveTo(cx,padT);ctx.lineTo(cx,padT+chartH);ctx.moveTo(padL,cy);ctx.lineTo(w-padR,cy);ctx.stroke();ctx.setLineDash([]);ccDrawPriceTag(ctx,w-padR+4,cy,ccChartFmt(price),'#64748B','#F8FAFC',w,padT,chartH);ctx.font='700 12px Segoe UI';const tw=Math.min(148,Math.max(100,ctx.measureText(timeLabel).width+12));const tx=Math.max(padL,Math.min(w-padR-tw,cx-tw/2));ctx.fillStyle='#64748B';ctx.fillRect(tx,h-padB+8,tw,20);ctx.fillStyle='#F8FAFC';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(timeLabel,tx+tw/2,h-padB+18);ctx.restore()}ccChartSetLegend(market,legendRow)}
const ccDrawCandleChart=drawCcChart;
drawCcChart=function(canvasId,rows){const out=ccTradeChartUsesLinear()?ccTradeDrawLinearChart(canvasId,rows):ccDrawCandleChart(canvasId,rows);if(canvasId==='ccFuturesChart')ccFuturesViewportPersistSoon();return out}
function bindCcChartWheel(canvasId){const canvas=cc(canvasId);const st=CC_CHART_STATE[canvasId];if(!canvas||!st||st.wheelBound)return;st.wheelBound=true;const pointFromEvent=event=>{const rect=canvas.getBoundingClientRect();return{x:event.clientX-rect.left,y:event.clientY-rect.top}};const inPlot=p=>{const v=st.view;return v&&p.x>=v.padL&&p.x<=v.w-v.padR&&p.y>=v.padT&&p.y<=v.padT+v.chartH};const inPriceScale=p=>{const v=st.view;return v&&p.x>v.w-v.padR&&p.x<=v.w&&p.y>=v.padT&&p.y<=v.padT+v.chartH};const inTimeScale=p=>{const v=st.view;return v&&p.x>=v.padL&&p.x<=v.w-v.padR&&p.y>v.padT+v.chartH&&p.y<=v.h};const maxVisible=()=>{const total=st.rows.length;const v=st.view;const chartW=v?.chartW||Math.max(1,canvas.clientWidth-94);return ccChartMaxVisibleCandles(total,chartW)};const applyHorizontalZoomFromDrag=(drag,p)=>{const total=st.rows.length;if(!total)return;const max=maxVisible(),min=Math.min(max,20);const dx=p.x-drag.x;const factor=Math.exp(dx/180);const next=Math.round(Math.max(min,Math.min(max,drag.visible*factor)));const focusRatio=Math.max(0,Math.min(1,(drag.x-drag.padL)/Math.max(1,drag.chartW)));const oldAfter=drag.visible*(1-focusRatio),nextAfter=next*(1-focusRatio);const maxPan=Math.max(0,total-next),minPan=-Math.max(8,Math.floor(next*.45));st.visibleCandles=next;st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(drag.panOffset+oldAfter-nextAfter)))};const applyVerticalZoomFromDrag=(drag,p)=>{const dy=p.y-drag.y;const factor=Math.exp(dy/160);const maxSpan=Math.max(drag.autoSpan*12,drag.span*50,1e-8);const minSpan=Math.max(drag.autoSpan*.02,Math.abs(drag.center)*1e-8,1e-8);const nextSpan=Math.max(minSpan,Math.min(maxSpan,drag.span*factor));st.priceRange={min:drag.center-nextSpan/2,max:drag.center+nextSpan/2}};canvas.addEventListener('wheel',event=>{if(st.drag){event.preventDefault();return}const total=st.rows.length;if(!total)return;const max=maxVisible();const current=ccChartClampVisible(st,total,max);const factor=event.deltaY<0 ? .82 : 1.22;const min=Math.min(max,20);const next=Math.round(Math.max(min,Math.min(max,current*factor)));if(next!==current){event.preventDefault();st.visibleCandles=next;const maxPan=Math.max(0,total-next),minPan=-Math.max(8,Math.floor(next*.45));st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));drawCcChart(canvasId)}},{passive:false});canvas.addEventListener('dblclick',event=>{if(event.button!==0)return;const p=pointFromEvent(event);if(!inPriceScale(p))return;event.preventDefault();event.stopPropagation();st.drag=null;if(ccChartAutoFitPriceRange(canvasId))drawCcChart(canvasId)});canvas.addEventListener('mousedown',event=>{if(event.button!==0)return;const p=pointFromEvent(event);const v=st.view;if(!v)return;const mode=inPriceScale(p)?'price-scale':(inTimeScale(p)?'time-scale':'pan');st.drag={mode,x:p.x,y:p.y,padL:v.padL,chartW:v.chartW,panOffset:v.panOffset,min:v.min,max:v.max,center:(v.min+v.max)/2,span:v.span,autoSpan:Math.max(1e-8,v.span),step:v.step,chartH:v.chartH,visible:v.visible,total:st.rows.length};canvas.style.cursor=mode==='price-scale'?'ns-resize':(mode==='time-scale'?'ew-resize':'grabbing');event.preventDefault()});window.addEventListener('mousemove',event=>{const drag=st.drag;if(!drag)return;const p=pointFromEvent(event);if(drag.mode==='price-scale')applyVerticalZoomFromDrag(drag,p);else if(drag.mode==='time-scale')applyHorizontalZoomFromDrag(drag,p);else{const total=st.rows.length;const visible=Math.max(1,Math.round(Number(drag.visible)||Number(st.visibleCandles)||total));st.visibleCandles=visible;const maxPan=Math.max(0,total-visible),minPan=-Math.max(8,Math.floor(visible*.45));const dx=p.x-drag.x,dy=p.y-drag.y;st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(drag.panOffset+dx/Math.max(1,drag.step))));const priceShift=dy/Math.max(1,drag.chartH)*drag.span;st.priceRange={min:drag.min+priceShift,max:drag.max+priceShift};st.crosshair={x:p.x,y:p.y}}drawCcChart(canvasId);event.preventDefault()});canvas.addEventListener('mousemove',event=>{if(st.drag)return;const p=pointFromEvent(event);st.crosshair=p;canvas.style.cursor=inPriceScale(p)?'ns-resize':(inTimeScale(p)?'ew-resize':(inPlot(p)?'grab':'crosshair'));drawCcChart(canvasId)});window.addEventListener('mouseup',()=>{if(st.drag){st.drag=null;canvas.style.cursor='grab'}});canvas.addEventListener('mouseleave',()=>{if(!st.drag){st.crosshair=null;canvas.style.cursor='crosshair';drawCcChart(canvasId)}})}
window.addEventListener('resize',()=>{drawCcChart('ccSpotChart');drawCcChart('ccFuturesChart')});

function ccSelectedText(id){const el=cc(id);return el&&el.options&&el.selectedIndex>=0?el.options[el.selectedIndex].textContent:(el?.value||'')}
async function loadCcChart(market){const symbol=ccChartSampleSymbol(market);const tf=ccChartTimeframe(market);const st=CC_CHART_STATE[ccChartCanvasId(market)];const chartKey=market+':'+symbol+':'+tf;const status=cc(ccChartStatusId(market));try{if(status)status.textContent='Loading chart…';const linearPromise=ccTradeChartUsesLinear()?ccTradeLoadLinearRows(market):Promise.resolve([]);const rows=await ccChartFetchBaseRows(market);await linearPromise;if(st){st.baseRows=rows.map(r=>({t:Number(r.t),o:Number(r.o),h:Number(r.h),l:Number(r.l),c:Number(r.c),v:r.v??null}));st.baseKey=chartKey}ccRenderWsChart(market,{ensureCurrent:true})}catch(e){if(status)status.textContent='Chart failed: '+e.message;ccRenderWsChart(market,{ensureCurrent:true})}}
function ccBookArray(source){return (Array.isArray(source)?source:[]).map((r,idx)=>{const isArr=Array.isArray(r);const p=ccNum(isArr?r[0]:(r.price??r.pr??r.px??r.p));const q=ccNum(isArr?r[1]:(r.amount??r.size??r.qty??r.sz??r.quantity));const total=ccNum(isArr?r[2]:(r.total??r.cumulative??r.sum));return {p,q,total,idx}}).filter(r=>r.p!==null&&r.q!==null&&r.p>0&&r.q>=0)}
function ccBookSort(rows,side){return rows.slice().sort((a,b)=>side==='ask'?((a.p-b.p)||(a.idx-b.idx)):((b.p-a.p)||(a.idx-b.idx)))}
function ccBookWithTotals(rows,side,depth=CC_BOOK_DEPTH){let total=0,quoteTotal=0;const sorted=ccBookSort(rows,side).slice(0,depth);return sorted.map(r=>{const q=Number.isFinite(r.q)?r.q:0;const quote=Number.isFinite(r.quote)?r.quote:(Number.isFinite(r.p)?q*r.p:NaN);total+=q;if(Number.isFinite(quote))quoteTotal+=quote;const runningTotal=Number.isFinite(r.total)?r.total:total;return {...r,total:runningTotal,quote:Number.isFinite(quote)?quote:null,quoteTotal:Number.isFinite(quoteTotal)?quoteTotal:null}})}
function ccBookSides(raw){const root=raw&&raw.data!==undefined?raw.data:raw;const askRows=ccBookArray(root&&(root.asks||root.a||root.ask||root.sell));const bidRows=ccBookArray(root&&(root.bids||root.b||root.bid||root.buy));return {askRows:ccBookSort(askRows,'ask'),bidRows:ccBookSort(bidRows,'bid'),asks:ccBookWithTotals(askRows,'ask'),bids:ccBookWithTotals(bidRows,'bid')}}
function ccFuturesTopOfBookFromRaw(raw){const rawSides=ccBookSides(raw);const askRows=ccFuturesVisibleBookRows(rawSides.askRows,'ask');const bidRows=ccFuturesVisibleBookRows(rawSides.bidRows,'bid');const ask=askRows[0]&&askRows[0].p,bid=bidRows[0]&&bidRows[0].p;return {ask:Number.isFinite(ask)?ask:NaN,bid:Number.isFinite(bid)?bid:NaN,askRows,bidRows,rawSides}}
function ccFuturesParts(){const label=String(ccSelectedText('ccFuturesInst')||cc('ccFuturesInst')?.value||'BTCUSDT-PERP').toUpperCase();const match=label.match(/^([A-Z0-9]+)USDT/);const base=match&&match[1]?match[1]:(label.endsWith('USD')?label.slice(0,-3):label);return {base,quote:'USDT'}}
function ccFuturesBookUnit(){return String(cc('ccFuturesAmountUnit')?.value||ccFuturesParts().base||'BTC').toUpperCase()}
const CC_FUTURES_BOOK_SEG_KEY='ccFuturesBookSegByBase.v1';
function ccFuturesBookSegBase(){return String(ccFuturesParts().base||'BTC').toUpperCase()}
function ccFuturesBookSegOptions(base=ccFuturesBookSegBase()){return String(base).toUpperCase()==='BTC'?['0.1','1','10']:['0.01','0.1','1']}
function ccFuturesLoadBookSegPrefs(){try{return JSON.parse(localStorage.getItem(CC_FUTURES_BOOK_SEG_KEY)||'{}')}catch(e){return {}}}
function ccFuturesSaveBookSegPref(base,value){const key=String(base||'').toUpperCase();if(!key||!value)return;const prefs=ccFuturesLoadBookSegPrefs();prefs[key]=String(value);try{localStorage.setItem(CC_FUTURES_BOOK_SEG_KEY,JSON.stringify(prefs))}catch(e){}}
function ccFuturesBookSegValue(base=ccFuturesBookSegBase()){const key=String(base||'').toUpperCase();const prefs=ccFuturesLoadBookSegPrefs();const options=ccFuturesBookSegOptions(key);const saved=String(prefs[key]||'');return options.includes(saved)?saved:options[0]}
function ccFuturesSyncBookPrecisionOptions(){const sel=cc('ccFuturesBookPrecision');if(!sel)return;const base=ccFuturesBookSegBase();const options=ccFuturesBookSegOptions(base);const next=ccFuturesBookSegValue(base);sel.innerHTML=options.map(value=>'<option value="'+value+'"'+(value===next?' selected':'')+'>'+value+'</option>').join('')}
function ccFuturesBookPrecisionText(){return String(cc('ccFuturesBookPrecision')?.value||'0.01')}
function ccFuturesBookSegStep(){const step=Number(ccFuturesBookPrecisionText());return Number.isFinite(step)&&step>0?step:0.01}
function ccFuturesBookPrecisionDigits(){const step=ccFuturesBookPrecisionText();const dot=step.indexOf('.');return dot>=0?step.length-dot-1:0}
function ccFuturesSyncAmountUnitOptions(){const sel=cc('ccFuturesAmountUnit');if(!sel)return;const {base}=ccFuturesParts();const current=String(sel.value||'').toUpperCase();const next=current&&current!=='USDT'?base:(current||base);sel.innerHTML='<option value="'+ccEsc(base)+'"'+(next===base?' selected':'')+'>'+ccEsc(base)+'</option><option value="USDT"'+(next==='USDT'?' selected':'')+'>USDT</option>'}
function ccFuturesBookHeadLabel(name,unit){return name+' <span class="sub">('+ccEsc(unit)+')</span>'}
function ccRenderFuturesBookHeadUi(){const unit=ccFuturesBookUnit();const amount=cc('ccFuturesBookAmountHead');const total=cc('ccFuturesBookTotalHead');if(amount)amount.innerHTML=ccFuturesBookHeadLabel('Amount',unit);if(total)total.innerHTML=ccFuturesBookHeadLabel('Total',unit)}
function ccFuturesBookPriceValue(n){const value=Number(n);const digits=ccFuturesBookPrecisionDigits();return Number.isFinite(value)?value.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits}):'—'}
function ccFuturesBookAmountDigits(symbol=''){const base=ccSpreadsFuturesBase(symbol||cc('ccFuturesInst')?.value||ccFuturesParts().base||'BTC');return String(base||'').toUpperCase()==='BTC'?3:2}
function ccFuturesBookAmountValue(n,symbol=''){const value=Number(n);const digits=ccFuturesBookAmountDigits(symbol);return Number.isFinite(value)?value.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits}):'—'}
function ccFuturesLeverageCurrent(){const direct=Number(cc('ccFuturesLeverageSelect')?.value||cc('ccFuturesLeverageSelect')?.dataset?.lastValue);if(Number.isFinite(direct)&&direct>0)return direct;const textValue=String(cc('ccFuturesLeverageValue')?.textContent||'').match(/([0-9.]+)/);const parsed=textValue?Number(textValue[1]):NaN;return Number.isFinite(parsed)&&parsed>0?parsed:NaN}
function ccFuturesAvailableMarginValue(){const rows=ccBalanceRows(ccLastAssetsSummary||{});const metrics=(ccLastAssetsSummary&&ccLastAssetsSummary.metrics)||{};return ccFirstFinite(metrics&&metrics.availableEquity,ccFindNumericDeep(ccLastAssetsSummary||{},['availableequity','availeq','availbalance','availablebalance','available']),ccHeaderStableSum(rows,'available'))}
function ccFuturesFormatTradeUnit(n,unit){const value=Number(n);const upper=String(unit||'').toUpperCase();const digits=upper==='BTC'?3:2;return Number.isFinite(value)?value.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits}):'—'}
let ccLastFuturesMaxAvailable=null;let ccFuturesMaxAvailablePromise=null;
function ccFuturesCurrentMarkPrice(){const sym=String(cc('ccFuturesInst')?.value||'').toUpperCase();const symKey=ccFuturesBookSymbolKey(sym);const rows=Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[];for(const p of rows){const keys=[ccPick(p,['instrument','symbol','displayName']),ccPick(p,['baseToken'])].map(ccFuturesBookSymbolKey).filter(Boolean);if(keys.includes(symKey)){const mark=Number(ccPick(p,['markPrice','lastPrice']));if(Number.isFinite(mark)&&mark>0)return mark}}const fallback=ccFuturesInstrumentMark(symKey);return Number.isFinite(fallback)&&fallback>0?fallback:NaN}
function ccFuturesCurrentBidAskMedian(){const bid=Number(CC_BOOK_STATE.top.futures.bid),ask=Number(CC_BOOK_STATE.top.futures.ask);const sym=String(cc('ccFuturesInst')?.value||'').toUpperCase();const key='main-futures:'+String(ccFuturesBookSymbolKey(sym)||sym||'');return ccFilteredBidAskMedian(key,bid,ask)}
let ccFuturesMaxAvailableLastAt=0;let ccFuturesMaxAvailableLastSig='';let ccFuturesMaxAvailableTimer=0;
async function ccLoadFuturesMaxAvailable(){const account=cc('ccAccountSelect')?.value||'';const symbol=cc('ccFuturesInst')?.value||'';const buyPrice=Number(CC_BOOK_STATE.top.futures.ask);const sellPrice=Number(CC_BOOK_STATE.top.futures.bid);const markPrice=ccFuturesCurrentMarkPrice();if(!account||!symbol||!Number.isFinite(buyPrice)||buyPrice<=0||!Number.isFinite(sellPrice)||sellPrice<=0||!Number.isFinite(markPrice)||markPrice<=0){ccLastFuturesMaxAvailable=null;ccRenderFuturesMaxOrderUi();return null}const request={symbol,buyPrice,sellPrice,markPrice};const sig=JSON.stringify([account,symbol,Math.round(buyPrice*100)/100,Math.round(sellPrice*100)/100,Math.round(markPrice*100)/100]);const now=Date.now();const cached=ccLastFuturesMaxAvailable&&ccLastFuturesMaxAvailable.data;if(ccFuturesMaxAvailablePromise)return ccFuturesMaxAvailablePromise;if(typeof ccSpreadsLatencyPriorityActive==='function'&&ccSpreadsLatencyPriorityActive()){clearTimeout(ccFuturesMaxAvailableTimer);ccFuturesMaxAvailableTimer=setTimeout(()=>ccLoadFuturesMaxAvailable().catch(()=>{}),2500);return cached||null}if(sig===ccFuturesMaxAvailableLastSig&&now-ccFuturesMaxAvailableLastAt<10000)return cached||null;if(now-ccFuturesMaxAvailableLastAt<1500){clearTimeout(ccFuturesMaxAvailableTimer);ccFuturesMaxAvailableTimer=setTimeout(()=>ccLoadFuturesMaxAvailable().catch(()=>{}),1500-(now-ccFuturesMaxAvailableLastAt));return cached||null}ccFuturesMaxAvailableLastAt=now;ccFuturesMaxAvailableLastSig=sig;ccFuturesMaxAvailablePromise=(async()=>{try{const res=await ccApi('/api/admin/coincall/futures/max-available?accountId='+encodeURIComponent(account),{method:'POST',body:JSON.stringify(request)});ccLastFuturesMaxAvailable={symbol,buyPrice,sellPrice,markPrice,data:res};ccRenderFuturesMaxOrderUi();return res}catch(e){ccLastFuturesMaxAvailable=null;ccRenderFuturesMaxOrderUi();return null}finally{ccFuturesMaxAvailablePromise=null}})();return ccFuturesMaxAvailablePromise}
function ccRenderFuturesMaxOrderUi(){const unit=String(cc('ccFuturesAmountUnit')?.value||ccFuturesParts().base||'BTC').toUpperCase();const maxLong=cc('ccFuturesMaxLong');const maxShort=cc('ccFuturesMaxShort');const snapshot=ccLastFuturesMaxAvailable&&ccLastFuturesMaxAvailable.data;const buyQty=Number(snapshot&&snapshot.buy&&snapshot.buy.qty);const sellQty=Number(snapshot&&snapshot.sell&&snapshot.sell.qty);const buyPrice=Number(snapshot&&snapshot.buy&&snapshot.buy.price||ccLastFuturesMaxAvailable&&ccLastFuturesMaxAvailable.buyPrice);const sellPrice=Number(snapshot&&snapshot.sell&&snapshot.sell.price||ccLastFuturesMaxAvailable&&ccLastFuturesMaxAvailable.sellPrice);const longValue=unit==='USDT'?(Number.isFinite(buyQty)&&Number.isFinite(buyPrice)?buyQty*buyPrice:NaN):buyQty;const shortValue=unit==='USDT'?(Number.isFinite(sellQty)&&Number.isFinite(sellPrice)?sellQty*sellPrice:NaN):sellQty;if(maxLong)maxLong.textContent='Max long '+ccFuturesFormatTradeUnit(longValue,unit)+' '+unit;if(maxShort)maxShort.textContent='Max short '+ccFuturesFormatTradeUnit(shortValue,unit)+' '+unit}
function ccFuturesBookScaledInt(value,scaleDigits){const n=Number(value);if(!Number.isFinite(n))return NaN;const fixed=Math.abs(n).toFixed(scaleDigits);const scaled=Number(fixed.replace(".",""));return n<0?-scaled:scaled}function ccFuturesBookBucketPrice(price,side){const p=Number(price);const step=ccFuturesBookSegStep();if(!Number.isFinite(p)||!Number.isFinite(step)||step<=0)return p;const digits=Math.max(0,ccFuturesBookPrecisionDigits());const scaleDigits=Math.max(digits,8);const scale=Math.pow(10,scaleDigits);const scaledStep=Math.max(1,Math.round(step*scale));const scaledPrice=ccFuturesBookScaledInt(p,scaleDigits);const scaledBucket=side==="ask"?Math.ceil(scaledPrice/scaledStep)*scaledStep:Math.floor(scaledPrice/scaledStep)*scaledStep;return Number((scaledBucket/scale).toFixed(digits))}function ccFuturesBookGap(current,next,side){const a=Number(current&&current.p),b=Number(next&&next.p);if(!Number.isFinite(a)||!Number.isFinite(b))return NaN;return side==='ask'?b-a:a-b}function ccFuturesVisibleBookRows(rows,side){const sorted=ccBookSort(Array.isArray(rows)?rows:[],side);if(sorted.length<2)return sorted;const gaps=[];for(let i=0;i<sorted.length-1&&gaps.length<12;i++){const gap=ccFuturesBookGap(sorted[i],sorted[i+1],side);if(Number.isFinite(gap)&&gap>0)gaps.push(gap)}const step=Math.max(ccFuturesBookSegStep(),1e-8);const baseline=gaps.length?gaps.slice().sort((a,b)=>a-b)[Math.floor((gaps.length-1)/2)]:step;const largeGap=Math.max(step,baseline)*8;for(let i=0;i<sorted.length-1;i++){const gap=ccFuturesBookGap(sorted[i],sorted[i+1],side);if(Number.isFinite(gap)&&gap>largeGap)return sorted.slice(0,i+1)}return sorted}function ccFuturesAggregateBookRows(rows,side){const buckets=new Map();for(const row of Array.isArray(rows)?rows:[]){const p=Number(row.p),q=Number(row.q);if(!Number.isFinite(p)||!Number.isFinite(q)||q<0)continue;const quote=Number.isFinite(row.quote)?Number(row.quote):q*p;const price=ccFuturesBookBucketPrice(p,side);const prev=buckets.get(price)||{p:price,q:0,quote:0,idx:row.idx};prev.q+=q;if(Number.isFinite(quote))prev.quote+=quote;prev.idx=Math.min(prev.idx,row.idx);buckets.set(price,prev)}return Array.from(buckets.values())}function ccFuturesDenseBookRows(rows,side){return ccBookSort(rows,side)}function ccPadBookRows(rows,side,market='spot',depth=CC_BOOK_DEPTH){const list=Array.isArray(rows)?rows.slice(0,depth):[];if(market!=='futures'||list.length>=depth)return list;const blanks=Array.from({length:depth-list.length},()=>null);return side==='ask'?blanks.concat(list):list.concat(blanks)}function ccRenderBookRows(rows,side,maxTotal,market='spot',depth=CC_BOOK_DEPTH,symbol=''){return ccPadBookRows(rows,side,market,depth).map(r=>{if(!r)return '<div class="okx-spot-book-row" aria-hidden="true"><span>&nbsp;</span><span class="okx-spot-book-col-right">&nbsp;</span><span class="okx-spot-book-col-right">&nbsp;</span></div>';const width=maxTotal>0?Math.max(2,Math.min(100,r.total/maxTotal*100)):0;const cls=side==='ask'?'okx-spot-ask':'okx-spot-bid';const color=side==='ask'?'#f87171':'#34d399';const markers=market==='spot'?ccSpotBookOrderMarkers(side,r.p,symbol):(market==='futures'?ccFuturesOrderBookMarkers(side,r.p,symbol):'');const markerHtml=markers?`<span class="okx-spot-order-markers">${markers}</span>`:'';const useQuoteUnit=market==='futures'&&!symbol&&ccFuturesBookUnit()==='USDT';const amount=useQuoteUnit?(Number.isFinite(r.quote)?r.quote:NaN):r.q;const total=useQuoteUnit?(Number.isFinite(r.quoteTotal)?r.quoteTotal:NaN):r.total;const amountText=market==='futures'?ccFuturesBookAmountValue(amount,symbol):ccFmt(r.q,8);const totalText=market==='futures'?ccFuturesBookAmountValue(total,symbol):ccFmt(r.total,8);const priceText=market==='futures'?ccFuturesBookPriceValue(r.p):ccFmt(r.p,ccPriceDecimals(r.p));return `<div class="okx-spot-book-row"><div class="okx-spot-depth ${side}" style="width:${width.toFixed(1)}%"></div><span class="okx-spot-book-price-cell ${cls}" data-cc-book-price="${r.p}"><span style="color:${color}">${ccEsc(priceText)}</span>${markerHtml}</span><span class="okx-spot-book-col-right">${ccEsc(amountText)}</span><span class="okx-spot-book-col-right">${ccEsc(totalText)}</span></div>`}).join('')}
function ccClearBook(market,message='No order book data'){const isF=market==='futures';const askHost=cc(isF?'ccFuturesAsks':'ccSpotAsks'),mid=cc(isF?'ccFuturesMid':'ccSpotMid'),bidHost=cc(isF?'ccFuturesBids':'ccSpotBids');CC_BOOK_STATE.top[market]={ask:NaN,bid:NaN};if(askHost)askHost.innerHTML='';if(bidHost)bidHost.innerHTML='';if(mid){mid.classList.remove('cc-futures-book-mid','buy','sell');mid.dataset.baseText=message;mid.textContent=message}}
function ccRenderBook(market,raw){CC_BOOK_STATE.raw[market]=raw;const isF=market==='futures';const ids=isF?['ccFuturesAsks','ccFuturesMid','ccFuturesBids']:['ccSpotAsks','ccSpotMid','ccSpotBids'];const top=isF?ccFuturesTopOfBookFromRaw(raw):null;const rawSides=top?top.rawSides:ccBookSides(raw);const askRows=top?top.askRows:rawSides.askRows;const bidRows=top?top.bidRows:rawSides.bidRows;CC_BOOK_STATE.top[market]={ask:top?top.ask:(askRows[0]&&Number.isFinite(askRows[0].p)?askRows[0].p:NaN),bid:top?top.bid:(bidRows[0]&&Number.isFinite(bidRows[0].p)?bidRows[0].p:NaN)};const asks=isF?ccBookWithTotals(ccFuturesDenseBookRows(ccFuturesAggregateBookRows(askRows,'ask'),'ask'),'ask'):rawSides.asks;const bids=isF?ccBookWithTotals(ccFuturesDenseBookRows(ccFuturesAggregateBookRows(bidRows,'bid'),'bid'),'bid'):rawSides.bids;const maxTotal=Math.max(0,...asks.map(r=>r.total),...bids.map(r=>r.total));const askHost=cc(ids[0]),mid=cc(ids[1]),bidHost=cc(ids[2]);if(!isF)ccRenderSpotBookHeadUi();if(askHost)askHost.innerHTML=ccRenderBookRows(asks.slice().reverse(),'ask',maxTotal,market);if(bidHost)bidHost.innerHTML=ccRenderBookRows(bids,'bid',maxTotal,market);const spotTrade=!isF?ccSpotLiveLastTradeInfo():{price:NaN,side:''};const futuresTrade=isF?ccFuturesLiveLastTradeInfo():{price:NaN,side:''};if(mid){mid.classList.remove('cc-futures-book-mid','buy','sell');let baseText='Loading last trade…';if(isF){baseText=Number.isFinite(futuresTrade.price)?ccFuturesBookPriceValue(futuresTrade.price):'...';if(futuresTrade.side==='buy'||futuresTrade.side==='sell')mid.classList.add(futuresTrade.side)}else if(Number.isFinite(spotTrade.price)){baseText=ccFmt(spotTrade.price,ccPriceDecimals(spotTrade.price));if(spotTrade.side==='buy'||spotTrade.side==='sell')mid.classList.add(spotTrade.side)}mid.dataset.baseText=baseText;mid.innerHTML=!isF&&Number.isFinite(spotTrade.price)&&spotTrade.side?'<span class="okx-spot-mid-trend">'+(spotTrade.side==='buy'?'▲':'▼')+'</span><span class="okx-spot-mid-price">'+ccEsc(baseText)+'</span>':ccEsc(baseText)}if(isF){ccFuturesAppendMarkPriceToBookMid();ccLoadFuturesMaxAvailable().catch(()=>{});ccRenderFuturesMaxOrderUi()}}
function ccBookTransportSymbol(market){return String(cc(market==='futures'?'ccFuturesInst':'ccSpotInst')?.value||'').trim()}
function ccBookDisplaySymbol(market){return ccSelectedText(market==='futures'?'ccFuturesInst':'ccSpotInst')}
function ccBookRememberSelection(market){const nextTransport=ccBookTransportSymbol(market);if(CC_BOOK_STATE.transport[market]!==nextTransport){CC_BOOK_STATE.raw[market]=null;if(market==='spot')ccSpotBookResetCache()}CC_BOOK_STATE.transport[market]=nextTransport;CC_BOOK_STATE.display[market]=ccBookDisplaySymbol(market)}
function ccBookMidEl(market){return cc(market==='futures'?'ccFuturesMid':'ccSpotMid')}
function ccBookSetMidMessage(market,msg){const mid=ccBookMidEl(market);if(!mid)return;mid.textContent=market==='futures'&&/(authorizing|connecting|loading order book|reconnecting order book)/i.test(String(msg||''))?'...':msg}
function ccBookSendJson(ws,payload){if(ws&&ws.readyState===WebSocket.OPEN)ws.send(JSON.stringify(payload))}
function ccBookSocketOpen(market){const ws=CC_BOOK_STATE.sockets[market];return !!(ws&&ws.readyState===WebSocket.OPEN)}
function ccBookSocketBusy(market){const ws=CC_BOOK_STATE.sockets[market];return !!(ws&&(ws.readyState===WebSocket.OPEN||ws.readyState===WebSocket.CONNECTING))}
function ccBookClearReconnect(market){if(CC_BOOK_STATE.reconnect[market]){clearTimeout(CC_BOOK_STATE.reconnect[market]);CC_BOOK_STATE.reconnect[market]=null}}
function ccBookClearHeartbeat(market){if(CC_BOOK_STATE.heartbeat[market]){clearInterval(CC_BOOK_STATE.heartbeat[market]);CC_BOOK_STATE.heartbeat[market]=null}}
function ccBookClearStale(market){if(CC_BOOK_STATE.stale[market]){clearTimeout(CC_BOOK_STATE.stale[market]);CC_BOOK_STATE.stale[market]=null}}
function ccBookTouch(market){CC_BOOK_STATE.lastMessageTs[market]=Date.now();ccBookClearStale(market);CC_BOOK_STATE.stale[market]=setTimeout(()=>{if(!CC_BOOK_STATE.wantReconnect[market])return;ccBookSetMidMessage(market,'Order book stale. Reconnecting…');ccBookCloseSocket(market,true)},CC_BOOK_STALE_MS)}
function ccSpotBookChannel(symbol){return 'market.'+symbol+'.mbp.'+CC_SPOT_BOOK_LEVEL}
function ccSpotChartKlineChannel(symbol){return 'market.'+symbol+'.kline.1min'}
function ccSpotOverviewChannel(symbol){return 'market.'+symbol+'.overviewv2'}
function ccSpotBookResetCache(channel=''){CC_BOOK_STATE.spotCache={channel,asks:new Map(),bids:new Map()}}
async function ccFuturesBookAuth(force=false){const cached=CC_BOOK_STATE.futuresAuth;if(!force&&cached.url&&Date.now()<cached.expiresAt)return {url:cached.url};if(cached.promise)return cached.promise;cached.promise=(async()=>{const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';const res=await ccApi('/api/admin/coincall/futures/ws-auth'+qs);const url=String(res&&res.url||'').trim();if(!url||!url.startsWith('wss://ws.coincall.com/futures?'))throw new Error('CoinCall futures WS auth URL missing');cached.url=url;const expires=Date.parse(res.expiresAtUtc||'');cached.expiresAt=Number.isFinite(expires)?expires:Date.now()+20000;return {url}})();try{return await cached.promise}finally{cached.promise=null}}
function ccFuturesBookSocketUrl(auth){return auth&&auth.url?auth.url:''}
function ccSpotBookApplySide(side,levels){const book=CC_BOOK_STATE.spotCache[side];for(const level of Array.isArray(levels)?levels:[]){if(!Array.isArray(level)||level.length<2)continue;const price=String(level[0]);const size=Number(level[1]);if(!Number.isFinite(size))continue;if(size<=0)book.delete(price);else book.set(price,size)}}
function ccSpotBookMaterializeSide(side){const rows=Array.from(CC_BOOK_STATE.spotCache[side].entries()).map(([p,q])=>[Number(p),q]).filter(([p,q])=>Number.isFinite(p)&&Number.isFinite(q)&&p>0&&q>=0);return rows.sort((a,b)=>side==='asks'?a[0]-b[0]:b[0]-a[0]).slice(0,CC_SPOT_BOOK_LEVEL)}
function ccSpotBookRaw(){return {asks:ccSpotBookMaterializeSide('asks'),bids:ccSpotBookMaterializeSide('bids')}}
function ccSpotOverviewLastPrice(tick){for(const key of ['lastPrice','last','closePrice','price','c']){const v=Number(tick&&tick[key]);if(Number.isFinite(v)&&v>0)return v}return NaN}
function ccSpotKlineHandleMessage(msg){const channel=String(msg&&msg.ch||'');const tick=msg&&msg.tick;const symbol=String(CC_BOOK_STATE.transport.spot||'').trim();if(!channel||!tick||channel!==ccSpotChartKlineChannel(symbol))return false;const endTime=Number(tick.endTime),open=Number(tick.open),high=Number(tick.high),low=Number(tick.low),close=Number(tick.close);if([endTime,open,high,low,close].every(Number.isFinite)){const bucketEnd=endTime+(CC_INTERVAL_MS['1m']||60000)-1;ccPushWsChartPrice('spot',open,endTime,symbol);ccPushWsChartPrice('spot',high,endTime+1,symbol);ccPushWsChartPrice('spot',low,endTime+2,symbol);ccPushWsChartPrice('spot',close,bucketEnd,symbol);const prev=CC_BOOK_STATE.spotLastTrade&&CC_BOOK_STATE.spotLastTrade.symbol===symbol?Number(CC_BOOK_STATE.spotLastTrade.price):NaN;let side=CC_BOOK_STATE.spotLastTrade&&CC_BOOK_STATE.spotLastTrade.symbol===symbol?CC_BOOK_STATE.spotLastTrade.side:'';if(Number.isFinite(prev)){if(close>prev)side='buy';else if(close<prev)side='sell'}CC_BOOK_STATE.spotLastTrade={symbol,price:close,side,ts:bucketEnd};if(CC_BOOK_STATE.raw.spot)ccRenderBook('spot',CC_BOOK_STATE.raw.spot)}ccBookTouch('spot');return true}
function ccSpotOverviewHandleMessage(msg){const channel=String(msg&&msg.ch||'');const tick=msg&&msg.tick;if(!channel||!tick||channel!==ccSpotOverviewChannel(CC_BOOK_STATE.transport.spot))return false;ccBookTouch('spot');return true}
function ccSpotTradeDetailHandleMessage(msg){const channel=String(msg&&msg.ch||'');const tick=msg&&msg.tick;if(!channel||!Array.isArray(tick)||channel!==ccSpotTradeDetailChannel(CC_BOOK_STATE.transport.spot))return false;const symbol=String(CC_BOOK_STATE.transport.spot||'').trim();const first=tick.filter(t=>String(t&&t.symbol||symbol).trim()===symbol).sort((a,b)=>Number(b&&b.ts||0)-Number(a&&a.ts||0))[0];if(first){const price=Number(first.price),sideRaw=Number(first.side),ts=Number(first.ts||0);if(Number.isFinite(price)){const side=sideRaw===1?'buy':(sideRaw===2?'sell':'');CC_BOOK_STATE.spotLastTrade={symbol,price,side,ts:Number.isFinite(ts)?ts:Date.now()};if(CC_BOOK_STATE.raw.spot)ccRenderBook('spot',CC_BOOK_STATE.raw.spot)}}ccBookTouch('spot');return true}
function ccFuturesTradeMessageHandle(msg){const dt=Number(msg&&msg.dt),symbol=String(CC_BOOK_STATE.transport.futures||cc('ccFuturesInst')?.value||'').trim();if(!symbol||!(dt===33||dt===43))return false;const rows=Array.isArray(msg&&msg.d)?msg.d:[msg&&msg.d].filter(Boolean);const first=rows.filter(t=>String(ccPick(t,['s','symbol'])||'').trim()===symbol).sort((a,b)=>Number(ccFuturesTradeTs(b)||0)-Number(ccFuturesTradeTs(a)||0))[0];if(first){const price=ccFuturesTradePrice(first),side=ccFuturesTradeSideRaw(first),ts=ccFuturesTradeTs(first);if(Number.isFinite(price)){const tradeTs=Number.isFinite(ts)?ts:Date.now();CC_BOOK_STATE.futuresLastTrade={symbol,price,side,ts:tradeTs};ccPushWsChartPrice('futures',price,tradeTs,symbol);if(CC_BOOK_STATE.raw.futures&&CC_BOOK_STATE.transport.futures===symbol)ccRenderBook('futures',CC_BOOK_STATE.raw.futures)}}ccBookTouch('futures');return true}
let ccFuturesPrivateRefreshTimer=null;let ccFuturesPositionEventReconcileTimer=null;let ccFuturesTradeHistoryRefreshTimers=[];let ccFuturesPrivateLastReconcileAt=0;let ccSpotForceReplaceOpenOrdersUntil=0;let ccSpotProtectOpenOrdersUntil=0;const CC_FUTURES_PRIVATE_RECONCILE_MS=120000;const CC_FUTURES_REST_REPLACE_GUARD_MS=15000;const CC_SPOT_REST_REPLACE_GUARD_MS=15000;const CC_FUTURES_FILL_NOTIFY_STATE=new Map();const CC_FUTURES_TERMINAL_ORDER_KEYS=new Map();const CC_SPOT_TERMINAL_ORDER_KEYS=new Map();const CC_SPOT_TERMINAL_ORDER_TTL_MS=180000;
function ccScheduleFuturesPrivateRefresh(delay=CC_FUTURES_PRIVATE_RECONCILE_MS){clearTimeout(ccFuturesPrivateRefreshTimer);const now=Date.now(),elapsed=now-ccFuturesPrivateLastReconcileAt,wait=Math.max(Number(delay)||0,CC_FUTURES_PRIVATE_RECONCILE_MS-elapsed,0);ccFuturesPrivateRefreshTimer=setTimeout(async()=>{ccFuturesPrivateRefreshTimer=null;ccFuturesPrivateLastReconcileAt=Date.now();await Promise.allSettled([loadCcFuturesOpenOrders(),loadCcFuturesPositions()])},wait)}
function ccScheduleFuturesPositionEventReconcile(delay=900){clearTimeout(ccFuturesPositionEventReconcileTimer);ccFuturesPositionEventReconcileTimer=setTimeout(async()=>{ccFuturesPositionEventReconcileTimer=null;if(document.hidden)return;ccFuturesPrivateLastReconcileAt=Date.now();await loadCcFuturesPositions().catch(()=>{})},Math.max(0,Number(delay)||0))}
function ccFirstValue(o,names){for(const n of names){if(o&&o[n]!==undefined&&o[n]!==null&&o[n]!=='')return o[n]}return undefined}
function ccFuturesOrderIdentity(o){return {orderId:ccOrderFirstId(o,['orderId','ordId','order_id','id','oid']),clientOrderId:ccOrderFirstId(o,['clientOrderId','clientOid','clOrdId','coid'])}}
function ccFuturesOrderIdentityKeys(o){const ids=ccFuturesOrderIdentity(o),keys=[];if(ids.orderId){keys.push('oid:'+ids.orderId);keys.push('id:'+ids.orderId)}if(ids.clientOrderId){keys.push('cid:'+ids.clientOrderId);keys.push('id:'+ids.clientOrderId)}return keys}
function ccFuturesOrderHasIdentity(o){const ids=ccFuturesOrderIdentity(o);return !!(ids.orderId||ids.clientOrderId)}
function ccFuturesOrderSame(a,b){const ak=ccFuturesOrderIdentityKeys(a),bk=ccFuturesOrderIdentityKeys(b);if(ak.length&&bk.length){if(ak.some(k=>bk.includes(k)))return true;const sa=ccFuturesOrderShapeKey(a),sb=ccFuturesOrderShapeKey(b);return !!(sa&&sb&&sa===sb&&((a&&a.ccOptimistic)||(b&&b.ccOptimistic)))}if(ak.length||bk.length){const sa=ccFuturesOrderShapeKey(a),sb=ccFuturesOrderShapeKey(b);return !!(sa&&sb&&sa===sb&&(!ccFuturesOrderHasIdentity(a)||!ccFuturesOrderHasIdentity(b)))}const sa=ccFuturesOrderShapeKey(a),sb=ccFuturesOrderShapeKey(b);return !!(sa&&sb&&sa===sb)}
function ccFuturesOrderTerminal(o){const raw=ccFirstValue(o,['os','status','state','orderStatus','orderState']);const key=String(raw??'').trim().toUpperCase().replace(/[\s_-]+/g,'');const label=String(ccOrderStatusLabel(o)||'').trim().toUpperCase().replace(/[\s_-]+/g,'');if(['FILLED','FULLFILLED','FULLYFILLED','CANCELED','CANCELLED','CANCELBYEXERCISE','INVALID','REJECTED','EXPIRED'].includes(key)||['FILLED','FULLFILLED','FULLYFILLED','CANCELED','CANCELLED','CANCELBYEXERCISE','INVALID','REJECTED','EXPIRED'].includes(label))return true;const total=Number(ccFirstValue(o,['qty','quantity','amount','size','orderQty','origQty','volume','q','sz']));const filled=Number(ccFirstValue(o,['fillQty','filledQty','filledQuantity','filled','dealQty','cumQty','fq']));if(Number.isFinite(total)&&total>0&&Number.isFinite(filled)&&filled>=total-Math.max(1e-12,total*1e-10))return true;const left=Number(ccFirstValue(o,['remainQty','remainingQty','leavesQty','leftQty','unfilledQty']));return Number.isFinite(left)&&left<=0&&Number.isFinite(filled)&&filled>0&&(!Number.isFinite(total)||total<=0)}
function ccFuturesPrivateOrderTerminal(o){return ccFuturesOrderTerminal(o)}
function ccFuturesOrderRawStatusKey(o){for(const k of ['os','status','state','orderStatus','orderState']){const v=o&&o[k];if(v!==undefined&&v!==null&&String(v).trim()!=='')return String(v).trim().toUpperCase().replace(/[\s_-]+/g,'')}return ''}
function ccFuturesOrderNotifyKey(o){const ids=ccFuturesOrderIdentity(o);return ids.orderId?('oid:'+ids.orderId):(ids.clientOrderId?('cid:'+ids.clientOrderId):'')}
function ccFuturesOrderTotalQty(o){const n=Number(ccFirstValue(o,['qty','quantity','amount','size','orderQty','origQty','volume','q','sz']));return Number.isFinite(n)?n:0}
function ccFuturesFilledQtyForNotify(o){const filled=ccOrderFilledQty(o);if(filled>0)return filled;const key=ccFuturesOrderRawStatusKey(o);if(['1','FILLED','FULLFILLED','FULLYFILLED'].includes(key)){const total=ccFuturesOrderTotalQty(o);if(total>0)return total}return 0}
function ccFuturesOrderFillNotifyText(o,qty){return ccOrderSide(o)+' '+ccFirst(ccPick(o,['displaySymbol','displayName','symbol','instId','instrument']))+' filled '+ccEsc(qty)}
function ccFuturesNotifyFillOnce(o,beforeQty=0,statusId='ccFuturesOrderStatus',reason=''){if(!CC_TRADE_SETTINGS.filledNotification||!o)return false;const key=ccFuturesOrderNotifyKey(o);if(!key)return false;const after=ccFuturesFilledQtyForNotify(o),before=Number(beforeQty)||0,last=Number((CC_FUTURES_FILL_NOTIFY_STATE.get(key)||{}).qty)||0;if(!(after>Math.max(before,last)+1e-12))return false;CC_FUTURES_FILL_NOTIFY_STATE.set(key,{qty:after,ts:Date.now(),reason});ccPlayFillSound();ccSetStatus(statusId,'Filled: '+ccFuturesOrderFillNotifyText(o,after));if('Notification' in window&&Notification.permission==='granted')new Notification('CoinCall fill',{body:ccFuturesOrderFillNotifyText(o,after)});return true}
function ccFuturesTradeOrderNotifySuppressed(t){const ids=ccFuturesOrderIdentity(t),keys=[];if(ids.orderId)keys.push('oid:'+ids.orderId);if(ids.clientOrderId)keys.push('cid:'+ids.clientOrderId);const now=Date.now();return keys.some(k=>{const hit=CC_FUTURES_FILL_NOTIFY_STATE.get(k);return hit&&now-hit.ts<60000})}
function ccFuturesNotifyTradeRowsImmediate(prev,next){if(!CC_TRADE_SETTINGS.filledNotification||!Array.isArray(next)||!next.length)return false;const prevKeys=new Set((Array.isArray(prev)?prev:[]).map(ccSpotTradeKey));const fresh=next.filter(t=>!prevKeys.has(ccSpotTradeKey(t))&&!ccFuturesTradeOrderNotifySuppressed(t)).slice(0,3);if(!fresh.length)return false;const text=fresh.map(ccSpotTradeNotifyText).join(' · ');ccPlayFillSound();ccSetStatus('ccFuturesOrderStatus','Filled: '+text);if('Notification' in window&&Notification.permission==='granted')new Notification('CoinCall fill',{body:text});return true}
function ccScheduleFuturesTradeHistoryRefreshBurst(reason=''){ccFuturesTradeHistoryRefreshTimers.forEach(clearTimeout);ccFuturesTradeHistoryRefreshTimers=[0,500,1500,3500].map(d=>setTimeout(()=>{if(document.hidden)return;loadCcFuturesTradeHistory({notify:false,reason}).catch(()=>{})},d));try{window.CC_FUTURES_TRADE_HISTORY_REFRESHES=window.CC_FUTURES_TRADE_HISTORY_REFRESHES||[];window.CC_FUTURES_TRADE_HISTORY_REFRESHES.push({ts:new Date().toISOString(),reason});if(window.CC_FUTURES_TRADE_HISTORY_REFRESHES.length>40)window.CC_FUTURES_TRADE_HISTORY_REFRESHES.shift()}catch(_){}}
function ccFuturesOrderExplicitTerminal(o){const key=ccFuturesOrderRawStatusKey(o);if(!key)return false;return ['1','3','6','10','FILLED','FULLFILLED','FULLYFILLED','CANCELED','CANCELLED','CANCELBYEXERCISE','INVALID','REJECTED','EXPIRED'].includes(key)}
function ccFuturesOrderExplicitOpen(o){const key=ccFuturesOrderRawStatusKey(o);if(!key)return false;return ['-10','-2','-1','0','2','4','5','NEW','OPEN','PARTIAL','PARTIALLYFILLED','PARTIALLY_FILLED','PRECANCEL','CANCELING','WAITINGEFFECT','PRE'].includes(key)}
function ccNormalizeFuturesPrivateOrder(d){const symbol=ccFirstValue(d,['displaySymbol','displayName','symbol','instId','instrument','s']);const side=ccFirstValue(d,['tradeSide','side','sd']);const status=ccFirstValue(d,['status','state','orderStatus','os']);const price=ccFirstValue(d,['price','px','orderPrice','p']);const avgPrice=ccFirstValue(d,['avgPrice','averagePrice','dealAvgPrice','filledAvgPrice','ap']);const qty=ccFirstValue(d,['qty','quantity','amount','size','orderQty','q','sz']);const filled=ccFirstValue(d,['fillQty','filledQty','filledQuantity','filled','dealQty','fq']);const type=ccFirstValue(d,['tradeType','orderType','type','ot']);const ts=ccFirstValue(d,['ts','updateTime','createTime','createdTime','time','t']);const ids=ccFuturesOrderIdentity(d);return {...d,orderId:ids.orderId||d.orderId,clientOrderId:ids.clientOrderId||d.clientOrderId,displaySymbol:symbol??d.displaySymbol,symbol:symbol??d.symbol,tradeSide:side??d.tradeSide,side:side??d.side,status:status??d.status,orderStatus:status??d.orderStatus,price:price??d.price,px:price??d.px,avgPrice:avgPrice??d.avgPrice,qty:qty??d.qty,quantity:qty??d.quantity,fillQty:filled??d.fillQty,filledQty:filled??d.filledQty,tradeType:type??d.tradeType,orderType:type??d.orderType,updateTime:ts??d.updateTime,createTime:ts??d.createTime}}
function ccFuturesPrivateOrderLooksOpen(o){const ids=ccFuturesOrderIdentity(o),symbol=ccFirstValue(o,['displaySymbol','displayName','symbol','instId','instrument','ticker_id','s']);if(!ids.orderId&&!ids.clientOrderId)return false;if(!symbol)return false;if(ccFuturesPrivateOrderTerminal(o))return false;const side=ccSpotOrderSideRaw(o),qty=Number(ccFirstValue(o,['qty','quantity','amount','size','orderQty','origQty','volume','q','sz','remainQty','remainingQty','leavesQty','leftQty','unfilledQty'])),price=Number(ccFirstValue(o,['price','px','orderPrice','limitPrice','p']));return (side==='buy'||side==='sell')&&Number.isFinite(qty)&&qty>0&&Number.isFinite(price)&&price>0}
function ccFuturesPrivatePositionIdentity(p){const id=String(ccFirstValue(p,['positionId','positionID','posId','id'])??'').trim();if(id)return 'id:'+id;const symbol=ccFirstValue(p,['displaySymbol','displayName','symbol','instId','instrument','s']);const side=String(ccFirstValue(p,['tradeSide','side','positionSide','sd'])??'').trim();const key=ccFuturesBookSymbolKey(symbol||'');return key?('sym:'+key+'|'+side):''}
function ccNormalizeFuturesPrivatePosition(d){const symbol=ccFirstValue(d,['displaySymbol','displayName','symbol','instId','instrument','s']);const side=ccFirstValue(d,['tradeSide','side','positionSide','sd']);const qty=ccFirstValue(d,['qty','quantity','amount','size','positionAmt','positionQty','q','sz']);const value=ccFirstValue(d,['value','positionValue','notional','notionalValue']);const avgPrice=ccFirstValue(d,['avgPrice','entryPrice','averagePrice','openPrice']);const markPrice=ccFirstValue(d,['markPrice','mark','mp']);const lastPrice=ccFirstValue(d,['lastPrice','price','px']);const upnl=ccFirstValue(d,['upnlByLastPrice','upnl','unrealizedPnl','unrealizedPNL','unrealisedPnl']);const roi=ccFirstValue(d,['roiByLastPrice','roi']);const delta=ccFirstValue(d,['delta']);const initMargin=ccFirstValue(d,['initMargin','initialMargin','tokenInitMargin','im']);const maintMargin=ccFirstValue(d,['maintMargin','maintenanceMargin','tokenMaintMargin','mm']);const leverage=ccFirstValue(d,['leverage','lever','level','currentLeverage','positionLeverage']);const ts=ccFirstValue(d,['ts','updateTime','createTime','createdTime','time','t']);return {...d,displaySymbol:symbol??d.displaySymbol,symbol:symbol??d.symbol,tradeSide:side??d.tradeSide,side:side??d.side,qty:qty??d.qty,quantity:qty??d.quantity,value:value??d.value,avgPrice:avgPrice??d.avgPrice,markPrice:markPrice??d.markPrice,lastPrice:lastPrice??d.lastPrice,upnlByLastPrice:upnl??d.upnlByLastPrice,upnl:upnl??d.upnl,roiByLastPrice:roi??d.roiByLastPrice,roi:roi??d.roi,delta:delta??d.delta,initMargin:initMargin??d.initMargin,maintMargin:maintMargin??d.maintMargin,leverage:leverage??d.leverage,updateTime:ts??d.updateTime,createTime:ts??d.createTime}}
function ccFuturesPrivatePositionClosed(p){const raw=String(ccFirstValue(p,['status','state','positionStatus'])??'').trim().toUpperCase().replace(/[\s_-]+/g,'');if(['CLOSED','CLOSE','EMPTY','NONE'].includes(raw))return true;const qty=Number(ccFirstValue(p,['qty','quantity','amount','size','positionAmt','positionQty','q','sz']));return Number.isFinite(qty)&&Math.abs(qty)<=1e-12}
function ccApplyFuturesPrivatePositionEvent(raw){const rows=Array.isArray(raw)?raw:[raw].filter(Boolean);let changed=false;for(const d of rows){if(!d||typeof d!=='object')continue;const pos=ccNormalizeFuturesPrivatePosition(d);const key=ccFuturesPrivatePositionIdentity(pos);if(!key)continue;const list=Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions.slice():[];const idx=list.findIndex(p=>ccFuturesPrivatePositionIdentity(p)===key);if(ccFuturesPrivatePositionClosed(pos)){if(idx>=0){list.splice(idx,1);ccLastFuturesPositions=list;changed=true}continue}if(idx>=0)list[idx]={...list[idx],...pos};else list.unshift(pos);ccLastFuturesPositions=list;ccFuturesPositionsLoaded=true;changed=true}if(changed){ccRenderFuturesBottomPanels();ccFuturesAppendMarkPriceToBookMid();ccLoadFuturesMaxAvailable().catch(()=>{});drawCcChart('ccFuturesChart')}return changed}
function ccFuturesPrivateTradeKey(t){return [ccFirstValue(t,['tradeId','dealId','fillId','matchId','id','tid']),ccFirstValue(t,['orderId','ordId','clientOrderId','clientOid','clOrdId']),ccTradeHistoryTimeMs(t),ccFirstValue(t,['price','fillPrice','filledPrice','px','tradePrice','dealPrice','matchPrice']),ccFirstValue(t,['qty','quantity','amount','fillQty','filledQty','filledQuantity','dealQty','baseQty'])].join('|')}
function ccNormalizeFuturesPrivateTrade(d){const symbol=ccFirstValue(d,['displaySymbol','displayName','symbol','instId','instrument','s']);const side=ccFirstValue(d,['tradeSide','side','orderSide','direction','sd','si']);const price=ccFirstValue(d,['price','fillPrice','filledPrice','px','tradePrice','dealPrice','matchPrice','mpr']);const qty=ccFirstValue(d,['qty','quantity','amount','fillQty','filledQty','filledQuantity','dealQty','baseQty','q','sz']);const ts=ccFirstValue(d,['time','ts','tradeTime','fillTime','createTime','createdTime','updateTime','t']);const tradeId=ccFirstValue(d,['tradeId','dealId','fillId','matchId','tid','id']);const ids=ccFuturesOrderIdentity(d);const fee=ccFirstValue(d,['fee','fees','commission']);const feeCurrency=ccFirstValue(d,['feeCurrency','feeCcy','commissionCurrency','commissionAsset']);const rpnl=ccFirstValue(d,['rpnl','realizedPnl','realizedPNL']);const isTaker=ccFirstValue(d,['isTaker','taker','makerTaker']);return {...d,tradeId:tradeId??d.tradeId,dealId:d.dealId??tradeId,orderId:ids.orderId||d.orderId,clientOrderId:ids.clientOrderId||d.clientOrderId,displaySymbol:symbol??d.displaySymbol,symbol:symbol??d.symbol,tradeSide:side??d.tradeSide,side:side??d.side,price:price??d.price,px:price??d.px,qty:qty??d.qty,quantity:qty??d.quantity,fillQty:qty??d.fillQty,filledQty:qty??d.filledQty,time:ts??d.time,ts:ts??d.ts,fee:fee??d.fee,feeCurrency:feeCurrency??d.feeCurrency,rpnl:rpnl??d.rpnl,isTaker:isTaker??d.isTaker,ccWsTrade:true}}
function ccFuturesPrivateTradeLooksLikeFill(t){const tradeId=ccFirstValue(t,['tradeId','dealId','fillId','matchId','tid','id']);if(tradeId)return true;const qty=Number(ccFirstValue(t,['qty','quantity','amount','fillQty','filledQty','filledQuantity','dealQty','baseQty','q','sz']));const price=Number(ccFirstValue(t,['price','fillPrice','filledPrice','px','tradePrice','dealPrice','matchPrice','mpr']));return Number.isFinite(qty)&&qty>0&&Number.isFinite(price)&&price>0}
function ccApplyFuturesPrivateTradeEvent(raw){const rows=Array.isArray(raw)?raw:[raw].filter(Boolean);let changed=false;const list=Array.isArray(ccLastFuturesTradeHistory)?ccLastFuturesTradeHistory.slice():[];const seen=new Set(list.map(ccFuturesPrivateTradeKey));for(const d of rows){if(!d||typeof d!=='object')continue;const trade=ccNormalizeFuturesPrivateTrade(d);if(!ccFuturesPrivateTradeLooksLikeFill(trade))continue;const key=ccFuturesPrivateTradeKey(trade);if(!key||seen.has(key))continue;seen.add(key);ccOrderDiagnosticPush('futures-ws/fill',{order:trade,action:'unique-fill',message:'unique private futures fill received'});list.unshift(trade);changed=true}if(!changed)return false;const prev=ccLastFuturesTradeHistory;ccLastFuturesTradeHistory=list.sort((a,b)=>ccTradeHistoryTimeMs(b)-ccTradeHistoryTimeMs(a)).slice(0,100);ccFuturesNotifyTradeRowsImmediate(prev,ccLastFuturesTradeHistory);ccRenderFuturesBottomPanels();drawCcChart('ccFuturesChart');return true}
const CC_ORDER_DIAGNOSTIC_PAGE_SIZE=8,CC_ORDER_DIAGNOSTIC_MAX=300;let ccOrderDiagnosticPage=1,ccOrderDiagnosticLastHtml='';
function ccOrderDiagnosticRows(){window.CC_ORDER_DIAGNOSTICS=Array.isArray(window.CC_ORDER_DIAGNOSTICS)?window.CC_ORDER_DIAGNOSTICS:[];return window.CC_ORDER_DIAGNOSTICS}
function ccOrderDiagFirst(o,names){if(typeof ccFirstValue==='function')return ccFirstValue(o,names);for(const n of names){if(o&&o[n]!==undefined&&o[n]!==null&&String(o[n]).trim()!=='')return o[n]}return undefined}
function ccOrderDiagText(v){return v===undefined||v===null?'':String(v).slice(0,320)}
function ccOrderDiagSymbol(o){return ccOrderDiagText(ccOrderDiagFirst(o,['symbol','displaySymbol','displayName','instId','instrument','ticker_id','s','pair']))}
function ccOrderDiagSide(o){let v=ccOrderDiagFirst(o,['side','tradeSide','orderSide','direction','sd','si']);v=String(v??'').trim().toUpperCase();if(v==='1')return 'BUY';if(v==='2')return 'SELL';return v}
function ccOrderDiagStatus(o){let v=ccOrderDiagFirst(o,['status','state','orderStatus','orderState','order_state','os']);if((v===undefined||v===null||String(v).trim()==='')&&typeof ccOrderStatusLabel==='function')v=ccOrderStatusLabel(o);return ccOrderDiagText(v).toUpperCase()}
function ccOrderDiagIds(o){const ids=typeof ccFuturesOrderIdentity==='function'?ccFuturesOrderIdentity(o||{}):{};return {orderId:ccOrderDiagText((ids&&ids.orderId)||ccOrderDiagFirst(o,['orderId','ordId','order_id','id','oid','orderNo'])),clientOrderId:ccOrderDiagText((ids&&ids.clientOrderId)||ccOrderDiagFirst(o,['clientOrderId','clientOid','clOrdId','coid']))}}
function ccOrderDiagFieldText(v){if(v===undefined||v===null||v==='')return '';if(typeof v==='number'&&!Number.isFinite(v))return '';return String(v).replace(/[\t\r\n]+/g,' ').slice(0,120)}
function ccOrderDiagMessage(data={}){const base=ccOrderDiagFieldText(data.message||'');const fields=[['code',data.code],['reason',data.reasonText!==undefined?data.reasonText:(data.closeReason!==undefined?data.closeReason:data.reason)],['wasClean',data.wasClean],['socketId',data.socketId],['backoffMs',data.backoffMs],['delayMs',data.delayMs],['nextBackoffMs',data.nextBackoffMs],['readyState',data.readyState],['state',data.state],['reconnectDueInMs',data.reconnectDueInMs],['reconnectCount',data.reconnectCount],['lastCloseCode',data.lastCloseCode],['lastCloseReason',data.lastCloseReason]].map(([k,v])=>{const text=ccOrderDiagFieldText(v);return text?k+'='+text:''}).filter(Boolean);return ccOrderDiagText([base,fields.join(' ')].filter(Boolean).join(' | '))}
function ccOrderDiagnosticPush(source,data={}){try{const ids=ccOrderDiagIds(data.order||data);const entry={ts:new Date().toISOString(),source:ccOrderDiagText(source),symbol:ccOrderDiagText(data.symbol)||ccOrderDiagSymbol(data.order||data),side:ccOrderDiagText(data.side)||ccOrderDiagSide(data.order||data),orderId:ccOrderDiagText(data.orderId)||ids.orderId,clientOrderId:ccOrderDiagText(data.clientOrderId)||ids.clientOrderId,status:ccOrderDiagText(data.status)||ccOrderDiagStatus(data.order||data),action:ccOrderDiagText(data.action||data.reason||''),message:ccOrderDiagMessage(data)};const rows=ccOrderDiagnosticRows();rows.unshift(entry);if(rows.length>CC_ORDER_DIAGNOSTIC_MAX)rows.length=CC_ORDER_DIAGNOSTIC_MAX;ccOrderDiagnosticPage=1;ccRenderOrderDiagnosticPanel()}catch(_){}}
function ccOrderDiagnosticPageCount(){return Math.max(1,Math.ceil(ccOrderDiagnosticRows().length/CC_ORDER_DIAGNOSTIC_PAGE_SIZE))}
function ccOrderDiagnosticHtml(){const rows=ccOrderDiagnosticRows(),pages=ccOrderDiagnosticPageCount();ccOrderDiagnosticPage=Math.min(Math.max(1,ccOrderDiagnosticPage),pages);const start=(ccOrderDiagnosticPage-1)*CC_ORDER_DIAGNOSTIC_PAGE_SIZE,pageRows=rows.slice(start,start+CC_ORDER_DIAGNOSTIC_PAGE_SIZE);const body=pageRows.length?pageRows.map(r=>'<tr><td>'+ccEsc(r.ts)+'</td><td>'+ccEsc(r.source)+'</td><td>'+ccEsc(r.symbol||'—')+'</td><td>'+ccEsc(r.side||'—')+'</td><td>'+ccEsc(r.orderId||r.clientOrderId||'—')+'</td><td>'+ccEsc(r.status||'—')+'</td><td>'+ccEsc(r.action||'—')+'</td><td>'+ccEsc(r.message||'')+'</td></tr>').join(''):'<tr><td class="cc-order-diagnostic-empty" colspan="8">No order diagnostics yet.</td></tr>';return '<div class="cc-order-diagnostic-head"><strong>Order updates ('+rows.length+')</strong><div class="cc-order-diagnostic-pager"><button type="button" data-cc-order-diag-page="prev"'+(ccOrderDiagnosticPage<=1?' disabled':'')+'>Prev</button><span>Page '+ccOrderDiagnosticPage+' / '+pages+'</span><button type="button" data-cc-order-diag-page="next"'+(ccOrderDiagnosticPage>=pages?' disabled':'')+'>Next</button></div></div><div class="cc-order-diagnostic-list"><table class="cc-order-diagnostic-table"><thead><tr><th style="width:154px">UTC</th><th style="width:132px">Source</th><th style="width:120px">Symbol</th><th style="width:58px">Side</th><th style="width:145px">Order</th><th style="width:92px">Status</th><th style="width:126px">Action</th><th>Message</th></tr></thead><tbody>'+body+'</tbody></table></div>'}
function ccRenderOrderDiagnosticPanel(){const el=cc('ccOrderDiagnosticPanel');if(!el)return;const html=ccOrderDiagnosticHtml();if(html!==ccOrderDiagnosticLastHtml){el.innerHTML=html;ccOrderDiagnosticLastHtml=html}}
function ccOrderDiagnosticSetPage(dir){const pages=ccOrderDiagnosticPageCount();ccOrderDiagnosticPage=Math.min(Math.max(1,ccOrderDiagnosticPage+(dir==='next'?1:-1)),pages);ccRenderOrderDiagnosticPanel()}
function ccFuturesOpenOrdersDiag(reason,prev,next,extra={}){try{const entry={ts:new Date().toISOString(),reason,prev:Array.isArray(prev)?prev.length:0,next:Array.isArray(next)?next.length:0,...extra};window.CC_FUTURES_ORDER_MUTATIONS=window.CC_FUTURES_ORDER_MUTATIONS||[];window.CC_FUTURES_ORDER_MUTATIONS.push(entry);if(window.CC_FUTURES_ORDER_MUTATIONS.length>40)window.CC_FUTURES_ORDER_MUTATIONS.shift();const rest=String(reason||'').startsWith('rest-');ccOrderDiagnosticPush(rest?'futures-rest/open-orders':'futures-ws/order',{...extra,action:reason,message:'open orders '+entry.prev+' -> '+entry.next+(extra.removed?'; removed '+extra.removed:'')});(Array.isArray(extra.removedOrders)?extra.removedOrders:[]).forEach(o=>ccOrderDiagnosticPush('reconcile',{order:o,action:'futures-rest-remove',message:'REST open orders removed local order'}));console.debug('[CoinCall futures orders]',entry)}catch(_){}}
function ccFuturesPrivateValuePresent(v){return v!==undefined&&v!==null&&String(v).trim()!==''}
function ccFuturesCompactPrivateOrder(order){const out={};for(const [k,v] of Object.entries(order||{})){if(ccFuturesPrivateValuePresent(v))out[k]=v}return out}
function ccFuturesPrivateTerminalUpdate(order){if(ccFuturesOrderExplicitTerminal(order))return true;if(ccFuturesOrderExplicitOpen(order))return false;const totalRaw=ccFirstValue(order,['qty','quantity','amount','size','orderQty','origQty','volume','q','sz']);const filledRaw=ccFirstValue(order,['fillQty','filledQty','filledQuantity','filled','dealQty','cumQty','fq']);const total=Number(totalRaw),filled=Number(filledRaw);if(ccFuturesPrivateValuePresent(totalRaw)&&ccFuturesPrivateValuePresent(filledRaw)&&Number.isFinite(total)&&total>0&&Number.isFinite(filled)&&filled>=total-Math.max(1e-12,total*1e-10))return true;const leftRaw=ccFirstValue(order,['remainQty','remainingQty','leavesQty','leftQty','unfilledQty']);const left=Number(leftRaw);return ccFuturesPrivateValuePresent(leftRaw)&&Number.isFinite(left)&&left<=0&&Number.isFinite(filled)&&filled>0}
function ccApplyFuturesPrivateOrderEvent(raw){const rows=Array.isArray(raw)?raw:[raw].filter(Boolean);let changed=false;const prev=Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders.slice():[];for(const d of rows){if(!d||typeof d!=='object')continue;const order=ccFuturesCompactPrivateOrder(ccNormalizeFuturesPrivateOrder(d));ccOrderDiagnosticPush('futures-ws/order',{order,action:'message',message:'private order update received'});const ids=ccFuturesOrderIdentity(order);if(!ids.orderId&&!ids.clientOrderId)continue;const list=Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders.slice():[];const idx=list.findIndex(o=>ccFuturesOrderSame(o,order));if(idx<0){if(!ccFuturesPrivateOrderLooksOpen(order))continue;list.unshift(order);ccLastFuturesOpenOrders=list.filter(o=>!ccFuturesPrivateOrderTerminal(o));ccFuturesOpenOrdersLoaded=true;changed=true;ccFuturesOpenOrdersDiag('private-add',prev,ccLastFuturesOpenOrders,{order,orderId:ids.orderId,clientOrderId:ids.clientOrderId,status:ccFuturesOrderRawStatusKey(order),symbol:ccFuturesOrderSymbolRaw(order),side:ccSpotOrderSideRaw(order)});continue}if(ccFuturesPrivateTerminalUpdate(order)){const before=ccOrderFilledQty(list[idx]),merged={...list[idx],...order};const notified=ccFuturesNotifyFillOnce(merged,before,'ccFuturesOrderStatus','private-terminal');list.splice(idx,1);ccLastFuturesOpenOrders=list;changed=true;ccFuturesOpenOrdersDiag('private-terminal-remove',prev,ccLastFuturesOpenOrders,{order:merged,orderId:ids.orderId,clientOrderId:ids.clientOrderId,status:ccFuturesOrderRawStatusKey(order),symbol:ccFuturesOrderSymbolRaw(merged),side:ccSpotOrderSideRaw(merged),filledQty:ccFuturesFilledQtyForNotify(merged),notify:notified});if(ccFuturesFilledQtyForNotify(merged)>before+1e-12)ccScheduleFuturesTradeHistoryRefreshBurst('private-terminal');continue}const merged=ccFuturesMergeOpenOrderRows(list[idx],order);list[idx]=merged;ccLastFuturesOpenOrders=list;ccFuturesOpenOrdersLoaded=true;changed=true;ccFuturesOpenOrdersDiag('private-merge',prev,ccLastFuturesOpenOrders,{order:merged,orderId:ids.orderId,clientOrderId:ids.clientOrderId,status:ccFuturesOrderRawStatusKey(order),symbol:ccFuturesOrderSymbolRaw(merged),side:ccSpotOrderSideRaw(merged)})}if(changed){ccMaybeNotifyOpenOrderFillDelta(prev,ccLastFuturesOpenOrders,'ccFuturesOrderStatus');ccRenderFuturesBottomPanels();drawCcChart('ccFuturesChart')}return changed}
function ccFuturesPrivateMessageHandle(msg){const dt=Number(msg&&msg.dt),d=msg&&msg.d;if(![35,36,38,46].includes(dt))return false;ccBookTouch('futures');CC_FUTURES_PRIVATE_WS.lastEventAt=Date.now();CC_FUTURES_PRIVATE_WS.lastDt=dt;CC_FUTURES_PRIVATE_WS.eventCount=(CC_FUTURES_PRIVATE_WS.eventCount||0)+1;const type=String(msg&&ccFirstValue(msg,['dataType','topic','channel','ch'])||'').toLowerCase();const isOrder=dt===35||type.includes('order'),isTrade=dt===36||type.includes('trade'),isPositionEvent=dt===46||type.includes('positionevent')||type.includes('position_event'),isPosition=(dt===38||type.includes('position'))&&!isPositionEvent;const orderChanged=isOrder&&d?ccApplyFuturesPrivateOrderEvent(d):false;const tradeChanged=isTrade&&d?ccApplyFuturesPrivateTradeEvent(d):false;const positionChanged=isPosition&&d?ccApplyFuturesPrivatePositionEvent(d):false;if(isPositionEvent&&d)ccScheduleFuturesPositionEventReconcile(900);ccScheduleFuturesPrivateRefresh();if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh((tradeChanged||orderChanged||positionChanged||isPositionEvent)?120:500);ccSpreadsScheduleLatencyPanelRender(100);return true}
const CC_FUTURES_PRIVATE_WS={socket:null,url:'',account:'',heartbeat:null,retry:null,lastOpenAt:0,lastCloseAt:0,lastEventAt:0,lastHeartbeatAt:0,lastReconnectAt:0,lastErrorAt:0,lastDt:null,eventCount:0,reconnectCount:0};
function ccFuturesPrivateSocketOpen(){const ws=CC_FUTURES_PRIVATE_WS.socket;return !!(ws&&ws.readyState===WebSocket.OPEN)}
function ccFuturesPrivateClearHeartbeat(){if(CC_FUTURES_PRIVATE_WS.heartbeat){clearInterval(CC_FUTURES_PRIVATE_WS.heartbeat);CC_FUTURES_PRIVATE_WS.heartbeat=null}}
function ccFuturesPrivateScheduleReconnect(){clearTimeout(CC_FUTURES_PRIVATE_WS.retry);if(document.hidden)return;CC_FUTURES_PRIVATE_WS.lastReconnectAt=Date.now();CC_FUTURES_PRIVATE_WS.reconnectCount=(CC_FUTURES_PRIVATE_WS.reconnectCount||0)+1;ccSpreadsScheduleLatencyPanelRender(100);CC_FUTURES_PRIVATE_WS.retry=setTimeout(()=>ccEnsureFuturesPrivateSocket().catch(()=>{}),2000)}
async function ccEnsureFuturesPrivateSocket(){const sel=cc('ccAccountSelect'),account=String(sel&&sel.value||'');const current=CC_FUTURES_PRIVATE_WS.socket;if(current&&CC_FUTURES_PRIVATE_WS.account===account&&[WebSocket.OPEN,WebSocket.CONNECTING].includes(current.readyState))return;if(current&&CC_FUTURES_PRIVATE_WS.account!==account&&[WebSocket.OPEN,WebSocket.CONNECTING].includes(current.readyState)){try{current.close()}catch{}return}if(current&&[WebSocket.OPEN,WebSocket.CONNECTING].includes(current.readyState))return;ccFuturesPrivateClearHeartbeat();const qs=account?'?accountId='+encodeURIComponent(account):'';const auth=await ccApi('/api/admin/coincall/futures/private-ws-auth'+qs),url=String(auth&&auth.url||'');if(!url)return;const ws=new WebSocket(url);CC_FUTURES_PRIVATE_WS.socket=ws;CC_FUTURES_PRIVATE_WS.url=url;CC_FUTURES_PRIVATE_WS.account=account;ws.onopen=()=>{CC_FUTURES_PRIVATE_WS.lastOpenAt=Date.now();clearTimeout(CC_FUTURES_PRIVATE_WS.retry);ccBookSendJson(ws,{action:'subscribe',dataType:'order'});ccBookSendJson(ws,{action:'subscribe',dataType:'trade'});ccBookSendJson(ws,{action:'subscribe',dataType:'position'});ccBookSendJson(ws,{action:'subscribe',dataType:'positionEvent'});ccFuturesPrivateClearHeartbeat();CC_FUTURES_PRIVATE_WS.heartbeat=setInterval(()=>{CC_FUTURES_PRIVATE_WS.lastHeartbeatAt=Date.now();ccBookSendJson(ws,{c:11});ccBookSendJson(ws,{action:'heartbeat'});ccSpreadsScheduleLatencyPanelRender(1000)},12000);ccSpreadsScheduleLatencyPanelRender(100)};ws.onmessage=ev=>{let msg;try{msg=JSON.parse(ev.data)}catch{return}if(msg&&msg.ping!==undefined){ccBookSendJson(ws,{pong:msg.ping});return}if(msg&&msg.rc===1){CC_FUTURES_PRIVATE_WS.lastEventAt=Date.now();ccSpreadsScheduleLatencyPanelRender(1000);return}ccFuturesPrivateMessageHandle(msg)};ws.onclose=()=>{CC_FUTURES_PRIVATE_WS.lastCloseAt=Date.now();if(CC_FUTURES_PRIVATE_WS.socket===ws)CC_FUTURES_PRIVATE_WS.socket=null;ccFuturesPrivateClearHeartbeat();ccSpreadsScheduleLatencyPanelRender(100);ccFuturesPrivateScheduleReconnect()};ws.onerror=()=>{CC_FUTURES_PRIVATE_WS.lastErrorAt=Date.now();ccSpreadsScheduleLatencyPanelRender(100)}}
const CC_FUTURES_ORDER_EVENTS={source:null,url:'',retry:null};
function ccEnsureFuturesOrderEventStream(){if(!window.EventSource)return;const token=ccToken();if(!token)return;const url='/api/admin/coincall/futures/order-events/stream?access_token='+encodeURIComponent(token);if(CC_FUTURES_ORDER_EVENTS.source&&CC_FUTURES_ORDER_EVENTS.url===url&&CC_FUTURES_ORDER_EVENTS.source.readyState!==EventSource.CLOSED)return;try{if(CC_FUTURES_ORDER_EVENTS.source)CC_FUTURES_ORDER_EVENTS.source.close()}catch{}clearTimeout(CC_FUTURES_ORDER_EVENTS.retry);const es=new EventSource(url);CC_FUTURES_ORDER_EVENTS.source=es;CC_FUTURES_ORDER_EVENTS.url=url;const handle=ev=>{let msg;try{msg=JSON.parse(ev.data)}catch{return}ccFuturesPrivateMessageHandle(msg)};es.onmessage=handle;es.addEventListener('order',handle);es.onerror=()=>{try{es.close()}catch{}if(CC_FUTURES_ORDER_EVENTS.source===es)CC_FUTURES_ORDER_EVENTS.source=null;clearTimeout(CC_FUTURES_ORDER_EVENTS.retry);CC_FUTURES_ORDER_EVENTS.retry=setTimeout(ccEnsureFuturesOrderEventStream,3000)}}
function ccSpotBookHandleMessage(msg){const channel=String(msg&&msg.ch||'');const tick=msg&&msg.tick;if(!channel||!tick||!channel.startsWith('market.')||!channel.includes('.mbp.'))return false;const symbol=String(tick.s||'').trim();if(symbol!==CC_BOOK_STATE.transport.spot)return true;const isSnapshot=String(tick.type||'').toLowerCase()==='snapshot';if(isSnapshot||CC_BOOK_STATE.expectSnapshot.spot||CC_BOOK_STATE.spotCache.channel!==channel){ccSpotBookResetCache(channel);CC_BOOK_STATE.expectSnapshot.spot=false}ccSpotBookApplySide('asks',tick.a);ccSpotBookApplySide('bids',tick.b);ccBookTouch('spot');ccRenderBook('spot',ccSpotBookRaw());return true}
function ccFuturesBookHandleMessage(msg){const dt=Number(msg&&msg.dt);if(!msg||typeof msg!=='object'||!msg.d||![32,37,58].includes(dt))return false;const data=msg.d;const symbol=String(data.s||'').trim();if(symbol!==CC_BOOK_STATE.transport.futures)return true;CC_BOOK_STATE.expectSnapshot.futures=false;ccBookTouch('futures');const asks=Array.isArray(data.asks)?data.asks:[],bids=Array.isArray(data.bids)?data.bids:[];ccRenderBook('futures',{asks,bids,ts:data.ts,symbol:symbol});return true}
function ccBookScheduleReconnect(market,reason=''){if(!CC_BOOK_STATE.wantReconnect[market])return;ccBookClearReconnect(market);if(reason)ccBookSetMidMessage(market,reason);CC_BOOK_STATE.reconnect[market]=setTimeout(()=>{CC_BOOK_STATE.reconnect[market]=null;ccEnsureBookSocket(market,true)},CC_BOOK_RECONNECT_MS)}
function ccBookCloseSocket(market,keepReconnect=false){const ws=CC_BOOK_STATE.sockets[market];const socketSymbol=CC_BOOK_STATE.socketSymbol[market];CC_BOOK_STATE.sockets[market]=null;CC_BOOK_STATE.socketSymbol[market]='';ccBookClearHeartbeat(market);ccBookClearStale(market);if(ws){try{if(ws.readyState===WebSocket.OPEN&&market==='spot'&&socketSymbol){ccBookSendJson(ws,{unsub:ccSpotBookChannel(socketSymbol),id:Date.now()});ccBookSendJson(ws,{unsub:ccSpotOverviewChannel(socketSymbol),id:Date.now()+1});ccBookSendJson(ws,{unsub:ccSpotChartKlineChannel(socketSymbol),id:Date.now()+2});ccBookSendJson(ws,{unsub:ccSpotTradeDetailChannel(socketSymbol),id:Date.now()+3})}if(ws.readyState===WebSocket.OPEN&&market==='futures'&&socketSymbol){ccBookSendJson(ws,{action:'unSubscribe',dataType:'lastTrade',payload:{symbol:socketSymbol}});ccBookSendJson(ws,{action:'unSubscribe',dataType:'lasttradeV2',payload:{symbol:socketSymbol}})}}catch{}try{ws.onopen=ws.onmessage=ws.onerror=ws.onclose=null}catch{}try{ws.close()}catch{}}if(keepReconnect)ccBookScheduleReconnect(market,'Reconnecting order book…');else ccBookClearReconnect(market)}
async function ccEnsureBookSocket(market,force=false){ccBookRememberSelection(market);const symbol=CC_BOOK_STATE.transport[market];if(!symbol){ccBookSetMidMessage(market,'Select an instrument');return}if(force)ccBookCloseSocket(market,false);else if(ccBookSocketBusy(market)){if(CC_BOOK_STATE.socketSymbol[market]===symbol){if(CC_BOOK_STATE.raw[market])ccRenderBook(market,CC_BOOK_STATE.raw[market]);return}ccBookCloseSocket(market,false)}const connectSeq=(CC_BOOK_STATE.connectSeq[market]||0)+1;CC_BOOK_STATE.connectSeq[market]=connectSeq;CC_BOOK_STATE.expectSnapshot[market]=true;if(market==='spot')CC_BOOK_STATE.spotLastTrade={symbol,price:NaN,side:'',ts:0};if(market==='futures'){CC_BOOK_STATE.futuresLastTrade={symbol,price:NaN,side:'',ts:0};CC_BOOK_STATE.futuresMidTrack={symbol,price:NaN,side:''}}let url='';if(market==='futures'){ccBookSetMidMessage(market,'Authorizing order book…');let auth;try{auth=await ccFuturesBookAuth(force)}catch(e){if(CC_BOOK_STATE.connectSeq[market]!==connectSeq)return;ccBookSetMidMessage(market,'Futures WS auth failed: '+e.message);ccBookScheduleReconnect(market,'Futures WS auth failed. Reconnecting…');return}if(CC_BOOK_STATE.connectSeq[market]!==connectSeq||CC_BOOK_STATE.transport[market]!==symbol)return;url=ccFuturesBookSocketUrl(auth)}else url='wss://ws.coincall.com/spot/ws';const ws=new WebSocket(url);CC_BOOK_STATE.sockets[market]=ws;CC_BOOK_STATE.socketSymbol[market]=symbol;ccBookSetMidMessage(market,'Connecting order book…');ws.onopen=()=>{if(CC_BOOK_STATE.sockets[market]!==ws||CC_BOOK_STATE.connectSeq[market]!==connectSeq)return;ccBookTouch(market);if(market==='spot'){ccSpotBookResetCache(ccSpotBookChannel(symbol));ccBookSendJson(ws,{sub:ccSpotBookChannel(symbol),id:Date.now()});ccBookSendJson(ws,{sub:ccSpotOverviewChannel(symbol),id:Date.now()+1});ccBookSendJson(ws,{sub:ccSpotChartKlineChannel(symbol),id:Date.now()+2});ccBookSendJson(ws,{sub:ccSpotTradeDetailChannel(symbol),id:Date.now()+3})}else{ccBookSendJson(ws,{c:20,dt:58,d:{s:symbol,step:CC_FUTURES_BOOK_STEP}});ccBookSendJson(ws,{c:20,dt:33,d:{s:symbol}});ccBookSendJson(ws,{c:20,dt:43,d:{s:symbol}});ccBookSendJson(ws,{action:'subscribe',dataType:'lastTrade',payload:{symbol}});ccBookSendJson(ws,{action:'subscribe',dataType:'lasttradeV2',payload:{symbol}})}ccBookClearHeartbeat(market);CC_BOOK_STATE.heartbeat[market]=setInterval(()=>ccBookSendJson(ws,{c:11}),15000)};ws.onmessage=ev=>{if(CC_BOOK_STATE.sockets[market]!==ws||CC_BOOK_STATE.connectSeq[market]!==connectSeq)return;let msg;try{msg=JSON.parse(ev.data)}catch{return}if(msg&&msg.ping!==undefined){ccBookSendJson(ws,{pong:msg.ping});ccBookTouch(market);return}if(msg&&msg.rc===1){ccBookTouch(market);return}if(market==='spot'){if(ccSpotKlineHandleMessage(msg))return;if(ccSpotTradeDetailHandleMessage(msg))return;if(ccSpotOverviewHandleMessage(msg))return;ccSpotBookHandleMessage(msg);return}if(ccFuturesTradeMessageHandle(msg))return;ccFuturesBookHandleMessage(msg)};ws.onerror=()=>{if(CC_BOOK_STATE.sockets[market]!==ws)return;ccBookSetMidMessage(market,'Order book socket error. Reconnecting…')};ws.onclose=()=>{if(CC_BOOK_STATE.sockets[market]===ws)CC_BOOK_STATE.sockets[market]=null;if(CC_BOOK_STATE.socketSymbol[market]===symbol)CC_BOOK_STATE.socketSymbol[market]='';ccBookClearHeartbeat(market);ccBookClearStale(market);if(CC_BOOK_STATE.wantReconnect[market])ccBookScheduleReconnect(market,'Order book disconnected. Reconnecting…')}}
async function loadCcBook(market){ccBookRememberSelection(market);if(CC_BOOK_STATE.raw[market]&&CC_BOOK_STATE.transport[market]===ccBookTransportSymbol(market))ccRenderBook(market,CC_BOOK_STATE.raw[market]);ccEnsureBookSocket(market,false)}
function loadCcMarket(market){loadCcChart(market);loadCcBook(market)}
async function loadCcCharts(){await loadCcChart('spot');await loadCcChart('futures')}
function ccStopBookRefresh(market){CC_BOOK_STATE.wantReconnect[market]=false;ccBookCloseSocket(market,false);if(CC_BOOK_STATE.timers[market]){clearInterval(CC_BOOK_STATE.timers[market]);CC_BOOK_STATE.timers[market]=null}}
function ccStartBookRefresh(market){const other=market==='futures'?'spot':'futures';CC_BOOK_STATE.active=market;ccStopBookRefresh(other);CC_BOOK_STATE.wantReconnect[market]=true;loadCcBook(market);if(market==='spot'){ccEnsureSpotPrivateSocket().catch(()=>{});ccMaybeLoadSpotOpenOrders(true,'initial-spot-book')}if(market==='futures'){ccEnsureFuturesPrivateSocket().catch(()=>{});loadCcFuturesPositions();loadCcFuturesInstruments();ccMaybeRefreshFuturesAccountSummaryLive()}if(!CC_BOOK_STATE.timers[market])CC_BOOK_STATE.timers[market]=setInterval(()=>{if(CC_BOOK_STATE.active===market&&!document.hidden){if(market==='spot'){ccEnsureSpotPrivateSocket().catch(()=>{});ccMaybeLoadSpotOpenOrders(false,'spot-book-refresh')}if(market==='futures'){loadCcBook('futures');ccEnsureFuturesPrivateSocket().catch(()=>{});ccMaybeRefreshFuturesAccountSummaryLive()}}},CC_BOOK_REFRESH_MS)}
function ccActiveMarket(){return cc('ccFuturesShell')&&!cc('ccFuturesShell').hidden?'futures':'spot'}
const CC_MARKET_DATA_SPOT_SYMBOLS=[{value:'BTCUSDT',label:'BTC/USDT'},{value:'ETHUSDT',label:'ETH/USDT'}];
function ccMarketDataActiveTab(){return document.querySelector('[data-cc-md-tab].active')?.dataset?.ccMdTab||'spot'}
function ccMarketDataMode(){return document.querySelector('[data-cc-trade-chart-mode].active')?.dataset?.ccTradeChartMode||document.querySelector('[data-cc-md-mode].active')?.dataset?.ccMdMode||'candles'}
function ccMarketDataSourceSelect(market,kind){return cc(market==='futures'?(kind==='symbol'?'ccFuturesInst':'ccFuturesBar'):(kind==='symbol'?'ccSpotInst':'ccSpotBar'))}
function ccMarketDataStateKeys(market=ccMarketDataActiveTab()){return market==='futures'?{symbol:'dataFuturesSymbol',timeframe:'dataFuturesTimeframe'}:{symbol:'dataSpotSymbol',timeframe:'dataSpotTimeframe'}}
function ccMarketDataSavedValue(market,kind){const key=ccMarketDataStateKeys(market)[kind==='timeframe'?'timeframe':'symbol'];return String(ccUiStateLoad()[key]||'').trim()}
function ccSaveMarketDataSelection(market=ccMarketDataActiveTab()){const keys=ccMarketDataStateKeys(market),symbol=String(cc('ccMarketDataSymbol')?.value||'').trim(),timeframe=String(cc('ccMarketDataBar')?.value||'').trim();return ccUiStateSave({dataMarketTab:market,[keys.symbol]:symbol,[keys.timeframe]:timeframe})}
function ccMarketDataSourceOptions(market,kind){const source=ccMarketDataSourceSelect(market,kind);const options=Array.from(source?.options||[]).map(o=>({value:String(o.value||'').trim(),label:String(o.textContent||o.value||'').trim()})).filter(o=>o.value);if(options.length)return options;if(kind==='symbol'&&market==='futures'){const rows=ccFuturesQuoteSymbolRows(ccLastFuturesInstruments);if(rows.length)return rows.map(row=>({value:String(row.symbol||'').trim(),label:String(row.displayName||row.symbolName||row.symbol||'').trim()}))}if(kind==='symbol')return CC_MARKET_DATA_SPOT_SYMBOLS;return ['1m','5m','15m','1H','4H','1D'].map(value=>({value,label:value}))}
function ccSyncMarketDataSymbolOptions(market=ccMarketDataActiveTab(),preferredValue){const sourceValue=String(ccMarketDataSourceSelect(market,'symbol')?.value||'').trim();const symbols=ccMarketDataSourceOptions(market,'symbol');const sync=(id)=>{const sel=cc(id);if(!sel)return;const prev=String(sel.value||'').trim();sel.innerHTML=symbols.map(s=>'<option value="'+ccEsc(s.value)+'">'+ccEsc(s.label)+'</option>').join('');const picks=preferredValue!==undefined?[String(preferredValue||'').trim(),sourceValue,prev]:[sourceValue,prev];const target=picks.find(value=>value&&symbols.some(s=>s.value===value))||symbols[0]?.value||'';sel.value=target};sync('ccMarketDataSymbol');sync('ccTradeMarketInst')}
function ccSyncMarketDataTimeframeOptions(market=ccMarketDataActiveTab(),preferredValue){const sourceValue=String(ccMarketDataSourceSelect(market,'timeframe')?.value||'').trim();const options=ccMarketDataSourceOptions(market,'timeframe');const sync=(id)=>{const sel=cc(id);if(!sel)return;const prev=String(sel.value||'').trim();sel.innerHTML=options.map(s=>'<option value="'+ccEsc(s.value)+'">'+ccEsc(s.label)+'</option>').join('');const picks=preferredValue!==undefined?[String(preferredValue||'').trim(),sourceValue,prev]:[sourceValue,prev];const target=picks.find(value=>value&&options.some(s=>s.value===value))||options[0]?.value||'1m';sel.value=target};sync('ccMarketDataBar');sync('ccTradeMarketBar')}
function ccMarketDataSyncControls(market=ccMarketDataActiveTab()){ccSyncMarketDataSymbolOptions(market);ccSyncMarketDataTimeframeOptions(market)}
function ccDbBackedFuturesSymbolRows(payload){const items=Array.isArray(payload?.items)?payload.items:[];return items.map(row=>({symbol:String(row?.symbol||'').trim(),displayName:String(row?.displayName||row?.symbol||'').trim()})).filter(row=>row.symbol)}
function ccRenderMarketDataLegend(){}
function ccSetMarketDataMode(mode,persist=true){const target=mode==='linear'?'linear':'candles';document.querySelectorAll('[data-cc-md-mode]').forEach(btn=>{const active=btn.dataset.ccMdMode===target;btn.classList.toggle('active',active);btn.setAttribute('aria-pressed',active?'true':'false')});document.querySelectorAll('[data-cc-trade-chart-mode]').forEach(btn=>{const active=btn.dataset.ccTradeChartMode===target;btn.classList.toggle('active',active);btn.setAttribute('aria-checked',active?'true':'false')});if(persist)ccUiStateSave({dataChartMode:target});ccRenderMarketDataLegend();ccMarketDataDraw()}
const CC_MARKET_DATA_PAGE_LIMIT=5000;
const CC_MARKET_DATA_HISTORY_STATE={market:'',symbol:'',minuteRows:[],loading:false,hasMore:true,requestSeq:0};
let ccLastMarketDataRows=[];let ccMarketDataInfoText='';
function ccMarketDataSeriesValue(row,prefix,suffix){const exact=ccMarketDataNumberOrNaN(row&&row[prefix+suffix]);if(Number.isFinite(exact))return exact;const fallback=ccMarketDataNumberOrNaN(row&&row[prefix]);return Number.isFinite(fallback)?fallback:NaN}
function ccMarketDataSeriesAssign(target,prefix,value){if(!target||!prefix||!Number.isFinite(value))return;const oKey=prefix+'O',hKey=prefix+'H',lKey=prefix+'L',cKey=prefix+'C';if(!Number.isFinite(target[oKey]))target[oKey]=value;target[cKey]=value;target[hKey]=Number.isFinite(target[hKey])?Math.max(target[hKey],value):value;target[lKey]=Number.isFinite(target[lKey])?Math.min(target[lKey],value):value;target[prefix]=value}
function ccMarketDataLegendValue(label,value,color){const n=Number(value),ok=Number.isFinite(n);return label+'<span style="color:'+(ok?color:'#94A3B8')+';margin-left:3px">'+(ok?ccChartFmt(n):'—')+'</span>'}
function ccMarketDataOhlcLegendHtml(values,color){return [['O',values&&values.o],['H',values&&(values.h??values.hi)],['L',values&&(values.l??values.lo)],['C',values&&values.c]].map(([label,value])=>ccMarketDataLegendValue(label,value,color)).join(' ')}
function ccMarketDataSeriesLegendHtml(label,row,prefix,color){return '<div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap"><span style="color:'+color+';font-weight:700;min-width:34px">'+label+'</span><span>'+ccMarketDataOhlcLegendHtml({o:ccMarketDataSeriesValue(row,prefix,'O'),h:ccMarketDataSeriesValue(row,prefix,'H'),l:ccMarketDataSeriesValue(row,prefix,'L'),c:ccMarketDataSeriesValue(row,prefix,'C')},color)+'</span></div>'}
function ccMarketDataSetOhlc(row){const meta=cc('ccMarketDataMeta');if(!meta)return;const linear=ccMarketDataMode()==='linear';const market=ccMarketDataActiveTab();const closeHtml=ccMarketDataOhlcLegendHtml(row||{},CC_LINEAR_CLOSE_COLOR);meta.classList.toggle('cc-market-data-meta-linear',linear);if(linear){const parts=[ccMarketDataSeriesLegendHtml('Ask',row,'ask',CC_LINEAR_ASK_COLOR),'<div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap"><span style="color:'+CC_LINEAR_CLOSE_COLOR+';font-weight:700;min-width:34px">Close</span><span>'+closeHtml+'</span></div>'];if(market==='futures')parts.push(ccMarketDataSeriesLegendHtml('Mark',row,'mark',CC_LINEAR_MARK_COLOR));parts.push(ccMarketDataSeriesLegendHtml('Bid',row,'bid',CC_LINEAR_BID_COLOR));meta.innerHTML=parts.join('')}else if(row)meta.innerHTML=ccChartLegendHtml(row);else meta.textContent='No stored chart data';if(ccMarketDataInfoText)meta.title=ccMarketDataInfoText}
function ccMarketDataViewportStateLoad(chartKey){const root=ccUiStateLoad().dataChartViewport;return root&&typeof root==='object'&&root[chartKey]&&typeof root[chartKey]==='object'?root[chartKey]:null}
function ccMarketDataViewportStateSave(chartKey,patch){if(!chartKey)return null;const state=ccUiStateLoad();const root=state.dataChartViewport&&typeof state.dataChartViewport==='object'?state.dataChartViewport:{};root[chartKey]={...(root[chartKey]&&typeof root[chartKey]==='object'?root[chartKey]:{}),...(patch&&typeof patch==='object'?patch:{})};return ccUiStateSave({dataChartViewport:root})}
function ccFuturesSymbolStateLoad(symbol){const key=String(symbol||'').trim();const root=ccUiStateLoad().futuresChartState;return key&&root&&typeof root==='object'&&root[key]&&typeof root[key]==='object'?root[key]:null}
function ccFuturesSymbolStateSave(symbol,patch){const key=String(symbol||'').trim();if(!key)return null;const state=ccUiStateLoad();const root=state.futuresChartState&&typeof state.futuresChartState==='object'?state.futuresChartState:{};root[key]={...(root[key]&&typeof root[key]==='object'?root[key]:{}),...(patch&&typeof patch==='object'?patch:{})};return ccUiStateSave({futuresChartState:root})}
function ccFuturesViewportStateLoad(chartKey){const root=ccUiStateLoad().futuresChartViewport;return root&&typeof root==='object'&&root[chartKey]&&typeof root[chartKey]==='object'?root[chartKey]:null}
function ccFuturesViewportStateSave(chartKey,patch){if(!chartKey)return null;const state=ccUiStateLoad();const root=state.futuresChartViewport&&typeof state.futuresChartViewport==='object'?state.futuresChartViewport:{};root[chartKey]={...(root[chartKey]&&typeof root[chartKey]==='object'?root[chartKey]:{}),...(patch&&typeof patch==='object'?patch:{})};return ccUiStateSave({futuresChartViewport:root})}
function ccFuturesViewportRestore(chartKey){const st=CC_CHART_STATE.ccFuturesChart,saved=ccFuturesViewportStateLoad(chartKey);if(!st||!saved)return false;const visible=Math.round(Number(saved.visibleCandles));if(Number.isFinite(visible)&&visible>0)st.visibleCandles=visible;const panOffset=Math.round(Number(saved.panOffset));if(Number.isFinite(panOffset))st.panOffset=panOffset;const pr=saved.priceRange;if(pr&&Number.isFinite(Number(pr.min))&&Number.isFinite(Number(pr.max))&&Number(pr.max)>Number(pr.min))st.priceRange={min:Number(pr.min),max:Number(pr.max)};else st.priceRange=null;return true}
function ccFuturesViewportSnapshot(){const st=CC_CHART_STATE.ccFuturesChart;if(!st||!st.chartKey)return null;return {visibleCandles:Math.max(1,Math.round(Number(st.visibleCandles)||0)),panOffset:Math.round(Number(st.panOffset)||0),priceRange:st.priceRange&&Number.isFinite(Number(st.priceRange.min))&&Number.isFinite(Number(st.priceRange.max))&&Number(st.priceRange.max)>Number(st.priceRange.min)?{min:Number(st.priceRange.min),max:Number(st.priceRange.max)}:null}}
function ccFuturesViewportPersistActive(){const st=CC_CHART_STATE.ccFuturesChart,snapshot=ccFuturesViewportSnapshot();if(!st||!snapshot||!st.chartKey||!st.chartKey.startsWith('futures:'))return null;clearTimeout(st.persistTimer||0);st.lastPersistSig=JSON.stringify([st.chartKey,snapshot.visibleCandles,snapshot.panOffset,snapshot.priceRange&&snapshot.priceRange.min,snapshot.priceRange&&snapshot.priceRange.max]);return ccFuturesViewportStateSave(st.chartKey,snapshot)}
function ccFuturesViewportPersistSoon(){const st=CC_CHART_STATE.ccFuturesChart,snapshot=ccFuturesViewportSnapshot();if(!st||!snapshot||!st.chartKey||!st.chartKey.startsWith('futures:'))return;const sig=JSON.stringify([st.chartKey,snapshot.visibleCandles,snapshot.panOffset,snapshot.priceRange&&snapshot.priceRange.min,snapshot.priceRange&&snapshot.priceRange.max]);if(sig===st.lastPersistSig)return;clearTimeout(st.persistTimer||0);st.persistTimer=setTimeout(()=>{ccFuturesViewportStateSave(st.chartKey,snapshot);st.lastPersistSig=sig},120)}
function ccFuturesApplySavedSymbolState(symbol){const key=String(symbol||'').trim();if(!key)return false;const saved=ccFuturesSymbolStateLoad(key);const timeframe=String(saved&&saved.timeframe||'').trim();if(timeframe&&ccUiStateOptionExists('ccFuturesBar',timeframe))cc('ccFuturesBar').value=timeframe;const currentTf=String(cc('ccFuturesBar')?.value||'').trim();ccUiStateSave({futuresSymbol:key,futuresTimeframe:currentTf});ccFuturesSymbolStateSave(key,{timeframe:currentTf});return true}
function ccMarketDataViewportRestore(chartKey){const st=CC_MARKET_DATA_CHART_STATE,saved=ccMarketDataViewportStateLoad(chartKey);if(!st||!saved)return false;const visible=Math.round(Number(saved.visibleCandles));if(Number.isFinite(visible)&&visible>0)st.visibleCandles=visible;const panOffset=Math.round(Number(saved.panOffset));if(Number.isFinite(panOffset))st.panOffset=panOffset;const pr=saved.priceRange;if(pr&&Number.isFinite(Number(pr.min))&&Number.isFinite(Number(pr.max))&&Number(pr.max)>Number(pr.min))st.priceRange={min:Number(pr.min),max:Number(pr.max)};else st.priceRange=null;return true}
function ccMarketDataViewportSnapshot(){const st=CC_MARKET_DATA_CHART_STATE;if(!st||!st.chartKey)return null;return {visibleCandles:Math.max(1,Math.round(Number(st.visibleCandles)||0)),panOffset:Math.round(Number(st.panOffset)||0),priceRange:st.priceRange&&Number.isFinite(Number(st.priceRange.min))&&Number.isFinite(Number(st.priceRange.max))&&Number(st.priceRange.max)>Number(st.priceRange.min)?{min:Number(st.priceRange.min),max:Number(st.priceRange.max)}:null}}
function ccMarketDataViewportPersistActive(){const st=CC_MARKET_DATA_CHART_STATE,snapshot=ccMarketDataViewportSnapshot();if(!st||!snapshot||!st.chartKey)return null;clearTimeout(st.persistTimer||0);st.lastPersistSig=JSON.stringify([st.chartKey,snapshot.visibleCandles,snapshot.panOffset,snapshot.priceRange&&snapshot.priceRange.min,snapshot.priceRange&&snapshot.priceRange.max]);return ccMarketDataViewportStateSave(st.chartKey,snapshot)}
function ccMarketDataViewportPersistSoon(){const st=CC_MARKET_DATA_CHART_STATE,snapshot=ccMarketDataViewportSnapshot();if(!st||!snapshot)return;const sig=JSON.stringify([st.chartKey,snapshot.visibleCandles,snapshot.panOffset,snapshot.priceRange&&snapshot.priceRange.min,snapshot.priceRange&&snapshot.priceRange.max]);if(sig===st.lastPersistSig)return;clearTimeout(st.persistTimer||0);st.persistTimer=setTimeout(()=>{ccMarketDataViewportStateSave(st.chartKey,snapshot);st.lastPersistSig=sig},120)}
function ccMarketDataHistoryKey(market,symbol){return String(market||'')+'|'+String(symbol||'')}
function ccMarketDataResetHistory(market,symbol){CC_MARKET_DATA_HISTORY_STATE.market=market;CC_MARKET_DATA_HISTORY_STATE.symbol=symbol;CC_MARKET_DATA_HISTORY_STATE.minuteRows=[];CC_MARKET_DATA_HISTORY_STATE.loading=false;CC_MARKET_DATA_HISTORY_STATE.hasMore=true}
function ccMarketDataHistoryMatches(market,symbol){return ccMarketDataHistoryKey(CC_MARKET_DATA_HISTORY_STATE.market,CC_MARKET_DATA_HISTORY_STATE.symbol)===ccMarketDataHistoryKey(market,symbol)}
function ccMarketDataMergeMinuteRows(existing,incoming){const byTs=new Map();for(const row of Array.isArray(existing)?existing:[]){const ts=ccChartMinuteBucket(row&&row.t);if(Number.isFinite(ts))byTs.set(ts,{...row,t:ts})}for(const row of Array.isArray(incoming)?incoming:[]){const ts=ccChartMinuteBucket(row&&row.t);if(!Number.isFinite(ts))continue;byTs.set(ts,{...row,t:ts})}return Array.from(byTs.values()).sort((a,b)=>a.t-b.t)}
function ccMarketDataRowsFromHistory(tf){return ccMarketDataRowsForTimeframe(CC_MARKET_DATA_HISTORY_STATE.minuteRows,tf,true)}
function ccIsMarketDataPanelActive(){return !!cc('ccMarketDataPanel')?.classList.contains('active')}
function ccRefreshMarketDataLiveTail(force=false){if(!ccIsMarketDataPanelActive())return;const market=ccMarketDataActiveTab(),symbol=String(cc('ccMarketDataSymbol')?.value||'').trim();if(force&&ccMarketDataHistoryMatches(market,symbol)&&Array.isArray(CC_MARKET_DATA_HISTORY_STATE.minuteRows)&&CC_MARKET_DATA_HISTORY_STATE.minuteRows.length){if(CC_MARKET_DATA_HISTORY_STATE.loading)return;ccMarketDataFetchHistoryPage(market,symbol,{reset:false}).catch(()=>{});return}loadCcMarketDataHistory(force).catch(()=>{})}
let ccMarketDataAutoRefreshTimer=0;function ccStartMarketDataAutoRefresh(){clearTimeout(ccMarketDataAutoRefreshTimer||0);const schedule=()=>{const delay=Math.max(1000,60000-(Date.now()%60000)+1500);ccMarketDataAutoRefreshTimer=setTimeout(()=>{ccRefreshMarketDataLiveTail(true);schedule()},delay)};schedule()}
function ccMarketDataUpdateInfoText(market,symbol,tf,rows,loading=false,error=''){if(error){ccMarketDataInfoText=error;return}const ordered=Array.isArray(rows)?rows:[];const bidAskRows=ordered.filter(r=>Number.isFinite(r.bid)||Number.isFinite(r.ask));const first=ordered[0]?.t?new Date(ordered[0].t).toISOString().replace('T',' ').slice(0,16):'none';const last=ordered[ordered.length-1]?.t?new Date(ordered[ordered.length-1].t).toISOString().replace('T',' ').slice(0,16):'none';const baFirst=bidAskRows[0]?.t?new Date(bidAskRows[0].t).toISOString().replace('T',' ').slice(0,16):'none';const baLast=bidAskRows[bidAskRows.length-1]?.t?new Date(bidAskRows[bidAskRows.length-1].t).toISOString().replace('T',' ').slice(0,16):'none';ccMarketDataInfoText=market.toUpperCase()+' · '+symbol+' · '+tf+' · '+ordered.length+' bars · range '+first+' UTC - '+last+' UTC · bid/ask rows '+bidAskRows.length+' · bid/ask range '+baFirst+' UTC - '+baLast+' UTC'+(loading?' · loading earlier candles…':(CC_MARKET_DATA_HISTORY_STATE.hasMore?'':' · reached DB start'))}
async function ccMarketDataFetchHistoryPage(market,symbol,opts={}){const tf=String(cc('ccMarketDataBar')?.value||'1m');const reset=!!opts.reset;const preserveView=!!opts.preserveView;const beforeTs=Number(opts.beforeTs);const requestSeq=++CC_MARKET_DATA_HISTORY_STATE.requestSeq;if(reset||!ccMarketDataHistoryMatches(market,symbol))ccMarketDataResetHistory(market,symbol);CC_MARKET_DATA_HISTORY_STATE.loading=true;const prevRows=ccMarketDataRowsFromHistory(tf);const prevLen=prevRows.length;ccMarketDataUpdateInfoText(market,symbol,tf,prevRows,true);ccMarketDataDraw(prevRows);try{let url='/api/admin/coincall/'+market+'/candles/minute?symbol='+encodeURIComponent(symbol)+'&limit='+CC_MARKET_DATA_PAGE_LIMIT;if(Number.isFinite(beforeTs)&&beforeTs>0)url+='&before='+Math.floor(beforeTs);const res=await ccApi(url);if(requestSeq!==CC_MARKET_DATA_HISTORY_STATE.requestSeq||!ccMarketDataHistoryMatches(market,symbol))return false;const incoming=ccMarketDataNormalizeRows(res&&res.rows);CC_MARKET_DATA_HISTORY_STATE.minuteRows=reset?incoming:ccMarketDataMergeMinuteRows(incoming,CC_MARKET_DATA_HISTORY_STATE.minuteRows);CC_MARKET_DATA_HISTORY_STATE.loading=false;CC_MARKET_DATA_HISTORY_STATE.hasMore=incoming.length>=CC_MARKET_DATA_PAGE_LIMIT;const rows=ccMarketDataRowsFromHistory(tf);const added=Math.max(0,rows.length-prevLen);if(!reset&&preserveView&&added>0){const st=CC_MARKET_DATA_CHART_STATE;st.panOffset=Math.round(Number(st.panOffset)||0)+added;if(st.drag){st.drag.panOffset=Math.round(Number(st.drag.panOffset)||0)+added;st.drag.total=Math.max(0,Math.round(Number(st.drag.total)||0)+added)}}ccMarketDataUpdateInfoText(market,symbol,tf,rows,false);ccMarketDataDraw(rows);return incoming.length>0}catch(e){if(requestSeq!==CC_MARKET_DATA_HISTORY_STATE.requestSeq||!ccMarketDataHistoryMatches(market,symbol))return false;CC_MARKET_DATA_HISTORY_STATE.loading=false;const rows=ccMarketDataRowsFromHistory(tf);ccMarketDataUpdateInfoText(market,symbol,tf,rows,false,'Market data failed: '+e.message);ccMarketDataDraw(rows);if(!rows.length){const meta=cc('ccMarketDataMeta');if(meta)meta.textContent=ccMarketDataInfoText}return false}}
async function ccMarketDataMaybeLoadEarlier(){const market=ccMarketDataActiveTab(),symbol=String(cc('ccMarketDataSymbol')?.value||'').trim(),tf=String(cc('ccMarketDataBar')?.value||'1m'),st=CC_MARKET_DATA_CHART_STATE;if(!symbol||!ccMarketDataHistoryMatches(market,symbol)||CC_MARKET_DATA_HISTORY_STATE.loading||!CC_MARKET_DATA_HISTORY_STATE.hasMore||!Array.isArray(CC_MARKET_DATA_HISTORY_STATE.minuteRows)||!CC_MARKET_DATA_HISTORY_STATE.minuteRows.length||!st||!Array.isArray(st.rows)||!st.rows.length)return false;const visible=Math.max(1,Math.round(Number(st.view?.visible)||Number(st.visibleCandles)||st.rows.length));const maxPan=Math.max(0,st.rows.length-visible);if(Math.round(Number(st.panOffset)||0)<Math.max(0,maxPan-1))return false;const oldestTs=Number(CC_MARKET_DATA_HISTORY_STATE.minuteRows[0]?.t);if(!Number.isFinite(oldestTs)||oldestTs<=0)return false;return await ccMarketDataFetchHistoryPage(market,symbol,{beforeTs:oldestTs-1,preserveView:true})}
function ccMarketDataChartKey(){return [ccMarketDataActiveTab(),cc('ccMarketDataSymbol')?.value||'',cc('ccMarketDataBar')?.value||''].join(':')}
function ccMarketDataChartPriceValues(row,mode){return mode==='linear'?[Number(row.c),Number.isFinite(Number(row.mark))?Number(row.mark):NaN,Number.isFinite(Number(row.ask))?Number(row.ask):NaN,Number.isFinite(Number(row.bid))?Number(row.bid):NaN]:[Number(row.h),Number(row.l)]}
function ccMarketDataLastFiniteValue(rows,key){for(let i=(Array.isArray(rows)?rows.length:0)-1;i>=0;i--){const value=Number(rows[i]&&rows[i][key]);if(Number.isFinite(value))return value}return NaN}
function ccMarketDataLastFinitePoint(rows,key){for(let i=(Array.isArray(rows)?rows.length:0)-1;i>=0;i--){const row=rows[i],value=Number(row&&row[key]),t=Number(row&&row.t);if(Number.isFinite(value)&&Number.isFinite(t))return {value,t}}return null}
function ccMarketDataVisiblePointIndex(rows,time,key){for(let i=(Array.isArray(rows)?rows.length:0)-1;i>=0;i--){const row=rows[i];if(Number(row&&row.t)!==Number(time))continue;if(key&&!Number.isFinite(Number(row&&row[key])))continue;return i}return -1}
function ccMarketDataChartAutoFitPriceRange(){const st=CC_MARKET_DATA_CHART_STATE;if(!st||!Array.isArray(st.rows)||!st.rows.length)return false;const mode=ccMarketDataMode();const total=st.rows.length;const visible=Math.max(1,Math.round(Number(st.view?.visible)||Number(st.visibleCandles)||total));const panOffset=Math.max(-ccChartRightBlankCap(visible),Math.min(Math.max(0,total-visible),Math.round(Number(st.panOffset)||0)));const rightBlank=Math.max(0,-panOffset);const end=total-Math.max(0,panOffset);const rowsShown=st.rows.slice(Math.max(0,end-Math.max(1,visible-rightBlank)),end);const vals=rowsShown.flatMap(r=>ccMarketDataChartPriceValues(r,mode)).filter(Number.isFinite);if(!vals.length)return false;const min=Math.min(...vals),max=Math.max(...vals),span=max-min||Math.max(Math.abs(max)*.001,1e-8),pad=Math.max(span*.045,Math.abs((min+max)/2)*1e-7,1e-8);st.priceRange={min:min-pad,max:max+pad};st.panOffset=panOffset;return true}
function ccBindMarketDataChartWheel(canvas){const st=CC_MARKET_DATA_CHART_STATE;if(!canvas||!st||st.wheelBound)return;st.wheelBound=true;const pointFromEvent=event=>{const rect=canvas.getBoundingClientRect();return{x:event.clientX-rect.left,y:event.clientY-rect.top}};const inPlot=p=>{const v=st.view;return v&&p.x>=v.padL&&p.x<=v.w-v.padR&&p.y>=v.padT&&p.y<=v.padT+v.chartH};const inPriceScale=p=>{const v=st.view;return v&&p.x>v.w-v.padR&&p.x<=v.w&&p.y>=v.padT&&p.y<=v.padT+v.chartH};const inTimeScale=p=>{const v=st.view;return v&&p.x>=v.padL&&p.x<=v.w-v.padR&&p.y>v.padT+v.chartH&&p.y<=v.h};const maxVisible=()=>{const total=st.rows.length;const v=st.view;const chartW=v?.chartW||Math.max(1,canvas.clientWidth-94);return ccChartMaxVisibleCandles(total,chartW)};const applyHorizontalZoomFromDrag=(drag,p)=>{const total=st.rows.length;if(!total)return;const max=maxVisible(),min=Math.min(max,20);const dx=p.x-drag.x;const factor=Math.exp(dx/180);const next=Math.round(Math.max(min,Math.min(max,drag.visible*factor)));const focusRatio=Math.max(0,Math.min(1,(drag.x-drag.padL)/Math.max(1,drag.chartW)));const oldAfter=drag.visible*(1-focusRatio),nextAfter=next*(1-focusRatio);const maxPan=Math.max(0,total-next),minPan=-ccChartRightBlankCap(next);st.visibleCandles=next;st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(drag.panOffset+oldAfter-nextAfter)))};const applyVerticalZoomFromDrag=(drag,p)=>{const dy=p.y-drag.y;const factor=Math.exp(dy/160);const maxSpan=Math.max(drag.autoSpan*12,drag.span*50,1e-8);const minSpan=Math.max(drag.autoSpan*.02,Math.abs(drag.center)*1e-8,1e-8);const nextSpan=Math.max(minSpan,Math.min(maxSpan,drag.span*factor));st.priceRange={min:drag.center-nextSpan/2,max:drag.center+nextSpan/2}};canvas.addEventListener('wheel',event=>{if(st.drag){event.preventDefault();return}const total=st.rows.length;if(!total)return;const max=maxVisible();const current=ccChartClampVisible(st,total,max);const factor=event.deltaY<0?.82:1.22;const min=Math.min(max,20);const next=Math.round(Math.max(min,Math.min(max,current*factor)));if(next!==current){event.preventDefault();st.visibleCandles=next;const maxPan=Math.max(0,total-next),minPan=-ccChartRightBlankCap(next);st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));ccMarketDataDraw()}},{passive:false});canvas.addEventListener('dblclick',event=>{if(event.button!==0)return;const p=pointFromEvent(event);if(!inPriceScale(p))return;event.preventDefault();event.stopPropagation();st.drag=null;if(ccMarketDataChartAutoFitPriceRange())ccMarketDataDraw()});canvas.addEventListener('mousedown',event=>{if(event.button!==0)return;const p=pointFromEvent(event);const v=st.view;if(!v)return;const mode=inPriceScale(p)?'price-scale':(inTimeScale(p)?'time-scale':'pan');st.drag={mode,x:p.x,y:p.y,padL:v.padL,chartW:v.chartW,panOffset:v.panOffset,min:v.min,max:v.max,center:(v.min+v.max)/2,span:v.span,autoSpan:Math.max(1e-8,v.span),step:v.step,chartH:v.chartH,visible:v.visible,total:st.rows.length};canvas.style.cursor=mode==='price-scale'?'ns-resize':(mode==='time-scale'?'ew-resize':'grabbing');event.preventDefault()});window.addEventListener('mousemove',event=>{const drag=st.drag;if(!drag)return;const p=pointFromEvent(event);if(drag.mode==='price-scale')applyVerticalZoomFromDrag(drag,p);else if(drag.mode==='time-scale')applyHorizontalZoomFromDrag(drag,p);else{const total=st.rows.length;const visible=Math.max(1,Math.round(Number(drag.visible)||Number(st.visibleCandles)||total));st.visibleCandles=visible;const maxPan=Math.max(0,total-visible),minPan=-ccChartRightBlankCap(visible);const dx=p.x-drag.x,dy=p.y-drag.y;st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(drag.panOffset+dx/Math.max(1,drag.step))));const priceShift=dy/Math.max(1,drag.chartH)*drag.span;st.priceRange={min:drag.min+priceShift,max:drag.max+priceShift};st.crosshair={x:p.x,y:p.y}}ccMarketDataDraw();event.preventDefault()});canvas.addEventListener('mousemove',event=>{if(st.drag)return;const p=pointFromEvent(event);st.crosshair=p;canvas.style.cursor=inPriceScale(p)?'ns-resize':(inTimeScale(p)?'ew-resize':(inPlot(p)?'grab':'crosshair'));ccMarketDataDraw()});window.addEventListener('mouseup',()=>{if(st.drag){st.drag=null;canvas.style.cursor='grab'}});canvas.addEventListener('mouseleave',()=>{if(!st.drag){st.crosshair=null;canvas.style.cursor='crosshair';ccMarketDataDraw()}})}
function ccMarketDataNumberOrNaN(v){if(v===null||v===undefined||v==='')return NaN;const n=Number(v);return Number.isFinite(n)?n:NaN}
function ccMarketDataNormalizeRows(rows){return (Array.isArray(rows)?rows:[]).map(r=>{const bid=ccMarketDataNumberOrNaN(r&&r.bestBidClose),ask=ccMarketDataNumberOrNaN(r&&r.bestAskClose),mark=ccMarketDataNumberOrNaN(r&&r.markPriceClose);const o=ccMarketDataNumberOrNaN(r&&r.open),h=ccMarketDataNumberOrNaN(r&&r.high),l=ccMarketDataNumberOrNaN(r&&r.low),c=ccMarketDataNumberOrNaN(r&&r.close),v=ccMarketDataNumberOrNaN(r&&r.volume);return {t:ccChartNormalizeTs(r&&r.minuteUtc),o,h,l,c,v,mark,bid,ask,markO:mark,markH:mark,markL:mark,markC:mark,bidO:bid,bidH:bid,bidL:bid,bidC:bid,askO:ask,askH:ask,askL:ask,askC:ask}}).filter(r=>Number.isFinite(r.t)&&([r.o,r.h,r.l,r.c].every(Number.isFinite)||[r.c,r.mark,r.bid,r.ask].some(Number.isFinite))).sort((a,b)=>a.t-b.t)}
function ccMarketDataRowsForTimeframe(rows,tf,keepAll=false){const ordered=(Array.isArray(rows)?rows:[]).map(r=>({t:ccChartMinuteBucket(r&&r.t),o:ccMarketDataNumberOrNaN(r&&r.o),h:ccMarketDataNumberOrNaN(r&&r.h),l:ccMarketDataNumberOrNaN(r&&r.l),c:ccMarketDataNumberOrNaN(r&&r.c),v:ccMarketDataNumberOrNaN(r&&r.v),mark:ccMarketDataSeriesValue(r,'mark','C'),bid:ccMarketDataSeriesValue(r,'bid','C'),ask:ccMarketDataSeriesValue(r,'ask','C'),markO:ccMarketDataSeriesValue(r,'mark','O'),markH:ccMarketDataSeriesValue(r,'mark','H'),markL:ccMarketDataSeriesValue(r,'mark','L'),markC:ccMarketDataSeriesValue(r,'mark','C'),bidO:ccMarketDataSeriesValue(r,'bid','O'),bidH:ccMarketDataSeriesValue(r,'bid','H'),bidL:ccMarketDataSeriesValue(r,'bid','L'),bidC:ccMarketDataSeriesValue(r,'bid','C'),askO:ccMarketDataSeriesValue(r,'ask','O'),askH:ccMarketDataSeriesValue(r,'ask','H'),askL:ccMarketDataSeriesValue(r,'ask','L'),askC:ccMarketDataSeriesValue(r,'ask','C')})).filter(r=>Number.isFinite(r.t)&&([r.o,r.h,r.l,r.c].every(Number.isFinite)||[r.c,r.mark,r.bid,r.ask].some(Number.isFinite))).sort((a,b)=>a.t-b.t);if(tf==='1m')return keepAll?ordered:ordered.slice(-180);const bucketMs=CC_INTERVAL_MS[tf]||60000;const byBucket=new Map();for(const row of ordered){const bucket=Math.floor(row.t/bucketMs)*bucketMs;let prev=byBucket.get(bucket);if(!prev){prev={t:bucket,o:row.o,h:row.h,l:row.l,c:row.c,v:Number.isFinite(row.v)?row.v:null,mark:NaN,bid:NaN,ask:NaN,markO:NaN,markH:NaN,markL:NaN,markC:NaN,bidO:NaN,bidH:NaN,bidL:NaN,bidC:NaN,askO:NaN,askH:NaN,askL:NaN,askC:NaN};byBucket.set(bucket,prev)}else{if(Number.isFinite(row.o)&&!Number.isFinite(prev.o))prev.o=row.o;if(Number.isFinite(row.h))prev.h=Number.isFinite(prev.h)?Math.max(prev.h,row.h):row.h;if(Number.isFinite(row.l))prev.l=Number.isFinite(prev.l)?Math.min(prev.l,row.l):row.l;if(Number.isFinite(row.c))prev.c=row.c;if(Number.isFinite(row.v))prev.v=(Number.isFinite(prev.v)?prev.v:0)+row.v}ccMarketDataSeriesAssign(prev,'mark',ccMarketDataSeriesValue(row,'mark','C'));ccMarketDataSeriesAssign(prev,'bid',ccMarketDataSeriesValue(row,'bid','C'));ccMarketDataSeriesAssign(prev,'ask',ccMarketDataSeriesValue(row,'ask','C'))}const aggregated=Array.from(byBucket.values()).filter(r=>[r.o,r.h,r.l,r.c].every(Number.isFinite)||[r.c,r.mark,r.bid,r.ask].some(Number.isFinite)).sort((a,b)=>a.t-b.t);return keepAll?aggregated:aggregated.slice(-180)}
function ccMarketDataDraw(rows){ccLastMarketDataRows=rows||ccLastMarketDataRows;const canvas=cc('ccMarketDataChart'),wrap=canvas&&canvas.parentElement,st=CC_MARKET_DATA_CHART_STATE;if(!canvas||!wrap||!st)return;ccRenderMarketDataLegend();ccBindMarketDataChartWheel(canvas);const data=Array.isArray(ccLastMarketDataRows)?ccLastMarketDataRows:[],mode=ccMarketDataMode(),chartKey=ccMarketDataChartKey(),isCandleRow=row=>Number.isFinite(Number(row&&row.o))&&Number.isFinite(Number(row&&row.h))&&Number.isFinite(Number(row&&row.l))&&Number.isFinite(Number(row&&row.c)),isLinearRow=row=>[row&&row.c,row&&row.mark,row&&row.ask,row&&row.bid].some(v=>Number.isFinite(Number(v)));if(st.chartKey!==chartKey){st.chartKey=chartKey;st.priceRange=null;st.panOffset=-8;st.visibleCandles=86;st.crosshair=null;st.drag=null;st.lastPersistSig='';ccMarketDataViewportRestore(chartKey)}st.rows=data.map(r=>({t:Number(r.t),o:ccMarketDataNumberOrNaN(r&&r.o),h:ccMarketDataNumberOrNaN(r&&r.h),l:ccMarketDataNumberOrNaN(r&&r.l),c:ccMarketDataNumberOrNaN(r&&r.c),mark:ccMarketDataSeriesValue(r,'mark','C'),ask:ccMarketDataSeriesValue(r,'ask','C'),bid:ccMarketDataSeriesValue(r,'bid','C'),markO:ccMarketDataSeriesValue(r,'mark','O'),markH:ccMarketDataSeriesValue(r,'mark','H'),markL:ccMarketDataSeriesValue(r,'mark','L'),markC:ccMarketDataSeriesValue(r,'mark','C'),askO:ccMarketDataSeriesValue(r,'ask','O'),askH:ccMarketDataSeriesValue(r,'ask','H'),askL:ccMarketDataSeriesValue(r,'ask','L'),askC:ccMarketDataSeriesValue(r,'ask','C'),bidO:ccMarketDataSeriesValue(r,'bid','O'),bidH:ccMarketDataSeriesValue(r,'bid','H'),bidL:ccMarketDataSeriesValue(r,'bid','L'),bidC:ccMarketDataSeriesValue(r,'bid','C')})).filter(r=>Number.isFinite(r.t)&&(isCandleRow(r)||isLinearRow(r)));const dpr=window.devicePixelRatio||1,w=Math.max(320,(wrap.clientWidth||canvas.clientWidth||900)-16),h=Number(canvas.getAttribute('height'))||420;canvas.style.width=w+'px';canvas.width=Math.floor(w*dpr);canvas.height=Math.floor(h*dpr);const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);ctx.fillStyle='#090B0D';ctx.fillRect(0,0,w,h);if(!st.rows.length){if(mode!=='linear'){ctx.fillStyle='#64748b';ctx.font='13px Segoe UI';ctx.textAlign='center';ctx.fillText('No stored chart data',w/2,h/2)}ccMarketDataSetOhlc(null);return}const padL=12,padR=mode==='linear'?118:82,padT=12,padB=46,chartW=Math.max(1,w-padL-padR),chartH=h-padT-padB,visible=ccChartClampVisible(st,st.rows.length,chartW),maxPan=Math.max(0,st.rows.length-visible),minPan=-ccChartRightBlankCap(visible),panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));st.panOffset=panOffset;const rightBlank=Math.max(0,-panOffset),end=st.rows.length-Math.max(0,panOffset),rowsShown=st.rows.slice(Math.max(0,end-Math.max(1,visible-rightBlank)),end);let vals=rowsShown.flatMap(r=>ccMarketDataChartPriceValues(r,mode)).filter(Number.isFinite);const autoMin=Math.min(...vals),autoMax=Math.max(...vals),autoSpan=autoMax-autoMin||Math.max(Math.abs(autoMax)*.001,1e-8),manual=st.priceRange,useManual=manual&&Number.isFinite(manual.min)&&Number.isFinite(manual.max)&&manual.max>manual.min,min=useManual?manual.min:autoMin-autoSpan*.045,max=useManual?manual.max:autoMax+autoSpan*.045,span=max-min||1;st.view={padL,padR,padT,padB,chartW,chartH,w,h,visible,total:st.rows.length,panOffset,rightBlank,min,max,span,step:chartW/visible};const y=v=>padT+(max-v)/span*chartH,step=chartW/visible,crisp=v=>Math.round(v)+.5,timeAxisY=h-16;ctx.strokeStyle='rgba(42,46,53,.55)';ctx.lineWidth=1;ctx.font='13px Segoe UI';ctx.textBaseline='middle';for(let i=0;i<5;i++){const yy=crisp(padT+chartH*i/4);ctx.beginPath();ctx.moveTo(padL,yy);ctx.lineTo(w-padR,yy);ctx.stroke();ctx.fillStyle='#8A8F98';ctx.textAlign='left';ctx.fillText(ccChartFmt(max-span*i/4),w-padR+8,yy)}ctx.textAlign='center';ctx.textBaseline='alphabetic';const tickCount=Math.min(5,rowsShown.length);for(let i=0;i<tickCount;i++){const idx=tickCount===1?0:Math.round(i*(rowsShown.length-1)/(tickCount-1));const xx=crisp(padL+(idx+.5)*step);ctx.strokeStyle='rgba(42,46,53,.55)';ctx.beginPath();ctx.moveTo(xx,padT);ctx.lineTo(xx,padT+chartH);ctx.stroke();ctx.fillStyle='#8A8F98';ctx.fillText(ccUtcHm(rowsShown[idx].t),xx,timeAxisY)}const bodyW=Math.max(2,Math.min(8,step*.62));if(mode==='candles'){rowsShown.forEach((r,i)=>{const xx=crisp(padL+i*step+step/2),up=r.c>=r.o,color=up?'#45C4A5':'#F04E60';ctx.strokeStyle=color;ctx.fillStyle=color;ctx.beginPath();ctx.moveTo(xx,y(r.h));ctx.lineTo(xx,y(r.l));ctx.stroke();const top=y(Math.max(r.o,r.c)),bot=y(Math.min(r.o,r.c));ctx.fillRect(xx-bodyW/2,top,bodyW,Math.max(1,bot-top))})}const line=(key,color,width=CC_LINEAR_SIDE_WIDTH)=>{ctx.save();ctx.strokeStyle=color;ctx.fillStyle=color;ctx.lineWidth=width;ctx.beginPath();let points=[],count=0,last=null;rowsShown.forEach((r,i)=>{const v=Number(r[key]);if(!Number.isFinite(v)){if(points.length)ccCanvasTraceStepPath(ctx,points);points=[];return}const point={x:padL+(i+.5)*step,y:y(v)};points.push(point);last=point;count++});if(points.length)ccCanvasTraceStepPath(ctx,points);ctx.stroke();if(count===1&&last){ctx.beginPath();ctx.arc(last.x,last.y,3,0,Math.PI*2);ctx.fill()}ctx.restore()};if(mode==='linear'){line('ask',CC_LINEAR_ASK_COLOR);line('bid',CC_LINEAR_BID_COLOR);line('mark',CC_LINEAR_MARK_COLOR);line('c',CC_LINEAR_CLOSE_COLOR,CC_LINEAR_PRICE_WIDTH)}const lastRow=st.rows[st.rows.length-1]||null,priceMarkers=[];if(mode==='candles'&&lastRow&&Number.isFinite(lastRow.c)){const candleColor=lastRow.c>=lastRow.o?'#45C4A5':'#F04E60';const closePoint={value:lastRow.c,t:lastRow.t,key:'c',bg:candleColor,lineColor:candleColor};const visibleIdx=ccMarketDataVisiblePointIndex(rowsShown,closePoint.t,closePoint.key);priceMarkers.push({value:closePoint.value,y:y(closePoint.value),bg:closePoint.bg,lineColor:closePoint.lineColor,fg:'#FFFFFF',startX:visibleIdx>=0?crisp(padL+(visibleIdx+.5)*step):w-padR})}else if(mode==='linear'){const askPoint=lastRow&&Number.isFinite(lastRow.ask)?{value:lastRow.ask,t:lastRow.t}:null,markPoint=lastRow&&Number.isFinite(lastRow.mark)?{value:lastRow.mark,t:lastRow.t}:null,closePoint=lastRow&&Number.isFinite(lastRow.c)?{value:lastRow.c,t:lastRow.t}:null,bidPoint=lastRow&&Number.isFinite(lastRow.bid)?{value:lastRow.bid,t:lastRow.t}:null;if(askPoint){const visibleIdx=ccMarketDataVisiblePointIndex(rowsShown,askPoint.t,'ask');priceMarkers.push({value:askPoint.value,y:y(askPoint.value),bg:CC_LINEAR_ASK_COLOR,lineColor:CC_LINEAR_ASK_COLOR,fg:'#FFFFFF',startX:visibleIdx>=0?crisp(padL+(visibleIdx+.5)*step):w-padR})}if(markPoint){const visibleIdx=ccMarketDataVisiblePointIndex(rowsShown,markPoint.t,'mark');priceMarkers.push({value:markPoint.value,y:y(markPoint.value),bg:CC_LINEAR_MARK_COLOR,lineColor:CC_LINEAR_MARK_COLOR,fg:'#FFFFFF',startX:visibleIdx>=0?crisp(padL+(visibleIdx+.5)*step):w-padR})}if(closePoint){const visibleIdx=ccMarketDataVisiblePointIndex(rowsShown,closePoint.t,'c');priceMarkers.push({value:closePoint.value,y:y(closePoint.value),bg:CC_LINEAR_CLOSE_COLOR,lineColor:CC_LINEAR_CLOSE_COLOR,fg:'#FFFFFF',startX:visibleIdx>=0?crisp(padL+(visibleIdx+.5)*step):w-padR})}if(bidPoint){const visibleIdx=ccMarketDataVisiblePointIndex(rowsShown,bidPoint.t,'bid');priceMarkers.push({value:bidPoint.value,y:y(bidPoint.value),bg:CC_LINEAR_BID_COLOR,lineColor:CC_LINEAR_BID_COLOR,fg:'#FFFFFF',startX:visibleIdx>=0?crisp(padL+(visibleIdx+.5)*step):w-padR})}}ccDrawStackedPriceMarkers(ctx,priceMarkers,{padL,chartH,padT,w,lineRight:w-padR,tagX:w-padR+4});let legendRow=rowsShown[rowsShown.length-1]||st.rows[st.rows.length-1]||null;const ch=st.crosshair;if(ch&&Number.isFinite(ch.x)&&Number.isFinite(ch.y)&&ch.x>=padL&&ch.x<=w-padR&&ch.y>=padT&&ch.y<=padT+chartH){const rawCx=Math.max(padL,Math.min(w-padR,ch.x)),cy=Math.max(padT,Math.min(padT+chartH,ch.y));const price=max-(cy-padT)/chartH*span;const idx=Math.max(0,Math.min(rowsShown.length-1,Math.round((rawCx-padL-step/2)/step)));const cx=padL+(idx+.5)*step;legendRow=rowsShown[idx]||legendRow;const time=new Date(rowsShown[idx].t);const timeLabel=ccUtcMdHms(time);ctx.save();ctx.strokeStyle='rgba(203,213,225,.82)';ctx.lineWidth=1;ctx.setLineDash([3,3]);ctx.beginPath();ctx.moveTo(cx,padT);ctx.lineTo(cx,padT+chartH);ctx.moveTo(padL,cy);ctx.lineTo(w-padR,cy);ctx.stroke();ctx.setLineDash([]);ccDrawPriceTag(ctx,w-padR+4,cy,ccChartFmt(price),'#64748B','#F8FAFC',w,padT,chartH);ctx.font='700 12px Segoe UI';const tw=Math.min(148,Math.max(100,ctx.measureText(timeLabel).width+12));const tx=Math.max(padL,Math.min(w-padR-tw,cx-tw/2));ctx.fillStyle='#64748B';ctx.fillRect(tx,h-padB+12,tw,20);ctx.fillStyle='#F8FAFC';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(timeLabel,tx+tw/2,h-padB+22);ctx.restore()}ccMarketDataSetOhlc(legendRow);ccMarketDataViewportPersistSoon();if(st.drag&&st.drag.mode==='pan'&&CC_MARKET_DATA_HISTORY_STATE.hasMore&&!CC_MARKET_DATA_HISTORY_STATE.loading)ccMarketDataMaybeLoadEarlier()}
async function loadCcMarketDataHistory(force=false){const market=ccMarketDataActiveTab();const preferredSymbol=cc('ccMarketDataSymbol')?.value||ccMarketDataSavedValue(market,'symbol')||cc('ccTradeMarketInst')?.value||'';const preferredBar=cc('ccMarketDataBar')?.value||ccMarketDataSavedValue(market,'timeframe')||cc('ccTradeMarketBar')?.value||'';ccSyncMarketDataSymbolOptions(market,preferredSymbol);ccSyncMarketDataTimeframeOptions(market,preferredBar);const sel=cc('ccMarketDataSymbol'),bar=cc('ccMarketDataBar'),meta=cc('ccMarketDataMeta');const symbol=String(sel&&sel.value||'').trim(),tf=String(bar&&bar.value||'1m');ccSaveMarketDataSelection(market);if(!symbol){ccMarketDataInfoText='No stored symbols.';if(meta)meta.textContent=ccMarketDataInfoText;ccMarketDataDraw([]);return}if(ccMarketDataHistoryMatches(market,symbol)&&Array.isArray(CC_MARKET_DATA_HISTORY_STATE.minuteRows)&&CC_MARKET_DATA_HISTORY_STATE.minuteRows.length){const rows=ccMarketDataRowsFromHistory(tf);ccMarketDataUpdateInfoText(market,symbol,tf,rows,CC_MARKET_DATA_HISTORY_STATE.loading);ccMarketDataDraw(rows);if(force&&!CC_MARKET_DATA_HISTORY_STATE.loading)await ccMarketDataFetchHistoryPage(market,symbol,{reset:false});return}ccMarketDataInfoText='Loading...';if(meta)meta.textContent=ccMarketDataInfoText;await ccMarketDataFetchHistoryPage(market,symbol,{reset:true})}
function ccSetMarketDataTab(market,persist=true){document.querySelectorAll('[data-cc-md-tab]').forEach(x=>x.classList.toggle('active',x.dataset.ccMdTab===market));if(persist)ccUiStateSave({dataMarketTab:market});const savedSymbol=ccMarketDataSavedValue(market,'symbol');const savedTimeframe=ccMarketDataSavedValue(market,'timeframe');const apply=()=>{ccSyncMarketDataSymbolOptions(market,savedSymbol);ccSyncMarketDataTimeframeOptions(market,savedTimeframe);ccSaveMarketDataSelection(market);loadCcMarketDataHistory()};if(market==='futures'){loadCcFuturesInstruments().catch(()=>null).finally(apply);return}apply()}
function ccPlaceContractToolbar(){const toolbar=document.querySelector('.cc-futures-contract-toolbar');if(!toolbar)return;const anchor=cc(ccActiveMarket()==='futures'?'ccFuturesContractToolbarAnchor':'ccSpotContractToolbarAnchor');if(anchor&&toolbar.parentElement!==anchor)anchor.appendChild(toolbar)}function ccBindViewportWheelToMain(){if(window.__ccViewportWheelBound)return;window.__ccViewportWheelBound=true;window.addEventListener('wheel',event=>{if(event.defaultPrevented)return;const main=document.querySelector('.main');if(!main||main.contains(event.target))return;const max=main.scrollHeight-main.clientHeight;if(max<=0)return;main.scrollBy({top:event.deltaY,left:event.deltaX,behavior:'auto'});event.preventDefault()},{passive:false})}
document.querySelectorAll('[data-cc-layer]').forEach(b=>b.addEventListener('click',()=>{const layer=b.dataset.ccLayer||'trade';ccApplyLayerState(layer);ccUiStateSave({layer});if(layer==='assets')summary();ccUpdateAssetsFunding1mAutoRefresh();ccUpdatePnlUsdAutoRefresh();}));
function ccIsAssetsFundingPanelActive(name){const panel=document.querySelector('[data-cc-assets-funding-panel="'+name+'"]');return !!(document.getElementById('cc-tab-funding')?.classList.contains('active')&&panel&&!panel.hidden)}
function ccIsAssetsFundingHistoryActive(){return ccIsAssetsFundingPanelActive('history')}
function ccIsAssetsFunding1mActive(){return ccIsAssetsFundingPanelActive('one-month')}
function ccAssetsFundingNextUtcMinutePlus2Delay(now=Date.now()){let next=Math.floor(now/60000)*60000+2000;if(next<=now)next+=60000;return next-now}
function ccAssetsFunding1mAutoRefreshActive(){return !document.hidden&&ccActiveLayer()==='assets'&&ccIsAssetsFunding1mActive()}
function ccClearAssetsFunding1mAutoRefresh(){if(window.ccAssetsFunding1mRefreshTimer){clearTimeout(window.ccAssetsFunding1mRefreshTimer);window.ccAssetsFunding1mRefreshTimer=null}}
function ccScheduleAssetsFunding1mAutoRefresh(){ccClearAssetsFunding1mAutoRefresh();if(!ccAssetsFunding1mAutoRefreshActive())return;window.ccAssetsFunding1mRefreshTimer=setTimeout(async()=>{window.ccAssetsFunding1mRefreshTimer=null;if(!ccAssetsFunding1mAutoRefreshActive()||ccAssetsFunding1mRefreshBusy){ccScheduleAssetsFunding1mAutoRefresh();return}ccAssetsFunding1mRefreshBusy=true;try{await loadCcAssetsFunding1m()}finally{ccAssetsFunding1mRefreshBusy=false;ccScheduleAssetsFunding1mAutoRefresh()}},ccAssetsFundingNextUtcMinutePlus2Delay())}
function ccUpdateAssetsFunding1mAutoRefresh(){if(ccAssetsFunding1mAutoRefreshActive())ccScheduleAssetsFunding1mAutoRefresh();else ccClearAssetsFunding1mAutoRefresh()}
function ccSetAssetsFundingTab(tab){const target=tab==='one-month'?'one-month':'history';document.querySelectorAll('[data-cc-assets-funding-tab]').forEach(b=>b.classList.toggle('active',b.dataset.ccAssetsFundingTab===target));document.querySelectorAll('[data-cc-assets-funding-panel]').forEach(p=>{p.hidden=p.dataset.ccAssetsFundingPanel!==target});if(target==='history')loadCcAssetsFundingHistory();else loadCcAssetsFunding1m();ccUpdateAssetsFunding1mAutoRefresh()}
document.querySelectorAll('[data-cc-assets-funding-tab]').forEach(b=>b.addEventListener('click',()=>ccSetAssetsFundingTab(b.dataset.ccAssetsFundingTab)));
document.querySelectorAll('[data-cc-pnl-asset]').forEach(b=>b.addEventListener('click',()=>{const asset=String(b.dataset.ccPnlAsset||'USD').toUpperCase();ccApplyPnlAssetTab(asset);if(asset==='USD')loadCcPnl();else if(asset==='BTC'||asset==='BTC_USD')loadCcBtcAssetEquity(asset==='BTC'?'native':'usd');ccUpdatePnlUsdAutoRefresh()}));
document.querySelectorAll('[data-cc-pnl-clearing-currency]').forEach(b=>b.addEventListener('click',()=>{ccPnlClearingCurrency=String(b.dataset.ccPnlClearingCurrency||'BTC').toUpperCase();ccSyncPnlClearingTabs();loadCcDailyClearing()}));
document.querySelectorAll('[data-cc-asset-tab]').forEach(b=>b.addEventListener('click',()=>{document.querySelectorAll('[data-cc-asset-tab]').forEach(x=>x.classList.toggle('active',x===b));document.querySelectorAll('#cc-layer-assets > .tab-panel').forEach(x=>x.classList.toggle('active',x.id==='cc-tab-'+b.dataset.ccAssetTab));ccUiStateSave({assetTab:b.dataset.ccAssetTab||'overview'});if(b.dataset.ccAssetTab==='pnl')loadCcPnl();if(b.dataset.ccAssetTab==='funding'&&ccIsAssetsFundingHistoryActive())loadCcAssetsFundingHistory();if(b.dataset.ccAssetTab==='funding'&&ccIsAssetsFunding1mActive())loadCcAssetsFunding1m();if(b.dataset.ccAssetTab==='transfer-records')loadCcTransferRecords();ccUpdateAssetsFunding1mAutoRefresh();ccUpdatePnlUsdAutoRefresh();}));
document.querySelectorAll('[data-cc-trade]').forEach(b=>b.addEventListener('click',()=>{const previousMarket=ccActiveMarket();document.querySelectorAll('[data-cc-trade]').forEach(x=>x.classList.toggle('active',x===b));const mode=b.dataset.ccTrade||'spot';const marketData=mode==='market-data';const spreads=mode==='spreads';const spot=!marketData&&!spreads&&mode!=='futures';cc('ccMarketDataPanel')?.classList.toggle('active',marketData);if(cc('ccSpreadsPanel')){cc('ccSpreadsPanel').hidden=!spreads;cc('ccSpreadsPanel').classList.toggle('active',spreads)}cc('ccSpotShell').hidden=!spot;cc('ccSpotBottomPanel').hidden=!spot;cc('ccFuturesShell').hidden=spot||marketData||spreads;cc('ccFuturesBottomPanel').hidden=spot||marketData||spreads;cc('ccSpotShell').classList.toggle('active',spot);cc('ccSpotBottomPanel').classList.toggle('active',spot);cc('ccFuturesShell').classList.toggle('active',!spot&&!marketData&&!spreads);cc('ccFuturesBottomPanel').classList.toggle('active',!spot&&!marketData&&!spreads);cc('cc-layer-trade')?.classList.toggle('cc-spreads-active',spreads);const toolbar=document.querySelector('.cc-futures-contract-toolbar');if(toolbar)toolbar.hidden=mode!=='futures';ccUiStateSave({trade:mode});ccTradeSyncTopControls();if(marketData){ccStopBookRefresh('spot');ccStopBookRefresh('futures');const targetMarket=ccUiStateLoad().dataMarketTab||(previousMarket==='spot'?'spot':'futures');const finish=()=>{ccSetMarketDataTab(targetMarket,false);ccSetMarketDataMode(ccUiStateLoad().dataChartMode||ccMarketDataMode(),false)};finish();return}if(spreads){ccStopBookRefresh('spot');ccStopBookRefresh('futures');ccSpreadsRefresh(false).catch(()=>{});return}const market=spot?'spot':'futures';if(mode==='futures')ccPlaceContractToolbar();if(toolbar)toolbar.hidden=mode!=='futures';ccSyncFuturesMarketChips();ccTradeApplyChartModeUi();if(!spot){loadCcFuturesLeverage();ccRefreshActiveFuturesPanel()}loadCcChart(market);ccStartBookRefresh(market);if(!spot){ccScheduleChartRedraw('ccFuturesChart',0);ccScheduleChartRedraw('ccFuturesChart',120)}}));
let ccAccountsCache=[];function syncCcSelectedAccount(){ccRenderSpotBottomPanels();const sel=cc('ccAccountSelect');const current=ccAccountsCache.find(a=>String(a.id)===String(sel&&sel.value))||ccAccountsCache.find(a=>a.isActive);const box=cc('ccStoreMinuteEquity');const cur=cc('ccEquityCurrency');if(box)box.checked=!!(current&&current.storeMinuteEquity);if(cur)cur.value=(current&&current.equityCurrency)||'USD';const settings=cc('ccSettingsStatus');if(settings)settings.textContent=current?('Name: '+current.name+'\nKey: '+(current.apiKeyMasked||'—')+'\nStatus: '+(current.isActive?'Active':'—')):'No accounts';cc('ccSelectedAccountBadge').innerHTML='<img class="coincall-account-icon" src="/coincall-logo.jpg" alt="CoinCall"><strong>'+ccEsc(current?current.name:'—')+'</strong>';cc('ccSelectedAccountBadge').classList.remove('empty')}async function loadAccounts(){try{const j=await ccApi('/api/admin/coincall/accounts');const items=j.items||[];ccAccountsCache=items;const sel=cc('ccAccountSelect');if(sel){const prev=sel.value;sel.innerHTML=items.map(a=>'<option value="'+a.id+'"'+(a.isActive?' selected':'')+'>'+ccEsc(a.name)+' · '+ccEsc(a.apiKeyMasked||'')+(a.isActive?' · Active':'')+' · equity:'+(a.equityCurrency||'USD')+(a.storeMinuteEquity?' · minute:on':' · minute:off')+'</option>').join('')||'<option value="">No accounts</option>';if(prev&&items.some(a=>String(a.id)===String(prev)))sel.value=prev;}const active=items.find(a=>a.isActive);if(active&&sel&&!sel.value)sel.value=active.id;const upd=cc('ccLastUpdate');if(upd)upd.textContent=items.length?(items.length+' account'+(items.length===1?'':'s')+' loaded'):'';syncCcSelectedAccount();if(items.length){summary();loadCcFuturesLeverage();ccRefreshActiveFuturesPanel();ccSpreadsRefresh(true).catch(()=>{});if(document.getElementById('cc-tab-pnl')?.classList.contains('active'))loadCcPnl()}}catch(e){const settings=cc('ccSettingsStatus');if(settings)settings.textContent=e.message;const st=cc('ccTopStatus');if(st)st.textContent=e.message;}}
async function deleteSelectedAccount(){try{const sel=cc('ccAccountSelect');const id=sel&&sel.value;if(!id)throw new Error('No CoinCall account selected');const label=sel.options&&sel.selectedIndex>=0?sel.options[sel.selectedIndex].textContent:'selected account';if(!confirm('Delete CoinCall account '+label+'?'))return;await ccApi('/api/admin/coincall/accounts/'+encodeURIComponent(id),{method:'DELETE'});const st=cc('ccTopStatus');if(st)st.textContent='CoinCall account deleted';loadAccounts()}catch(e){const st=cc('ccTopStatus');if(st)st.textContent=e.message;else alert(e.message)}}
async function saveAccountSettings(){try{const sel=cc('ccAccountSelect');const id=sel&&sel.value;if(!id)throw new Error('No CoinCall account selected');await ccApi('/api/admin/coincall/accounts/'+id+'/settings',{method:'PUT',body:JSON.stringify({storeMinuteEquity:cc('ccStoreMinuteEquity').checked,equityCurrency:cc('ccEquityCurrency').value})});const st=cc('ccSettingsStatus');if(st)st.textContent='CoinCall account settings saved';loadAccounts()}catch(e){const st=cc('ccSettingsStatus');if(st)st.textContent=e.message;}}
async function activateAccount(id){try{if(!id){const sel=cc('ccAccountSelect');id=sel&&sel.value;}if(!id)throw new Error('No CoinCall account selected');await ccApi('/api/admin/coincall/accounts/'+id+'/activate',{method:'PUT'});const st=cc('ccTopStatus');if(st)st.textContent='Active account updated';loadAccounts()}catch(e){const st=cc('ccTopStatus');if(st)st.textContent=e.message;else alert(e.message)}}
async function testCreds(){try{const apiKey=(cc('ccApiKey').value||'').trim();const apiSecret=(cc('ccApiSecret').value||'').trim();let res;if(apiKey&&apiSecret){res=await ccApi('/api/admin/coincall/test-credentials',{method:'POST',body:JSON.stringify({name:cc('ccName').value,apiKey,apiSecret})});}else{const sel=cc('ccAccountSelect');const id=sel&&sel.value;if(!id)throw new Error('Enter API Key and API Secret or select a saved CoinCall account');res=await ccApi('/api/admin/coincall/summary?accountId='+encodeURIComponent(id));}ccOut('ccAssetsOut',res);const st=cc('ccTopStatus');if(st)st.textContent='CoinCall credentials test completed'}catch(e){ccOut('ccAssetsOut','Test failed: '+e.message);const st=cc('ccTopStatus');if(st)st.textContent='Test failed: '+e.message}}
async function saveAccount(){try{const setActiveEl=cc('ccSetActive');const res=await ccApi('/api/admin/coincall/accounts',{method:'POST',body:JSON.stringify({name:cc('ccName').value,apiKey:cc('ccApiKey').value,apiSecret:cc('ccApiSecret').value,setActive:!setActiveEl||setActiveEl.checked,storeMinuteEquity:cc('ccStoreMinuteEquityNew').checked,equityCurrency:cc('ccEquityCurrencyNew').value})});ccOut('ccAssetsOut',res);const st=cc('ccAddStatus');if(st)st.textContent='CoinCall account saved';loadAccounts()}catch(e){ccOut('ccAssetsOut','Save failed: '+e.message);const st=cc('ccAddStatus');if(st)st.textContent='Save failed: '+e.message}}
async function loadInstruments(m){try{const sym=(m==='futures'?cc('ccFuturesInst').value:cc('ccSpotInst').value);const q=m==='spot'?'?symbol='+encodeURIComponent(sym):'';ccOut('ccAssetsOut',await ccApi('/api/admin/coincall/public/'+m+'/instruments'+q))}catch(e){ccOut('ccAssetsOut','Market data failed: '+e.message)}}
function ccMoney(v,ccy){const n=Number(v);if(!Number.isFinite(n))return '—';const digits=Math.abs(n)>=1000?2:(Math.abs(n)>=1?4:8);return n.toLocaleString('en-US',{minimumFractionDigits:0,maximumFractionDigits:digits})+(ccy?' '+ccy:'')}
function ccFindKey(o,names){if(!o||typeof o!=='object')return undefined;for(const k of Object.keys(o)){const low=k.toLowerCase();if(names.includes(low))return o[k]}return undefined}
function ccCollectObjects(node,out=[]){if(Array.isArray(node)){node.forEach(x=>ccCollectObjects(x,out));return out}if(node&&typeof node==='object'){out.push(node);Object.values(node).forEach(x=>{if(x&&typeof x==='object')ccCollectObjects(x,out)})}return out}
function ccFindValueDeep(raw,keys){for(const o of ccCollectObjects(raw)){const v=ccFindKey(o,keys);if(v!==undefined&&v!==null&&v!=='')return v}return undefined}
function ccBalanceRows(raw){const root=raw&&raw.data!==undefined?raw.data:raw;const rows=[];for(const o of ccCollectObjects(root)){const ccy=ccFindKey(o,['ccy','currency','coin','asset','basecurrency','symbol','margincoin']);const balance=ccFindKey(o,['equityamount','marginbalance','accountequity','equity','eq','totalbalance','walletbalance','cashbalanceamount','cashbalance','cashbal','total','balance','bal']);const avail=ccFindKey(o,['available','avail','availablebalance','availablebal','availbalance','availeq','availableequity','free']);const frozen=ccFindKey(o,['frozen','locked','hold','freeze','usedmargin']);const upl=ccFindKey(o,['upl','unrealizedpnl','unrealisedpnl','unrealisedprofit','unrealizedprofit']);const borrowed=ccFindKey(o,['borrowed','borrowedamount','borrow','liability','liabilities','debt','loan','loanamount']);if(ccy!==undefined&&(balance!==undefined||avail!==undefined||frozen!==undefined||upl!==undefined||borrowed!==undefined)){const key=String(ccy).toUpperCase();if(!rows.some(r=>r.ccy===key&&r.balance===balance&&r.available===avail))rows.push({ccy:key,balance,available:avail,frozen,upl,borrowed,raw:o})}}return rows}
function ccClassifyBalances(rows){const trading=[],funding=[],earn=[];for(const r of rows){const raw=JSON.stringify(r.raw||{}).toLowerCase();if(raw.includes('earn'))earn.push(r);else if(raw.includes('funding'))funding.push(r);else trading.push(r)}return {trading:trading.length?trading:rows,funding,earn}}
function ccRowEquityValue(r){const balance=Number(r.balance);if(Number.isFinite(balance))return balance;const available=Number(r.available);const frozen=Number(r.frozen);if(Number.isFinite(available)||Number.isFinite(frozen))return (Number.isFinite(available)?available:0)+(Number.isFinite(frozen)?frozen:0);return NaN}function ccCoincallAssetAvailableValue(r){const cash=Number(r&&r.raw&&('cashBalanceAmount' in r.raw?r.raw.cashBalanceAmount:r.raw.cashbalanceamount));if(Number.isFinite(cash))return cash;const available=Number(r&&r.available);return Number.isFinite(available)?available:NaN}
function ccSumUsd(rows){let s=0,ok=false;for(const r of rows){const c=String(r.ccy||"").toUpperCase();const v=ccRowEquityValue(r);if(Number.isFinite(v)&&(c==="USD"||c==="USDT"||c==="USDC")){s+=v;ok=true}}return ok?s:null}
function ccHeaderStableSum(rows,field){let s=0,ok=false;for(const r of rows){const c=String(r.ccy||"").toUpperCase();const v=field==="balance"?ccRowEquityValue(r):Number(r.available??r.balance);if(Number.isFinite(v)&&(c==="USD"||c==="USDT"||c==="USDC")){s+=v;ok=true}}return ok?s:null}
function ccFindNumericDeep(raw,keys){for(const o of ccCollectObjects(raw)){for(const k of keys){const v=ccFindKey(o,[k]);const n=Number(v);if(Number.isFinite(n))return n}}return NaN}
function ccMarginModeText(raw){const v=ccFindValueDeep(raw,['marginmode','margin_mode','marginmodel','accountmarginmode']);const text=String(v??'').trim().toUpperCase().replace(/[\s_-]+/g,'');if(!text)return '—';if(text==='1'||text==='SM'||text==='SINGLEMARGIN')return 'SM';if(text==='2'||text==='PM'||text==='PORTFOLIOMARGIN')return 'PM';if(text==='3'||text==='MCM'||text==='MULTICURRENCY'||text==='MULTICURRENCYMARGIN')return 'MULTICURRENCY';return String(v).trim()}
function ccHeaderPct(raw,usedKeys,pctKeys,equity){const direct=ccFindNumericDeep(raw,pctKeys);if(Number.isFinite(direct))return direct>1?direct:direct*100;const used=ccFindNumericDeep(raw,usedKeys);return Number.isFinite(used)&&Number.isFinite(equity)&&equity>0?used/equity*100:0}
function ccRenderHeaderMetrics(raw){const metrics=raw&&raw.metrics?raw.metrics:null;const equity=Number(metrics&&metrics.totalEquity),available=Number(metrics&&metrics.availableEquity);const im=ccHeaderPct(raw,['initialmargin','initialmarginused','im','usedmargin'],['imrate','imratio','initialmarginrate','initialmarginratio'],equity);const mm=ccHeaderPct(raw,['maintenancemargin','maintmargin','mm'],['mmrate','mmratio','maintenancemarginrate','maintenancemarginratio'],equity);const marginMode=ccMarginModeText(raw);const set=(id,text)=>{const el=cc(id);if(el)el.textContent=text};if(Number.isFinite(equity))set('ccHeaderEquity',ccFmt(equity,2));if(Number.isFinite(available))set('ccHeaderAvailable',ccFmt(available,2));set('ccHeaderIm',Number.isFinite(im)?im.toFixed(2)+'%':'—');set('ccHeaderMm',Number.isFinite(mm)?mm.toFixed(2)+'%':'—');set('ccHeaderMarginMode',marginMode);const mmode=cc('ccHeaderMarginMode');if(mmode)mmode.title='Margin Mode: '+marginMode}
function ccIsNonZeroTradingBalance(r){return [r.balance,r.available,r.frozen,r.upl].some(v=>{const n=Number(v);return Number.isFinite(n)&&Math.abs(n)>0})}
function ccTradingNonZeroEnabled(){return Array.from(document.querySelectorAll('.ccTradingNonZeroToggle')).some(x=>x.checked)}
function ccSyncTradingNonZeroToggles(on){document.querySelectorAll('.ccTradingNonZeroToggle').forEach(x=>{x.checked=!!on})}
let ccLastAssetsSummary=null;let ccLastSpotOpenOrders=[];let ccLastSpotOrderHistory=[];let ccLastSpotTradeHistory=[];let ccLastFuturesOpenOrders=[];let ccLastFuturesOrderHistory=[];let ccLastFuturesTradeHistory=[];
const CC_SPOT_FILL_REFRESH_DELAYS=[250,1500,5000,12000];const CC_SPOT_OPEN_ORDERS_RECONCILE_MS=90000,CC_SPOT_PRIVATE_WS_STALE_MS=45000,CC_SPOT_PRIVATE_WS_OPEN_RECONCILE_FLOOR_MS=60000,CC_SPOT_PRIVATE_WS_STATUS_DIAG_MS=30000,CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS=2000,CC_SPOT_PRIVATE_WS_BACKOFF_CAP_MS=30000,CC_SPOT_PRIVATE_WS_BACKOFF_JITTER=0.25,CC_SPOT_PRIVATE_WS_STABLE_RESET_MS=30000;const CC_SPOT_TRADE_REFRESH={timers:[],openSig:'',lastAuto:0,openOrdersPromise:null,openOrdersLastAttemptAt:0,openOrdersLoaded:false};
const CC_FUTURES_ORDER_REFRESH_DELAYS=[15000];const CC_FUTURES_ORDER_REFRESH={timers:[],protectUntil:0,seq:0,lastAppliedAt:0,lastBurstAt:0};const CC_FUTURES_CANCEL_PENDING=new Set();const CC_ORDER_SUBMIT_PENDING=new Set();let ccFuturesOpenOrdersLoaded=false;
function ccSpotTradingActive(){return ccActiveMarket&&ccActiveMarket()==='spot'&&!document.hidden}
function ccSpotPrivateSocketHealthy(now=Date.now()){const ws=CC_SPOT_PRIVATE_WS&&CC_SPOT_PRIVATE_WS.socket;if(!(ws&&ws.readyState===WebSocket.OPEN))return false;const last=Math.max(CC_SPOT_PRIVATE_WS.lastEventAt||0,CC_SPOT_PRIVATE_WS.lastOpenAt||0,CC_SPOT_PRIVATE_WS.lastHeartbeatAt||0);return !!last&&now-last<CC_SPOT_PRIVATE_WS_STALE_MS}
function ccSpotOpenOrdersRestNeeded(force=false,reason='auto',now=Date.now()){if(force)return true;if(!CC_SPOT_TRADE_REFRESH.openOrdersLoaded)return true;if(ccSpotPrivateSocketHealthy(now))return false;if(!CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt)return true;return now-CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt>=CC_SPOT_OPEN_ORDERS_RECONCILE_MS}
function ccMaybeLoadSpotOpenOrders(force=false,reason='auto'){const now=Date.now();if(!ccSpotOpenOrdersRestNeeded(force,reason,now))return Promise.resolve(ccLastSpotOpenOrders);if(!force&&CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt&&now-CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt<CC_SPOT_OPEN_ORDERS_RECONCILE_MS)return Promise.resolve(ccLastSpotOpenOrders);if(CC_SPOT_TRADE_REFRESH.openOrdersPromise)return CC_SPOT_TRADE_REFRESH.openOrdersPromise;CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt=now;CC_SPOT_TRADE_REFRESH.openOrdersPromise=Promise.resolve(loadCcSpotOpenOrders(reason)).finally(()=>{CC_SPOT_TRADE_REFRESH.openOrdersPromise=null});return CC_SPOT_TRADE_REFRESH.openOrdersPromise}
function ccScheduleSpotTradeRefreshBurst(reason=''){CC_SPOT_TRADE_REFRESH.timers.forEach(clearTimeout);if(ccSpotPrivateSocketOpen()){if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(500);return}CC_SPOT_TRADE_REFRESH.timers=CC_SPOT_FILL_REFRESH_DELAYS.map(d=>setTimeout(()=>{if(document.hidden)return;Promise.allSettled([ccMaybeLoadSpotOpenOrders(true),ccActiveBottomPanel('spot')==='trade-history'?loadCcSpotTradeHistory():Promise.resolve(),ccActiveBottomPanel('spot')==='order-history'?loadCcSpotOrderHistory():Promise.resolve()]);if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(500)},d))}
function ccScheduleFuturesOrderRefreshBurst(reason=''){const now=Date.now();if(reason==='private-event'&&CC_FUTURES_ORDER_REFRESH.lastBurstAt&&now-CC_FUTURES_ORDER_REFRESH.lastBurstAt<1200)return;CC_FUTURES_ORDER_REFRESH.lastBurstAt=now;CC_FUTURES_ORDER_REFRESH.timers.forEach(clearTimeout);if(ccFuturesPrivateSocketOpen()){if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(500);ccScheduleFuturesPrivateRefresh(CC_FUTURES_PRIVATE_RECONCILE_MS);return}CC_FUTURES_ORDER_REFRESH.timers=CC_FUTURES_ORDER_REFRESH_DELAYS.map(d=>setTimeout(()=>{if(document.hidden)return;Promise.allSettled([loadCcFuturesOpenOrders(),loadCcFuturesPositions(),ccActiveBottomPanel('futures')==='order-history'?loadCcFuturesOrderHistory():Promise.resolve()]);if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(500)},d))}
function ccFuturesOrdersSig(list){return (Array.isArray(list)?list:[]).map(o=>[ccFuturesOrderKey(o),ccPick(o,['status','state','orderStatus','os']),ccPick(o,['fillQty','filledQty','filledQuantity','filled','dealQty','fq']),ccPick(o,['qty','quantity','amount','size','orderQty','q','sz']),ccPick(o,['price','px','orderPrice','p'])].join(':')).join('|')}
function ccFuturesOrderSymbolRaw(o){return ccFirstValue(o,['displaySymbol','displayName','symbol','instId','instrument','ticker_id','baseToken','base_currency','s'])||''}
function ccFuturesCanonicalOrderSymbolKey(value){const raw=String(value||'').toUpperCase().replace(/[^A-Z0-9]/g,'');if(!raw)return '';if(raw==='BTC'||raw==='ETH')return 'PERP:'+raw;let m=raw.match(/^([A-Z0-9]+)USDT(?:PERP|PERPETUAL)$/);if(m&&m[1])return 'PERP:'+m[1];m=raw.match(/^([A-Z0-9]+)USD(?:PERP|PERPETUAL)?$/);if(m&&m[1])return 'PERP:'+m[1];m=raw.match(/^([A-Z0-9]+)USDT$/);if(m&&m[1])return 'PERP:'+m[1];return 'RAW:'+raw}
function ccFuturesCanonicalDisplaySymbol(value){const text=String(value||'').trim(),raw=text.toUpperCase().replace(/[^A-Z0-9]/g,''),dated=raw.match(/^([A-Z0-9]+)(?:USD|USDT)(\d{1,2}(?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\d{2})$/);if(dated&&dated[1]&&dated[2])return dated[1]+'USDT-'+dated[2];const key=ccFuturesCanonicalOrderSymbolKey(value),m=key.match(/^PERP:([A-Z0-9]+)$/);if(m&&m[1])return m[1]+'USDT-PERP';return text}
function ccFuturesCanonicalDisplaySymbolFromOrder(o){const keys=['displaySymbol','displayName','symbol','instId','instrument','ticker_id','baseToken','base_currency','s'];const candidates=keys.map(k=>({key:k,value:ccFirstValue(o,[k])})).filter(x=>x.value!==null&&x.value!==undefined&&String(x.value).trim()&&String(x.value).trim()!=='—');for(const item of candidates){const text=ccFuturesCanonicalDisplaySymbol(item.value);if(/\d{1,2}(?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\d{2}$/i.test(text))return text}for(const item of candidates){if(item.key==='baseToken'||item.key==='base_currency')continue;const text=ccFuturesCanonicalDisplaySymbol(item.value);if(/\bPERP\b/i.test(text))return text}return candidates.length?ccFuturesCanonicalDisplaySymbol(candidates[0].value):''}
function ccFuturesOrderShapeKey(o){const sym=ccFuturesCanonicalOrderSymbolKey(ccFuturesOrderSymbolRaw(o)),side=ccSpotOrderSideRaw(o),priceValue=ccFuturesOrderPrice(o),price=String(priceValue||''),qty=String(ccFuturesOrderAmountRaw(o)||''),rawType=String(ccFirstValue(o,['tradeType','orderType','type','ot','orderTypeName','tradeTypeName'])??'').trim().toUpperCase(),type=rawType||(Number.isFinite(priceValue)?'LIMIT':'');return sym&&side&&price&&qty?['shape',sym,side,price,qty,type].join('|'):''}
function ccFuturesOpenOrderLooksValid(o){const side=ccSpotOrderSideRaw(o),price=ccFuturesOrderPrice(o),qty=Number(ccFuturesOrderAmountRaw(o));return !!o&&!ccFuturesOrderTerminal(o)&&(side==='buy'||side==='sell')&&Number.isFinite(price)&&price>0&&Number.isFinite(qty)&&qty>0}
function ccFuturesOpenOrderDedupeKey(o){const ids=ccFuturesOrderIdentity(o);return ids.orderId?('oid:'+ids.orderId):(ids.clientOrderId?('cid:'+ids.clientOrderId):ccFuturesOrderShapeKey(o))}
function ccRememberFuturesTerminalOrder(o){const now=Date.now();const ids=ccFuturesOrderIdentity(o);if(ids.orderId)CC_FUTURES_TERMINAL_ORDER_KEYS.set('oid:'+ids.orderId,now);if(ids.clientOrderId)CC_FUTURES_TERMINAL_ORDER_KEYS.set('cid:'+ids.clientOrderId,now);for(const [k,ts] of CC_FUTURES_TERMINAL_ORDER_KEYS.entries()){if(now-ts>CC_SPOT_TERMINAL_ORDER_TTL_MS)CC_FUTURES_TERMINAL_ORDER_KEYS.delete(k)}}
function ccFuturesOrderTerminalSuppressed(o){if(ccFuturesOrderTerminal(o))return true;const ids=ccFuturesOrderIdentity(o);return !!((ids.orderId&&CC_FUTURES_TERMINAL_ORDER_KEYS.has('oid:'+ids.orderId))||(ids.clientOrderId&&CC_FUTURES_TERMINAL_ORDER_KEYS.has('cid:'+ids.clientOrderId)))}
function ccFuturesMergeOpenOrderRows(existing,incoming){const a=ccFuturesOrderIdentity(existing),b=ccFuturesOrderIdentity(incoming),prefer=(!incoming?.ccOptimistic&&(b.orderId||b.clientOrderId))?b:a,out={...existing,...ccFuturesCompactPrivateOrder(incoming)};if(prefer.orderId)out.orderId=prefer.orderId;if(prefer.clientOrderId)out.clientOrderId=prefer.clientOrderId;if(!incoming?.ccOptimistic)delete out.ccOptimistic;return out}
function ccMergeFuturesOpenOrders(prev,next){const out=[],seen=new Set(),nextList=Array.isArray(next)?next:[],nextShapes=new Set(nextList.map(ccFuturesOrderShapeKey).filter(Boolean));const push=o=>{if(!ccFuturesOpenOrderLooksValid(o)||ccFuturesOrderTerminalSuppressed(o))return;const match=out.findIndex(x=>ccFuturesOrderSame(x,o));if(match>=0){out[match]=ccFuturesMergeOpenOrderRows(out[match],o);return}const k=ccFuturesOpenOrderDedupeKey(o);if(!k||seen.has(k))return;seen.add(k);out.push(o)};nextList.forEach(push);(Array.isArray(prev)?prev:[]).forEach(o=>{if(ccFuturesOrderTerminalSuppressed(o))return;const matched=nextList.find(n=>ccFuturesOrderSame(o,n));if(matched){push(ccFuturesMergeOpenOrderRows(o,matched));return}const ids=ccFuturesOrderIdentity(o);if(!ids.orderId&&!ids.clientOrderId)return;const shape=ccFuturesOrderShapeKey(o);if(shape&&nextShapes.has(shape))return;push(o)});return out}
function ccApplyFuturesOpenOrdersResponse(res,seq,startedAt=Date.now()){if(startedAt<CC_FUTURES_ORDER_REFRESH.lastAppliedAt)return false;CC_FUTURES_ORDER_REFRESH.lastAppliedAt=startedAt;ccFuturesOpenOrdersLoaded=true;const parsedList=ccFuturesOpenOrdersFrom(res).filter(o=>!ccFuturesOrderTerminalSuppressed(o));const prev=ccLastFuturesOpenOrders;const replace=Date.now()>=CC_FUTURES_ORDER_REFRESH.protectUntil;const finalList=replace?ccMergeFuturesOpenOrders([],parsedList):ccMergeFuturesOpenOrders(prev,parsedList);if(replace)CC_FUTURES_ORDER_REFRESH.protectUntil=0;if(ccFuturesOrdersSig(prev)===ccFuturesOrdersSig(finalList)){ccRenderFuturesBottomPanels();return true}const removed=(Array.isArray(prev)?prev:[]).filter(o=>!finalList.some(n=>ccFuturesOrderSame(o,n)));ccMaybeNotifyOpenOrderFillDelta(prev,finalList,'ccFuturesOrderStatus');ccLastFuturesOpenOrders=finalList;ccFuturesOpenOrdersDiag(replace?'rest-replace':'rest-merge',prev,finalList,{parsed:parsedList.length,seq,removed:removed.length,removedOrders:removed,protectUntil:CC_FUTURES_ORDER_REFRESH.protectUntil});if(removed.length)ccScheduleFuturesTradeHistoryRefreshBurst('rest-open-orders-removed');ccRenderFuturesBottomPanels();drawCcChart('ccFuturesChart');return true}
function ccFuturesOrderKey(o){const id=ccFuturesOrderIdentity(o);return id.orderId||id.clientOrderId||ccFuturesOrderShapeKey(o)||''}
function ccFuturesPlaceResponseOrderIds(res){const primitive=v=>{const s=ccOrderIdString(v);return /^\d{10,}$/.test(s)?{orderId:s,clientOrderId:''}:null};const direct=primitive(res&&res.data!==undefined?res.data:res);if(direct)return direct;for(const o of ccCollectObjects(res)){const ids=ccFuturesOrderIdentity(o);if(ids.orderId||ids.clientOrderId)return ids;const alt=primitive(ccFirstValue(o,['order','orderNo','orderNumber','order_id','ordId','id']));if(alt)return alt}return ccFuturesOrderIdentity(res||{})}
function ccFuturesDisplaySymbolForTransport(symbol){const raw=String(symbol||'').trim(),rawKey=ccFuturesBookSymbolKey(raw);const selected=String(cc('ccFuturesInst')?.value||'').trim();if(raw&&raw===selected){const label=String(ccSelectedText('ccFuturesInst')||'').trim();if(label)return label}for(const row of Array.isArray(ccLastFuturesInstruments)?ccLastFuturesInstruments:[]){const rowSymbol=String(ccPick(row,['symbol','ticker_id'])||'').trim(),label=String(ccPick(row,['displayName','symbolName','ticker_id','symbol'])||'').trim();if((rowSymbol&&rowSymbol===raw)||(rawKey&&ccFuturesBookSymbolKey(rowSymbol)===rawKey)){return label&&label!=='—'?label:raw}}return raw}
function ccAddOptimisticFuturesOpenOrder(p,res){const ids=ccFuturesPlaceResponseOrderIds(res);if(!ids.orderId&&!ids.clientOrderId)return false;const display=ccFuturesDisplaySymbolForTransport(p.symbol);const order={...p,orderId:ids.orderId||undefined,clientOrderId:ids.clientOrderId||undefined,displaySymbol:display||p.symbol,displayName:display||p.symbol,symbol:p.symbol,tradeSide:p.tradeSide,side:p.tradeSide,tradeType:p.tradeType,orderType:p.tradeType,qty:p.qty,quantity:p.qty,fillQty:'0',filledQty:'0',filledQuantity:'0',avgPrice:'0',averagePrice:'0',price:p.price,px:p.price,status:'OPEN',orderStatus:'OPEN',createTime:Date.now(),updateTime:Date.now(),ccOptimistic:true};const prev=Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders.slice():[];const list=prev.slice();const idx=list.findIndex(o=>ccFuturesOrderSame(o,order));if(idx>=0)list[idx]=ccFuturesMergeOpenOrderRows(list[idx],order);else list.unshift(order);ccLastFuturesOpenOrders=list.filter(o=>!ccFuturesOrderTerminal(o));ccFuturesOpenOrdersDiag('optimistic-add',prev,ccLastFuturesOpenOrders,{order,orderId:ids.orderId,clientOrderId:ids.clientOrderId,symbol:display||p.symbol,side:p.tradeSide,action:'optimistic-add'});ccRenderFuturesBottomPanels();drawCcChart('ccFuturesChart');if(typeof ccRenderSpreadsPositionsOrdersTable==='function')ccRenderSpreadsPositionsOrdersTable();if(typeof ccRenderSpreadsSideOrdersTable==='function')ccRenderSpreadsSideOrdersTable();return true}
function ccAddOptimisticSpotOpenOrder(p,res){const ids=ccFuturesPlaceResponseOrderIds(res);if(!ids.orderId&&!ids.clientOrderId){ccOrderDiagnosticPush('place',{symbol:p&&p.symbol,side:p&&p.tradeSide,action:'place-success-no-id',message:'place response had no order id'});return false;}const canonicalSymbol=ccSpotCanonicalSymbol(p.symbol);const order=ccNormalizeSpotOrderSymbol({...p,orderId:ids.orderId||undefined,clientOrderId:ids.clientOrderId||undefined,displaySymbol:canonicalSymbol||p.symbol,symbol:canonicalSymbol||p.symbol,instId:canonicalSymbol||p.symbol,side:p.tradeSide,tradeSide:p.tradeSide,tradeType:p.tradeType,orderType:p.tradeType,qty:p.qty,quantity:p.qty,amount:p.qty,fillQty:'0',filledQty:'0',filledQuantity:'0',avgPrice:'0',averagePrice:'0',price:p.price,px:p.price,status:'OPEN',orderStatus:'OPEN',createTime:Date.now(),updateTime:Date.now(),ccOptimistic:true});const prev=Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders.slice():[];const list=prev.slice();const idx=list.findIndex(o=>ccSpotOrderSame(o,order));if(idx>=0)list[idx]=ccNormalizeSpotOrderSymbol({...list[idx],...order});else list.unshift(order);ccLastSpotOpenOrders=ccMergeSpotOpenOrders([],list);ccOrderDiagnosticPush('place',{order,orderId:ids.orderId,clientOrderId:ids.clientOrderId,symbol:canonicalSymbol||p.symbol,side:p.tradeSide,status:'OPEN',action:'optimistic-add',message:'local optimistic spot insert'});ccMaybeSpotTradeRefreshFromOpenOrders(ccSpotOpenOrdersSig(ccLastSpotOpenOrders));ccRenderSpotBottomPanels();drawCcChart('ccSpotChart');if(typeof ccRenderSpreadsPositionsOrdersTable==='function')ccRenderSpreadsPositionsOrdersTable();if(typeof ccRenderSpreadsSideOrdersTable==='function')ccRenderSpreadsSideOrdersTable();return true}
function ccReconcileSpotOpenOrdersSoon(reason='spot-place'){CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt=0;setTimeout(()=>{if(document.hidden)return;ccMaybeLoadSpotOpenOrders(true,reason).catch(()=>{});if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(120)},0)}
function ccFuturesOpenOrderKeySet(){return new Set((Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[]).map(ccFuturesOrderKey).filter(Boolean))}
function ccFuturesOrderSymbolKey(o){return ccFuturesBookSymbolKey(ccFirstValue(o,['displaySymbol','displayName','symbol','instId','instrument','ticker_id','baseToken','base_currency','s']))}
function ccFuturesSubmittedOrderMatches(o,p){if(!o||!p)return false;if(ccFuturesOrderSymbolKey(o)!==ccFuturesBookSymbolKey(p.symbol))return false;const side=String(ccFirstValue(o,['tradeSide','side','sd'])??'').trim().toUpperCase();const wantSide=String(p.tradeSide??'').trim().toUpperCase();if(side&&wantSide&&side!==wantSide&&!(side==='BUY'&&wantSide==='1')&&!(side==='SELL'&&wantSide==='2'))return false;const type=String(ccFirstValue(o,['tradeType','orderType','type','ot'])??'').trim().toUpperCase();const wantType=String(p.tradeType??'').trim().toUpperCase();if(type&&wantType&&type!==wantType&&!(type==='LIMIT'&&wantType==='1')&&!(type==='MARKET'&&wantType==='2'))return false;const price=Number(ccFirstValue(o,['price','px','orderPrice','p'])),wantPrice=Number(p.price);if(p.price&&Number.isFinite(price)&&Number.isFinite(wantPrice)&&Math.abs(price-wantPrice)>Math.max(1e-8,Math.abs(wantPrice)*1e-8))return false;const qty=Number(ccFirstValue(o,['qty','quantity','amount','size','orderQty','q','sz'])),wantQty=Number(p.qty);if(Number.isFinite(qty)&&Number.isFinite(wantQty)&&Math.abs(qty-wantQty)>Math.max(1e-8,Math.abs(wantQty)*1e-6))return false;return true}
function ccFuturesPlacedOrderFromState(p,beforeKeys,res){const responseIds=new Set(ccCollectObjects(res).map(ccFuturesOrderKey).filter(Boolean));for(const o of Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[]){const key=ccFuturesOrderKey(o);if(responseIds.size&&key&&responseIds.has(key))return o;if(key&&beforeKeys&&beforeKeys.has(key))continue;if(ccFuturesSubmittedOrderMatches(o,p))return o}return null}
function ccSleep(ms){return new Promise(resolve=>setTimeout(resolve,ms))}
async function ccWaitForConfirmedFuturesOpenOrder(p,beforeKeys,res){const delays=[0,120,260,520,900,1400,2200,3500,5000];for(const delay of delays){if(delay>0)await ccSleep(delay);let found=ccFuturesPlacedOrderFromState(p,beforeKeys,res);if(found)return found;await loadCcFuturesOpenOrders();found=ccFuturesPlacedOrderFromState(p,beforeKeys,res);if(found)return found}return null}
function ccMaybeSpotTradeRefreshFromOpenOrders(nextSig){const prev=CC_SPOT_TRADE_REFRESH.openSig;CC_SPOT_TRADE_REFRESH.openSig=nextSig;if(prev&&prev!==nextSig){ccScheduleSpotTradeRefreshBurst('open-orders-changed');setTimeout(()=>refreshCcAccountSnapshot(),0)}}
function ccNormalizeSpotPrivateOrder(d){const tick=d&&d.tick?d.tick:d;return ccNormalizeSpotOrderSymbol({...tick,orderId:ccFirstValue(tick,['orderId','ordId','order_id','id','oid','orderNo']),clientOrderId:ccFirstValue(tick,['clientOrderId','clientOid','clOrdId','coid']),displaySymbol:ccFirstValue(tick,['displaySymbol','symbol','instId','instrument','pair']),symbol:ccFirstValue(tick,['symbol','instId','displaySymbol','instrument','pair']),tradeSide:ccFirstValue(tick,['tradeSide','side','orderSide','direction','sd','si']),price:ccFirstValue(tick,['price','px','orderPrice','limitPrice']),qty:ccFirstValue(tick,['qty','quantity','amount','orderQty','origQty','volume','q','sz']),filledQty:ccFirstValue(tick,['filledQty','fillQty','filled','dealQty','cumQty','fq']),remainQty:ccFirstValue(tick,['remainQty','remainingQty','leavesQty','leftQty','unfilledQty']),avgPrice:ccFirstValue(tick,['avgPrice','averagePrice','matchPrice','avgPx']),time:ccFirstValue(tick,['createTime','createdTime','time','ts','updateTime','matchTime']),ts:ccFirstValue(tick,['ts','matchTime','createTime','updateTime','time']),ccWsSpotOrder:true})}
function ccSpotPrivateOrderKey(o){return String(ccFirstValue(o,['orderId','ordId','order_id','id','oid','orderNo','clientOrderId','clientOid','clOrdId','coid'])||'')}
function ccSpotPrivateOrderStatusKey(o){return String(ccFirstValue(o,['status','state','orderStatus','orderState','order_state','os'])??'').trim().toUpperCase().replace(/[\s_-]+/g,'')}
function ccSpotPrivateOrderIsTerminal(o){const key=ccSpotPrivateOrderStatusKey(o);const label=String(ccOrderStatusLabel(o)||'').trim().toUpperCase().replace(/[\s_-]+/g,'');if(['1','3','6','10','FILLED','FULLFILLED','FULLYFILLED','CANCELED','CANCELLED','CANCELBYEXERCISE','INVALID','REJECTED','EXPIRED'].includes(key)||['FILLED','FULLFILLED','FULLYFILLED','CANCELED','CANCELLED','CANCELBYEXERCISE','INVALID','REJECTED','EXPIRED'].includes(label))return true;const total=Number(ccSpotOrderAmountRaw(o));const filled=Number(ccFirstValue(o,['fillQty','filledQty','filledQuantity','filled','dealQty','cumQty','fq']));if(Number.isFinite(total)&&total>0&&Number.isFinite(filled)&&filled>=total-Math.max(1e-12,total*1e-10))return true;const left=Number(ccFirstValue(o,['remainQty','remainingQty','leavesQty','leftQty','unfilledQty']));return Number.isFinite(left)&&left<=0&&Number.isFinite(filled)&&filled>0&&(!Number.isFinite(total)||total<=0)}
function ccSpotPrivateOrderLooksOpen(o){const v=ccSpotPrivateOrderStatusKey(o);return !ccSpotPrivateOrderIsTerminal(o)&&!['4','5','6','PRECANCEL','CANCELING','CANCELLING','INVALID'].includes(v)}
function ccSpotTerminalOrderKeys(o){const ids=ccSpotOrderIdentity(o),keys=[];if(ids.orderId)keys.push('oid:'+ids.orderId);if(ids.clientOrderId)keys.push('cid:'+ids.clientOrderId);const shape=ccSpotOrderShapeKey(o);if(shape)keys.push(shape);return keys}
function ccRememberSpotTerminalOrder(o){const now=Date.now();ccSpotTerminalOrderKeys(o).forEach(k=>CC_SPOT_TERMINAL_ORDER_KEYS.set(k,now));for(const [k,ts] of CC_SPOT_TERMINAL_ORDER_KEYS.entries()){if(now-ts>CC_SPOT_TERMINAL_ORDER_TTL_MS)CC_SPOT_TERMINAL_ORDER_KEYS.delete(k)}}
function ccRememberSpotCanceledOrder(o){const ids=ccSpotOrderIdentity(o),now=Date.now();let usedId=false;if(ids.orderId){CC_SPOT_TERMINAL_ORDER_KEYS.set('oid:'+ids.orderId,now);usedId=true}if(ids.clientOrderId){CC_SPOT_TERMINAL_ORDER_KEYS.set('cid:'+ids.clientOrderId,now);usedId=true}if(!usedId)ccRememberSpotTerminalOrder(o)}
function ccSpotOrderTerminalSuppressed(o){return ccSpotPrivateOrderIsTerminal(o)||ccSpotTerminalOrderKeys(o).some(k=>CC_SPOT_TERMINAL_ORDER_KEYS.has(k))}
function ccMergeSpotOpenOrders(prev,next){const out=[];const nextList=(Array.isArray(next)?next:[]).map(ccNormalizeSpotOrderSymbol);const forceReplace=Date.now()<ccSpotForceReplaceOpenOrdersUntil;const sameOpenOrder=(a,b)=>ccSpotOrderSame(a,b)||ccSpotOrderCanShapeDedupe(a,b);const push=raw=>{const o=ccNormalizeSpotOrderSymbol(raw);if(!ccSpotOpenOrderLooksValid(o)||ccSpotOrderTerminalSuppressed(o))return;const match=out.findIndex(x=>sameOpenOrder(x,o));if(match>=0){out[match]=ccSpotMergeOpenOrderRows(out[match],o);return}out.push(o)};nextList.forEach(push);if(forceReplace)return out;(Array.isArray(prev)?prev:[]).map(ccNormalizeSpotOrderSymbol).forEach(o=>{if(ccSpotOrderTerminalSuppressed(o))return;const matched=nextList.find(n=>sameOpenOrder(o,n));if(matched){push(ccSpotMergeOpenOrderRows(o,matched));return}const ids=ccSpotOrderIdentity(o);if(!ids.orderId&&!ids.clientOrderId)return;push(o)});return out}
function ccForceSpotTerminalReconcile(reason='spot-terminal'){ccSpotForceReplaceOpenOrdersUntil=Date.now()+15000;CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt=0;setTimeout(()=>{if(document.hidden)return;Promise.allSettled([loadCcSpotOpenOrders(reason),loadCcSpotTradeHistory(),loadCcSpotOrderHistory()]);if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(120)},0)}
function ccApplySpotCancelSuccess(order){const prev=Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders.slice():[];ccRememberSpotCanceledOrder(order);ccForceSpotTerminalReconcile('cancel-rest-success');ccLastSpotOpenOrders=prev.filter(o=>!ccSpotOrderSame(o,order)&&!ccSpotOrderTerminalSuppressed(o));ccOrderDiagnosticPush('reconcile',{order,action:'spot-local-cancel-remove',message:'local open order removed after cancel accepted'});ccMaybeSpotTradeRefreshFromOpenOrders(ccSpotOpenOrdersSig(ccLastSpotOpenOrders));ccRenderSpotBottomPanels();drawCcChart('ccSpotChart');return prev.length!==ccLastSpotOpenOrders.length}
function ccSpotRestDiagnosticReason(reason){return /initial|manual|refresh|order-submit|cancel|terminal|private-ws|stale|force/i.test(String(reason||''))}
function ccApplySpotOpenOrdersResponse(res,reason='rest'){const prev=Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders.slice():[];const prevSig=ccSpotOpenOrdersSig(prev);const parsed=ccSpotOpenOrdersFrom(res).filter(o=>!ccSpotOrderTerminalSuppressed(o));const now=Date.now();const forceReplace=now<ccSpotForceReplaceOpenOrdersUntil;const replace=forceReplace||now>=ccSpotProtectOpenOrdersUntil;const next=ccMergeSpotOpenOrders(replace?[]:prev,parsed);const nextSig=ccSpotOpenOrdersSig(next);const removed=prev.filter(o=>!next.some(n=>ccSpotOrderSame(o,n)));if(forceReplace)ccSpotForceReplaceOpenOrdersUntil=0;if(replace)ccSpotProtectOpenOrdersUntil=0;CC_SPOT_TRADE_REFRESH.openOrdersLoaded=true;if(prevSig!==nextSig||removed.length)ccOrderDiagnosticPush('spot-rest/open-orders',{action:reason+' response',message:'parsed '+parsed.length+', open orders '+prev.length+' -> '+next.length+(removed.length?'; removed '+removed.length:'')});removed.forEach(o=>ccOrderDiagnosticPush('reconcile',{order:o,action:'spot-rest-remove',message:'REST open orders removed local order'}));ccMaybeNotifyOpenOrderFillDelta(prev,next,'ccSpotOrderStatus');ccLastSpotOpenOrders=next;ccMaybeSpotTradeRefreshFromOpenOrders(ccSpotOpenOrdersSig(ccLastSpotOpenOrders));ccRenderSpotBottomPanels();drawCcChart('ccSpotChart');return ccLastSpotOpenOrders}
function ccApplySpotPrivateOrderEvent(raw){const rows=Array.isArray(raw)?raw:[raw].filter(Boolean);let changed=false,terminal=false;const prev=Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders.slice():[];for(const item of rows){const order=ccNormalizeSpotPrivateOrder(item);const isTerminal=ccSpotPrivateOrderIsTerminal(order);ccOrderDiagnosticPush('spot-ws/order',{order,status:ccSpotPrivateOrderStatusKey(order),action:isTerminal?'terminal-message':'message',message:'private spot order update received'});const list=Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders.slice():[];const idx=list.findIndex(o=>ccSpotOrderSame(o,order));if(isTerminal){ccRememberSpotTerminalOrder(order);terminal=true;if(idx>=0){list.splice(idx,1);ccLastSpotOpenOrders=list;ccOrderDiagnosticPush('reconcile',{order,action:'spot-ws-terminal-remove',message:'private WS terminal update removed local order'});changed=true}continue}if(!ccSpotPrivateOrderLooksOpen(order)||ccSpotOrderTerminalSuppressed(order))continue;if(idx<0){if(!ccSpotOpenOrderLooksValid(order))continue;list.unshift(order);ccLastSpotOpenOrders=ccMergeSpotOpenOrders([],list);changed=true;continue}list[idx]={...list[idx],...order};ccLastSpotOpenOrders=ccMergeSpotOpenOrders([],list);changed=true}if(changed){ccMaybeNotifyOpenOrderFillDelta(prev,ccLastSpotOpenOrders,'ccSpotOrderStatus');ccMaybeSpotTradeRefreshFromOpenOrders(ccSpotOpenOrdersSig(ccLastSpotOpenOrders));ccRenderSpotBottomPanels();drawCcChart('ccSpotChart');if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(120)}if(terminal){ccScheduleSpotTradeRefreshBurst('private-terminal');ccForceSpotTerminalReconcile('private-terminal')}return changed}
function ccNormalizeSpotPrivateTrade(d){const tick=d&&d.tick?d.tick:d;const price=ccFirstValue(tick,['price','matchPrice','fillPrice','filledPrice','px']);const qty=ccFirstValue(tick,['volume','matchQty','qty','quantity','amount','filledQty','fillQty']);const ts=ccFirstValue(tick,['ts','tradeTime','matchTime','fillTime','createTime','time']);return {...tick,displaySymbol:ccFirstValue(tick,['displaySymbol','symbol','instId']),symbol:ccFirstValue(tick,['symbol','instId','displaySymbol']),tradeSide:ccFirstValue(tick,['tradeSide','side']),price:price,fillPrice:price,qty:qty,volume:qty,matchQty:qty,ts:ts,time:ts,ccWsSpotTrade:true}}
function ccSpotPrivateTradeKey(t){return [ccFirstValue(t,['tradeId','dealId','fillId','id']),ccFirstValue(t,['orderId','clientOrderId']),ccFirstValue(t,['ts','time'])].filter(Boolean).join(':')}
function ccApplySpotPrivateTradeEvent(raw){const rows=Array.isArray(raw)?raw:[raw].filter(Boolean);let changed=false;const prev=Array.isArray(ccLastSpotTradeHistory)?ccLastSpotTradeHistory.slice():[];const list=prev.slice();const seen=new Set(list.map(ccSpotPrivateTradeKey));for(const item of rows){const trade=ccNormalizeSpotPrivateTrade(item);ccOrderDiagnosticPush('spot-ws/trade',{order:trade,action:'trade-message',message:'private spot trade/fill update received'});const key=ccSpotPrivateTradeKey(trade);if(!key||seen.has(key))continue;seen.add(key);list.unshift(trade);changed=true}if(!changed)return false;ccLastSpotTradeHistory=list.sort((a,b)=>ccSpotTradeTs(b)-ccSpotTradeTs(a)).slice(0,100);ccMaybeNotifySpotFills(prev,ccLastSpotTradeHistory);ccRenderSpotBottomPanels();if(CC_BOOK_STATE.raw.spot&&CC_BOOK_STATE.transport.spot===ccBookTransportSymbol('spot'))ccRenderBook('spot',CC_BOOK_STATE.raw.spot);drawCcChart('ccSpotChart');if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(120);return true}
const CC_SPOT_PRIVATE_WS={socket:null,url:'',account:'',heartbeat:null,reconnectTimer:null,retry:null,state:'idle',socketId:0,activeSocketId:0,connectPromise:null,reconnectDueAt:0,backoffMs:CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS,stableTimer:null,lastOpenAt:0,lastCloseAt:0,lastEventAt:0,lastHeartbeatAt:0,lastReconnectAt:0,lastErrorAt:0,lastOpenReconcileAt:0,lastStatusDiagnosticAt:0,lastSkippedEnsureAt:0,lastCloseCode:'',lastCloseReason:'',openReconcileSocket:null,lastChannel:'',eventCount:0,reconnectCount:0};
function ccSpotPrivateSocketOpen(){const ws=CC_SPOT_PRIVATE_WS.socket;return !!(ws&&ws.readyState===WebSocket.OPEN)}
function ccSpotPrivateClearHeartbeat(){if(CC_SPOT_PRIVATE_WS.heartbeat){clearInterval(CC_SPOT_PRIVATE_WS.heartbeat);CC_SPOT_PRIVATE_WS.heartbeat=null}}
function ccSpotPrivateClearStableTimer(){if(CC_SPOT_PRIVATE_WS.stableTimer){clearTimeout(CC_SPOT_PRIVATE_WS.stableTimer);CC_SPOT_PRIVATE_WS.stableTimer=null}}
function ccSpotPrivateStatusDiagnostic(action,extra={}){const now=Date.now();if(CC_SPOT_PRIVATE_WS.lastStatusDiagnosticAt&&now-CC_SPOT_PRIVATE_WS.lastStatusDiagnosticAt<CC_SPOT_PRIVATE_WS_STATUS_DIAG_MS)return;CC_SPOT_PRIVATE_WS.lastStatusDiagnosticAt=now;ccOrderDiagnosticPush('spot-ws/status',{action,message:extra.message||'private spot WS status',socketId:CC_SPOT_PRIVATE_WS.activeSocketId||'',state:CC_SPOT_PRIVATE_WS.state||'',backoffMs:CC_SPOT_PRIVATE_WS.backoffMs||0,reconnectDueInMs:Math.max(0,(CC_SPOT_PRIVATE_WS.reconnectDueAt||0)-now),reconnectCount:CC_SPOT_PRIVATE_WS.reconnectCount||0,lastCloseCode:CC_SPOT_PRIVATE_WS.lastCloseCode||'',lastCloseReason:CC_SPOT_PRIVATE_WS.lastCloseReason||'',...extra})}
function ccSpotPrivateEventDiagnostic(action,extra={}){const now=Date.now();ccOrderDiagnosticPush('spot-ws/status',{action,message:extra.message||'private spot WS event',socketId:CC_SPOT_PRIVATE_WS.activeSocketId||'',state:CC_SPOT_PRIVATE_WS.state||'',backoffMs:CC_SPOT_PRIVATE_WS.backoffMs||0,reconnectDueInMs:Math.max(0,(CC_SPOT_PRIVATE_WS.reconnectDueAt||0)-now),reconnectCount:CC_SPOT_PRIVATE_WS.reconnectCount||0,lastCloseCode:CC_SPOT_PRIVATE_WS.lastCloseCode||'',lastCloseReason:CC_SPOT_PRIVATE_WS.lastCloseReason||'',...extra})}
function ccSpotPrivateSkipEnsure(reason){const now=Date.now();if(CC_SPOT_PRIVATE_WS.lastSkippedEnsureAt&&now-CC_SPOT_PRIVATE_WS.lastSkippedEnsureAt<CC_SPOT_PRIVATE_WS_STATUS_DIAG_MS)return;CC_SPOT_PRIVATE_WS.lastSkippedEnsureAt=now;ccSpotPrivateStatusDiagnostic('private-ws-ensure-skipped',{reason,message:'private spot WS ensure skipped: '+reason})}
function ccSpotPrivateResetBackoff(reason='stable'){CC_SPOT_PRIVATE_WS.backoffMs=CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS;CC_SPOT_PRIVATE_WS.reconnectDueAt=0;ccSpotPrivateStatusDiagnostic('private-ws-backoff-reset',{reason,message:'private spot WS backoff reset after stable '+reason})}
function ccSpotPrivateMaybeResetBackoff(reason='message'){if(CC_SPOT_PRIVATE_WS.state==='open'&&CC_SPOT_PRIVATE_WS.lastOpenAt&&Date.now()-CC_SPOT_PRIVATE_WS.lastOpenAt>=CC_SPOT_PRIVATE_WS_STABLE_RESET_MS&&CC_SPOT_PRIVATE_WS.backoffMs!==CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS)ccSpotPrivateResetBackoff(reason)}
function ccMaybeReconcileSpotOpenOrdersOnPrivateWsOpen(ws){const now=Date.now();if(ws&&CC_SPOT_PRIVATE_WS.socket&&CC_SPOT_PRIVATE_WS.socket!==ws)return false;if(ws&&CC_SPOT_PRIVATE_WS.openReconcileSocket===ws)return false;if(CC_SPOT_PRIVATE_WS.lastOpenReconcileAt&&now-CC_SPOT_PRIVATE_WS.lastOpenReconcileAt<CC_SPOT_PRIVATE_WS_OPEN_RECONCILE_FLOOR_MS){ccSpotPrivateStatusDiagnostic('private-ws-open-reconcile-skipped',{message:'private WS open reconcile skipped; previous reconcile '+(now-CC_SPOT_PRIVATE_WS.lastOpenReconcileAt)+'ms ago'});return false}CC_SPOT_PRIVATE_WS.lastOpenReconcileAt=now;CC_SPOT_PRIVATE_WS.openReconcileSocket=ws||null;ccReconcileSpotOpenOrdersSoon('private-ws-open');return true}
function ccSpotPrivateBackoffDelay(){const base=Math.min(CC_SPOT_PRIVATE_WS.backoffMs||CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS,CC_SPOT_PRIVATE_WS_BACKOFF_CAP_MS),jitter=base*CC_SPOT_PRIVATE_WS_BACKOFF_JITTER*(Math.random()*2-1);return Math.max(500,Math.round(base+jitter))}
function ccSpotPrivateScheduleReconnect(reason='close'){if(document.hidden)return;if(CC_SPOT_PRIVATE_WS.reconnectTimer){ccSpotPrivateStatusDiagnostic('private-ws-reconnect-skip',{reason,message:'private spot WS reconnect already scheduled'});return}const delay=ccSpotPrivateBackoffDelay();CC_SPOT_PRIVATE_WS.state='backoff';CC_SPOT_PRIVATE_WS.lastReconnectAt=Date.now();CC_SPOT_PRIVATE_WS.reconnectDueAt=CC_SPOT_PRIVATE_WS.lastReconnectAt+delay;CC_SPOT_PRIVATE_WS.reconnectCount=(CC_SPOT_PRIVATE_WS.reconnectCount||0)+1;ccSpotPrivateEventDiagnostic('private-ws-reconnect-scheduled',{reason,delayMs:delay,backoffMs:CC_SPOT_PRIVATE_WS.backoffMs||0,nextBackoffMs:Math.min((CC_SPOT_PRIVATE_WS.backoffMs||CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS)*2,CC_SPOT_PRIVATE_WS_BACKOFF_CAP_MS),readyState:CC_SPOT_PRIVATE_WS.socket?CC_SPOT_PRIVATE_WS.socket.readyState:'',message:'private spot WS reconnect scheduled'});CC_SPOT_PRIVATE_WS.backoffMs=Math.min((CC_SPOT_PRIVATE_WS.backoffMs||CC_SPOT_PRIVATE_WS_BACKOFF_BASE_MS)*2,CC_SPOT_PRIVATE_WS_BACKOFF_CAP_MS);CC_SPOT_PRIVATE_WS.reconnectTimer=setTimeout(()=>{CC_SPOT_PRIVATE_WS.reconnectTimer=null;CC_SPOT_PRIVATE_WS.retry=null;CC_SPOT_PRIVATE_WS.reconnectDueAt=0;ccEnsureSpotPrivateSocket({fromReconnect:true}).catch(()=>{})},delay);CC_SPOT_PRIVATE_WS.retry=CC_SPOT_PRIVATE_WS.reconnectTimer}
function ccSpotPrivateChannelText(msg){const arg=msg&&msg.arg&&typeof msg.arg==='object'?msg.arg:{};return [msg.ch,msg.channel,msg.topic,msg.dataType,msg.type,msg.event,msg.action,msg.subscription,arg.channel,arg.dataType,arg.type,arg.subscription].map(v=>String(v||'').toLowerCase()).filter(Boolean).join('|')}
function ccSpotPrivateChannelKind(msg,payload){const ch=ccSpotPrivateChannelText(msg);if(/(^|[|._:-])order(s)?($|[|._:-])/.test(ch)||/order/.test(ch))return 'order';if(/(^|[|._:-])trade(s)?($|[|._:-])/.test(ch)||/deal|fill/.test(ch))return 'trade';const probe=Array.isArray(payload)?payload[0]:payload;if(probe&&typeof probe==='object'){if(ccFirstValue(probe,['tradeId','dealId','fillId','matchQty','fillQty','fillPrice'])!==undefined)return 'trade';if(ccFirstValue(probe,['orderId','ordId','order_id','id','oid','orderNo','clientOrderId','clientOid','clOrdId','coid','orderStatus','orderState','order_state','status','state','os'])!==undefined)return 'order'}return ''}
function ccSpotPrivatePayload(msg){const data=msg&&msg.data!==undefined?msg.data:(msg&&msg.tick!==undefined?msg.tick:(msg&&msg.d!==undefined?msg.d:msg));if(data&&typeof data==='object'&&!Array.isArray(data)&&data.list!==undefined)return data.list;if(data&&typeof data==='object'&&!Array.isArray(data)&&data.rows!==undefined)return data.rows;if(data&&typeof data==='object'&&!Array.isArray(data)&&data.items!==undefined)return data.items;return data}
function ccSpotPrivateHandleMessage(msg){if(!msg||typeof msg!=='object')return false;if(msg.rc===1){CC_SPOT_PRIVATE_WS.lastEventAt=Date.now();ccSpotPrivateMaybeResetBackoff('event');return true}const payload=ccSpotPrivatePayload(msg),kind=ccSpotPrivateChannelKind(msg,payload);if(kind==='order'){CC_SPOT_PRIVATE_WS.lastChannel='order';CC_SPOT_PRIVATE_WS.lastEventAt=Date.now();CC_SPOT_PRIVATE_WS.eventCount=(CC_SPOT_PRIVATE_WS.eventCount||0)+1;ccSpotPrivateMaybeResetBackoff('message');ccApplySpotPrivateOrderEvent(payload);return true}if(kind==='trade'){CC_SPOT_PRIVATE_WS.lastChannel='trade';CC_SPOT_PRIVATE_WS.lastEventAt=Date.now();CC_SPOT_PRIVATE_WS.eventCount=(CC_SPOT_PRIVATE_WS.eventCount||0)+1;ccSpotPrivateMaybeResetBackoff('message');ccApplySpotPrivateTradeEvent(payload);return true}return false}
async function ccEnsureSpotPrivateSocket(opts={}){const sel=cc('ccAccountSelect'),account=String(sel&&sel.value||'');const current=CC_SPOT_PRIVATE_WS.socket;if(CC_SPOT_PRIVATE_WS.connectPromise){ccSpotPrivateSkipEnsure('connecting');return CC_SPOT_PRIVATE_WS.connectPromise}if(CC_SPOT_PRIVATE_WS.reconnectTimer&&!opts.fromReconnect){ccSpotPrivateSkipEnsure('backoff');return}if(CC_SPOT_PRIVATE_WS.state==='backoff'&&!opts.fromReconnect){ccSpotPrivateSkipEnsure('backoff');return}if(current&&CC_SPOT_PRIVATE_WS.account===account&&[WebSocket.OPEN,WebSocket.CONNECTING,WebSocket.CLOSING].includes(current.readyState)){if(current.readyState===WebSocket.CLOSING)ccSpotPrivateSkipEnsure('closing');return}if(current&&[WebSocket.OPEN,WebSocket.CONNECTING,WebSocket.CLOSING].includes(current.readyState)){if(current.readyState!==WebSocket.CLOSING){CC_SPOT_PRIVATE_WS.state='closing';try{current.close()}catch{}}else ccSpotPrivateSkipEnsure('closing');return}ccSpotPrivateClearHeartbeat();ccSpotPrivateClearStableTimer();CC_SPOT_PRIVATE_WS.state='connecting';const qs=account?'?accountId='+encodeURIComponent(account):'';CC_SPOT_PRIVATE_WS.connectPromise=(async()=>{try{const auth=await ccApi('/api/admin/coincall/spot/private-ws-auth'+qs),url=String(auth&&auth.url||'');if(!url){CC_SPOT_PRIVATE_WS.state='idle';return}const ws=new WebSocket(url),socketId=(CC_SPOT_PRIVATE_WS.socketId||0)+1;CC_SPOT_PRIVATE_WS.socketId=socketId;CC_SPOT_PRIVATE_WS.activeSocketId=socketId;CC_SPOT_PRIVATE_WS.socket=ws;CC_SPOT_PRIVATE_WS.url=url;CC_SPOT_PRIVATE_WS.account=account;ws.onopen=()=>{if(CC_SPOT_PRIVATE_WS.socket!==ws)return;CC_SPOT_PRIVATE_WS.state='open';CC_SPOT_PRIVATE_WS.lastOpenAt=Date.now();CC_SPOT_PRIVATE_WS.reconnectDueAt=0;ccSpotPrivateEventDiagnostic('private-ws-open',{socketId,readyState:ws.readyState,message:'private spot WS opened'});ccBookSendJson(ws,{action:'subscribe',dataType:'order'});ccBookSendJson(ws,{action:'subscribe',dataType:'trade'});ccSpotPrivateClearHeartbeat();ccMaybeReconcileSpotOpenOrdersOnPrivateWsOpen(ws);ccSpotPrivateClearStableTimer();CC_SPOT_PRIVATE_WS.stableTimer=setTimeout(()=>{if(CC_SPOT_PRIVATE_WS.socket===ws&&ws.readyState===WebSocket.OPEN)ccSpotPrivateResetBackoff('open')},CC_SPOT_PRIVATE_WS_STABLE_RESET_MS);CC_SPOT_PRIVATE_WS.heartbeat=setInterval(()=>{CC_SPOT_PRIVATE_WS.lastHeartbeatAt=Date.now();ccBookSendJson(ws,{c:11});ccBookSendJson(ws,{action:'heartbeat'});ccSpotPrivateMaybeResetBackoff('heartbeat')},12000)};ws.onmessage=ev=>{if(CC_SPOT_PRIVATE_WS.socket!==ws)return;let msg;try{msg=JSON.parse(ev.data)}catch{return}if(msg&&msg.ping!==undefined){ccBookSendJson(ws,{pong:msg.ping});return}ccSpotPrivateHandleMessage(msg)};ws.onclose=ev=>{const isActive=CC_SPOT_PRIVATE_WS.socket===ws;CC_SPOT_PRIVATE_WS.lastCloseAt=Date.now();CC_SPOT_PRIVATE_WS.lastCloseCode=ev&&ev.code!==undefined?String(ev.code):'';CC_SPOT_PRIVATE_WS.lastCloseReason=ev&&ev.reason!==undefined?String(ev.reason):'';if(isActive){CC_SPOT_PRIVATE_WS.socket=null;CC_SPOT_PRIVATE_WS.state='closing'}ccSpotPrivateClearHeartbeat();ccSpotPrivateClearStableTimer();ccSpotPrivateEventDiagnostic('private-ws-close',{socketId,code:CC_SPOT_PRIVATE_WS.lastCloseCode,reasonText:CC_SPOT_PRIVATE_WS.lastCloseReason,wasClean:!!(ev&&ev.wasClean),backoffMs:CC_SPOT_PRIVATE_WS.backoffMs||0,readyState:ws.readyState,message:'private spot WS closed; reconnect scheduled'});if(isActive)ccSpotPrivateScheduleReconnect('close')};ws.onerror=()=>{if(CC_SPOT_PRIVATE_WS.socket!==ws)return;CC_SPOT_PRIVATE_WS.lastErrorAt=Date.now();ccSpotPrivateEventDiagnostic('private-ws-error',{socketId,readyState:ws.readyState,message:'private spot WS error'})}}catch(e){CC_SPOT_PRIVATE_WS.lastErrorAt=Date.now();CC_SPOT_PRIVATE_WS.state='idle';ccSpotPrivateEventDiagnostic('private-ws-error',{message:'private spot WS auth/connect failed: '+(e&&e.message?e.message:e)});ccSpotPrivateScheduleReconnect('connect-error');throw e}finally{CC_SPOT_PRIVATE_WS.connectPromise=null}})();return CC_SPOT_PRIVATE_WS.connectPromise}
function ccMaybeAutoSpotTradeRefresh(){const now=Date.now();if(ccSpotTradingActive()&&now-CC_SPOT_TRADE_REFRESH.lastAuto>10000){CC_SPOT_TRADE_REFRESH.lastAuto=now;loadCcSpotTradeHistory()}}
const CC_TRADE_SETTINGS_KEY='ccTradeSettings.v1';const CC_TRADE_SETTINGS={orderConfirmation:true,filledNotification:true,fillSound:'triple'};
function ccLoadTradeSettings(){try{const saved=JSON.parse(localStorage.getItem(CC_TRADE_SETTINGS_KEY)||'{}');if(typeof saved.orderConfirmation==='boolean')CC_TRADE_SETTINGS.orderConfirmation=saved.orderConfirmation;if(typeof saved.filledNotification==='boolean')CC_TRADE_SETTINGS.filledNotification=saved.filledNotification;if(typeof saved.fillSound==='string')CC_TRADE_SETTINGS.fillSound=saved.fillSound}catch(e){}}
function ccSaveTradeSettings(){try{localStorage.setItem(CC_TRADE_SETTINGS_KEY,JSON.stringify(CC_TRADE_SETTINGS))}catch(e){}}
function ccSyncTradeSettingsUi(){[['ccSettingOrderConfirmation','orderConfirmation'],['ccSettingFilledNotification','filledNotification']].forEach(([id,key])=>{const btn=cc(id),on=!!CC_TRADE_SETTINGS[key];if(btn){btn.setAttribute('aria-pressed',on?'true':'false');btn.querySelector('.okx-trade-settings-toggle')?.classList.toggle('on',on)}});const sound=cc('ccFillSoundSelect');if(sound)sound.value=CC_TRADE_SETTINGS.fillSound||'triple'}
let ccFillAudioCtx=null;function ccFillAudioContext(){try{const Ctx=window.AudioContext||window.webkitAudioContext;if(!Ctx)return null;if(!ccFillAudioCtx)ccFillAudioCtx=new Ctx();if(ccFillAudioCtx.state==='suspended')ccFillAudioCtx.resume().catch(()=>{});return ccFillAudioCtx}catch{return null}}
function ccFillSoundPattern(name,start){if(name==='bell')return [['sine',1046,start,.42,.24],['sine',1568,start+.03,.22,.36],['triangle',2093,start+.11,.16,.28]];if(name==='chirp')return [['sawtooth',900,start,.34,.10],['sawtooth',1320,start+.09,.34,.10],['sawtooth',1760,start+.18,.34,.12]];if(name==='alarm')return [['square',740,start,.46,.18],['square',740,start+.28,.46,.18],['square',740,start+.56,.46,.22]];if(name==='clicks')return [['square',1800,start,.40,.045],['square',1800,start+.09,.40,.045],['square',1800,start+.18,.40,.045],['square',1200,start+.31,.36,.07]];return [['square',784,start,.42,.16],['square',1046,start+.18,.42,.16],['square',1568,start+.36,.42,.22]]}
function ccPlayFillSound(force=false){if(!force&&!CC_TRADE_SETTINGS.filledNotification)return;const ctx=ccFillAudioContext();if(!ctx)return;const start=ctx.currentTime+.02;ccFillSoundPattern(CC_TRADE_SETTINGS.fillSound||'triple',start).forEach(([type,freq,t,level,dur])=>{const osc=ctx.createOscillator(),gain=ctx.createGain();osc.type=type;osc.frequency.value=freq;gain.gain.setValueAtTime(0.0001,t);gain.gain.exponentialRampToValueAtTime(level,t+.018);gain.gain.exponentialRampToValueAtTime(0.0001,t+dur);osc.connect(gain);gain.connect(ctx.destination);osc.start(t);osc.stop(t+dur+.04)})}
function ccToggleTradeSetting(key){CC_TRADE_SETTINGS[key]=!CC_TRADE_SETTINGS[key];ccSaveTradeSettings();ccSyncTradeSettingsUi();if(key==='filledNotification'&&CC_TRADE_SETTINGS.filledNotification){ccFillAudioContext();if('Notification' in window&&Notification.permission==='default')Notification.requestPermission().catch(()=>{})}}
function ccConfirmOrderAction(message){return !CC_TRADE_SETTINGS.orderConfirmation||confirm(message)}
function ccSpotTradeKey(t){return [ccPick(t,['tradeId','dealId','fillId','id']),ccPick(t,['orderId','ordId','clientOrderId']),ccSpotTradeTs(t),ccSpotTradePrice(t),ccSpotTradeQty(t)].join('|')}
function ccSpotTradeNotifyText(t){const sym=ccPick(t,['displaySymbol','symbol','instId']),side=ccOrderSide(t),qty=ccSpotTradeQty(t),px=ccSpotTradePrice(t);return `${side} ${Number.isFinite(qty)?ccFmt(qty,8):ccFirst(ccPick(t,['qty','quantity','amount','fillQty','filledQty']))} ${sym} @ ${Number.isFinite(px)?ccChartFmt(px):ccFirst(ccPick(t,['price','fillPrice','filledPrice','px']))}`}
function ccMaybeNotifyFills(prev,next,statusId='ccSpotOrderStatus'){if(!CC_TRADE_SETTINGS.filledNotification||!Array.isArray(prev)||!prev.length||!Array.isArray(next)||!next.length)return;const prevKeys=new Set(prev.map(ccSpotTradeKey));const fresh=next.filter(t=>!prevKeys.has(ccSpotTradeKey(t))).slice(0,3);if(!fresh.length)return;const text=fresh.map(ccSpotTradeNotifyText).join(' · ');ccPlayFillSound();ccSetStatus(statusId,'Filled: '+text);if('Notification' in window&&Notification.permission==='granted')new Notification('CoinCall fill', {body:text});}
function ccMaybeNotifySpotFills(prev,next){ccMaybeNotifyFills(prev,next,'ccSpotOrderStatus')}
function ccMaybeNotifyFuturesFills(prev,next){const filtered=Array.isArray(next)?next.filter(t=>!ccFuturesTradeOrderNotifySuppressed(t)):next;ccMaybeNotifyFills(prev,filtered,'ccFuturesOrderStatus')}
function ccOrderFilledQty(o){const n=Number(ccFirstValue(o,['fillQty','filledQty','filledQuantity','filled','dealQty','cumQty','fq']));return Number.isFinite(n)?n:0}
function ccMaybeNotifyOpenOrderFillDelta(prev,next,statusId='ccFuturesOrderStatus'){if(!CC_TRADE_SETTINGS.filledNotification||!Array.isArray(prev)||!prev.length||!Array.isArray(next))return;const prevMap=new Map(prev.map(o=>[ccFuturesOrderKey(o)||ccSpotOrderDisplayId(o)||ccSpotTradeKey(o),ccOrderFilledQty(o)]));let changed=[];for(const o of next){const key=ccFuturesOrderKey(o)||ccSpotOrderDisplayId(o)||ccSpotTradeKey(o);if(!key||!prevMap.has(key))continue;const before=Number(prevMap.get(key))||0,after=ccOrderFilledQty(o);if(after>before+1e-12)changed.push(o)}if(!changed.length)return;const text=changed.slice(0,3).map(o=>ccOrderSide(o)+' '+ccFirst(ccPick(o,['displaySymbol','displayName','symbol','instId','instrument']))+' filled '+ccEsc(ccOrderFilledQty(o))).join(' · ');ccPlayFillSound();ccSetStatus(statusId,'Filled: '+text);if(statusId==='ccFuturesOrderStatus')changed.forEach(o=>{const key=ccFuturesOrderNotifyKey(o);if(key)CC_FUTURES_FILL_NOTIFY_STATE.set(key,{qty:ccOrderFilledQty(o),ts:Date.now(),reason:'open-order-delta'})})}
document.addEventListener('pointerdown',()=>{if(CC_TRADE_SETTINGS.filledNotification)ccFillAudioContext()},{once:true,passive:true})
ccLoadTradeSettings()
function ccBalanceTr(r,mode){const equity=r.balance??r.available;return mode==='funding'?`<tr><td>${ccEsc(r.ccy)}</td><td>${ccEsc(ccMoney(r.balance,r.ccy))}</td><td>${ccEsc(ccMoney(r.available,r.ccy))}</td><td>${ccEsc(ccMoney(r.frozen,r.ccy))}</td></tr>`:`<tr><td>${ccEsc(r.ccy)}</td><td>${ccEsc(ccMoney(equity,r.ccy))}</td><td>${ccEsc(ccMoney(r.available,r.ccy))}</td><td>${ccEsc(ccMoney(r.borrowed??0,r.ccy))}</td><td class="muted">—</td></tr>`}
function ccSetRows(id,html,colspan,msg){const el=cc(id);if(el)el.innerHTML=html||`<tr><td colspan="${colspan}" class="muted">${ccEsc(msg||'No CoinCall data returned.')}</td></tr>`}
function ccApplyBottomPanelState(scope,panel){const ds='cc'+scope[0].toUpperCase()+scope.slice(1)+'Panel';const cs=ds+'Content';const toolbarDs=ds.replace('Panel','ToolbarPanel');document.querySelectorAll(`[data-cc-${scope}-panel]`).forEach(b=>b.classList.toggle('active',b.dataset[ds]===panel));document.querySelectorAll(`[data-cc-${scope}-toolbar-panel]`).forEach(b=>b.classList.toggle('active',b.dataset[toolbarDs]===panel));document.querySelectorAll(`[data-cc-${scope}-panel-content]`).forEach(x=>{x.hidden=x.dataset[cs]!==panel})}function ccActiveBottomPanel(scope){const active=document.querySelector(`[data-cc-${scope}-panel].active`);const key='cc'+scope[0].toUpperCase()+scope.slice(1)+'Panel';return active&&active.dataset?String(active.dataset[key]||'').trim():''}function ccSetBottomPanel(scope,panel){ccApplyBottomPanelState(scope,panel);if(scope==='futures')ccUiStateSave({futuresBottomPanel:panel});if(scope==='spot'&&panel==='order-history')loadCcSpotOrderHistory();if(scope==='spot'&&panel==='trade-history')loadCcSpotTradeHistory();if(scope==='futures'&&panel==='open-orders')loadCcFuturesOpenOrders();if(scope==='futures'&&panel==='open-positions')loadCcFuturesPositions();if(scope==='futures'&&panel==='order-history')loadCcFuturesOrderHistory();if(scope==='futures'&&panel==='trade-history')loadCcFuturesTradeHistory();if(scope==='futures'&&panel==='funding')loadCcFuturesFundingHistory()}
function ccBottomTable(headers,rows,empty){if(!rows.length)return `<div class="okx-nitro-empty-state">${ccEsc(empty)}</div>`;return `<div class="okx-spot-orders-wrap"><table class="okx-spot-orders-table"><thead><tr>${headers.map(h=>`<th>${ccEsc(h)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`}
function ccBottomTableAlways(headers,rows,empty,colspan=headers.length){const body=rows.length?rows.join(''):`<tr><td class="muted" colspan="${colspan}">${ccEsc(empty)}</td></tr>`;return `<div class="okx-spot-orders-wrap"><table class="okx-spot-orders-table"><thead><tr>${headers.map(h=>`<th>${ccEsc(h)}</th>`).join('')}</tr></thead><tbody>${body}</tbody></table></div>`}
function ccFirst(v){return v===undefined||v===null||v===''?'—':v}
function ccPick(o,names){for(const n of names){if(o&&o[n]!==undefined&&o[n]!==null&&o[n]!=='')return o[n]}return '—'}
let ccLastFuturesPositions=[];let ccFuturesPositionsLoaded=false;
let ccLastFuturesInstruments=[];
let ccFuturesExpandedGroups={};
function ccFormatOrderTime(v){const n=Number(v);if(Number.isFinite(n)&&n>0)return ccUtcDateTime(n);const raw=ccFirst(v);if(!raw||raw==='—')return raw;const d=new Date(typeof raw==='string'&&!/[zZ]$/.test(raw)?raw+'Z':raw);return Number.isNaN(d.getTime())?raw:ccUtcDateTime(d)}
function ccOrderSide(o){const rawSide=ccSpotOrderSideRaw(o);if(rawSide==='buy')return 'Buy';if(rawSide==='sell')return 'Sell';const raw=ccPick(o,['side','tradeSide','orderSide','direction','sd','si']);return ccFirst(raw)}
function ccOrderValue(o){const explicit=ccPick(o,['value','filledValue','fillValue','dealValue','amountValue']);if(explicit!=='—')return explicit;const qty=Number(ccPick(o,['fillQty','filledQty','filledQuantity','filled']));const px=Number(ccPick(o,['avgPrice','price']));return Number.isFinite(qty)&&Number.isFinite(px)?(qty*px).toLocaleString('en-US',{maximumFractionDigits:8}):'—'}
function ccOrderStatusLabel(o){const raw=ccPick(o,['status','state','orderStatus']);const v=String(raw??'').trim().toUpperCase();const labels={'-10':'Illegal','-2':'Waiting Effect','-1':'Pre','0':'New','1':'Filled','2':'Partially Filled','3':'Canceled','4':'Pre Cancel','5':'Canceling','6':'Invalid','10':'Cancel By Exercise','NEW':'New','FILLED':'Filled','PARTIALLY_FILLED':'Partially Filled','CANCELED':'Canceled','CANCELLED':'Canceled'};return labels[v]||String(raw??'').trim()||'—'}
function ccOrderFees(o){const fee=ccPick(o,['fee','fees','commission']);const ccy=ccPick(o,['feeCurrency','feeCcy','commissionCurrency']);return fee==='—'?fee:(String(fee)+(ccy&&ccy!=='—'?' '+ccy:''))}
function ccTradeFeeText(t){const fee=ccPick(t,['fee','fees','commission']);if(fee===''||fee==='—'||fee==null)return '—';let txt=String(fee).trim();if(!txt)return '—';if(!txt.startsWith('-')){const n=Number(txt);if(!Number.isFinite(n)||n!==0)txt='-'+txt.replace(/^\+/,'')}const ccy=ccPick(t,['feeCurrency','feeCcy','commissionCurrency','commissionAsset']);return txt+(ccy&&ccy!=='—'?' '+ccy:'')}
function ccSpotOpenOrdersFrom(node,seen=new Set()){if(Array.isArray(node))return node;if(!node||typeof node!=='object'||seen.has(node))return [];seen.add(node);for(const key of ['data','result','list','items','orders','rows','openOrders']){const found=ccSpotOpenOrdersFrom(node[key],seen);if(found.length)return found}for(const v of Object.values(node)){const found=ccSpotOpenOrdersFrom(v,seen);if(found.length)return found}return []}
function ccFuturesPositionsFrom(node,seen=new Set()){if(Array.isArray(node))return node.filter(v=>v&&typeof v==='object');if(!node||typeof node!=='object'||seen.has(node))return [];seen.add(node);for(const key of ['data','list','items','rows','positions']){const found=ccFuturesPositionsFrom(node[key],seen);if(found.length)return found}for(const v of Object.values(node)){const found=ccFuturesPositionsFrom(v,seen);if(found.length)return found}return []}
function ccSignedAmount(value,side,digits=2){const n=Number(value);if(!Number.isFinite(n))return '—';const abs=Math.abs(n).toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits});return (Number(side)===2?'-':'+')+abs}
function ccSignedValue(value,digits=2){const n=Number(value);if(!Number.isFinite(n))return '—';const abs=Math.abs(n).toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits});return (n>=0?'+':'-')+abs}
function ccFmtNumber(value,digits=2){const n=Number(value);return Number.isFinite(n)?n.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits}):'—'}
function ccAssetAmountDigits(asset){const key=String(asset||'').toUpperCase();if(key==='BTC')return 3;if(key==='ETH')return 2;return 2}
function ccFormatAssetAmount(value,asset){return ccFmtNumber(value,ccAssetAmountDigits(asset))}
function ccAssetAmountText(value,asset){const rendered=ccFormatAssetAmount(value,asset);const unit=String(asset||'').toUpperCase();return rendered==='—'?rendered:(unit&&unit!=='—'?rendered+' '+unit:rendered)}
function ccOrderBaseAsset(o){const explicit=String(ccPick(o,['baseToken','base_currency','baseAsset','coin','currency'])||'').trim().toUpperCase();if(explicit&&explicit!=='—')return explicit;const symbol=String(ccPick(o,['displaySymbol','displayName','symbol','instId','instrument'])||'').toUpperCase();if(/^[A-Z0-9]+(?:USDT|USD)$/.test(symbol)){const spot=ccSpreadsSpotParts(symbol);if(spot&&spot.base)return spot.base}return ccSpreadsFuturesBase(symbol)}
function ccFmtDynamic(value){const n=Number(value);if(!Number.isFinite(n))return '—';const digits=Math.abs(n)>=100?2:5;return n.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits})}
function ccFmtPctParen(value){const n=Number(value);if(!Number.isFinite(n))return '(—)';const pct=(n*100).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2});return '('+(n>=0?'+':'')+pct+'%)'}
function ccFuturesRowTs(p){const n=Number(ccPick(p,['updateTime','createTime','time']));return Number.isFinite(n)?n:0}
function ccFuturesIsGroup(p){return Array.isArray(p?.children)&&p.children.length>0}
function ccFuturesGroupKey(p){return String(ccPick(p,['instrument','displayName','baseToken','symbol'])||'OTHER').toUpperCase()}
function ccFuturesSignedQty(p){const qty=Number(ccPick(p,['qty']));const side=Number(ccPick(p,['tradeSide','side']));if(!Number.isFinite(qty))return NaN;return Number(side)===2?-Math.abs(qty):Math.abs(qty)}
function ccFuturesSignedValue(p){const value=Number(ccPick(p,['value']));const side=Number(ccPick(p,['tradeSide','side']));if(!Number.isFinite(value))return NaN;return Number(side)===2?-Math.abs(value):Math.abs(value)}
function ccFuturesSum(list,pick){let sum=0,found=false;for(const item of list){const n=Number(pick(item));if(Number.isFinite(n)){sum+=n;found=true}}return found?sum:NaN}
function ccFuturesWeighted(list,pick,weight){let num=0,den=0;for(const item of list){const n=Number(pick(item)),w=Math.abs(Number(weight(item)));if(Number.isFinite(n)&&Number.isFinite(w)&&w>0){num+=n*w;den+=w}}return den>0?num/den:NaN}
function ccFuturesEnsureGroupExpanded(key){if(!(key in ccFuturesExpandedGroups))ccFuturesExpandedGroups[key]=true}
function ccFuturesGroupPositions(list){const buckets=new Map();for(const p of Array.isArray(list)?list:[]){const parsed=ccOrderBaseAsset(p);const base=(parsed&&parsed!=='—')?String(parsed).toUpperCase():'OTHER';if(!buckets.has(base))buckets.set(base,[]);buckets.get(base).push(p)}const rows=[];for(const [base,items] of buckets){items.sort((a,b)=>ccFuturesRowTs(b)-ccFuturesRowTs(a));if(items.length>1){const key=String(base).toUpperCase();ccFuturesEnsureGroupExpanded(key);const grossQty=ccFuturesSum(items,p=>Math.abs(Number(ccPick(p,['qty']))));const grossValue=ccFuturesSum(items,p=>Math.abs(Number(ccPick(p,['value']))));const initMargin=ccFuturesSum(items,p=>ccPick(p,['tokenInitMargin','initMargin']));const maintMargin=ccFuturesSum(items,p=>ccPick(p,['tokenMaintMargin','maintMargin']));const upnl=ccFuturesSum(items,p=>ccPick(p,['upnlByLastPrice','upnl']));const delta=ccFuturesSum(items,p=>ccPick(p,['delta']));const avgPrice=ccFuturesWeighted(items,p=>ccPick(p,['avgPrice']),p=>ccPick(p,['qty']));const markPrice=ccFuturesWeighted(items,p=>ccPick(p,['markPrice']),p=>ccPick(p,['qty']));const elp=ccFuturesWeighted(items,p=>ccPick(p,['elp']),p=>ccPick(p,['qty']));const lastPrice=ccFuturesWeighted(items,p=>ccPick(p,['lastPrice','markPrice']),p=>ccPick(p,['qty']));const roi=Number.isFinite(upnl)&&Number.isFinite(initMargin)&&Math.abs(initMargin)>1e-12?upnl/initMargin:NaN;rows.push({instrument:base,displayName:base,baseToken:base,children:items,qty:grossQty,value:grossValue,avgPrice,markPrice,elp,upnl,roi,upnlByLastPrice:upnl,roiByLastPrice:roi,initMargin,maintMargin,delta,lastPrice,tradeSide:1})}else rows.push(items[0])}return rows.sort((a,b)=>{const at=ccFuturesIsGroup(a)?ccFuturesRowTs(a.children[0]):ccFuturesRowTs(a);const bt=ccFuturesIsGroup(b)?ccFuturesRowTs(b.children[0]):ccFuturesRowTs(b);return bt-at})}
function ccFuturesFlatRows(list){const grouped=ccFuturesGroupPositions(list),rows=[];for(const row of grouped){rows.push(row);if(ccFuturesIsGroup(row)&&ccFuturesExpandedGroups[ccFuturesGroupKey(row)]!==false)rows.push(...row.children.map(child=>({...child,__childOf:ccFuturesGroupKey(row)})))}return rows}
function ccFuturesPositionLeverageValue(p){const direct=ccFirstFinite(ccPick(p,['leverage','lever','level','currentLeverage','positionLeverage']));if(Number.isFinite(direct)&&direct>0)return direct;const symbol=String(ccPick(p,['symbol','displayName','instrument'])||'').toUpperCase();const account=ccSpreadsActiveAccountId();const cached=Number(CC_SPREADS_LEVERAGE_CACHE[ccSpreadsLeverageCacheKey(symbol,account)]);return Number.isFinite(cached)&&cached>0?cached:NaN}
function ccFuturesPositionSymbolCell(p){if(ccFuturesIsGroup(p)){const key=ccFuturesGroupKey(p),open=ccFuturesExpandedGroups[key]!==false;return `<td><span><button type="button" data-cc-futures-toggle="${ccEsc(key)}" aria-label="Toggle ${ccEsc(key)}" style="margin-right:6px;background:none;border:0;padding:0;color:inherit;cursor:pointer;font:inherit">${open?'▾':'▸'}</button>${ccEsc(ccPick(p,['displayName','instrument','baseToken']))}</span><span class="sub">${ccEsc(String(p.children.length)+' positions')}</span></td>`}const name=ccPick(p,['displayName','symbol']);const lev=ccFuturesPositionLeverageValue(p);const childStyle=' style="display:block;padding-left:18px"';return `<td><span${childStyle}>${ccEsc(name)}</span><span class="sub"${childStyle}>${ccEsc(Number.isFinite(lev)&&lev>0?(ccFmtNumber(lev,0)+'x'):'—')}</span></td>`}
function ccFuturesPositionSignedAmountInfo(p){const qty=Number(ccPick(p,['qty']));const side=Number(ccPick(p,['tradeSide','side']));if(!Number.isFinite(qty))return {signed:NaN,cls:''};const signed=Number(side)===2?-Math.abs(qty):Math.abs(qty);return {signed,cls:signed>0?'cc-spreads-amount-positive':(signed<0?'cc-spreads-amount-negative':'')}}
function ccFuturesPositionAmountCell(p){const isEthGroup=ccFuturesIsGroup(p)&&String(ccPick(p,['displayName','instrument','baseToken'])||'').toUpperCase()==='ETH';if(isEthGroup)return `<td></td>`;const base=ccOrderBaseAsset(p);const qty=Number(ccPick(p,['qty']));const side=Number(ccPick(p,['tradeSide','side']));const value=Number(ccPick(p,['value']));const info=ccFuturesPositionSignedAmountInfo(p);const qtyText=Number.isFinite(qty)?ccSignedAmount(qty,side,ccAssetAmountDigits(base))+' '+base:'—';const valueText=Number.isFinite(value)?'≈ '+ccSignedAmount(value,side,2).replace(/^\+/,'$').replace(/^-/,'-$'):'—';const qtyClass=info.cls?' class=\"'+info.cls+'\"':'';return `<td><span${qtyClass}>${ccEsc(qtyText)}</span><span class=\"sub\">${ccEsc(valueText)}</span></td>`}
function ccFuturesPositionPnlCell(p){const upnl=Number(ccPick(p,['upnlByLastPrice','upnl']));const roi=Number(ccPick(p,['roiByLastPrice','roi']));const cls=Number.isFinite(upnl)?(upnl>=0?'success':'danger'):'muted';const pnlText=Number.isFinite(upnl)?ccSignedValue(upnl,2):'—';if(ccFuturesIsGroup(p))return `<td><span class="${cls}">${ccEsc(pnlText)}</span></td>`;return `<td><span class="${cls}">${ccEsc(pnlText)}</span><span class="sub ${cls}">${ccEsc(ccFmtPctParen(roi))}</span></td>`}
function ccFuturesPositionRow(p){const isGroup=ccFuturesIsGroup(p);const isEthGroup=isGroup&&String(ccPick(p,['displayName','instrument','baseToken'])||'').toUpperCase()==='ETH';const base=ccOrderBaseAsset(p);const side=Number(ccPick(p,['tradeSide','side']));const delta=Number(ccPick(p,['delta']));const price=Number(ccPick(p,['lastPrice','markPrice']));const qty=Number(ccPick(p,['qty']));const amountInfo=ccFuturesPositionSignedAmountInfo(p);const amountClass=amountInfo.cls?' class="'+amountInfo.cls+'"':'';const closeAmount=Number.isFinite(amountInfo.signed)?ccFormatAssetAmount(amountInfo.signed,base):'--';const deltaText=Number.isFinite(delta)?(isGroup?ccFmtDynamic(delta):ccSignedAmount(Math.abs(delta),side,5)):'--';const cell=(value,formatter)=>ccEsc(formatter?formatter(value):value);const avgCell=isEthGroup?'':cell(ccPick(p,['avgPrice']),v=>Number.isFinite(Number(v))?ccFmtNumber(v,2):'--');const markCell=isEthGroup?'':cell(ccPick(p,['markPrice']),v=>Number.isFinite(Number(v))?ccFmtDynamic(v):'--');const elpCell=isEthGroup?'':cell(ccPick(p,['elp']),v=>Number.isFinite(Number(v))?ccFmtNumber(v,2):'--');return `<tr${isGroup?' data-cc-futures-group-row="1"':''}>${ccFuturesPositionSymbolCell(p)}${ccFuturesPositionAmountCell(p)}<td>${avgCell}</td><td>${markCell}</td><td>${elpCell}</td>${ccFuturesPositionPnlCell(p)}<td>${cell(ccPick(p,['initMargin']),v=>Number.isFinite(Number(v))?ccFmtNumber(v,2):'--')}</td><td>${cell(ccPick(p,['maintMargin']),v=>Number.isFinite(Number(v))?ccFmtNumber(v,2):'--')}</td><td>${ccEsc(deltaText)}</td><td>--</td><td>--</td><td>--</td><td>--</td><td>-</td><td>--</td><td>${ccEsc(Number.isFinite(price)?ccFmtDynamic(price):'--')}</td><td${amountClass}>${ccEsc(closeAmount)}</td><td>--</td></tr>`}
function ccFuturesBookSymbolKey(v){return String(v||'').toUpperCase().replace(/[^A-Z0-9]/g,'').replace(/USDT(?=\d)/,'USD').replace(/PERP$/,'').replace(/USDT$/,'').replace(/USD$/,'')}
function ccFuturesInstrumentMark(symKey){const rows=Array.isArray(ccLastFuturesInstruments)?ccLastFuturesInstruments:[];for(const row of rows){const keys=[ccPick(row,['ticker_id','symbol','displayName']),ccPick(row,['base_currency','baseToken'])].map(ccFuturesBookSymbolKey).filter(Boolean);if(keys.includes(symKey)){const mark=Number(ccPick(row,['markPrice','mark_price','contract_price','contractPrice','last_price','lastPrice','index_price','indexPrice']));if(Number.isFinite(mark))return mark}}return NaN}
function ccFuturesAppendMarkPriceToBookMid(){const mid=cc('ccFuturesMid');if(!mid)return;const rawText=String(mid.dataset.baseText||mid.textContent||'').trim();if(!rawText||rawText==='No order book data')return;const sym=String(cc('ccFuturesInst')?.value||'').toUpperCase();const symKey=ccFuturesBookSymbolKey(sym);const rows=Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[];const liveTrade=ccFuturesLiveLastTradeInfo();let mark=NaN,last=Number.isFinite(liveTrade.price)?liveTrade.price:NaN;for(const p of rows){const keys=[ccPick(p,['instrument','symbol','displayName']),ccPick(p,['baseToken'])].map(ccFuturesBookSymbolKey).filter(Boolean);if(keys.includes(symKey)){if(!Number.isFinite(last)){const nextLast=Number(ccPick(p,['lastPrice','markPrice']));if(Number.isFinite(nextLast))last=nextLast}const nextMark=Number(ccPick(p,['markPrice','lastPrice']));if(Number.isFinite(nextMark))mark=nextMark;if(Number.isFinite(last)&&Number.isFinite(mark))break}}if(!Number.isFinite(mark)||!Number.isFinite(last)){for(const p of rows){const baseKey=ccFuturesBookSymbolKey(ccPick(p,['baseToken','displayName','instrument','symbol']));if(baseKey&&baseKey===symKey){if(!Number.isFinite(last)){const nextLast=Number(ccPick(p,['lastPrice','markPrice']));if(Number.isFinite(nextLast))last=nextLast}if(!Number.isFinite(mark)){const nextMark=Number(ccPick(p,['markPrice','lastPrice']));if(Number.isFinite(nextMark))mark=nextMark}if(Number.isFinite(last)&&Number.isFinite(mark))break}}}if(!Number.isFinite(mark))mark=ccFuturesInstrumentMark(symKey);if(!Number.isFinite(last)){const chartLastText=String(cc('ccFuturesLast')?.textContent||'').trim();if(chartLastText&&chartLastText!=='—')last=Number(String(chartLastText).replace(/,/g,''))}const baseText=Number.isFinite(last)?ccFmt(last,ccPriceDecimals(last)):(rawText||'...');const markText=Number.isFinite(mark)?ccFmt(mark,ccPriceDecimals(mark)):'';const bidAskMedian=ccFuturesCurrentBidAskMedian();const bidAskText=Number.isFinite(bidAskMedian)?ccFmt(bidAskMedian,ccPriceDecimals(bidAskMedian)):'';let side=liveTrade.side==='buy'||liveTrade.side==='sell'?liveTrade.side:'';const track=CC_BOOK_STATE.futuresMidTrack||{};if(!side&&Number.isFinite(last)){if(track.symbol===sym&&Number.isFinite(track.price)){if(last>track.price)side='buy';else if(last<track.price)side='sell';else if(track.side==='buy'||track.side==='sell')side=track.side}CC_BOOK_STATE.futuresMidTrack={symbol:sym,price:last,side:side||track.side||''}}mid.classList.add('cc-futures-book-mid');mid.innerHTML='<span class="cc-futures-book-mid-last'+(side?' '+side:'')+'">'+(side?'<span class="okx-spot-mid-trend">'+(side==='buy'?'▲':'▼')+'</span>':'')+'<span class="okx-spot-mid-price">'+ccEsc(baseText)+'</span></span><span class="cc-futures-book-mid-mark">'+ccEsc(markText)+'</span><span class="cc-futures-book-mid-bidask">'+ccEsc(bidAskText)+'</span>'}
let ccFuturesAccountSummaryRefreshPromise=null;
function ccFirstFinite(){for(const value of arguments){const n=Number(value);if(Number.isFinite(n))return n}return NaN}
function ccFuturesSummarySum(rows,key){let sum=0,seen=false;for(const row of Array.isArray(rows)?rows:[]){const n=Number(row&&row[key]);if(Number.isFinite(n)){sum+=n;seen=true}}return seen?sum:NaN}
function ccIsFuturesAccountSummaryActive(){return ccActiveMarket()==='futures'}
async function ccMaybeRefreshFuturesAccountSummaryLive(){if(!ccIsFuturesAccountSummaryActive())return;return ccRefreshCcAccountSnapshotLive(false)}
function ccFuturesAccountSummaryState(){const raw=ccLastAssetsSummary||{};const rows=ccBalanceRows(raw);const metrics=raw&&raw.metrics?raw.metrics:{};const positions=Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[];const upnl=ccFirstFinite(ccFuturesSummarySum(rows,'upl'),ccFindNumericDeep(raw,['upl','unrealizedpnl','unrealisedpnl','unrealizedprofit','unrealisedprofit']),ccFuturesSummarySum(positions,'upnl'),ccFuturesSummarySum(positions,'upnlByLastPrice'));const totalEquity=ccFirstFinite(metrics&&metrics.totalEquity,ccHeaderStableSum(rows,'balance'));const marginBalance=ccFirstFinite(ccFindNumericDeep(raw,['marginbalance','margin_balance']),Number.isFinite(totalEquity)&&Number.isFinite(upnl)?totalEquity-upnl:NaN,totalEquity);const maintenanceMargin=ccFirstFinite(ccFindNumericDeep(raw,['maintenancemargin','maintenance_margin','maintmargin','mm']),ccFuturesSummarySum(positions,'maintMargin'),ccFuturesSummarySum(positions,'tokenMaintMargin'));const trialBonus=ccFirstFinite(ccFindNumericDeep(raw,['trialbonus','trial_bonus','bonus','couponbonus','creditbonus']),0);const ratioDirect=ccFindNumericDeep(raw,['marginratio','margin_ratio']);const marginRatio=ccFirstFinite(ratioDirect,Number.isFinite(maintenanceMargin)&&Number.isFinite(marginBalance)&&Math.abs(marginBalance)>1e-12?maintenanceMargin/Math.abs(marginBalance)*100:NaN);return {marginRatio:Number.isFinite(ratioDirect)&&ratioDirect<=1?ratioDirect*100:marginRatio,maintenanceMargin,marginBalance,totalEquity,trialBonus,upnl}}
function ccFuturesSummaryMarkup(){const state=ccFuturesAccountSummaryState();const ratioText=Number.isFinite(state.marginRatio)?state.marginRatio.toFixed(2)+'%':'—';const ratioFill=Number.isFinite(state.marginRatio)?Math.max(0,Math.min(100,state.marginRatio)):0;const money=(value)=>Number.isFinite(Number(value))?ccFmtNumber(value,2)+' USDT':'—';const pnlClass=Number.isFinite(state.upnl)?(state.upnl>=0?' positive':' negative'):'';const pnlText=Number.isFinite(state.upnl)?ccSignedValue(state.upnl,2)+' USDT':'—';return '<div class="cc-futures-summary-row"><div class="cc-futures-summary-label">Margin Ratio</div><div class="cc-futures-summary-ratio"><span class="cc-futures-summary-value">'+ccEsc(ratioText)+'</span><span class="cc-futures-summary-bar" aria-hidden="true"><span class="cc-futures-summary-bar-fill" style="width:'+ccEsc(ratioFill.toFixed(2))+'%"></span></span></div></div><div class="cc-futures-summary-row"><div class="cc-futures-summary-label">Maintenance Margin</div><div class="cc-futures-summary-value">'+ccEsc(money(state.maintenanceMargin))+'</div></div><div class="cc-futures-summary-row"><div class="cc-futures-summary-label">Margin Balance</div><div class="cc-futures-summary-value">'+ccEsc(money(state.marginBalance))+'</div></div><div class="cc-futures-summary-row"><div class="cc-futures-summary-label">Total Equity</div><div class="cc-futures-summary-value">'+ccEsc(money(state.totalEquity))+'</div></div><div class="cc-futures-summary-row"><div class="cc-futures-summary-label">Trial Bonus</div><div class="cc-futures-summary-value">'+ccEsc(money(state.trialBonus))+'</div></div><div class="cc-futures-summary-row"><div class="cc-futures-summary-label">Unrealized PnL</div><div class="cc-futures-summary-value'+pnlClass+'">'+ccEsc(pnlText)+'</div></div>'}
function ccSpreadsHeaderMetricValues(raw){const metrics=raw&&raw.metrics?raw.metrics:null;const equity=Number(metrics&&metrics.totalEquity),available=Number(metrics&&metrics.availableEquity);const im=ccHeaderPct(raw,['initialmargin','initialmarginused','im','usedmargin'],['imrate','imratio','initialmarginrate','initialmarginratio'],equity);const mm=ccHeaderPct(raw,['maintenancemargin','maintmargin','mm'],['mmrate','mmratio','maintenancemarginrate','maintenancemarginratio'],equity);return {equity,available,im,mm}}
function ccSpreadsModalAccountMarkup(){const values=ccSpreadsHeaderMetricValues(ccLastAssetsSummary||{});const marginMode=ccMarginModeText(ccLastAssetsSummary||{});const sel=cc('ccAccountSelect');const current=ccAccountsCache.find(a=>String(a.id)===String(sel&&sel.value))||ccAccountsCache.find(a=>a.isActive);const fmt=v=>Number.isFinite(v)?ccFmt(v,2):'—';const pct=v=>Number.isFinite(v)?v.toFixed(2)+'%':'—';return '<div class="cc-header-metrics"><span class="cc-header-metric-label">Equity</span><span class="cc-header-metric-value">'+ccEsc(fmt(values.equity))+'</span><span class="cc-header-metric-label cc-header-metric-under">IM%</span><span class="cc-header-metric-muted">'+ccEsc(pct(values.im))+'</span><button type="button" class="cc-header-mcm" tabindex="-1" title="Margin Mode: '+ccEsc(marginMode)+'">'+ccEsc(marginMode)+'</button><span class="cc-header-metric-label">Available</span><span class="cc-header-metric-value">'+ccEsc(fmt(values.available))+'</span><span class="cc-header-metric-label cc-header-metric-under">MM%</span><span class="cc-header-metric-muted">'+ccEsc(pct(values.mm))+'</span></div><div class="account-badge"><img class="coincall-account-icon" src="/coincall-logo.jpg" alt="CoinCall"><strong>'+ccEsc(current?current.name:'—')+'</strong></div>'}
function ccRenderSpreadsModalAccount(){const el=cc('ccSpreadsModalAccount');if(el)el.innerHTML=ccSpreadsModalAccountMarkup()}
function ccRenderSpreadsAccountSummary(){const inline=document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-account-summary]');if(inline)inline.innerHTML=ccFuturesSummaryMarkup();ccRenderSpreadsPositionSummary();ccRenderSpreadsModalAccount();ccRenderSpreadsPositionsOrdersTable()}
function ccRenderFuturesAccountSummary(){const inline=cc('ccFuturesTradeSummary');if(inline)inline.innerHTML=ccFuturesSummaryMarkup();ccRenderSpreadsAccountSummary()}

function ccFuturesToolbarSetLabel(panel,text){document.querySelectorAll(`[data-cc-futures-panel="${panel}"]`).forEach(el=>el.replaceChildren(document.createTextNode(text)))}
function ccFuturesSortQuoteSymbols(rows){return rows.slice().sort((a,b)=>{const aBase=String(a?.baseToken||a?.base_currency||'').toUpperCase(),bBase=String(b?.baseToken||b?.base_currency||'').toUpperCase();if(aBase!==bBase)return aBase.localeCompare(bBase);const aPerp=Number(a?.expireTime||a?.expire_time||0)===0?0:1,bPerp=Number(b?.expireTime||b?.expire_time||0)===0?0:1;if(aPerp!==bPerp)return aPerp-bPerp;const aExp=Number(a?.expireTime||a?.expire_time||0),bExp=Number(b?.expireTime||b?.expire_time||0);if(aExp!==bExp)return aExp-bExp;return String(a?.symbol||'').localeCompare(String(b?.symbol||''))})}
function ccFuturesQuoteSymbolRows(rows){return ccFuturesSortQuoteSymbols((Array.isArray(rows)?rows:[]).filter(row=>String(row?.symbol||row?.ticker_id||'').trim()))}
function ccFuturesFindInstrumentRow(rows,symbol){const want=String(symbol||'').trim();return ccFuturesQuoteSymbolRows(rows).find(row=>String(row?.symbol||'').trim()===want)||null}
function ccFuturesContractLabel(row){const expireRaw=Number(row?.expireTime||row?.expire_time||0);if(!Number.isFinite(expireRaw)||expireRaw<=0)return 'PERPETUAL';const ts=expireRaw<1e12?expireRaw*1000:expireRaw;const d=new Date(ts);if(Number.isNaN(d.getTime()))return String(row?.displayName||row?.symbolName||row?.symbol||'').trim().toUpperCase();const months=['JAN','FEB','MAR','APR','MAY','JUN','JUL','AUG','SEP','OCT','NOV','DEC'];return String(d.getUTCDate()).padStart(2,'0')+' '+months[d.getUTCMonth()]+' '+String(d.getUTCFullYear()).slice(-2)}
function ccSyncFuturesMarketChips(){const active=document.querySelector('[data-cc-trade].active')?.dataset?.ccTrade||ccActiveMarket();document.querySelectorAll('[data-cc-contract-market]').forEach(btn=>btn.classList.toggle('active',btn.dataset.ccContractMarket===active))}
function ccRenderFuturesContractStrip(rows=ccLastFuturesInstruments){const host=cc('ccFuturesContractStrip');if(!host)return;const list=ccFuturesQuoteSymbolRows(rows);if(!list.length){host.innerHTML='<span class="cc-futures-contract-strip-empty">Loading contracts...</span>';ccSyncFuturesMarketChips();return}const current=String(cc('ccFuturesInst')?.value||'').trim();const currentRow=ccFuturesFindInstrumentRow(list,current)||list[0];const activeBase=String(currentRow?.baseToken||currentRow?.base_currency||ccFuturesParts().base||'BTC').toUpperCase();const family=list.filter(row=>String(row?.baseToken||row?.base_currency||'').toUpperCase()===activeBase);host.innerHTML=family.map(row=>{const symbol=String(row?.symbol||'').trim();const active=symbol===current?' active':'';return '<button type="button" class="cc-futures-contract-chip'+active+'" data-cc-futures-contract-symbol="'+ccEsc(symbol)+'" aria-pressed="'+(active?'true':'false')+'">'+ccEsc(ccFuturesContractLabel(row))+'</button>'}).join('');host.querySelectorAll('[data-cc-futures-contract-symbol]').forEach(btn=>btn.addEventListener('click',()=>{const symbol=String(btn.getAttribute('data-cc-futures-contract-symbol')||'').trim();const sel=cc('ccFuturesInst');if(!sel||!symbol)return;if(sel.value!==symbol)sel.value=symbol;sel.dispatchEvent(new Event('change',{bubbles:true}))}));ccSyncFuturesMarketChips()}
function ccPopulateFuturesInstrumentSelect(rows){const sel=cc('ccFuturesInst');if(!sel)return;const list=ccFuturesQuoteSymbolRows(rows);if(!list.length){ccRenderFuturesContractStrip([]);return}const prev=String(sel.value||'').trim();sel.innerHTML=list.map(row=>'<option value="'+ccEsc(String(row.symbol||'').trim())+'">'+ccEsc(String(row.displayName||row.symbolName||row.symbol||'').trim())+'</option>').join('');const fallback=list.find(row=>Number(row.expireTime||row.expire_time||0)===0)?.symbol||list[0].symbol;sel.value=list.some(row=>String(row.symbol||'').trim()===prev)?prev:String(fallback||'');ccRenderFuturesContractStrip(list)}
async function loadCcFuturesInstruments(){try{const [dbRes,quoteRes,instrRes]=await Promise.allSettled([ccApi('/api/admin/coincall/futures/candles/minute/symbols'),ccApi('/api/admin/coincall/public/futures/quote-symbols'),ccApi('/api/admin/coincall/public/futures/instruments')]);const dbRows=dbRes.status==='fulfilled'?ccDbBackedFuturesSymbolRows(dbRes.value):[];const quoteRows=quoteRes.status==='fulfilled'&&Array.isArray(quoteRes.value?.data)?quoteRes.value.data:[];const instrRows=instrRes.status==='fulfilled'&&Array.isArray(instrRes.value?.data)?instrRes.value.data:[];const merged=new Map();for(const row of quoteRows){const symbol=String(row?.symbol||row?.ticker_id||'').trim();if(symbol)merged.set(symbol,{...row})}for(const row of instrRows){const symbol=String(row?.symbol||row?.ticker_id||'').trim();if(!symbol)continue;const prev=merged.get(symbol)||{};merged.set(symbol,{...prev,...row})}const rows=dbRows.length?dbRows.map(row=>{const prev=merged.get(row.symbol)||{};return {...prev,symbol:row.symbol,displayName:row.displayName||prev.displayName||prev.symbolName||row.symbol}}):Array.from(merged.values());ccLastFuturesInstruments=rows;if(rows.length)ccPopulateFuturesInstrumentSelect(rows);else ccRenderFuturesContractStrip([]);ccFuturesAppendMarkPriceToBookMid();ccRefreshActiveFuturesPanel()}catch(e){ccLastFuturesInstruments=[];ccRenderFuturesContractStrip([])}}
function ccRestoreUiState(){const state=ccUiStateLoad();const layer=state.layer||'trade';ccUiStateApplySelect('ccSpotInst',state.spotSymbol);ccUiStateApplySelect('ccSpotBar',state.spotTimeframe);ccUiStateApplySelect('ccFuturesInst',state.futuresSymbol);ccUiStateApplySelect('ccFuturesBar',state.futuresTimeframe);ccApplyLayerState(layer);if(layer==='trade'){ccApplyTradeState(state.trade||'futures');ccApplyBottomPanelState('futures',state.futuresBottomPanel||'open-orders')}else ccHideTradeOnlyPanels();ccApplyAssetTabState(state.assetTab||'overview');ccSetMarketDataMode(state.dataChartMode||'candles',false);ccSetMarketDataTab(state.dataMarketTab||'spot',false);return state}
function ccPopulateFuturesInstrumentSelect(rows){const sel=cc('ccFuturesInst');if(!sel)return;const list=ccFuturesQuoteSymbolRows(rows);if(!list.length){ccRenderFuturesContractStrip([]);ccSyncAssetsFundingSymbolSelect();return}const prev=String(sel.value||'').trim();const saved=String(ccUiStateLoad().futuresSymbol||'').trim();sel.innerHTML=list.map(row=>'<option value="'+ccEsc(String(row.symbol||'').trim())+'">'+ccEsc(String(row.displayName||row.symbolName||row.symbol||'').trim())+'</option>').join('');const fallback=list.find(row=>Number(row.expireTime||row.expire_time||0)===0)?.symbol||list[0].symbol;const target=[saved,prev,String(fallback||'')].find(value=>list.some(row=>String(row.symbol||'').trim()===String(value||'').trim()));sel.value=String(target||fallback||'');sel.dataset.ccPrevValue=sel.value;ccFuturesApplySavedSymbolState(sel.value);ccRenderFuturesContractStrip(list);ccSyncAssetsFundingSymbolSelect()}
async function loadCcFuturesPositions(){try{const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';const res=await ccApi('/api/admin/coincall/futures/positions'+qs);ccLastFuturesPositions=ccFuturesPositionsFrom(res);ccFuturesPositionsLoaded=true;ccRenderFuturesBottomPanels();ccFuturesAppendMarkPriceToBookMid();ccLoadFuturesMaxAvailable().catch(()=>{});drawCcChart('ccFuturesChart')}catch(e){if(Array.isArray(ccLastFuturesPositions)&&ccLastFuturesPositions.length){ccFuturesPositionsLoaded=true;ccRenderFuturesBottomPanels();return}ccLastFuturesPositions=[];ccFuturesPositionsLoaded=false;const panel=document.querySelector('[data-cc-futures-panel-content="open-positions"]');if(panel)panel.innerHTML='<div class="okx-nitro-empty-state">CoinCall futures positions failed: '+ccEsc(e.message)+'</div>';ccFuturesToolbarSetLabel('open-positions','Positions (0)');ccLastFuturesMaxAvailable=null;ccRenderFuturesMaxOrderUi();drawCcChart('ccFuturesChart')}}
function ccRenderFuturesBottomPanels(){ccRenderSpreadsPositionSummary();ccRenderSpreadsPositionsOrdersTable();const panel=(name)=>document.querySelector(`[data-cc-futures-panel-content="${name}"]`);const positions=Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[];const openOrders=Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[];const orderHistory=Array.isArray(ccLastFuturesOrderHistory)?ccLastFuturesOrderHistory:[];const tradeHistory=Array.isArray(ccLastFuturesTradeHistory)?ccLastFuturesTradeHistory:[];const rows=ccBalanceRows(ccLastAssetsSummary||{});const assetRows=rows.filter(ccIsNonZeroTradingBalance).map(r=>{const equity=ccRowEquityValue(r),available=ccCoincallAssetAvailableValue(r);return `<tr><td>${ccEsc(r.ccy)}</td><td>${ccEsc(ccMoney(equity,r.ccy))}</td><td>${ccEsc(ccMoney(available,r.ccy))}</td><td>${ccEsc(ccMoney(r.borrowed??0,r.ccy))}</td><td class="muted">—</td></tr>`});document.querySelector('[data-cc-futures-panel="open-orders"]')?.replaceChildren(document.createTextNode('Orders ('+openOrders.length+')'));document.querySelector('[data-cc-futures-panel="order-history"]')?.replaceChildren(document.createTextNode('Order history ('+orderHistory.length+')'));document.querySelector('[data-cc-futures-panel="trade-history"]')?.replaceChildren(document.createTextNode('Trade history ('+tradeHistory.length+')'));ccFuturesToolbarSetLabel('open-positions','Positions ('+positions.length+')');const openOrdersPanel=panel('open-orders');if(openOrdersPanel)openOrdersPanel.innerHTML=ccBottomTableAlways(['Time','Symbol','Side','Average｜Price','Filled｜Amount','IM','Type','Reduce Only','Trigger Conditions','TP/SL','Actions'],openOrders.map(ccFuturesOrderRow),ccFuturesOpenOrdersLoaded?'No CoinCall futures open orders.':'Loading CoinCall futures open orders...',11);const historyPanel=panel('order-history');if(historyPanel)historyPanel.innerHTML=ccBottomTableAlways(['Time','Symbol','Side','Average｜Price','Filled｜Amount','Status','Order ID','Fees','Realized PnL','Type','Reduce Only','Trigger Conditions','TIF'],orderHistory.map(ccFuturesOrderHistoryRow),'No CoinCall futures order history returned.',13);const tradeHistoryPanel=panel('trade-history');if(tradeHistoryPanel)tradeHistoryPanel.innerHTML=ccBottomTableAlways(['Time','Symbol','Side','Amount','Value','Filled Price','Mark Price','Index','Order ID｜Trade ID','Fees','Realized PnL','Role'],tradeHistory.map(ccFuturesTradeHistoryRow),'No CoinCall futures trade history returned.',12);const pos=panel('open-positions');if(pos)pos.innerHTML=ccBottomTable(['Symbol','Amount','Entry Price','Mark Price','ELP','PnL (ROI)','IM','MM','Delta','Gamma','Vega','Theta','Rho','Reverse','Close Positions','Price','Amount','TP/SL'],ccFuturesFlatRows(positions).map(ccFuturesPositionRow),'No CoinCall futures positions returned.');const assets=panel('assets');if(assets)assets.innerHTML=ccBottomTable(['Coin','Equity','Available Balance','Borrowed Amount','Actions'],assetRows,'No CoinCall assets returned by the assets summary.');ccRenderFuturesAccountSummary()}
function ccOrderTypeLabel(raw){const v=String(raw??'').trim().toUpperCase().replace(/[-\s]+/g,'_');const labels={'0':'Limit','1':'Limit','LIMIT':'Limit','2':'Market','MARKET':'Market','3':'Post Only','POST_ONLY':'Post Only','POSTONLY':'Post Only','4':'Stop Limit','STOP_LIMIT':'Stop Limit','5':'Stop Market','STOP_MARKET':'Stop Market','14':'Block Trade','BLOCK_TRADE':'Block Trade'};return labels[v]||String(raw??'').trim()||'—'}
function ccSpotOrderTypeLabel(o){return ccOrderTypeLabel(ccPick(o,['type','orderType','ordType','tradeType','timeInForce']))}
function ccSpotOrderId(o){return ccOrderFirstId(o,['orderId','ordId','order_id'])}
function ccSpotClientOrderId(o){return ccOrderFirstId(o,['clientOrderId','clientOid','clOrdId'])}
function ccSpotOrderDisplayId(o){return ccSpotOrderId(o)||ccSpotClientOrderId(o)||ccOrderFirstId(o,['id','orderNo'])}
function ccSpotCancelPayload(o){const clientOrderId=ccSpotClientOrderId(o);if(clientOrderId&&clientOrderId!=='—')return {clientOrderId:String(clientOrderId)};const orderId=ccSpotOrderId(o);if(orderId&&orderId!=='—')return {orderId:String(orderId)};return {}}
function ccFuturesCancelPayload(o){const ids=ccFuturesOrderIdentity(o);if(ids.orderId&&ids.orderId!=='—')return {orderId:String(ids.orderId)};if(ids.clientOrderId&&ids.clientOrderId!=='—')return {clientOrderId:String(ids.clientOrderId)};return {}}
function ccFuturesCancelKey(o,payload=ccFuturesCancelPayload(o)){if(payload.orderId)return 'oid:'+payload.orderId;if(payload.clientOrderId)return 'cid:'+payload.clientOrderId;const symbol=ccFuturesCanonicalDisplaySymbolFromOrder(o)||ccPick(o,['displaySymbol','displayName','symbol','instId','instrument']);return ['shape',symbol,ccSpotOrderSideRaw(o),ccFuturesOrderPrice(o),ccFuturesOrderAmountRaw(o)].join('|')}
function ccFuturesCancelSummary(o,payload=ccFuturesCancelPayload(o)){const symbol=ccFuturesCanonicalDisplaySymbolFromOrder(o)||ccPick(o,['displaySymbol','displayName','symbol','instId','instrument'])||'futures';const side=ccOrderSide(o);const price=ccFuturesOrderPrice(o);const id=String(payload.orderId||payload.clientOrderId||'').trim();return [symbol,side,Number.isFinite(Number(price))?'@ '+ccFmtNumber(price,2):'',id?'#'+id.slice(-6):''].filter(Boolean).join(' ')}
function ccFuturesCancelActionHtml(o,i,quote='"'){const payload=ccFuturesCancelPayload(o);if(!payload.orderId&&!payload.clientOrderId)return '<span class='+quote+'muted'+quote+'>—</span>';const pending=CC_FUTURES_CANCEL_PENDING.has(ccFuturesCancelKey(o,payload));const attrs='class='+quote+'cc-spot-cancel-order cc-futures-cancel-order'+(pending?' pending':'')+quote+' role='+quote+'button'+quote+' data-cc-futures-cancel-index='+quote+i+quote+(pending?' aria-disabled='+quote+'true'+quote+' data-cc-cancel-pending='+quote+'1'+quote:'');return '<span '+attrs+'>'+(pending?'Canceling...':'Cancel')+'</span>'}
function ccSpotOrderRow(o,i){const base=ccOrderBaseAsset(o);const filled=ccPick(o,['fillQty','filledQty','filledQuantity','filled']);const amount=ccPick(o,['qty','quantity','amount','remainQty']);const filledText=Number.isFinite(Number(filled))?ccFormatAssetAmount(filled,base):filled;const amountText=Number.isFinite(Number(amount))?ccFormatAssetAmount(amount,base):amount;const orderId=ccSpotOrderDisplayId(o);const action=orderId?`<button type=\"button\" class=\"link-btn cc-spot-cancel-order\" data-cc-spot-cancel-index=\"${i}\">Cancel</button>`:'<span class=\"muted\">—</span>';return `<tr><td>${ccEsc(ccFormatOrderTime(ccPick(o,['ts','createTime','createdTime','time','updateTime'])))}</td><td>${ccEsc(ccPick(o,['displaySymbol','symbol','instId']))}</td><td>${ccEsc(ccOrderSide(o))}</td><td>${ccEsc(ccPick(o,['price','px']))}</td><td>${ccEsc(filledText)}｜${ccEsc(amountText)}</td><td>${ccEsc(ccOrderValue(o))}</td><td>${ccEsc(ccSpotOrderTypeLabel(o))}</td><td>${action}</td></tr>`}
function ccFmtBoolText(v){if(v===true||String(v).toLowerCase()==='true'||String(v)==='1')return 'True';if(v===false||String(v).toLowerCase()==='false'||String(v)==='0')return 'False';return '—'}
function ccFuturesOpenOrdersFrom(raw){const root=raw&&raw.data!==undefined?raw.data:raw;const out=[];const seen=new Set();for(const o of ccCollectObjects(root)){if(!o||typeof o!=='object'||Array.isArray(o))continue;const symbolRaw=String(ccPick(o,['displaySymbol','displayName','symbol','instId','instrument','ticker_id','baseToken','base_currency','s'])||'').trim();const typeHint=String(ccPick(o,['market','productType','category','businessType','contractType','instType'])||'').toUpperCase();const ids=ccFuturesOrderIdentity(o);const hasOrderShape=!!(ids.orderId||ids.clientOrderId||ccPick(o,['price','px','orderPrice','limitPrice','avgPrice','orderType','tradeType','type'])!=='—');const side=ccSpotOrderSideRaw(o),price=ccFuturesOrderPrice(o),qty=Number(ccFuturesOrderAmountRaw(o));const futuresLike=/PERP|FUT|SWAP/.test(symbolRaw.toUpperCase())||/USD/.test(symbolRaw.toUpperCase())||/^(BTC|ETH)$/i.test(symbolRaw)||/FUT|PERP|SWAP/.test(typeHint);if(!hasOrderShape||!futuresLike||ccFuturesOrderTerminal(o)||!(side==='buy'||side==='sell')||!Number.isFinite(price)||price<=0||!Number.isFinite(qty)||qty<=0)continue;const normalized={...o,displaySymbol:ccFuturesCanonicalDisplaySymbolFromOrder(o)||symbolRaw};const match=out.findIndex(x=>ccFuturesOrderSame(x,normalized));if(match>=0){out[match]={...out[match],...ccFuturesCompactPrivateOrder(normalized)};continue}const key=ccFuturesOpenOrderDedupeKey(normalized);if(!key||seen.has(key))continue;seen.add(key);out.push(normalized)}return out}
function ccFuturesOrderTimeCell(o){const text=ccFormatOrderTime(ccPick(o,['ts','createTime','createdTime','time','updateTime']));const parts=String(text).split(' ');return `<span>${ccEsc(parts[0]||'—')}</span><span class="sub">${ccEsc((parts[1]||'—')+(parts[2]?' '+parts[2]:''))}</span>`}
function ccFuturesOrderSymbolText(o){let symbol=String(ccFuturesCanonicalDisplaySymbolFromOrder(o)||'—').trim();symbol=symbol.replace(/[-_]/g,' ');symbol=symbol.replace(/\bPERP\b/ig,'Perp');const leverage=Number(ccPick(o,['leverage','lever','level']));return Number.isFinite(leverage)&&leverage>0?`${symbol} ${ccFmtNumber(leverage,0)}x`:symbol}
function ccFuturesOrderQtyUnit(o){const explicit=String(ccPick(o,['baseToken','base_currency','baseAsset'])||'').trim().toUpperCase();if(explicit&&explicit!=='—')return explicit;const symbol=String(ccPick(o,['displaySymbol','displayName','symbol','instId','instrument'])||'').toUpperCase();const match=symbol.match(/^([A-Z0-9]+?)(?:USDT|USD|\s)/);return match&&match[1]?match[1]:'—'}
function ccFuturesOrderAmountRaw(o){return ccPick(o,['qty','quantity','amount','size','orderQty','remainQty','remainingQty','leavesQty','leftQty','unfilledQty','volume','origQty','q','sz'])}
function ccFuturesOrderAmountText(value,o){const unit=ccFuturesOrderQtyUnit(o);return ccAssetAmountText(value,unit)}
function ccFuturesOrderAvgPriceCell(o){const avg=ccPick(o,['avgPrice','averagePrice','dealAvgPrice','filledAvgPrice']);const px=ccPick(o,['price','px','orderPrice']);const priceText=Number.isFinite(Number(px))?ccFmtNumber(px,2):px;return `<span style="color:#f8fafc">${ccEsc(avg)}</span><span class="sub" style="color:#f8fafc">${ccEsc(priceText)}</span>`}
function ccFuturesOrderFilledAmountCell(o){const filled=ccPick(o,['fillQty','filledQty','filledQuantity','filled','dealQty']);const amount=ccFuturesOrderAmountRaw(o);return `<span style="color:#f8fafc">${ccEsc(ccFuturesOrderAmountText(filled,o))}</span><span class="sub" style="color:#f8fafc">${ccEsc(ccFuturesOrderAmountText(amount,o))}</span>`}
function ccSpreadsPopupFuturesOrderAmountInline(o){const filled=ccPick(o,['fillQty','filledQty','filledQuantity','filled','dealQty']);const amount=ccFuturesOrderAmountRaw(o);return ccEsc(ccFuturesOrderAmountText(filled,o))+' | '+ccEsc(ccFuturesOrderAmountText(amount,o))}
function ccSpreadsPopupFuturesOrderAvgPriceInline(o){const avg=ccPick(o,['avgPrice','averagePrice','dealAvgPrice','filledAvgPrice']);const px=ccPick(o,['price','px','orderPrice','limitPrice','p']);const priceText=Number.isFinite(Number(px))?ccFmtNumber(px,2):px;return ccEsc(avg)+' | '+ccEsc(priceText)}
function ccSpreadsRenderableFuturesOrder(o){const side=ccSpotOrderSideRaw(o),price=ccFuturesOrderPrice(o),qty=Number(ccFuturesOrderAmountRaw(o));return (side==='buy'||side==='sell')&&Number.isFinite(price)&&price>0&&Number.isFinite(qty)&&qty>0&&!ccFuturesOrderTerminal(o)}
function ccSpreadsPopupSpotAmountDigits(asset){const key=String(asset||'').toUpperCase();if(key==='BTC')return 5;if(key==='ETH')return 4;return ccAssetAmountDigits(key)}
function ccSpreadsPopupSpotAmountText(value,asset){const n=Number(value);return Number.isFinite(n)?ccFmtNumber(n,ccSpreadsPopupSpotAmountDigits(asset)):String(value??'—')}
function ccFuturesOrderTypeLabel(o){const postOnly=ccPick(o,['postOnly','isPostOnly','makerOnly']);if(String(postOnly).toLowerCase()==='true'||String(postOnly)==='1')return 'Post Only';const tif=String(ccPick(o,['timeInForce','tif','execInst'])||'').trim().toUpperCase().replace(/[-\s]+/g,'_');if(tif==='POST_ONLY'||tif==='POSTONLY'||tif==='PO')return 'Post Only';for(const keys of [['tradeType'],['orderTypeName'],['tradeTypeName'],['ordTypeName'],['orderType'],['type']]){const raw=ccPick(o,keys);if(raw==='—')continue;const mapped=ccOrderTypeLabel(raw);if(String(raw)==='0')continue;if(mapped!=='—')return mapped}return ccOrderTypeLabel(ccPick(o,['tradeType','orderType','type']))}
function ccFuturesOrderTriggerText(o){const trigger=ccPick(o,['triggerCondition','triggerPrice','stopPrice','triggerPx']);if(trigger==='—')return '—';return Number.isFinite(Number(trigger))?ccFmtNumber(trigger,2):String(trigger)}
function ccFuturesOrderTpSlText(o){const tp=ccPick(o,['takeProfitPrice','tpPrice','takeProfit']);const sl=ccPick(o,['stopLossPrice','slPrice','stopLoss']);if(tp==='—'&&sl==='—')return '—';return ['TP '+tp,'SL '+sl].filter(x=>!x.endsWith('—')).join(' · ')||'—'}
function ccFuturesOrderDisplayId(o){const orderId=ccPick(o,['orderId','ordId','order_id','id']);if(orderId!=='—')return String(orderId);const clientOrderId=ccPick(o,['clientOrderId','clientOid','clOrdId']);return clientOrderId==='—'?'—':String(clientOrderId)}
function ccFmtSignedNumberOrDash(value,digits=2){const n=Number(value);if(!Number.isFinite(n))return '—';const text=Math.abs(n).toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits});return (n>0?'+':(n<0?'-':''))+text}
function ccFuturesOrderTriggerConditionsText(o){const expression=ccPick(o,['expression']);const trigger=ccPick(o,['triggerPrice','triggerPx','stopPrice','triggerCondition']);const triggerText=trigger==='—'?'—':(Number.isFinite(Number(trigger))?ccFmtNumber(trigger,2):String(trigger));if(expression==='—'&&triggerText==='—')return '—';if(expression!=='—'&&triggerText!=='—')return String(expression)+' '+triggerText;return expression!=='—'?String(expression):triggerText}
function ccFuturesOrderTifText(o){const raw=String(ccPick(o,['timeInForce','tif','execInst'])||'').trim();if(!raw||raw==='—')return '—';const key=raw.toUpperCase().replace(/[-\s]+/g,'_');return ({GTC:'GTC',IOC:'IOC',FOK:'FOK',POST_ONLY:'Post Only',POSTONLY:'Post Only',PO:'Post Only'})[key]||raw}
function ccFuturesOrderHistoryRow(o){const sideRaw=ccSpotOrderSideRaw(o);const sideStyle=sideRaw==='sell'?' style="color:#F04E60"':(sideRaw==='buy'?' style="color:#45C4A5"':'');return `<tr><td>${ccFuturesOrderTimeCell(o)}</td><td>${ccEsc(ccFuturesOrderSymbolText(o))}</td><td><span${sideStyle}>${ccEsc(ccOrderSide(o))}</span></td><td>${ccFuturesOrderAvgPriceCell(o)}</td><td>${ccFuturesOrderFilledAmountCell(o)}</td><td>${ccEsc(ccOrderStatusLabel(o))}</td><td>${ccEsc(ccFuturesOrderDisplayId(o))}</td><td>${ccEsc(ccOrderFees(o))}</td><td>${ccEsc(ccFmtSignedNumberOrDash(ccPick(o,['rpnl','realizedPnl','realizedPNL']),2))}</td><td>${ccEsc(ccFuturesOrderTypeLabel(o))}</td><td>${ccEsc(ccFmtBoolText(ccPick(o,['reduceOnly','isReduceOnly'])))}</td><td>${ccEsc(ccFuturesOrderTriggerConditionsText(o))}</td><td>${ccEsc(ccFuturesOrderTifText(o))}</td></tr>`}
function ccFuturesTradeAmountCell(t){const qty=Number(ccPick(t,['qty','quantity','amount','fillQty','filledQty']));const unit=ccFuturesOrderQtyUnit(t);return Number.isFinite(qty)?ccAssetAmountText(qty,unit):ccFirst(ccPick(t,['qty','quantity','amount','fillQty','filledQty']))}
function ccFuturesTradeValueCell(t){const qty=Number(ccPick(t,['qty','quantity','amount','fillQty','filledQty']));const px=Number(ccPick(t,['price','fillPrice','filledPrice','px']));return Number.isFinite(qty)&&Number.isFinite(px)?ccMoney(qty*px,'USDT'):ccFirst(ccPick(t,['value','filledValue','quoteQty','quoteAmount']))}
function ccFuturesTradePriceText(t,names){const value=ccPick(t,names);const num=Number(value);return Number.isFinite(num)?ccFmt(num,num>=100?2:6):ccFirst(value)}
function ccFuturesTradeRoleText(t){const isTaker=Number(ccPick(t,['isTaker']));if(isTaker===1)return 'Taker';if(isTaker===0)return 'Maker';return ccFirst(ccPick(t,['role','liquidity']))}
function ccTradeHistoryTimeMs(t){const raw=ccPick(t,['time','ts','createTime','createdTime','updateTime']);const n=Number(raw);if(Number.isFinite(n)&&n>0)return n<1e12?n*1000:n;const text=ccFirst(raw);if(!text||text==='—')return 0;const d=new Date(typeof text==='string'&&!/[zZ]$/.test(text)?text+'Z':text);const ms=d.getTime();return Number.isFinite(ms)?ms:0}
function ccTradeHistoryIsStale(t){const ms=ccTradeHistoryTimeMs(t);return !!(ms&&Math.abs(ms-Date.now())>120000)}
function ccTradeHistoryStaleClass(t){return ccTradeHistoryIsStale(t)?' class="cc-spreads-stale-trade-row"':''}
function ccTradeHistorySideClass(sideRaw){return sideRaw==='sell'?'cc-spreads-trade-side-sell':(sideRaw==='buy'?'cc-spreads-trade-side-buy':'')}
function ccTradeHistorySideHtml(t){const sideRaw=ccSpotOrderSideRaw(t);const stale=ccTradeHistoryIsStale(t);const color=sideRaw==='sell'?(stale?'#7f1d1d':'#F04E60'):(sideRaw==='buy'?(stale?'#166534':'#45C4A5'):'');const sideStyle=color?' style="color:'+color+(stale?' !important':'')+'"':'';const sideClass=ccTradeHistorySideClass(sideRaw);return '<span class="'+sideClass+'"'+sideStyle+'>'+ccEsc(ccOrderSide(t))+'</span>'}
function ccFuturesTradeHistoryRow(t){const orderId=ccPick(t,['orderId','ordId','clientOrderId']);const tradeId=ccPick(t,['tradeId','dealId','fillId','id']);return `<tr${ccTradeHistoryStaleClass(t)}><td>${ccEsc(ccFormatOrderTime(ccPick(t,['time','ts','createTime','createdTime','updateTime'])))}</td><td>${ccEsc(ccFuturesCanonicalDisplaySymbolFromOrder(t)||ccPick(t,['displaySymbol','displayName','symbol','instId']))}</td><td>${ccTradeHistorySideHtml(t)}</td><td>${ccEsc(ccFuturesTradeAmountCell(t))}</td><td>${ccEsc(ccFuturesTradeValueCell(t))}</td><td>${ccEsc(ccFuturesTradePriceText(t,['price','fillPrice','filledPrice','px']))}</td><td>${ccEsc(ccFuturesTradePriceText(t,['markPrice']))}</td><td>${ccEsc(ccFuturesTradePriceText(t,['indexPrice']))}</td><td>${ccEsc(ccTradeFeeText(t))}</td><td>${ccEsc(ccFmtSignedNumberOrDash(ccPick(t,['rpnl','realizedPnl','realizedPNL']),2))}</td><td>${ccEsc(ccFuturesTradeRoleText(t))}</td><td>${ccEsc(orderId)}｜${ccEsc(tradeId)}</td></tr>`}
function ccFuturesOrderRow(o,i){const payload=ccFuturesCancelPayload(o);const action=ccFuturesCancelActionHtml(o,i);const pending=CC_FUTURES_CANCEL_PENDING.has(ccFuturesCancelKey(o,payload));const sideRaw=ccSpotOrderSideRaw(o);const sideStyle=sideRaw==='sell'?' style="color:#F04E60"':(sideRaw==='buy'?' style="color:#45C4A5"':'');return `<tr${pending?' class="cc-futures-cancel-pending"':''}><td>${ccFuturesOrderTimeCell(o)}</td><td>${ccEsc(ccFuturesOrderSymbolText(o))}</td><td><span${sideStyle}>${ccEsc(ccOrderSide(o))}</span></td><td>${ccFuturesOrderAvgPriceCell(o)}</td><td>${ccFuturesOrderFilledAmountCell(o)}</td><td>${ccEsc(ccFmtNumber(ccPick(o,['initMargin','im','margin','orderMargin']),2))}</td><td>${ccEsc(ccFuturesOrderTypeLabel(o))}</td><td>${ccEsc(ccFmtBoolText(ccPick(o,['reduceOnly','isReduceOnly'])))}</td><td>${ccEsc(ccFuturesOrderTriggerText(o))}</td><td>${ccEsc(ccFuturesOrderTpSlText(o))}</td><td>${action}</td></tr>`}
function ccCoincallFundingRowsFrom(res){const scan=value=>{if(Array.isArray(value))return value;if(!value||typeof value!=='object')return [];for(const key of ['list','rows','items','records','result']){if(Array.isArray(value[key]))return value[key]}if(Array.isArray(value.data))return value.data;const nested=scan(value.data);if(nested.length)return nested;if(value.symbol||value.fundFee!==undefined||value.fundRate!==undefined)return [value];return []};return scan(res&&res.data!==undefined?res.data:res)}function ccFundingRateText(v){const n=Number(v);if(!Number.isFinite(n))return '—';const pct=Math.abs(n)<=1?n*100:n;return ccFmtNumber(pct,4)+'%'}function ccFuturesFundingRowHtml(r){const t=r.ctime?ccUtcDateTime(Number(r.ctime)):'—';const sym2=ccEsc(ccFuturesCanonicalDisplaySymbolFromOrder(r)||r.symbol||r.displayName||'—');const sideStyle=r.tradeSide===1||r.tradeSide==='1'?' style="color:#45C4A5"':r.tradeSide===2||r.tradeSide==='2'?' style="color:#F04E60"':'';const side=r.tradeSide===1||r.tradeSide==='1'?'<span class=side-buy'+sideStyle+'>Buy</span>':r.tradeSide===2||r.tradeSide==='2'?'<span class=side-sell'+sideStyle+'>Sell</span>':'—';const amt=r.qty!=null?ccEsc(String(r.qty)):'—';const funding=r.fundFee!=null?ccEsc(String(r.fundFee)):'—';const rate=r.fundRate!=null?ccEsc(ccFundingRateText(r.fundRate)):'—';return '<tr><td style="color:#f8fafc">'+t+'</td><td style="color:#f8fafc">'+sym2+'</td><td>'+side+'</td><td style="color:#f8fafc">'+amt+'</td><td style="color:#f8fafc">'+funding+'</td><td class=muted>—</td><td style="color:#f8fafc">'+rate+'</td></tr>'}function ccFuturesFundingSymbols(sym){const out=[];const seen=new Set();const push=v=>{const s=String(v||'').trim();if(!s||seen.has(s))return;seen.add(s);out.push(s)};push(sym);const opt=cc('ccFuturesInst')?.selectedOptions?.[0];const label=String(opt&&opt.textContent||'').trim();push(label);if(label)push(label.replace(/\s+/g,'-'));const upper=String(sym||'').trim().toUpperCase();if(/USD$/.test(upper)&&!/USDT$/.test(upper)){const usdt=upper.replace(/USD$/,'USDT');push(usdt);push(usdt+'-Perp');push(usdt+' Perp')}if(label&&/PERP/i.test(label)){push(label.replace(/PERP/i,'').trim());push(label.replace(/\s*PERP\s*/ig,'-Perp').trim())}return out}function ccRefreshActiveFuturesPanel(){if(ccActiveLayer()!=='trade'||ccTradeActiveMode()!=='futures')return;const panel=ccActiveBottomPanel('futures');if(panel==='funding')loadCcFuturesFundingHistory();if(panel==='order-history')loadCcFuturesOrderHistory();if(panel==='trade-history')loadCcFuturesTradeHistory();if(panel==='open-orders')loadCcFuturesOpenOrders();if(panel==='open-positions')loadCcFuturesPositions()}async function ccLoadFuturesFundingHistoryBrowser(sym){let lastErr=null;const symbols=ccFuturesFundingSymbols(sym);for(const symbol of symbols){const params={symbol,pageSize:50,page:1};for(const uri of ['/open/settle/future/latestRecord/v1','/settle/future/latestRecord/v1','/open/settle/future/record/v1','/settle/future/record/v1']){try{const res=await ccCoincallBrowserApi(uri,{method:'GET',params});const rows=ccCoincallFundingRowsFrom(res);if(rows.length)return rows}catch(err){lastErr=err}}}if(lastErr)throw lastErr;return []}async function loadCcFuturesFundingHistory(){const panel=document.querySelector('[data-cc-futures-panel-content=funding]');const sym=cc('ccFuturesInst')?.value;if(!sym){if(panel)panel.innerHTML='<div class=table-wrap><table><thead><tr><th>Time</th><th>Symbol</th><th>Side</th><th>Amount</th><th>Funding</th><th>Fees</th><th>Funding Rate</th></tr></thead><tbody><tr><td class=muted colspan=7>Select a futures symbol to load funding history.</td></tr></tbody></table></div>';return}let list=[];let lastError=null;try{const params=new URLSearchParams();const sel=cc('ccAccountSelect');if(sel&&sel.value)params.set('accountId',sel.value);params.set('symbol',sym);params.set('pageSize','50');const qs=params.toString()?'?'+params.toString():'';const res=await ccApi('/api/admin/coincall/futures/funding/history'+qs);list=ccCoincallFundingRowsFrom(res)}catch(e){lastError=e}if(!list.length){try{list=await ccLoadFuturesFundingHistoryBrowser(sym);lastError=null}catch(e){lastError=lastError||e}}const rows=list.map(ccFuturesFundingRowHtml);if(panel)panel.innerHTML='<div class=table-wrap><table><thead><tr><th>Time</th><th>Symbol</th><th>Side</th><th>Amount</th><th>Funding</th><th>Fees</th><th>Funding Rate</th></tr></thead><tbody>'+(rows.length?rows.join(''):(lastError?'<tr><td class=muted colspan=7>Funding history failed: '+ccEsc(lastError.message)+'</td></tr>':'<tr><td class=muted colspan=7>No CoinCall futures funding history returned.</td></tr>'))+'</tbody></table></div>'}
let ccLastAssetsFundingRows=[];let ccLastAssetsFunding1mRows=[];let ccAssetsFunding1mDays=3;let ccAssetsFunding1mChart=null;let ccAssetsFunding1mResizeObserver=null;let ccAssetsFunding1mRefreshBusy=false;function ccSyncAssetsFundingSymbolSelect(){const sel=cc('ccAssetsFundingSymbol');if(!sel)return;const source=cc('ccFuturesInst');const prev=String(sel.value||ccUiStateLoad().assetsFundingSymbol||source?.value||'').trim();let options=source?Array.from(source.options||[]).map(opt=>({value:String(opt.value||'').trim(),label:String(opt.textContent||opt.value||'').trim()})).filter(x=>x.value):[];if(!options.length)options=[{value:'BTCUSD',label:'BTCUSDT-Perp'},{value:'ETHUSD',label:'ETHUSDT-Perp'}];sel.innerHTML=options.map(x=>'<option value="'+ccEsc(x.value)+'">'+ccEsc(x.label||x.value)+'</option>').join('');const target=options.some(x=>x.value===prev)?prev:(source?.value&&options.some(x=>x.value===source.value)?source.value:options[0].value);sel.value=target||''}
function ccSyncAssetsFunding1mSymbolSelect(){const src=cc('ccAssetsFundingSymbol'),sel=cc('ccAssetsFunding1mSymbol');if(!sel)return;ccSyncAssetsFundingSymbolSelect();const prev=String(sel.value||ccUiStateLoad().assetsFunding1mSymbol||src?.value||'').trim();const opts=src?Array.from(src.options||[]).map(opt=>({value:String(opt.value||'').trim(),label:String(opt.textContent||opt.value||'').trim()})).filter(x=>x.value):[{value:'BTCUSD',label:'BTCUSDT-Perp'},{value:'ETHUSD',label:'ETHUSDT-Perp'}];sel.innerHTML=opts.map(x=>'<option value="'+ccEsc(x.value)+'">'+ccEsc(x.label||x.value)+'</option>').join('');const target=opts.some(x=>x.value===prev)?prev:(src?.value&&opts.some(x=>x.value===src.value)?src.value:opts[0]?.value||'');sel.value=target;if(src&&target)src.value=target;ccAssetsFunding1mUpdateTitle()}
function ccAssetsFunding1mUpdateTitle(){const sel=cc('ccAssetsFunding1mSymbol'),title=cc('ccAssetsFunding1mTitle');if(title)title.textContent=String(sel?.selectedOptions?.[0]?.textContent||sel?.value||'—')}
function ccAssetsFundingPercentValue(v){const n=Number(v);if(!Number.isFinite(n))return NaN;return Math.abs(n)<=1?n*100:n}
function ccAssetsFundingRateValue(r){return ccAssetsFundingPercentValue(r&&(r.currentFunding??r.fundRate??r.current_funding))}
function ccAssetsFundingInterest8hValue(r){return ccAssetsFundingPercentValue(r&&(r.interest8h??r.interest_8h))}
function ccAssetsFundingTimeValue(r){const raw=r&&((r.ctime!==undefined&&r.ctime!==null?r.ctime:r.time)!==undefined&&((r.ctime!==undefined&&r.ctime!==null?r.ctime:r.time)!==null)?(r.ctime!==undefined&&r.ctime!==null?r.ctime:r.time):(r.ts!==undefined&&r.ts!==null?r.ts:(r.createTime!==undefined&&r.createTime!==null?r.createTime:(r.recordTimeUtc||r.observedMinuteUtc))));const n=Number(raw);if(Number.isFinite(n))return n;const d=new Date(typeof raw==='string'&&!/[zZ]$/.test(raw)?raw+'Z':raw);return Number.isNaN(d.getTime())?NaN:d.getTime()}
function ccSetAssetsFundingStatus(textValue){const st=cc('ccAssetsFundingStatus');if(st)st.textContent=textValue||''}
function ccDrawAssetsFundingChart(rows){const canvas=cc('ccAssetsFundingChart'),wrap=canvas&&canvas.parentElement;if(!canvas||!wrap)return;const dpr=window.devicePixelRatio||1,w=Math.max(320,wrap.clientWidth||canvas.clientWidth||900),h=Number(canvas.getAttribute('height'))||280;canvas.style.width=w+'px';canvas.width=Math.floor(w*dpr);canvas.height=Math.floor(h*dpr);const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);ctx.fillStyle='#050b16';ctx.fillRect(0,0,w,h);const points=(Array.isArray(rows)?rows:[]).map(r=>({t:ccAssetsFundingTimeValue(r),v:ccAssetsFundingRateValue(r)})).filter(p=>Number.isFinite(p.t)&&Number.isFinite(p.v)).sort((a,b)=>a.t-b.t);if(!points.length)return;const padL=46,padR=16,padT=18,padB=34,chartW=Math.max(1,w-padL-padR),chartH=Math.max(1,h-padT-padB),vals=points.map(p=>p.v),rawMin=Math.min(...vals),rawMax=Math.max(...vals),spanRaw=rawMax-rawMin||Math.max(Math.abs(rawMax)*0.2,0.01),min=rawMin-spanRaw*0.16,max=rawMax+spanRaw*0.16,span=max-min||1,x=i=>padL+(points.length===1?chartW/2:i*chartW/(points.length-1)),y=v=>padT+(max-v)/span*chartH;ctx.strokeStyle='rgba(42,46,53,.8)';ctx.lineWidth=1;ctx.font='12px Segoe UI';ctx.textBaseline='middle';ctx.textAlign='right';for(let i=0;i<5;i++){const yy=Math.round(padT+chartH*i/4)+.5,val=max-span*i/4;ctx.beginPath();ctx.moveTo(padL,yy);ctx.lineTo(w-padR,yy);ctx.stroke();ctx.fillStyle='#8A8F98';ctx.fillText(ccFmtNumber(val,4)+'%',padL-8,yy)}const zeroY=y(0);if(zeroY>=padT&&zeroY<=padT+chartH){ctx.strokeStyle='rgba(148,163,184,.6)';ctx.beginPath();ctx.moveTo(padL,Math.round(zeroY)+.5);ctx.lineTo(w-padR,Math.round(zeroY)+.5);ctx.stroke()}ctx.strokeStyle='#60a5fa';ctx.lineWidth=2;ctx.beginPath();points.forEach((p,i)=>{const xx=x(i),yy=y(p.v);if(i)ctx.lineTo(xx,yy);else ctx.moveTo(xx,yy)});ctx.stroke();const last=points[points.length-1],lastX=x(points.length-1),lastY=y(last.v);ctx.fillStyle=last.v>=0?'#45C4A5':'#F04E60';ctx.beginPath();ctx.arc(lastX,lastY,3.5,0,Math.PI*2);ctx.fill();ctx.textAlign='center';ctx.textBaseline='alphabetic';ctx.fillStyle='#8A8F98';const ticks=Math.min(4,points.length);for(let i=0;i<ticks;i++){const idx=ticks===1?0:Math.round(i*(points.length-1)/(ticks-1));ctx.fillText(ccUtcHm(points[idx].t),x(idx),h-12)}}
function ccRenderAssetsFundingHistory(rows,error){const body=cc('ccAssetsFundingHistoryBody'),meta=cc('ccAssetsFundingMeta');const list=Array.isArray(rows)?rows:[];ccLastAssetsFundingRows=list;if(body){if(list.length)body.innerHTML=list.map(ccFuturesFundingRowHtml).join('');else body.innerHTML='<tr><td class=muted colspan=7>'+(error?'Funding history failed: '+ccEsc(error.message||String(error)):'No CoinCall futures funding history returned.')+'</td></tr>'}if(meta){const rates=list.map(ccAssetsFundingRateValue).filter(Number.isFinite),latest=rates.length?rates[rates.length-1]:NaN;meta.textContent=list.length?'Rows: '+list.length+(Number.isFinite(latest)?' · latest rate '+ccFmtNumber(latest,4)+'%':''):''}ccSetAssetsFundingStatus(list.length?'':(error?'Funding history failed: '+(error.message||String(error)):'No CoinCall futures funding history returned.'));ccDrawAssetsFundingChart(list)}
async function loadCcAssetsFundingHistory(){ccSyncAssetsFundingSymbolSelect();const sym=String(cc('ccAssetsFundingSymbol')?.value||cc('ccFuturesInst')?.value||'').trim();if(!sym){ccRenderAssetsFundingHistory([],new Error('Select a futures symbol to load funding history.'));return}ccUiStateSave({assetsFundingSymbol:sym});ccSetAssetsFundingStatus('Loading funding history...');let dbError=null;try{const dbParams=new URLSearchParams();dbParams.set('symbol',sym);dbParams.set('limit','500');const dbRes=await ccApi('/api/admin/coincall/futures/funding/history/db?'+dbParams.toString());const dbRows=ccCoincallFundingRowsFrom(dbRes);if(dbRows.length){ccRenderAssetsFundingHistory(dbRows,null);return}}catch(e){dbError=e}try{const params=new URLSearchParams();const sel=cc('ccAccountSelect');if(sel&&sel.value)params.set('accountId',sel.value);params.set('symbol',sym);params.set('pageSize','100');const res=await ccApi('/api/admin/coincall/futures/funding/history?'+params.toString());const rows=ccCoincallFundingRowsFrom(res);ccRenderAssetsFundingHistory(rows,null)}catch(e){ccRenderAssetsFundingHistory([],dbError||e)}}
function ccAssetsFunding1mStatus(textValue){const container=cc('ccAssetsFunding1mChart');if(container)container.innerHTML='<div class="cc-assets-funding-1m-status" id="ccAssetsFunding1mStatus">'+ccEsc(textValue||'')+'</div>'}
function ccAssetsFunding1mSeries(rows,valueFn){const cutoff=Date.now()-ccAssetsFunding1mDays*24*60*60*1000;return (Array.isArray(rows)?rows:[]).map(r=>({time:Math.floor(ccAssetsFundingTimeValue(r)/1000),value:valueFn(r)})).filter(p=>Number.isFinite(p.time)&&Number.isFinite(p.value)&&p.time*1000>=cutoff).sort((a,b)=>a.time-b.time).filter((p,i,a)=>i===0||p.time!==a[i-1].time)}
function ccAssetsFunding1mPoints(rows){return ccAssetsFunding1mSeries(rows,ccAssetsFundingRateValue)}
function ccAssetsFunding1mUpdateLegend(currentPoints,interestPoints){const items=document.querySelectorAll('.cc-assets-funding-legend .cc-assets-funding-legend-item');if(items[0])items[0].hidden=!currentPoints.length;if(items[1])items[1].hidden=!interestPoints.length}
function ccFundingSettlementInterestRow(row){const t=ccAssetsFundingTimeValue(row);let rate=Number(row&&(row.fundRate??row.currentFunding??row.fund_rate));if(!Number.isFinite(rate)){const pct=Number(row&&row.current_funding);if(Number.isFinite(pct))rate=pct/100}if(!Number.isFinite(t)||!Number.isFinite(rate))return null;return {ctime:t,symbol:row.symbol,displayName:row.displayName,interest8h:rate,interest_8h:null,source:'coincall-settled-funding-history'}}
function ccRenderAssetsFunding1m(rows,error){const meta=cc('ccAssetsFunding1mMeta'),container=cc('ccAssetsFunding1mChart'),currentPoints=ccAssetsFunding1mSeries(rows,ccAssetsFundingRateValue),interestPoints=ccAssetsFunding1mSeries(rows,ccAssetsFundingInterest8hValue),points=currentPoints.length?currentPoints:interestPoints;ccLastAssetsFunding1mRows=Array.isArray(rows)?rows:[];ccAssetsFunding1mUpdateLegend(currentPoints,interestPoints);if(meta){const latest=currentPoints[currentPoints.length-1],latestInterest=interestPoints[interestPoints.length-1];meta.textContent=points.length?'Rows: '+points.length+' · range '+ccAssetsFunding1mDays+'D'+(latest?' · Current Funding '+ccFmtNumber(latest.value,6)+'%':'')+(latestInterest?' · Interest 8h '+ccFmtNumber(latestInterest.value,6)+'%':'')+(latest?' · '+ccUtcDateTime(latest.time*1000):latestInterest?' · '+ccUtcDateTime(latestInterest.time*1000):''):(error?'Funding chart failed: '+(error.message||String(error)):'No CoinCall persisted funding rows for this range.')}if(!container)return;if(!points.length){if(ccAssetsFunding1mChart){ccAssetsFunding1mChart.remove();ccAssetsFunding1mChart=null}ccAssetsFunding1mStatus(error?'Funding chart failed: '+(error.message||String(error)):'No CoinCall persisted funding rows for this range.');return}if(typeof LightweightCharts==='undefined'){ccAssetsFunding1mStatus('Funding chart library is unavailable.');return}container.innerHTML='';if(ccAssetsFunding1mChart){ccAssetsFunding1mChart.remove();ccAssetsFunding1mChart=null}const chart=LightweightCharts.createChart(container,{layout:{background:{color:'#0b1220'},textColor:'#94a3b8'},grid:{vertLines:{color:'#1e293b'},horzLines:{color:'#1e293b'}},crosshair:{mode:LightweightCharts.CrosshairMode.Normal},rightPriceScale:{borderColor:'#334155'},timeScale:{borderColor:'#334155',timeVisible:true,secondsVisible:false},width:container.clientWidth,height:340,localization:{priceFormatter:p=>p.toFixed(6)+'%'}});ccAssetsFunding1mChart=chart;if(currentPoints.length)chart.addLineSeries({color:'#f59e0b',lineWidth:2,title:'',priceLineVisible:false,lastValueVisible:true}).setData(currentPoints);if(interestPoints.length)chart.addLineSeries({color:'#818cf8',lineWidth:1,priceLineVisible:false,lastValueVisible:true}).setData(interestPoints);chart.timeScale().fitContent();if(ccAssetsFunding1mResizeObserver)ccAssetsFunding1mResizeObserver.disconnect();ccAssetsFunding1mResizeObserver=new ResizeObserver(()=>{if(ccAssetsFunding1mChart)ccAssetsFunding1mChart.applyOptions({width:container.clientWidth})});ccAssetsFunding1mResizeObserver.observe(container)}
async function loadCcAssetsFunding1m(){ccSyncAssetsFunding1mSymbolSelect();const sym=String(cc('ccAssetsFunding1mSymbol')?.value||cc('ccAssetsFundingSymbol')?.value||cc('ccFuturesInst')?.value||'').trim();if(!sym){ccRenderAssetsFunding1m([],new Error('Select a futures symbol to load 1M funding.'));return}ccUiStateSave({assetsFunding1mSymbol:sym});ccAssetsFunding1mUpdateTitle();ccAssetsFunding1mStatus('Loading 1M funding minute snapshots...');try{const dbParams=new URLSearchParams();dbParams.set('symbol',sym);dbParams.set('limit','5000');const histParams=new URLSearchParams();histParams.set('symbol',sym);histParams.set('limit','500');const [dbRes,histRes]=await Promise.all([ccApi('/api/admin/coincall/futures/funding/minute?'+dbParams.toString()),ccApi('/api/admin/coincall/futures/funding/history/db?'+histParams.toString()).catch(()=>null)]);const minuteRows=ccCoincallFundingRowsFrom(dbRes).map(r=>({...r,interest8h:null,interest_8h:null}));const settlementRows=ccCoincallFundingRowsFrom(histRes).map(ccFundingSettlementInterestRow).filter(Boolean);ccRenderAssetsFunding1m(minuteRows.concat(settlementRows),null)}catch(e){ccRenderAssetsFunding1m([],e)}}
async function loadCcFuturesOpenOrders(){const seq=++CC_FUTURES_ORDER_REFRESH.seq,startedAt=Date.now();try{const params=new URLSearchParams();const sel=cc('ccAccountSelect');if(sel&&sel.value)params.set('accountId',sel.value);const qs=params.toString()?'?'+params.toString():'';const res=await ccApi('/api/admin/coincall/futures/orders/open'+qs);ccApplyFuturesOpenOrdersResponse(res,seq,startedAt);ccEnsureFuturesPrivateSocket().catch(()=>{})}catch(e){if(startedAt<CC_FUTURES_ORDER_REFRESH.lastAppliedAt)return;if(Array.isArray(ccLastFuturesOpenOrders)&&ccLastFuturesOpenOrders.length)return;ccFuturesOpenOrdersLoaded=false;document.querySelector('[data-cc-futures-panel="open-orders"]')?.replaceChildren(document.createTextNode('Orders ('+(Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders.length:0)+')'));const panel=document.querySelector('[data-cc-futures-panel-content="open-orders"]');if(panel)panel.innerHTML=ccBottomTableAlways(['Time','Symbol','Side','Average｜Price','Filled｜Amount','IM','Type','Reduce Only','Trigger Conditions','TP/SL','Actions'],[],'CoinCall futures open orders failed: '+e.message,11);drawCcChart('ccFuturesChart')}}
async function loadCcFuturesOrderHistory(){try{const params=new URLSearchParams();const sel=cc('ccAccountSelect');if(sel&&sel.value)params.set('accountId',sel.value);params.set('pageSize','100');const qs=params.toString()?'?'+params.toString():'';const res=await ccApi('/api/admin/coincall/futures/orders/history'+qs);ccLastFuturesOrderHistory=ccSpotOpenOrdersFrom(res);ccRenderFuturesBottomPanels()}catch(e){ccLastFuturesOrderHistory=[];document.querySelector('[data-cc-futures-panel="order-history"]')?.replaceChildren(document.createTextNode('Order history (0)'));const panel=document.querySelector('[data-cc-futures-panel-content="order-history"]');if(panel)panel.innerHTML=ccBottomTableAlways(['Time','Symbol','Side','Average｜Price','Filled｜Amount','Status','Order ID','Fees','Realized PnL','Type','Reduce Only','Trigger Conditions','TIF'],[],'CoinCall futures order history failed: '+e.message,13)}}
async function loadCcFuturesTradeHistory(opts={}){try{const params=new URLSearchParams();const sel=cc('ccAccountSelect');if(sel&&sel.value)params.set('accountId',sel.value);params.set('pageSize','100');const qs=params.toString()?'?'+params.toString():'';const res=await ccApi('/api/admin/coincall/futures/trades/history'+qs);const prev=ccLastFuturesTradeHistory;const next=ccSpotOpenOrdersFrom(res);if(opts.notify!==false)ccMaybeNotifyFuturesFills(prev,next);ccLastFuturesTradeHistory=next;ccRenderFuturesBottomPanels();return next}catch(e){if(opts&&opts.notify===false){ccRenderFuturesBottomPanels();throw e}ccLastFuturesTradeHistory=[];document.querySelector('[data-cc-futures-panel="trade-history"]')?.replaceChildren(document.createTextNode('Trade history (0)'));const panel=document.querySelector('[data-cc-futures-panel-content="trade-history"]');if(panel)panel.innerHTML=ccBottomTableAlways(['Time','Symbol','Side','Amount','Value','Filled Price','Mark Price','Index','Order ID｜Trade ID','Fees','Realized PnL','Role'],[],'CoinCall futures trade history failed: '+e.message,12);return ccLastFuturesTradeHistory}}
let ccFuturesOpenOrdersSyncTimer=null;
function ccStartFuturesOpenOrdersSync(){if(ccFuturesOpenOrdersSyncTimer)return;ccFuturesOpenOrdersSyncTimer=setInterval(()=>{if(ccActiveMarket()==='futures'&&!document.hidden){ccEnsureFuturesPrivateSocket().catch(()=>{});if(!ccFuturesPrivateSocketOpen())loadCcFuturesOpenOrders()}},15000)}
function ccSpotOrderHistoryRow(o){const base=ccOrderBaseAsset(o);const avg=ccPick(o,['avgPrice','averagePrice','avgPx']);const price=ccPick(o,['price','px']);const filled=ccPick(o,['fillQty','filledQty','filledQuantity','filled']);const amount=ccPick(o,['qty','quantity','amount','remainQty']);const filledText=Number.isFinite(Number(filled))?ccFormatAssetAmount(filled,base):filled;const amountText=Number.isFinite(Number(amount))?ccFormatAssetAmount(amount,base):amount;return `<tr><td>${ccEsc(ccFormatOrderTime(ccPick(o,['ts','createTime','createdTime','time','updateTime'])))}</td><td>${ccEsc(ccPick(o,['displaySymbol','symbol','instId']))}</td><td>${ccEsc(ccOrderSide(o))}</td><td>${ccEsc(avg)}｜${ccEsc(price)}</td><td>${ccEsc(filledText)}｜${ccEsc(amountText)}</td><td>${ccEsc(ccOrderValue(o))}</td><td>${ccEsc(ccOrderStatusLabel(o))}</td><td>${ccEsc(ccSpotOrderDisplayId(o))}</td><td>${ccEsc(ccOrderFees(o))}</td><td>${ccEsc(ccSpotOrderTypeLabel(o))}</td></tr>`}
function ccSpotTradeRow(t){const parts=ccSpotParts();const qty=Number(ccPick(t,['qty','quantity','amount','fillQty','filledQty']));const px=ccSpotTradePrice(t);const value=Number.isFinite(qty)&&Number.isFinite(px)?ccMoney(qty*px,parts.quote):ccFirst(ccPick(t,['value','filledValue','quoteQty','quoteAmount']));const amount=Number.isFinite(qty)?ccAssetAmountText(qty,parts.base):ccFirst(ccPick(t,['qty','quantity','amount','fillQty','filledQty']));const orderId=ccPick(t,['orderId','ordId','clientOrderId']);const tradeId=ccPick(t,['tradeId','dealId','fillId','id']);const role=Number(ccPick(t,['isTaker']))===1?'Taker':(Number(ccPick(t,['isTaker']))===0?'Maker':ccFirst(ccPick(t,['role','liquidity'])));return `<tr><td>${ccEsc(ccFormatOrderTime(ccSpotTradeTs(t)))}</td><td>${ccEsc(ccPick(t,['displaySymbol','symbol','instId']))}</td><td>${ccEsc(ccOrderSide(t))}</td><td>${ccEsc(amount)}</td><td>${ccEsc(value)}</td><td>${ccEsc(Number.isFinite(px)?ccFmt(px,px>100?2:6):ccFirst(ccPick(t,['price','fillPrice','filledPrice','px'])))}</td><td>${ccEsc(orderId)}｜${ccEsc(tradeId)}</td><td>${ccEsc(ccTradeFeeText(t))}</td><td>${ccEsc(role||'—')}</td></tr>`}
async function loadCcSpotOpenOrders(reason='spot-open-orders-reconcile'){CC_SPOT_TRADE_REFRESH.openOrdersLastAttemptAt=Date.now();ccEnsureSpotPrivateSocket().catch(()=>{});try{const sel=cc('ccAccountSelect');const params=new URLSearchParams();if(sel&&sel.value)params.set('accountId',sel.value);params.set('restNotify','spot-external-cancel-reconcile');params.set('restReason',String(reason||'spot-open-orders-reconcile'));params.set('restPage','CoinCall Spread Workstation');const restSymbol=String(cc('ccSpotInst')?.value||'').trim();if(restSymbol)params.set('restSymbol',restSymbol);const res=await ccApi('/api/admin/coincall/spot/orders/open?'+params.toString());return ccApplySpotOpenOrdersResponse(res,reason)}catch(e){if(Array.isArray(ccLastSpotOpenOrders)&&ccLastSpotOpenOrders.length){ccRenderSpotBottomPanels();drawCcChart('ccSpotChart');return ccLastSpotOpenOrders}ccLastSpotOpenOrders=[];const panel=document.querySelector('[data-cc-spot-panel-content="open-orders"]');if(panel)panel.innerHTML='<div class="okx-nitro-empty-state">CoinCall open orders failed: '+ccEsc(e.message)+'</div>';document.querySelector('[data-cc-spot-panel="open-orders"]')?.replaceChildren(document.createTextNode('Open orders (0)'));drawCcChart('ccSpotChart');return ccLastSpotOpenOrders}}
async function loadCcSpotOrderHistory(){try{const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';const res=await ccApi('/api/admin/coincall/spot/orders/history'+qs);ccLastSpotOrderHistory=ccSpotOpenOrdersFrom(res);ccRenderSpotBottomPanels()}catch(e){ccLastSpotOrderHistory=[];const panel=document.querySelector('[data-cc-spot-panel-content="order-history"]');if(panel)panel.innerHTML='<div class="okx-nitro-empty-state">CoinCall order history failed: '+ccEsc(e.message)+'</div>'}}
async function loadCcSpotTradeHistory(){try{const params=new URLSearchParams();const sel=cc('ccAccountSelect');if(sel&&sel.value)params.set('accountId',sel.value);const sym=cc('ccSpotInst')?.value;if(sym)params.set('symbol',sym);const qs=params.toString()?'?'+params.toString():'';const res=await ccApi('/api/admin/coincall/spot/trades/history'+qs);const prev=ccLastSpotTradeHistory;const next=ccSpotOpenOrdersFrom(res);ccMaybeNotifySpotFills(prev,next);ccLastSpotTradeHistory=next;ccRenderSpotBottomPanels();if(CC_BOOK_STATE.raw.spot&&CC_BOOK_STATE.transport.spot===ccBookTransportSymbol('spot'))ccRenderBook('spot',CC_BOOK_STATE.raw.spot);drawCcChart('ccSpotChart')}catch(e){ccLastSpotTradeHistory=[];const panel=document.querySelector('[data-cc-spot-panel-content="trade-history"]');if(panel)panel.innerHTML='<div class="okx-nitro-empty-state">CoinCall trade history failed: '+ccEsc(e.message)+'</div>';if(CC_BOOK_STATE.raw.spot&&CC_BOOK_STATE.transport.spot===ccBookTransportSymbol('spot'))ccRenderBook('spot',CC_BOOK_STATE.raw.spot);drawCcChart('ccSpotChart')}}
function ccRenderSpotBottomPanels(){ccRenderSpreadsPositionsOrdersTable();const rows=ccBalanceRows(ccLastAssetsSummary||{});const orders=Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders:[];const panel=(name)=>document.querySelector(`[data-cc-spot-panel-content="${name}"]`);document.querySelector('[data-cc-spot-panel="open-orders"]')?.replaceChildren(document.createTextNode('Open orders ('+orders.length+')'));document.querySelector('[data-cc-spot-panel="open-positions"]')?.replaceChildren(document.createTextNode('Open positions (0)'));const orderRows=orders.map((o,i)=>ccSpotOrderRow(o,i));const assetRows=rows.filter(ccIsNonZeroTradingBalance).map(r=>{const equity=ccRowEquityValue(r),available=ccCoincallAssetAvailableValue(r);return `<tr><td>${ccEsc(r.ccy)}</td><td>${ccEsc(ccMoney(equity,r.ccy))}</td><td>${ccEsc(ccMoney(available,r.ccy))}</td><td>${ccEsc(ccMoney(r.borrowed??0,r.ccy))}</td><td class="muted">—</td></tr>`});const ordersPanel=panel('open-orders');if(ordersPanel)ordersPanel.innerHTML=ccBottomTable(['Time','Symbol','Side','Price','Filled｜Amount','Filled｜Value','Type','Actions'],orderRows,'No CoinCall open orders returned.');const histPanel=panel('order-history');if(histPanel)histPanel.innerHTML=ccBottomTable(['Time','Symbol','Side','Average｜Price','Filled｜Amount','Filled｜Value','Status','Order ID','Fees','Type'],(Array.isArray(ccLastSpotOrderHistory)?ccLastSpotOrderHistory:[]).map(ccSpotOrderHistoryRow),'No CoinCall order history returned.');const pos=panel('open-positions');if(pos)pos.innerHTML=ccBottomTable(['Symbol','Amount','Entry Price','Mark Price','ELP','PnL (ROI)','IM','MM','Delta','Gamma','Vega','Theta','Rho','Reverse','Close Positions','Price','Amount','TP/SL'],[],'No CoinCall spot positions returned.');const tradeHist=panel('trade-history');if(tradeHist)tradeHist.innerHTML=ccBottomTable(['Time','Symbol','Side','Amount','Value','Filled Price','Order ID｜Trade ID','Fees','Role'],(Array.isArray(ccLastSpotTradeHistory)?ccLastSpotTradeHistory:[]).map(ccSpotTradeRow),'No CoinCall trade history returned.');const assets=panel('assets');if(assets)assets.innerHTML=ccBottomTable(['Coin','Equity','Available Balance','Borrowed Amount','Actions'],assetRows,'No CoinCall assets returned by the assets summary.')}

let ccLastPnlData=null;let ccEstimateRange='1W';let ccEstimateCurrency='USD';let ccBtcUsdtPrice=null;let ccBtcPriceLoading=false;
function ccRenderEstimateSparkline(points){if(!points||points.length<2)return '<div class="estimate-chart-empty">No PnL chart data yet</div>';const w=420,h=150,pad=10,vals=points.map(p=>p.v),min=Math.min(...vals),max=Math.max(...vals),span=max-min||1;const poly=points.map((p,i)=>{const x=pad+(w-pad*2)*(i/(points.length-1)),y=h-pad-((p.v-min)/span)*(h-pad*2);return x.toFixed(1)+','+y.toFixed(1)}).join(' ');const stroke=points[points.length-1].v>=points[0].v?'#22c55e':'#f43f5e';return `<svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-label="${ccEsc(ccEstimateRange)} PnL chart"><polyline fill="none" stroke="${stroke}" stroke-width="2.2" points="${poly}"/></svg>`}
function ccEstimateRate(currency){const c=String(currency||'USD').toUpperCase();if(c==='USD'||c==='USDT'||c==='USDC')return 1;const px=Number(ccBtcUsdtPrice);if(c==='BTC'&&Number.isFinite(px)&&px>0)return px;return NaN}
function ccConvertEstimateValue(v,source,target){const n=Number(v);if(!Number.isFinite(n))return NaN;if(n===0)return 0;const sr=ccEstimateRate(source),tr=ccEstimateRate(target);return Number.isFinite(sr)&&sr>0&&Number.isFinite(tr)&&tr>0?n*sr/tr:NaN}
function ccFindKlineClose(node){if(Array.isArray(node)){for(let i=node.length-1;i>=0;i--){const child=ccFindKlineClose(node[i]);if(Number.isFinite(child))return child}if(node.length>=5){const n=Number(node[4]);if(Number.isFinite(n)&&n>0)return n}}else if(node&&typeof node==='object'){for(const k of ['close','closePrice','c','last','lastPrice','price']){const n=Number(node[k]);if(Number.isFinite(n)&&n>0)return n}for(const v of Object.values(node)){const child=ccFindKlineClose(v);if(Number.isFinite(child))return child}}return NaN}
let ccBtcPricePromise=null;async function ccFetchBtcUsdtPrice(force=false){if(!force&&Number.isFinite(Number(ccBtcUsdtPrice)))return ccBtcUsdtPrice;if(ccBtcPricePromise&&!force)return ccBtcPricePromise;ccBtcPriceLoading=true;ccBtcPricePromise=(async()=>{try{const res=await fetch('/coincall-public-api/open/spot/market/klines?symbol=BTCUSDT&interval=1min&limit=1',{cache:'no-store'});if(!res.ok)throw new Error('HTTP '+res.status);const data=await res.json();const px=ccFindKlineClose(data);if(Number.isFinite(px)&&px>0){ccBtcUsdtPrice=px;return px}throw new Error('BTCUSDT close not found')}catch(e){console.warn('CoinCall BTCUSDT price fetch failed',e);return NaN}finally{ccBtcPriceLoading=false;ccBtcPricePromise=null}})();return ccBtcPricePromise}
function ccRerenderEstimatedTotalValue(){const rows=ccBalanceRows(ccLastAssetsSummary),totalNow=ccSumUsd(rows),account=ccLastAssetsSummary&&ccLastAssetsSummary.account,grouped=ccClassifyBalances(rows);ccRenderEstimatedTotalValue(totalNow,account&&account.name?account.name:'—');ccRenderPortfolioMetrics(grouped,totalNow)}
function ccEstimateSeries(range,currency){const data=ccLastPnlData||{},daily=ccPnlPoints(data.daily),minute=ccPnlPoints(data.minute),source=String((data.metrics&&data.metrics.equityCurrency)||'USD').toUpperCase();const rangeHours={'1D':24,'1W':168,'1M':720,'6M':4392,'1Y':8760}[range]||168;let base=(range==='1D'&&minute.length>1)?minute:daily.slice();if(minute.length){const lm=minute[minute.length-1],day=ccFmtDate(lm.dt),last=base.length?ccFmtDate(base[base.length-1].dt):'';if(day===last&&base.length)base[base.length-1]={dt:base[base.length-1].dt,v:lm.v};else base.push({dt:day+'T00:00:00Z',v:lm.v})}const lastTs=base.length?new Date(base[base.length-1].dt).getTime():NaN,cutoff=Number.isFinite(lastTs)?lastTs-rangeHours*3600000:NaN;const points=Number.isFinite(cutoff)?base.filter(p=>new Date(p.dt).getTime()>=cutoff):base;return points.map(p=>({dt:p.dt,v:ccConvertEstimateValue(p.v,source,currency)})).filter(p=>Number.isFinite(p.v))}
function ccPanelMoneyUsd(v,currency=ccEstimateCurrency){const converted=ccConvertEstimateValue(v,'USD',currency);return Number.isFinite(converted)?ccPnlValue(converted,currency):'—'}
function ccRenderPortfolioMetrics(grouped,total){const metrics=cc('ccPortfolioMetrics');if(!metrics)return;const currency=String(ccEstimateCurrency||'USD').toUpperCase();const tradingUsd=ccSumUsd((grouped&&grouped.trading)||[]);const earnRows=(grouped&&grouped.earn)||[];const earnUsd=earnRows.length?ccSumUsd(earnRows):0;metrics.innerHTML=`<div class="portfolio-grid"><div class="portfolio-item"><div class="portfolio-label">Assets</div><div class="portfolio-value">${ccBalanceRows(ccLastAssetsSummary).length}</div></div><div class="portfolio-item"><div class="portfolio-label">Trading</div><div class="portfolio-value">${ccEsc(ccPanelMoneyUsd(Number.isFinite(tradingUsd)?tradingUsd:total,currency))}</div></div><div class="portfolio-item"><div class="portfolio-label">Earn</div><div class="portfolio-value">${ccEsc(ccPanelMoneyUsd(earnUsd,currency))}</div></div></div>`}
function ccRenderEstimatedTotalValue(total,accountName){const host=cc('ccEstimatedTotalValue');if(!host)return;const metrics=(ccLastPnlData&&ccLastPnlData.metrics)||{},sourceCurrency=String(metrics.equityCurrency||'USD').toUpperCase(),currency=String(ccEstimateCurrency||'USD').toUpperCase();if(currency==='BTC'&&!Number.isFinite(Number(ccBtcUsdtPrice)))ccFetchBtcUsdtPrice().then(()=>ccRerenderEstimatedTotalValue());const range=ccEstimateRange||'1W',series=ccEstimateSeries(range,currency);const first=series.length?series[0].v:null,last=series.length?series[series.length-1].v:null,pnl=(Number.isFinite(first)&&Number.isFinite(last))?last-first:null,pnlPct=(Number.isFinite(pnl)&&first>0)?pnl/first*100:null,pnlClass=Number.isFinite(pnl)?(pnl>=0?'positive':'negative'):'';const latest=Number.isFinite(last)?last:ccConvertEstimateValue(total,'USD',currency);host.innerHTML=`<div class="estimate-head"><div class="estimate-left"><div class="estimate-title">Total Equity</div><div class="estimate-value-row"><div class="estimate-total">${Number.isFinite(latest)?ccEsc(ccEstimateAmountOnly(latest,currency)):'—'}</div><select id="ccEstimateCurrencySelect" class="estimate-currency" aria-label="Estimated total value currency">${['USD','USDT','BTC'].map(c=>`<option value="${c}"${c===currency?' selected':''}>${c}</option>`).join('')}</select></div><div class="estimate-pnl"><div class="estimate-pnl-label">${ccEsc(range)} PnL</div><div class="estimate-pnl-value ${pnlClass}">${Number.isFinite(pnl)?ccEsc(ccPnlValue(pnl,currency)):'—'} <span>(${Number.isFinite(pnlPct)?ccPct(pnlPct):'—'})</span></div></div><div class="estimate-actions"><button id="ccAssetsRefreshTop" class="estimate-action" type="button" data-cc-action="summary">Refresh</button></div></div><div class="estimate-chart-wrap"><div class="estimate-range-tabs">${['1D','1W','1M','6M','1Y'].map(r=>`<button class="estimate-range-tab ${r===range?'active':''}" data-cc-estimate-range="${r}">${r}</button>`).join('')}</div><div class="estimate-chart">${ccRenderEstimateSparkline(series)}</div></div></div>`;const select=cc('ccEstimateCurrencySelect');if(select)select.addEventListener('change',async()=>{ccEstimateCurrency=select.value;if(ccEstimateCurrency==='BTC'){await ccFetchBtcUsdtPrice(true)}ccRerenderEstimatedTotalValue()});document.querySelectorAll('[data-cc-estimate-range]').forEach(btn=>btn.addEventListener('click',()=>{ccEstimateRange=btn.dataset.ccEstimateRange||'1W';ccRerenderEstimatedTotalValue()}));}
function renderCcAssets(res){ccLastAssetsSummary=res;ccRenderHeaderMetrics(res);ccOut('ccAssetsOut',res);const account=res&&res.account;const rows=ccBalanceRows(res);const grouped=ccClassifyBalances(rows);const total=ccSumUsd(rows);const accountName=account&&account.name?account.name:'—';ccRenderEstimatedTotalValue(total,accountName);ccRenderPortfolioMetrics(grouped,total);const tradingRows=ccTradingNonZeroEnabled()?grouped.trading.filter(ccIsNonZeroTradingBalance):grouped.trading;const tradingEmpty=ccTradingNonZeroEnabled()?'No non-zero CoinCall trading balances returned.':'No CoinCall trading balances returned.';const tradingHtml=tradingRows.map(r=>ccBalanceTr(r,'trading')).join('');ccSetRows('ccTradingBalanceBody',tradingHtml,5,tradingEmpty);ccSetRows('ccTradingTableBody',tradingHtml,5,tradingEmpty);const pnlContext=cc('ccPnlContext');if(pnlContext)pnlContext.textContent='Selected CoinCall account: '+accountName;const stats=cc('ccStatsCards');if(stats)stats.innerHTML=`<div class="metric"><strong>${rows.length}</strong><span>Assets returned</span></div><div class="metric"><strong>${grouped.trading.length}</strong><span>Trading rows</span></div><div class="metric"><strong>${grouped.funding.length}</strong><span>Funding rows</span></div><div class="metric"><strong>${account&&account.apiKeyMasked?ccEsc(account.apiKeyMasked):'—'}</strong><span>API key</span></div>`;ccSetRows('ccOrdersBody','',9,'No CoinCall open orders returned by the assets summary.');ccRenderSpotAvailableBalances();ccRenderFuturesAmountUi();ccRenderSpotBottomPanels();ccRenderFuturesBottomPanels()}
const CC_ACCOUNT_SUMMARY_LIVE_REFRESH_MS=2000;let ccAccountSummaryLiveTimer=0,ccAccountSummaryLivePromise=null,ccAccountSummaryLiveLastAttemptAt=0;
function ccApplyAccountSummaryLive(res){ccLastAssetsSummary=res;ccRenderHeaderMetrics(res);if(ccLastPnlData&&ccPnlUsdAutoRefreshActive())renderCcPnl(ccLastPnlData);const rows=ccBalanceRows(res),account=res&&res.account;ccRenderEstimatedTotalValue(ccSumUsd(rows),account&&account.name?account.name:'—');ccRenderSpotAvailableBalances();ccRenderFuturesAmountUi();ccRenderFuturesBottomPanels();ccRenderSpotBottomPanels();if(ccSpreadsTradeModalOpen())ccSpreadsUpdateTradePanels();else ccRenderFuturesAccountSummary()}
async function ccRefreshCcAccountSnapshotLive(force=false){const now=Date.now();if(!force&&ccAccountSummaryLiveLastAttemptAt&&now-ccAccountSummaryLiveLastAttemptAt<CC_ACCOUNT_SUMMARY_LIVE_REFRESH_MS)return ccLastAssetsSummary;if(ccAccountSummaryLivePromise)return ccAccountSummaryLivePromise;ccAccountSummaryLiveLastAttemptAt=now;ccAccountSummaryLivePromise=(async()=>{try{const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';const res=await ccApi('/api/admin/coincall/summary'+qs,{cache:'no-store'});ccApplyAccountSummaryLive(res);return res}catch(e){return ccLastAssetsSummary}finally{ccAccountSummaryLivePromise=null}})();return ccAccountSummaryLivePromise}
function ccStartAccountSummaryLiveAutoRefresh(){if(ccAccountSummaryLiveTimer)return;ccAccountSummaryLiveTimer=setInterval(()=>{if(!document.hidden)ccRefreshCcAccountSnapshotLive(false)},CC_ACCOUNT_SUMMARY_LIVE_REFRESH_MS)}
async function refreshCcAccountSnapshot(){const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';const res=await ccApi('/api/admin/coincall/summary'+qs,{cache:'no-store'});renderCcAssets(res);ccAccountSummaryLiveLastAttemptAt=Date.now();loadCcPnl()}
async function summary(){try{await refreshCcAccountSnapshot();ccMaybeLoadSpotOpenOrders(true);loadCcFuturesOpenOrders();loadCcFuturesPositions();ccRefreshActiveFuturesPanel();if(ccActiveAssetTab()==='transfer-records')loadCcTransferRecords()}catch(e){ccOut('ccAssetsOut','Summary failed: '+e.message);['ccTradingBalanceBody','ccTradingTableBody'].forEach(id=>ccSetRows(id,'',5,'Summary failed: '+e.message))}}
function ccPct(v){const n=Number(v);return Number.isFinite(n)?(n>0?'+':'')+n.toFixed(2)+'%':'—'}
function ccPnlValue(v,currency='USD'){const n=Number(v);if(!Number.isFinite(n))return '—';const c=String(currency||'USD').toUpperCase();const digits=c==='BTC'?8:2;const rendered=n.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits});return c==='USD'?'$'+rendered:(c==='BTC'?'₿'+rendered:rendered+' '+c)}
function ccLiveSummaryEquityValue(){const n=Number(ccLastAssetsSummary&&ccLastAssetsSummary.metrics&&ccLastAssetsSummary.metrics.totalEquity);return Number.isFinite(n)?n:null}
function ccEstimateAmountOnly(v,currency='USD'){const full=ccPnlValue(v,currency);return String(full).replace(/^[$₿]/,'').replace(/\s+(USDT|USD|BTC)$/,'')}
function ccFmtDate(v){if(!v)return '—';const d=new Date(typeof v==='string'&&!v.endsWith('Z')?v+'Z':v);return Number.isNaN(d.getTime())?String(v).slice(0,10):d.toISOString().slice(0,10)}
function ccFmtShort(v){if(!v)return '—';const d=new Date(typeof v==='string'&&!v.endsWith('Z')?v+'Z':v);return Number.isNaN(d.getTime())?String(v).slice(11,16):d.toISOString().slice(11,16)}
function ccPnlPoints(obj){return ((obj&&obj.points)||[]).map(p=>({dt:p.tsUtc||p.dt,v:Number(p.value??p.v)})).filter(p=>p.dt&&Number.isFinite(p.v)).sort((a,b)=>new Date(a.dt)-new Date(b.dt))}
function ccPnlPointsWithLive(points,daily){const v=ccLiveSummaryEquityValue();if(!Number.isFinite(v))return points;const src=ccLastAssetsSummary&&ccLastAssetsSummary.fetchedAtUtc?ccLastAssetsSummary.fetchedAtUtc:new Date().toISOString(),d=new Date(typeof src==='string'&&!src.endsWith('Z')?src+'Z':src);if(Number.isNaN(d.getTime()))return points;const dt=daily?d.toISOString().slice(0,10)+'T00:00:00Z':d.toISOString(),key=p=>daily?ccFmtDate(p.dt):new Date(p.dt).toISOString().slice(0,16),liveKey=daily?ccFmtDate(dt):dt.slice(0,16),out=points.slice();if(out.length&&key(out[out.length-1])===liveKey)out[out.length-1]={dt:daily?out[out.length-1].dt:dt,v};else out.push({dt,v});return out}
function ccDrawdownValues(points, external, daily){if(external&&external.length){const key=d=>daily?ccFmtDate(d):(new Date(d).toISOString().slice(0,16));const map=new Map(external.map(p=>[key(p.tsUtc||p.dt),Number(p.value??p.v)||0]));return points.map(p=>map.get(key(p.dt))||0)}let peak=points.length?points[0].v:0;return points.map(p=>{if(p.v>peak)peak=p.v;return peak>0?(peak-p.v)/peak*100:0})}
function ccDrawEquityChart(id,points,drawdowns,currency,daily){const host=cc(id);if(!host)return;if(!points.length){host.innerHTML='<div class="chart-empty">No CoinCall equity history yet.</div>';return}if(points.length<2){host.innerHTML='<div class="chart-empty">Only 1 point so far: '+ccEsc((daily?ccFmtDate(points[0].dt):ccFmtShort(points[0].dt))+' · '+ccPnlValue(points[0].v,currency))+'. Chart will render after more data accumulates.</div>';return}host.innerHTML='<canvas height="220" style="display:block;width:100%"></canvas>';const canvas=host.querySelector('canvas'),W=host.clientWidth||900,H=260;canvas.width=W;canvas.height=H;const ctx=canvas.getContext('2d');const pad={top:18,right:62,bottom:32,left:14},cw=W-pad.left-pad.right,totalH=H-pad.top-pad.bottom,gap=14,topH=Math.floor((totalH-gap)*.66),botH=totalH-gap-topH,topY=pad.top,botY=pad.top+topH+gap;const vals=points.map(p=>p.v),minV=Math.min(...vals),maxV=Math.max(...vals),range=maxV-minV||1;const px=i=>pad.left+i/(points.length-1)*cw,py=v=>topY+(1-(v-minV)/range)*topH;ctx.strokeStyle='rgba(71,85,105,.55)';ctx.lineWidth=1;for(let i=0;i<=4;i++){const y=topY+i/4*topH;ctx.beginPath();ctx.moveTo(pad.left,y);ctx.lineTo(pad.left+cw,y);ctx.stroke();ctx.fillStyle='#94a3b8';ctx.font='600 11px Arial';ctx.textAlign='left';ctx.fillText(ccPnlValue(maxV-i/4*range,currency),pad.left+cw+6,y+4)}const step=Math.max(1,Math.floor(points.length/(daily?8:6)));ctx.fillStyle='#94a3b8';ctx.textAlign='center';for(let i=0;i<points.length;i+=step)ctx.fillText(daily?ccFmtDate(points[i].dt):ccFmtShort(points[i].dt),px(i),H-8);const grad=ctx.createLinearGradient(0,topY,0,topY+topH);grad.addColorStop(0,'rgba(52,211,153,.22)');grad.addColorStop(1,'rgba(52,211,153,0)');ctx.beginPath();ctx.moveTo(px(0),py(points[0].v));for(let i=1;i<points.length;i++)ctx.lineTo(px(i),py(points[i].v));ctx.lineTo(px(points.length-1),topY+topH);ctx.lineTo(px(0),topY+topH);ctx.closePath();ctx.fillStyle=grad;ctx.fill();ctx.beginPath();ctx.moveTo(px(0),py(points[0].v));for(let i=1;i<points.length;i++)ctx.lineTo(px(i),py(points[i].v));ctx.strokeStyle='#34d399';ctx.lineWidth=2;ctx.stroke();const dd=ccDrawdownValues(points,drawdowns,daily),maxDd=Math.max(...dd,0.01),pyd=v=>botY+v/maxDd*botH,fmtDd=v=>Math.abs(v)<.005?'0.00%':'-'+Number(v).toFixed(2)+'%';ctx.font='600 11px Arial';ctx.textAlign='left';for(let i=0;i<=4;i++){const y=botY+i/4*botH;ctx.strokeStyle='rgba(71,85,105,.55)';ctx.beginPath();ctx.moveTo(pad.left,y);ctx.lineTo(pad.left+cw,y);ctx.stroke();ctx.fillStyle='#94a3b8';ctx.fillText(fmtDd(i/4*maxDd),pad.left+cw+6,y+4)}const barW=daily?Math.max(2,Math.floor(cw/points.length)-1):Math.max(1,Math.floor(cw/points.length));for(let i=0;i<points.length;i++){ctx.fillStyle=dd[i]>0?'rgba(148,163,184,.45)':'rgba(148,163,184,.18)';ctx.fillRect(px(i)-Math.floor(barW/2),botY,barW,Math.max(1,pyd(dd[i])-botY))}ctx.fillStyle='#34d399';ctx.textAlign='left';ctx.font='700 11px Arial';ctx.fillText('■ Equity',pad.left,topY-8);ctx.fillStyle='#6b7280';ctx.fillText('■ Drawdown histogram',pad.left,botY+12);ctx.fillStyle='#d1d5db';ctx.textAlign='center';ctx.fillText('Max DD: -'+maxDd.toFixed(2)+'%',pad.left+cw/2,topY-8)}
function ccEquityChartTooltip(){let el=document.getElementById('ccEquityChartTooltip');if(!el){el=document.createElement('div');el.id='ccEquityChartTooltip';el.className='cc-equity-chart-tooltip';document.body.appendChild(el)}return el}
function ccHideEquityChartTooltip(){const el=document.getElementById('ccEquityChartTooltip');if(el)el.style.display='none'}
function ccShowEquityChartTooltip(canvas,clientX,clientY,html){const el=ccEquityChartTooltip(),pad=12;el.innerHTML=html;el.style.display='block';let left=clientX+pad,top=clientY+pad;const w=el.offsetWidth||180,h=el.offsetHeight||54;if(left+w>window.innerWidth-8)left=clientX-w-pad;if(top+h>window.innerHeight-8)top=clientY-h-pad;el.style.left=Math.max(8,left)+'px';el.style.top=Math.max(8,top)+'px'}
function ccBindEquityDrawdownTooltip(canvas,daily,currency){canvas.addEventListener('mousemove',ev=>{const bars=canvas._ccDdBars||[],rect=canvas.getBoundingClientRect(),scaleX=canvas.width/rect.width,scaleY=canvas.height/rect.height,x=(ev.clientX-rect.left)*scaleX,y=(ev.clientY-rect.top)*scaleY,hit=bars.find(b=>x>=b.x0&&x<=b.x1&&y>=b.y0&&y<=b.y1);if(!hit){ccHideEquityChartTooltip();return}const time=daily?ccFmtDate(hit.point.dt):(ccFmtDate(hit.point.dt)+' '+ccFmtShort(hit.point.dt)+' UTC'),dd=Math.abs(hit.dd)<.005?'0.00%':'-'+Number(hit.dd).toFixed(2)+'%';ccShowEquityChartTooltip(canvas,ev.clientX,ev.clientY,'<div>'+ccEsc(time)+'</div><div><span class="muted">Drawdown:</span> '+ccEsc(dd)+'</div><div><span class="muted">Equity:</span> '+ccEsc(ccPnlValue(hit.point.v,currency))+'</div>')});canvas.addEventListener('mouseleave',ccHideEquityChartTooltip)}
function ccDrawEquityChart(id,points,drawdowns,currency,daily){
  const host=cc(id);if(!host)return;
  if(!points||!points.length){host.innerHTML='<div class="chart-empty">No CoinCall equity history yet.</div>';return}
  if(points.length<2){host.innerHTML='<div class="chart-empty">Only 1 point so far: '+ccEsc((daily?ccFmtDate(points[0].dt):ccFmtShort(points[0].dt))+' · '+ccPnlValue(points[0].v,currency))+'. Chart will render after more data accumulates.</div>';return}
  host.innerHTML='<canvas style="display:block;width:100%"></canvas>';const canvas=host.querySelector('canvas'),W=host.clientWidth||900,baseH=260,isMobile=window.innerWidth<=700,pad=isMobile?{top:20,right:42,bottom:daily?34:30,left:2}:{top:16,right:62,bottom:32,left:14},cw=W-pad.left-pad.right,gap=14,baseTotalH=baseH-pad.top-pad.bottom,baseTopH=Math.floor((baseTotalH-gap)*(2/3)),botH=baseTotalH-gap-baseTopH,topH=daily?Math.round(baseTopH*1.56):baseTopH,H=pad.top+topH+gap+botH+pad.bottom,topY=pad.top,botY=pad.top+topH+gap;
  canvas.width=W;canvas.height=H;const ctx=canvas.getContext('2d');ctx.clearRect(0,0,W,H);
  const vals=points.map(p=>p.v),minV=Math.min(...vals),maxV=Math.max(...vals),range=maxV-minV||1,px=i=>pad.left+(i/(points.length-1))*cw,py=v=>topY+(1-(v-minV)/range)*topH,axisValue=v=>String(currency||'USD').toUpperCase()==='USD'?'$'+Number(v).toLocaleString('en-US',{maximumFractionDigits:0}):ccPnlValue(v,currency);
  const ddVals=ccDrawdownValues(points,drawdowns,daily),maxDd=Math.max(...ddVals,0.01),currentDd=ddVals[ddVals.length-1]||0,deepThreshold=maxDd*.75,pyd=v=>botY+(v/maxDd)*botH,fmtDd=v=>(Math.abs(v)<.005?'0.00%':'-'+Number(v).toFixed(2)+'%');
  ctx.strokeStyle='rgba(71,85,105,.55)';ctx.lineWidth=1;
  for(let i=0;i<=4;i++){const y=topY+(i/4)*topH;ctx.beginPath();ctx.moveTo(pad.left,y);ctx.lineTo(pad.left+cw,y);ctx.stroke();ctx.fillStyle='#94a3b8';ctx.font='600 9px Liberation Sans, Arial, sans-serif';ctx.textAlign='left';ctx.fillText(axisValue(maxV-(i/4)*range),pad.left+cw+6,y+4)}
  for(let i=0;i<=4;i++){const y=botY+(i/4)*botH;ctx.beginPath();ctx.moveTo(pad.left,y);ctx.lineTo(pad.left+cw,y);ctx.stroke();ctx.fillStyle='#9ca3af';ctx.font='600 9px Liberation Sans, Arial, sans-serif';ctx.textAlign='left';ctx.fillText(fmtDd((i/4)*maxDd),pad.left+cw+6,y+4)}
  ctx.strokeStyle='rgba(148,163,184,.9)';ctx.beginPath();ctx.moveTo(pad.left,topY+topH+gap/2);ctx.lineTo(pad.left+cw,topY+topH+gap/2);ctx.stroke();
  const targetTicks=isMobile?(daily?4:6):8,step=Math.max(1,Math.floor(points.length/targetTicks));ctx.fillStyle='#94a3b8';ctx.font='600 9px Liberation Sans, Arial, sans-serif';ctx.textAlign='center';for(let i=0;i<points.length;i+=step)ctx.fillText(daily?ccFmtDate(points[i].dt):ccFmtShort(points[i].dt),px(i),H-8);
  const color=daily?'#34d399':'#60a5fa',rgb=daily?'52,211,153':'96,165,250',grad=ctx.createLinearGradient(0,topY,0,topY+topH),gapMs=daily?2*24*60*60*1000:2*60*1000;grad.addColorStop(0,'rgba('+rgb+',.22)');grad.addColorStop(1,'rgba('+rgb+',0)');
  ctx.beginPath();ctx.moveTo(px(0),py(points[0].v));for(let i=1;i<points.length;i++){const dtGap=new Date(points[i].dt)-new Date(points[i-1].dt);if(dtGap>gapMs)ctx.moveTo(px(i),py(points[i].v));else ctx.lineTo(px(i),py(points[i].v))}ctx.lineTo(px(points.length-1),topY+topH);ctx.lineTo(px(0),topY+topH);ctx.closePath();ctx.fillStyle=grad;ctx.fill();
  ctx.beginPath();ctx.moveTo(px(0),py(points[0].v));for(let i=1;i<points.length;i++){const dtGap=new Date(points[i].dt)-new Date(points[i-1].dt);if(dtGap>gapMs)ctx.moveTo(px(i),py(points[i].v));else ctx.lineTo(px(i),py(points[i].v))}ctx.strokeStyle=color;ctx.lineWidth=2;ctx.stroke();
  const lx=px(points.length-1),ly=py(points[points.length-1].v);ctx.beginPath();ctx.arc(lx,ly,4,0,Math.PI*2);ctx.fillStyle=color;ctx.fill();
  const barW=daily?Math.max(2,Math.floor(cw/points.length)-1):Math.max(1,Math.floor(cw/points.length));canvas._ccDdBars=[];for(let i=0;i<points.length;i++){const x=px(i)-Math.floor(barW/2),h=Math.max(1,pyd(ddVals[i])-botY);ctx.fillStyle=ddVals[i]>=deepThreshold&&ddVals[i]>0?(daily?'rgba(148,163,184,.72)':'rgba(107,114,128,.42)'):(ddVals[i]>0?(daily?'rgba(148,163,184,.45)':'rgba(71,85,105,.30)'):(daily?'rgba(148,163,184,.22)':'rgba(71,85,105,.14)'));ctx.fillRect(x,botY,barW,h);canvas._ccDdBars.push({x0:x,x1:x+barW,y0:botY,y1:botY+h,dd:ddVals[i],point:points[i]})}
  ccBindEquityDrawdownTooltip(canvas,daily,currency);
  ctx.font='700 11px Liberation Sans, Arial, sans-serif';ctx.textAlign='left';ctx.fillStyle=color;ctx.fillText('■ Equity',pad.left,topY-8);ctx.fillStyle=daily?'#94a3b8':'#6b7280';ctx.fillText('■ Drawdown histogram',pad.left,botY+3);if(daily){ctx.fillStyle='#d1d5db';ctx.textAlign='center';ctx.fillText('Current DD: '+fmtDd(currentDd)+'   Max DD: '+fmtDd(maxDd),pad.left+cw/2,topY-8)}
}

function ccComputeRatio(points,kind){if(!points||points.length<2)return null;const rets=[];for(let i=1;i<points.length;i++){const a=points[i-1].v,b=points[i].v;if(a>0&&Number.isFinite(b))rets.push(b/a-1)}if(rets.length<2)return null;const avg=rets.reduce((s,x)=>s+x,0)/rets.length;const variance=rets.reduce((s,x)=>{const d=kind==='sortino'?Math.min(0,x):x-avg;return s+d*d},0)/rets.length;if(!(variance>0))return null;return avg/Math.sqrt(variance)*Math.sqrt(365)}

function ccVanFsHexToRgb(hex){var h=String(hex||'#34d399').replace('#','');var n=parseInt(h,16);return ((n>>16)&255)+','+((n>>8)&255)+','+(n&255)}
function ccVanFsFmtUsd(v){return String.fromCharCode(36)+Number(v).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})}
function ccVanFsFmtValue(v,currency){var c=String(currency||'USD').toUpperCase();if(c==='BTC')return ccPnlValue(v,'BTC');var n=Number(v);return Number.isFinite(n)?String.fromCharCode(36)+n.toLocaleString('en-US',{minimumFractionDigits:4,maximumFractionDigits:4}):'—'}function ccVanFsAxisValue(v,currency){var c=String(currency||'USD').toUpperCase();if(c==='BTC')return ccPnlValue(v,'BTC');var n=Number(v);return Number.isFinite(n)?String.fromCharCode(36)+n.toLocaleString('en-US',{minimumFractionDigits:4,maximumFractionDigits:4}):'—'}
function ccVanFsSetMetric(id,value,color){var el=cc(id);if(!el)return;el.textContent=value;el.style.color=color||''}
function ccVanFsDrawChart(points,canvasId,emptyId,wrapId,color,isDaily,assetLabel,currency,drawdowns){var canvas=cc(canvasId),empty=cc(emptyId),wrap=cc(wrapId);if(!canvas||!empty||!wrap)return;if(!points||points.length<2){empty.textContent='Not enough data yet.';empty.style.display='';canvas.style.display='none';return}empty.style.display='none';canvas.style.display='block';var W=wrap.clientWidth||900,H=260;canvas.width=W;canvas.height=H;var ctx=canvas.getContext('2d');ctx.clearRect(0,0,W,H);var pad={top:16,right:62,bottom:32,left:14},cw=W-pad.left-pad.right,totalH=H-pad.top-pad.bottom,gap=14,topH=Math.floor((totalH-gap)*(2/3)),botH=totalH-gap-topH,topY=pad.top,botY=pad.top+topH+gap;var vals=points.map(function(p){return p.v}),minV=Math.min.apply(null,vals),maxV=Math.max.apply(null,vals),range=maxV-minV||1,px=function(i){return pad.left+(i/(points.length-1))*cw},py=function(v){return topY+(1-(v-minV)/range)*topH};var ddVals=(drawdowns&&drawdowns.length&&isDaily)?(function(){var map=new Map(drawdowns.map(function(p){var raw=p.value;if(raw===undefined)raw=p.v;if(raw===undefined)raw=p.maxDrawdownPct;return[ccFmtDate(p.tsUtc||p.dt),Number(raw)||0]}));return points.map(function(p){return map.get(ccFmtDate(p.dt))||0})})():(function(){var peak=points[0].v;return points.map(function(p){if(p.v>peak)peak=p.v;return peak>0?(peak-p.v)/peak*100:0})})();var maxDd=Math.max.apply(null,ddVals.concat([0.01])),deepThreshold=maxDd*.75,pyd=function(v){return botY+(v/maxDd)*botH},fmtDd=function(v){return Math.abs(v)<.005?'0.00%':'-'+Number(v).toFixed(2)+'%'};ctx.strokeStyle='rgba(71,85,105,.55)';ctx.lineWidth=1;for(var i=0;i<=4;i++){var y=topY+i/4*topH;ctx.beginPath();ctx.moveTo(pad.left,y);ctx.lineTo(pad.left+cw,y);ctx.stroke();ctx.fillStyle='#94a3b8';ctx.font='11px monospace';ctx.textAlign='left';ctx.fillText(ccVanFsAxisValue(maxV-i/4*range,currency),pad.left+cw+6,y+4)}for(var j=0;j<=4;j++){var y2=botY+j/4*botH;ctx.beginPath();ctx.moveTo(pad.left,y2);ctx.lineTo(pad.left+cw,y2);ctx.stroke();ctx.fillStyle='#9ca3af';ctx.font='11px monospace';ctx.textAlign='left';ctx.fillText(fmtDd(j/4*maxDd),pad.left+cw+6,y2+4)}ctx.strokeStyle='rgba(148,163,184,.9)';ctx.beginPath();ctx.moveTo(pad.left,botY);ctx.lineTo(pad.left+cw,botY);ctx.stroke();var step=Math.max(1,Math.floor(points.length/8));ctx.fillStyle='#94a3b8';ctx.font='11px monospace';ctx.textAlign='center';for(var k=0;k<points.length;k+=step)ctx.fillText(isDaily?ccFmtDate(points[k].dt):ccFmtShort(points[k].dt),px(k),H-8);var gapMs=isDaily?2*24*60*60*1000:2*60*1000,grad=ctx.createLinearGradient(0,topY,0,topY+topH);grad.addColorStop(0,'rgba('+ccVanFsHexToRgb(color)+',.22)');grad.addColorStop(1,'rgba('+ccVanFsHexToRgb(color)+',0)');ctx.beginPath();ctx.moveTo(px(0),py(points[0].v));for(var m=1;m<points.length;m++){if(new Date(points[m].dt)-new Date(points[m-1].dt)>gapMs)ctx.moveTo(px(m),py(points[m].v));else ctx.lineTo(px(m),py(points[m].v))}ctx.lineTo(px(points.length-1),topY+topH);ctx.lineTo(px(0),topY+topH);ctx.closePath();ctx.fillStyle=grad;ctx.fill();ctx.beginPath();ctx.moveTo(px(0),py(points[0].v));for(var n2=1;n2<points.length;n2++){if(new Date(points[n2].dt)-new Date(points[n2-1].dt)>gapMs)ctx.moveTo(px(n2),py(points[n2].v));else ctx.lineTo(px(n2),py(points[n2].v))}ctx.strokeStyle=color;ctx.lineWidth=2;ctx.stroke();var lx=px(points.length-1),ly=py(points[points.length-1].v);ctx.beginPath();ctx.arc(lx,ly,4,0,Math.PI*2);ctx.fillStyle=color;ctx.fill();var barW=Math.max(isDaily?2:1,Math.floor(cw/points.length)-(isDaily?1:0));for(var b=0;b<ddVals.length;b++){var dd=ddVals[b],h=Math.max(1,pyd(dd)-botY);if(dd>=deepThreshold&&dd>0)ctx.fillStyle=isDaily?'rgba(148,163,184,.72)':'rgba(107,114,128,.42)';else if(dd>0)ctx.fillStyle=isDaily?'rgba(148,163,184,.45)':'rgba(71,85,105,.30)';else ctx.fillStyle=isDaily?'rgba(148,163,184,.22)':'rgba(71,85,105,.14)';ctx.fillRect(px(b)-barW/2,botY,barW,h)}ctx.fillStyle=color;ctx.textAlign='left';ctx.font='700 11px Arial';ctx.fillText('■ '+assetLabel+' Equity ('+(String(currency||'USD').toUpperCase()==='BTC'?'BTC-denom':'USD-denom')+')',pad.left,topY-2);ctx.fillStyle=isDaily?'#94a3b8':'#6b7280';if(isDaily)ctx.fillText('■ Drawdown histogram',pad.left+100,topY-2);else ctx.fillText('■ Drawdown histogram',pad.left,botY+12);ctx.fillStyle='#d1d5db';ctx.textAlign='center';ctx.fillText('Max DD: '+fmtDd(maxDd),pad.left+cw/2,topY-2)}
function ccVanFsDailyStats(points,current,drawdowns){var initial=points.length?points[0].v:null,latest=Number.isFinite(current)?current:(points.length?points[points.length-1].v:null),days=points.length>1?Math.round((new Date(points[points.length-1].dt)-new Date(points[0].dt))/86400000)+1:0,total=initial&&latest?(latest-initial)/initial*100:null,cagr=initial&&latest&&days>1?(Math.pow(latest/initial,365/days)-1)*100:null,dd=ccDrawdownValues(points,drawdowns||[],true),maxDd=dd.length?Math.max.apply(null,dd):null;return{initial:initial,latest:latest,days:days,total:total,cagr:cagr,maxDd:maxDd,sortino:ccComputeRatio(points,'sortino'),sharpe:ccComputeRatio(points,'sharpe')}}
function ccRenderBtcAssetEquity(data){var m=(data&&data.metrics)||{},currency=String(m.equityCurrency||"USD").toUpperCase(),isNative=currency==="BTC",label=isNative?"BTC Equity (BTC-denom)":"BTC Equity (USD-denom)",titlePrefix=isNative?"BTC Equity (BTC-denom)":"BTC Equity (USD-denom)",daily=ccPnlPoints(data&&data.daily),minute=ccPnlPoints(data&&data.minute),displayDaily=daily.slice();var shell=cc("ccPnlBtcContent");if(shell){var labels=shell.querySelectorAll(".stat-label");if(labels[0])labels[0].textContent=label;var titles=shell.querySelectorAll(".card-title");if(titles[0])titles[0].textContent=titlePrefix+" · Daily";if(titles[1])titles[1].textContent=titlePrefix+" · Last 24 hours · 1-minute resolution"}if(minute.length){var lm=minute[minute.length-1],day=ccFmtDate(lm.dt),last=displayDaily.length?ccFmtDate(displayDaily[displayDaily.length-1].dt):"";if(day===last&&displayDaily.length)displayDaily[displayDaily.length-1]={dt:displayDaily[displayDaily.length-1].dt,v:lm.v};else displayDaily.push({dt:day+"T00:00:00Z",v:lm.v})}var dailyDrawdowns=(data&&data.daily&&data.daily.drawdownPoints)||[];var current=minute.length?minute[minute.length-1].v:(displayDaily.length?displayDaily[displayDaily.length-1].v:null),stats=ccVanFsDailyStats(displayDaily,current,dailyDrawdowns);ccVanFsSetMetric("cc-btc-stat-equity",Number.isFinite(current)?ccVanFsFmtValue(current,currency):"—");ccVanFsSetMetric("cc-btc-stat-change",ccPct(stats.total),Number.isFinite(stats.total)?(stats.total>=0?"#34d399":"#f87171"):"");ccVanFsSetMetric("cc-btc-stat-maxdd",Number.isFinite(stats.maxDd)?((Math.abs(stats.maxDd)<.005?"0.00":"-"+stats.maxDd.toFixed(2))+"%"):"—","#94a3b8");ccVanFsSetMetric("cc-btc-stat-days",stats.days||"—");ccVanFsSetMetric("cc-btc-stat-apr",Number.isFinite(stats.cagr)?((stats.cagr>=0?"+":"")+stats.cagr.toFixed(1)+"%"):"—",Number.isFinite(stats.cagr)?(stats.cagr>=0?"#34d399":"#f87171"):"");ccVanFsSetMetric("cc-btc-stat-sortino",Number.isFinite(stats.sortino)?stats.sortino.toFixed(2):"—");ccVanFsSetMetric("cc-btc-stat-sharpe",Number.isFinite(stats.sharpe)?stats.sharpe.toFixed(2):"—");ccVanFsSetMetric("cc-btc-stat-time",minute.length?ccFmtShort(minute[minute.length-1].dt)+" UTC":((data&&data.metrics&&data.metrics.lastSnapshotUtc)?ccFmtShort(data.metrics.lastSnapshotUtc)+" UTC":"—"));if(cc("cc-btc-daily-chart-range"))cc("cc-btc-daily-chart-range").textContent=displayDaily.length?ccFmtDate(displayDaily[0].dt)+" – "+ccFmtDate(displayDaily[displayDaily.length-1].dt):"";if(cc("cc-btc-chart-range"))cc("cc-btc-chart-range").textContent=minute.length?ccFmtShort(minute[0].dt)+" – "+ccFmtShort(minute[minute.length-1].dt)+" UTC":"";ccVanFsDrawChart(displayDaily,"cc-btc-daily-chart","cc-btc-daily-chart-empty","cc-btc-daily-chart-wrap","#f59e0b",true,"BTC",currency,dailyDrawdowns);ccVanFsDrawChart(minute,"cc-btc-chart","cc-btc-chart-empty","cc-btc-chart-wrap","#f59e0b",false,"BTC",currency)}
let ccBtcUsdEquityRefreshBusy=false;async function loadCcBtcAssetEquity(denom){try{var sel=cc('ccAccountSelect'),qs='asset=BTC&denom='+encodeURIComponent(denom||'usd')+(sel&&sel.value?'&accountId='+encodeURIComponent(sel.value):'');ccRenderBtcAssetEquity(await ccApi('/api/admin/coincall/equity/asset?'+qs))}catch(e){['cc-btc-daily-chart-empty','cc-btc-chart-empty'].forEach(function(id){if(cc(id))cc(id).textContent='CoinCall BTC equity failed: '+e.message})}}function ccStartBtcUsdEquityAutoRefresh(){if(window.ccBtcUsdEquityRefreshTimer)return;window.ccBtcUsdEquityRefreshTimer=setInterval(async()=>{if(document.hidden||String(ccPnlAsset||'').toUpperCase()!=='BTC_USD'||ccBtcUsdEquityRefreshBusy)return;ccBtcUsdEquityRefreshBusy=true;try{await loadCcBtcAssetEquity('usd')}finally{ccBtcUsdEquityRefreshBusy=false}},15000)}
let ccPnlClearingCurrency='BTC';let ccPnlAsset='USD';function ccApplyPnlAssetTab(asset){const target=String(asset||'USD').toUpperCase();ccPnlAsset=target;document.querySelectorAll('[data-cc-pnl-asset]').forEach(x=>{const active=String(x.dataset.ccPnlAsset||'').toUpperCase()===target;x.classList.toggle('active',active);x.setAttribute('aria-selected',active?'true':'false')});const usd=cc('ccPnlUsdContent'),btc=cc('ccPnlBtcContent'),placeholder=cc('ccPnlAssetPlaceholder');if(usd)usd.hidden=target!=='USD';if(btc)btc.hidden=target!=='BTC'&&target!=='BTC_USD';if(placeholder){placeholder.hidden=target==='USD'||target==='BTC'||target==='BTC_USD';if(target!=='USD'&&target!=='BTC'&&target!=='BTC_USD')placeholder.innerHTML='<div class="chart-empty">'+ccEsc(target)+' equity history is not wired to a backend endpoint yet.</div>'}}
let ccPnlUsdRefreshBusy=false;function ccPnlUsdAutoRefreshActive(){return !document.hidden&&ccActiveLayer()==='assets'&&ccActiveAssetTab()==='pnl'&&String(ccPnlAsset||'USD').toUpperCase()==='USD'}function ccClearPnlUsdAutoRefresh(){if(window.ccPnlUsdRefreshTimer){clearTimeout(window.ccPnlUsdRefreshTimer);window.ccPnlUsdRefreshTimer=null}}function ccSchedulePnlUsdAutoRefresh(){ccClearPnlUsdAutoRefresh();if(!ccPnlUsdAutoRefreshActive())return;window.ccPnlUsdRefreshTimer=setTimeout(async()=>{window.ccPnlUsdRefreshTimer=null;if(!ccPnlUsdAutoRefreshActive()||ccPnlUsdRefreshBusy){ccSchedulePnlUsdAutoRefresh();return}ccPnlUsdRefreshBusy=true;try{await loadCcPnl()}finally{ccPnlUsdRefreshBusy=false;ccSchedulePnlUsdAutoRefresh()}},15000)}function ccUpdatePnlUsdAutoRefresh(){if(ccPnlUsdAutoRefreshActive())ccSchedulePnlUsdAutoRefresh();else ccClearPnlUsdAutoRefresh()}
function ccPnlClearingCurrencyDigits(currency){const c=String(currency||'').toUpperCase();return c==='BTC'||c==='ETH'?8:4}
function ccPnlClearingValue(v,currency){const n=Number(v);if(!Number.isFinite(n))return '—';const c=String(currency||'USD').toUpperCase();const digits=ccPnlClearingCurrencyDigits(c);const rendered=n.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits});return c==='USD'?String.fromCharCode(36)+rendered:(c==='BTC'?'₿'+rendered:rendered+' '+c)}
function ccPnlClearingNumber(v,currency){const n=Number(v);if(!Number.isFinite(n))return '—';const c=String(currency||'').toUpperCase();const digits=c?ccPnlClearingCurrencyDigits(c):(Math.abs(n)>=100?2:4);return n.toLocaleString('en-US',{minimumFractionDigits:digits,maximumFractionDigits:digits})}
function ccPnlClearingPct(row){const value=ccPnlClearingPick(row,['pnlPct','pnlPercent','pnlPercentage','realizedPnlPct','realizedPnlPercent','roi','roiPct','roiPercent']);const n=Number(value);if(!Number.isFinite(n))return '—';return (n>0?'+':'')+n.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})+'%'}
function ccPnlClearingPnlUsdt(row){const value=ccPnlClearingPick(row,['pnlUsdt','pnlUSDT','pnlUsd','pnlUSD','unrealizedPnlUsdt','unrealizedPnlUSDT','unrealizedPnlUsd','unrealizedPnlUSD','realizedPnlUsdt','realizedPnlUSDT','realizedPnlUsd','realizedPnlUSD','settlementPnlUsdt','settlementPnlUSD','settlementPnL']);const n=Number(value);if(!Number.isFinite(n))return '—';return (n>0?'+':'')+n.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:4})+' USDT'}
function ccPnlClearingEquityUsdt(row){const value=ccPnlClearingPick(row,['equityUsdt','equityUSDT','equityUsd','equityUSD']);const n=Number(value);if(!Number.isFinite(n))return '—';return n.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2})+' USDT'}
function ccPnlClearingNotes(row){const notes=Array.isArray(row&&row.notes)?row.notes.filter(Boolean):[];return notes.length?notes.join(' | '):'—'}
function ccPnlClearingTime(v){if(!v)return '—';const d=new Date(typeof v==='string'&&!(/[zZ]$|[+-]\d\d:?\d\d$/.test(v))?v+'Z':v);return Number.isNaN(d.getTime())?String(v):ccUtcDateTime(d)}
function ccSyncPnlClearingTabs(){document.querySelectorAll('[data-cc-pnl-clearing-currency]').forEach(btn=>{const active=String(btn.dataset.ccPnlClearingCurrency||'').toUpperCase()===String(ccPnlClearingCurrency||'BTC').toUpperCase();btn.classList.toggle('active',active);btn.setAttribute('aria-selected',active?'true':'false')})}
function ccPnlClearingRowCurrency(row,fallback){return String((row&&row.currency)||(row&&row.asset)||(row&&row.equityCurrency)||(row&&row.sources&&row.sources.equity&&row.sources.equity.end&&row.sources.equity.end.equityCurrency)||fallback||'').toUpperCase()}
function ccPnlClearingSources(row){return (row&&row.sources&&typeof row.sources==='object')?row.sources:{}}
function ccPnlClearingRaw(row){const src=ccPnlClearingSources(row);return (src.raw&&typeof src.raw==='object')?src.raw:{}}
function ccPnlClearingPick(row,names){const src=ccPnlClearingSources(row),raw=ccPnlClearingRaw(row);for(const obj of [row,src,raw]){if(!obj||typeof obj!=='object')continue;for(const name of names){for(const key of Object.keys(obj)){if(key.toLowerCase()===name.toLowerCase()){const value=obj[key];if(value!==null&&value!==undefined&&String(value)!=='')return value}}}}return null}
function ccPnlClearingText(row,names,fallback){const value=ccPnlClearingPick(row,names);return value===null||value===undefined||String(value)===''?(fallback||'—'):String(value)}
function ccPnlClearingAmount(row,names,currency){const value=ccPnlClearingPick(row,names);return ccPnlClearingNumber(value,currency)}
function ccPnlClearingValueWithUnit(row){const value=ccPnlClearingPick(row,['value','dealValue','filledValue','quoteQty','quoteAmount']);if(value===null||value===undefined||String(value)==='')return '—';const unit=ccPnlClearingText(row,['valueCurrency','dealValueUnit','quoteAmountCurrency','quoteToken','quoteCurrency'],'');const rendered=ccPnlClearingNumber(value,unit);return unit?rendered+' '+unit:rendered}
function ccPnlClearingRole(row){const role=ccPnlClearingPick(row,['role','liquidity']);if(role!==null&&role!==undefined&&String(role)!=='')return String(role);const isTaker=ccPnlClearingPick(row,['isTaker']);const text=String(isTaker??'').trim().toLowerCase();if(text==='1'||text==='true')return 'Taker';if(text==='0'||text==='false')return 'Maker';return '—'}
function ccPnlClearingInfo(row){const parts=[];const source=ccPnlClearingPick(row,['source']);if(source!==null&&source!==undefined&&String(source)!=='')parts.push(String(source));const tradeId=ccPnlClearingPick(row,['tradeId','fillId','execId','id']);if(tradeId!==null&&tradeId!==undefined&&String(tradeId)!=='')parts.push('Trade '+String(tradeId));const orderId=ccPnlClearingPick(row,['orderId','ordId','order_id']);if(orderId!==null&&orderId!==undefined&&String(orderId)!=='')parts.push('Order '+String(orderId));const role=ccPnlClearingPick(row,['role','liquidity']);if(role!==null&&role!==undefined&&String(role)!=='')parts.push(String(role));return parts.length?parts.join(' | '):'—'}
function ccPnlClearingSideText(side){const text=String(side??'').trim();const lower=text.toLowerCase();if(!text||text==='—')return '—';if(text==='1'||lower==='buy'||lower==='long'||lower==='bid'||lower==='b')return 'Buy';if(text==='2'||lower==='sell'||lower==='short'||lower==='ask'||lower==='s')return 'Sell';return '—'}
function ccPnlClearingSideClass(side){const text=String(side||'').trim().toLowerCase();if(!text||text==='—')return'';if(['buy','long','in','inflow','deposit','credit','receive','received','1','b'].includes(text))return'green';if(['sell','short','out','outflow','withdraw','withdrawal','debit','send','sent','2','s'].includes(text))return'danger';return''}
function ccRenderPnlClearing(data){const currency=String((data&&data.requestedCurrency)||ccPnlClearingCurrency||'BTC').toUpperCase();const rawRows=Array.isArray(data&&data.rows)?data.rows:[];const rows=rawRows.filter(row=>ccPnlClearingRowCurrency(row,data&&data.equityCurrency)===currency);const meta=cc('ccPnlClearingMeta'),body=cc('ccPnlClearingBody'),details=cc('ccPnlClearingDetails');ccSyncPnlClearingTabs();if(meta)meta.textContent=rows.length?(rows.length+' event'+(rows.length===1?'':'s')+' | '+currency):currency;if(!body)return;if(!rows.length){body.innerHTML='<tr><td class="muted" colspan="23">No confirmed ledger events for '+ccEsc(currency)+'.</td></tr>';if(details)details.innerHTML='';return}body.innerHTML=rows.map(row=>{const change=Number(row&&row.residual);const changeCls=Number.isFinite(change)?(change>=0?'green':'danger'):'';const note=ccEsc(ccPnlClearingNotes(row));const fee=Number(row&&row.fees);const feeCls=Number.isFinite(fee)&&fee<0?'danger':'';const cash=Number(row&&row.transfers);const cashCls=Number.isFinite(cash)?(cash>=0?'green':'danger'):'';const funding=Number(row&&row.funding);const fundingCls=Number.isFinite(funding)?(funding>=0?'green':'danger'):'';const rawSide=ccPnlClearingText(row,['side','tradeSide','rawSide','direction','sd','si','orderSide']);const side=ccPnlClearingSideText(rawSide);const sideCls=ccPnlClearingSideClass(rawSide);return '<tr><td>'+ccEsc(ccPnlClearingTime(row.timeUtc||row.dayStartUtc||row.dayUtc))+'</td><td>'+ccEsc(ccPnlClearingText(row,['instrument','symbol','displaySymbol','displayName','instId','tickerId'],row.asset||row.currency||currency))+'</td><td>'+ccEsc(row.eventType||row.event||'—')+'</td><td class="pnl-clearing-side '+sideCls+'">'+ccEsc(side)+'</td><td>'+ccEsc(ccPnlClearingAmount(row,['qty','quantity','amount','fillQty','filledQty'],currency))+'</td><td>'+ccEsc(ccPnlClearingAmount(row,['baseAmount','baseQty','baseQuantity'],currency))+'</td><td>'+ccEsc(ccPnlClearingValueWithUnit(row))+'</td><td>'+ccEsc(ccPnlClearingAmount(row,['position','pos','positionSize','size'],currency))+'</td><td>'+ccEsc(ccPnlClearingNumber(ccPnlClearingPick(row,['price','fillPrice','filledPrice','px'])))+'</td><td>'+ccEsc(ccPnlClearingNumber(ccPnlClearingPick(row,['markPrice','mark_price','markPx'])))+'</td><td>'+ccEsc(ccPnlClearingNumber(ccPnlClearingPick(row,['indexPrice','index_price','indexPx'])))+'</td><td>'+ccEsc(ccPnlClearingPct(row))+'</td><td>'+ccEsc(ccPnlClearingPnlUsdt(row))+'</td><td>'+ccEsc(ccPnlClearingNumber(ccPnlClearingPick(row,['settlementPrice','settlePrice','settlement_price'])))+'</td><td class="'+cashCls+'">'+ccEsc(ccPnlClearingValue(row.transfers,currency))+'</td><td class="'+fundingCls+'">'+ccEsc(ccPnlClearingValue(row.funding,currency))+'</td><td class="'+feeCls+'">'+ccEsc(ccPnlClearingValue(row.fees,currency))+'</td><td class="'+changeCls+'">'+ccEsc(ccPnlClearingValue(row.residual,currency))+'</td><td>'+ccEsc(ccPnlClearingValue(ccPnlClearingPick(row,['balance','availableEquity','availableBalance','cashBalanceAmount']),currency))+'</td><td>'+ccEsc(ccPnlClearingEquityUsdt(row))+'</td><td>'+ccEsc(ccPnlClearingRole(row))+'</td><td>'+ccEsc(ccPnlClearingInfo(row))+'</td><td class="muted pnl-clearing-notes-col" title="'+note+'">'+note+'</td></tr>'}).join('');if(details)details.innerHTML=rows.map(row=>'<details><summary>'+ccEsc([ccPnlClearingTime(row.timeUtc||row.dayStartUtc||row.dayUtc),row.eventType||row.event||'Event',ccPnlClearingValue(row.funding??row.realizedPnl??row.fees??row.transfers??row.equity,currency)].join(' | '))+'</summary><pre class="status muted coincall-response">'+ccEsc(JSON.stringify(row.sources||{},null,2))+'</pre></details>').join('')}
async function loadCcDailyClearing(){const sel=cc('ccAccountSelect');const parts=['days=14','currency='+encodeURIComponent(ccPnlClearingCurrency||'BTC')];if(sel&&sel.value)parts.unshift('accountId='+encodeURIComponent(sel.value));ccRenderPnlClearing(await ccApi('/api/admin/coincall/equity/daily-breakdown?'+parts.join('&')))}
function renderCcPnl(data){ccLastPnlData=data;const m=(data&&data.metrics)||{},currency=m.equityCurrency||'USD';let daily=ccPnlPoints(data&&data.daily),minute=ccPnlPoints(data&&data.minute);minute=ccPnlPointsWithLive(minute,false);let displayDaily=ccPnlPointsWithLive(daily,true);if(minute.length){const lm=minute[minute.length-1],day=ccFmtDate(lm.dt),last=displayDaily.length?ccFmtDate(displayDaily[displayDaily.length-1].dt):'';if(day===last)displayDaily[displayDaily.length-1]={dt:displayDaily[displayDaily.length-1].dt,v:lm.v};else displayDaily.push({dt:day+'T00:00:00Z',v:lm.v})}const liveLatest=ccLiveSummaryEquityValue(),latest=Number.isFinite(liveLatest)?liveLatest:(displayDaily.length?displayDaily[displayDaily.length-1].v:null),initial=displayDaily.length?displayDaily[0].v:null,days=displayDaily.length>1?Math.round((new Date(displayDaily[displayDaily.length-1].dt)-new Date(displayDaily[0].dt))/86400000)+1:0,total=(initial&&latest)?(latest-initial)/initial*100:null,cagr=(initial&&latest&&days>1)?(Math.pow(latest/initial,365/days)-1)*100:null,dd=ccDrawdownValues(displayDaily,(data.daily&&data.daily.drawdownPoints)||[],true),maxDd=dd.length?Math.max(...dd,0):0,calmar=(cagr!==null&&maxDd>0)?cagr/maxDd:null,sharpe=ccComputeRatio(displayDaily,'sharpe'),sortino=ccComputeRatio(displayDaily,'sortino');const metrics=cc('ccPnlMetrics');if(metrics)metrics.innerHTML=`<div class="pnl-stat pnl-stat-current"><div class="pnl-stat-label">Current Equity</div><div class="pnl-stat-value green compact">${ccPnlValue(latest,currency)}</div></div><div class="pnl-stat"><div class="pnl-stat-label">Total Return</div><div class="pnl-stat-value ${total>=0?'green':'danger'}">${ccPct(total)}</div></div><div class="pnl-stat"><div class="pnl-stat-label">Max DD</div><div class="pnl-stat-value">${maxDd>0?'-':''}${ccFmt(maxDd)}%</div></div><div class="pnl-stat"><div class="pnl-stat-label">Days</div><div class="pnl-stat-value">${days||'—'}</div></div><div class="pnl-stat"><div class="pnl-stat-label">CAGR</div><div class="pnl-stat-value ${cagr>=0?'green':'danger'}">${ccPct(cagr)}</div></div><div class="pnl-stat"><div class="pnl-stat-label">Calmar</div><div class="pnl-stat-value">${Number.isFinite(calmar)?calmar.toFixed(2):'—'}</div></div><div class="pnl-stat"><div class="pnl-stat-label">Sortino</div><div class="pnl-stat-value">${Number.isFinite(sortino)?sortino.toFixed(2):'—'}</div></div><div class="pnl-stat"><div class="pnl-stat-label">Sharpe</div><div class="pnl-stat-value">${Number.isFinite(sharpe)?sharpe.toFixed(2):'—'}</div></div><div class="pnl-stat"><div class="pnl-stat-label">Last Update</div><div class="pnl-stat-value">${ccFmtShort(minute.length?minute[minute.length-1].dt:m.lastSnapshotUtc)} UTC</div></div>`;if(cc('ccPnlDailyMeta'))cc('ccPnlDailyMeta').textContent=displayDaily.length?ccFmtDate(displayDaily[0].dt)+' – '+ccFmtDate(displayDaily[displayDaily.length-1].dt):'';if(cc('ccPnlMinuteMeta'))cc('ccPnlMinuteMeta').textContent=minute.length?(ccFmtShort(minute[0].dt)+' – '+ccFmtShort(minute[minute.length-1].dt)+' UTC'):(m.storeMinuteEquity?'':'minute storage disabled');ccDrawEquityChart('ccPnlDailyChart',displayDaily,(data.daily&&data.daily.drawdownPoints)||[],currency,true);ccDrawEquityChart('ccPnlMinuteChart',minute,(data.minute&&data.minute.drawdownPoints)||[],currency,false);if(ccLastAssetsSummary){const rows=ccBalanceRows(ccLastAssetsSummary);const account=ccLastAssetsSummary.account;ccRenderEstimatedTotalValue(ccSumUsd(rows),account&&account.name?account.name:'—')}}
async function loadCcPnl(){ccApplyPnlAssetTab(ccPnlAsset||'USD');const active=String(ccPnlAsset||'USD').toUpperCase();if(active==='BTC'||active==='BTC_USD'){await loadCcBtcAssetEquity(active==='BTC'?'native':'usd');return}if(active!=='USD')return;try{const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';renderCcPnl(await ccApi('/api/admin/coincall/pnl'+qs))}catch(e){if(cc('ccPnlMetrics'))cc('ccPnlMetrics').innerHTML='<div class="status danger">CoinCall PnL failed: '+ccEsc(e.message)+'</div>';['ccPnlDailyChart','ccPnlMinuteChart'].forEach(id=>{if(cc(id))cc(id).innerHTML='<div class="chart-empty">CoinCall PnL failed: '+ccEsc(e.message)+'</div>'})}}
const CC_TRADE_STATE={spotPostOnly:true,chartMode:'candles'};
function ccTradeActiveMode(){return document.querySelector('[data-cc-trade].active')?.dataset?.ccTrade||'futures'}
function ccTradeControlMarket(){return ccTradeActiveMode()==='futures'?'futures':'spot'}
function ccTradeSyncTopControls(){const mode=ccTradeActiveMode();const controls=document.querySelector('.cc-trade-inline-controls');if(controls)controls.hidden=mode==='market-data'||mode==='spreads';if(mode==='market-data'||mode==='spreads')return;const market=ccTradeControlMarket();const sourceInst=cc(market==='futures'?'ccFuturesInst':'ccSpotInst');const sourceBar=cc(market==='futures'?'ccFuturesBar':'ccSpotBar');const tradeInst=cc('ccTradeMarketInst');const tradeBar=cc('ccTradeMarketBar');if(sourceInst&&tradeInst){tradeInst.innerHTML=sourceInst.innerHTML;tradeInst.value=sourceInst.value}if(sourceBar&&tradeBar){tradeBar.innerHTML=sourceBar.innerHTML;tradeBar.value=sourceBar.value}document.querySelectorAll('[data-cc-trade-chart-mode]').forEach(btn=>{const active=btn.dataset.ccTradeChartMode===ccTradeChartMode();btn.classList.toggle('active',active);btn.setAttribute('aria-checked',active?'true':'false')})}
function ccTradeApplyChartModeUi(){const linear=ccTradeChartUsesLinear();document.querySelectorAll('[data-cc-book-card]').forEach(card=>{card.hidden=!linear});ccTradeSyncTopControls();drawCcChart('ccSpotChart');drawCcChart('ccFuturesChart')}
function ccTradeSetChartMode(mode,persist=true){ccFuturesViewportPersistActive();CC_TRADE_STATE.chartMode=String(mode||'candles').toLowerCase()==='linear'?'linear':'candles';if(persist)ccUiStateSave({tradeChartMode:CC_TRADE_STATE.chartMode});ccTradeApplyChartModeUi();const market=ccActiveMarket();if(CC_TRADE_STATE.chartMode==='linear'&&(market==='spot'||market==='futures'))ccTradeLoadLinearRows(market).then(()=>drawCcChart(ccChartCanvasId(market))).catch(()=>drawCcChart(ccChartCanvasId(market)))}
function ccTradeProxySelect(kind){if(ccTradeActiveMode()==='market-data')return;const market=ccTradeControlMarket();const source=cc(market==='futures'?(kind==='inst'?'ccFuturesInst':'ccFuturesBar'):(kind==='inst'?'ccSpotInst':'ccSpotBar'));const proxy=cc(kind==='inst'?'ccTradeMarketInst':'ccTradeMarketBar');if(!source||!proxy)return;if(source.value!==proxy.value)source.value=proxy.value;source.dispatchEvent(new Event('change',{bubbles:true}))}
function ccSpotParts(){const v=cc('ccSpotInst')?.value||'BTCUSDT';if(v.endsWith('USDT'))return{base:v.slice(0,-4),quote:'USDT'};if(v.endsWith('USD'))return{base:v.slice(0,-3),quote:'USD'};return{base:v,quote:'USDT'}}
function ccSetStatus(id,msg){const el=cc(id);if(el)el.textContent=msg}
function ccRenderSpotAvailableBalances(){const {base,quote}=ccSpotParts();const quoteBal=ccSpotBalance(quote),baseBal=ccSpotBalance(base);const q=cc('ccSpotAvailQuote'),b=cc('ccSpotAvailBase');if(q)q.textContent='Available: '+(Number.isFinite(quoteBal)?ccMoney(quoteBal,quote):'— '+quote);if(b)b.textContent='Available: '+(Number.isFinite(baseBal)?ccMoney(baseBal,base):'— '+base)}
function ccSpotMinSize(base){return String(base||'').toUpperCase()==='ETH'?'0.0001':'0.00001'}
function ccUpdateSpotTradeUi(){const type=cc('ccSpotOrdType')?.value||'LIMIT';const postBtn=cc('ccSpotPostOnlyBtn');if(type==='MARKET')CC_TRADE_STATE.spotPostOnly=false;const postOnly=CC_TRADE_STATE.spotPostOnly&&type==='LIMIT';if(postBtn){postBtn.classList.toggle('active',postOnly);postBtn.setAttribute('aria-pressed',postOnly?'true':'false');postBtn.disabled=type!=='LIMIT';postBtn.title=type==='LIMIT'?'Post only':'Post only is disabled for market orders.'}const px=cc('ccSpotPx');if(px)px.disabled=type==='MARKET';const {base}=ccSpotParts();if(cc('ccSpotBaseLabel'))cc('ccSpotBaseLabel').textContent=base;if(cc('ccSpotSz'))cc('ccSpotSz').placeholder='Min '+ccSpotMinSize(base)+' '+base;document.querySelectorAll('[data-cc-order="spot"]').forEach(btn=>{btn.textContent=(btn.dataset.side==='SELL'?'Sell ':'Buy ')+base});ccRenderSpotAvailableBalances();ccRenderSpotBookHeadUi()}
function ccFuturesLeverageOptions(current){const cur=Number(current);return Array.from({length:125},(_,i)=>i+1).map(v=>'<option value="'+v+'"'+(v===cur?' selected':'')+'>'+v+'x</option>').join('')}
function ccFuturesLeverageValue(res){const root=res&&res.data&&res.data.data?res.data.data:(res&&res.data?res.data:res);const v=Number(root&&root.currentLeverage);return Number.isFinite(v)?v:NaN}
function ccRenderFuturesLeverageValue(value){const el=cc('ccFuturesLeverageValue');if(el)el.textContent=Number.isFinite(Number(value))?Number(value)+'x':'—'}
function ccSetLeverageModalStatus(msg,kind){const el=cc('ccLeverageModalStatus');if(!el)return;el.textContent=msg||'';el.className='cc-leverage-modal-status'+(kind&&kind!=='muted'?' '+kind:'')}
function ccUpdateLeverageModal(){const slider=cc('ccLeverageSlider');const current=cc('ccLeverageCurrent');const maxText=cc('ccLeverageMaxText');const availText=cc('ccFuturesAvailMargin')?.textContent||'';const match=availText.match(/Available\s+([0-9.,]+)\s+USDT/i);const available=match?Number(String(match[1]).replace(/,/g,'')):NaN;const leverage=Number(slider?.value);if(current)current.textContent=Number.isFinite(leverage)?leverage+'x':'—';if(maxText){const maxPos=Number.isFinite(leverage)&&Number.isFinite(available)?(available*leverage).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}):'—';maxText.textContent='Maximum position at current leverage: '+maxPos+' USDT';}}
function ccOpenLeverageModal(){const modal=cc('ccLeverageModal');const slider=cc('ccLeverageSlider');const sel=cc('ccFuturesLeverageSelect');if(!modal||!slider||!sel)return;slider.value=String(Number(sel.value||sel.dataset.lastValue||1)||1);ccSetLeverageModalStatus('');ccUpdateLeverageModal();modal.hidden=false}
function ccCloseLeverageModal(){const modal=cc('ccLeverageModal');if(modal)modal.hidden=true}
function ccRenderFuturesAvailableMarginUi(){const rows=ccBalanceRows(ccLastAssetsSummary||{});const metrics=(ccLastAssetsSummary&&ccLastAssetsSummary.metrics)||{};const available=ccFirstFinite(metrics&&metrics.availableEquity,ccFindNumericDeep(ccLastAssetsSummary||{},['availableequity','availeq','availbalance','availablebalance','available']),ccHeaderStableSum(rows,'available'));const el=cc('ccFuturesAvailMargin');if(el)el.textContent='Available '+(Number.isFinite(available)?ccFmtNumber(available,2):'—')+' USDT'}
function ccRenderFuturesOrderTypeUi(){const v=String(cc('ccFuturesOrdType')?.value||'LIMIT').toUpperCase();const px=cc('ccFuturesPx');if(px)px.disabled=v==='MARKET'}
function ccRenderFuturesAmountUi(){ccFuturesSyncAmountUnitOptions();ccFuturesSyncBookPrecisionOptions();const input=cc('ccFuturesSz');if(input&&!input.placeholder)input.placeholder='';ccRenderFuturesAvailableMarginUi();ccRenderFuturesOrderTypeUi();ccRenderFuturesBookHeadUi();ccRenderFuturesMaxOrderUi();ccLoadFuturesMaxAvailable().catch(()=>{});if(cc('ccFuturesAsks')&&cc('ccFuturesBids'))loadCcBook('futures')}
let ccFuturesLeverageRefreshTimers=[];
function ccScheduleFuturesLeverageRefreshBurst(){ccFuturesLeverageRefreshTimers.forEach(id=>clearTimeout(id));ccFuturesLeverageRefreshTimers=[];[300,1200,3000].forEach(delay=>{const id=setTimeout(()=>{loadCcFuturesLeverage().catch(()=>{})},delay);ccFuturesLeverageRefreshTimers.push(id)})}
async function loadCcFuturesLeverage(){const sel=cc('ccFuturesLeverageSelect');if(!sel)return;const account=cc('ccAccountSelect')?.value||'',sym=cc('ccFuturesInst')?.value||'';if(!account||!sym){sel.innerHTML='<option>—</option>';ccRenderFuturesLeverageValue(NaN);ccRenderFuturesMaxOrderUi();return}try{sel.disabled=true;const res=await ccApi('/api/admin/coincall/futures/leverage?accountId='+encodeURIComponent(account)+'&symbol='+encodeURIComponent(sym));const value=ccFuturesLeverageValue(res);if(!Number.isFinite(value))throw new Error('CoinCall leverage response has no currentLeverage');sel.innerHTML=ccFuturesLeverageOptions(value);sel.dataset.lastValue=String(value);ccRenderFuturesLeverageValue(value);ccLoadFuturesMaxAvailable().catch(()=>{});ccRenderFuturesMaxOrderUi()}catch(e){sel.innerHTML='<option>—</option>';ccRenderFuturesLeverageValue(NaN);ccRenderFuturesMaxOrderUi();ccSetStatus('ccFuturesOrderStatus','Leverage load failed: '+e.message)}finally{sel.disabled=false}}
async function setCcFuturesLeverage(){const sel=cc('ccFuturesLeverageSelect');if(!sel||!sel.value)return false;const account=cc('ccAccountSelect')?.value||'',sym=cc('ccFuturesInst')?.value||'',leverage=Number(sel.value),previous=sel.dataset.lastValue||sel.value;if(!account||!sym||!Number.isFinite(leverage))return false;try{sel.disabled=true;ccSetStatus('ccFuturesOrderStatus','Saving leverage '+leverage+'x…');ccSetLeverageModalStatus('Saving leverage '+leverage+'x…');const res=await ccApi('/api/admin/coincall/futures/leverage?accountId='+encodeURIComponent(account),{method:'POST',body:JSON.stringify({symbol:sym,leverage})});const value=ccFuturesLeverageValue(res);if(!Number.isFinite(value))throw new Error('CoinCall leverage response has no currentLeverage');sel.innerHTML=ccFuturesLeverageOptions(value);sel.dataset.lastValue=String(value);sel.value=String(value);ccRenderFuturesLeverageValue(value);ccLoadFuturesMaxAvailable().catch(()=>{});ccRenderFuturesMaxOrderUi();await loadCcFuturesLeverage();ccScheduleFuturesLeverageRefreshBurst();ccSetStatus('ccFuturesOrderStatus','Leverage set to '+(sel.dataset.lastValue||value)+'x');ccSetLeverageModalStatus('Leverage set to '+(sel.dataset.lastValue||value)+'x','success');ccUpdateLeverageModal();return true}catch(e){sel.value=previous;ccRenderFuturesLeverageValue(previous);ccRenderFuturesMaxOrderUi();ccSetStatus('ccFuturesOrderStatus','Leverage save failed: '+e.message);ccSetLeverageModalStatus('Leverage save failed: '+e.message,'danger');ccUpdateLeverageModal();return false}finally{sel.disabled=false}}
async function ccSetFuturesLeverageDirect(leverage){const sel=cc('ccFuturesLeverageSelect');if(!sel)return false;const want=Number(leverage);if(!Number.isFinite(want))return false;const hasOption=Array.from(sel.options||[]).some(o=>Number(o.value)===want);if(!hasOption)sel.innerHTML=ccFuturesLeverageOptions(want);sel.value=String(want);return setCcFuturesLeverage()}
function ccSetSpotAmountMin(){const input=cc('ccSpotSz');const {base}=ccSpotParts();if(input){input.value=ccSpotMinSize(base);input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();}}
function ccSpotBalance(ccy){const key=String(ccy||'').toUpperCase();const rows=ccBalanceRows(ccLastAssetsSummary||{});const grouped=ccClassifyBalances(rows);const row=(grouped.trading||[]).find(r=>String(r.ccy||'').toUpperCase()===key)||rows.find(r=>String(r.ccy||'').toUpperCase()===key);const n=Number(row?.available??row?.balance);return Number.isFinite(n)?n:NaN}
function ccSetSpotAmountMax(){const {base}=ccSpotParts();const n=ccSpotBalance(base);const input=cc('ccSpotSz');if(input){input.value=Number.isFinite(n)?String(Number(n.toFixed(8))):'';input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();}if(!Number.isFinite(n))ccSetStatus('ccSpotOrderStatus','Max requires loaded CoinCall trading balance for '+base+'.');}
function ccOpenTradeSettings(){const m=cc('ccTradeSettingsModal');if(m){if(m.parentElement!==document.body)document.body.appendChild(m);m.hidden=false;m.setAttribute('aria-hidden','false')}}
function ccCloseTradeSettings(){const m=cc('ccTradeSettingsModal');if(m){m.hidden=true;m.setAttribute('aria-hidden','true')}}
function ccSetFuturesOrderKind(kind){const sel=cc('ccFuturesOrdType'),px=cc('ccFuturesPx');if(sel){if(kind==='market')sel.value='MARKET';else if(kind==='limit')sel.value='LIMIT';else sel.value='POST_ONLY'}if(px)px.disabled=kind==='MARKET'||kind==='market'}
function ccStepFuturesPx(dir){const input=cc('ccFuturesPx');if(!input)return;const n=parseFloat(String(input.value||'').replace(/,/g,''));input.value=Number.isFinite(n)?(n+dir).toFixed(1):'';input.dispatchEvent(new Event('input',{bubbles:true}));input.focus()}
function ccOrderSubmitPendingKey(m,p){const stable={market:m,symbol:String(p&&p.symbol||''),side:String(p&&p.tradeSide||p&&p.side||''),type:String(p&&p.tradeType||p&&p.type||''),qty:String(p&&p.qty||''),price:String(p&&p.price||'')};return Object.keys(stable).sort().map(k=>k+'='+stable[k]).join('|')}
function ccSetOrderSubmitButtonsPending(m,side,pending){document.querySelectorAll('[data-cc-order]').forEach(btn=>{if(String(btn.dataset.ccOrder||'')===String(m||'')&&String(btn.dataset.side||'')===String(side||'')){btn.toggleAttribute('disabled',!!pending);btn.setAttribute('aria-disabled',pending?'true':'false')}})}
function ccOrderSubmitLock(key){if(CC_ORDER_SUBMIT_PENDING.has(key))return false;CC_ORDER_SUBMIT_PENDING.add(key);return true}
function ccOrderSubmitUnlock(key){CC_ORDER_SUBMIT_PENDING.delete(key)}
async function placeOrder(m,side){let p;if(m==='spot'){const type=cc('ccSpotOrdType').value;const tradeType=type==='MARKET'?'MARKET':(CC_TRADE_STATE.spotPostOnly?'POST_ONLY':'LIMIT');p={symbol:cc('ccSpotInst').value,tradeSide:side,tradeType:tradeType,price:cc('ccSpotPx').value,qty:cc('ccSpotSz').value};if(tradeType==='MARKET')delete p.price}else{const type=String(cc('ccFuturesOrdType')?.value||'LIMIT').toUpperCase();const tradeSide=String(side).toUpperCase()==='SELL'?'2':'1';const tradeType=type==='MARKET'?'2':(type==='POST_ONLY'?'3':'1');p={symbol:cc('ccFuturesInst').value,tradeSide:tradeSide,tradeType:tradeType,qty:cc('ccFuturesSz').value};if(tradeType!=='2')p.price=cc('ccFuturesPx').value}const submitKey=ccOrderSubmitPendingKey(m,p);if(CC_ORDER_SUBMIT_PENDING.has(submitKey)){ccOut(m==='spot'?'ccSpotOrderStatus':'ccFuturesOrderStatus','Order already submitting...');return}if(!ccConfirmOrderAction('Submit '+((p.tradeSide==='2'||p.tradeSide==='SELL')?'SELL':'BUY')+' '+ccOrderTypeLabel(p.tradeType||p.type)+' order '+p.qty+' '+p.symbol+(p.price?' @ '+p.price:'')+'?'))return;if(!ccOrderSubmitLock(submitKey)){ccOut(m==='spot'?'ccSpotOrderStatus':'ccFuturesOrderStatus','Order already submitting...');return}ccSetOrderSubmitButtonsPending(m,side,true);if(m==='futures')CC_FUTURES_ORDER_REFRESH.protectUntil=Date.now()+CC_FUTURES_REST_REPLACE_GUARD_MS;else ccSpotProtectOpenOrdersUntil=Date.now()+CC_SPOT_REST_REPLACE_GUARD_MS;const diag=ccSpreadsLatencyStart('place',m,p.symbol);try{if(m==='futures'){ccEnsureFuturesPrivateSocket().catch(()=>{});ccOut('ccFuturesOrderStatus','Submitting order...')}else ccOut('ccSpotOrderStatus','Submitting order...');ccOrderDiagnosticPush('place',{symbol:p.symbol,side:p.tradeSide||side,action:'sent',message:m+' order place request sent'});const apiStarted=ccSpreadsLatencyNow();const res=await ccApi('/api/admin/coincall/'+m+'/orders/place',{method:'POST',body:JSON.stringify(p)});ccOrderDiagnosticPush('place',{symbol:p.symbol,side:p.tradeSide||side,action:'accepted',message:m+' order place response accepted'});diag.apiMs=ccSpreadsLatencyNow()-apiStarted;const postStarted=ccSpreadsLatencyNow();if(m==='spot'){ccEnsureSpotPrivateSocket().catch(()=>{});ccAddOptimisticSpotOpenOrder(p,res);ccOut('ccSpotOrderStatus',ccOrderResultText('Order placed',res,{symbol:p.symbol,side:p.tradeSide||side,type:p.tradeType||p.type,qty:p.qty,price:p.price}));ccReconcileSpotOpenOrdersSoon('order-submit');ccScheduleSpotTradeRefreshBurst('order-submit')}else{diag.confirmMs=0;ccAddOptimisticFuturesOpenOrder(p,res);ccScheduleFuturesOrderRefreshBurst('order-submit');ccOut('ccFuturesOrderStatus',ccOrderResultText('Order sent',res,{symbol:p.symbol,side:p.tradeSide||side,type:p.tradeType||p.type,qty:p.qty,price:p.price}))}if(typeof ccSpreadsScheduleFastPrivateRefresh==='function')ccSpreadsScheduleFastPrivateRefresh(80);diag.postMs=ccSpreadsLatencyNow()-postStarted;ccSpreadsLatencyFinish(diag,'ok')}catch(e){ccOrderDiagnosticPush('place',{symbol:p.symbol,side:p.tradeSide||side,action:'failed',message:m+' order place failed: '+e.message});ccSpreadsLatencyFinish(diag,'error',e);ccOut(m==='spot'?'ccSpotOrderStatus':'ccFuturesOrderStatus','Order failed: '+e.message)}finally{ccOrderSubmitUnlock(submitKey);ccSetOrderSubmitButtonsPending(m,side,false)}}
async function cancelCcSpotOrder(i){const order=(Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders:[])[Number(i)];if(!order){ccOut('ccSpotOrderStatus','Order not found. Refreshing…');loadCcSpotOpenOrders();return}const payload=ccSpotCancelPayload(order);if(!payload.orderId&&!payload.clientOrderId){ccOut('ccSpotOrderStatus','Cannot cancel: CoinCall order id is missing.');return}const label=(ccSpotOrderSymbol(order)||'CoinCall order')+' '+(payload.orderId||payload.clientOrderId);if(!ccConfirmOrderAction('Cancel '+label+'?'))return;const diag=ccSpreadsLatencyStart('cancel','spot',ccSpotOrderSymbol(order));try{ccOut('ccSpotOrderStatus','Canceling '+label+'…');const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';ccOrderDiagnosticPush('cancel',{order,payload,action:'sent',message:'spot cancel request sent'});const apiStarted=ccSpreadsLatencyNow();const res=await ccApi('/api/admin/coincall/spot/orders/cancel'+qs,{method:'POST',body:JSON.stringify(payload)});ccOrderDiagnosticPush('cancel',{order,payload,action:'accepted',message:'spot cancel accepted'});diag.apiMs=ccSpreadsLatencyNow()-apiStarted;const postStarted=ccSpreadsLatencyNow();ccEnsureSpotPrivateSocket().catch(()=>{});ccApplySpotCancelSuccess(order);ccOut('ccSpotOrderStatus',ccOrderResultText('Order canceled',res,{symbol:ccSpotOrderSymbol(order),type:ccSpotOrderTypeLabel(order)}));ccScheduleSpotTradeRefreshBurst('cancel');ccSpreadsScheduleFastPrivateRefresh(80);diag.postMs=ccSpreadsLatencyNow()-postStarted;ccSpreadsLatencyFinish(diag,'ok')}catch(e){ccOrderDiagnosticPush('cancel',{order,payload,action:'failed',message:'spot cancel failed: '+e.message});ccSpreadsLatencyFinish(diag,'error',e);ccOut('ccSpotOrderStatus','Cancel failed: '+e.message);await loadCcSpotOpenOrders();await loadCcSpotOrderHistory()}}
async function cancelCcFuturesOrder(i){const order=(Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[])[Number(i)];if(!order){ccOut('ccFuturesOrderStatus','Order not found. Refreshing…');loadCcFuturesOpenOrders();return}const payload=ccFuturesCancelPayload(order);if(!payload.orderId&&!payload.clientOrderId){ccOut('ccFuturesOrderStatus','Cannot cancel: CoinCall futures order id is missing.');return}const key=ccFuturesCancelKey(order,payload);const summary=ccFuturesCancelSummary(order,payload);if(CC_FUTURES_CANCEL_PENDING.has(key)){ccOut('ccFuturesOrderStatus','Cancel already sent '+summary);ccSpreadsFuturesCancelStatus(order,'Cancel already sent '+summary,'muted');return}const label=(ccPick(order,['displaySymbol','displayName','symbol','instId','instrument'])||'CoinCall futures order')+' '+(payload.orderId||payload.clientOrderId);if(!ccConfirmOrderAction('Cancel '+label+'?'))return;CC_FUTURES_CANCEL_PENDING.add(key);ccOrderDiagnosticPush('cancel',{order,...payload,action:'sent',message:'futures cancel request sent '+summary});ccRenderFuturesBottomPanels();ccSpreadsFuturesCancelStatus(order,'Cancel sent '+summary,'muted');const diag=ccSpreadsLatencyStart('cancel','futures',ccPick(order,['displaySymbol','displayName','symbol','instId','instrument']));try{ccOut('ccFuturesOrderStatus','Cancel sent '+summary);let res;let backendUsed=true;const apiStarted=ccSpreadsLatencyNow();try{const sel=cc('ccAccountSelect');const qs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';res=await ccApi('/api/admin/coincall/futures/orders/cancel'+qs,{method:'POST',body:JSON.stringify(payload)})}catch(err){backendUsed=false;res=await ccCancelFuturesOrderViaCoincallBrowser(payload)}diag.apiMs=ccSpreadsLatencyNow()-apiStarted;const postStarted=ccSpreadsLatencyNow();CC_FUTURES_CANCEL_PENDING.delete(key);ccRememberFuturesTerminalOrder(order);const accepted=ccOrderResultText('Cancel accepted',res,{symbol:ccPick(order,['displaySymbol','displayName','symbol','instId','instrument']),type:ccFuturesOrderTypeLabel(order)})+(backendUsed?' · backend path used':' · browser path used');ccOrderDiagnosticPush('cancel',{order,...payload,action:'accepted',message:'futures cancel accepted'});ccOut('ccFuturesOrderStatus',accepted);ccSpreadsFuturesCancelStatus(order,accepted,'success');const prev=Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders.slice():[];ccLastFuturesOpenOrders=prev.filter(o=>!ccFuturesOrderSame(o,order));ccFuturesOpenOrdersDiag('manual-cancel-remove',prev,ccLastFuturesOpenOrders,{order,...payload,action:'local-remove',message:'local futures open order removed after cancel accepted'});ccRenderFuturesBottomPanels();drawCcChart('ccFuturesChart');ccScheduleFuturesOrderRefreshBurst('cancel');ccSpreadsScheduleFastPrivateRefresh(80);diag.postMs=ccSpreadsLatencyNow()-postStarted;ccSpreadsLatencyFinish(diag,'ok')}catch(e){ccOrderDiagnosticPush('cancel',{order,...payload,action:'failed',message:'futures cancel failed: '+e.message});CC_FUTURES_CANCEL_PENDING.delete(key);ccRenderFuturesBottomPanels();ccSpreadsFuturesCancelStatus(order,'Cancel failed: '+e.message,'danger');ccSpreadsLatencyFinish(diag,'error',e);ccOut('ccFuturesOrderStatus','Cancel failed: '+e.message);await loadCcFuturesOpenOrders()}}
document.querySelectorAll('[data-cc-spot-panel]').forEach(btn=>btn.addEventListener('click',()=>ccSetBottomPanel('spot',btn.dataset.ccSpotPanel)));document.querySelectorAll('[data-cc-futures-panel]').forEach(btn=>btn.addEventListener('click',()=>ccSetBottomPanel('futures',btn.dataset.ccFuturesPanel)));document.querySelectorAll('[data-cc-contract-market]').forEach(btn=>btn.addEventListener('click',()=>{const target=document.querySelector('[data-cc-trade="'+btn.dataset.ccContractMarket+'"]');if(target)target.click();else ccSyncFuturesMarketChips()}));
document.querySelectorAll('.ccTradingNonZeroToggle').forEach(el=>el.addEventListener('change',()=>{ccSyncTradingNonZeroToggles(el.checked);if(ccLastAssetsSummary)renderCcAssets(ccLastAssetsSummary)}));
cc('ccTradeSettingsBtn')?.addEventListener('click',ccOpenTradeSettings);cc('ccFuturesTradeSettingsBtn')?.addEventListener('click',ccOpenTradeSettings);cc('ccTradeSettingsClose')?.addEventListener('click',ccCloseTradeSettings);cc('ccTradeSettingsModal')?.addEventListener('click',e=>{if(e.target?.id==='ccTradeSettingsModal')ccCloseTradeSettings()});cc('ccSettingOrderConfirmation')?.addEventListener('click',()=>ccToggleTradeSetting('orderConfirmation'));cc('ccSettingFilledNotification')?.addEventListener('click',()=>ccToggleTradeSetting('filledNotification'));cc('ccTestFillSound')?.addEventListener('click',()=>{ccFillAudioContext();ccPlayFillSound(true);ccSetStatus('ccTopStatus','Fill sound test played')});cc('ccFillSoundSelect')?.addEventListener('change',e=>{CC_TRADE_SETTINGS.fillSound=e.target.value||'triple';ccSaveTradeSettings();ccPlayFillSound(true);ccSyncTradeSettingsUi()});ccSyncTradeSettingsUi();document.querySelectorAll('[data-cc-trade-chart-mode]').forEach(btn=>btn.addEventListener('click',()=>ccTradeSetChartMode(btn.dataset.ccTradeChartMode||'candles')));cc('ccTradeMarketInst')?.addEventListener('change',()=>ccTradeProxySelect('inst'));cc('ccTradeMarketBar')?.addEventListener('change',()=>ccTradeProxySelect('bar'));cc('ccSpotOrdType')?.addEventListener('change',ccUpdateSpotTradeUi);cc('ccSpotPostOnlyBtn')?.addEventListener('click',()=>{CC_TRADE_STATE.spotPostOnly=!CC_TRADE_STATE.spotPostOnly;ccUpdateSpotTradeUi()});cc('ccSpotAmountMin')?.addEventListener('click',ccSetSpotAmountMin);cc('ccSpotAmountMax')?.addEventListener('click',ccSetSpotAmountMax);cc('ccFuturesOrdType')?.addEventListener('change',ccRenderFuturesOrderTypeUi);cc('ccFuturesPxUp')?.addEventListener('click',()=>ccStepFuturesPx(1));cc('ccFuturesPxDown')?.addEventListener('click',()=>ccStepFuturesPx(-1));cc('ccFuturesAmountUnit')?.addEventListener('change',ccRenderFuturesAmountUi);cc('ccFuturesBookPrecision')?.addEventListener('change',()=>{const value=cc('ccFuturesBookPrecision')?.value||'';ccFuturesSaveBookSegPref(ccFuturesBookSegBase(),value);if(ccActiveMarket()==='futures')loadCcBook('futures')});cc('ccFuturesAdjustLeverageBtn')?.addEventListener('click',ccOpenLeverageModal);cc('ccLeverageModalClose')?.addEventListener('click',ccCloseLeverageModal);cc('ccLeverageModal')?.addEventListener('click',e=>{if(e.target?.id==='ccLeverageModal')ccCloseLeverageModal()});cc('ccLeverageSlider')?.addEventListener('input',ccUpdateLeverageModal);cc('ccLeverageMinus')?.addEventListener('click',()=>{const slider=cc('ccLeverageSlider');if(!slider)return;slider.value=String(Math.max(Number(slider.min||1),Number(slider.value||1)-1));ccUpdateLeverageModal()});cc('ccLeveragePlus')?.addEventListener('click',()=>{const slider=cc('ccLeverageSlider');if(!slider)return;slider.value=String(Math.min(Number(slider.max||125),Number(slider.value||1)+1));ccUpdateLeverageModal()});cc('ccLeverageConfirm')?.addEventListener('click',async()=>{const slider=cc('ccLeverageSlider');const btn=cc('ccLeverageConfirm');if(!slider||!btn)return;btn.disabled=true;const ok=await ccSetFuturesLeverageDirect(slider.value);btn.disabled=false;if(ok)ccCloseLeverageModal()});document.addEventListener('keydown',e=>{if(e.key==='Escape'){ccCloseTradeSettings();ccCloseLeverageModal()}});
document.querySelectorAll('[data-cc-md-tab]').forEach(b=>b.addEventListener('click',()=>ccSetMarketDataTab(b.dataset.ccMdTab||'spot')));document.querySelectorAll('[data-cc-md-mode]').forEach(b=>b.addEventListener('click',()=>ccSetMarketDataMode(b.dataset.ccMdMode||'candles')));cc('ccMarketDataSymbol')?.addEventListener('change',()=>{const market=ccMarketDataActiveTab(),symbol=String(cc('ccMarketDataSymbol')?.value||'').trim();if(market==='futures'){const futuresSel=cc('ccFuturesInst');if(futuresSel&&symbol&&futuresSel.value!==symbol){futuresSel.value=symbol;futuresSel.dispatchEvent(new Event('change',{bubbles:true}));return}}ccSaveMarketDataSelection();loadCcMarketDataHistory(true)});cc('ccMarketDataBar')?.addEventListener('change',()=>{ccSaveMarketDataSelection();loadCcMarketDataHistory()});window.addEventListener('resize',()=>{if(ccIsMarketDataPanelActive())ccMarketDataDraw()});ccMarketDataSyncControls(ccUiStateLoad().dataMarketTab||'spot');ccSetMarketDataMode(ccUiStateLoad().dataChartMode||'candles',false);
ccUpdateSpotTradeUi();ccRenderFuturesAmountUi();ccStartFuturesOpenOrdersSync();ccEnsureFuturesOrderEventStream();ccTradeSyncTopControls();ccBindViewportWheelToMain();cc('ccAssetsFundingSymbol')?.addEventListener('change',()=>{loadCcAssetsFundingHistory();const one=cc('ccAssetsFunding1mSymbol');if(one){one.value=cc('ccAssetsFundingSymbol')?.value||one.value;if(ccIsAssetsFunding1mActive())loadCcAssetsFunding1m()}});cc('ccAssetsFunding1mSymbol')?.addEventListener('change',()=>{const hist=cc('ccAssetsFundingSymbol');if(hist)hist.value=cc('ccAssetsFunding1mSymbol')?.value||hist.value;loadCcAssetsFunding1m()});cc('ccAccountSelect')?.addEventListener('change',()=>{if(ccIsAssetsFundingHistoryActive())loadCcAssetsFundingHistory();if(ccIsAssetsFunding1mActive())loadCcAssetsFunding1m();ccUpdatePnlUsdAutoRefresh()});document.querySelector('[data-cc-action="assetsFundingRefresh"]')?.addEventListener('click',loadCcAssetsFundingHistory);document.querySelector('[data-cc-action="assetsFunding1mRefresh"]')?.addEventListener('click',loadCcAssetsFunding1m);document.querySelectorAll('[data-cc-assets-funding-days]').forEach(b=>b.addEventListener('click',()=>{ccAssetsFunding1mDays=Number(b.dataset.ccAssetsFundingDays)||3;document.querySelectorAll('[data-cc-assets-funding-days]').forEach(x=>x.classList.toggle('active',x===b));loadCcAssetsFunding1m()}));document.addEventListener('visibilitychange',()=>{const fundingActive=ccAssetsFunding1mAutoRefreshActive(),pnlActive=ccPnlUsdAutoRefreshActive();ccUpdateAssetsFunding1mAutoRefresh();ccUpdatePnlUsdAutoRefresh();if(fundingActive)loadCcAssetsFunding1m();if(pnlActive)loadCcPnl()});window.addEventListener('focus',()=>{if(ccAssetsFunding1mAutoRefreshActive())loadCcAssetsFunding1m();if(ccPnlUsdAutoRefreshActive())loadCcPnl()});window.addEventListener('resize',()=>{if(ccIsAssetsFundingHistoryActive())ccDrawAssetsFundingChart(ccLastAssetsFundingRows);if(ccIsAssetsFunding1mActive())ccRenderAssetsFunding1m(ccLastAssetsFunding1mRows,null)});
document.addEventListener('pointerdown',e=>{if(ccHandleSpreadsCancelEvent(e))return;ccHandleSpreadsBottomPanelEvent(e)},true);document.addEventListener('mousedown',e=>{if(ccHandleSpreadsCancelEvent(e))return;ccHandleSpreadsBottomPanelEvent(e)},true);
cc('ccAccountSelect')?.addEventListener('change',ccSpreadsResetFundingHistoryPreload,true);
cc('ccFuturesInst')?.addEventListener('change',ccSpreadsResetFundingHistoryPreload,true);
document.addEventListener('input',e=>{const panel=e.target?.closest?.('#ccSpreadsTradeModal [data-cc-spreads-trade-panel]');if(panel)ccSpreadsRememberTradePanel(panel);},true);
document.addEventListener('change',e=>{const panel=e.target?.closest?.('#ccSpreadsTradeModal [data-cc-spreads-trade-panel]');if(!panel)return;ccSpreadsRememberTradePanel(panel);if(e.target.matches('[data-cc-spreads-spot-ord-type]'))ccSpreadsUpdateSpotTradePanel(panel);if(e.target.matches('[data-cc-spreads-futures-ord-type],[data-cc-spreads-futures-unit],[data-cc-spreads-futures-leverage]'))ccSpreadsUpdateFuturesTradePanel(panel);ccSpreadsRememberTradePanel(panel);},true);
document.addEventListener('change',e=>{const toggle=e.target.closest('[data-cc-spreads-funding-overlay]');if(!toggle)return;const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(toggle))return;ccSpreadsSetFundingOverlay(!!toggle.checked).catch(err=>{CC_SPREADS_CHART_STATE.error='Funding failed: '+err.message;ccSpreadsDrawSpreadChart()})},true);
document.addEventListener('change',e=>{const toggle=e.target.closest('[data-cc-spreads-bid-ask-overlay]');if(!toggle)return;const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(toggle))return;ccSpreadsSetBidAskOverlay(!!toggle.checked)},true);
document.addEventListener('change',e=>{const toggle=e.target.closest('[data-cc-spreads-bidask-diff-overlay]');if(!toggle)return;const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(toggle))return;ccSpreadsSetBidAskDiffOverlay(!!toggle.checked)},true);
document.addEventListener('click',e=>{const toggle=e.target.closest('[data-cc-spreads-chart-mode]');if(!toggle)return;const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(toggle))return;e.preventDefault();e.stopPropagation();ccSpreadsSetPopupChartMode(toggle.dataset.ccSpreadsChartMode)},true);
document.addEventListener('click',e=>{const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(e.target))return;if(ccHandleSpreadsBottomPanelEvent(e))return;const price=e.target.closest('[data-cc-book-price]');if(price&&modal.contains(price)&&ccSpreadsPanelSetBookPrice(price)){e.preventDefault();e.stopPropagation();return}const settings=e.target.closest('[data-cc-spreads-trade-settings]');if(settings){e.preventDefault();e.stopPropagation();ccOpenTradeSettings();return}if(ccHandleSpreadsCancelEvent(e))return;const panel=e.target.closest('[data-cc-spreads-trade-panel]');if(!panel)return;const post=e.target.closest('[data-cc-spreads-spot-post-only]');if(post){e.preventDefault();e.stopPropagation();panel.dataset.ccSpreadsPostOnly=panel.dataset.ccSpreadsPostOnly==='0'?'1':'0';ccSpreadsUpdateSpotTradePanel(panel);return}const min=e.target.closest('[data-cc-spreads-spot-min]');if(min){const input=panel.querySelector('[data-cc-spreads-spot-sz]');const symbol=String(panel.dataset.ccSpreadsTradeSymbol||'').toUpperCase();const base=ccSpreadsSpotParts(symbol).base;if(input){input.value=ccSpotMinSize(base);input.dispatchEvent(new Event('input',{bubbles:true}));input.focus()}e.preventDefault();e.stopPropagation();return}const max=e.target.closest('[data-cc-spreads-spot-max]');if(max){const symbol=String(panel.dataset.ccSpreadsTradeSymbol||'').toUpperCase();const base=ccSpreadsSpotParts(symbol).base;const n=ccSpotBalance(base);const input=panel.querySelector('[data-cc-spreads-spot-sz]');if(input){input.value=Number.isFinite(n)?String(Number(n.toFixed(8))):'';input.dispatchEvent(new Event('input',{bubbles:true}));input.focus()}if(!Number.isFinite(n))ccSpreadsPopupSetStatus(panel,'Max requires loaded CoinCall trading balance for '+base+'.','muted');e.preventDefault();e.stopPropagation();return}const amountMin=e.target.closest('[data-cc-spreads-futures-amount-min]');if(amountMin){const symbol=String(panel.dataset.ccSpreadsTradeSymbol||'').toUpperCase();const base=ccSpreadsFuturesBase(symbol);const input=panel.querySelector('[data-cc-spreads-futures-sz]');if(input){input.value=ccSpreadsFuturesAmountMin(base);input.dispatchEvent(new Event('input',{bubbles:true}));input.focus()}e.preventDefault();e.stopPropagation();return}const step=e.target.closest('[data-cc-spreads-futures-px-step]');if(step){e.preventDefault();e.stopPropagation();ccSpreadsPanelPriceStep(panel,Number(step.dataset.ccSpreadsFuturesPxStep));return}const order=e.target.closest('[data-cc-spreads-order]');if(order){e.preventDefault();e.stopPropagation();ccFillAudioContext();ccSpreadsPlacePopupOrder(panel,order.dataset.ccSpreadsOrder,order.dataset.side);return}},true);
const ccAccountSelectEl=cc('ccAccountSelect');if(ccAccountSelectEl)ccAccountSelectEl.addEventListener('change',()=>{syncCcSelectedAccount();summary();ccMaybeLoadSpotOpenOrders(true);loadCcSpotOrderHistory();loadCcSpotTradeHistory();loadCcFuturesOpenOrders();loadCcFuturesPositions();loadCcFuturesInstruments();loadCcFuturesLeverage();ccRefreshActiveFuturesPanel();ccSpreadsRefresh(true).catch(()=>{});if(document.getElementById('cc-tab-pnl')?.classList.contains('active'))loadCcPnl();ccUpdatePnlUsdAutoRefresh();if(ccActiveAssetTab()==='transfer-records')loadCcTransferRecords()});document.addEventListener('click',e=>{const toggle=e.target.closest('[data-cc-futures-toggle]');if(toggle){const key=String(toggle.dataset.ccFuturesToggle||'').toUpperCase();if(key){ccFuturesExpandedGroups[key]=ccFuturesExpandedGroups[key]===false;ccRenderFuturesBottomPanels()}return}const price=e.target.closest('[data-cc-book-price]');if(price){const market=price.closest('#ccFuturesAsks,#ccFuturesBids')?'futures':'spot';const input=cc(market==='futures'?'ccFuturesPx':'ccSpotPx');if(input){input.value=price.dataset.ccBookPrice||price.textContent.trim();input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();}}const a=e.target.closest('[data-cc-action]'); if(a){const act=a.dataset.ccAction;if(act==='loadAccounts'){ccSyncTradingNonZeroToggles(true);loadAccounts();loadCcFuturesOpenOrders();loadCcFuturesPositions();loadCcFuturesInstruments();loadCcFuturesLeverage();ccRefreshActiveFuturesPanel();}if(act==='testCreds')testCreds();if(act==='saveAccount')saveAccount();if(act==='activateSelectedAccount')activateAccount();if(act==='deleteSelectedAccount')deleteSelectedAccount();if(act==='saveAccountSettings')saveAccountSettings();if(act==='summary')summary();if(act==='transferRecordsRefresh')loadCcTransferRecords();if(act==='loadInstruments'){const m=a.dataset.market||'spot';loadInstruments(m);if(ccActiveMarket()===m){loadCcChart(m);ccStartBookRefresh(m);}else loadCcMarket(m);}}const futuresCancel=e.target.closest('[data-cc-futures-cancel-index]');if(futuresCancel){e.preventDefault();ccFillAudioContext();cancelCcFuturesOrder(futuresCancel.dataset.ccFuturesCancelIndex);return}const cancel=e.target.closest('[data-cc-spot-cancel-index]'); if(cancel){cancelCcSpotOrder(cancel.dataset.ccSpotCancelIndex);return}const o=e.target.closest('[data-cc-order]'); if(o) placeOrder(o.dataset.ccOrder,o.dataset.side);});
const CC_PAGE_STATE_KEY='cc_spreads_page_state_v1';
const CC_PAGE_STATE_CONTROL_SELECTOR='input,select,textarea';
const CC_PAGE_STATE_GROUP_ATTRS=['data-cc-layer','data-cc-trade','data-cc-md-tab','data-cc-md-mode','data-cc-spot-panel','data-cc-futures-panel','data-cc-asset-tab','data-cc-pnl-asset','data-cc-pnl-clearing-currency','data-cc-assets-funding-tab','data-cc-assets-funding-days','data-cc-options-asset','data-cc-futures-ticket','data-cc-spreads-main-days','data-cc-spreads-display-mode','data-cc-spreads-price-source'];
const CC_PAGE_STATE_SECRET_RE=/apikey|api_key|apisecret|api_secret|secret|password|token|credential/i;
function ccPageStateLoad(){try{const raw=localStorage.getItem(CC_PAGE_STATE_KEY);if(!raw)return {controls:{},groups:{}};const parsed=JSON.parse(raw);return parsed&&typeof parsed==='object'?{controls:parsed.controls&&typeof parsed.controls==='object'?parsed.controls:{},groups:parsed.groups&&typeof parsed.groups==='object'?parsed.groups:{}}:{controls:{},groups:{}}}catch{return {controls:{},groups:{}}}}
function ccPageStateSave(next){try{localStorage.setItem(CC_PAGE_STATE_KEY,JSON.stringify(next))}catch{}}
function ccPageStateControlKey(el){if(!el||ccPageStateSkipControl(el))return '';if(el.id)return '#'+el.id;if(el.name)return el.tagName.toLowerCase()+'[name="'+el.name+'"]';if(!el.dataset.ccPageStateKey){const list=Array.from(document.querySelectorAll(CC_PAGE_STATE_CONTROL_SELECTOR)).filter(x=>!ccPageStateSkipControl(x));el.dataset.ccPageStateKey='anon:'+list.indexOf(el)}return el.dataset.ccPageStateKey}
function ccPageStateSkipControl(el){if(!el)return true;const id=(el.id||'')+' '+(el.name||'')+' '+(el.getAttribute('autocomplete')||'')+' '+(el.type||'');return CC_PAGE_STATE_SECRET_RE.test(id)||el.type==='file'||el.type==='submit'||el.type==='button'||el.type==='reset'}
function ccPageStateReadControl(el){const type=String(el.type||'').toLowerCase();if(type==='checkbox'||type==='radio')return {checked:!!el.checked};return {value:el.value}}
function ccPageStateApplyControl(el,saved){if(!el||!saved||typeof saved!=='object')return false;const type=String(el.type||'').toLowerCase();if(type==='checkbox'||type==='radio'){if(typeof saved.checked==='boolean')el.checked=saved.checked;return true}if('value' in saved){if(el.tagName==='SELECT'&&saved.value!==''&&!Array.from(el.options||[]).some(opt=>String(opt.value)===String(saved.value)))return false;el.value=String(saved.value??'');return true}return false}
function ccPageStateSaveControl(el){const key=ccPageStateControlKey(el);if(!key)return;const state=ccPageStateLoad();state.controls[key]=ccPageStateReadControl(el);ccPageStateSave(state)}
function ccPageStateGroupKey(el){if(!el)return null;for(const attr of CC_PAGE_STATE_GROUP_ATTRS){if(el.hasAttribute(attr))return attr}return null}
function ccPageStateGroupValue(el,attr){return el?.getAttribute?.(attr)||''}
function ccPageStateSaveGroup(el){const attr=ccPageStateGroupKey(el);if(!attr)return;const value=ccPageStateGroupValue(el,attr);if(!value)return;const state=ccPageStateLoad();state.groups[attr]=value;ccPageStateSave(state)}
function ccPageStateRestoreControls(){const state=ccPageStateLoad();document.querySelectorAll(CC_PAGE_STATE_CONTROL_SELECTOR).forEach(el=>{const key=ccPageStateControlKey(el);if(key&&state.controls[key])ccPageStateApplyControl(el,state.controls[key])});ccPageStateSyncAfterRestore()}
function ccPageStateCssValue(value){return String(value).replace(/\\/g,'\\\\').replace(/"/g,'\\\"')}
function ccPageStateRestoreGroups(){const state=ccPageStateLoad();for(const [attr,value] of Object.entries(state.groups||{})){if(!CC_PAGE_STATE_GROUP_ATTRS.includes(attr)||!value)continue;const selector='['+attr+'="'+ccPageStateCssValue(value)+'"]';const el=document.querySelector(selector);if(!el||el.classList.contains('active'))continue;if(el.closest('#ccSpreadsTradeModal')&&cc('ccSpreadsTradeModal')?.hidden)continue;if(typeof el.click==='function')el.click()}}
function ccPageStateSyncAfterRestore(){try{ccUpdateSpotTradeUi?.();ccRenderFuturesOrderTypeUi?.();ccRenderFuturesAmountUi?.();ccSyncTradingNonZeroToggles?.(!!document.querySelector('.ccTradingNonZeroToggle')?.checked);ccSyncTradeSettingsUi?.();ccTradeSyncTopControls?.()}catch{}}
function ccPageStateBind(){document.addEventListener('input',e=>{const el=e.target?.closest?.(CC_PAGE_STATE_CONTROL_SELECTOR);if(el)ccPageStateSaveControl(el)},true);document.addEventListener('change',e=>{const el=e.target?.closest?.(CC_PAGE_STATE_CONTROL_SELECTOR);if(el){ccPageStateSaveControl(el);setTimeout(ccPageStateSyncAfterRestore,0)}},true);document.addEventListener('click',e=>{const el=e.target?.closest?.(CC_PAGE_STATE_GROUP_ATTRS.map(attr=>'['+attr+']').join(','));if(el)setTimeout(()=>ccPageStateSaveGroup(el),0)},true);window.addEventListener('beforeunload',()=>{document.querySelectorAll(CC_PAGE_STATE_CONTROL_SELECTOR).forEach(ccPageStateSaveControl)},true)}
ccPageStateRestoreControls();
ccRestoreUiState();
setTimeout(()=>{ccPageStateRestoreControls();ccPageStateRestoreGroups();ccPageStateBind()},0);
if (document.body && document.body.classList.contains("cc-spreads-workstation")) { ccApplyLayerState("trade"); ccApplyTradeState("spreads"); }
ccTradeSetChartMode(ccUiStateLoad().tradeChartMode||'candles',false);
ccSyncTradingNonZeroToggles(true);
const ccSpreadsStandaloneBoot = !!(document.body && document.body.classList.contains("cc-spreads-workstation"));
if(ccSpreadsStandaloneBoot) setTimeout(loadAccounts, 3000);
else loadAccounts();
if(ccIsAssetsFundingHistoryActive())loadCcAssetsFundingHistory();
if(ccIsAssetsFunding1mActive())loadCcAssetsFunding1m();
if(ccActiveAssetTab()==='transfer-records')loadCcTransferRecords();
['ccSpotInst','ccSpotBar','ccFuturesInst','ccFuturesBar'].forEach(id=>{const el=cc(id); if(el) el.addEventListener('change',()=>{const market=id.includes('Futures')?'futures':'spot';const dataPanelActive=!!cc('ccMarketDataPanel')?.classList.contains('active');const dataMarket=ccMarketDataActiveTab();const dataTimeframeBefore=dataPanelActive&&dataMarket===market?String(cc('ccMarketDataBar')?.value||ccMarketDataSavedValue(market,'timeframe')||'').trim():'';if(id==='ccSpotInst'){if(dataPanelActive&&dataMarket==='spot')ccMarketDataViewportPersistActive();ccUiStateSave({spotSymbol:cc('ccSpotInst')?.value||''});ccUpdateSpotTradeUi();loadCcSpotTradeHistory()}if(id==='ccFuturesInst'){const prevSymbol=String(el.dataset.ccPrevValue||CC_CHART_STATE.ccFuturesChart?.chartKey?.split(':')[1]||'').trim();if(prevSymbol){ccFuturesViewportPersistActive();ccFuturesSymbolStateSave(prevSymbol,{timeframe:cc('ccFuturesBar')?.value||''})}if(dataPanelActive&&dataMarket==='futures')ccMarketDataViewportPersistActive();ccFuturesApplySavedSymbolState(cc('ccFuturesInst')?.value||'');el.dataset.ccPrevValue=cc('ccFuturesInst')?.value||'';CC_BOOK_STATE.requestSeq.futures=(CC_BOOK_STATE.requestSeq.futures||0)+1;ccClearBook('futures','...');ccRenderFuturesAmountUi();ccRenderFuturesContractStrip();loadCcFuturesLeverage();loadCcFuturesOpenOrders();ccRefreshActiveFuturesPanel()}if(id==='ccFuturesBar'){ccFuturesViewportPersistActive();ccFuturesSymbolStateSave(cc('ccFuturesInst')?.value||'',{timeframe:cc('ccFuturesBar')?.value||''});if(dataPanelActive&&dataMarket==='futures')ccMarketDataViewportPersistActive()}if(id==='ccSpotBar'&&dataPanelActive&&dataMarket==='spot')ccMarketDataViewportPersistActive();if(ccMarketDataActiveTab()===market){ccSyncMarketDataSymbolOptions(market,ccMarketDataSourceSelect(market,'symbol')?.value||'');ccSyncMarketDataTimeframeOptions(market,dataTimeframeBefore||ccMarketDataSavedValue(market,'timeframe')||ccMarketDataSourceSelect(market,'timeframe')?.value||'');ccSaveMarketDataSelection(market);if(dataPanelActive)loadCcMarketDataHistory()}ccTradeSyncTopControls();if(ccActiveMarket()===market){loadCcChart(market);ccStartBookRefresh(market);}else loadCcMarket(market);});});
['ccSpotInst','ccSpotBar','ccFuturesInst','ccFuturesBar'].forEach(id=>cc(id)?.addEventListener('change',ccTradeSyncTopControls));
cc('ccSpotBar')?.addEventListener('change',()=>ccUiStateSave({spotTimeframe:cc('ccSpotBar')?.value||''}));
cc('ccFuturesBar')?.addEventListener('change',()=>{const value=cc('ccFuturesBar')?.value||'';ccUiStateSave({futuresTimeframe:value});ccFuturesSymbolStateSave(cc('ccFuturesInst')?.value||'',{timeframe:value})});
cc('ccFuturesLeverageSelect')?.addEventListener('change',setCcFuturesLeverage);
window.addEventListener('resize',()=>{clearTimeout(window.__ccResize);window.__ccResize=setTimeout(loadCcCharts,150)});
document.addEventListener('visibilitychange',()=>{const market=CC_BOOK_STATE.active;if(document.hidden||!market)return;ccRefreshCcAccountSnapshotLive(true);if(ccSpreadsIsActive())ccSpreadsRefresh(true).catch(()=>{});if(market==='futures'){loadCcBook('futures');ccScheduleChartRedraw('ccFuturesChart',0);return}if(CC_BOOK_STATE.wantReconnect[market]&&!ccBookSocketOpen(market))ccEnsureBookSocket(market,true)});
ccPlaceContractToolbar();ccSyncFuturesMarketChips();ccTradeSyncTopControls();
ccBindChartVisibilityObservers('ccSpotChart','ccSpotShell');
ccBindChartVisibilityObservers('ccFuturesChart','ccFuturesShell');
if(!ccSpreadsStandaloneBoot){
loadCcChart('spot');
loadCcChart('futures');
const ccInitialMarket=ccActiveMarket();
ccStartBookRefresh(ccInitialMarket);
if(ccInitialMarket==='futures'){ccScheduleChartRedraw('ccFuturesChart',0);ccScheduleChartRedraw('ccFuturesChart',120);}
setInterval(loadCcCharts,60000);ccStartBtcUsdEquityAutoRefresh();ccStartAccountSummaryLiveAutoRefresh();ccUpdateAssetsFunding1mAutoRefresh();ccUpdatePnlUsdAutoRefresh();ccStartMarketDataAutoRefresh();
}else{
setTimeout(()=>{try{ccStartAccountSummaryLiveAutoRefresh();ccUpdatePnlUsdAutoRefresh();}catch{}},3000);
}
ccChartUpdateUtcLabels();
setInterval(ccChartUpdateUtcLabels,1000);
/* BEGIN CC_MARKET_DATA_CONTINUOUS_TIME_OVERRIDE 20260520T1805Z */
(function(){
  const ccMdBucketMs=()=>CC_INTERVAL_MS[String(cc('ccMarketDataBar')?.value||'1m')]||60000;
  const ccMdSlot=(t,bucketMs=ccMdBucketMs())=>Math.floor(Number(t)/bucketMs);
  const ccMdAnnotateRows=rows=>{
    const bucketMs=ccMdBucketMs();
    return (Array.isArray(rows)?rows:[]).map(r=>{
      const t=Number(r&&r.t),slot=ccMdSlot(t,bucketMs);
      if(!Number.isFinite(t)||!Number.isFinite(slot))return null;
      return {...r,__slot:slot};
    }).filter(Boolean).sort((a,b)=>a.__slot-b.__slot||a.t-b.t);
  };
  const ccMdCurrentTotalSlots=rows=>{
    if(!Array.isArray(rows)||!rows.length)return 0;
    const first=Number(rows[0].__slot),last=Number(rows[rows.length-1].__slot);
    return Number.isFinite(first)&&Number.isFinite(last)?Math.max(rows.length,last-first+1):rows.length;
  };
  const ccMdHasCandle=row=>[row&&row.o,row&&row.h,row&&row.l,row&&row.c].every(v=>Number.isFinite(Number(v)));
  const ccMdHasLinear=row=>[row&&row.c,row&&row.mark,row&&row.ask,row&&row.bid].some(v=>Number.isFinite(Number(v)));
  const ccMdHasModeValue=(row,mode)=>mode==='candles'?ccMdHasCandle(row):ccMdHasLinear(row);
  const ccMdLayout=(st,chartW)=>{
    if(!st||!Array.isArray(st.rows)||!st.rows.length)return null;
    const totalSlots=ccMdCurrentTotalSlots(st.rows);
    const visible=ccChartClampVisible(st,totalSlots,chartW);
    const maxPan=Math.max(0,totalSlots-visible);
    const minPan=-ccChartRightBlankCap(visible);
    const panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));
    const rightBlank=Math.max(0,-panOffset);
    const lastSlot=Number(st.rows[st.rows.length-1].__slot);
    const dataEndSlot=lastSlot-Math.max(0,panOffset);
    const displayEndSlot=dataEndSlot+rightBlank;
    const displayStartSlot=displayEndSlot-visible+1;
    return {
      bucketMs:ccMdBucketMs(),
      totalSlots,visible,maxPan,minPan,panOffset,rightBlank,
      firstSlot:Number(st.rows[0].__slot),lastSlot,dataEndSlot,displayStartSlot,displayEndSlot,
      rowsShown:st.rows.filter(r=>Number(r.__slot)>=displayStartSlot&&Number(r.__slot)<=dataEndSlot),
      step:chartW/visible
    };
  };
  const ccMdXForSlot=(layout,padL,slot)=>padL+((slot-layout.displayStartSlot)+.5)*layout.step;
  const ccMdLastVisibleRow=(rowsShown,allRows,mode)=>{
    for(let i=(Array.isArray(rowsShown)?rowsShown.length:0)-1;i>=0;i--){if(ccMdHasModeValue(rowsShown[i],mode))return rowsShown[i]}
    for(let i=(Array.isArray(allRows)?allRows.length:0)-1;i>=0;i--){if(ccMdHasModeValue(allRows[i],mode))return allRows[i]}
    return null;
  };
  const ccMdRowBySlot=(rows,slot,mode)=>{
    const list=Array.isArray(rows)?rows:[];
    for(let i=0;i<list.length;i++){
      const row=list[i];
      if(Number(row&&row.__slot)===Number(slot)&&ccMdHasModeValue(row,mode))return row;
    }
    return null;
  };
  ccMarketDataChartAutoFitPriceRange=function(){
    const st=CC_MARKET_DATA_CHART_STATE;
    if(!st||!Array.isArray(st.rows)||!st.rows.length)return false;
    const layout=ccMdLayout(st,Number(st.view?.chartW)||Math.max(1,Number(st.view?.w||0)-Number(st.view?.padL||12)-Number(st.view?.padR||82)));
    if(!layout||!layout.rowsShown.length)return false;
    const mode=ccMarketDataMode();
    const vals=layout.rowsShown.flatMap(r=>ccMarketDataChartPriceValues(r,mode)).filter(Number.isFinite);
    if(!vals.length)return false;
    const min=Math.min(...vals),max=Math.max(...vals),span=max-min||Math.max(Math.abs(max)*.001,1e-8),pad=Math.max(span*.045,Math.abs((min+max)/2)*1e-7,1e-8);
    st.priceRange={min:min-pad,max:max+pad};
    st.panOffset=layout.panOffset;
    return true;
  };
  ccMarketDataMaybeLoadEarlier=async function(){
    const market=ccMarketDataActiveTab(),symbol=String(cc('ccMarketDataSymbol')?.value||'').trim(),st=CC_MARKET_DATA_CHART_STATE;
    if(!symbol||!ccMarketDataHistoryMatches(market,symbol)||CC_MARKET_DATA_HISTORY_STATE.loading||!CC_MARKET_DATA_HISTORY_STATE.hasMore||!Array.isArray(CC_MARKET_DATA_HISTORY_STATE.minuteRows)||!CC_MARKET_DATA_HISTORY_STATE.minuteRows.length||!st||!Array.isArray(st.rows)||!st.rows.length)return false;
    const totalSlots=Math.max(1,Number(st.view?.totalSlots)||ccMdCurrentTotalSlots(st.rows));
    const visible=Math.max(1,Math.round(Number(st.view?.visible)||Number(st.visibleCandles)||totalSlots));
    const maxPan=Math.max(0,totalSlots-visible);
    if(Math.round(Number(st.panOffset)||0)<Math.max(0,maxPan-1))return false;
    const oldestTs=Number(CC_MARKET_DATA_HISTORY_STATE.minuteRows[0]?.t);
    if(!Number.isFinite(oldestTs)||oldestTs<=0)return false;
    return await ccMarketDataFetchHistoryPage(market,symbol,{beforeTs:oldestTs-1,preserveView:true});
  };
  ccMarketDataFetchHistoryPage=async function(market,symbol,opts={}){
    const tf=String(cc('ccMarketDataBar')?.value||'1m');
    const reset=!!opts.reset,preserveView=!!opts.preserveView,beforeTs=Number(opts.beforeTs);
    const requestSeq=++CC_MARKET_DATA_HISTORY_STATE.requestSeq;
    if(reset||!ccMarketDataHistoryMatches(market,symbol))ccMarketDataResetHistory(market,symbol);
    CC_MARKET_DATA_HISTORY_STATE.loading=true;
    const prevRows=ccMarketDataRowsFromHistory(tf);
    const prevFirstTs=Number(prevRows[0]?.t),prevFirstSlot=Number.isFinite(prevFirstTs)?ccMdSlot(prevFirstTs,CC_INTERVAL_MS[tf]||60000):NaN;
    ccMarketDataUpdateInfoText(market,symbol,tf,prevRows,true);
    ccMarketDataDraw(prevRows);
    try{
      let url='/api/admin/coincall/'+market+'/candles/minute?symbol='+encodeURIComponent(symbol)+'&limit='+CC_MARKET_DATA_PAGE_LIMIT;
      if(Number.isFinite(beforeTs)&&beforeTs>0)url+='&before='+Math.floor(beforeTs);
      const res=await ccApi(url);
      if(requestSeq!==CC_MARKET_DATA_HISTORY_STATE.requestSeq||!ccMarketDataHistoryMatches(market,symbol))return false;
      const incoming=ccMarketDataNormalizeRows(res&&res.rows);
      CC_MARKET_DATA_HISTORY_STATE.minuteRows=reset?incoming:ccMarketDataMergeMinuteRows(incoming,CC_MARKET_DATA_HISTORY_STATE.minuteRows);
      CC_MARKET_DATA_HISTORY_STATE.loading=false;
      CC_MARKET_DATA_HISTORY_STATE.hasMore=incoming.length>=CC_MARKET_DATA_PAGE_LIMIT;
      const rows=ccMarketDataRowsFromHistory(tf);
      const nextFirstTs=Number(rows[0]?.t),nextFirstSlot=Number.isFinite(nextFirstTs)?ccMdSlot(nextFirstTs,CC_INTERVAL_MS[tf]||60000):NaN;
      const addedSlots=!reset&&preserveView&&Number.isFinite(prevFirstSlot)&&Number.isFinite(nextFirstSlot)?Math.max(0,prevFirstSlot-nextFirstSlot):0;
      if(addedSlots>0){
        const st=CC_MARKET_DATA_CHART_STATE;
        st.panOffset=Math.round(Number(st.panOffset)||0)+addedSlots;
        if(st.drag){
          st.drag.panOffset=Math.round(Number(st.drag.panOffset)||0)+addedSlots;
          st.drag.total=Math.max(0,Math.round(Number(st.drag.total)||0)+addedSlots);
        }
      }
      ccMarketDataUpdateInfoText(market,symbol,tf,rows,false);
      ccMarketDataDraw(rows);
      return incoming.length>0;
    }catch(e){
      if(requestSeq!==CC_MARKET_DATA_HISTORY_STATE.requestSeq||!ccMarketDataHistoryMatches(market,symbol))return false;
      CC_MARKET_DATA_HISTORY_STATE.loading=false;
      const rows=ccMarketDataRowsFromHistory(tf);
      ccMarketDataUpdateInfoText(market,symbol,tf,rows,false,'Market data failed: '+e.message);
      ccMarketDataDraw(rows);
      if(!rows.length){
        const meta=cc('ccMarketDataMeta');
        if(meta)meta.textContent=ccMarketDataInfoText;
      }
      return false;
    }
  };
  ccBindMarketDataChartWheel=function(canvas){
    const st=CC_MARKET_DATA_CHART_STATE;
    if(!canvas||!st||st.wheelBound)return;
    st.wheelBound=true;
    const pointFromEvent=event=>{const rect=canvas.getBoundingClientRect();return{x:event.clientX-rect.left,y:event.clientY-rect.top}};
    const inPlot=p=>{const v=st.view;return v&&p.x>=v.padL&&p.x<=v.w-v.padR&&p.y>=v.padT&&p.y<=v.padT+v.chartH};
    const inPriceScale=p=>{const v=st.view;return v&&p.x>v.w-v.padR&&p.x<=v.w&&p.y>=v.padT&&p.y<=v.padT+v.chartH};
    const inTimeScale=p=>{const v=st.view;return v&&p.x>=v.padL&&p.x<=v.w-v.padR&&p.y>v.padT+v.chartH&&p.y<=v.h};
    const totalSlots=()=>Math.max(1,Number(st.view?.totalSlots)||ccMdCurrentTotalSlots(st.rows));
    const maxVisible=()=>{const total=totalSlots(),v=st.view,chartW=v?.chartW||Math.max(1,canvas.clientWidth-94);return ccChartMaxVisibleCandles(total,chartW)};
    const applyHorizontalZoomFromDrag=(drag,p)=>{
      const total=Math.max(1,Number(drag.total)||totalSlots()),max=maxVisible(),min=Math.min(max,20);
      const dx=p.x-drag.x,factor=Math.exp(dx/180),next=Math.round(Math.max(min,Math.min(max,drag.visible*factor)));
      const focusRatio=Math.max(0,Math.min(1,(drag.x-drag.padL)/Math.max(1,drag.chartW)));
      const oldAfter=drag.visible*(1-focusRatio),nextAfter=next*(1-focusRatio);
      const maxPan=Math.max(0,total-next),minPan=-ccChartRightBlankCap(next);
      st.visibleCandles=next;
      st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(drag.panOffset+oldAfter-nextAfter)));
    };
    const applyVerticalZoomFromDrag=(drag,p)=>{
      const dy=p.y-drag.y,factor=Math.exp(dy/160),maxSpan=Math.max(drag.autoSpan*12,drag.span*50,1e-8),minSpan=Math.max(drag.autoSpan*.02,Math.abs(drag.center)*1e-8,1e-8),nextSpan=Math.max(minSpan,Math.min(maxSpan,drag.span*factor));
      st.priceRange={min:drag.center-nextSpan/2,max:drag.center+nextSpan/2};
    };
    canvas.addEventListener('wheel',event=>{
      if(st.drag){event.preventDefault();return}
      const total=totalSlots();
      if(!total)return;
      const max=maxVisible(),current=ccChartClampVisible(st,total,max),factor=event.deltaY<0?.82:1.22,min=Math.min(max,20),next=Math.round(Math.max(min,Math.min(max,current*factor)));
      if(next!==current){
        event.preventDefault();
        st.visibleCandles=next;
        const maxPan=Math.max(0,total-next),minPan=-ccChartRightBlankCap(next);
        st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(Number(st.panOffset)||0)));
        ccMarketDataDraw();
      }
    },{passive:false});
    canvas.addEventListener('dblclick',event=>{
      if(event.button!==0)return;
      const p=pointFromEvent(event);
      if(!inPriceScale(p))return;
      event.preventDefault();
      event.stopPropagation();
      st.drag=null;
      if(ccMarketDataChartAutoFitPriceRange())ccMarketDataDraw();
    });
    canvas.addEventListener('mousedown',event=>{
      if(event.button!==0)return;
      const p=pointFromEvent(event),v=st.view;
      if(!v)return;
      const mode=inPriceScale(p)?'price-scale':(inTimeScale(p)?'time-scale':'pan');
      st.drag={mode,x:p.x,y:p.y,padL:v.padL,chartW:v.chartW,panOffset:v.panOffset,min:v.min,max:v.max,center:(v.min+v.max)/2,span:v.span,autoSpan:Math.max(1e-8,v.span),step:v.step,chartH:v.chartH,visible:v.visible,total:Number(v.totalSlots)||totalSlots()};
      canvas.style.cursor=mode==='price-scale'?'ns-resize':(mode==='time-scale'?'ew-resize':'grabbing');
      event.preventDefault();
    });
    window.addEventListener('mousemove',event=>{
      const drag=st.drag;
      if(!drag)return;
      const p=pointFromEvent(event);
      if(drag.mode==='price-scale')applyVerticalZoomFromDrag(drag,p);
      else if(drag.mode==='time-scale')applyHorizontalZoomFromDrag(drag,p);
      else{
        const total=Math.max(1,Number(drag.total)||totalSlots()),visible=Math.max(1,Math.round(Number(drag.visible)||Number(st.visibleCandles)||total));
        st.visibleCandles=visible;
        const maxPan=Math.max(0,total-visible),minPan=-ccChartRightBlankCap(visible),dx=p.x-drag.x,dy=p.y-drag.y;
        st.panOffset=Math.max(minPan,Math.min(maxPan,Math.round(drag.panOffset+dx/Math.max(1,drag.step))));
        const priceShift=dy/Math.max(1,drag.chartH)*drag.span;
        st.priceRange={min:drag.min+priceShift,max:drag.max+priceShift};
        st.crosshair={x:p.x,y:p.y};
      }
      ccMarketDataDraw();
      event.preventDefault();
    });
    canvas.addEventListener('mousemove',event=>{
      if(st.drag)return;
      const p=pointFromEvent(event);
      st.crosshair=p;
      canvas.style.cursor=inPriceScale(p)?'ns-resize':(inTimeScale(p)?'ew-resize':(inPlot(p)?'grab':'crosshair'));
      ccMarketDataDraw();
    });
    window.addEventListener('mouseup',()=>{if(st.drag){st.drag=null;canvas.style.cursor='grab'}});
    canvas.addEventListener('mouseleave',()=>{if(!st.drag){st.crosshair=null;canvas.style.cursor='crosshair';ccMarketDataDraw()}});
  };
  ccMarketDataDraw=function(rows){
    ccLastMarketDataRows=rows||ccLastMarketDataRows;
    const canvas=cc('ccMarketDataChart'),wrap=canvas&&canvas.parentElement,st=CC_MARKET_DATA_CHART_STATE;
    if(!canvas||!wrap||!st)return;
    ccRenderMarketDataLegend();
    ccBindMarketDataChartWheel(canvas);
    const data=Array.isArray(ccLastMarketDataRows)?ccLastMarketDataRows:[],mode=ccMarketDataMode(),chartKey=ccMarketDataChartKey();
    if(st.chartKey!==chartKey){
      st.chartKey=chartKey;st.priceRange=null;st.panOffset=-8;st.visibleCandles=86;st.crosshair=null;st.drag=null;st.lastPersistSig='';
      ccMarketDataViewportRestore(chartKey);
    }
    st.rows=ccMdAnnotateRows(data.map(r=>({
      t:Number(r.t),o:ccMarketDataNumberOrNaN(r&&r.o),h:ccMarketDataNumberOrNaN(r&&r.h),l:ccMarketDataNumberOrNaN(r&&r.l),c:ccMarketDataNumberOrNaN(r&&r.c),
      mark:ccMarketDataSeriesValue(r,'mark','C'),ask:ccMarketDataSeriesValue(r,'ask','C'),bid:ccMarketDataSeriesValue(r,'bid','C'),
      markO:ccMarketDataSeriesValue(r,'mark','O'),markH:ccMarketDataSeriesValue(r,'mark','H'),markL:ccMarketDataSeriesValue(r,'mark','L'),markC:ccMarketDataSeriesValue(r,'mark','C'),
      askO:ccMarketDataSeriesValue(r,'ask','O'),askH:ccMarketDataSeriesValue(r,'ask','H'),askL:ccMarketDataSeriesValue(r,'ask','L'),askC:ccMarketDataSeriesValue(r,'ask','C'),
      bidO:ccMarketDataSeriesValue(r,'bid','O'),bidH:ccMarketDataSeriesValue(r,'bid','H'),bidL:ccMarketDataSeriesValue(r,'bid','L'),bidC:ccMarketDataSeriesValue(r,'bid','C')
    })).filter(r=>Number.isFinite(r.t)&&(ccMdHasCandle(r)||ccMdHasLinear(r))));
    const dpr=window.devicePixelRatio||1,w=Math.max(320,(wrap.clientWidth||canvas.clientWidth||900)-16),h=Number(canvas.getAttribute('height'))||420;
    canvas.style.width=w+'px';canvas.width=Math.floor(w*dpr);canvas.height=Math.floor(h*dpr);
    const ctx=canvas.getContext('2d');
    ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);ctx.fillStyle='#090B0D';ctx.fillRect(0,0,w,h);
if(!st.rows.length){if(mode!=='linear'){ctx.fillStyle='#64748b';ctx.font='13px Segoe UI';ctx.textAlign='center';ctx.fillText('No stored chart data',w/2,h/2)}ccMarketDataSetOhlc(null);return}
    const padL=12,padR=mode==='linear'?118:82,padT=12,padB=46,chartW=Math.max(1,w-padL-padR),chartH=h-padT-padB;
    const layout=ccMdLayout(st,chartW);
    if(!layout||!layout.rowsShown.length){ccMarketDataSetOhlc(null);return}
    st.panOffset=layout.panOffset;
    const rowsShown=layout.rowsShown;
    const vals=rowsShown.flatMap(r=>ccMarketDataChartPriceValues(r,mode)).filter(Number.isFinite);
    if(!vals.length){ccMarketDataSetOhlc(null);return}
    const autoMin=Math.min(...vals),autoMax=Math.max(...vals),autoSpan=autoMax-autoMin||Math.max(Math.abs(autoMax)*.001,1e-8),manual=st.priceRange,useManual=manual&&Number.isFinite(manual.min)&&Number.isFinite(manual.max)&&manual.max>manual.min,min=useManual?manual.min:autoMin-autoSpan*.045,max=useManual?manual.max:autoMax+autoSpan*.045,span=max-min||1;
    const y=v=>padT+(max-v)/span*chartH,step=layout.step,crisp=v=>Math.round(v)+.5,timeAxisY=h-16,xForSlot=slot=>ccMdXForSlot(layout,padL,slot);
    st.view={padL,padR,padT,padB,chartW,chartH,w,h,visible:layout.visible,total:st.rows.length,totalSlots:layout.totalSlots,panOffset:layout.panOffset,rightBlank:layout.rightBlank,startSlot:layout.displayStartSlot,endSlot:layout.displayEndSlot,dataEndSlot:layout.dataEndSlot,min,max,span,step,bucketMs:layout.bucketMs};
    ctx.strokeStyle='rgba(42,46,53,.55)';ctx.lineWidth=1;ctx.font='13px Segoe UI';ctx.textBaseline='middle';
    for(let i=0;i<5;i++){
      const yy=crisp(padT+chartH*i/4);
      ctx.beginPath();ctx.moveTo(padL,yy);ctx.lineTo(w-padR,yy);ctx.stroke();
      ctx.fillStyle='#8A8F98';ctx.textAlign='left';ctx.fillText(ccChartFmt(max-span*i/4),w-padR+8,yy);
    }
    ctx.textAlign='center';ctx.textBaseline='alphabetic';
    const displayedSlots=Math.max(1,layout.dataEndSlot-layout.displayStartSlot+1),tickCount=Math.min(5,displayedSlots);
    for(let i=0;i<tickCount;i++){
      const slot=tickCount===1?layout.displayStartSlot:Math.round(layout.displayStartSlot+i*(displayedSlots-1)/(tickCount-1));
      const xx=crisp(xForSlot(slot));
      ctx.strokeStyle='rgba(42,46,53,.55)';
      ctx.beginPath();ctx.moveTo(xx,padT);ctx.lineTo(xx,padT+chartH);ctx.stroke();
      ctx.fillStyle='#8A8F98';ctx.fillText(ccUtcHm(slot*layout.bucketMs),xx,timeAxisY);
    }
    const bodyW=Math.max(2,Math.min(8,step*.62));
    if(mode==='candles'){
      rowsShown.forEach(r=>{
        if(!ccMdHasCandle(r))return;
        const xx=crisp(xForSlot(r.__slot)),up=r.c>=r.o,color=up?'#45C4A5':'#F04E60';
        ctx.strokeStyle=color;ctx.fillStyle=color;
        ctx.beginPath();ctx.moveTo(xx,y(r.h));ctx.lineTo(xx,y(r.l));ctx.stroke();
        const top=y(Math.max(r.o,r.c)),bot=y(Math.min(r.o,r.c));
        ctx.fillRect(xx-bodyW/2,top,bodyW,Math.max(1,bot-top));
      });
    }
    const line=(key,color,width=CC_LINEAR_SIDE_WIDTH)=>{
      ctx.save();ctx.strokeStyle=color;ctx.fillStyle=color;ctx.lineWidth=width;
      let points=[],count=0,last=null,lastSlot=null;
      const flush=()=>{if(points.length){ctx.beginPath();ccCanvasTraceStepPath(ctx,points);ctx.stroke()}points=[]};
      rowsShown.forEach(r=>{
        const v=Number(r[key]),slot=Number(r.__slot);
        if(!Number.isFinite(v)||!Number.isFinite(slot)){flush();lastSlot=null;return}
        if(lastSlot!==null&&slot>lastSlot+1)flush();
        const point={x:xForSlot(slot),y:y(v)};
        points.push(point);last=point;lastSlot=slot;count++;
      });
      flush();
      if(count===1&&last){ctx.beginPath();ctx.arc(last.x,last.y,3,0,Math.PI*2);ctx.fill()}
      ctx.restore();
    };
    if(mode==='linear'){line('ask',CC_LINEAR_ASK_COLOR);line('bid',CC_LINEAR_BID_COLOR);line('mark',CC_LINEAR_MARK_COLOR);line('c',CC_LINEAR_CLOSE_COLOR,CC_LINEAR_PRICE_WIDTH)}
    const lastRow=ccMdLastVisibleRow(rowsShown,st.rows,mode),priceMarkers=[];
    if(mode==='candles'&&lastRow&&Number.isFinite(lastRow.c)){
      const candleColor=lastRow.c>=lastRow.o?'#45C4A5':'#F04E60';
      priceMarkers.push({value:lastRow.c,y:y(lastRow.c),bg:candleColor,lineColor:candleColor,fg:'#FFFFFF',startX:lastRow.__slot>=layout.displayStartSlot&&lastRow.__slot<=layout.displayEndSlot?crisp(xForSlot(lastRow.__slot)):w-padR});
    }else if(mode==='linear'&&lastRow){
      const pushMarker=(value,color,slot)=>{if(Number.isFinite(value))priceMarkers.push({value,y:y(value),bg:color,lineColor:color,fg:'#FFFFFF',startX:slot>=layout.displayStartSlot&&slot<=layout.displayEndSlot?crisp(xForSlot(slot)):w-padR})};
      pushMarker(Number(lastRow.ask),CC_LINEAR_ASK_COLOR,Number(lastRow.__slot));
      pushMarker(Number(lastRow.mark),CC_LINEAR_MARK_COLOR,Number(lastRow.__slot));
      pushMarker(Number(lastRow.c),CC_LINEAR_CLOSE_COLOR,Number(lastRow.__slot));
      pushMarker(Number(lastRow.bid),CC_LINEAR_BID_COLOR,Number(lastRow.__slot));
    }
    ccDrawStackedPriceMarkers(ctx,priceMarkers,{padL,chartH,padT,w,lineRight:w-padR,tagX:w-padR+4});
    let legendRow=ccMdLastVisibleRow(rowsShown,st.rows,mode);
    const ch=st.crosshair;
    if(ch&&Number.isFinite(ch.x)&&Number.isFinite(ch.y)&&ch.x>=padL&&ch.x<=w-padR&&ch.y>=padT&&ch.y<=padT+chartH){
      const rawCx=Math.max(padL,Math.min(w-padR,ch.x)),cy=Math.max(padT,Math.min(padT+chartH,ch.y)),price=max-(cy-padT)/chartH*span;
      const slot=Math.max(layout.displayStartSlot,Math.min(layout.displayEndSlot,Math.round(layout.displayStartSlot+(rawCx-padL-step/2)/step)));
      const cx=xForSlot(slot);
      legendRow=ccMdRowBySlot(rowsShown,slot,mode);
      const timeLabel=ccUtcMdHms(slot*layout.bucketMs);
      ctx.save();ctx.strokeStyle='rgba(203,213,225,.82)';ctx.lineWidth=1;ctx.setLineDash([3,3]);
      ctx.beginPath();ctx.moveTo(cx,padT);ctx.lineTo(cx,padT+chartH);ctx.moveTo(padL,cy);ctx.lineTo(w-padR,cy);ctx.stroke();ctx.setLineDash([]);
      ccDrawPriceTag(ctx,w-padR+4,cy,ccChartFmt(price),'#64748B','#F8FAFC',w,padT,chartH);
      ctx.font='700 12px Segoe UI';
      const tw=Math.min(148,Math.max(100,ctx.measureText(timeLabel).width+12)),tx=Math.max(padL,Math.min(w-padR-tw,cx-tw/2));
      ctx.fillStyle='#64748B';ctx.fillRect(tx,h-padB+12,tw,20);
      ctx.fillStyle='#F8FAFC';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(timeLabel,tx+tw/2,h-padB+22);
      ctx.restore();
    }
    ccMarketDataSetOhlc(legendRow);
    ccMarketDataViewportPersistSoon();
    if(st.drag&&st.drag.mode==='pan'&&CC_MARKET_DATA_HISTORY_STATE.hasMore&&!CC_MARKET_DATA_HISTORY_STATE.loading)ccMarketDataMaybeLoadEarlier();
  };
})();
/* END CC_MARKET_DATA_CONTINUOUS_TIME_OVERRIDE 20260520T1805Z */


const CC_SPREADS_HEADER_DATA = { BTC: { columns: [], perp: null, spot: null }, ETH: { columns: [], perp: null, spot: null } };
const CC_SPREADS_SPOT_PRICE_CACHE = { BTC: { price: NaN, ts: 0, rows: [] }, ETH: { price: NaN, ts: 0, rows: [] } };
const CC_SPREADS_LEVERAGE_CACHE = {};
const CC_SPREADS_DISPLAY_MODE_KEY = 'cc_spreads_display_mode_v1';
const CC_SPREADS_PRICE_SOURCE_KEY = 'cc_spreads_price_source_v1';
const CC_SPREADS_MAIN_CHART_SETTINGS_KEY = 'cc_spreads_main_chart_settings_v1';
let ccSpreadsHeaderAsset = 'BTC';
let ccSpreadsDisplayMode = 'usd';
let ccSpreadsPriceSource = 'mark';
let ccSpreadsClockTimer = 0;
let ccSpreadsAutoRefreshTimer = 0;
let ccSpreadsMainLiveTickTimer = 0;
const CC_SPREADS_REFRESH_MS = 15000;
const CC_SPREADS_MAIN_LIVE_TICK_MS = 1000;
const CC_SPREADS_MATRIX_REALTIME_MARKER = 'CC_SPREADS_MATRIX_REALTIME_MONTHLY_BTC_ETH_20260603T0956Z';
const CC_SPREADS_MATRIX_BID_ASK_DIAGNOSTIC_MARKER = 'CC_SPREADS_MATRIX_BID_ASK_DIAGNOSTIC_20260603T1029Z';
const CC_SPREADS_MATRIX_BOOK_SOURCE_MARKER = 'CC_SPREADS_MATRIX_BOOK_SOURCE_FUTURES_ORDER_BOOK_20260603T1040Z';
const CC_SPREADS_ROW_PAIR_VISIBILITY_MARKER = 'CC_SPREADS_ROW_PAIR_VISIBILITY_20260603T1638Z';
const CC_SPREADS_MATRIX_WS = { socket:null, heartbeat:0, reconnect:0, seq:0, symbols:new Set(), subscribed:new Set(), status:'idle', lastEventAt:0, lastErrorAt:0, books:new Map() };
const CC_SPREADS_MATRIX_SPOT_WS = { socket:null, heartbeat:0, reconnect:0, seq:0, symbols:new Set(), subscribed:new Set(), status:'idle', lastEventAt:0, lastErrorAt:0, books:new Map() };
const CC_SPREADS_MATRIX_QUOTE_CACHE = new Map();
const CC_SPREADS_MATRIX_DIRTY_SYMBOLS = new Set();
const CC_SPREADS_MATRIX_DIRTY_SPOT_INDEX_SYMBOLS = new Set();
let ccSpreadsMatrixPatchRaf = 0;
let ccSpreadsLoadPromise = null;
let ccSpreadsLiveRefreshTimer = 0;
let ccSpreadsLiveRefreshLastAt = 0;
const CC_SPREADS_HEADER_CACHE_KEY = 'cc_spreads_header_quote_cache_v1';
const CC_SPREADS_EMBEDDED_HEADER_CACHE = {"ts":1782304870240,"quoteRows":[{"contractId":1,"symbol":"BTCUSD","symbolName":"BTC/USD","displayName":"BTCUSDT Perp","baseToken":"BTC","quoteToken":"USD","price":62824,"changeRate":0.0076,"changePrice":473.5,"markPrice":62838.5002971,"indexPrice":62860.80358696,"price24hHigh":63225.7,"price24hLow":61966.3,"volumeUsd24h":8890583.82561,"volume24h":141.9473,"ts":1675408117600,"price24hOpen":56541.6,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BTCUSD","deliveryKind":"E"},{"contractId":2,"symbol":"ETHUSD","symbolName":"ETH/USD","displayName":"ETHUSDT Perp","baseToken":"ETH","quoteToken":"USD","price":1676.5,"changeRate":0.0115,"changePrice":19.03,"markPrice":1676.26815891,"indexPrice":1676.71,"price24hHigh":1689.77,"price24hLow":1644.77,"volumeUsd24h":1204027.9992,"volume24h":723.102,"ts":1675408117600,"price24hOpen":1508.85,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ETHUSD","deliveryKind":"E"},{"contractId":15,"symbol":"SOLUSD","symbolName":"SOL/USD","displayName":"SOLUSDT Perp","baseToken":"SOL","quoteToken":"USD","price":69.68,"changeRate":0.0084,"changePrice":0.58,"markPrice":69.69,"indexPrice":69.71204091,"price24hHigh":70.29,"price24hLow":68.37,"volumeUsd24h":78859.981,"volume24h":1137.7,"ts":1675408117600,"price24hOpen":62.712,"icon":"https://file.coincall.com/statics/symbol/SOL.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SOLUSD","deliveryKind":"E"},{"contractId":57,"symbol":"SUIUSD","symbolName":"SUI/USD","displayName":"SUIUSDT Perp","baseToken":"SUI","quoteToken":"USD","price":0.7002,"changeRate":-0.002,"changePrice":-0.0014,"markPrice":0.70040353,"indexPrice":0.70061654,"price24hHigh":0.7126,"price24hLow":0.692,"volumeUsd24h":252077.00013,"volume24h":358736,"ts":1675408117600,"price24hOpen":0.63018,"icon":"https://file.coincall.com/statics/symbol/SUI.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SUIUSD","deliveryKind":"E"},{"contractId":4423,"symbol":"LITUSD","symbolName":"LIT/USD","displayName":"LITUSDT Perp","baseToken":"LIT","quoteToken":"USD","price":1.557,"changeRate":0.0006,"changePrice":0.001,"markPrice":1.557227,"indexPrice":1.557878,"price24hHigh":1.588,"price24hLow":1.488,"volumeUsd24h":46648.4314,"volume24h":30297.49,"ts":1767757517824,"price24hOpen":1.4013,"icon":"https://file.coincall.com/statics/symbol/LIT.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"LITUSD","deliveryKind":"E"},{"contractId":4378,"symbol":"WLFIUSD","symbolName":"WLFI/USD","displayName":"WLFIUSDT Perp","baseToken":"WLFI","quoteToken":"USD","price":0.0586,"changeRate":0.0209,"changePrice":0.0012,"markPrice":0.05867518,"indexPrice":0.05870341,"price24hHigh":0.0602,"price24hLow":0.0572,"volumeUsd24h":38923.7748,"volume24h":661831,"ts":1755989301342,"price24hOpen":0.05274,"icon":"https://file.coincall.com/statics/symbol/WLFI.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"WLFIUSD","deliveryKind":"E"},{"contractId":4424,"symbol":"XAGUSD","symbolName":"XAG/USD","displayName":"XAGUSDT Perp","baseToken":"XAG","quoteToken":"USD","price":58.98,"changeRate":-0.0502,"changePrice":-3.12,"markPrice":58.93,"indexPrice":58.83632083,"price24hHigh":62.57,"price24hLow":58.84,"volumeUsd24h":2912113.22013,"volume24h":47582.966,"ts":1767776638878,"price24hOpen":53.082,"icon":"https://file.coincall.com/statics/symbol/XAG.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"XAGUSD","deliveryKind":"E"},{"contractId":93,"symbol":"AAVEUSD","symbolName":"AAVE/USD","displayName":"AAVEUSDT Perp","baseToken":"AAVE","quoteToken":"USD","price":76.72,"changeRate":0.0616,"changePrice":4.45,"markPrice":76.87654848,"indexPrice":76.83,"price24hHigh":78.39,"price24hLow":71.12,"volumeUsd24h":163754.737,"volume24h":2222.8,"ts":1675408117600,"price24hOpen":69.048,"icon":"https://file.coincall.com/statics/symbol/AAVE.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"AAVEUSD","deliveryKind":"E"},{"contractId":16,"symbol":"DOGEUSD","symbolName":"DOGE/USD","displayName":"DOGEUSDT Perp","baseToken":"DOGE","quoteToken":"USD","price":0.0789,"changeRate":-0.0073,"changePrice":-0.00058,"markPrice":0.07892,"indexPrice":0.07894268,"price24hHigh":0.0798,"price24hLow":0.07821,"volumeUsd24h":490734.32204,"volume24h":6215350,"ts":1675408117600,"price24hOpen":0.07101,"icon":"https://file.coincall.com/statics/symbol/DOGE.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"DOGEUSD","deliveryKind":"E"},{"contractId":102,"symbol":"ORDIUSD","symbolName":"ORDI/USD","displayName":"ORDIUSDT Perp","baseToken":"ORDI","quoteToken":"USD","price":3.2,"changeRate":0.0211,"changePrice":0.066,"markPrice":3.20357027,"indexPrice":3.20589499,"price24hHigh":3.279,"price24hLow":3.087,"volumeUsd24h":19793.9082,"volume24h":6222.1,"ts":1675408117600,"price24hOpen":2.88,"icon":"https://file.coincall.com/statics/symbol/ORDI.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ORDIUSD","deliveryKind":"E"},{"contractId":213,"symbol":"TRUMPUSD","symbolName":"TRUMP/USD","displayName":"TRUMPUSDT Perp","baseToken":"TRUMP","quoteToken":"USD","price":1.735,"changeRate":-0.0057,"changePrice":-0.01,"markPrice":1.73559886,"indexPrice":1.73711566,"price24hHigh":1.778,"price24hLow":1.714,"volumeUsd24h":73812.7362,"volume24h":42183.16,"ts":1675408117600,"price24hOpen":1.5615,"icon":"https://file.coincall.com/statics/symbol/TRUMP.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TRUMPUSD","deliveryKind":"E"},{"contractId":214,"symbol":"SUSD","symbolName":"S/USD","displayName":"SUSDT Perp","baseToken":"S","quoteToken":"USD","price":0.023,"changeRate":-0.0417,"changePrice":-0.001,"markPrice":0.023,"indexPrice":0.02329032,"price24hHigh":0.025,"price24hLow":0.022,"volumeUsd24h":19373.44436,"volume24h":834586.27,"ts":1675408117600,"price24hOpen":0.0207,"icon":"https://file.coincall.com/statics/symbol/S.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SUSD","deliveryKind":"E"},{"contractId":4433,"symbol":"TSLAUSD","symbolName":"TSLA/USD","displayName":"TSLAUSDT Perp","baseToken":"TSLA","quoteToken":"USD","price":383.65,"changeRate":-0.026,"changePrice":-10.23,"markPrice":383.65,"indexPrice":383.46815894,"price24hHigh":394.84,"price24hLow":379.45,"volumeUsd24h":129527.7513,"volume24h":335.06,"ts":1770780636643,"price24hOpen":345.285,"icon":"https://file.coincall.com/statics/symbol/TSLA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TSLAUSD","deliveryKind":"E"},{"contractId":4434,"symbol":"MSTRUSD","symbolName":"MSTR/USD","displayName":"MSTRUSDT Perp","baseToken":"MSTR","quoteToken":"USD","price":105.02,"changeRate":-0.009,"changePrice":-0.95,"markPrice":105.02,"indexPrice":104.84073529,"price24hHigh":107.77,"price24hLow":103.61,"volumeUsd24h":290757.9639,"volume24h":2751.98,"ts":1770780636644,"price24hOpen":94.518,"icon":"https://file.coincall.com/statics/symbol/MSTR.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MSTRUSD","deliveryKind":"E"},{"contractId":89,"symbol":"NEARUSD","symbolName":"NEAR/USD","displayName":"NEARUSDT Perp","baseToken":"NEAR","quoteToken":"USD","price":1.97,"changeRate":-0.014,"changePrice":-0.028,"markPrice":1.97029741,"indexPrice":1.97227568,"price24hHigh":2.008,"price24hLow":1.937,"volumeUsd24h":203829.878,"volume24h":103129,"ts":1675408117600,"price24hOpen":1.773,"icon":"https://file.coincall.com/statics/symbol/NEAR.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"NEARUSD","deliveryKind":"E"},{"contractId":4435,"symbol":"INTCUSD","symbolName":"INTC/USD","displayName":"INTCUSDT Perp","baseToken":"INTC","quoteToken":"USD","price":134.02,"changeRate":0.0224,"changePrice":2.94,"markPrice":134.03966482,"indexPrice":134.01548185,"price24hHigh":138.09,"price24hLow":128.57,"volumeUsd24h":555080.5745,"volume24h":4131.05,"ts":1770780636645,"price24hOpen":120.618,"icon":"https://file.coincall.com/statics/symbol/INTC.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"INTCUSD","deliveryKind":"E"},{"contractId":64,"symbol":"BNBUSD","symbolName":"BNB/USD","displayName":"BNBUSDT Perp","baseToken":"BNB","quoteToken":"USD","price":578.99,"changeRate":0.0081,"changePrice":4.67,"markPrice":578.99571159,"indexPrice":578.96085424,"price24hHigh":582.02,"price24hLow":571.4,"volumeUsd24h":403548.8247,"volume24h":699.66,"ts":1675408117600,"price24hOpen":521.091,"icon":"https://file.coincall.com/statics/symbol/BNB.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BNBUSD","deliveryKind":"E"},{"contractId":4436,"symbol":"HOODUSD","symbolName":"HOOD/USD","displayName":"HOODUSDT Perp","baseToken":"HOOD","quoteToken":"USD","price":103.78,"changeRate":0.0397,"changePrice":3.96,"markPrice":103.74873816,"indexPrice":103.5895587,"price24hHigh":106.07,"price24hLow":99.18,"volumeUsd24h":64748.3456,"volume24h":627.26,"ts":1770780636645,"price24hOpen":93.402,"icon":"https://file.coincall.com/statics/symbol/HOOD.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"HOODUSD","deliveryKind":"E"},{"contractId":101,"symbol":"KASUSD","symbolName":"KAS/USD","displayName":"KASUSDT Perp","baseToken":"KAS","quoteToken":"USD","price":0.02839,"changeRate":-0.0284,"changePrice":-0.00083,"markPrice":0.02839938,"indexPrice":0.0284125,"price24hHigh":0.02963,"price24hLow":0.02814,"volumeUsd24h":7876.42706,"volume24h":273786,"ts":1675408117600,"price24hOpen":0.025551,"icon":"https://file.coincall.com/statics/symbol/KAS.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"KASUSD","deliveryKind":"E"},{"contractId":4403,"symbol":"XAUTUSD","symbolName":"XAUT/USD","displayName":"XAUTUSDT Perp","baseToken":"XAUT","quoteToken":"USD","price":4008.95,"changeRate":-0.0241,"changePrice":-99.07,"markPrice":4008.95,"indexPrice":4009.54,"price24hHigh":4131.72,"price24hLow":4004.24,"volumeUsd24h":21381.5587,"volume24h":5.255,"ts":1762506588282,"price24hOpen":3608.055,"icon":"https://file.coincall.com/statics/symbol/XAUT.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"XAUTUSD","deliveryKind":"E"},{"contractId":4404,"symbol":"MNTUSD","symbolName":"MNT/USD","displayName":"MNTUSDT Perp","baseToken":"MNT","quoteToken":"USD","price":0.5149,"changeRate":-0.005,"changePrice":-0.0026,"markPrice":0.5149,"indexPrice":0.5154,"price24hHigh":0.5191,"price24hLow":0.5098,"volumeUsd24h":1788.5038,"volume24h":3470,"ts":1762506588283,"price24hOpen":0.46341,"icon":"https://file.coincall.com/statics/symbol/MNT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MNTUSD","deliveryKind":"E"},{"contractId":4437,"symbol":"CRCLUSD","symbolName":"CRCL/USD","displayName":"CRCLUSDT Perp","baseToken":"CRCL","quoteToken":"USD","price":76.62,"changeRate":-0.0025,"changePrice":-0.19,"markPrice":76.67,"indexPrice":76.6584156,"price24hHigh":78.21,"price24hLow":74.76,"volumeUsd24h":198957.4164,"volume24h":2603.63,"ts":1770780636646,"price24hOpen":68.958,"icon":"https://file.coincall.com/statics/symbol/CRCL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"CRCLUSD","deliveryKind":"E"},{"contractId":151,"symbol":"ICPUSD","symbolName":"ICP/USD","displayName":"ICPUSDT Perp","baseToken":"ICP","quoteToken":"USD","price":2.195,"changeRate":0.02,"changePrice":0.043,"markPrice":2.19575833,"indexPrice":2.19764014,"price24hHigh":2.225,"price24hLow":2.135,"volumeUsd24h":33335.891,"volume24h":15287,"ts":1675408117600,"price24hOpen":1.9755,"icon":"https://file.coincall.com/statics/symbol/ICP.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ICPUSD","deliveryKind":"E"},{"contractId":4438,"symbol":"AMZNUSD","symbolName":"AMZN/USD","displayName":"AMZNUSDT Perp","baseToken":"AMZN","quoteToken":"USD","price":233.24,"changeRate":-0.0012,"changePrice":-0.28,"markPrice":233.19971864,"indexPrice":233.1164668,"price24hHigh":233.96,"price24hLow":233.19,"volumeUsd24h":245.4052,"volume24h":1.05,"ts":1770780636647,"price24hOpen":209.916,"icon":"https://file.coincall.com/statics/symbol/AMZN.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"AMZNUSD","deliveryKind":"E"},{"contractId":25,"symbol":"AVAXUSD","symbolName":"AVAX/USD","displayName":"AVAXUSDT Perp","baseToken":"AVAX","quoteToken":"USD","price":6.426,"changeRate":0.0273,"changePrice":0.171,"markPrice":6.43069399,"indexPrice":6.43190725,"price24hHigh":6.54,"price24hLow":6.213,"volumeUsd24h":234151.557,"volume24h":36647.1,"ts":1675408117600,"price24hOpen":5.7834,"icon":"https://file.coincall.com/statics/symbol/AVAX.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"AVAXUSD","deliveryKind":"E"},{"contractId":4439,"symbol":"PLTRUSD","symbolName":"PLTR/USD","displayName":"PLTRUSDT Perp","baseToken":"PLTR","quoteToken":"USD","price":118.38,"changeRate":0,"changePrice":0,"markPrice":115.32932585,"indexPrice":114.27930147,"price24hHigh":118.38,"price24hLow":118.38,"volumeUsd24h":0,"volume24h":0,"ts":1770780636647,"price24hOpen":106.542,"icon":"https://file.coincall.com/statics/symbol/PLTR.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PLTRUSD","deliveryKind":"E"},{"contractId":4440,"symbol":"COINUSD","symbolName":"COIN/USD","displayName":"COINUSDT Perp","baseToken":"COIN","quoteToken":"USD","price":159.01,"changeRate":0.0029,"changePrice":0.46,"markPrice":159.01,"indexPrice":158.99804766,"price24hHigh":164.11,"price24hLow":156.84,"volumeUsd24h":36405.4244,"volume24h":228.14,"ts":1770780636647,"price24hOpen":143.109,"icon":"https://file.coincall.com/statics/symbol/COIN.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"COINUSD","deliveryKind":"E"},{"contractId":66,"symbol":"FILUSD","symbolName":"FIL/USD","displayName":"FILUSDT Perp","baseToken":"FIL","quoteToken":"USD","price":0.774,"changeRate":0.0225,"changePrice":0.017,"markPrice":0.77418613,"indexPrice":0.774937,"price24hHigh":0.805,"price24hLow":0.755,"volumeUsd24h":130510.0893,"volume24h":167361.4,"ts":1675408117600,"price24hOpen":0.6966,"icon":"https://file.coincall.com/statics/symbol/FIL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"FILUSD","deliveryKind":"E"},{"contractId":17,"symbol":"XRPUSD","symbolName":"XRP/USD","displayName":"XRPUSDT Perp","baseToken":"XRP","quoteToken":"USD","price":1.0928,"changeRate":-0.0095,"changePrice":-0.0105,"markPrice":1.09315718,"indexPrice":1.09350052,"price24hHigh":1.1134,"price24hLow":1.0812,"volumeUsd24h":953032.0513,"volume24h":866886,"ts":1675408117600,"price24hOpen":0.98352,"icon":"https://file.coincall.com/statics/symbol/XRP.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"XRPUSD","deliveryKind":"E"},{"contractId":22,"symbol":"ADAUSD","symbolName":"ADA/USD","displayName":"ADAUSDT Perp","baseToken":"ADA","quoteToken":"USD","price":0.1485,"changeRate":-0.0217,"changePrice":-0.0033,"markPrice":0.14855012,"indexPrice":0.1487,"price24hHigh":0.1543,"price24hLow":0.1457,"volumeUsd24h":355965.327,"volume24h":2373160,"ts":1675408117600,"price24hOpen":0.13365,"icon":"https://file.coincall.com/statics/symbol/ADA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ADAUSD","deliveryKind":"E"},{"contractId":62,"symbol":"BCHUSD","symbolName":"BCH/USD","displayName":"BCHUSDT Perp","baseToken":"BCH","quoteToken":"USD","price":193.1,"changeRate":0.0099,"changePrice":1.9,"markPrice":193.2635947,"indexPrice":193.3511112,"price24hHigh":196.42,"price24hLow":189.22,"volumeUsd24h":118877.71454,"volume24h":615.601,"ts":1675408117600,"price24hOpen":173.79,"icon":"https://file.coincall.com/statics/symbol/BCH.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BCHUSD","deliveryKind":"E"},{"contractId":18,"symbol":"LTCUSD","symbolName":"LTC/USD","displayName":"LTCUSDT Perp","baseToken":"LTC","quoteToken":"USD","price":42.18,"changeRate":-0.025,"changePrice":-1.08,"markPrice":42.2166348,"indexPrice":42.22044118,"price24hHigh":43.5,"price24hLow":41.45,"volumeUsd24h":139487.6667,"volume24h":3303.24,"ts":1675408117600,"price24hOpen":37.962,"icon":"https://file.coincall.com/statics/symbol/LTC.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"LTCUSD","deliveryKind":"E"},{"contractId":32,"symbol":"TRXUSD","symbolName":"TRX/USD","displayName":"TRXUSDT Perp","baseToken":"TRX","quoteToken":"USD","price":0.33127,"changeRate":0.0052,"changePrice":0.00173,"markPrice":0.33127,"indexPrice":0.33137147,"price24hHigh":0.33183,"price24hLow":0.32832,"volumeUsd24h":76459.28281,"volume24h":231839,"ts":1675408117600,"price24hOpen":0.298143,"icon":"https://file.coincall.com/statics/symbol/TRX.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TRXUSD","deliveryKind":"E"},{"contractId":29,"symbol":"LINKUSD","symbolName":"LINK/USD","displayName":"LINKUSDT Perp","baseToken":"LINK","quoteToken":"USD","price":7.608,"changeRate":0.0022,"changePrice":0.017,"markPrice":7.61589934,"indexPrice":7.61741004,"price24hHigh":7.688,"price24hLow":7.527,"volumeUsd24h":174509.5139,"volume24h":22931,"ts":1675408117600,"price24hOpen":6.8472,"icon":"https://file.coincall.com/statics/symbol/LINK.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"LINKUSD","deliveryKind":"E"},{"contractId":35,"symbol":"OPUSD","symbolName":"OP/USD","displayName":"OPUSDT Perp","baseToken":"OP","quoteToken":"USD","price":0.1017,"changeRate":0.0378,"changePrice":0.0037,"markPrice":0.10172018,"indexPrice":0.10179763,"price24hHigh":0.1029,"price24hLow":0.0976,"volumeUsd24h":41418.037,"volume24h":411927,"ts":1675408117600,"price24hOpen":0.09153,"icon":"https://file.coincall.com/statics/symbol/OP.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"OPUSD","deliveryKind":"E"},{"contractId":41,"symbol":"APTUSD","symbolName":"APT/USD","displayName":"APTUSDT Perp","baseToken":"APT","quoteToken":"USD","price":0.6401,"changeRate":0.0165,"changePrice":0.0104,"markPrice":0.6405396,"indexPrice":0.64114125,"price24hHigh":0.6557,"price24hLow":0.6274,"volumeUsd24h":30806.0107,"volume24h":48035,"ts":1675408117600,"price24hOpen":0.57609,"icon":"https://file.coincall.com/statics/symbol/APT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"APTUSD","deliveryKind":"E"},{"contractId":43,"symbol":"LDOUSD","symbolName":"LDO/USD","displayName":"LDOUSDT Perp","baseToken":"LDO","quoteToken":"USD","price":0.2577,"changeRate":0.0074,"changePrice":0.0019,"markPrice":0.25782861,"indexPrice":0.25788514,"price24hHigh":0.2618,"price24hLow":0.2533,"volumeUsd24h":15893.564,"volume24h":61771,"ts":1675408117600,"price24hOpen":0.23193,"icon":"https://file.coincall.com/statics/symbol/LDO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"LDOUSD","deliveryKind":"E"},{"contractId":56,"symbol":"ARBUSD","symbolName":"ARB/USD","displayName":"ARBUSDT Perp","baseToken":"ARB","quoteToken":"USD","price":0.0785,"changeRate":-0.0051,"changePrice":-0.0004,"markPrice":0.07855454,"indexPrice":0.07860549,"price24hHigh":0.0804,"price24hLow":0.0773,"volumeUsd24h":36867.69836,"volume24h":468669.5,"ts":1675408117600,"price24hOpen":0.07065,"icon":"https://file.coincall.com/statics/symbol/ARB.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ARBUSD","deliveryKind":"E"},{"contractId":63,"symbol":"1000PEPEUSD","symbolName":"1000PEPE/USD","displayName":"1000PEPEUSDT Perp","baseToken":"1000PEPE","quoteToken":"USD","price":0.0026534,"changeRate":-0.0199,"changePrice":-0.0000538,"markPrice":0.00265554,"indexPrice":0.00265594,"price24hHigh":0.002755,"price24hLow":0.0026159,"volumeUsd24h":284795.8039521,"volume24h":106125603,"ts":1675408117600,"price24hOpen":0.00238806,"icon":"https://file.coincall.com/statics/symbol/1000PEPE.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"1000PEPEUSD","deliveryKind":"E"},{"contractId":67,"symbol":"INJUSD","symbolName":"INJ/USD","displayName":"INJUSDT Perp","baseToken":"INJ","quoteToken":"USD","price":4.361,"changeRate":-0.0377,"changePrice":-0.171,"markPrice":4.36507497,"indexPrice":4.3670439,"price24hHigh":4.625,"price24hLow":4.273,"volumeUsd24h":65505.1887,"volume24h":14649.4,"ts":1675408117600,"price24hOpen":3.9249,"icon":"https://file.coincall.com/statics/symbol/INJ.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"INJUSD","deliveryKind":"E"},{"contractId":70,"symbol":"STXUSD","symbolName":"STX/USD","displayName":"STXUSDT Perp","baseToken":"STX","quoteToken":"USD","price":0.1747,"changeRate":-0.0046,"changePrice":-0.0008,"markPrice":0.1749399,"indexPrice":0.17512068,"price24hHigh":0.1801,"price24hLow":0.1733,"volumeUsd24h":5067.279,"volume24h":28705,"ts":1675408117600,"price24hOpen":0.15723,"icon":"https://file.coincall.com/statics/symbol/STX.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"STXUSD","deliveryKind":"E"},{"contractId":71,"symbol":"ETCUSD","symbolName":"ETC/USD","displayName":"ETCUSDT Perp","baseToken":"ETC","quoteToken":"USD","price":7.031,"changeRate":0.0092,"changePrice":0.064,"markPrice":7.03407905,"indexPrice":7.04,"price24hHigh":7.175,"price24hLow":6.842,"volumeUsd24h":37319.04612,"volume24h":5322.23,"ts":1675408117600,"price24hOpen":6.3279,"icon":"https://file.coincall.com/statics/symbol/ETC.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ETCUSD","deliveryKind":"E"},{"contractId":79,"symbol":"ARPAUSD","symbolName":"ARPA/USD","displayName":"ARPAUSDT Perp","baseToken":"ARPA","quoteToken":"USD","price":0.0084,"changeRate":0.0024,"changePrice":0.00002,"markPrice":0.00840126,"indexPrice":0.00840134,"price24hHigh":0.00853,"price24hLow":0.0083,"volumeUsd24h":1493.39169,"volume24h":177238,"ts":1675408117600,"price24hOpen":0.00756,"icon":"https://file.coincall.com/statics/symbol/ARPA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ARPAUSD","deliveryKind":"E"},{"contractId":80,"symbol":"CRVUSD","symbolName":"CRV/USD","displayName":"CRVUSDT Perp","baseToken":"CRV","quoteToken":"USD","price":0.199,"changeRate":-0.0197,"changePrice":-0.004,"markPrice":0.19990051,"indexPrice":0.2002,"price24hHigh":0.207,"price24hLow":0.196,"volumeUsd24h":25939.2655,"volume24h":129290.9,"ts":1675408117600,"price24hOpen":0.1791,"icon":"https://file.coincall.com/statics/symbol/CRV.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"CRVUSD","deliveryKind":"E"},{"contractId":86,"symbol":"XLMUSD","symbolName":"XLM/USD","displayName":"XLMUSDT Perp","baseToken":"XLM","quoteToken":"USD","price":0.19162,"changeRate":-0.0088,"changePrice":-0.00171,"markPrice":0.1917076,"indexPrice":0.19173,"price24hHigh":0.19722,"price24hLow":0.18919,"volumeUsd24h":134661.40588,"volume24h":697649,"ts":1675408117600,"price24hOpen":0.172458,"icon":"https://file.coincall.com/statics/symbol/XLM.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"XLMUSD","deliveryKind":"E"},{"contractId":87,"symbol":"SANDUSD","symbolName":"SAND/USD","displayName":"SANDUSDT Perp","baseToken":"SAND","quoteToken":"USD","price":0.0519,"changeRate":-0.0095,"changePrice":-0.0005,"markPrice":0.0519182,"indexPrice":0.05200622,"price24hHigh":0.0534,"price24hLow":0.0514,"volumeUsd24h":13466.9009,"volume24h":257267,"ts":1675408117600,"price24hOpen":0.04671,"icon":"https://file.coincall.com/statics/symbol/SAND.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SANDUSD","deliveryKind":"E"},{"contractId":88,"symbol":"ATOMUSD","symbolName":"ATOM/USD","displayName":"ATOMUSDT Perp","baseToken":"ATOM","quoteToken":"USD","price":1.641,"changeRate":-0.0745,"changePrice":-0.132,"markPrice":1.64256355,"indexPrice":1.64408143,"price24hHigh":1.774,"price24hLow":1.641,"volumeUsd24h":25568.63361,"volume24h":14987.44,"ts":1675408117600,"price24hOpen":1.4769,"icon":"https://file.coincall.com/statics/symbol/ATOM.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ATOMUSD","deliveryKind":"E"},{"contractId":91,"symbol":"CTSIUSD","symbolName":"CTSI/USD","displayName":"CTSIUSDT Perp","baseToken":"CTSI","quoteToken":"USD","price":0.0226,"changeRate":0.0044,"changePrice":0.0001,"markPrice":0.0226,"indexPrice":0.02263774,"price24hHigh":0.0229,"price24hLow":0.0223,"volumeUsd24h":1008.8446,"volume24h":44587,"ts":1675408117600,"price24hOpen":0.02034,"icon":"https://file.coincall.com/statics/symbol/CTSI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"CTSIUSD","deliveryKind":"E"},{"contractId":92,"symbol":"UNIUSD","symbolName":"UNI/USD","displayName":"UNIUSDT Perp","baseToken":"UNI","quoteToken":"USD","price":2.936,"changeRate":0.0152,"changePrice":0.044,"markPrice":2.938,"indexPrice":2.938,"price24hHigh":2.965,"price24hLow":2.864,"volumeUsd24h":85034.483,"volume24h":29212,"ts":1675408117600,"price24hOpen":2.6424,"icon":"https://file.coincall.com/statics/symbol/UNI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"UNIUSD","deliveryKind":"E"},{"contractId":96,"symbol":"ONTUSD","symbolName":"ONT/USD","displayName":"ONTUSDT Perp","baseToken":"ONT","quoteToken":"USD","price":0.044,"changeRate":0.0023,"changePrice":0.0001,"markPrice":0.044,"indexPrice":0.04411,"price24hHigh":0.0446,"price24hLow":0.0433,"volumeUsd24h":3594.93986,"volume24h":81777.4,"ts":1675408117600,"price24hOpen":0.0396,"icon":"https://file.coincall.com/statics/symbol/ONT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ONTUSD","deliveryKind":"E"},{"contractId":97,"symbol":"WLDUSD","symbolName":"WLD/USD","displayName":"WLDUSDT Perp","baseToken":"WLD","quoteToken":"USD","price":0.5435,"changeRate":-0.0401,"changePrice":-0.0227,"markPrice":0.5438,"indexPrice":0.5441648,"price24hHigh":0.5809,"price24hLow":0.5101,"volumeUsd24h":871053.2528,"volume24h":1613469,"ts":1675408117600,"price24hOpen":0.48915,"icon":"https://file.coincall.com/statics/symbol/WLD.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"WLDUSD","deliveryKind":"E"},{"contractId":98,"symbol":"VETUSD","symbolName":"VET/USD","displayName":"VETUSDT Perp","baseToken":"VET","quoteToken":"USD","price":0.00466,"changeRate":-0.0064,"changePrice":-0.00003,"markPrice":0.00466082,"indexPrice":0.004674,"price24hHigh":0.00475,"price24hLow":0.00459,"volumeUsd24h":6307.48444,"volume24h":1352147,"ts":1675408117600,"price24hOpen":0.004194,"icon":"https://file.coincall.com/statics/symbol/VET.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"VETUSD","deliveryKind":"E"},{"contractId":99,"symbol":"SEIUSD","symbolName":"SEI/USD","displayName":"SEIUSDT Perp","baseToken":"SEI","quoteToken":"USD","price":0.0537,"changeRate":0.017,"changePrice":0.0009,"markPrice":0.0537754,"indexPrice":0.05386498,"price24hHigh":0.054,"price24hLow":0.0517,"volumeUsd24h":26675.2202,"volume24h":506018,"ts":1675408117600,"price24hOpen":0.04833,"icon":"https://file.coincall.com/statics/symbol/SEI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SEIUSD","deliveryKind":"E"},{"contractId":81,"symbol":"DOTUSD","symbolName":"DOT/USD","displayName":"DOTUSDT Perp","baseToken":"DOT","quoteToken":"USD","price":0.904,"changeRate":0.0033,"changePrice":0.003,"markPrice":0.904,"indexPrice":0.9048,"price24hHigh":0.919,"price24hLow":0.893,"volumeUsd24h":88579.5381,"volume24h":97953.4,"ts":1675408117600,"price24hOpen":0.8136,"icon":"https://file.coincall.com/statics/symbol/DOT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"DOTUSD","deliveryKind":"E"},{"contractId":104,"symbol":"TIAUSD","symbolName":"TIA/USD","displayName":"TIAUSDT Perp","baseToken":"TIA","quoteToken":"USD","price":0.3701,"changeRate":-0.0064,"changePrice":-0.0024,"markPrice":0.37052195,"indexPrice":0.37082143,"price24hHigh":0.3891,"price24hLow":0.3639,"volumeUsd24h":43379.9094,"volume24h":114996,"ts":1675408117600,"price24hOpen":0.33309,"icon":"https://file.coincall.com/statics/symbol/TIA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TIAUSD","deliveryKind":"E"},{"contractId":105,"symbol":"USTCUSD","symbolName":"USTC/USD","displayName":"USTCUSDT Perp","baseToken":"USTC","quoteToken":"USD","price":0.00555,"changeRate":-0.0072,"changePrice":-0.00004,"markPrice":0.00555,"indexPrice":0.00555487,"price24hHigh":0.00562,"price24hLow":0.00546,"volumeUsd24h":1431.89964,"volume24h":258526,"ts":1675408117600,"price24hOpen":0.004995,"icon":"https://file.coincall.com/statics/symbol/USTC.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"USTCUSD","deliveryKind":"E"},{"contractId":106,"symbol":"1000LUNCUSD","symbolName":"1000LUNC/USD","displayName":"1000LUNCUSDT Perp","baseToken":"1000LUNC","quoteToken":"USD","price":0.0629,"changeRate":-0.0247,"changePrice":-0.00159,"markPrice":0.06292444,"indexPrice":0.0629492,"price24hHigh":0.06484,"price24hLow":0.06228,"volumeUsd24h":13071.63751,"volume24h":205349,"ts":1675408117600,"price24hOpen":0.05661,"icon":"https://file.coincall.com/statics/symbol/1000LUNC.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"1000LUNCUSD","deliveryKind":"E"},{"contractId":107,"symbol":"MEMEUSD","symbolName":"MEME/USD","displayName":"MEMEUSDT Perp","baseToken":"MEME","quoteToken":"USD","price":0.000528,"changeRate":0.0115,"changePrice":0.000006,"markPrice":0.0005293,"indexPrice":0.00052931,"price24hHigh":0.000556,"price24hLow":0.000515,"volumeUsd24h":9926.372129,"volume24h":18524576,"ts":1675408117600,"price24hOpen":0.0004752,"icon":"https://file.coincall.com/statics/symbol/MEME.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MEMEUSD","deliveryKind":"E"},{"contractId":109,"symbol":"IOSTUSD","symbolName":"IOST/USD","displayName":"IOSTUSDT Perp","baseToken":"IOST","quoteToken":"USD","price":0.000739,"changeRate":-0.0186,"changePrice":-0.000014,"markPrice":0.00074065,"indexPrice":0.00074062,"price24hHigh":0.000759,"price24hLow":0.000722,"volumeUsd24h":2653.180801,"volume24h":3583563,"ts":1675408117600,"price24hOpen":0.0006651,"icon":"https://file.coincall.com/statics/symbol/IOST.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"IOSTUSD","deliveryKind":"E"},{"contractId":110,"symbol":"1000RATSUSD","symbolName":"1000RATS/USD","displayName":"1000RATSUSDT Perp","baseToken":"1000RATS","quoteToken":"USD","price":0.024762,"changeRate":0.005,"changePrice":0.000122,"markPrice":0.024828,"indexPrice":0.02468643,"price24hHigh":0.025122,"price24hLow":0.024299,"volumeUsd24h":996.744967,"volume24h":40290,"ts":1675408117600,"price24hOpen":0.0222858,"icon":"https://file.coincall.com/statics/symbol/1000RATS.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"1000RATSUSD","deliveryKind":"E"},{"contractId":117,"symbol":"COTIUSD","symbolName":"COTI/USD","displayName":"COTIUSDT Perp","baseToken":"COTI","quoteToken":"USD","price":0.00892,"changeRate":-0.0089,"changePrice":-0.00008,"markPrice":0.00892181,"indexPrice":0.00893744,"price24hHigh":0.0091,"price24hLow":0.00873,"volumeUsd24h":1459.94504,"volume24h":163495,"ts":1675408117600,"price24hOpen":0.008028,"icon":"https://file.coincall.com/statics/symbol/COTI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"COTIUSD","deliveryKind":"E"},{"contractId":116,"symbol":"JUPUSD","symbolName":"JUP/USD","displayName":"JUPUSDT Perp","baseToken":"JUP","quoteToken":"USD","price":0.2191,"changeRate":0.0955,"changePrice":0.0191,"markPrice":0.21918653,"indexPrice":0.21949622,"price24hHigh":0.2211,"price24hLow":0.1984,"volumeUsd24h":40433.1481,"volume24h":192908,"ts":1675408117600,"price24hOpen":0.19719,"icon":"https://file.coincall.com/statics/symbol/JUP.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"JUPUSD","deliveryKind":"E"},{"contractId":119,"symbol":"STRKUSD","symbolName":"STRK/USD","displayName":"STRKUSDT Perp","baseToken":"STRK","quoteToken":"USD","price":0.0317,"changeRate":-0.0094,"changePrice":-0.0003,"markPrice":0.0317,"indexPrice":0.03180736,"price24hHigh":0.0324,"price24hLow":0.0311,"volumeUsd24h":9811.76955,"volume24h":308735.5,"ts":1675408117600,"price24hOpen":0.02853,"icon":"https://file.coincall.com/statics/symbol/STRK.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"STRKUSD","deliveryKind":"E"},{"contractId":120,"symbol":"RUNEUSD","symbolName":"RUNE/USD","displayName":"RUNEUSDT Perp","baseToken":"RUNE","quoteToken":"USD","price":0.42,"changeRate":0,"changePrice":0,"markPrice":0.42000788,"indexPrice":0.42090041,"price24hHigh":0.424,"price24hLow":0.412,"volumeUsd24h":7655.94,"volume24h":18293,"ts":1675408117600,"price24hOpen":0.378,"icon":"https://file.coincall.com/statics/symbol/RUNE.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"RUNEUSD","deliveryKind":"E"},{"contractId":121,"symbol":"ARKMUSD","symbolName":"ARKM/USD","displayName":"ARKMUSDT Perp","baseToken":"ARKM","quoteToken":"USD","price":0.1212,"changeRate":0.0008,"changePrice":0.0001,"markPrice":0.1212,"indexPrice":0.12150058,"price24hHigh":0.1232,"price24hLow":0.1185,"volumeUsd24h":4992.6958,"volume24h":41214,"ts":1675408117600,"price24hOpen":0.10908,"icon":"https://file.coincall.com/statics/symbol/ARKM.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ARKMUSD","deliveryKind":"E"},{"contractId":122,"symbol":"ARUSD","symbolName":"AR/USD","displayName":"ARUSDT Perp","baseToken":"AR","quoteToken":"USD","price":2.004,"changeRate":0.0325,"changePrice":0.063,"markPrice":2.0077106,"indexPrice":2.0083208,"price24hHigh":2.045,"price24hLow":1.923,"volumeUsd24h":9863.3469,"volume24h":4943.2,"ts":1675408117600,"price24hOpen":1.8036,"icon":"https://file.coincall.com/statics/symbol/AR.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ARUSD","deliveryKind":"E"},{"contractId":123,"symbol":"1000BONKUSD","symbolName":"1000BONK/USD","displayName":"1000BONKUSDT Perp","baseToken":"1000BONK","quoteToken":"USD","price":0.004376,"changeRate":-0.0039,"changePrice":-0.000017,"markPrice":0.00437957,"indexPrice":0.00438236,"price24hHigh":0.004437,"price24hLow":0.004326,"volumeUsd24h":22957.654332,"volume24h":5237692,"ts":1675408117600,"price24hOpen":0.0039384,"icon":"https://file.coincall.com/statics/symbol/1000BONK.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"1000BONKUSD","deliveryKind":"E"},{"contractId":124,"symbol":"WIFUSD","symbolName":"WIF/USD","displayName":"WIFUSDT Perp","baseToken":"WIF","quoteToken":"USD","price":0.1542,"changeRate":-0.0147,"changePrice":-0.0023,"markPrice":0.15435804,"indexPrice":0.15447,"price24hHigh":0.1574,"price24hLow":0.1532,"volumeUsd24h":26211.25019,"volume24h":168534.1,"ts":1675408117600,"price24hOpen":0.13878,"icon":"https://file.coincall.com/statics/symbol/WIF.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"WIFUSD","deliveryKind":"E"},{"contractId":128,"symbol":"1000FLOKIUSD","symbolName":"1000FLOKI/USD","displayName":"1000FLOKIUSDT Perp","baseToken":"1000FLOKI","quoteToken":"USD","price":0.02378,"changeRate":-0.0008,"changePrice":-0.00002,"markPrice":0.02378,"indexPrice":0.0237975,"price24hHigh":0.02416,"price24hLow":0.02344,"volumeUsd24h":7802.64517,"volume24h":327528,"ts":1675408117600,"price24hOpen":0.021402,"icon":"https://file.coincall.com/statics/symbol/1000FLOKI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"1000FLOKIUSD","deliveryKind":"E"},{"contractId":130,"symbol":"MANAUSD","symbolName":"MANA/USD","displayName":"MANAUSDT Perp","baseToken":"MANA","quoteToken":"USD","price":0.0696,"changeRate":-0.0197,"changePrice":-0.0014,"markPrice":0.0696,"indexPrice":0.06961786,"price24hHigh":0.0722,"price24hLow":0.0689,"volumeUsd24h":5631.8796,"volume24h":79891,"ts":1675408117600,"price24hOpen":0.06264,"icon":"https://file.coincall.com/statics/symbol/MANA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MANAUSD","deliveryKind":"E"},{"contractId":132,"symbol":"1000SHIBUSD","symbolName":"1000SHIB/USD","displayName":"1000SHIBUSDT Perp","baseToken":"1000SHIB","quoteToken":"USD","price":0.004551,"changeRate":-0.0026,"changePrice":-0.000012,"markPrice":0.00455792,"indexPrice":0.00455773,"price24hHigh":0.004612,"price24hLow":0.00452,"volumeUsd24h":33843.097021,"volume24h":7418219,"ts":1675408117600,"price24hOpen":0.0040959,"icon":"https://file.coincall.com/statics/symbol/1000SHIB.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"1000SHIBUSD","deliveryKind":"E"},{"contractId":135,"symbol":"THETAUSD","symbolName":"THETA/USD","displayName":"THETAUSDT Perp","baseToken":"THETA","quoteToken":"USD","price":0.1465,"changeRate":0,"changePrice":0,"markPrice":0.14668639,"indexPrice":0.14682906,"price24hHigh":0.1489,"price24hLow":0.1435,"volumeUsd24h":4945.07857,"volume24h":33870.3,"ts":1675408117600,"price24hOpen":0.13185,"icon":"https://file.coincall.com/statics/symbol/THETA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"THETAUSD","deliveryKind":"E"},{"contractId":139,"symbol":"BEAMXUSD","symbolName":"BEAMX/USD","displayName":"BEAMXUSDT Perp","baseToken":"BEAMX","quoteToken":"USD","price":0.00135,"changeRate":-0.0037,"changePrice":-0.000005,"markPrice":0.00135,"indexPrice":0.00134949,"price24hHigh":0.001378,"price24hLow":0.001341,"volumeUsd24h":1591.669421,"volume24h":1171126,"ts":1675408117600,"price24hOpen":0.001215,"icon":"https://file.coincall.com/statics/symbol/BEAMX.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BEAMXUSD","deliveryKind":"E"},{"contractId":141,"symbol":"BOMEUSD","symbolName":"BOME/USD","displayName":"BOMEUSDT Perp","baseToken":"BOME","quoteToken":"USD","price":0.000416,"changeRate":0.0122,"changePrice":0.000005,"markPrice":0.0004164,"indexPrice":0.00041748,"price24hHigh":0.000422,"price24hLow":0.000408,"volumeUsd24h":4738.263446,"volume24h":11421108,"ts":1675408117600,"price24hOpen":0.0003744,"icon":"https://file.coincall.com/statics/symbol/BOME.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BOMEUSD","deliveryKind":"E"},{"contractId":142,"symbol":"TAOUSD","symbolName":"TAO/USD","displayName":"TAOUSDT Perp","baseToken":"TAO","quoteToken":"USD","price":221.03,"changeRate":0.005,"changePrice":1.1,"markPrice":221.24315642,"indexPrice":221.258334,"price24hHigh":225.98,"price24hLow":214.15,"volumeUsd24h":195098.405,"volume24h":888.67,"ts":1675408117600,"price24hOpen":198.927,"icon":"https://file.coincall.com/statics/symbol/TAO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TAOUSD","deliveryKind":"E"},{"contractId":143,"symbol":"ONDOUSD","symbolName":"ONDO/USD","displayName":"ONDOUSDT Perp","baseToken":"ONDO","quoteToken":"USD","price":0.3062,"changeRate":-0.0186,"changePrice":-0.0058,"markPrice":0.30640453,"indexPrice":0.3067,"price24hHigh":0.3164,"price24hLow":0.3017,"volumeUsd24h":107847.232892,"volume24h":347512.56,"ts":1675408117600,"price24hOpen":0.27558,"icon":"https://file.coincall.com/statics/symbol/ONDO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ONDOUSD","deliveryKind":"E"},{"contractId":145,"symbol":"POLYXUSD","symbolName":"POLYX/USD","displayName":"POLYXUSDT Perp","baseToken":"POLYX","quoteToken":"USD","price":0.03674,"changeRate":0.0194,"changePrice":0.0007,"markPrice":0.03675,"indexPrice":0.03680428,"price24hHigh":0.03766,"price24hLow":0.03566,"volumeUsd24h":3086.4335,"volume24h":84364,"ts":1675408117600,"price24hOpen":0.033066,"icon":"https://file.coincall.com/statics/symbol/POLYX.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"POLYXUSD","deliveryKind":"E"},{"contractId":146,"symbol":"ETHFIUSD","symbolName":"ETHFI/USD","displayName":"ETHFIUSDT Perp","baseToken":"ETHFI","quoteToken":"USD","price":0.344,"changeRate":0.0456,"changePrice":0.015,"markPrice":0.344,"indexPrice":0.345,"price24hHigh":0.353,"price24hLow":0.326,"volumeUsd24h":20003.7896,"volume24h":58298.3,"ts":1675408117600,"price24hOpen":0.3096,"icon":"https://file.coincall.com/statics/symbol/ETHFI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ETHFIUSD","deliveryKind":"E"},{"contractId":150,"symbol":"PYTHUSD","symbolName":"PYTH/USD","displayName":"PYTHUSDT Perp","baseToken":"PYTH","quoteToken":"USD","price":0.035,"changeRate":0.0057,"changePrice":0.0002,"markPrice":0.03510519,"indexPrice":0.0352,"price24hHigh":0.0356,"price24hLow":0.0343,"volumeUsd24h":8389.127,"volume24h":239043,"ts":1675408117600,"price24hOpen":0.0315,"icon":"https://file.coincall.com/statics/symbol/PYTH.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PYTHUSD","deliveryKind":"E"},{"contractId":158,"symbol":"CHZUSD","symbolName":"CHZ/USD","displayName":"CHZUSDT Perp","baseToken":"CHZ","quoteToken":"USD","price":0.01983,"changeRate":0.0448,"changePrice":0.00085,"markPrice":0.01983732,"indexPrice":0.01986148,"price24hHigh":0.02032,"price24hLow":0.01882,"volumeUsd24h":34296.03864,"volume24h":1751887,"ts":1675408117600,"price24hOpen":0.017847,"icon":"https://file.coincall.com/statics/symbol/CHZ.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"CHZUSD","deliveryKind":"E"},{"contractId":160,"symbol":"ENAUSD","symbolName":"ENA/USD","displayName":"ENAUSDT Perp","baseToken":"ENA","quoteToken":"USD","price":0.0849,"changeRate":-0.0035,"changePrice":-0.0003,"markPrice":0.08496744,"indexPrice":0.08502139,"price24hHigh":0.0872,"price24hLow":0.0829,"volumeUsd24h":154344.3795,"volume24h":1809753,"ts":1675408117600,"price24hOpen":0.07641,"icon":"https://file.coincall.com/statics/symbol/ENA.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ENAUSD","deliveryKind":"E"},{"contractId":161,"symbol":"JTOUSD","symbolName":"JTO/USD","displayName":"JTOUSDT Perp","baseToken":"JTO","quoteToken":"USD","price":0.6911,"changeRate":0.1043,"changePrice":0.0653,"markPrice":0.69170202,"indexPrice":0.69235319,"price24hHigh":0.7105,"price24hLow":0.621,"volumeUsd24h":77306.9268,"volume24h":115730,"ts":1675408117600,"price24hOpen":0.62199,"icon":"https://file.coincall.com/statics/symbol/JTO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"JTOUSD","deliveryKind":"E"},{"contractId":162,"symbol":"PENDLEUSD","symbolName":"PENDLE/USD","displayName":"PENDLEUSDT Perp","baseToken":"PENDLE","quoteToken":"USD","price":1.2648,"changeRate":-0.0385,"changePrice":-0.0507,"markPrice":1.26639419,"indexPrice":1.2668008,"price24hHigh":1.3323,"price24hLow":1.2562,"volumeUsd24h":19234.1775,"volume24h":14860,"ts":1675408117600,"price24hOpen":1.13832,"icon":"https://file.coincall.com/statics/symbol/PENDLE.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PENDLEUSD","deliveryKind":"E"},{"contractId":163,"symbol":"TNSRUSD","symbolName":"TNSR/USD","displayName":"TNSRUSDT Perp","baseToken":"TNSR","quoteToken":"USD","price":0.0363,"changeRate":0.0254,"changePrice":0.0009,"markPrice":0.0363,"indexPrice":0.0363178,"price24hHigh":0.0374,"price24hLow":0.0337,"volumeUsd24h":45793.35409,"volume24h":1301759.3,"ts":1675408117600,"price24hOpen":0.03267,"icon":"https://file.coincall.com/statics/symbol/TNSR.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TNSRUSD","deliveryKind":"E"},{"contractId":165,"symbol":"UMAUSD","symbolName":"UMA/USD","displayName":"UMAUSDT Perp","baseToken":"UMA","quoteToken":"USD","price":0.396,"changeRate":-0.0025,"changePrice":-0.001,"markPrice":0.39746926,"indexPrice":0.3981,"price24hHigh":0.401,"price24hLow":0.389,"volumeUsd24h":1949.598,"volume24h":4933,"ts":1675408117600,"price24hOpen":0.3564,"icon":"https://file.coincall.com/statics/symbol/UMA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"UMAUSD","deliveryKind":"E"},{"contractId":167,"symbol":"PEOPLEUSD","symbolName":"PEOPLE/USD","displayName":"PEOPLEUSDT Perp","baseToken":"PEOPLE","quoteToken":"USD","price":0.0054,"changeRate":-0.011,"changePrice":-0.00006,"markPrice":0.0054,"indexPrice":0.0054073,"price24hHigh":0.00555,"price24hLow":0.00532,"volumeUsd24h":3414.89244,"volume24h":629313,"ts":1675408117600,"price24hOpen":0.00486,"icon":"https://file.coincall.com/statics/symbol/PEOPLE.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PEOPLEUSD","deliveryKind":"E"},{"contractId":168,"symbol":"TRBUSD","symbolName":"TRB/USD","displayName":"TRBUSDT Perp","baseToken":"TRB","quoteToken":"USD","price":13.477,"changeRate":0.0274,"changePrice":0.36,"markPrice":13.477,"indexPrice":13.49428858,"price24hHigh":13.707,"price24hLow":12.965,"volumeUsd24h":8338.8226,"volume24h":626.2,"ts":1675408117600,"price24hOpen":12.1293,"icon":"https://file.coincall.com/statics/symbol/TRB.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TRBUSD","deliveryKind":"E"},{"contractId":169,"symbol":"HBARUSD","symbolName":"HBAR/USD","displayName":"HBARUSDT Perp","baseToken":"HBAR","quoteToken":"USD","price":0.07669,"changeRate":-0.0143,"changePrice":-0.00111,"markPrice":0.07671365,"indexPrice":0.07674647,"price24hHigh":0.07823,"price24hLow":0.07601,"volumeUsd24h":31294.1131,"volume24h":404463,"ts":1675408117600,"price24hOpen":0.069021,"icon":"https://file.coincall.com/statics/symbol/HBAR.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"HBARUSD","deliveryKind":"E"},{"contractId":174,"symbol":"TURBOUSD","symbolName":"TURBO/USD","displayName":"TURBOUSDT Perp","baseToken":"TURBO","quoteToken":"USD","price":0.000846,"changeRate":-0.0024,"changePrice":-0.000002,"markPrice":0.00084614,"indexPrice":0.00084862,"price24hHigh":0.000872,"price24hLow":0.000833,"volumeUsd24h":3785.831459,"volume24h":4433013,"ts":1675408117600,"price24hOpen":0.0007614,"icon":"https://file.coincall.com/statics/symbol/TURBO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TURBOUSD","deliveryKind":"E"},{"contractId":177,"symbol":"NOTUSD","symbolName":"NOT/USD","displayName":"NOTUSDT Perp","baseToken":"NOT","quoteToken":"USD","price":0.000388,"changeRate":-0.0177,"changePrice":-0.000007,"markPrice":0.000388,"indexPrice":0.00038852,"price24hHigh":0.000399,"price24hLow":0.000385,"volumeUsd24h":563.776594,"volume24h":1435875,"ts":1675408117600,"price24hOpen":0.0003492,"icon":"https://file.coincall.com/statics/symbol/NOT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"NOTUSD","deliveryKind":"E"},{"contractId":179,"symbol":"POPCATUSD","symbolName":"POPCAT/USD","displayName":"POPCATUSDT Perp","baseToken":"POPCAT","quoteToken":"USD","price":0.0522,"changeRate":-0.0132,"changePrice":-0.0007,"markPrice":0.0522,"indexPrice":0.05228279,"price24hHigh":0.0538,"price24hLow":0.0491,"volumeUsd24h":43161.9624,"volume24h":836739,"ts":1675408117600,"price24hOpen":0.04698,"icon":"https://file.coincall.com/statics/symbol/POPCAT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"POPCATUSD","deliveryKind":"E"},{"contractId":180,"symbol":"DOGSUSD","symbolName":"DOGS/USD","displayName":"DOGSUSDT Perp","baseToken":"DOGS","quoteToken":"USD","price":0.0000404,"changeRate":-0.0025,"changePrice":-1e-7,"markPrice":0.0000404,"indexPrice":0.00004042,"price24hHigh":0.0000415,"price24hLow":0.0000392,"volumeUsd24h":4636.3834241,"volume24h":115351735,"ts":1675408117600,"price24hOpen":0.00003636,"icon":"https://file.coincall.com/statics/symbol/DOGS.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"DOGSUSD","deliveryKind":"E"},{"contractId":181,"symbol":"CATIUSD","symbolName":"CATI/USD","displayName":"CATIUSDT Perp","baseToken":"CATI","quoteToken":"USD","price":0.0604,"changeRate":-0.0242,"changePrice":-0.0015,"markPrice":0.0604,"indexPrice":0.06039,"price24hHigh":0.0627,"price24hLow":0.0584,"volumeUsd24h":2428.6192,"volume24h":39862,"ts":1675408117600,"price24hOpen":0.05436,"icon":"https://file.coincall.com/statics/symbol/CATI.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"CATIUSD","deliveryKind":"E"},{"contractId":182,"symbol":"POLUSD","symbolName":"POL/USD","displayName":"POLUSDT Perp","baseToken":"POL","quoteToken":"USD","price":0.0767,"changeRate":-0.009,"changePrice":-0.0007,"markPrice":0.07671555,"indexPrice":0.07684962,"price24hHigh":0.0786,"price24hLow":0.076,"volumeUsd24h":14005.3156,"volume24h":181119,"ts":1675408117600,"price24hOpen":0.06903,"icon":"https://file.coincall.com/statics/symbol/POL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"POLUSD","deliveryKind":"E"},{"contractId":184,"symbol":"USDCUSD","symbolName":"USDC/USD","displayName":"USDCUSDT Perp","baseToken":"USDC","quoteToken":"USD","price":0.999913,"changeRate":0,"changePrice":0,"markPrice":0.99996777,"indexPrice":1.00104427,"price24hHigh":1,"price24hLow":0.999913,"volumeUsd24h":4366.80773,"volume24h":4367,"ts":1675408117600,"price24hOpen":0.8999217,"icon":"https://file.coincall.com/statics/symbol/USDC.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"USDCUSD","deliveryKind":"E"},{"contractId":187,"symbol":"ACTUSD","symbolName":"ACT/USD","displayName":"ACTUSDT Perp","baseToken":"ACT","quoteToken":"USD","price":0.00805,"changeRate":-0.0025,"changePrice":-0.00002,"markPrice":0.00805811,"indexPrice":0.00805936,"price24hHigh":0.00869,"price24hLow":0.00797,"volumeUsd24h":4411.2363,"volume24h":532698,"ts":1675408117600,"price24hOpen":0.007245,"icon":"https://file.coincall.com/statics/symbol/ACT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ACTUSD","deliveryKind":"E"},{"contractId":188,"symbol":"PNUTUSD","symbolName":"PNUT/USD","displayName":"PNUTUSDT Perp","baseToken":"PNUT","quoteToken":"USD","price":0.04263,"changeRate":-0.0072,"changePrice":-0.00031,"markPrice":0.04267535,"indexPrice":0.04273892,"price24hHigh":0.04405,"price24hLow":0.04213,"volumeUsd24h":7127.54948,"volume24h":164754,"ts":1675408117600,"price24hOpen":0.038367,"icon":"https://file.coincall.com/statics/symbol/PNUT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PNUTUSD","deliveryKind":"E"},{"contractId":189,"symbol":"BANUSD","symbolName":"BAN/USD","displayName":"BANUSDT Perp","baseToken":"BAN","quoteToken":"USD","price":0.0734,"changeRate":0.0266,"changePrice":0.0019,"markPrice":0.0734,"indexPrice":0.07321278,"price24hHigh":0.0778,"price24hLow":0.0678,"volumeUsd24h":17485.4781,"volume24h":239016,"ts":1675408117600,"price24hOpen":0.06606,"icon":"https://file.coincall.com/statics/symbol/BAN.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BANUSD","deliveryKind":"E"},{"contractId":192,"symbol":"MORPHOUSD","symbolName":"MORPHO/USD","displayName":"MORPHOUSDT Perp","baseToken":"MORPHO","quoteToken":"USD","price":1.6737,"changeRate":-0.0275,"changePrice":-0.0473,"markPrice":1.67595,"indexPrice":1.6785,"price24hHigh":1.721,"price24hLow":1.5844,"volumeUsd24h":22373.7828,"volume24h":13602,"ts":1675408117600,"price24hOpen":1.50633,"icon":"https://file.coincall.com/statics/symbol/MORPHO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MORPHOUSD","deliveryKind":"E"},{"contractId":193,"symbol":"ENSUSD","symbolName":"ENS/USD","displayName":"ENSUSDT Perp","baseToken":"ENS","quoteToken":"USD","price":4.406,"changeRate":0.0048,"changePrice":0.021,"markPrice":4.40956112,"indexPrice":4.41,"price24hHigh":4.471,"price24hLow":4.347,"volumeUsd24h":6258.3258,"volume24h":1420.6,"ts":1675408117600,"price24hOpen":3.9654,"icon":"https://file.coincall.com/statics/symbol/ENS.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ENSUSD","deliveryKind":"E"},{"contractId":198,"symbol":"ACXUSD","symbolName":"ACX/USD","displayName":"ACXUSDT Perp","baseToken":"ACX","quoteToken":"USD","price":0.03981,"changeRate":0.0058,"changePrice":0.00023,"markPrice":0.03981038,"indexPrice":0.03995,"price24hHigh":0.0403,"price24hLow":0.03923,"volumeUsd24h":1291.11284,"volume24h":32457,"ts":1675408117600,"price24hOpen":0.035829,"icon":"https://file.coincall.com/statics/symbol/ACX.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"ACXUSD","deliveryKind":"E"},{"contractId":203,"symbol":"HYPEUSD","symbolName":"HYPE/USD","displayName":"HYPEUSDT Perp","baseToken":"HYPE","quoteToken":"USD","price":62.23,"changeRate":-0.0128,"changePrice":-0.808,"markPrice":62.1766,"indexPrice":62.143,"price24hHigh":63.662,"price24hLow":60.478,"volumeUsd24h":206887.016,"volume24h":3324,"ts":1675408117600,"price24hOpen":56.007,"icon":"https://file.coincall.com/statics/symbol/HYPE.png","hasOption":true,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"HYPEUSD","deliveryKind":"E"},{"contractId":205,"symbol":"GRIFFAINUSD","symbolName":"GRIFFAIN/USD","displayName":"GRIFFAINUSDT Perp","baseToken":"GRIFFAIN","quoteToken":"USD","price":0.00833,"changeRate":-0.0502,"changePrice":-0.00044,"markPrice":0.00834423,"indexPrice":0.00834388,"price24hHigh":0.00908,"price24hLow":0.00825,"volumeUsd24h":2912.57058,"volume24h":333393,"ts":1675408117600,"price24hOpen":0.007497,"icon":"https://file.coincall.com/statics/symbol/GRIFFAIN.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"GRIFFAINUSD","deliveryKind":"E"},{"contractId":206,"symbol":"PENGUUSD","symbolName":"PENGU/USD","displayName":"PENGUUSDT Perp","baseToken":"PENGU","quoteToken":"USD","price":0.006201,"changeRate":-0.0177,"changePrice":-0.000112,"markPrice":0.00620346,"indexPrice":0.006208,"price24hHigh":0.006389,"price24hLow":0.006125,"volumeUsd24h":49329.857172,"volume24h":7884484,"ts":1675408117600,"price24hOpen":0.0055809,"icon":"https://file.coincall.com/statics/symbol/PENGU.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PENGUUSD","deliveryKind":"E"},{"contractId":207,"symbol":"VIRTUALUSD","symbolName":"VIRTUAL/USD","displayName":"VIRTUALUSDT Perp","baseToken":"VIRTUAL","quoteToken":"USD","price":0.5675,"changeRate":0.006,"changePrice":0.0034,"markPrice":0.56769486,"indexPrice":0.568,"price24hHigh":0.5804,"price24hLow":0.5607,"volumeUsd24h":26877.58098,"volume24h":47167.3,"ts":1675408117600,"price24hOpen":0.51075,"icon":"https://file.coincall.com/statics/symbol/VIRTUAL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"VIRTUALUSD","deliveryKind":"E"},{"contractId":209,"symbol":"FARTCOINUSD","symbolName":"FARTCOIN/USD","displayName":"FARTCOINUSDT Perp","baseToken":"FARTCOIN","quoteToken":"USD","price":0.1301,"changeRate":0.0655,"changePrice":0.008,"markPrice":0.1302179,"indexPrice":0.13021428,"price24hHigh":0.1331,"price24hLow":0.1213,"volumeUsd24h":47264.59762,"volume24h":369837.9,"ts":1675408117600,"price24hOpen":0.11709,"icon":"https://file.coincall.com/statics/symbol/FARTCOIN.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"FARTCOINUSD","deliveryKind":"E"},{"contractId":210,"symbol":"AIXBTUSD","symbolName":"AIXBT/USD","displayName":"AIXBTUSDT Perp","baseToken":"AIXBT","quoteToken":"USD","price":0.0215,"changeRate":-0.0037,"changePrice":-0.00008,"markPrice":0.02150677,"indexPrice":0.02156571,"price24hHigh":0.02216,"price24hLow":0.02128,"volumeUsd24h":2642.2196,"volume24h":121483,"ts":1675408117600,"price24hOpen":0.01935,"icon":"https://file.coincall.com/statics/symbol/AIXBT.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"AIXBTUSD","deliveryKind":"E"},{"contractId":216,"symbol":"BERAUSD","symbolName":"BERA/USD","displayName":"BERAUSDT Perp","baseToken":"BERA","quoteToken":"USD","price":0.218,"changeRate":-0.0091,"changePrice":-0.002,"markPrice":0.21800024,"indexPrice":0.2189,"price24hHigh":0.223,"price24hLow":0.212,"volumeUsd24h":11170.867,"volume24h":51424.4,"ts":1675408117600,"price24hOpen":0.1962,"icon":"https://file.coincall.com/statics/symbol/BERA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BERAUSD","deliveryKind":"E"},{"contractId":4348,"symbol":"MOODENGUSD","symbolName":"MOODENG/USD","displayName":"MOODENGUSDT Perp","baseToken":"MOODENG","quoteToken":"USD","price":0.03855,"changeRate":0.0068,"changePrice":0.00026,"markPrice":0.03857093,"indexPrice":0.0385918,"price24hHigh":0.03917,"price24hLow":0.03795,"volumeUsd24h":5142.99842,"volume24h":133250,"ts":1675408117600,"price24hOpen":0.034695,"icon":"https://file.coincall.com/statics/symbol/MOODENG.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MOODENGUSD","deliveryKind":"E"},{"contractId":4329,"symbol":"KAITOUSD","symbolName":"KAITO/USD","displayName":"KAITOUSDT Perp","baseToken":"KAITO","quoteToken":"USD","price":0.4393,"changeRate":-0.0115,"changePrice":-0.0051,"markPrice":0.43934887,"indexPrice":0.44012059,"price24hHigh":0.4493,"price24hLow":0.4332,"volumeUsd24h":5455.20331,"volume24h":12372.9,"ts":1675408117600,"price24hOpen":0.39537,"icon":"https://file.coincall.com/statics/symbol/KAITO.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"KAITOUSD","deliveryKind":"E"},{"contractId":4367,"symbol":"PUMPFUNUSD","symbolName":"PUMPFUN/USD","displayName":"PUMPFUNUSDT Perp","baseToken":"PUMPFUN","quoteToken":"USD","price":0.001397,"changeRate":0.0043,"changePrice":0.000006,"markPrice":0.00139645,"indexPrice":0.0013876,"price24hHigh":0.00146,"price24hLow":0.001377,"volumeUsd24h":1973.809457,"volume24h":1396517,"ts":1752124585008,"price24hOpen":0.0012573,"icon":"https://file.coincall.com/statics/symbol/PUMPFUN.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PUMPFUNUSD","deliveryKind":"E"},{"contractId":4463,"symbol":"CLUSD","symbolName":"CL/USD","displayName":"CLUSDT Perp","baseToken":"CL","quoteToken":"USD","price":71.17,"changeRate":-0.0305,"changePrice":-2.24,"markPrice":71.17,"indexPrice":71.12940075,"price24hHigh":73.82,"price24hLow":70.95,"volumeUsd24h":646819.3806,"volume24h":8924.26,"ts":1775099872754,"price24hOpen":64.053,"icon":"https://file.coincall.com/statics/symbol/CL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"CLUSD","deliveryKind":"E"},{"contractId":4464,"symbol":"BZUSD","symbolName":"BZ/USD","displayName":"BZUSDT Perp","baseToken":"BZ","quoteToken":"USD","price":74.71,"changeRate":-0.0292,"changePrice":-2.25,"markPrice":74.67931667,"indexPrice":74.635,"price24hHigh":77.34,"price24hLow":74.48,"volumeUsd24h":194979.0202,"volume24h":2563.2,"ts":1775099872755,"price24hOpen":67.239,"icon":"https://file.coincall.com/statics/symbol/BZ.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BZUSD","deliveryKind":"E"},{"contractId":4465,"symbol":"COPPERUSD","symbolName":"COPPER/USD","displayName":"COPPERUSDT Perp","baseToken":"COPPER","quoteToken":"USD","price":6.123,"changeRate":-0.0186,"changePrice":-0.116,"markPrice":6.12153178,"indexPrice":6.1117376,"price24hHigh":6.259,"price24hLow":6.113,"volumeUsd24h":15777.1944,"volume24h":2545.1,"ts":1775099872756,"price24hOpen":5.5107,"icon":"https://file.coincall.com/statics/symbol/COPPER.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"COPPERUSD","deliveryKind":"E"},{"contractId":4466,"symbol":"NATGASUSD","symbolName":"NATGAS/USD","displayName":"NATGASUSDT Perp","baseToken":"NATGAS","quoteToken":"USD","price":3.24,"changeRate":-0.0012,"changePrice":-0.004,"markPrice":3.23955292,"indexPrice":3.239495,"price24hHigh":3.264,"price24hLow":3.172,"volumeUsd24h":33865.0873,"volume24h":10547.8,"ts":1775099872756,"price24hOpen":2.916,"icon":"https://file.coincall.com/statics/symbol/NATGAS.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"NATGASUSD","deliveryKind":"E"},{"contractId":4467,"symbol":"EWYUSD","symbolName":"EWY/USD","displayName":"EWYUSDT Perp","baseToken":"EWY","quoteToken":"USD","price":199.79,"changeRate":0.0234,"changePrice":4.57,"markPrice":199.74817073,"indexPrice":199.73000361,"price24hHigh":201.28,"price24hLow":190.55,"volumeUsd24h":412319.9178,"volume24h":2090.56,"ts":1775099872757,"price24hOpen":179.811,"icon":"https://file.coincall.com/statics/symbol/EWY.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"EWYUSD","deliveryKind":"E"},{"contractId":4468,"symbol":"GOOGLUSD","symbolName":"GOOGL/USD","displayName":"GOOGLUSDT Perp","baseToken":"GOOGL","quoteToken":"USD","price":348.74,"changeRate":0.0159,"changePrice":5.45,"markPrice":348.74,"indexPrice":348.60483852,"price24hHigh":352.1,"price24hLow":339.93,"volumeUsd24h":48415.4892,"volume24h":139.48,"ts":1775114157381,"price24hOpen":313.866,"icon":"https://file.coincall.com/statics/symbol/GOOGL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"GOOGLUSD","deliveryKind":"E"},{"contractId":4469,"symbol":"METAUSD","symbolName":"META/USD","displayName":"METAUSDT Perp","baseToken":"META","quoteToken":"USD","price":563.57,"changeRate":0.007,"changePrice":3.93,"markPrice":563.57,"indexPrice":563.83702805,"price24hHigh":572.97,"price24hLow":558.88,"volumeUsd24h":30493.9956,"volume24h":53.99,"ts":1775099872758,"price24hOpen":507.213,"icon":"https://file.coincall.com/statics/symbol/META.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"METAUSD","deliveryKind":"E"},{"contractId":4470,"symbol":"NVDAUSD","symbolName":"NVDA/USD","displayName":"NVDAUSDT Perp","baseToken":"NVDA","quoteToken":"USD","price":201.53,"changeRate":-0.0085,"changePrice":-1.73,"markPrice":201.49427162,"indexPrice":201.26958333,"price24hHigh":203.88,"price24hLow":200.19,"volumeUsd24h":103718.8559,"volume24h":514.04,"ts":1775099872759,"price24hOpen":181.377,"icon":"https://file.coincall.com/statics/symbol/NVDA.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"NVDAUSD","deliveryKind":"E"},{"contractId":4471,"symbol":"BASEDUSD","symbolName":"BASED/USD","displayName":"BASEDUSDT Perp","baseToken":"BASED","quoteToken":"USD","price":0.0941,"changeRate":0.0341,"changePrice":0.0031,"markPrice":0.0940731,"indexPrice":0.09405812,"price24hHigh":0.1055,"price24hLow":0.0888,"volumeUsd24h":76718.6671,"volume24h":777352,"ts":1775099872759,"price24hOpen":0.08469,"icon":"https://file.coincall.com/statics/symbol/BASED.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"BASEDUSD","deliveryKind":"E"},{"contractId":4473,"symbol":"PRLUSD","symbolName":"PRL/USD","displayName":"PRLUSDT Perp","baseToken":"PRL","quoteToken":"USD","price":0.1454,"changeRate":0.0041,"changePrice":0.0006,"markPrice":0.1454,"indexPrice":0.14534022,"price24hHigh":0.1504,"price24hLow":0.1395,"volumeUsd24h":12194.8525,"volume24h":84070,"ts":1775099872760,"price24hOpen":0.13086,"icon":"https://file.coincall.com/statics/symbol/PRL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"PRLUSD","deliveryKind":"E"},{"contractId":4488,"symbol":"QQQUSD","symbolName":"QQQ/USD","displayName":"QQQUSDT Perp","baseToken":"QQQ","quoteToken":"USD","price":718.47,"changeRate":0.0018,"changePrice":1.3,"markPrice":718.47,"indexPrice":717.7254593,"price24hHigh":724.32,"price24hLow":712.74,"volumeUsd24h":198428.4703,"volume24h":276.29,"ts":1775705978211,"price24hOpen":646.623,"icon":"https://file.coincall.com/statics/symbol/QQQ.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"QQQUSD","deliveryKind":"E"},{"contractId":4489,"symbol":"SPYUSD","symbolName":"SPY/USD","displayName":"SPYUSDT Perp","baseToken":"SPY","quoteToken":"USD","price":736.97,"changeRate":0.0029,"changePrice":2.13,"markPrice":736.78702644,"indexPrice":736.03062331,"price24hHigh":740.16,"price24hLow":732.94,"volumeUsd24h":55717.4389,"volume24h":75.69,"ts":1775705978212,"price24hOpen":663.273,"icon":"https://file.coincall.com/statics/symbol/SPY.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SPYUSD","deliveryKind":"E"},{"contractId":4490,"symbol":"SNDKUSD","symbolName":"SNDK/USD","displayName":"SNDKUSDT Perp","baseToken":"SNDK","quoteToken":"USD","price":2031.13,"changeRate":-0.0182,"changePrice":-37.57,"markPrice":2030.6041753,"indexPrice":2027.32353456,"price24hHigh":2068.7,"price24hLow":1952.82,"volumeUsd24h":1905378.7312,"volume24h":950.96,"ts":1775705978212,"price24hOpen":1828.017,"icon":"https://file.coincall.com/statics/symbol/SNDK.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"SNDKUSD","deliveryKind":"E"},{"contractId":4491,"symbol":"MUUSD","symbolName":"MU/USD","displayName":"MUUSDT Perp","baseToken":"MU","quoteToken":"USD","price":1097.19,"changeRate":-0.0106,"changePrice":-11.79,"markPrice":1097.31,"indexPrice":1096.28259336,"price24hHigh":1126.2,"price24hLow":1040.58,"volumeUsd24h":2438380.9627,"volume24h":2250.47,"ts":1775705978213,"price24hOpen":987.471,"icon":"https://file.coincall.com/statics/symbol/MU.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"MUUSD","deliveryKind":"E"},{"contractId":4492,"symbol":"AAPLUSD","symbolName":"AAPL/USD","displayName":"AAPLUSDT Perp","baseToken":"AAPL","quoteToken":"USD","price":295.89,"changeRate":0.0038,"changePrice":1.11,"markPrice":295.8417705,"indexPrice":295.81546826,"price24hHigh":301.87,"price24hLow":292.37,"volumeUsd24h":25266.0344,"volume24h":85.21,"ts":1775705978214,"price24hOpen":266.301,"icon":"https://file.coincall.com/statics/symbol/AAPL.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"AAPLUSD","deliveryKind":"E"},{"contractId":4493,"symbol":"TSMUSD","symbolName":"TSM/USD","displayName":"TSMUSDT Perp","baseToken":"TSM","quoteToken":"USD","price":435.88,"changeRate":-0.0206,"changePrice":-9.15,"markPrice":435.8212744,"indexPrice":435.01541306,"price24hHigh":448.45,"price24hLow":434.48,"volumeUsd24h":32741.7889,"volume24h":73.96,"ts":1775705978214,"price24hOpen":392.292,"icon":"https://file.coincall.com/statics/symbol/TSM.png","hasOption":false,"sortIdx":null,"contractType":"E","expireTime":0,"underSymbol":"TSMUSD","deliveryKind":"E"},{"contractId":4641,"symbol":"BTCUSD-25JUN26","symbolName":"BTCUSD-25JUN26","displayName":"BTCUSDT-25JUN26","baseToken":"BTC","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":62792.07,"indexPrice":62860.80358696,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1782028800000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782374400000,"underSymbol":"BTCUSD","deliveryKind":""},{"contractId":4361,"symbol":"BTCUSD-26JUN26","symbolName":"BTCUSD-26JUN26","displayName":"BTCUSDT-26JUN26","baseToken":"BTC","quoteToken":"USD","price":71851.4,"changeRate":0,"changePrice":0,"markPrice":62797.22,"indexPrice":62860.80358696,"price24hHigh":71851.4,"price24hLow":71851.4,"volumeUsd24h":0,"volume24h":0,"ts":1751011200000,"price24hOpen":64666.26,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782460800000,"underSymbol":"BTCUSD","deliveryKind":"M"},{"contractId":4643,"symbol":"BTCUSD-27JUN26","symbolName":"BTCUSD-27JUN26","displayName":"BTCUSDT-27JUN26","baseToken":"BTC","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":62779.91,"indexPrice":62860.80358696,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1782201600000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782547200000,"underSymbol":"BTCUSD","deliveryKind":""},{"contractId":4645,"symbol":"BTCUSD-28JUN26","symbolName":"BTCUSD-28JUN26","displayName":"BTCUSDT-28JUN26","baseToken":"BTC","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":62792.07,"indexPrice":62860.80358696,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1782288000000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782633600000,"underSymbol":"BTCUSD","deliveryKind":""},{"contractId":4620,"symbol":"BTCUSD-3JUL26","symbolName":"BTCUSD-3JUL26","displayName":"BTCUSDT-3JUL26","baseToken":"BTC","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":62818.45,"indexPrice":62860.80358696,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1781164800000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1783065600000,"underSymbol":"BTCUSD","deliveryKind":"W"},{"contractId":4634,"symbol":"BTCUSD-10JUL26","symbolName":"BTCUSD-10JUL26","displayName":"BTCUSDT-10JUL26","baseToken":"BTC","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":62836.6,"indexPrice":62860.80358696,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1781769600000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1783670400000,"underSymbol":"BTCUSD","deliveryKind":"W"},{"contractId":4545,"symbol":"BTCUSD-31JUL26","symbolName":"BTCUSD-31JUL26","displayName":"BTCUSDT-31JUL26","baseToken":"BTC","quoteToken":"USD","price":65550,"changeRate":0,"changePrice":0,"markPrice":62903.72,"indexPrice":62860.80358696,"price24hHigh":65550,"price24hLow":65550,"volumeUsd24h":0,"volume24h":0,"ts":1778054400000,"price24hOpen":58995,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1785484800000,"underSymbol":"BTCUSD","deliveryKind":"M"},{"contractId":4593,"symbol":"BTCUSD-28AUG26","symbolName":"BTCUSD-28AUG26","displayName":"BTCUSDT-28AUG26","baseToken":"BTC","quoteToken":"USD","price":63250,"changeRate":0.0048,"changePrice":300,"markPrice":63066.65,"indexPrice":62860.80358696,"price24hHigh":63600,"price24hLow":62800,"volumeUsd24h":1453.6,"volume24h":0.023,"ts":1779955200000,"price24hOpen":56925,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1787904000000,"underSymbol":"BTCUSD","deliveryKind":"M"},{"contractId":4389,"symbol":"BTCUSD-25SEP26","symbolName":"BTCUSD-25SEP26","displayName":"BTCUSDT-25SEP26","baseToken":"BTC","quoteToken":"USD","price":64850,"changeRate":0,"changePrice":0,"markPrice":63314.1,"indexPrice":62860.80358696,"price24hHigh":64850,"price24hLow":64850,"volumeUsd24h":0,"volume24h":0,"ts":1758873600000,"price24hOpen":58365,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1790323200000,"underSymbol":"BTCUSD","deliveryKind":"M"},{"contractId":4417,"symbol":"BTCUSD-25DEC26","symbolName":"BTCUSD-25DEC26","displayName":"BTCUSDT-25DEC26","baseToken":"BTC","quoteToken":"USD","price":64100,"changeRate":0.0232,"changePrice":1453.5,"markPrice":63951.68,"indexPrice":62860.80358696,"price24hHigh":64250,"price24hLow":62646.5,"volumeUsd24h":513.45,"volume24h":0.008,"ts":1766736000000,"price24hOpen":57690,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1798185600000,"underSymbol":"BTCUSD","deliveryKind":"M"},{"contractId":4453,"symbol":"BTCUSD-26MAR27","symbolName":"BTCUSD-26MAR27","displayName":"BTCUSDT-26MAR27","baseToken":"BTC","quoteToken":"USD","price":75533.7,"changeRate":0,"changePrice":0,"markPrice":64596.32,"indexPrice":62860.80358696,"price24hHigh":75533.7,"price24hLow":75533.7,"volumeUsd24h":0,"volume24h":0,"ts":1774598400000,"price24hOpen":67980.33,"icon":"https://file.coincall.com/statics/symbol/BTC.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1806048000000,"underSymbol":"BTCUSD","deliveryKind":"M"},{"contractId":4642,"symbol":"ETHUSD-25JUN26","symbolName":"ETHUSD-25JUN26","displayName":"ETHUSDT-25JUN26","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1675.61,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1782028800000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782374400000,"underSymbol":"ETHUSD","deliveryKind":""},{"contractId":4364,"symbol":"ETHUSD-26JUN26","symbolName":"ETHUSD-26JUN26","displayName":"ETHUSDT-26JUN26","baseToken":"ETH","quoteToken":"USD","price":2334.09,"changeRate":0,"changePrice":0,"markPrice":1676.32,"indexPrice":1676.71,"price24hHigh":2334.09,"price24hLow":2334.09,"volumeUsd24h":0,"volume24h":0,"ts":1751011200000,"price24hOpen":2100.681,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782460800000,"underSymbol":"ETHUSD","deliveryKind":"M"},{"contractId":4644,"symbol":"ETHUSD-27JUN26","symbolName":"ETHUSD-27JUN26","displayName":"ETHUSDT-27JUN26","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1675.61,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1782201600000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782547200000,"underSymbol":"ETHUSD","deliveryKind":""},{"contractId":4646,"symbol":"ETHUSD-28JUN26","symbolName":"ETHUSD-28JUN26","displayName":"ETHUSDT-28JUN26","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1675.61,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1782288000000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1782633600000,"underSymbol":"ETHUSD","deliveryKind":""},{"contractId":4622,"symbol":"ETHUSD-3JUL26","symbolName":"ETHUSD-3JUL26","displayName":"ETHUSDT-3JUL26","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1675.44,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1781164800000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1783065600000,"underSymbol":"ETHUSD","deliveryKind":"W"},{"contractId":4636,"symbol":"ETHUSD-10JUL26","symbolName":"ETHUSD-10JUL26","displayName":"ETHUSDT-10JUL26","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1675.7,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1781769600000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1783670400000,"underSymbol":"ETHUSD","deliveryKind":"W"},{"contractId":4547,"symbol":"ETHUSD-31JUL26","symbolName":"ETHUSD-31JUL26","displayName":"ETHUSDT-31JUL26","baseToken":"ETH","quoteToken":"USD","price":1998.3,"changeRate":0,"changePrice":0,"markPrice":1676.08,"indexPrice":1676.71,"price24hHigh":1998.3,"price24hLow":1998.3,"volumeUsd24h":0,"volume24h":0,"ts":1778054400000,"price24hOpen":1798.47,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1785484800000,"underSymbol":"ETHUSD","deliveryKind":"M"},{"contractId":4596,"symbol":"ETHUSD-28AUG26","symbolName":"ETHUSD-28AUG26","displayName":"ETHUSDT-28AUG26","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1678.69,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1779955200000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1787904000000,"underSymbol":"ETHUSD","deliveryKind":"M"},{"contractId":4392,"symbol":"ETHUSD-25SEP26","symbolName":"ETHUSD-25SEP26","displayName":"ETHUSDT-25SEP26","baseToken":"ETH","quoteToken":"USD","price":2006,"changeRate":0,"changePrice":0,"markPrice":1682.37,"indexPrice":1676.71,"price24hHigh":2006,"price24hLow":2006,"volumeUsd24h":0,"volume24h":0,"ts":1758873600000,"price24hOpen":1805.4,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1790323200000,"underSymbol":"ETHUSD","deliveryKind":"M"},{"contractId":4420,"symbol":"ETHUSD-25DEC26","symbolName":"ETHUSD-25DEC26","displayName":"ETHUSDT-25DEC26","baseToken":"ETH","quoteToken":"USD","price":2022,"changeRate":0,"changePrice":0,"markPrice":1697.45,"indexPrice":1676.71,"price24hHigh":2022,"price24hLow":2022,"volumeUsd24h":0,"volume24h":0,"ts":1766736000000,"price24hOpen":1819.8,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1798185600000,"underSymbol":"ETHUSD","deliveryKind":"M"},{"contractId":4456,"symbol":"ETHUSD-26MAR27","symbolName":"ETHUSD-26MAR27","displayName":"ETHUSDT-26MAR27","baseToken":"ETH","quoteToken":"USD","price":0,"changeRate":0,"changePrice":0,"markPrice":1714.31,"indexPrice":1676.71,"price24hHigh":0,"price24hLow":0,"volumeUsd24h":0,"volume24h":0,"ts":1774598400000,"price24hOpen":0,"icon":"https://file.coincall.com/statics/symbol/ETH.png","hasOption":false,"sortIdx":null,"contractType":"D","expireTime":1806048000000,"underSymbol":"ETHUSD","deliveryKind":"M"}]};
let ccSpreadsLeveragePromise = null;
let ccSpreadsRefreshBusy = false;
let ccSpreadsTradeModalOpenedAt = 0;
let ccSpreadsTradeModalState = null;
const CC_SPREADS_PANEL_NAMES = ['open-positions','open-orders','order-history','trade-history','position-history','funding','exercise','assets','logs','diagnostic'];
let ccSpreadsSelectedPanel = 'trade-history';
let ccLastSpreadsFundingRows = [];
let ccSpreadsFundingHistoryLoaded = false;
const CC_SPREADS_LATENCY={ops:[],bookRenders:[],longTasks:[],actionBusyUntil:0,lastPrivateRefreshMs:0,lastBookRenderMs:0,seq:0};
function ccSpreadsLatencyNow(){return (window.performance&&performance.now)?performance.now():Date.now()}
function ccSpreadsLatencyStart(kind,market,symbol){const rec={id:++CC_SPREADS_LATENCY.seq,kind,market,symbol,start:ccSpreadsLatencyNow(),wall:Date.now(),apiMs:null,confirmMs:null,postMs:null,totalMs:null,status:'running'};CC_SPREADS_LATENCY.actionBusyUntil=Date.now()+2500;ccSpreadsRenderLatencyPanel(true);return rec}
function ccSpreadsLatencyFinish(rec,status,error){if(!rec)return;rec.totalMs=ccSpreadsLatencyNow()-rec.start;rec.status=status||'ok';rec.error=error?String(error.message||error).slice(0,80):'';CC_SPREADS_LATENCY.ops.unshift(rec);CC_SPREADS_LATENCY.ops=CC_SPREADS_LATENCY.ops.slice(0,50);CC_SPREADS_LATENCY.actionBusyUntil=Date.now()+1000;ccSpreadsRenderLatencyPanel(true)}
function ccSpreadsLatencyPriorityActive(){return Date.now()<CC_SPREADS_LATENCY.actionBusyUntil}
function ccSpreadsLatencyRecordBook(ms){const now=Date.now();CC_SPREADS_LATENCY.lastBookRenderMs=ms;CC_SPREADS_LATENCY.bookRenders.push({t:now,ms});CC_SPREADS_LATENCY.bookRenders=CC_SPREADS_LATENCY.bookRenders.filter(x=>now-x.t<5000);ccSpreadsScheduleLatencyPanelRender(1000)}
function ccSpreadsLatencyRecordLongTask(ms){const now=Date.now();CC_SPREADS_LATENCY.longTasks.unshift({t:now,ms});CC_SPREADS_LATENCY.longTasks=CC_SPREADS_LATENCY.longTasks.slice(0,20);ccSpreadsScheduleLatencyPanelRender(1000)}
try{if(window.PerformanceObserver){const po=new PerformanceObserver(list=>{for(const entry of list.getEntries())ccSpreadsLatencyRecordLongTask(entry.duration||0)});po.observe({entryTypes:['longtask']})}}catch{}
function ccSpreadsLatencyFmt(v){return Number.isFinite(Number(v))?Math.round(Number(v))+'ms':'—'}
function ccSpreadsAgeText(ts){const n=Number(ts);if(!Number.isFinite(n)||n<=0)return '—';const s=Math.max(0,Math.round((Date.now()-n)/1000));return s<60?s+'s':Math.floor(s/60)+'m '+(s%60)+'s'}
let ccSpreadsLatencyRenderTimer=0;let ccSpreadsLatencyLastHtml='';let ccSpreadsLatencyLastRenderAt=0;function ccSpreadsScheduleLatencyPanelRender(delay=1000){clearTimeout(ccSpreadsLatencyRenderTimer);ccSpreadsLatencyRenderTimer=setTimeout(ccSpreadsRenderLatencyPanel,delay)}
function ccSpreadsLatencyP95(rows,key,kind){const vals=rows.filter(r=>!kind||r.kind===kind).map(r=>Number(r[key])).filter(Number.isFinite).sort((a,b)=>a-b);return vals.length?vals[Math.min(vals.length-1,Math.floor(vals.length*.95))]:NaN}
function ccSpreadsRenderLatencyPanel(force){const el=cc('ccSpreadsLatencyPanel');if(!el)return;const panel=el.closest('[data-cc-spreads-panel-content="diagnostic"]');if(panel&&panel.hidden)return;const renderNow=Date.now();if(!force&&ccSpreadsLatencyLastRenderAt&&renderNow-ccSpreadsLatencyLastRenderAt<1000)return;ccSpreadsLatencyLastRenderAt=renderNow;if(!el.dataset.ccDiagStaticReady||!el.querySelector('[data-cc-diag-value="ws"]')){const fields=[['ws','WS'],['evt','evt'],['dt','dt'],['reconn','reconn'],['acct','acct'],['rest','REST'],['place','place'],['cancel','cancel'],['api','API'],['book','book'],['priv','priv'],['long','long'],['prio','prio'],['n','n']];el.innerHTML='<div class="cc-spreads-latency-grid">'+fields.map(f=>'<div class="cc-spreads-latency-stat">'+f[1]+' <b data-cc-diag-value="'+f[0]+'">—</b></div>').join('')+'</div><div class="cc-spreads-latency-list" data-cc-diag-list></div><div class="cc-order-diagnostic-panel" id="ccOrderDiagnosticPanel"></div>';el.dataset.ccDiagStaticReady='1';ccSpreadsLatencyLastHtml=''}const rows=CC_SPREADS_LATENCY.ops,now=Date.now(),bookRps=CC_SPREADS_LATENCY.bookRenders.filter(x=>now-x.t<1000).length,longMs=CC_SPREADS_LATENCY.longTasks[0]?.ms;const busy=ccSpreadsLatencyPriorityActive();const ws=CC_FUTURES_PRIVATE_WS||{},sock=ws.socket,rs=sock?Number(sock.readyState):-1,wsState=rs===WebSocket.OPEN?'OPEN':(rs===WebSocket.CONNECTING?'CONNECTING':(rs===WebSocket.CLOSING?'CLOSING':'CLOSED'));const set=(name,value,warn)=>{const node=el.querySelector('[data-cc-diag-value="'+name+'"]');if(!node)return;const text=String(value==null?'—':value);if(node.textContent!==text)node.textContent=text;node.classList.toggle('cc-spreads-latency-warn',!!warn)};set('ws',wsState,wsState!=='OPEN');set('evt',ccSpreadsAgeText(ws.lastEventAt));set('dt',ws.lastDt??'—');set('reconn',ws.reconnectCount||0);set('acct',ws.account||'—');set('rest',ccSpreadsAgeText(ccFuturesPrivateLastReconcileAt));set('place',ccSpreadsLatencyFmt(ccSpreadsLatencyP95(rows,'totalMs','place')));set('cancel',ccSpreadsLatencyFmt(ccSpreadsLatencyP95(rows,'totalMs','cancel')));set('api',ccSpreadsLatencyFmt(ccSpreadsLatencyP95(rows,'apiMs')));set('book',bookRps+'/s '+ccSpreadsLatencyFmt(CC_SPREADS_LATENCY.lastBookRenderMs));set('priv',ccSpreadsLatencyFmt(CC_SPREADS_LATENCY.lastPrivateRefreshMs));set('long',ccSpreadsLatencyFmt(longMs));set('prio',busy?'ON':'off');set('n',rows.length);const list=el.querySelector('[data-cc-diag-list]');if(list){const last=rows.slice(0,4).map(r=>'<div class="cc-spreads-latency-row"><span>'+ccEsc(r.kind)+'</span><span>'+ccEsc(r.market||'')+'</span><span class="'+(r.totalMs>1000?'cc-spreads-latency-warn':'')+'">'+ccSpreadsLatencyFmt(r.totalMs)+' api '+ccSpreadsLatencyFmt(r.apiMs)+' conf '+ccSpreadsLatencyFmt(r.confirmMs)+' post '+ccSpreadsLatencyFmt(r.postMs)+(r.error?' · '+ccEsc(r.error):'')+'</span></div>').join('')||'<div class="muted">No place/cancel measurements yet.</div>';if(last!==ccSpreadsLatencyLastHtml){list.innerHTML=last;ccSpreadsLatencyLastHtml=last}}ccRenderOrderDiagnosticPanel()}

function ccSpreadsCurrentPanel(){const modal=cc('ccSpreadsTradeModal');return ccSpreadsNormalizePanel((modal&&modal.dataset?modal.dataset.ccSpreadsSelectedPanel:'')||ccSpreadsSelectedPanel)}
function ccSpreadsStorePanel(name){const active=ccSpreadsNormalizePanel(name);ccSpreadsSelectedPanel=active;const modal=cc('ccSpreadsTradeModal');if(modal&&modal.dataset)modal.dataset.ccSpreadsSelectedPanel=active;const host=document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-positions-orders]');if(host&&host.dataset)host.dataset.ccSpreadsSelectedPanel=active;return active}
const CC_SPREADS_TRADE_INPUTS_KEY = 'cc_spreads_trade_inputs_v1';
let ccSpreadsTradeInputs = {};
let ccSpreadsTradeFocusState = null;
const CC_SPREADS_LOG_LIMIT = 300;
let ccSpreadsLogs = [];
function ccSpreadsTradeInputsLoad(){try{const raw=localStorage.getItem(CC_SPREADS_TRADE_INPUTS_KEY);const parsed=raw?JSON.parse(raw):{};ccSpreadsTradeInputs=parsed&&typeof parsed==='object'?parsed:{};}catch{ccSpreadsTradeInputs={}}}
function ccSpreadsTradeInputsSave(){try{localStorage.setItem(CC_SPREADS_TRADE_INPUTS_KEY,JSON.stringify(ccSpreadsTradeInputs||{}));}catch{}}
function ccSpreadsTradeInputKeyParts(market,role,symbol){market=String(market||'').toLowerCase();role=String(role||'').toLowerCase();symbol=String(symbol||'').toUpperCase();return market&&role&&symbol?market+'|'+role+'|'+symbol:''}
function ccSpreadsTradeInputKey(panel){const market=String(panel?.dataset?.ccSpreadsTradePanel||'').toLowerCase();const role=String(panel?.dataset?.ccSpreadsTradeRole||'').toLowerCase();const symbol=String(panel?.dataset?.ccSpreadsTradeSymbol||'').toUpperCase();return market&&role&&symbol?market+'|'+role+'|'+symbol:''}
function ccSpreadsTradeInputState(market,role,symbol){return ccSpreadsTradeInputs[ccSpreadsTradeInputKeyParts(market,role,symbol)]||{}}
function ccSpreadsSelectedAttr(value,want){return String(value||'').toUpperCase()===String(want||'').toUpperCase()?' selected':''}
function ccSpreadsInputValueAttr(value){return value!==undefined&&value!==null&&String(value)!==''?' value="'+ccEsc(String(value))+'"':''}
function ccSpreadsRememberTradePanel(panel){
  const key=ccSpreadsTradeInputKey(panel); if(!key)return;
  const market=String(panel.dataset.ccSpreadsTradePanel||'').toLowerCase();
  const state={market,symbol:String(panel.dataset.ccSpreadsTradeSymbol||'').toUpperCase()};
  if(market==='spot'){
    state.ordType=panel.querySelector('[data-cc-spreads-spot-ord-type]')?.value||'';
    state.price=panel.querySelector('[data-cc-spreads-spot-px]')?.value||'';
    state.amount=panel.querySelector('[data-cc-spreads-spot-sz]')?.value||'';
    state.postOnly=panel.dataset.ccSpreadsPostOnly;
  }else{
    state.margin=panel.querySelector('[id$="_FuturesMargin"]')?.value||'';
    state.ordType=panel.querySelector('[data-cc-spreads-futures-ord-type]')?.value||'';
    state.price=panel.querySelector('[data-cc-spreads-futures-px]')?.value||'';
    state.amount=panel.querySelector('[data-cc-spreads-futures-sz]')?.value||'';
    state.unit=panel.querySelector('[data-cc-spreads-futures-unit]')?.value||'';
    state.leverage=panel.querySelector('[data-cc-spreads-futures-leverage]')?.value||'';
  }
  ccSpreadsTradeInputs[key]=state; ccSpreadsTradeInputsSave();
}
function ccSpreadsRememberTradePanels(){document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-panel]').forEach(ccSpreadsRememberTradePanel)}
function ccSpreadsCaptureTradeFocus(){
  const el=document.activeElement;
  const panel=el?.closest?.('#ccSpreadsTradeModal [data-cc-spreads-trade-panel]');
  if(!panel||!el.matches?.('input,select,textarea')){ccSpreadsTradeFocusState=null;return}
  ccSpreadsTradeFocusState={key:ccSpreadsTradeInputKey(panel),selector:''};
  for(const attr of ['data-cc-spreads-spot-px','data-cc-spreads-spot-sz','data-cc-spreads-spot-ord-type','data-cc-spreads-futures-px','data-cc-spreads-futures-sz','data-cc-spreads-futures-ord-type','data-cc-spreads-futures-unit','data-cc-spreads-futures-leverage']){
    if(el.hasAttribute(attr)){ccSpreadsTradeFocusState.selector='['+attr+']';break}
  }
  if(!ccSpreadsTradeFocusState.selector&&el.id)ccSpreadsTradeFocusState.selector='#'+CSS.escape(el.id);
  ccSpreadsTradeFocusState.start=typeof el.selectionStart==='number'?el.selectionStart:null;
  ccSpreadsTradeFocusState.end=typeof el.selectionEnd==='number'?el.selectionEnd:null;
}
function ccSpreadsApplyTradePanelState(panel){
  const key=ccSpreadsTradeInputKey(panel), state=key?ccSpreadsTradeInputs[key]:null; if(!state)return;
  const setValue=(sel,value)=>{const el=panel.querySelector(sel); if(el&&value!==undefined&&value!==null&&value!=='')el.value=String(value)};
  if(state.market==='spot'){
    setValue('[data-cc-spreads-spot-ord-type]',state.ordType); setValue('[data-cc-spreads-spot-px]',state.price); setValue('[data-cc-spreads-spot-sz]',state.amount);
    if(state.postOnly!==undefined)panel.dataset.ccSpreadsPostOnly=String(state.postOnly);
  }else{
    setValue('[id$="_FuturesMargin"]',state.margin); setValue('[data-cc-spreads-futures-ord-type]',state.ordType); setValue('[data-cc-spreads-futures-px]',state.price); setValue('[data-cc-spreads-futures-sz]',state.amount); setValue('[data-cc-spreads-futures-unit]',state.unit); setValue('[data-cc-spreads-futures-leverage]',state.leverage);
  }
}
function ccSpreadsApplyTradePanelsState(){document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-panel]').forEach(ccSpreadsApplyTradePanelState)}
function ccSpreadsRestoreTradeFocus(){
  const st=ccSpreadsTradeFocusState;if(!st||!st.key||!st.selector)return;
  const panel=Array.from(document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-panel]')).find(p=>ccSpreadsTradeInputKey(p)===st.key);
  const el=panel?.querySelector?.(st.selector);
  if(!el)return;
  requestAnimationFrame(()=>{try{el.focus({preventScroll:true});if(typeof st.start==='number'&&typeof el.setSelectionRange==='function')el.setSelectionRange(st.start,typeof st.end==='number'?st.end:st.start);}catch{}});
}
function ccSpreadsCaptureBottomScroll(){const modal=cc('ccSpreadsTradeModal');if(!modal)return null;const dialog=modal.querySelector('.okx-nitro-modal');const state={dialogTop:dialog?dialog.scrollTop:0,panels:{}};modal.querySelectorAll('[data-cc-spreads-panel-content]').forEach(panel=>{const name=panel.dataset.ccSpreadsPanelContent||'';state.panels[name]={panelTop:panel.scrollTop||0,nodes:Array.from(panel.querySelectorAll('.okx-spot-orders-wrap,.table-wrap')).map(n=>({top:n.scrollTop||0,left:n.scrollLeft||0}))}});return state}
function ccSpreadsRestoreBottomScroll(state){if(!state)return;const apply=()=>{const modal=cc('ccSpreadsTradeModal');if(!modal)return;const dialog=modal.querySelector('.okx-nitro-modal');if(dialog&&state.dialogTop)dialog.scrollTop=state.dialogTop;Object.keys(state.panels||{}).forEach(name=>{const panel=modal.querySelector('[data-cc-spreads-panel-content="'+name+'"]');const saved=state.panels[name];if(!panel||!saved)return;if(saved.panelTop)panel.scrollTop=saved.panelTop;Array.from(panel.querySelectorAll('.okx-spot-orders-wrap,.table-wrap')).forEach((n,i)=>{const v=saved.nodes&&saved.nodes[i];if(v){n.scrollTop=v.top||0;n.scrollLeft=v.left||0}})})};apply();requestAnimationFrame(apply);setTimeout(apply,0)}
ccSpreadsTradeInputsLoad();
const CC_SPREADS_CHART_MODE_STORAGE_KEY = 'cc_spreads_popup_1m_chart_mode_v1';
function ccSpreadsLoadPopupChartMode(){
  try{ return localStorage.getItem(CC_SPREADS_CHART_MODE_STORAGE_KEY)==='trade-only' ? 'trade-only' : 'interface'; }catch(e){ return 'interface'; }
}
const CC_SPREADS_CHART_STATE = { key:'', rows:[], fundingRows:[], fundingEnabled:false, fundingSymbol:'', bidAskVisible:false, bidAskDiffVisible:false, loading:false, requestSeq:0, source:'', error:'', visibleCandles:240, panOffset:0, priceRange:null, fundingRange:null, view:null, drag:null, wheelBound:false, interfaceMode:ccSpreadsLoadPopupChartMode() };
const CC_SPREADS_CHART_STORAGE_PREFIX = 'cc_spreads_1m_chart_state_v1:';
const CC_SPREADS_MAIN_BID_ASK_KEY = 'cc_spreads_main_bid_ask_overlay_v1';
const CC_SPREADS_MAIN_BID_ASK_DIFF_KEY = 'cc_spreads_main_bid_ask_diff_overlay_v1';
const CC_SPREADS_POPUP_SPREAD_AXIS_STEP_PCT = 0.02;
const CC_SPREADS_POPUP_DIFF_AXIS_STEP_PCT = 0.02;
const CC_SPREADS_POPUP_FUNDING_AXIS_STEP_PCT = 0.005;
const CC_SPREADS_POPUP_FUNDING_AXIS_LABEL_X_OFFSET = 24;
const CC_SPREADS_POPUP_FUNDING_LIVE_REFRESH_MS = 5000;
const CC_SPREADS_MAIN_CHART_STATE = { row:null, col:null, days:3, requestSeq:0, fundingEnabled:false, bidAskEnabled:false, bidAskDiffEnabled:false, fundingRows:[], fundingSymbol:'', lastRefreshAt:0, mode:'single', allPairs:[], allSource:'mark', allPairVisibility:{} };
const CC_SPREADS_MEAN_WINDOW = 120;
const CC_SPREADS_ENVELOPE_STEPS = [0.0005];
const CC_SPREADS_MARK_LINE_COLOR = '#4b5563';
const CC_SPREADS_MA_LINE_COLOR = '#1e3a8a';
const CC_SPREADS_MA_LINE_WIDTH = 3;
const CC_SPREADS_MA_TAG_COLOR = '#1e3a8a';
const CC_SPREADS_BID_ASK_DIFF_LINE_COLOR = '#a78bfa';
const CC_SPREADS_ALL_SPREADS_MARKER = 'CC_SPREADS_ALL_SPREADS_CONTEXTMENU_20260603T1605Z';
const CC_SPREADS_ALL_COLORS = ['#2563eb','#16a34a','#dc2626','#9333ea','#ea580c','#0891b2','#be123c','#4f46e5','#65a30d','#d97706','#0f766e','#7c3aed','#0284c7','#c2410c','#059669','#db2777','#475569','#84cc16'];
function ccSpreadsLoadMainBidAskEnabled(){
  try { return localStorage.getItem(CC_SPREADS_MAIN_BID_ASK_KEY) === '1'; } catch { return false; }
}
function ccSpreadsSaveMainBidAskEnabled(enabled){
  try { localStorage.setItem(CC_SPREADS_MAIN_BID_ASK_KEY, enabled ? '1' : '0'); } catch {}
}
function ccSpreadsLoadMainBidAskDiffEnabled(){
  try { return localStorage.getItem(CC_SPREADS_MAIN_BID_ASK_DIFF_KEY) === '1'; } catch { return false; }
}
function ccSpreadsSaveMainBidAskDiffEnabled(enabled){
  try { localStorage.setItem(CC_SPREADS_MAIN_BID_ASK_DIFF_KEY, enabled ? '1' : '0'); } catch {}
}
CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled = ccSpreadsLoadMainBidAskEnabled();
CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled = ccSpreadsLoadMainBidAskDiffEnabled();
let ccSpreadsMainChart = null;
let ccSpreadsMainResizeObserver = null;
function ccSpreadsLoadMainChartSettings(){
  try{
    const parsed = JSON.parse(localStorage.getItem(CC_SPREADS_MAIN_CHART_SETTINGS_KEY) || '{}');
    if(!parsed || typeof parsed !== 'object') return {};
    const asset = String(parsed.asset || '').toUpperCase() === 'ETH' ? 'ETH' : 'BTC';
    const days = [1,3,7,14,30,60,90].includes(Number(parsed.days)) ? Number(parsed.days) : 3;
    const mode = parsed.mode === 'row' ? 'row' : (parsed.mode === 'single' ? 'single' : '');
    const out = {
      asset,
      days,
      mode,
      rowSymbol:String(parsed.rowSymbol || '').toUpperCase(),
      colSymbol:String(parsed.colSymbol || '').toUpperCase(),
      fundingEnabled:parsed.fundingEnabled === true,
      bidAskEnabled:parsed.bidAskEnabled === true,
      bidAskDiffEnabled:parsed.bidAskDiffEnabled === true,
      pairVisibility:{}
    };
    Object.entries(parsed.pairVisibility || {}).forEach(([key, value]) => {
      const normalized = String(key || '').toUpperCase();
      if(normalized) out.pairVisibility[normalized] = value !== false;
    });
    return out;
  }catch(e){ return {}; }
}
function ccSpreadsSaveMainChartSettings(patch){
  try{
    const current = ccSpreadsLoadMainChartSettings();
    const state = Object.assign({}, current, patch || {});
    state.asset = String(state.asset || ccSpreadsHeaderAsset || 'BTC').toUpperCase() === 'ETH' ? 'ETH' : 'BTC';
    state.days = [1,3,7,14,30,60,90].includes(Number(state.days)) ? Number(state.days) : Number(CC_SPREADS_MAIN_CHART_STATE.days || 3);
    state.mode = state.mode === 'row' ? 'row' : (state.mode === 'single' ? 'single' : '');
    state.rowSymbol = String(state.rowSymbol || '').toUpperCase();
    state.colSymbol = String(state.colSymbol || '').toUpperCase();
    state.fundingEnabled = !!state.fundingEnabled;
    state.bidAskEnabled = !!state.bidAskEnabled;
    state.bidAskDiffEnabled = !!state.bidAskDiffEnabled;
    const pairVisibility = {};
    Object.entries(state.pairVisibility || {}).forEach(([key, value]) => {
      const normalized = String(key || '').toUpperCase();
      if(normalized) pairVisibility[normalized] = value !== false;
    });
    state.pairVisibility = pairVisibility;
    localStorage.setItem(CC_SPREADS_MAIN_CHART_SETTINGS_KEY, JSON.stringify(state));
  }catch(e){}
}
function ccSpreadsSaveCurrentMainChartSettings(extra){
  const base = {
    asset:ccSpreadsHeaderAsset,
    days:Number(CC_SPREADS_MAIN_CHART_STATE.days || 3),
    mode:CC_SPREADS_MAIN_CHART_STATE.mode || '',
    rowSymbol:String(CC_SPREADS_MAIN_CHART_STATE.row?.symbol || '').toUpperCase(),
    colSymbol:String(CC_SPREADS_MAIN_CHART_STATE.col?.symbol || '').toUpperCase(),
    fundingEnabled:!!CC_SPREADS_MAIN_CHART_STATE.fundingEnabled,
    bidAskEnabled:!!CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled,
    bidAskDiffEnabled:!!CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled,
    pairVisibility:Object.assign({}, CC_SPREADS_MAIN_CHART_STATE.allPairVisibility || {})
  };
  ccSpreadsSaveMainChartSettings(Object.assign(base, extra || {}));
}
function ccSpreadsSyncMainDaysButtons(){
  const days = Number(CC_SPREADS_MAIN_CHART_STATE.days || 3);
  document.querySelectorAll('[data-cc-spreads-main-days]').forEach(btn => {
    btn.classList.toggle('active', Number(btn.dataset.ccSpreadsMainDays) === days);
  });
}
function ccSpreadsRestoreMainPairVisibility(row, pairs){
  const saved = ccSpreadsLoadMainChartSettings();
  const savedRow = String(saved.rowSymbol || '').toUpperCase();
  const rowSymbol = String(row?.symbol || '').toUpperCase();
  if(saved.mode !== 'row' || !savedRow || savedRow !== rowSymbol) return;
  const allowed = new Set((Array.isArray(pairs) ? pairs : []).map(pair => ccSpreadsMainPairKey(pair)).filter(Boolean));
  Object.entries(saved.pairVisibility || {}).forEach(([key, value]) => {
    const normalized = String(key || '').toUpperCase();
    if(allowed.has(normalized)) CC_SPREADS_MAIN_CHART_STATE.allPairVisibility[normalized] = value !== false;
  });
}
function ccSpreadsRestoreMainChartSettings(options){
  let hasSavedSettings = false;
  try{ hasSavedSettings = !!localStorage.getItem(CC_SPREADS_MAIN_CHART_SETTINGS_KEY); }catch(e){}
  const saved = ccSpreadsLoadMainChartSettings();
  CC_SPREADS_MAIN_CHART_STATE.days = Number(saved.days || 3);
  if(hasSavedSettings){
    CC_SPREADS_MAIN_CHART_STATE.fundingEnabled = !!saved.fundingEnabled;
    CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled = !!saved.bidAskEnabled;
    CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled = !!saved.bidAskDiffEnabled;
  }
  ccSpreadsSyncMainDaysButtons();
  const bidAskToggle = cc('ccSpreadsMainBidAskToggle');
  if(bidAskToggle) bidAskToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled;
  const bidAskDiffToggle = cc('ccSpreadsMainBidAskMedianDiffToggle');
  if(bidAskDiffToggle) bidAskDiffToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled;
  const fundingToggle = cc('ccSpreadsMainFundingToggle');
  if(fundingToggle) fundingToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.fundingEnabled;
  if(saved.mode === 'row' && saved.rowSymbol){
    const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
    const rowIndex = rows.findIndex(row => String(row?.symbol || '').toUpperCase() === saved.rowSymbol);
    if(rowIndex >= 0){
      if(options && options.skipHeavyRowAutoLoad) return false;
      ccSpreadsMainLoadRowChart(rowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
      return true;
    }
  }
  if(saved.mode === 'single' && saved.rowSymbol && saved.colSymbol){
    return ccSpreadsMainSelectBySymbols(ccSpreadsHeaderAsset, saved.rowSymbol, saved.colSymbol);
  }
  return false;
}
function ccSpreadsSetLastUpdate(date){
  const el = cc('ccSpreadsLastUpdate'); if(!el) return;
  const d = date instanceof Date ? date : new Date();
  const dateText = d.getUTCFullYear() + '-' + String(d.getUTCMonth() + 1).padStart(2,'0') + '-' + String(d.getUTCDate()).padStart(2,'0');
  const timeText = String(d.getUTCHours()).padStart(2,'0') + ':' + String(d.getUTCMinutes()).padStart(2,'0') + ':' + String(d.getUTCSeconds()).padStart(2,'0') + ' UTC';
  el.textContent = 'Date: ' + dateText + ' · Updated: ' + timeText;
}
function ccSpreadsParseSymbolExpiry(symbol){
  const match = String(symbol || '').toUpperCase().match(/-(\d{1,2})([A-Z]{3})(\d{2})$/);
  if(!match) return null;
  const monthMap = {JAN:0,FEB:1,MAR:2,APR:3,MAY:4,JUN:5,JUL:6,AUG:7,SEP:8,OCT:9,NOV:10,DEC:11};
  const month = monthMap[match[2]];
  if(month === undefined) return null;
  return Date.UTC(2000 + Number(match[3]), month, Number(match[1]), 0, 0, 0, 0);
}
const CC_SPREADS_DAILY_EXP_KEY = 'cc_spreads_daily_exp_v2';
const CC_SPREADS_WEEKLY_EXP_KEY = 'cc_spreads_weekly_exp_v1';
const CC_SPREADS_MONTHLY_EXP_KEY = 'cc_spreads_monthly_exp_v1';
const CC_SPREADS_QUARTERLY_EXP_KEY = 'cc_spreads_quarterly_exp_v1';
const CC_SPREADS_MATRIX_DIFF_COL_KEY = 'cc_spreads_matrix_diff_col_v1';
const CC_SPREADS_MATRIX_BID_COL_KEY = 'cc_spreads_matrix_bid_col_v1';
const CC_SPREADS_MATRIX_ASK_COL_KEY = 'cc_spreads_matrix_ask_col_v1';
function ccSpreadsMatrixStoredVisible(key){
  try { const saved = localStorage.getItem(key); return saved === null ? true : saved === '1'; } catch { return true; }
}
function ccSpreadsMatrixDiffColEnabled(){
  const toggle = cc('ccSpreadsMatrixDiffCol');
  if(toggle) return !!toggle.checked;
  return ccSpreadsMatrixStoredVisible(CC_SPREADS_MATRIX_DIFF_COL_KEY);
}
function ccSpreadsMatrixBidColEnabled(){
  const toggle = cc('ccSpreadsMatrixBidCol');
  if(toggle) return !!toggle.checked;
  return ccSpreadsMatrixStoredVisible(CC_SPREADS_MATRIX_BID_COL_KEY);
}
function ccSpreadsMatrixAskColEnabled(){
  const toggle = cc('ccSpreadsMatrixAskCol');
  if(toggle) return !!toggle.checked;
  return ccSpreadsMatrixStoredVisible(CC_SPREADS_MATRIX_ASK_COL_KEY);
}
function ccSpreadsSyncMatrixVisibilityControl(id, key){
  const toggle = cc(id);
  if(!toggle) return true;
  toggle.checked = ccSpreadsMatrixStoredVisible(key);
  return toggle.checked;
}
function ccSpreadsDailyExpEnabled(){
  const toggle = cc('ccSpreadsDailyExp');
  if(toggle) return !!toggle.checked;
  try { return localStorage.getItem(CC_SPREADS_DAILY_EXP_KEY) === '1'; } catch { return false; }
}
function ccSpreadsWeeklyExpEnabled(){
  const toggle = cc('ccSpreadsWeeklyExp');
  if(toggle) return !!toggle.checked;
  try { return localStorage.getItem(CC_SPREADS_WEEKLY_EXP_KEY) === '1'; } catch { return false; }
}
function ccSpreadsMonthlyExpEnabled(){
  const toggle = cc('ccSpreadsMonthlyExp');
  if(toggle) return !!toggle.checked;
  try { const saved = localStorage.getItem(CC_SPREADS_MONTHLY_EXP_KEY); return saved === null ? true : saved === '1'; } catch { return true; }
}
function ccSpreadsQuarterlyExpEnabled(){
  const toggle = cc('ccSpreadsQuarterlyExp');
  if(toggle) return !!toggle.checked;
  try { const saved = localStorage.getItem(CC_SPREADS_QUARTERLY_EXP_KEY); return saved === null ? true : saved === '1'; } catch { return true; }
}
function ccSpreadsSyncDailyExpControl(){
  const toggle = cc('ccSpreadsDailyExp');
  if(!toggle) return false;
  let enabled = false;
  try { enabled = localStorage.getItem(CC_SPREADS_DAILY_EXP_KEY) === '1'; } catch {}
  toggle.checked = enabled;
  return enabled;
}
function ccSpreadsSyncWeeklyExpControl(){
  const toggle = cc('ccSpreadsWeeklyExp');
  if(!toggle) return false;
  let enabled = false;
  try { enabled = localStorage.getItem(CC_SPREADS_WEEKLY_EXP_KEY) === '1'; } catch {}
  toggle.checked = enabled;
  return enabled;
}
function ccSpreadsSyncMonthlyExpControl(){
  const toggle = cc('ccSpreadsMonthlyExp');
  if(!toggle) return false;
  let enabled = true;
  try { const saved = localStorage.getItem(CC_SPREADS_MONTHLY_EXP_KEY); enabled = saved === null ? true : saved === '1'; } catch {}
  toggle.checked = enabled;
  return enabled;
}
function ccSpreadsSyncQuarterlyExpControl(){
  const toggle = cc('ccSpreadsQuarterlyExp');
  if(!toggle) return false;
  let enabled = true;
  try { const saved = localStorage.getItem(CC_SPREADS_QUARTERLY_EXP_KEY); enabled = saved === null ? true : saved === '1'; } catch {}
  toggle.checked = enabled;
  return enabled;
}
function ccSpreadsSyncMatrixVisibilityControls(){
  ccSpreadsSyncMatrixVisibilityControl('ccSpreadsMatrixDiffCol', CC_SPREADS_MATRIX_DIFF_COL_KEY);
  ccSpreadsSyncMatrixVisibilityControl('ccSpreadsMatrixBidCol', CC_SPREADS_MATRIX_BID_COL_KEY);
  ccSpreadsSyncMatrixVisibilityControl('ccSpreadsMatrixAskCol', CC_SPREADS_MATRIX_ASK_COL_KEY);
}
function ccSpreadsMatrixGridTemplate(columnsLength){
  const tracks = ['132px','86px','92px'];
  if(ccSpreadsMatrixDiffColEnabled()) tracks.push('62px');
  if(ccSpreadsMatrixBidColEnabled()) tracks.push('68px');
  if(ccSpreadsMatrixAskColEnabled()) tracks.push('68px');
  tracks.push('repeat(' + Math.max(0, Number(columnsLength) || 0) + ', minmax(116px,1fr))');
  return tracks.join(' ');
}
function ccSpreadsLabelFromExpiry(expiryTs, fallback){
  if(!Number.isFinite(Number(expiryTs)) || Number(expiryTs) <= 0) return String(fallback || '');
  return new Date(Number(expiryTs)).toLocaleString('en-US', { timeZone:'UTC', month:'short', day:'numeric', year:'numeric' });
}
function ccSpreadsIsSpotLeg(item){
  return String(item?.market || item?.type || '').toLowerCase() === 'spot' || String(item?.symbol || '').toUpperCase().endsWith('USDT');
}
function ccSpreadsSpotSymbolForLeg(item){
  return ccSpreadsIsSpotLeg(item) ? String(item?.symbol || '').toUpperCase() : '';
}
function ccSpreadsSpotIndexGateAllows(row, col, dirtySpotIndexSymbols){
  const spotSymbol = ccSpreadsSpotSymbolForLeg(row) || ccSpreadsSpotSymbolForLeg(col);
  if(!spotSymbol) return true;
  return dirtySpotIndexSymbols instanceof Set && dirtySpotIndexSymbols.has(spotSymbol);
}
function ccSpreadsSpotLeg(asset){
  const base = asset === 'ETH' ? 'ETH' : 'BTC';
  const cache = CC_SPREADS_SPOT_PRICE_CACHE[base] || {};
  const mark = Number(cache.price);
  return {
    market:'spot',
    type:'spot',
    symbol:base + 'USDT',
    displayName:base + '/USDT',
    rawLabel:'SPOT',
    label:'SPOT',
    expiryTs:-1,
    markPrice:Number.isFinite(mark) && mark > 0 ? mark : NaN,
    premium:''
  };
}
function ccSpreadsKlineTime(row){
  const value = row?.t ?? row?.time ?? row?.timestamp ?? row?.ts ?? row?.minuteUtc ?? row?.minute_utc ?? row?.openTime ?? row?.startTime;
  if(value === undefined || value === null) return NaN;
  if(typeof value === 'number') return value > 100000000000 ? Math.floor(value / 1000) : Math.floor(value);
  const ms = Date.parse(String(value).endsWith('Z') ? String(value) : String(value) + 'Z');
  return Number.isFinite(ms) ? Math.floor(ms / 1000) : NaN;
}
function ccSpreadsKlineClose(row){
  if(Array.isArray(row)){
    const value = row.length > 4 ? row[4] : row[row.length - 1];
    const n = Number(value);
    return Number.isFinite(n) && n > 0 ? n : NaN;
  }
  const n = Number(row?.close ?? row?.c ?? row?.price ?? row?.lastPrice);
  return Number.isFinite(n) && n > 0 ? n : NaN;
}
function ccSpreadsKlineBid(row){
  const n = Number(row?.bestBidClose ?? row?.best_bid_close ?? row?.bid ?? row?.bidClose);
  return Number.isFinite(n) && n > 0 ? n : NaN;
}
function ccSpreadsKlineAsk(row){
  const n = Number(row?.bestAskClose ?? row?.best_ask_close ?? row?.ask ?? row?.askClose);
  return Number.isFinite(n) && n > 0 ? n : NaN;
}
function ccSpreadsSpotIndexPrice(row){
  const n = Number(row?.indexPrice ?? row?.index_price ?? row?.indexPriceClose ?? row?.index_price_close ?? row?.index ?? row?.idx ?? row?.i);
  return Number.isFinite(n) && n > 0 ? n : NaN;
}
function ccSpreadsNormalizeSpotKlines(rows){
  return (Array.isArray(rows) ? rows : []).map(row => {
    const t = Array.isArray(row) ? ccChartNormalizeTs(row[0]) : ccChartNormalizeTs(ccSpreadsKlineTime(row));
    const c = ccSpreadsKlineClose(row);
    const bid = ccSpreadsKlineBid(row);
    const ask = ccSpreadsKlineAsk(row);
    const index = Array.isArray(row) ? NaN : ccSpreadsSpotIndexPrice(row);
    return { t:ccChartNormalizeTs(t), c, mark:index, index, bid, ask };
  }).filter(row => Number.isFinite(row.t) && (Number.isFinite(row.c) || Number.isFinite(row.index) || Number.isFinite(row.bid) || Number.isFinite(row.ask))).sort((a,b) => a.t - b.t);
}
async function ccSpreadsLoadSpotPrice(asset, force=false){
  const base = asset === 'ETH' ? 'ETH' : 'BTC';
  const cache = CC_SPREADS_SPOT_PRICE_CACHE[base] || (CC_SPREADS_SPOT_PRICE_CACHE[base] = { price:NaN, ts:0, rows:[] });
  if(!force && Number.isFinite(Number(cache.price)) && Date.now() - Number(cache.ts || 0) < 60000) return cache.price;
  try{
    const res = await ccApi('/api/admin/coincall/spot/candles/minute?symbol=' + encodeURIComponent(base + 'USDT') + '&limit=720', { cache:'no-store' });
    const rawRows = Array.isArray(res?.rows) ? res.rows : (Array.isArray(res?.data) ? res.data : []);
    const rows = ccSpreadsNormalizeSpotKlines(rawRows);
    const last = rows[rows.length - 1];
    cache.rows = rows;
    const index = last ? Number(last.index) : NaN;
    if(Number.isFinite(index) && index > 0) cache.price = index;
    cache.ts = Date.now();
    return cache.price;
  }catch(e){
    cache.ts = Date.now();
    return NaN;
  }
}
async function ccSpreadsLoadSpotPrices(force=false){
  await Promise.allSettled(['BTC','ETH'].map(asset => ccSpreadsLoadSpotPrice(asset, force)));
}
function ccSpreadsDaysToExpiryValue(expiryTs){
  if(!Number.isFinite(Number(expiryTs)) || Number(expiryTs) <= 0) return null;
  const now = new Date();
  const todayUtc = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  return Math.round((Number(expiryTs) - todayUtc) / 86400000);
}
function ccSpreadsDaysToExpiry(expiryTs){
  const days = ccSpreadsDaysToExpiryValue(expiryTs);
  return days !== null && days >= 0 ? days + 'd' : '';
}
function ccSpreadsLastFridayUtc(year, month){
  const d = new Date(Date.UTC(year, month + 1, 0));
  d.setUTCDate(d.getUTCDate() - ((d.getUTCDay() + 2) % 7));
  return Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
}
function ccSpreadsIsMonthlyOrQuarterlyExpiryFuture(item){
  const ts = Number(item?.expiryTs);
  if(!Number.isFinite(ts) || ts <= 0) return false;
  const d = new Date(ts);
  if(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()) !== ts) return false;
  return ts === ccSpreadsLastFridayUtc(d.getUTCFullYear(), d.getUTCMonth());
}
function ccSpreadsIsMonthlyExpiryFuture(item){
  if(!ccSpreadsIsMonthlyOrQuarterlyExpiryFuture(item)) return false;
  const month = new Date(Number(item?.expiryTs)).getUTCMonth();
  return ![2,5,8,11].includes(month);
}
function ccSpreadsIsQuarterlyExpiryFuture(item){
  if(!ccSpreadsIsMonthlyOrQuarterlyExpiryFuture(item)) return false;
  const month = new Date(Number(item?.expiryTs)).getUTCMonth();
  return [2,5,8,11].includes(month);
}
function ccSpreadsExpiryUtcDay(item){
  const ts = Number(item?.expiryTs);
  if(!Number.isFinite(ts) || ts <= 0) return null;
  const d = new Date(ts);
  if(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()) !== ts) return null;
  return d.getUTCDay();
}
function ccSpreadsIsDailyExpiryFuture(item){
  const day = ccSpreadsExpiryUtcDay(item);
  return day !== null && day !== 5;
}
function ccSpreadsIsWeeklyExpiryFuture(item){
  const day = ccSpreadsExpiryUtcDay(item);
  return day === 5 && !ccSpreadsIsMonthlyOrQuarterlyExpiryFuture(item);
}
function ccSpreadsExpiryFilter(item){
  const monthly = ccSpreadsIsMonthlyExpiryFuture(item);
  if(monthly) return ccSpreadsMonthlyExpEnabled();
  if(ccSpreadsIsQuarterlyExpiryFuture(item)) return ccSpreadsQuarterlyExpEnabled();
  if(ccSpreadsIsWeeklyExpiryFuture(item)) return ccSpreadsWeeklyExpEnabled();
  if(ccSpreadsIsDailyExpiryFuture(item)) return ccSpreadsDailyExpEnabled();
  return false;
}
function ccSpreadsMid(row){
  const bid = Number(row?.bid ?? row?.bidPx);
  const ask = Number(row?.ask ?? row?.askPx);
  return Number.isFinite(bid) && Number.isFinite(ask) && bid > 0 && ask > 0 ? (bid + ask) / 2 : NaN;
}
function ccSpreadsPremiumText(row, perpRow){
  const days = ccSpreadsDaysToExpiryValue(row?.expiryTs);
  const future = ccSpreadsMarkPrice(row);
  const perp = ccSpreadsMarkPrice(perpRow);
  if(days === null || days <= 0 || !Number.isFinite(future) || !Number.isFinite(perp) || !perp) return '';
  const monthlyPct = ((future - perp) / perp) * (30 / days) * 100;
  if(!Number.isFinite(monthlyPct)) return '';
  return (monthlyPct >= 0 ? '+' : '') + monthlyPct.toFixed(2) + '%';
}
function ccSpreadsMarkPrice(row){
  if(ccSpreadsIsSpotLeg(row)){
    const asset = String(row?.symbol || '').toUpperCase().startsWith('ETH') ? 'ETH' : 'BTC';
    const cached = Number(CC_SPREADS_SPOT_PRICE_CACHE[asset]?.price);
    const price = Number.isFinite(cached) && cached > 0 ? cached : Number(row?.markPrice);
    return Number.isFinite(price) && price > 0 ? price : NaN;
  }
  const value = row?.markPrice ?? row?.mark_price ?? row?.markPx ?? row?.mark_price_close ?? row?.markPriceClose;
  const mark = Number(value);
  return Number.isFinite(mark) && mark > 0 ? mark : NaN;
}
function ccSpreadsNormalizePriceSource(source){
  return ['mark','bidask'].includes(String(source || '').trim()) ? String(source).trim() : 'mark';
}
function ccSpreadsLoadPriceSource(){
  try { ccSpreadsPriceSource = ccSpreadsNormalizePriceSource(localStorage.getItem(CC_SPREADS_PRICE_SOURCE_KEY)); } catch { ccSpreadsPriceSource = 'mark'; }
  return ccSpreadsPriceSource;
}
function ccSpreadsBidAskFilterKey(row){
  const symbol = String(row?.symbol || row?.displaySymbol || row?.instrument || '').trim().toUpperCase();
  if(symbol) return 'spreads-matrix:' + symbol;
  if(ccSpreadsIsSpotLeg(row)){
    const asset = String(row?.symbol || '').toUpperCase().startsWith('ETH') ? 'ETH' : 'BTC';
    return 'spreads-matrix:' + asset + 'USDT';
  }
  return 'spreads-matrix:unknown';
}
function ccSpreadsRawBookPrice(row, side){
  const key = side === 'ask' ? 'ask' : 'bid';
  let price = Number(row?.[key] ?? row?.[key + 'Px']);
  if(ccSpreadsIsSpotLeg(row)){
    const asset = String(row?.symbol || '').toUpperCase().startsWith('ETH') ? 'ETH' : 'BTC';
    const cache = CC_SPREADS_SPOT_PRICE_CACHE[asset] || {};
    const cached = Number(cache[key]);
    if(Number.isFinite(cached) && cached > 0) price = cached;
  }
  return Number.isFinite(price) && price > 0 ? price : NaN;
}
const CC_SPREADS_BIDASK_FILTER_DEDUP_MS = 250;
const CC_SPREADS_BIDASK_FILTER_CALL_CACHE = {};
function ccSpreadsFilteredBookPrice(row, side){
  const raw = ccSpreadsRawBookPrice(row, side);
  const key = ccSpreadsBidAskFilterKey(row);
  const cacheKey = key + '::' + String(side || '');
  const cache = CC_SPREADS_BIDASK_FILTER_CALL_CACHE[cacheKey];
  const now = Date.now();
  if(cache && cache.raw === raw && now - cache.ts < CC_SPREADS_BIDASK_FILTER_DEDUP_MS) return cache.filtered;
  const filtered = ccBidAskFilteredValue(key, side, raw);
  CC_SPREADS_BIDASK_FILTER_CALL_CACHE[cacheKey] = { raw, filtered, ts:now };
  return filtered;
}
function ccSpreadsCellPrice(row){
  const source = ccSpreadsNormalizePriceSource(ccSpreadsPriceSource);
  return source === 'bidask' ? ccSpreadsRowBidAskMedian(row) : ccSpreadsMarkPrice(row);
}
function ccSpreadsPriceSourceLabel(){
  return ccSpreadsNormalizePriceSource(ccSpreadsPriceSource) === 'bidask' ? 'BidAskMedian' : 'MarkPrice';
}
function ccSpreadsMarkPriceText(row){
  const price = ccSpreadsMarkPrice(row);
  return Number.isFinite(price) && price > 0 ? ccFmt(price, ccPriceDecimals(price)) : '—';
}
function ccSpreadsRowBidAskMedian(row){
  const bid = ccSpreadsFilteredBookPrice(row, 'bid');
  const ask = ccSpreadsFilteredBookPrice(row, 'ask');
  return Number.isFinite(bid) && bid > 0 && Number.isFinite(ask) && ask > 0 ? (bid + ask) / 2 : NaN;
}
function ccSpreadsRowBidAskMedianText(row){
  const price = ccSpreadsRowBidAskMedian(row);
  return Number.isFinite(price) && price > 0 ? ccFmt(price, ccPriceDecimals(price)) : '—';
}
function ccSpreadsRowBidAskMarkDiffPct(row){
  const mark = ccSpreadsMarkPrice(row);
  const mid = ccSpreadsRowBidAskMedian(row);
  return Number.isFinite(mark) && mark > 0 && Number.isFinite(mid) && mid > 0 ? ((mid - mark) / mark) * 100 : NaN;
}
function ccSpreadsRowBidAskMarkDiffText(row){
  const pct = ccSpreadsRowBidAskMarkDiffPct(row);
  if(!Number.isFinite(pct)) return '—';
  const abs = Math.abs(pct);
  const decimals = abs >= 10 ? 1 : 2;
  return (pct > 0 ? '+' : '') + pct.toFixed(decimals) + '%';
}
function ccSpreadsRowBidAskMarkDiffClass(row){
  const pct = ccSpreadsRowBidAskMarkDiffPct(row);
  if(!Number.isFinite(pct)) return 'cc-spreads-diff-flat';
  if(Math.abs(pct) < 0.005) return 'cc-spreads-diff-flat';
  return pct > 0 ? 'cc-spreads-diff-pos' : 'cc-spreads-diff-neg';
}
function ccSpreadsRowPriceText(row){
  const price = ccSpreadsCellPrice(row);
  return Number.isFinite(price) && price > 0 ? ccFmt(price, ccPriceDecimals(price)) : '—';
}
function ccSpreadsRowBookPrice(row, side){
  return ccSpreadsFilteredBookPrice(row, side);
}
function ccSpreadsRowBookPriceText(row, side){
  const price = ccSpreadsRowBookPrice(row, side);
  return Number.isFinite(price) && price > 0 ? ccFmt(price, ccPriceDecimals(price)) : '—';
}
function ccSpreadsPatchRenderedRowPrices(){
  const host = cc('coincallSpreadsGridHost');
  if(!host) return;
  const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
  host.querySelectorAll('[data-cc-spreads-row-mark-price-index]').forEach(cell => {
    const row = rows[Number(cell.dataset.ccSpreadsRowPriceIndex)];
    if(!row) return;
    const text = ccSpreadsMarkPriceText(row);
    if(cell.textContent !== text) cell.textContent = text;
  });
  host.querySelectorAll('[data-cc-spreads-row-bidask-price-index]').forEach(cell => {
    const row = rows[Number(cell.dataset.ccSpreadsRowPriceIndex)];
    if(!row) return;
    const text = ccSpreadsRowBidAskMedianText(row);
    if(cell.textContent !== text) cell.textContent = text;
  });
  host.querySelectorAll('[data-cc-spreads-row-bidask-diff-index]').forEach(cell => {
    const row = rows[Number(cell.dataset.ccSpreadsRowPriceIndex)];
    if(!row) return;
    const text = ccSpreadsRowBidAskMarkDiffText(row);
    if(cell.textContent !== text) cell.textContent = text;
    cell.classList.toggle('cc-spreads-diff-pos', ccSpreadsRowBidAskMarkDiffClass(row) === 'cc-spreads-diff-pos');
    cell.classList.toggle('cc-spreads-diff-neg', ccSpreadsRowBidAskMarkDiffClass(row) === 'cc-spreads-diff-neg');
    cell.classList.toggle('cc-spreads-diff-flat', ccSpreadsRowBidAskMarkDiffClass(row) === 'cc-spreads-diff-flat');
  });
  host.querySelectorAll('[data-cc-spreads-row-book-price-index]').forEach(cell => {
    const row = rows[Number(cell.dataset.ccSpreadsRowBookPriceIndex)];
    if(!row) return;
    const text = ccSpreadsRowBookPriceText(row, cell.dataset.ccSpreadsRowBookPriceSide);
    if(cell.textContent !== text) cell.textContent = text;
  });
}
function ccSpreadsMatrixSymbols(){
  const out = new Set();
  ['BTC','ETH'].forEach(asset => {
    const state = ccSpreadsAssetState(asset);
    if(state?.perp?.symbol) out.add(String(state.perp.symbol).toUpperCase());
    (Array.isArray(state?.monthlyColumns) ? state.monthlyColumns : []).forEach(item => {
      const symbol = String(item?.symbol || '').toUpperCase();
      if(symbol) out.add(symbol);
    });
    (Array.isArray(state?.columns) ? state.columns : []).forEach(item => {
      const symbol = String(item?.symbol || '').toUpperCase();
      if(symbol) out.add(symbol);
    });
  });
  return out;
}
function ccSpreadsMatrixSpotSymbols(){
  const out = new Set();
  ['BTC','ETH'].forEach(asset => {
    const state = ccSpreadsAssetState(asset);
    const loaded = !!(state?.spot || state?.perp || (Array.isArray(state?.columns) && state.columns.length));
    if(loaded || asset === ccSpreadsHeaderAsset) out.add(asset + 'USDT');
  });
  return out;
}
function ccSpreadsMatrixItemsForSymbol(symbol){
  const key = String(symbol || '').toUpperCase();
  const out = [];
  if(!key) return out;
  const seen = new Set();
  const push = item => {
    if(!item) return;
    const itemKey = String(item?.symbol || '').toUpperCase();
    if(itemKey !== key || seen.has(item)) return;
    seen.add(item);
    out.push(item);
  };
  ['BTC','ETH'].forEach(asset => {
    const state = ccSpreadsAssetState(asset);
    push(state?.perp);
    (Array.isArray(state?.monthlyColumns) ? state.monthlyColumns : []).forEach(push);
    (Array.isArray(state?.columns) ? state.columns : []).forEach(push);
  });
  return out;
}
function ccSpreadsMatrixSpotAsset(symbol){
  return String(symbol || '').toUpperCase().startsWith('ETH') ? 'ETH' : 'BTC';
}
function ccSpreadsApplyMatrixSpotQuote(symbol, patch){
  symbol = String(symbol || '').trim().toUpperCase();
  if(!symbol) return false;
  const asset = ccSpreadsMatrixSpotAsset(symbol);
  const cache = CC_SPREADS_SPOT_PRICE_CACHE[asset] || (CC_SPREADS_SPOT_PRICE_CACHE[asset] = { price:NaN, ts:0, rows:[] });
  let changed = false;
  const indexPrice = Number(patch?.indexPrice ?? patch?.index_price ?? patch?.index ?? patch?.idx ?? patch?.i ?? patch?.markPrice);
  if(Number.isFinite(indexPrice) && indexPrice > 0 && Number(cache.price) !== indexPrice){
    cache.price = indexPrice;
    cache.indexPrice = indexPrice;
    changed = true;
  }
  const bid = Number(patch?.bid);
  if(Number.isFinite(bid) && bid > 0 && Number(cache.bid) !== bid){
    cache.bid = bid;
    changed = true;
  }
  const ask = Number(patch?.ask);
  if(Number.isFinite(ask) && ask > 0 && Number(cache.ask) !== ask){
    cache.ask = ask;
    changed = true;
  }
  if(!changed) return false;
  cache.ts = Date.now();
  const rows = Array.isArray(cache.rows) ? cache.rows : [];
  const last = rows[rows.length - 1];
  if(last){
    if(Number.isFinite(indexPrice) && indexPrice > 0){ last.mark = indexPrice; last.index = indexPrice; }
    if(Number.isFinite(bid) && bid > 0) last.bid = bid;
    if(Number.isFinite(ask) && ask > 0) last.ask = ask;
  }
  const state = CC_SPREADS_HEADER_DATA[asset] || (CC_SPREADS_HEADER_DATA[asset] = { columns:[], perp:null, spot:null });
  const spot = state.spot || (state.spot = ccSpreadsSpotLeg(asset));
  if(Number.isFinite(indexPrice) && indexPrice > 0){
    spot.markPrice = indexPrice;
    spot.indexPrice = indexPrice;
    spot.index_price = indexPrice;
  }
  if(Number.isFinite(bid) && bid > 0){
    spot.bid = bid;
    spot.bidPx = bid;
  }
  if(Number.isFinite(ask) && ask > 0){
    spot.ask = ask;
    spot.askPx = ask;
  }
  ccSpreadsScheduleMatrixRealtimePatch(symbol, { spotIndex:Number.isFinite(indexPrice) && indexPrice > 0 });
  return true;
}
function ccSpreadsApplyMatrixQuote(symbol, patch){
  symbol = String(symbol || '').trim().toUpperCase();
  if(symbol){
    const cached = Object.assign({}, CC_SPREADS_MATRIX_QUOTE_CACHE.get(symbol) || {});
    const mark = Number(patch?.markPrice);
    if(Number.isFinite(mark) && mark > 0){ cached.markPrice = mark; cached.mark_price = mark; }
    const indexPrice = Number(patch?.indexPrice);
    if(Number.isFinite(indexPrice) && indexPrice > 0){ cached.indexPrice = indexPrice; cached.index_price = indexPrice; }
    const bid = Number(patch?.bid);
    if(Number.isFinite(bid) && bid > 0){ cached.bid = bid; cached.bidPx = bid; }
    const ask = Number(patch?.ask);
    if(Number.isFinite(ask) && ask > 0){ cached.ask = ask; cached.askPx = ask; }
    cached.ts = Date.now();
    CC_SPREADS_MATRIX_QUOTE_CACHE.set(symbol, cached);
  }
  const items = ccSpreadsMatrixItemsForSymbol(symbol);
  if(!items.length) return false;
  let changed = false;
  items.forEach(item => {
    const mark = Number(patch?.markPrice);
    if(Number.isFinite(mark) && mark > 0 && Number(item.markPrice) !== mark){
      item.markPrice = mark;
      item.mark_price = mark;
      changed = true;
    }
    const indexPrice = Number(patch?.indexPrice);
    if(Number.isFinite(indexPrice) && indexPrice > 0 && Number(item.indexPrice) !== indexPrice){
      item.indexPrice = indexPrice;
      item.index_price = indexPrice;
      changed = true;
    }
    const cached = symbol ? CC_SPREADS_MATRIX_QUOTE_CACHE.get(symbol) : null;
    const bid = Number(cached?.bid ?? patch?.bid);
    if(Number.isFinite(bid) && bid > 0 && Number(item.bid) !== bid){
      item.bid = bid;
      item.bidPx = bid;
      changed = true;
    }
    const ask = Number(cached?.ask ?? patch?.ask);
    if(Number.isFinite(ask) && ask > 0 && Number(item.ask) !== ask){
      item.ask = ask;
      item.askPx = ask;
      changed = true;
    }
  });
  return changed;
}
function ccSpreadsApplyCachedMatrixQuote(item){
  const symbol = String(item?.symbol || '').trim().toUpperCase();
  const cached = symbol ? CC_SPREADS_MATRIX_QUOTE_CACHE.get(symbol) : null;
  if(!item || !cached) return item;
  ['markPrice','mark_price','indexPrice','index_price','bid','bidPx','ask','askPx'].forEach(key => {
    if(cached[key] !== undefined) item[key] = cached[key];
  });
  return item;
}
function ccSpreadsPatchRenderedCell(cell, col, row){
  if(!cell || !col || !row) return;
  const spread = ccSpreadsSpreadValue(col, row);
  if(!Number.isFinite(spread)) return;
  const side = cell.querySelector('.okx-nitro-side');
  const nextClass = ccSpreadsSpreadClass(col, row);
  const nextHtml = ccSpreadsCellDisplayHtml(col, row) + '<div class="okx-nitro-size"></div>';
  if(side){
    side.classList.toggle('ask', nextClass === 'ask');
    side.classList.toggle('bid', nextClass === 'bid');
    if(side.innerHTML !== nextHtml) side.innerHTML = nextHtml;
  }
}
function ccSpreadsPatchRenderedMatrixSymbols(symbols, dirtySpotIndexSymbols){
  const host = cc('coincallSpreadsGridHost');
  if(!host) return;
  const dirty = new Set(Array.from(symbols || []).map(s => String(s || '').toUpperCase()).filter(Boolean));
  const spotIndexDirty = dirtySpotIndexSymbols instanceof Set ? dirtySpotIndexSymbols : new Set();
  const columns = ccSpreadsColumnsForAsset(ccSpreadsHeaderAsset);
  const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
  ccSpreadsPatchRenderedRowPrices();
  host.querySelectorAll('[data-cc-spreads-col-index]').forEach(head => {
    const col = columns[Number(head.dataset.ccSpreadsColIndex)];
    if(!col || !dirty.has(String(col.symbol || '').toUpperCase())) return;
    const meta = head.querySelector('.okx-nitro-col-lev');
    if(meta){
      const html = ccSpreadsMetaHtml(col);
      if(meta.innerHTML !== html) meta.innerHTML = html;
    }
  });
  host.querySelectorAll('[data-cc-spreads-clickable="1"]').forEach(cell => {
    const row = rows[Number(cell.dataset.ccSpreadsRowIndex)];
    const col = columns[Number(cell.dataset.ccSpreadsColIndex)];
    const rowSymbol = String(row?.symbol || '').toUpperCase();
    const colSymbol = String(col?.symbol || '').toUpperCase();
    if(!dirty.has(rowSymbol) && !dirty.has(colSymbol)) return;
    if(!ccSpreadsSpotIndexGateAllows(row, col, spotIndexDirty)) return;
    ccSpreadsPatchRenderedCell(cell, col, row);
  });
}
function ccSpreadsScheduleMatrixRealtimePatch(symbol, options){
  const key = String(symbol || '').toUpperCase();
  if(key) CC_SPREADS_MATRIX_DIRTY_SYMBOLS.add(key);
  if(key && options && options.spotIndex) CC_SPREADS_MATRIX_DIRTY_SPOT_INDEX_SYMBOLS.add(key);
  if(ccSpreadsMatrixPatchRaf) return;
  const flush = () => {
    ccSpreadsMatrixPatchRaf = 0;
    const symbols = Array.from(CC_SPREADS_MATRIX_DIRTY_SYMBOLS);
    const spotIndexSymbols = new Set(Array.from(CC_SPREADS_MATRIX_DIRTY_SPOT_INDEX_SYMBOLS));
    CC_SPREADS_MATRIX_DIRTY_SYMBOLS.clear();
    CC_SPREADS_MATRIX_DIRTY_SPOT_INDEX_SYMBOLS.clear();
    ccSpreadsPatchRenderedMatrixSymbols(symbols, spotIndexSymbols);
    ccSpreadsSetLastUpdate(new Date());
    ccSpreadsSyncSelectedMarkSpreadCharts(symbols, 'matrix-live', spotIndexSymbols);
  };
  if(typeof requestAnimationFrame === 'function') ccSpreadsMatrixPatchRaf = requestAnimationFrame(flush);
  else ccSpreadsMatrixPatchRaf = setTimeout(flush, 0);
}
function ccSpreadsDirtySymbolsMatchSelection(symbols, rowSymbol, colSymbol, dirtySpotIndexSymbols){
  const dirty = new Set(Array.from(symbols || []).map(s => String(s || '').toUpperCase()).filter(Boolean));
  if(!dirty.size) return true;
  rowSymbol = String(rowSymbol || '').toUpperCase();
  colSymbol = String(colSymbol || '').toUpperCase();
  const rowSpot = rowSymbol.endsWith('USDT');
  const colSpot = colSymbol.endsWith('USDT');
  if(rowSpot || colSpot){
    const spotSymbol = rowSpot ? rowSymbol : colSymbol;
    return dirtySpotIndexSymbols instanceof Set && dirtySpotIndexSymbols.has(spotSymbol);
  }
  return dirty.has(rowSymbol) || dirty.has(colSymbol);
}
function ccSpreadsUpdateMainSelectedMarkSpreadChart(symbols, reason, dirtySpotIndexSymbols){
  const section = cc('ccSpreadsMainChartSection');
  if(!section || section.hidden) return false;
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row') return ccSpreadsMainUpdateRowModeLive(symbols, dirtySpotIndexSymbols);
  const rowSymbol = String(CC_SPREADS_MAIN_CHART_STATE.row?.symbol || '').toUpperCase();
  const colSymbol = String(CC_SPREADS_MAIN_CHART_STATE.col?.symbol || '').toUpperCase();
  if(!rowSymbol || !colSymbol || !ccSpreadsDirtySymbolsMatchSelection(symbols, rowSymbol, colSymbol, dirtySpotIndexSymbols)) return false;
  const cell = ccSpreadsFindCellBySymbols(ccSpreadsHeaderAsset, rowSymbol, colSymbol);
  if(!cell) return false;
  CC_SPREADS_MAIN_CHART_STATE.row = cell.row;
  CC_SPREADS_MAIN_CHART_STATE.col = cell.col;
  const liveRow = ccSpreadsMainLiveRow(cell.row, cell.col);
  if(!liveRow) return false;
  const currentRows = Array.isArray(CC_SPREADS_MAIN_CHART_STATE.mainRows) ? CC_SPREADS_MAIN_CHART_STATE.mainRows : [];
  const rows = ccSpreadsMainMergeLiveRow(currentRows, cell.row, cell.col);
  if(!rows.length) return false;
  ccSpreadsMainChartUpdateLatestData(rows);
  return true;
}
function ccSpreadsSyncSelectedMarkSpreadCharts(symbols, reason, dirtySpotIndexSymbols){
  const mainUpdated = ccSpreadsUpdateMainSelectedMarkSpreadChart(symbols, reason, dirtySpotIndexSymbols);
  const popupUpdated = ccSpreadsMaybeUpdateSelectedMarkSpreadChart(symbols, reason, dirtySpotIndexSymbols);
  return mainUpdated || popupUpdated;
}
function ccSpreadsSelectedChartLiveTick(){
  if(!ccSpreadsIsActive()) return false;
  const section = cc('ccSpreadsMainChartSection');
  const hasMain = !!(section && !section.hidden && CC_SPREADS_MAIN_CHART_STATE.mainSeries);
  const hasPopup = ccSpreadsTradeModalOpen() && !!ccSpreadsTradeModalState;
  if(!hasMain && !hasPopup) return false;
  const spotSymbols = typeof ccSpreadsMatrixSpotSymbols === 'function' ? ccSpreadsMatrixSpotSymbols() : new Set();
  return ccSpreadsSyncSelectedMarkSpreadCharts([], 'selected-live-tick', spotSymbols);
}
function ccSpreadsStartMainLiveTick(){
  if(ccSpreadsMainLiveTickTimer) return;
  ccSpreadsMainLiveTickTimer = window.setInterval(() => {
    try{ ccSpreadsSelectedChartLiveTick(); }catch(e){}
  }, CC_SPREADS_MAIN_LIVE_TICK_MS);
}
function ccSpreadsStopMainLiveTick(){
  if(ccSpreadsMainLiveTickTimer){
    clearInterval(ccSpreadsMainLiveTickTimer);
    ccSpreadsMainLiveTickTimer = 0;
  }
}
function ccSpreadsMaybeUpdateSelectedMarkSpreadChart(symbols, reason, dirtySpotIndexSymbols){
  if(!ccSpreadsTradeModalOpen() || !ccSpreadsTradeModalState) return false;
  const rowSymbol = String(ccSpreadsTradeModalState.rowSymbol || '').toUpperCase();
  const colSymbol = String(ccSpreadsTradeModalState.colSymbol || '').toUpperCase();
  if(!ccSpreadsDirtySymbolsMatchSelection(symbols, rowSymbol, colSymbol, dirtySpotIndexSymbols)) return false;
  const cell = ccSpreadsFindCellBySymbols(ccSpreadsTradeModalState.asset, rowSymbol, colSymbol);
  if(!cell) return false;
  ccSpreadsScheduleLiveSpreadChartUpdate(reason || 'matrix-live');
  return true;
}
function ccSpreadsSpreadValue(col, row){
  const colPrice = ccSpreadsCellPrice(col);
  const rowPrice = ccSpreadsCellPrice(row);
  if(!Number.isFinite(colPrice) || !Number.isFinite(rowPrice)) return NaN;
  return colPrice - rowPrice;
}
function ccSpreadsSpreadText(col, row){
  const spread = ccSpreadsSpreadValue(col, row);
  if(!Number.isFinite(spread)) return '—';
  return ccFmtSignedNumberOrDash(spread, 2);
}
function ccSpreadsSpreadClass(col, row){
  const spread = ccSpreadsSpreadValue(col, row);
  return Number.isFinite(spread) ? (spread < 0 ? 'ask' : 'bid') : '';
}
function ccSpreadsNormalizeDisplayMode(mode){
  return ['usd','both','percent'].includes(String(mode || '').trim()) ? String(mode).trim() : 'usd';
}
function ccSpreadsLoadDisplayMode(){
  try { ccSpreadsDisplayMode = ccSpreadsNormalizeDisplayMode(localStorage.getItem(CC_SPREADS_DISPLAY_MODE_KEY)); } catch { ccSpreadsDisplayMode = 'usd'; }
  return ccSpreadsDisplayMode;
}
function ccSpreadsColumnPremiumText(col){
  if(col && typeof col.premium === 'string' && col.premium) return col.premium;
  const state = ccSpreadsAssetState(ccSpreadsHeaderAsset);
  return state?.perp ? ccSpreadsPremiumText(col, state.perp) : '';
}
function ccSpreadsCellPremiumText(col, row){
  const days = ccSpreadsDaysToExpiryValue(col?.expiryTs);
  const colPrice = ccSpreadsCellPrice(col);
  const rowPrice = ccSpreadsCellPrice(row);
  if(days === null || days <= 0 || !Number.isFinite(colPrice) || !Number.isFinite(rowPrice) || !rowPrice) return '';
  const monthlyPct = ((colPrice - rowPrice) / rowPrice) * (30 / days) * 100;
  if(!Number.isFinite(monthlyPct)) return '';
  return (monthlyPct >= 0 ? '+' : '') + monthlyPct.toFixed(2) + '%';
}
function ccSpreadsCellDisplayHtml(col, row){
  const usdText = ccSpreadsSpreadText(col, row);
  const pctText = ccSpreadsCellPremiumText(col, row) || '—';
  const mode = ccSpreadsNormalizeDisplayMode(ccSpreadsDisplayMode);
  if(mode === 'percent') return '<div class="okx-nitro-price cc-spreads-cell-percent">' + ccEsc(pctText) + '</div>';
  if(mode === 'both') return '<div class="okx-nitro-price cc-spreads-cell-both"><span class="cc-spreads-cell-usd">' + ccEsc(usdText) + '</span><span class="cc-spreads-cell-percent">' + ccEsc(pctText) + '</span></div>';
  return '<div class="okx-nitro-price cc-spreads-cell-both"><span class="cc-spreads-cell-usd">' + ccEsc(usdText) + '</span><span class="cc-spreads-cell-percent">' + ccEsc(pctText) + '</span></div>';
}
function ccSpreadsSyncDisplaySwitch(){
  ccSpreadsLoadDisplayMode();
  document.querySelectorAll('[data-cc-spreads-display-mode]').forEach(btn => {
    const active = btn.dataset.ccSpreadsDisplayMode === ccSpreadsDisplayMode;
    btn.classList.toggle('active', active);
    btn.setAttribute('aria-pressed', active ? 'true' : 'false');
  });
}
function ccSpreadsSyncPriceSourceSwitch(){
  ccSpreadsLoadPriceSource();
  document.querySelectorAll('[data-cc-spreads-price-source]').forEach(btn => {
    const active = btn.dataset.ccSpreadsPriceSource === ccSpreadsPriceSource;
    btn.classList.toggle('active', active);
    btn.setAttribute('aria-pressed', active ? 'true' : 'false');
  });
}
function ccSpreadsSetDisplayMode(mode){
  ccSpreadsDisplayMode = ccSpreadsNormalizeDisplayMode(mode);
  try { localStorage.setItem(CC_SPREADS_DISPLAY_MODE_KEY, ccSpreadsDisplayMode); } catch {}
  ccSpreadsSyncDisplaySwitch();
  ccSpreadsRenderGrid();
}
function ccSpreadsSetPriceSource(source){
  ccSpreadsPriceSource = ccSpreadsNormalizePriceSource(source);
  try { localStorage.setItem(CC_SPREADS_PRICE_SOURCE_KEY, ccSpreadsPriceSource); } catch {}
  ccSpreadsSyncPriceSourceSwitch();
  ccSpreadsRenderGrid();
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row') ccSpreadsMainLoadRowChart(CC_SPREADS_MAIN_CHART_STATE.rowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
}
function ccSpreadsCellUnavailable(rowIndex, colIndex, row, col){
  const rowSymbol = String(row?.symbol || '').toUpperCase();
  const colSymbol = String(col?.symbol || '').toUpperCase();
  if(rowSymbol && colSymbol && rowSymbol === colSymbol) return true;
  if(ccSpreadsIsSpotLeg(row)) return false;
  if(ccSpreadsIsSpotLeg(col)) return true;
  if(ccSpreadsIsPerpetualFuture(col)) return true;
  const rowExpiry = Number(row?.expiryTs || 0);
  const colExpiry = Number(col?.expiryTs || 0);
  return rowExpiry > 0 && colExpiry > 0 && colExpiry <= rowExpiry;
}
function ccSpreadsMetaHtml(item){
  const days = ccSpreadsDaysToExpiry(item?.expiryTs);
  const mark = ccSpreadsMarkPrice(item);
  const markText = Number.isFinite(mark) && mark > 0 ? ccFmt(mark, ccPriceDecimals(mark)) : '';
  const parts = [];
  if(days) parts.push('<span>' + ccEsc(days) + '</span>');
  if(markText) parts.push('<span class="mark-price">' + ccEsc(markText) + '</span>');
  return parts.join(' ');
}
function ccSpreadsSortRows(rows){
  return rows.slice().sort((a,b) => {
    const aTs = Number(a.expiryTs || 0);
    const bTs = Number(b.expiryTs || 0);
    if (aTs && bTs) return aTs - bTs;
    return String(a.symbol || '').localeCompare(String(b.symbol || ''));
  });
}
function ccSpreadsActiveAccountId(){
  return String(cc('ccAccountSelect')?.value || '').trim();
}
function ccSpreadsLeverageCacheKey(symbol, accountId){
  return String(accountId || '') + '::' + String(symbol || '').toUpperCase();
}
const CC_SPREADS_BOOK_STATE = { sockets:{row:null,col:null,spot:null}, symbols:{row:'',col:'',spot:''}, types:{row:'futures',col:'futures',spot:'spot'}, items:{row:null,col:null,spot:null}, labels:{row:'Futures',col:'Futures',spot:'Spot'}, raw:{row:null,col:null,spot:null}, heartbeat:{row:null,col:null,spot:null}, reconnect:{row:null,col:null,spot:null}, connectSeq:{row:0,col:0,spot:0}, updated:{row:0,col:0,spot:0}, status:{row:'',col:'',spot:''}, lastTrade:{row:{symbol:'',price:NaN,side:'',ts:0},col:{symbol:'',price:NaN,side:'',ts:0}}, mark:{row:{symbol:'',price:NaN,indexPrice:NaN,ts:0},col:{symbol:'',price:NaN,indexPrice:NaN,ts:0}}, spotCache:{channel:'',asks:new Map(),bids:new Map()}, spotLastTrade:{symbol:'',price:NaN,side:'',ts:0} };
const CC_SPREADS_POPUP_BOOK_DEPTH = 6;
function ccSpreadsMatrixRealtimeHandleMessage(msg){
  if(!msg || typeof msg !== 'object') return false;
  const dt = Number(msg.dt);
  const data = msg.d;
  if(!data || ![30,32,37,58].includes(dt)) return false;
  const symbol = String(data.s || data.symbol || '').trim().toUpperCase();
  if(!symbol || !CC_SPREADS_MATRIX_WS.symbols.has(symbol)) return false;
  if(dt === 30){
    const mark = Number(data.mp ?? data.markPrice ?? data.mark_price);
    const indexPrice = Number(data.ip ?? data.indexPrice ?? data.index_price);
    if(!Number.isFinite(mark) || mark <= 0) return false;
    if(ccSpreadsApplyMatrixQuote(symbol, { markPrice:mark, indexPrice })) ccSpreadsScheduleMatrixRealtimePatch(symbol);
    CC_SPREADS_MATRIX_WS.lastEventAt = Date.now();
    return true;
  }
  const patch = ccSpreadsMatrixFuturesApplyBook(symbol, data);
  if(!patch) return false;
  if(ccSpreadsApplyMatrixQuote(symbol, patch)) ccSpreadsScheduleMatrixRealtimePatch(symbol);
  CC_SPREADS_MATRIX_WS.lastEventAt = Date.now();
  return true;
}
function ccSpreadsMatrixFuturesBookState(symbol){
  const key = String(symbol || '').trim().toUpperCase();
  let state = CC_SPREADS_MATRIX_WS.books.get(key);
  if(!state){
    state = { asks:new Map(), bids:new Map() };
    CC_SPREADS_MATRIX_WS.books.set(key, state);
  }
  return state;
}
function ccSpreadsMatrixBookLevelValue(level, keys, arrIndex){
  if(Array.isArray(level)) return Number(level[arrIndex]);
  return Number(ccPick(level, keys));
}
function ccSpreadsMatrixApplyBookSide(book, levels){
  if(!Array.isArray(levels)) return false;
  let touched = false;
  levels.forEach(level => {
    const price = ccSpreadsMatrixBookLevelValue(level, ['price','p','px','pr'], 0);
    const size = ccSpreadsMatrixBookLevelValue(level, ['size','amount','qty','sz','quantity','q'], 1);
    if(!Number.isFinite(price) || price <= 0 || !Number.isFinite(size)) return;
    const key = String(price);
    if(size <= 0) book.delete(key); else book.set(key, size);
    touched = true;
  });
  return touched;
}
function ccSpreadsMatrixFuturesBookLevels(state, side){
  return Array.from(state[side].entries()).map(([p,q]) => [Number(p), q]).filter(([p,q]) => Number.isFinite(p) && p > 0 && Number.isFinite(q) && q >= 0).sort((a,b) => side === 'asks' ? a[0] - b[0] : b[0] - a[0]);
}
function ccSpreadsMatrixFuturesApplyBook(symbol, tick){
  if(!tick || typeof tick !== 'object') return null;
  const root = tick.data && typeof tick.data === 'object' ? tick.data : tick;
  const hasAsks = Array.isArray(root.asks) || Array.isArray(root.a);
  const hasBids = Array.isArray(root.bids) || Array.isArray(root.b);
  if(!hasAsks && !hasBids) return null;
  const top = ccFuturesTopOfBookFromRaw(root);
  const patch = {};
  if(Number.isFinite(top.ask) && top.ask > 0) patch.ask = top.ask;
  if(Number.isFinite(top.bid) && top.bid > 0) patch.bid = top.bid;
  return patch.ask === undefined && patch.bid === undefined ? null : patch;
}
function ccSpreadsMatrixSpotBookState(symbol, channel){
  const key = String(symbol || '').toUpperCase();
  let state = CC_SPREADS_MATRIX_SPOT_WS.books.get(key);
  if(!state || (channel && state.channel !== channel)){
    state = { channel:channel || '', asks:new Map(), bids:new Map() };
    CC_SPREADS_MATRIX_SPOT_WS.books.set(key, state);
  }
  return state;
}
function ccSpreadsMatrixSpotBookLevels(state, side){
  return Array.from(state[side].entries()).map(([p,q]) => [Number(p), q]).filter(([p,q]) => Number.isFinite(p) && p > 0 && Number.isFinite(q) && q >= 0).sort((a,b) => side === 'asks' ? a[0] - b[0] : b[0] - a[0]);
}
function ccSpreadsMatrixSpotApplyBook(symbol, channel, tick){
  if(!tick) return false;
  const state = ccSpreadsMatrixSpotBookState(symbol, channel);
  if(String(tick.type || '').toLowerCase() === 'snapshot'){
    state.asks.clear();
    state.bids.clear();
  }
  const apply = (side, levels) => {
    const book = state[side];
    (Array.isArray(levels) ? levels : []).forEach(level => {
      if(!Array.isArray(level) || level.length < 2) return;
      const price = String(level[0]);
      const size = Number(level[1]);
      if(!Number.isFinite(size)) return;
      if(size <= 0) book.delete(price); else book.set(price, size);
    });
  };
  apply('asks', tick.a || tick.asks);
  apply('bids', tick.b || tick.bids);
  const ask = ccSpreadsMatrixSpotBookLevels(state, 'asks')[0]?.[0];
  const bid = ccSpreadsMatrixSpotBookLevels(state, 'bids')[0]?.[0];
  if(!Number.isFinite(ask) || ask <= 0 || !Number.isFinite(bid) || bid <= 0) return false;
  return ccSpreadsApplyMatrixSpotQuote(symbol, { ask, bid });
}
function ccSpreadsMatrixSpotHandleMessage(msg){
  if(!msg || typeof msg !== 'object') return false;
  if(msg.ping !== undefined || msg.rc === 1) return false;
  const channel = String(msg.ch || '');
  const tick = msg.tick;
  if(!channel || tick === undefined) return false;
  const symbols = CC_SPREADS_MATRIX_SPOT_WS.symbols;
  for(const symbol of symbols){
    if(channel === ccSpotTradeDetailChannel(symbol) && Array.isArray(tick)){
      const first = tick.filter(t => String(t?.symbol || symbol).trim().toUpperCase() === symbol).sort((a,b) => Number(b?.ts || 0) - Number(a?.ts || 0))[0];
      const price = Number(first?.price);
      if(Number.isFinite(price) && price > 0){
        CC_SPREADS_MATRIX_SPOT_WS.lastEventAt = Date.now();
        return false;
      }
      return false;
    }
    if(channel === ccSpotChartKlineChannel(symbol) && tick){
      const close = Number(tick.close ?? tick.c);
      if(Number.isFinite(close) && close > 0){
        CC_SPREADS_MATRIX_SPOT_WS.lastEventAt = Date.now();
        return false;
      }
      return false;
    }
    if(channel === ccSpotOverviewChannel(symbol) && tick){
      const indexPrice = ccSpreadsSpotIndexPrice(tick);
      if(Number.isFinite(indexPrice) && indexPrice > 0){
        CC_SPREADS_MATRIX_SPOT_WS.lastEventAt = Date.now();
        const changed = ccSpreadsApplyMatrixSpotQuote(symbol, { indexPrice });
        if(changed) ccSpreadsScheduleMatrixRealtimePatch(symbol, { spotIndex:true });
        return changed;
      }
      return false;
    }
    if(channel === ccSpotBookChannel(symbol)){
      const changed = ccSpreadsMatrixSpotApplyBook(symbol, channel, tick);
      if(changed){
        CC_SPREADS_MATRIX_SPOT_WS.lastEventAt = Date.now();
        ccSpreadsScheduleMatrixRealtimePatch(symbol, { spotIndex:true });
      }
      return changed;
    }
  }
  return false;
}
function ccSpreadsMatrixRealtimeSubscribe(ws){
  if(!ws || ws.readyState !== WebSocket.OPEN) return;
  const symbols = ccSpreadsMatrixSymbols();
  CC_SPREADS_MATRIX_WS.symbols = symbols;
  symbols.forEach(symbol => {
    if(CC_SPREADS_MATRIX_WS.subscribed.has(symbol)) return;
    ccBookSendJson(ws, { c:20, dt:30, d:{ s:symbol } });
    ccBookSendJson(ws, { c:20, dt:58, d:{ s:symbol, step:CC_FUTURES_BOOK_STEP } });
    CC_SPREADS_MATRIX_WS.subscribed.add(symbol);
  });
}
function ccSpreadsMatrixSpotSubscribe(ws){
  if(!ws || ws.readyState !== WebSocket.OPEN) return;
  const symbols = ccSpreadsMatrixSpotSymbols();
  CC_SPREADS_MATRIX_SPOT_WS.symbols = symbols;
  symbols.forEach(symbol => {
    if(CC_SPREADS_MATRIX_SPOT_WS.subscribed.has(symbol)) return;
    ccBookSendJson(ws, { sub:ccSpotBookChannel(symbol), id:Date.now() });
    ccBookSendJson(ws, { sub:ccSpotOverviewChannel(symbol), id:Date.now()+1 });
    ccBookSendJson(ws, { sub:ccSpotChartKlineChannel(symbol), id:Date.now()+2 });
    ccBookSendJson(ws, { sub:ccSpotTradeDetailChannel(symbol), id:Date.now()+3 });
    CC_SPREADS_MATRIX_SPOT_WS.subscribed.add(symbol);
  });
}
function ccSpreadsMatrixSpotStop(){
  clearTimeout(CC_SPREADS_MATRIX_SPOT_WS.reconnect);
  clearInterval(CC_SPREADS_MATRIX_SPOT_WS.heartbeat);
  CC_SPREADS_MATRIX_SPOT_WS.reconnect = 0;
  CC_SPREADS_MATRIX_SPOT_WS.heartbeat = 0;
  CC_SPREADS_MATRIX_SPOT_WS.subscribed.clear();
  CC_SPREADS_MATRIX_SPOT_WS.books.clear();
  const ws = CC_SPREADS_MATRIX_SPOT_WS.socket;
  CC_SPREADS_MATRIX_SPOT_WS.socket = null;
  CC_SPREADS_MATRIX_SPOT_WS.status = 'stopped';
  if(ws && [WebSocket.OPEN, WebSocket.CONNECTING].includes(ws.readyState)){
    try{ ws.close(); }catch{}
  }
}
function ccSpreadsMatrixRealtimeStop(){
  clearTimeout(CC_SPREADS_MATRIX_WS.reconnect);
  clearInterval(CC_SPREADS_MATRIX_WS.heartbeat);
  CC_SPREADS_MATRIX_WS.reconnect = 0;
  CC_SPREADS_MATRIX_WS.heartbeat = 0;
  CC_SPREADS_MATRIX_WS.subscribed.clear();
  CC_SPREADS_MATRIX_WS.books.clear();
  const ws = CC_SPREADS_MATRIX_WS.socket;
  CC_SPREADS_MATRIX_WS.socket = null;
  CC_SPREADS_MATRIX_WS.status = 'stopped';
  if(ws && [WebSocket.OPEN, WebSocket.CONNECTING].includes(ws.readyState)){
    try{ ws.close(); }catch{}
  }
  ccSpreadsMatrixSpotStop();
}
function ccSpreadsMatrixRealtimeScheduleReconnect(){
  if(!ccSpreadsIsActive()) return;
  clearTimeout(CC_SPREADS_MATRIX_WS.reconnect);
  CC_SPREADS_MATRIX_WS.reconnect = setTimeout(() => ccSpreadsMatrixRealtimeEnsure().catch(() => {}), CC_BOOK_RECONNECT_MS);
}
function ccSpreadsMatrixSpotScheduleReconnect(){
  if(!ccSpreadsIsActive()) return;
  clearTimeout(CC_SPREADS_MATRIX_SPOT_WS.reconnect);
  CC_SPREADS_MATRIX_SPOT_WS.reconnect = setTimeout(() => ccSpreadsMatrixSpotEnsure(), CC_BOOK_RECONNECT_MS);
}
function ccSpreadsMatrixSpotEnsure(){
  if(!ccSpreadsIsActive()) return;
  const symbols = ccSpreadsMatrixSpotSymbols();
  if(!symbols.size) return;
  const current = CC_SPREADS_MATRIX_SPOT_WS.socket;
  if(current && current.readyState === WebSocket.OPEN){
    CC_SPREADS_MATRIX_SPOT_WS.symbols = symbols;
    ccSpreadsMatrixSpotSubscribe(current);
    return;
  }
  if(current && current.readyState === WebSocket.CONNECTING) return;
  clearTimeout(CC_SPREADS_MATRIX_SPOT_WS.reconnect);
  clearInterval(CC_SPREADS_MATRIX_SPOT_WS.heartbeat);
  CC_SPREADS_MATRIX_SPOT_WS.subscribed.clear();
  const seq = ++CC_SPREADS_MATRIX_SPOT_WS.seq;
  const ws = new WebSocket('wss://ws.coincall.com/spot/ws');
  CC_SPREADS_MATRIX_SPOT_WS.socket = ws;
  CC_SPREADS_MATRIX_SPOT_WS.symbols = symbols;
  CC_SPREADS_MATRIX_SPOT_WS.status = 'connecting';
  ws.onopen = () => {
    if(CC_SPREADS_MATRIX_SPOT_WS.socket !== ws || CC_SPREADS_MATRIX_SPOT_WS.seq !== seq) return;
    CC_SPREADS_MATRIX_SPOT_WS.status = 'live';
    ccSpreadsMatrixSpotSubscribe(ws);
    clearInterval(CC_SPREADS_MATRIX_SPOT_WS.heartbeat);
    CC_SPREADS_MATRIX_SPOT_WS.heartbeat = setInterval(() => ccBookSendJson(ws, { c:11 }), 15000);
  };
  ws.onmessage = ev => {
    if(CC_SPREADS_MATRIX_SPOT_WS.socket !== ws || CC_SPREADS_MATRIX_SPOT_WS.seq !== seq) return;
    let msg;
    try{ msg = JSON.parse(ev.data); }catch{return;}
    if(msg && msg.ping !== undefined){ ccBookSendJson(ws, { pong:msg.ping }); return; }
    ccSpreadsMatrixSpotHandleMessage(msg);
  };
  ws.onerror = () => {
    if(CC_SPREADS_MATRIX_SPOT_WS.socket !== ws) return;
    CC_SPREADS_MATRIX_SPOT_WS.status = 'socket error';
    CC_SPREADS_MATRIX_SPOT_WS.lastErrorAt = Date.now();
  };
  ws.onclose = () => {
    if(CC_SPREADS_MATRIX_SPOT_WS.socket === ws) CC_SPREADS_MATRIX_SPOT_WS.socket = null;
    clearInterval(CC_SPREADS_MATRIX_SPOT_WS.heartbeat);
    CC_SPREADS_MATRIX_SPOT_WS.heartbeat = 0;
    CC_SPREADS_MATRIX_SPOT_WS.subscribed.clear();
    if(ccSpreadsIsActive()){
      CC_SPREADS_MATRIX_SPOT_WS.status = 'reconnecting';
      ccSpreadsMatrixSpotScheduleReconnect();
    }
  };
}
async function ccSpreadsMatrixRealtimeEnsure(){
  if(!ccSpreadsIsActive()) return;
  ccSpreadsMatrixSpotEnsure();
  const symbols = ccSpreadsMatrixSymbols();
  if(!symbols.size) return;
  const current = CC_SPREADS_MATRIX_WS.socket;
  if(current && current.readyState === WebSocket.OPEN){
    CC_SPREADS_MATRIX_WS.symbols = symbols;
    ccSpreadsMatrixRealtimeSubscribe(current);
    return;
  }
  if(current && current.readyState === WebSocket.CONNECTING) return;
  clearTimeout(CC_SPREADS_MATRIX_WS.reconnect);
  clearInterval(CC_SPREADS_MATRIX_WS.heartbeat);
  CC_SPREADS_MATRIX_WS.subscribed.clear();
  const seq = ++CC_SPREADS_MATRIX_WS.seq;
  CC_SPREADS_MATRIX_WS.status = 'authorizing';
  let auth;
  try{
    auth = await ccFuturesBookAuth(false);
  }catch(e){
    if(CC_SPREADS_MATRIX_WS.seq === seq){
      CC_SPREADS_MATRIX_WS.status = 'auth failed';
      CC_SPREADS_MATRIX_WS.lastErrorAt = Date.now();
      ccSpreadsMatrixRealtimeScheduleReconnect();
    }
    return;
  }
  if(CC_SPREADS_MATRIX_WS.seq !== seq || !ccSpreadsIsActive()) return;
  const ws = new WebSocket(ccFuturesBookSocketUrl(auth));
  CC_SPREADS_MATRIX_WS.socket = ws;
  CC_SPREADS_MATRIX_WS.symbols = symbols;
  CC_SPREADS_MATRIX_WS.status = 'connecting';
  ws.onopen = () => {
    if(CC_SPREADS_MATRIX_WS.socket !== ws || CC_SPREADS_MATRIX_WS.seq !== seq) return;
    CC_SPREADS_MATRIX_WS.status = 'live';
    ccSpreadsMatrixRealtimeSubscribe(ws);
    clearInterval(CC_SPREADS_MATRIX_WS.heartbeat);
    CC_SPREADS_MATRIX_WS.heartbeat = setInterval(() => ccBookSendJson(ws, { c:11 }), 15000);
  };
  ws.onmessage = ev => {
    if(CC_SPREADS_MATRIX_WS.socket !== ws || CC_SPREADS_MATRIX_WS.seq !== seq) return;
    let msg;
    try{ msg = JSON.parse(ev.data); }catch{return;}
    if(msg && msg.ping !== undefined){ ccBookSendJson(ws, { pong:msg.ping }); return; }
    ccSpreadsMatrixRealtimeHandleMessage(msg);
  };
  ws.onerror = () => {
    if(CC_SPREADS_MATRIX_WS.socket !== ws) return;
    CC_SPREADS_MATRIX_WS.status = 'socket error';
    CC_SPREADS_MATRIX_WS.lastErrorAt = Date.now();
  };
  ws.onclose = () => {
    if(CC_SPREADS_MATRIX_WS.socket === ws) CC_SPREADS_MATRIX_WS.socket = null;
    clearInterval(CC_SPREADS_MATRIX_WS.heartbeat);
    CC_SPREADS_MATRIX_WS.heartbeat = 0;
    CC_SPREADS_MATRIX_WS.subscribed.clear();
    if(ccSpreadsIsActive()){
      CC_SPREADS_MATRIX_WS.status = 'reconnecting';
      ccSpreadsMatrixRealtimeScheduleReconnect();
    }
  };
}

function ccSpreadsBookPanelId(role){ return role === 'col' ? 'ccSpreadsColBook' : (role === 'spot' ? 'ccSpreadsSpotBook' : 'ccSpreadsRowBook'); }
function ccSpreadsBookRowsId(role, side){ return ccSpreadsBookPanelId(role) + (side === 'ask' ? 'Asks' : 'Bids'); }
function ccSpreadsBookStatusId(role){ return ccSpreadsBookPanelId(role) + 'Status'; }
function ccSpreadsBookMidId(role){ return ccSpreadsBookPanelId(role) + 'Mid'; }
function ccSpreadsBookLabel(role){ return CC_SPREADS_BOOK_STATE.labels[role] || (role === 'spot' ? 'Spot' : 'Futures'); }
function ccSpreadsBookFormatNumber(value, digits=4){
  const n = Number(value);
  if(!Number.isFinite(n)) return '—';
  return n.toLocaleString('en-US', { minimumFractionDigits:0, maximumFractionDigits:digits });
}
function ccSpreadsBookFormatPrice(value){
  const n = Number(value);
  if(!Number.isFinite(n)) return '—';
  const digits = Math.abs(n) >= 100 ? 2 : 5;
  return n.toLocaleString('en-US', { minimumFractionDigits:0, maximumFractionDigits:digits });
}
function ccSpreadsBookFormatTime(ts){
  const n = Number(ts);
  if(!Number.isFinite(n) || n <= 0) return 'Connecting';
  return ccUtcHms(n) + ' UTC';
}
function ccSpreadsFutureBase(item){
  const symbol = String(item?.symbol || item?.displayName || item?.rawLabel || '').toUpperCase();
  const match = symbol.match(/^(BTC|ETH)[A-Z]*/);
  return match ? match[1] : '';
}
function ccSpreadsSpotSymbolForFuture(item){
  const base = ccSpreadsFutureBase(item);
  return base ? base + 'USDT' : '';
}
function ccSpreadsSpotParts(symbol){
  const v = String(symbol || '').toUpperCase();
  if(v.endsWith('USDT')) return { base:v.slice(0,-4), quote:'USDT' };
  if(v.endsWith('USD')) return { base:v.slice(0,-3), quote:'USD' };
  return { base:v || 'BTC', quote:'USDT' };
}
function ccSpreadsIsPerpetualFuture(item){
  const symbol = String(item?.symbol || '').toUpperCase();
  const expiry = Number(item?.expiryTs || item?.expireTime || item?.expire_time || 0);
  return !!symbol && (!Number.isFinite(expiry) || expiry <= 0) && !ccSpreadsParseSymbolExpiry(symbol);
}
function ccSpreadsBookRoles(row, col){
  const spotLeg = ccSpreadsIsSpotLeg(row) ? row : (ccSpreadsIsSpotLeg(col) ? col : null);
  const roles = spotLeg ? [{ role:'spot', item:spotLeg, label:'Spot' }] : [];
  const futures = [row, col].filter(item => item && !ccSpreadsIsSpotLeg(item));
  if(!futures.length) return roles;
  if(futures.length === 1){
    const item = futures[0];
    roles.push({ role:'row', item, label:ccSpreadsIsPerpetualFuture(item) ? 'Perpetual' : 'Dated future' });
    return roles;
  }
  const perp = futures.find(ccSpreadsIsPerpetualFuture);
  if(perp){
    const dated = futures.find(item => item !== perp);
    roles.push({ role:'row', item:perp, label:'Perpetual' });
    if(dated) roles.push({ role:'col', item:dated, label:'Dated future' });
    return roles;
  }
  roles.push({ role:'row', item:futures[0], label:'Dated future' });
  roles.push({ role:'col', item:futures[1], label:'Dated future' });
  return roles;
}
function ccSpreadsFuturesMarkPrice(role){
  const liveMark = CC_SPREADS_BOOK_STATE.mark[role] || {};
  if(liveMark.symbol === CC_SPREADS_BOOK_STATE.symbols[role] && Number.isFinite(Number(liveMark.price)) && Number(liveMark.price) > 0) return Number(liveMark.price);
  const item = CC_SPREADS_BOOK_STATE.items[role];
  const mark = ccSpreadsMarkPrice(item);
  if(Number.isFinite(mark) && mark > 0) return mark;
  const symKey = ccFuturesBookSymbolKey(CC_SPREADS_BOOK_STATE.symbols[role]);
  const fallback = ccFuturesInstrumentMark(symKey);
  return Number.isFinite(fallback) && fallback > 0 ? fallback : NaN;
}
function ccSpreadsSpotIndexForRole(role){
  const item = CC_SPREADS_BOOK_STATE.items[role];
  const symbol = String(CC_SPREADS_BOOK_STATE.symbols[role] || item?.symbol || '').toUpperCase();
  const asset = ccSpreadsMatrixSpotAsset(symbol);
  const cache = CC_SPREADS_SPOT_PRICE_CACHE[asset] || {};
  const cached = Number(cache.indexPrice ?? cache.price);
  if(Number.isFinite(cached) && cached > 0) return cached;
  const itemIndex = ccSpreadsSpotIndexPrice(item);
  return Number.isFinite(itemIndex) && itemIndex > 0 ? itemIndex : NaN;
}
function ccSpreadsSpotIndexMidHtml(role){
  const index = ccSpreadsSpotIndexForRole(role);
  const indexText = Number.isFinite(index) ? ccFmt(index, ccPriceDecimals(index)) : '';
  return '<span class="cc-futures-book-mid-mark">' + ccEsc(indexText) + '</span>';
}
function ccSpreadsSpotMidHtml(role, price, side){
  const baseText = Number.isFinite(price) ? ccFmt(price, ccPriceDecimals(price)) : '...';
  return '<span class="cc-futures-book-mid-last' + (side ? ' ' + ccEsc(side) : '') + '">' + (side ? '<span class="okx-spot-mid-trend">' + (side === 'buy' ? '▲' : '▼') + '</span>' : '') + '<span class="okx-spot-mid-price">' + ccEsc(baseText) + '</span></span>' + ccSpreadsSpotIndexMidHtml(role);
}
function ccSpreadsFuturesBookBidAskMedian(role){
  const raw = CC_SPREADS_BOOK_STATE.raw[role];
  const sides = raw ? ccBookSides(raw) : null;
  if(!sides) return NaN;
  const bidRows = ccBookSort(sides.bidRows || [], 'bid');
  const askRows = ccBookSort(sides.askRows || [], 'ask');
  const bid = Number(bidRows[0]?.p), ask = Number(askRows[0]?.p);
  const symbol = String(CC_SPREADS_BOOK_STATE.symbols[role] || '').toUpperCase();
  const key = 'spreads-futures:' + String(ccFuturesBookSymbolKey(symbol) || symbol || role);
  return ccFilteredBidAskMedian(key, bid, ask);
}
function ccSpreadsFuturesMidHtml(role, price, side){
  const baseText = Number.isFinite(price) ? ccFuturesBookPriceValue(price) : '...';
  const mark = ccSpreadsFuturesMarkPrice(role);
  const markText = Number.isFinite(mark) ? ccFmt(mark, ccPriceDecimals(mark)) : '';
  const bidAskMedian = ccSpreadsFuturesBookBidAskMedian(role);
  const bidAskText = Number.isFinite(bidAskMedian) ? ccFmt(bidAskMedian, ccPriceDecimals(bidAskMedian)) : '';
  return '<span class="cc-futures-book-mid-last' + (side ? ' ' + ccEsc(side) : '') + '">' + (side ? '<span class="okx-spot-mid-trend">' + (side === 'buy' ? '▲' : '▼') + '</span>' : '') + '<span class="okx-spot-mid-price">' + ccEsc(baseText) + '</span></span><span class="cc-futures-book-mid-mark">' + ccEsc(markText) + '</span><span class="cc-futures-book-mid-bidask">' + ccEsc(bidAskText) + '</span>';
}
function ccSpreadsBookHeadHtml(role, symbol){
  const type = CC_SPREADS_BOOK_STATE.types[role] || 'futures';
  if(type === 'spot'){
    const parts = ccSpreadsSpotParts(symbol);
    return '<span>Price</span><span class="okx-spot-book-col-right">Amount</span><span class="okx-spot-book-col-right">Total</span>';
  }
  const unit = ccFuturesBookUnit();
  return '<span>Price</span><span class="okx-spot-book-col-right">Amount</span><span class="okx-spot-book-col-right">Total</span>';
}
function ccSpreadsBookPanelHtml(role, item){
  const symbol = String(item?.symbol || '').toUpperCase();
  const type = CC_SPREADS_BOOK_STATE.types[role] || 'futures';
  const label = type === 'spot' ? ccSpreadsSpotTitle(symbol) : ccSpreadsFutureBookTitle(item);
  return '<section class="okx-spot-card" id="' + ccEsc(ccSpreadsBookPanelId(role)) + '" data-cc-book-card="' + ccEsc(type) + '" data-cc-spreads-book-role="' + ccEsc(role) + '" data-cc-spreads-book-symbol="' + ccEsc(symbol) + '">' +
    '<div class="okx-spot-head"><div class="okx-spot-symbol"><strong>' + ccEsc(label) + '</strong></div><div class="okx-spot-meta" id="' + ccEsc(ccSpreadsBookStatusId(role)) + '">Connecting</div></div>' +
    '<div class="okx-spot-book"><div class="okx-spot-book-head">' + ccSpreadsBookHeadHtml(role, symbol) + '</div><div id="' + ccEsc(ccSpreadsBookRowsId(role,'ask')) + '"></div><div class="okx-spot-mid" id="' + ccEsc(ccSpreadsBookMidId(role)) + '">Connecting</div><div id="' + ccEsc(ccSpreadsBookRowsId(role,'bid')) + '"></div></div>' +
  '</section>';
}
function ccSpreadsTradeFieldId(role, name){ return 'ccSpreadsTrade_' + String(role || '').replace(/[^a-z0-9_-]/gi,'') + '_' + name; }
function ccSpreadsTradeSymbol(role){ return String(CC_SPREADS_BOOK_STATE.symbols[role] || CC_SPREADS_BOOK_STATE.items[role]?.symbol || '').toUpperCase(); }
function ccSpreadsFuturesBase(symbol){
  const text = String(symbol || '').toUpperCase();
  const match = text.match(/^(BTC|ETH|SOL|[A-Z0-9]+?)(?:USDT|USD)/);
  return match && match[1] ? match[1] : (text.replace(/(?:USDT|USD).*$/,'') || 'BTC');
}
function ccSpreadsFuturesAmountMin(base){const key=String(base||'').toUpperCase();if(key==='ETH')return '0.01';if(key==='BTC')return '0.001';return '0.001'}
function ccSpreadsSpotTradePanelHtml(role, item){
  const symbol = String(item?.symbol || '').toUpperCase();
  const parts = ccSpreadsSpotParts(symbol);
  const spotTitle = ccSpreadsSpotTitle(symbol);
  const saved = ccSpreadsTradeInputState('spot', role, symbol);
  const spotType = String(saved.ordType || 'LIMIT').toUpperCase();
  const postOnly = saved.postOnly === undefined ? '1' : String(saved.postOnly);
  return '<section class="okx-spot-card" data-cc-spreads-trade-panel="spot" data-cc-spreads-trade-role="' + ccEsc(role) + '" data-cc-spreads-trade-symbol="' + ccEsc(symbol) + '">' +
    '<div class="okx-spot-head cc-spreads-spot-trade-head"><div class="okx-spot-symbol cc-spreads-spot-trade-title"><strong>Trade ' + ccEsc(spotTitle) + '</strong></div><button type="button" class="okx-spot-settings-btn" data-cc-spreads-trade-settings="1" aria-label="Trade settings" title="Trade settings"><svg class="okx-spot-settings-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 3.2L18.8 7.1V16.9L12 20.8L5.2 16.9V7.1L12 3.2Z"></path><circle cx="12" cy="12" r="2.4"></circle></svg></button></div>' +
    '<div class="okx-spot-trade"><div class="okx-spot-form"><div class="okx-spot-field okx-spot-order-type-field cc-spreads-inline-order-type"><div class="okx-spot-order-type-control"><label>Type</label><select id="' + ccEsc(ccSpreadsTradeFieldId(role,'SpotOrdType')) + '" data-cc-spreads-spot-ord-type><option value="LIMIT"' + ccSpreadsSelectedAttr(spotType,'LIMIT') + '>Limit</option><option value="MARKET"' + ccSpreadsSelectedAttr(spotType,'MARKET') + '>Market</option></select></div><div class="okx-spot-mode-actions"><button type="button" class="tab okx-spot-post-only-btn' + (postOnly !== '0' && spotType === 'LIMIT' ? ' active' : '') + '" data-cc-spreads-spot-post-only aria-pressed="' + (postOnly !== '0' && spotType === 'LIMIT' ? 'true' : 'false') + '" title="Post only">Post only</button></div></div>' +
    '<div class="okx-spot-field cc-spreads-inline-field"><label>Price (' + ccEsc(parts.quote) + ')</label><input id="' + ccEsc(ccSpreadsTradeFieldId(role,'SpotPx')) + '" data-cc-spreads-spot-px inputmode="decimal" placeholder="Market price"' + ccSpreadsInputValueAttr(saved.price) + ' /></div>' +
    '<div class="okx-spot-field cc-spreads-inline-field"><label>Amount (<span data-cc-spreads-spot-base-label>' + ccEsc(parts.base) + '</span>)</label><div class="okx-spot-amount-wrap"><input id="' + ccEsc(ccSpreadsTradeFieldId(role,'SpotSz')) + '" data-cc-spreads-spot-sz inputmode="decimal" placeholder="Min ' + ccEsc(ccSpotMinSize(parts.base)) + ' ' + ccEsc(parts.base) + '"' + ccSpreadsInputValueAttr(saved.amount) + ' /><button type="button" class="okx-spot-amount-btn" data-cc-spreads-spot-min>Min</button><button type="button" class="okx-spot-amount-btn" data-cc-spreads-spot-max>Max</button></div></div>' +
    '<div class="okx-spot-balances"><span data-cc-spreads-spot-avail-quote>Available: — ' + ccEsc(parts.quote) + '</span><span data-cc-spreads-spot-avail-base>Available: — ' + ccEsc(parts.base) + '</span></div>' +
    '<div class="okx-spot-submit-row"><button type="button" class="btn okx-spot-submit buy" data-cc-spreads-order="spot" data-side="BUY">Buy ' + ccEsc(parts.base) + '</button><button type="button" class="btn okx-spot-submit sell" data-cc-spreads-order="spot" data-side="SELL">Sell ' + ccEsc(parts.base) + '</button></div><div class="status muted" data-cc-spreads-order-status></div></div></div></section>';
}
function ccSpreadsFuturesTradePanelHtml(role, item){
  const symbol = String(item?.symbol || '').toUpperCase();
  const base = ccSpreadsFuturesBase(symbol);
  const saved = ccSpreadsTradeInputState('futures', role, symbol);
  const futuresType = String(saved.ordType || 'LIMIT').toUpperCase();
  const unit = String(saved.unit || base).toUpperCase();
  const levValue = saved.leverage || Number(CC_SPREADS_LEVERAGE_CACHE[ccSpreadsLeverageCacheKey(symbol, ccSpreadsActiveAccountId())]) || 1;
  return '<section class="okx-spot-card okx-futures-card" data-cc-spreads-trade-panel="futures" data-cc-spreads-trade-role="' + ccEsc(role) + '" data-cc-spreads-trade-symbol="' + ccEsc(symbol) + '">' +
    '<div class="okx-spot-trade okx-futures-ticket"><div class="okx-futures-ticket-head"><div class="okx-futures-ticket-tabs" role="tablist" aria-label="Futures ticket tabs"><button type="button" class="okx-futures-ticket-tab active" role="tab" aria-selected="true" data-cc-futures-ticket="trade">Trade</button><button type="button" class="okx-futures-ticket-tab" role="tab" aria-selected="false" data-cc-futures-ticket="tools">Tools</button></div><button type="button" class="okx-spot-settings-btn" data-cc-spreads-trade-settings="1" aria-label="Trade settings" title="Trade settings"><svg class="okx-spot-settings-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 3.2L18.8 7.1V16.9L12 20.8L5.2 16.9V7.1L12 3.2Z"></path><circle cx="12" cy="12" r="2.4"></circle></svg></button></div>' +
    '<div class="okx-futures-ticket-panel" data-cc-futures-ticket-panel="trade"><div class="okx-futures-mode-row"><select id="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesMargin')) + '" aria-label="Margin mode"><option value="cross"' + ccSpreadsSelectedAttr(saved.margin || 'cross','cross') + '>Cross</option></select><div class="cc-futures-leverage-wrap"><button type="button" class="okx-futures-leverage-chip cross" data-cc-spreads-leverage-chip aria-label="Adjust leverage"><span class="lev-green" data-cc-spreads-leverage-value>—</span> <span class="lev-red">—</span></button><select id="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesLeverageSelect')) + '" data-cc-spreads-futures-leverage aria-label="Futures leverage">' + ccFuturesLeverageOptions(Number(levValue) || 1) + '</select></div></div>' +
    '<div class="okx-futures-field cc-spreads-futures-inline-field cc-spreads-futures-order-field"><label for="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesOrdType')) + '">Order type</label><select id="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesOrdType')) + '" data-cc-spreads-futures-ord-type><option value="LIMIT"' + ccSpreadsSelectedAttr(futuresType,'LIMIT') + '>Limit</option><option value="MARKET"' + ccSpreadsSelectedAttr(futuresType,'MARKET') + '>Market</option><option value="POST_ONLY"' + ccSpreadsSelectedAttr(futuresType,'POST_ONLY') + '>Post Only</option></select></div>' +
    '<div class="okx-futures-field cc-spreads-futures-inline-field cc-spreads-futures-price-field"><label for="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesPx')) + '">Price (USDT)</label><div class="okx-futures-price-wrap"><input id="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesPx')) + '" data-cc-spreads-futures-px inputmode="decimal" placeholder="Market price"' + ccSpreadsInputValueAttr(saved.price) + ' /></div></div>' +
    '<div class="okx-futures-field cc-spreads-futures-amount-field"><label for="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesSz')) + '">Amount</label><div class="cc-futures-amount-wrap"><input id="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesSz')) + '" data-cc-spreads-futures-sz inputmode="decimal" placeholder="Min ' + ccEsc(ccSpreadsFuturesAmountMin(base)) + ' ' + ccEsc(base) + '"' + ccSpreadsInputValueAttr(saved.amount) + ' /><button type="button" class="okx-spot-amount-btn" data-cc-spreads-futures-amount-min>Min</button><select id="' + ccEsc(ccSpreadsTradeFieldId(role,'FuturesAmountUnit')) + '" class="cc-futures-amount-unit" data-cc-spreads-futures-unit aria-label="Amount unit"><option value="' + ccEsc(base) + '"' + ccSpreadsSelectedAttr(unit,base) + '>' + ccEsc(base) + '</option><option value="USDT"' + ccSpreadsSelectedAttr(unit,'USDT') + '>USDT</option></select></div></div>' +
    '<div class="okx-futures-info-row"><span data-cc-spreads-futures-avail-margin>Available — USDT</span></div><div class="okx-futures-info-row"><span data-cc-spreads-futures-max-long>Max long — ' + ccEsc(base) + '</span><span data-cc-spreads-futures-max-short>Max short — ' + ccEsc(base) + '</span></div>' +
    '<div class="okx-futures-submit-row"><button type="button" class="okx-futures-side-submit long" data-cc-spreads-order="futures" data-side="BUY">Buy</button><button type="button" class="okx-futures-side-submit short" data-cc-spreads-order="futures" data-side="SELL">Sell</button></div><div class="status muted" data-cc-spreads-order-status></div></div><div class="okx-futures-ticket-panel" data-cc-futures-ticket-panel="tools" hidden><div class="okx-futures-tools-empty">Tools are not connected yet.</div></div></div></section>';
}
function ccSpreadsTradePanelHtml(role, item){
  return (CC_SPREADS_BOOK_STATE.types[role] || 'futures') === 'spot' ? ccSpreadsSpotTradePanelHtml(role, item) : ccSpreadsFuturesTradePanelHtml(role, item);
}
function ccSpreadsInstrumentPairHtml(entry){
  return '<div class="cc-spreads-instrument-pair" data-cc-spreads-pair-role="' + ccEsc(entry.role) + '">' + ccSpreadsBookPanelHtml(entry.role, entry.item) + ccSpreadsTradePanelHtml(entry.role, entry.item) + '</div>';
}
function ccSpreadsPopupSetStatus(panel, msg, kind){
  const el = panel?.querySelector?.('[data-cc-spreads-order-status]');
  if(el){ el.textContent = msg || ''; el.className = 'status ' + (kind || 'muted'); }
  ccSpreadsLogMessage(panel, msg, kind);
}
function ccSpreadsFuturesCancelStatus(order,msg,kind){
  if(!ccSpreadsTradeModalOpen()) return;
  const symbol=ccFuturesCanonicalDisplaySymbolFromOrder(order)||ccPick(order,['displaySymbol','displayName','symbol','instId','instrument']);
  document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-panel="futures"]').forEach(panel=>{
    const panelSymbol=String(panel.dataset.ccSpreadsTradeSymbol||'');
    if(!symbol||!panelSymbol||ccFuturesCanonicalDisplaySymbol(panelSymbol)===ccFuturesCanonicalDisplaySymbol(symbol))ccSpreadsPopupSetStatus(panel,msg,kind);
  });
}
function ccSpreadsLogMessage(panel, msg, kind){
  const text = String(msg || '').trim();
  if(!text) return;
  const entry = {
    ts: Date.now(),
    kind: kind || 'muted',
    market: String(panel?.dataset?.ccSpreadsTradePanel || '—'),
    role: String(panel?.dataset?.ccSpreadsTradeRole || '—'),
    symbol: String(panel?.dataset?.ccSpreadsTradeSymbol || '—'),
    message: text
  };
  ccSpreadsLogs.unshift(entry);
  if(ccSpreadsLogs.length > CC_SPREADS_LOG_LIMIT) ccSpreadsLogs.length = CC_SPREADS_LOG_LIMIT;
  if(ccSpreadsCurrentPanel() === 'logs') ccRenderSpreadsPositionsOrdersTable();
}
function ccSpreadsLogStatusOutput(id, msg){
  if(!ccSpreadsTradeModalOpen()) return;
  if(id !== 'ccSpotOrderStatus' && id !== 'ccFuturesOrderStatus') return;
  const market = id === 'ccSpotOrderStatus' ? 'spot' : 'futures';
  const text = typeof msg === 'string' ? msg : JSON.stringify(msg);
  const symbol = market === 'spot' ? String(cc('ccSpotInst')?.value || '—') : String(cc('ccFuturesInst')?.value || '—');
  ccSpreadsLogMessage({ dataset:{ ccSpreadsTradePanel:market, ccSpreadsTradeRole:'main', ccSpreadsTradeSymbol:symbol } }, text, /failed|cannot|not found/i.test(text) ? 'danger' : 'muted');
}
function ccSpreadsLogsRows(){
  return ccSpreadsLogs.map(x => '<tr><td>' + ccEsc(ccUtcDateTime(x.ts)) + '</td><td>' + ccEsc(x.market) + '</td><td>' + ccEsc(x.role) + '</td><td>' + ccEsc(x.symbol) + '</td><td class="' + (x.kind === 'danger' ? 'sell' : x.kind === 'success' ? 'buy' : 'muted') + '">' + ccEsc(x.kind) + '</td><td>' + ccEsc(x.message) + '</td></tr>');
}
function ccSpreadsUpdateSpotTradePanel(panel){
  const symbol = String(panel?.dataset?.ccSpreadsTradeSymbol || '').toUpperCase();
  const parts = ccSpreadsSpotParts(symbol);
  const type = String(panel.querySelector('[data-cc-spreads-spot-ord-type]')?.value || 'LIMIT').toUpperCase();
  const postBtn = panel.querySelector('[data-cc-spreads-spot-post-only]');
  if(type === 'MARKET') panel.dataset.ccSpreadsPostOnly = '0';
  if(panel.dataset.ccSpreadsPostOnly === undefined) panel.dataset.ccSpreadsPostOnly = '1';
  const postOnly = panel.dataset.ccSpreadsPostOnly !== '0' && type === 'LIMIT';
  if(postBtn){ postBtn.classList.toggle('active', postOnly); postBtn.setAttribute('aria-pressed', postOnly ? 'true' : 'false'); postBtn.disabled = type !== 'LIMIT'; postBtn.title = type === 'LIMIT' ? 'Post only' : 'Post only is disabled for market orders.'; }
  const px = panel.querySelector('[data-cc-spreads-spot-px]');
  if(px) px.disabled = type === 'MARKET';
  const baseLabel = panel.querySelector('[data-cc-spreads-spot-base-label]');
  if(baseLabel) baseLabel.textContent = parts.base;
  const sz = panel.querySelector('[data-cc-spreads-spot-sz]');
  if(sz) sz.placeholder = 'Min ' + ccSpotMinSize(parts.base) + ' ' + parts.base;
  panel.querySelectorAll('[data-cc-spreads-order="spot"]').forEach(btn => { btn.textContent = (btn.dataset.side === 'SELL' ? 'Sell ' : 'Buy ') + parts.base; });
  const quoteBal = ccSpotBalance(parts.quote), baseBal = ccSpotBalance(parts.base);
  const q = panel.querySelector('[data-cc-spreads-spot-avail-quote]'), b = panel.querySelector('[data-cc-spreads-spot-avail-base]');
  if(q) q.textContent = 'Available: ' + (Number.isFinite(quoteBal) ? ccMoney(quoteBal, parts.quote) : '— ' + parts.quote);
  if(b) b.textContent = 'Available: ' + (Number.isFinite(baseBal) ? ccMoney(baseBal, parts.base) : '— ' + parts.base);
}
function ccSpreadsUpdateFuturesTradePanel(panel){
  const symbol = String(panel?.dataset?.ccSpreadsTradeSymbol || '').toUpperCase();
  const base = ccSpreadsFuturesBase(symbol);
  const unitSel = panel.querySelector('[data-cc-spreads-futures-unit]');
  const unit = String(unitSel?.value || base).toUpperCase();
  const ordType = String(panel.querySelector('[data-cc-spreads-futures-ord-type]')?.value || 'LIMIT').toUpperCase();
  const px = panel.querySelector('[data-cc-spreads-futures-px]');
  if(px) px.disabled = ordType === 'MARKET';
  const lev = Number(CC_SPREADS_LEVERAGE_CACHE[ccSpreadsLeverageCacheKey(symbol, ccSpreadsActiveAccountId())]);
  const levText = Number.isFinite(lev) && lev > 0 ? lev + 'x' : '—';
  const levEl = panel.querySelector('[data-cc-spreads-leverage-value]');
  if(levEl) levEl.textContent = levText;
  const rows = ccBalanceRows(ccLastAssetsSummary || {});
  const metrics = (ccLastAssetsSummary && ccLastAssetsSummary.metrics) || {};
  const available = ccFirstFinite(metrics && metrics.availableEquity, ccFindNumericDeep(ccLastAssetsSummary || {}, ['availableequity','availeq','availbalance','availablebalance','available']), ccHeaderStableSum(rows,'available'));
  const avail = panel.querySelector('[data-cc-spreads-futures-avail-margin]');
  if(avail) avail.textContent = 'Available ' + (Number.isFinite(available) ? ccFmtNumber(available, 2) : '—') + ' USDT';
  const mark = ccSpreadsFuturesMarkPrice(panel.dataset.ccSpreadsTradeRole);
  const max = Number.isFinite(available) && Number.isFinite(lev) && lev > 0 ? available * lev : NaN;
  const maxBase = Number.isFinite(max) && Number.isFinite(mark) && mark > 0 ? max / mark : NaN;
  const maxValue = unit === 'USDT' ? max : maxBase;
  const maxText = ccFuturesFormatTradeUnit(maxValue, unit) + ' ' + unit;
  const maxLong = panel.querySelector('[data-cc-spreads-futures-max-long]'), maxShort = panel.querySelector('[data-cc-spreads-futures-max-short]');
  if(maxLong) maxLong.textContent = 'Max long ' + maxText;
  if(maxShort) maxShort.textContent = 'Max short ' + maxText;
}
function ccSpreadsUpdateTradePanels(){
  document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-panel="spot"]').forEach(ccSpreadsUpdateSpotTradePanel);
  document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-panel="futures"]').forEach(ccSpreadsUpdateFuturesTradePanel);
  ccRenderSpreadsAccountSummary();
}
function ccSpreadsPanelPriceStep(panel, dir){
  const input = panel?.querySelector?.('[data-cc-spreads-futures-px]');
  const current = Number(input?.value || 0);
  const step = 0.1;
  if(input){ input.value = String(Math.max(0, (Number.isFinite(current) ? current : 0) + Number(dir || 0) * step)); input.dispatchEvent(new Event('input',{bubbles:true})); input.focus(); }
}
function ccSpreadsPanelSetBookPrice(priceEl){
  const role = priceEl?.closest?.('[data-cc-spreads-book-role]')?.dataset?.ccSpreadsBookRole || '';
  const panel = Array.from(document.querySelectorAll('#ccSpreadsTradeModal [data-cc-spreads-trade-role]')).find(x => x.dataset.ccSpreadsTradeRole === role);
  const type = panel?.dataset?.ccSpreadsTradePanel;
  const input = panel?.querySelector?.(type === 'futures' ? '[data-cc-spreads-futures-px]' : '[data-cc-spreads-spot-px]');
  if(input){ input.value = priceEl.dataset.ccBookPrice || priceEl.textContent.trim(); input.dispatchEvent(new Event('input',{bubbles:true})); input.focus(); return true; }
  return false;
}
function ccSpreadsScheduleFastPrivateRefresh(delay=120){if(!ccSpreadsTradeModalOpen())return;const minDelay=ccSpreadsLatencyPriorityActive()?500:250;ccSpreadsPrivateRefreshLastAt=0;clearTimeout(ccSpreadsPrivateRefreshTimer);ccSpreadsPrivateRefreshTimer=setTimeout(ccSpreadsRefreshPrivateState,Math.max(delay,minDelay))}
function ccSpreadsSetPanelSubmitPending(panel,pending){if(!panel)return;panel.querySelectorAll('[data-cc-spreads-order]').forEach(btn=>{btn.toggleAttribute('disabled',!!pending);btn.setAttribute('aria-disabled',pending?'true':'false')})}
async function ccSpreadsPlacePopupOrder(panel, market, side){
  const symbol = String(panel?.dataset?.ccSpreadsTradeSymbol || '').toUpperCase();
  if(!symbol){ ccSpreadsPopupSetStatus(panel, 'Order failed: popup instrument is missing', 'danger'); return; }
  let p;
  if(market === 'spot'){
    const type = String(panel.querySelector('[data-cc-spreads-spot-ord-type]')?.value || 'LIMIT').toUpperCase();
    const tradeType = type === 'MARKET' ? 'MARKET' : (panel.dataset.ccSpreadsPostOnly !== '0' ? 'POST_ONLY' : 'LIMIT');
    p = { symbol, tradeSide:side, tradeType, price:panel.querySelector('[data-cc-spreads-spot-px]')?.value || '', qty:panel.querySelector('[data-cc-spreads-spot-sz]')?.value || '' };
    if(tradeType === 'MARKET') delete p.price;
  } else {
    const type = String(panel.querySelector('[data-cc-spreads-futures-ord-type]')?.value || 'LIMIT').toUpperCase();
    const unit = String(panel.querySelector('[data-cc-spreads-futures-unit]')?.value || ccSpreadsFuturesBase(symbol)).toUpperCase();
    const rawQty = Number(panel.querySelector('[data-cc-spreads-futures-sz]')?.value || '');
    const price = Number(panel.querySelector('[data-cc-spreads-futures-px]')?.value || '');
    const mark = ccSpreadsFuturesMarkPrice(panel.dataset.ccSpreadsTradeRole);
    const qty = unit === 'USDT' ? (Number.isFinite(rawQty) && rawQty > 0 ? rawQty / (Number.isFinite(price) && price > 0 ? price : mark) : NaN) : rawQty;
    p = { symbol, tradeSide:String(side).toUpperCase() === 'SELL' ? '2' : '1', tradeType:type === 'MARKET' ? '2' : (type === 'POST_ONLY' ? '3' : '1'), qty:Number.isFinite(qty) ? String(qty) : '' };
    if(p.tradeType !== '2') p.price = panel.querySelector('[data-cc-spreads-futures-px]')?.value || '';
  }
  const submitKey=ccOrderSubmitPendingKey(market,p);
  if(CC_ORDER_SUBMIT_PENDING.has(submitKey)){ ccSpreadsPopupSetStatus(panel, 'Order already submitting...', 'muted'); return; }
  if(!ccConfirmOrderAction('Submit ' + ((p.tradeSide === '2' || p.tradeSide === 'SELL') ? 'SELL' : 'BUY') + ' ' + ccOrderTypeLabel(p.tradeType || p.type) + ' order ' + p.qty + ' ' + p.symbol + (p.price ? ' @ ' + p.price : '') + '?')) return;
  if(!ccOrderSubmitLock(submitKey)){ ccSpreadsPopupSetStatus(panel, 'Order already submitting...', 'muted'); return; }
  ccSpreadsSetPanelSubmitPending(panel,true);
  if(market==='futures')CC_FUTURES_ORDER_REFRESH.protectUntil=Date.now()+CC_FUTURES_REST_REPLACE_GUARD_MS;else ccSpotProtectOpenOrdersUntil=Date.now()+CC_SPOT_REST_REPLACE_GUARD_MS;
  const diag=ccSpreadsLatencyStart('place',market,symbol);
  try{
    if(market === 'futures'){ ccEnsureFuturesPrivateSocket().catch(()=>{}); ccSpreadsPopupSetStatus(panel, 'Submitting order…', 'muted'); }
    const apiStarted=ccSpreadsLatencyNow();
    ccOrderDiagnosticPush('place',{symbol:p.symbol,side:p.tradeSide||side,action:'sent',message:market+' popup order place request sent'});const res = await ccApi('/api/admin/coincall/' + market + '/orders/place', { method:'POST', body:JSON.stringify(p) });ccOrderDiagnosticPush('place',{symbol:p.symbol,side:p.tradeSide||side,action:'accepted',message:market+' popup order place response accepted'});
    diag.apiMs=ccSpreadsLatencyNow()-apiStarted;
    const postStarted=ccSpreadsLatencyNow();
    if(market === 'spot'){ ccAddOptimisticSpotOpenOrder(p,res); ccSpreadsPopupSetStatus(panel, ccOrderResultText('Order placed', res, { symbol:p.symbol, side:p.tradeSide || side, type:p.tradeType, qty:p.qty, price:p.price }), 'muted'); ccReconcileSpotOpenOrdersSoon('spreads-popup-order-submit'); ccScheduleSpotTradeRefreshBurst('spreads-popup-order-submit'); ccSpreadsScheduleFastPrivateRefresh(80); }
    else { diag.confirmMs=0; ccAddOptimisticFuturesOpenOrder(p,res); ccScheduleFuturesOrderRefreshBurst('spreads-popup-order-submit'); ccSpreadsPopupSetStatus(panel, ccOrderResultText('Order sent', res, { symbol:p.symbol, side:p.tradeSide || side, type:p.tradeType, qty:p.qty, price:p.price }), 'muted'); ccSpreadsScheduleFastPrivateRefresh(80); }
    diag.postMs=ccSpreadsLatencyNow()-postStarted;
    ccSpreadsLatencyFinish(diag,'ok');
  }catch(e){
    ccOrderDiagnosticPush('place',{symbol:p.symbol,side:p.tradeSide||side,action:'failed',message:market+' popup order place failed: '+e.message});
    ccSpreadsLatencyFinish(diag,'error',e);
    ccSpreadsPopupSetStatus(panel, 'Order failed: ' + e.message, 'danger');
  }finally{
    ccOrderSubmitUnlock(submitKey);
    ccSpreadsSetPanelSubmitPending(panel,false);
  }
}
const CC_SPREADS_POPUP_BOOK_RENDER_MIN_MS = 200;
const CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE = { timer:{row:null,col:null,spot:null}, last:{row:0,col:0,spot:0} };
function ccSpreadsRenderModalBookNow(role){
  if(CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role]){
    clearTimeout(CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role]);
    CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role] = null;
  }
  CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.last[role] = Date.now();
  const renderStarted=ccSpreadsLatencyNow();
  const raw = CC_SPREADS_BOOK_STATE.raw[role];
  const status = cc(ccSpreadsBookStatusId(role));
  if(status) status.textContent = CC_SPREADS_BOOK_STATE.status[role] || ccSpreadsBookFormatTime(CC_SPREADS_BOOK_STATE.updated[role]);
  const mid = cc(ccSpreadsBookMidId(role));
  if(!raw){
    if(mid) mid.textContent = CC_SPREADS_BOOK_STATE.status[role] || 'Connecting';
    return false;
  }
  const type = CC_SPREADS_BOOK_STATE.types[role] || 'futures';
  const sides = ccBookSides(raw);
  const askRows = type === 'futures' ? ccFuturesVisibleBookRows(sides.askRows, 'ask') : sides.askRows;
  const bidRows = type === 'futures' ? ccFuturesVisibleBookRows(sides.bidRows, 'bid') : sides.bidRows;
  const asks = type === 'futures' ? ccBookWithTotals(ccFuturesDenseBookRows(ccFuturesAggregateBookRows(askRows, 'ask'), 'ask'), 'ask', CC_SPREADS_POPUP_BOOK_DEPTH) : ccBookWithTotals(ccBookSort(askRows, 'ask'), 'ask', CC_SPREADS_POPUP_BOOK_DEPTH);
  const bids = type === 'futures' ? ccBookWithTotals(ccFuturesDenseBookRows(ccFuturesAggregateBookRows(bidRows, 'bid'), 'bid'), 'bid', CC_SPREADS_POPUP_BOOK_DEPTH) : ccBookWithTotals(ccBookSort(bidRows, 'bid'), 'bid', CC_SPREADS_POPUP_BOOK_DEPTH);
  const maxTotal = Math.max(0, ...asks.map(r => Number(r.total) || 0), ...bids.map(r => Number(r.total) || 0));
  const askHost = cc(ccSpreadsBookRowsId(role, 'ask'));
  const bidHost = cc(ccSpreadsBookRowsId(role, 'bid'));
  if(askHost) askHost.innerHTML = ccRenderBookRows(asks.slice().reverse(), 'ask', maxTotal, type, CC_SPREADS_POPUP_BOOK_DEPTH, CC_SPREADS_BOOK_STATE.symbols[role]);
  if(bidHost) bidHost.innerHTML = ccRenderBookRows(bids, 'bid', maxTotal, type, CC_SPREADS_POPUP_BOOK_DEPTH, CC_SPREADS_BOOK_STATE.symbols[role]);
  ccSpreadsLatencyRecordBook(ccSpreadsLatencyNow()-renderStarted);
  if(mid){
    mid.classList.remove('cc-futures-book-mid','buy','sell');
    if(type === 'spot'){
      const live = CC_SPREADS_BOOK_STATE.spotLastTrade || {};
      const price = live.symbol === CC_SPREADS_BOOK_STATE.symbols.spot ? Number(live.price) : NaN;
      const side = live.side === 'buy' || live.side === 'sell' ? live.side : '';
      mid.classList.add('cc-futures-book-mid');
      if(side) mid.classList.add(side);
      mid.innerHTML = ccSpreadsSpotMidHtml(role, price, side);
    } else {
      const live = CC_SPREADS_BOOK_STATE.lastTrade[role] || {};
      const price = live.symbol === CC_SPREADS_BOOK_STATE.symbols[role] ? Number(live.price) : NaN;
      const side = live.side === 'buy' || live.side === 'sell' ? live.side : '';
      mid.classList.add('cc-futures-book-mid');
      if(side) mid.classList.add(side);
      mid.innerHTML = ccSpreadsFuturesMidHtml(role, price, side);
    }
  }
  ccSpreadsUpdateTradePanels();
  return true;
}
function ccSpreadsRenderModalBook(role){ return ccSpreadsRenderModalBookNow(role); }
function ccSpreadsScheduleModalBookRender(role){
  if(!role || !CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.last.hasOwnProperty(role)) return ccSpreadsRenderModalBookNow(role);
  const now = Date.now();
  const elapsed = now - (CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.last[role] || 0);
  const delay = Math.max(0, CC_SPREADS_POPUP_BOOK_RENDER_MIN_MS - elapsed);
  if(delay === 0) return ccSpreadsRenderModalBookNow(role);
  if(CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role]) return false;
  CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role] = setTimeout(() => ccSpreadsRenderModalBookNow(role), delay);
  return false;
}
function ccSpreadsRenderModalBooks(){
  ccSpreadsRenderModalBook('row');
  ccSpreadsRenderModalBook('col');
  ccSpreadsRenderModalBook('spot');
}
function ccSpreadsBookClearTimer(type, role){
  const id = CC_SPREADS_BOOK_STATE[type]?.[role];
  if(id) clearTimeout(id), clearInterval(id);
  if(CC_SPREADS_BOOK_STATE[type]) CC_SPREADS_BOOK_STATE[type][role] = null;
}
function ccSpreadsClearScheduledModalBookRender(role){
  const id = CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role];
  if(id) clearTimeout(id);
  CC_SPREADS_POPUP_BOOK_RENDER_THROTTLE.timer[role] = null;
}
function ccSpreadsBookClose(role, reconnect=false){
  ccSpreadsClearScheduledModalBookRender(role);
  ccSpreadsBookClearTimer('heartbeat', role);
  ccSpreadsBookClearTimer('reconnect', role);
  const ws = CC_SPREADS_BOOK_STATE.sockets[role];
  CC_SPREADS_BOOK_STATE.sockets[role] = null;
  if(ws){
    try{ ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null; }catch{}
    try{ ws.close(); }catch{}
  }
  if(reconnect && ccSpreadsTradeModalOpen() && CC_SPREADS_BOOK_STATE.symbols[role]){
    CC_SPREADS_BOOK_STATE.reconnect[role] = setTimeout(() => ccSpreadsBookConnect(role, CC_SPREADS_BOOK_STATE.symbols[role]), CC_BOOK_RECONNECT_MS);
  }
}
function ccSpreadsStopModalBooks(){
  ['row','col','spot'].forEach(role => {
    ccSpreadsBookClose(role, false);
    CC_SPREADS_BOOK_STATE.symbols[role] = '';
    CC_SPREADS_BOOK_STATE.items[role] = null;
    CC_SPREADS_BOOK_STATE.labels[role] = role === 'spot' ? 'Spot' : 'Futures';
    CC_SPREADS_BOOK_STATE.raw[role] = null;
    CC_SPREADS_BOOK_STATE.status[role] = '';
    CC_SPREADS_BOOK_STATE.updated[role] = 0;
    if(CC_SPREADS_BOOK_STATE.lastTrade[role]) CC_SPREADS_BOOK_STATE.lastTrade[role] = { symbol:'', price:NaN, side:'', ts:0 };
    if(CC_SPREADS_BOOK_STATE.mark[role]) CC_SPREADS_BOOK_STATE.mark[role] = { symbol:'', price:NaN, indexPrice:NaN, ts:0 };
  });
  CC_SPREADS_BOOK_STATE.spotCache = { channel:'', asks:new Map(), bids:new Map() };
  CC_SPREADS_BOOK_STATE.spotLastTrade = { symbol:'', price:NaN, side:'', ts:0 };
}
async function ccSpreadsBookConnect(role, symbol){
  symbol = String(symbol || '').trim().toUpperCase();
  if(!symbol) return;
  const type = CC_SPREADS_BOOK_STATE.types[role] || 'futures';
  const current = CC_SPREADS_BOOK_STATE.sockets[role];
  if(current && CC_SPREADS_BOOK_STATE.symbols[role] === symbol && [WebSocket.OPEN, WebSocket.CONNECTING].includes(current.readyState)) return;
  ccSpreadsBookClose(role, false);
  CC_SPREADS_BOOK_STATE.symbols[role] = symbol;
  CC_SPREADS_BOOK_STATE.status[role] = type === 'futures' ? 'Authorizing' : 'Connecting';
  ccSpreadsRenderModalBook(role);
  const seq = (CC_SPREADS_BOOK_STATE.connectSeq[role] || 0) + 1;
  CC_SPREADS_BOOK_STATE.connectSeq[role] = seq;
  let auth;
  if(type === 'futures'){
    try{
      auth = await ccFuturesBookAuth(false);
    }catch(e){
      if(CC_SPREADS_BOOK_STATE.connectSeq[role] !== seq) return;
      CC_SPREADS_BOOK_STATE.status[role] = 'Auth failed';
      ccSpreadsRenderModalBook(role);
      CC_SPREADS_BOOK_STATE.reconnect[role] = setTimeout(() => ccSpreadsBookConnect(role, symbol), CC_BOOK_RECONNECT_MS);
      return;
    }
  }
  if(CC_SPREADS_BOOK_STATE.connectSeq[role] !== seq || CC_SPREADS_BOOK_STATE.symbols[role] !== symbol || !ccSpreadsTradeModalOpen()) return;
  const ws = new WebSocket(type === 'spot' ? 'wss://ws.coincall.com/spot/ws' : ccFuturesBookSocketUrl(auth));
  CC_SPREADS_BOOK_STATE.sockets[role] = ws;
  CC_SPREADS_BOOK_STATE.status[role] = 'Connecting';
  ccSpreadsRenderModalBook(role);
  ws.onopen = () => {
    if(CC_SPREADS_BOOK_STATE.sockets[role] !== ws || CC_SPREADS_BOOK_STATE.connectSeq[role] !== seq) return;
    CC_SPREADS_BOOK_STATE.status[role] = ccSpreadsBookFormatTime(Date.now());
    if(type === 'spot'){
      CC_SPREADS_BOOK_STATE.spotCache = { channel:ccSpotBookChannel(symbol), asks:new Map(), bids:new Map() };
      ccBookSendJson(ws, { sub:ccSpotBookChannel(symbol), id:Date.now() });
      ccBookSendJson(ws, { sub:ccSpotTradeDetailChannel(symbol), id:Date.now()+1 });
      ccBookSendJson(ws, { sub:ccSpotOverviewChannel(symbol), id:Date.now()+2 });
    } else {
      ccBookSendJson(ws, { c:20, dt:58, d:{ s:symbol, step:CC_FUTURES_BOOK_STEP } });
      ccBookSendJson(ws, { c:20, dt:30, d:{ s:symbol } });
      ccBookSendJson(ws, { c:20, dt:33, d:{ s:symbol } });
      ccBookSendJson(ws, { c:20, dt:43, d:{ s:symbol } });
    }
    ccSpreadsBookClearTimer('heartbeat', role);
    CC_SPREADS_BOOK_STATE.heartbeat[role] = setInterval(() => ccBookSendJson(ws, { c:11 }), 15000);
    ccSpreadsRenderModalBook(role);
  };
  ws.onmessage = ev => {
    if(CC_SPREADS_BOOK_STATE.sockets[role] !== ws || CC_SPREADS_BOOK_STATE.connectSeq[role] !== seq) return;
    let msg;
    try{ msg = JSON.parse(ev.data); }catch{return;}
    if(msg && msg.ping !== undefined){ ccBookSendJson(ws, { pong:msg.ping }); return; }
    if(type === 'spot'){
      const channel = String(msg && msg.ch || '');
      const tick = msg && msg.tick;
      if(channel === ccSpotOverviewChannel(symbol) && tick){
        const indexPrice = ccSpreadsSpotIndexPrice(tick);
        if(Number.isFinite(indexPrice) && indexPrice > 0) ccSpreadsApplyMatrixSpotQuote(symbol, { indexPrice });
        CC_SPREADS_BOOK_STATE.updated[role] = Date.now();
        CC_SPREADS_BOOK_STATE.status[role] = ccSpreadsBookFormatTime(CC_SPREADS_BOOK_STATE.updated[role]);
        ccSpreadsScheduleModalBookRender(role);
        return;
      }
      if(channel === ccSpotTradeDetailChannel(symbol) && Array.isArray(tick)){
        const first = tick.filter(t => String(t && t.symbol || symbol).trim() === symbol).sort((a,b) => Number(b && b.ts || 0) - Number(a && a.ts || 0))[0];
        if(first){
          const price = Number(first.price), sideRaw = Number(first.side), ts = Number(first.ts || 0);
          if(Number.isFinite(price)){
            CC_SPREADS_BOOK_STATE.spotLastTrade = { symbol, price, side:sideRaw === 1 ? 'buy' : (sideRaw === 2 ? 'sell' : ''), ts:Number.isFinite(ts) ? ts : Date.now() };
            ccSpreadsScheduleLiveSpreadChartUpdate('spot-trade');
          }
        }
        CC_SPREADS_BOOK_STATE.updated[role] = Date.now();
        CC_SPREADS_BOOK_STATE.status[role] = ccSpreadsBookFormatTime(CC_SPREADS_BOOK_STATE.updated[role]);
        ccSpreadsScheduleModalBookRender(role);
        return;
      }
      if(!channel.startsWith('market.') || !channel.includes('.mbp.') || !tick) return;
      const tickSymbol = String(tick.s || '').trim();
      if(tickSymbol !== symbol) return;
      if(String(tick.type || '').toLowerCase() === 'snapshot' || CC_SPREADS_BOOK_STATE.spotCache.channel !== channel) CC_SPREADS_BOOK_STATE.spotCache = { channel, asks:new Map(), bids:new Map() };
      const apply = (side, levels) => {
        const book = CC_SPREADS_BOOK_STATE.spotCache[side];
        for(const level of Array.isArray(levels) ? levels : []){
          if(!Array.isArray(level) || level.length < 2) continue;
          const price = String(level[0]), size = Number(level[1]);
          if(!Number.isFinite(size)) continue;
          if(size <= 0) book.delete(price); else book.set(price, size);
        }
      };
      const materialize = side => Array.from(CC_SPREADS_BOOK_STATE.spotCache[side].entries()).map(([p,q]) => [Number(p), q]).filter(([p,q]) => Number.isFinite(p) && Number.isFinite(q) && p > 0 && q >= 0).sort((a,b) => side === 'asks' ? a[0] - b[0] : b[0] - a[0]).slice(0, CC_SPOT_BOOK_LEVEL);
      apply('asks', tick.a);
      apply('bids', tick.b);
      CC_SPREADS_BOOK_STATE.raw[role] = { asks:materialize('asks'), bids:materialize('bids'), ts:Date.now(), symbol };
      CC_SPREADS_BOOK_STATE.updated[role] = Date.now();
      CC_SPREADS_BOOK_STATE.status[role] = ccSpreadsBookFormatTime(CC_SPREADS_BOOK_STATE.updated[role]);
      ccSpreadsScheduleModalBookRender(role);
      ccSpreadsScheduleLiveSpreadChartUpdate('spot-book');
      return;
    }
    const dt = Number(msg && msg.dt);
    if(dt === 30){
      const data = msg && msg.d;
      if(!data || String(data.s || '').trim().toUpperCase() !== symbol) return;
      const mark = Number(data.mp ?? data.markPrice ?? data.mark_price);
      const indexPrice = Number(data.ip ?? data.indexPrice ?? data.index_price);
      const ts = Number(data.ts || msg.ts || Date.now());
      if(Number.isFinite(mark) && mark > 0){
        CC_SPREADS_BOOK_STATE.mark[role] = { symbol, price:mark, indexPrice:Number.isFinite(indexPrice) ? indexPrice : NaN, ts:Number.isFinite(ts) ? ts : Date.now() };
        const item = CC_SPREADS_BOOK_STATE.items[role];
        if(item && String(item.symbol || '').toUpperCase() === symbol){
          item.markPrice = mark;
          if(Number.isFinite(indexPrice)) item.indexPrice = indexPrice;
        }
        ccSpreadsScheduleModalBookRender(role);
        ccSpreadsScheduleLiveSpreadChartUpdate('futures-mark');
      }
      return;
    }
    if(dt === 33 || dt === 43){
      const rows = Array.isArray(msg && msg.d) ? msg.d : [msg && msg.d].filter(Boolean);
      const first = rows.filter(t => String(ccPick(t, ['s','symbol']) || '').trim() === symbol).sort((a,b) => Number(ccFuturesTradeTs(b) || 0) - Number(ccFuturesTradeTs(a) || 0))[0];
      if(first){
        const price = ccFuturesTradePrice(first), side = ccFuturesTradeSideRaw(first), ts = ccFuturesTradeTs(first);
        if(Number.isFinite(price)){
          CC_SPREADS_BOOK_STATE.lastTrade[role] = { symbol, price, side, ts:Number.isFinite(ts) ? ts : Date.now() };
          CC_SPREADS_BOOK_STATE.updated[role] = Number.isFinite(ts) ? ts : Date.now();
          CC_SPREADS_BOOK_STATE.status[role] = ccSpreadsBookFormatTime(CC_SPREADS_BOOK_STATE.updated[role]);
          ccSpreadsScheduleModalBookRender(role);
        }
      }
      return;
    }
    const data = msg && msg.d;
    if(!data || ![32,37,58].includes(dt)) return;
    if(String(data.s || '').trim().toUpperCase() !== symbol) return;
    CC_SPREADS_BOOK_STATE.raw[role] = { asks:Array.isArray(data.asks) ? data.asks : [], bids:Array.isArray(data.bids) ? data.bids : [], ts:data.ts, symbol };
    CC_SPREADS_BOOK_STATE.updated[role] = Number(data.ts) || Date.now();
    CC_SPREADS_BOOK_STATE.status[role] = ccSpreadsBookFormatTime(CC_SPREADS_BOOK_STATE.updated[role]);
    ccSpreadsScheduleModalBookRender(role);
  };
  ws.onerror = () => {
    if(CC_SPREADS_BOOK_STATE.sockets[role] !== ws) return;
    CC_SPREADS_BOOK_STATE.status[role] = 'Socket error';
    ccSpreadsRenderModalBook(role);
  };
  ws.onclose = () => {
    if(CC_SPREADS_BOOK_STATE.sockets[role] === ws) CC_SPREADS_BOOK_STATE.sockets[role] = null;
    ccSpreadsBookClearTimer('heartbeat', role);
    if(ccSpreadsTradeModalOpen() && CC_SPREADS_BOOK_STATE.symbols[role] === symbol){
      CC_SPREADS_BOOK_STATE.status[role] = 'Reconnecting';
      ccSpreadsRenderModalBook(role);
      ccSpreadsBookClearTimer('reconnect', role);
      CC_SPREADS_BOOK_STATE.reconnect[role] = setTimeout(() => ccSpreadsBookConnect(role, symbol), CC_BOOK_RECONNECT_MS);
    }
  };
}
function ccSpreadsEnsureModalBooks(row, col){
  const roles = ccSpreadsBookRoles(row, col);
  const activeRoles = new Set(roles.map(entry => entry.role));
  ['row','col','spot'].forEach(role => {
    if(activeRoles.has(role)) return;
    ccSpreadsBookClose(role, false);
    CC_SPREADS_BOOK_STATE.symbols[role] = '';
    CC_SPREADS_BOOK_STATE.types[role] = role === 'spot' ? 'spot' : 'futures';
    CC_SPREADS_BOOK_STATE.items[role] = null;
    CC_SPREADS_BOOK_STATE.labels[role] = role === 'spot' ? 'Spot' : 'Futures';
    CC_SPREADS_BOOK_STATE.raw[role] = null;
    CC_SPREADS_BOOK_STATE.status[role] = '';
    CC_SPREADS_BOOK_STATE.updated[role] = 0;
    if(CC_SPREADS_BOOK_STATE.lastTrade[role]) CC_SPREADS_BOOK_STATE.lastTrade[role] = { symbol:'', price:NaN, side:'', ts:0 };
    if(CC_SPREADS_BOOK_STATE.mark[role]) CC_SPREADS_BOOK_STATE.mark[role] = { symbol:'', price:NaN, indexPrice:NaN, ts:0 };
  });
  for(const entry of roles){
    const role = entry.role;
    const symbol = String(entry.item?.symbol || '').toUpperCase();
    CC_SPREADS_BOOK_STATE.types[role] = role === 'spot' ? 'spot' : 'futures';
    CC_SPREADS_BOOK_STATE.items[role] = entry.item || null;
    CC_SPREADS_BOOK_STATE.labels[role] = entry.label || (role === 'spot' ? 'Spot' : 'Futures');
    if(symbol) ccSpreadsBookConnect(role, symbol);
  }
  ccSpreadsRenderModalBooks();
}
function ccSpreadsFormatLeverage(value){
  const num = Number(value);
  if (!Number.isFinite(num) || num <= 0) return '—';
  return (Number.isInteger(num) ? String(num) : String(Number(num.toFixed(2)))) + 'x';
}
function ccSpreadsLeverageForRow(item){
  const accountId = ccSpreadsActiveAccountId();
  if (!accountId || !item?.symbol) return '—';
  return ccSpreadsFormatLeverage(CC_SPREADS_LEVERAGE_CACHE[ccSpreadsLeverageCacheKey(item.symbol, accountId)]);
}
function ccSpreadsPopupAsset(row, col){
  return ccSpreadsFutureBase(row) || ccSpreadsFutureBase(col) || ccSpreadsFutureBase(CC_SPREADS_BOOK_STATE.items.row) || ccSpreadsFutureBase(CC_SPREADS_BOOK_STATE.items.col) || 'BTC';
}
function ccSpreadsPositionExpiryShort(symbol){
  const text = String(symbol || '').toUpperCase();
  const m = text.match(/-(\d{1,2})([A-Z]{3})(\d{2})$/);
  return m ? String(m[1]).padStart(2,'0') + m[2] + m[3] : 'PERP';
}
function ccSpreadsPositionInstrument(symbol, asset){
  const key = String(symbol || '').toUpperCase();
  const state = ccSpreadsAssetState(asset);
  const all = [state.perp].concat(Array.isArray(state.columns) ? state.columns : []).filter(Boolean);
  return all.find(x => String(x.symbol || '').toUpperCase() === key) || null;
}
function ccSpreadsPositionCompactRow(p){
  const isGroup = ccFuturesIsGroup(p);
  const side = Number(ccPick(p,['tradeSide','side']));
  const delta = Number(ccPick(p,['delta']));
  const price = Number(ccPick(p,['lastPrice','markPrice']));
  const qty = Number(ccPick(p,['qty']));
  const deltaText = Number.isFinite(delta) ? (isGroup ? ccFmtDynamic(delta) : ccSignedAmount(Math.abs(delta), side, 5)) : '--';
  const cell = (value, formatter) => ccEsc(formatter ? formatter(value) : value);
  const avgCell = isGroup ? '' : cell(ccPick(p,['avgPrice']), v => Number.isFinite(Number(v)) ? ccFmtNumber(v,2) : '--');
  const markCell = isGroup ? '' : cell(ccPick(p,['markPrice']), v => Number.isFinite(Number(v)) ? ccFmtDynamic(v) : '--');
  const elpCell = isGroup ? '' : cell(ccPick(p,['elp']), v => Number.isFinite(Number(v)) ? ccFmtNumber(v,2) : '--');
  const amountCell = isGroup ? '<td></td>' : ccFuturesPositionAmountCell(p);
  return '<tr' + (isGroup ? ' data-cc-futures-group-row="1"' : '') + '>' +
    ccFuturesPositionSymbolCell(p) +
    amountCell +
    '<td>' + avgCell + '</td>' +
    '<td>' + markCell + '</td>' +
    '<td>' + elpCell + '</td>' +
    ccFuturesPositionPnlCell(p) +
    '<td>' + cell(ccPick(p,['initMargin']), v => Number.isFinite(Number(v)) ? ccFmtNumber(v,2) : '--') + '</td>' +
    '<td>' + cell(ccPick(p,['maintMargin']), v => Number.isFinite(Number(v)) ? ccFmtNumber(v,2) : '--') + '</td>' +
    '<td>' + ccEsc(deltaText) + '</td>' +
  '</tr>';
}
function ccSpreadsSpotPositionUsdValue(r){
  const raw = r && r.raw ? r.raw : {};
  const direct = ccFindNumericDeep(raw, ['usdtvalue','usdvalue','dollarvalue','valueusd','valueusdt','valuation','equityusd']);
  if(Number.isFinite(direct)) return direct;
  const ccy = String(r && r.ccy || '').toUpperCase();
  const qty = ccRowEquityValue(r);
  if(!Number.isFinite(qty)) return NaN;
  if(ccy === 'USD' || ccy === 'USDT' || ccy === 'USDC') return qty;
  const mark = ccFuturesInstrumentMark(ccFuturesBookSymbolKey(ccy + 'USD'));
  return Number.isFinite(mark) && mark > 0 ? qty * mark : NaN;
}
function ccSpreadsSpotPositionCompactRow(r){
  const ccy = String(r && r.ccy || '').toUpperCase();
  const qty = ccRowEquityValue(r);
  const valueUsd = ccSpreadsSpotPositionUsdValue(r);
  const amount = Number.isFinite(qty) ? ccSpreadsPopupSpotAmountText(qty, ccy) + ' ' + ccy : '--';
  const sub = Number.isFinite(valueUsd) ? ccFmtNumber(valueUsd, 2) + ' USD' : '--';
  const delta = Number.isFinite(qty) ? (qty >= 0 ? '+' : '-') + ccSpreadsPopupSpotAmountText(Math.abs(qty), ccy) : '--';
  return '<tr><td><span style="display:block;padding-left:18px">Spot ' + ccEsc(ccy) + '</span><span class="sub" style="display:block;padding-left:18px">Balance</span></td>' +
    '<td><span>' + ccEsc(amount) + '</span><span class="sub">' + ccEsc(sub) + '</span></td>' +
    '<td>--</td><td>--</td><td>--</td><td>--</td><td>--</td><td>--</td><td>' + ccEsc(delta) + '</td></tr>';
}
function ccSpreadsSignedPopupAmountText(value, asset){const n=Number(value);if(!Number.isFinite(n))return '--';return (n>=0?'+':'-') + ccSpreadsPopupSpotAmountText(Math.abs(n), asset)}
function ccSpreadsPositionTotalRow(asset, futuresPositions, spotItems){
  const futuresAmount = ccFuturesSum(futuresPositions, p => ccFuturesSignedQty(p));
  const futuresDelta = ccFuturesSum(futuresPositions, p => { const d = Number(ccPick(p,['delta'])); return Number.isFinite(d) ? d : ccFuturesSignedQty(p); });
  const spotAmount = ccFuturesSum(spotItems, r => ccRowEquityValue(r));
  const totalAmount = (Number.isFinite(futuresAmount) ? futuresAmount : 0) + (Number.isFinite(spotAmount) ? spotAmount : 0);
  const totalDelta = (Number.isFinite(futuresDelta) ? futuresDelta : 0) + (Number.isFinite(spotAmount) ? spotAmount : 0);
  return '<tr data-cc-futures-group-row="1"><td><span>' + ccEsc(asset) + '</span></td>' +
    '<td><span>' + ccEsc(ccSpreadsSignedPopupAmountText(totalAmount, asset) + ' ' + asset) + '</span></td>' +
    '<td></td><td></td><td></td><td></td><td></td><td></td><td>' + ccEsc(ccSpreadsSignedPopupAmountText(totalDelta, asset)) + '</td></tr>';
}
function ccSpreadsPositionRowKey(p, idx){
  if(ccFuturesIsGroup(p)) return 'group:' + ccFuturesGroupKey(p);
  const id = ccPick(p, ['positionId','posId','id','position_id']);
  if(id && id !== '—') return 'posid:' + String(id);
  const symbol = ccPick(p, ['displayName','symbol','instrument','instId']) || 'position';
  const side = ccPick(p, ['tradeSide','side']) || '';
  const child = p && p.__childOf ? String(p.__childOf) : '';
  return 'pos:' + String(symbol).toUpperCase() + ':' + String(side).toUpperCase() + ':' + child + ':' + idx;
}
function ccSpreadsRowWithKeyHtml(key, html){
  return String(html || '').replace(/^<tr(\s|>)/, '<tr data-cc-spreads-row-key="' + ccEsc(key) + '"$1');
}
function ccSpreadsPatchAttrs(dst, src, keepKey){
  if(!dst || !src || dst.nodeType !== 1 || src.nodeType !== 1) return;
  [...dst.attributes].forEach(attr => { if(!(keepKey && attr.name === 'data-cc-spreads-row-key') && !src.hasAttribute(attr.name)) dst.removeAttribute(attr.name); });
  [...src.attributes].forEach(attr => { if(!(keepKey && attr.name === 'data-cc-spreads-row-key') && dst.getAttribute(attr.name) !== attr.value) dst.setAttribute(attr.name, attr.value); });
}
function ccSpreadsPatchNode(dst, src, keepKey){
  if(!dst || !src) return;
  if(dst.nodeType === 3 && src.nodeType === 3){ if(dst.nodeValue !== src.nodeValue) dst.nodeValue = src.nodeValue; return; }
  if(dst.nodeType !== src.nodeType || (dst.nodeType === 1 && dst.tagName !== src.tagName)){ dst.replaceWith(src.cloneNode(true)); return; }
  if(dst.nodeType !== 1) return;
  ccSpreadsPatchAttrs(dst, src, keepKey);
  const dc = [...dst.childNodes], sc = [...src.childNodes];
  const sameShape = dc.length === sc.length && dc.every((n,i) => n.nodeType === sc[i].nodeType && (n.nodeType !== 1 || n.tagName === sc[i].tagName));
  if(!sameShape){ dst.replaceChildren(...sc.map(n => n.cloneNode(true))); return; }
  sc.forEach((n,i) => ccSpreadsPatchNode(dc[i], n, false));
}
function ccSpreadsRowsFromHtml(rows){
  const tpl = document.createElement('template');
  tpl.innerHTML = '<table><tbody>' + rows.map(r => r.html).join('') + '</tbody></table>';
  return [...tpl.content.querySelectorAll('tbody tr')].map((tr,i) => ({ key: rows[i]?.key || tr.getAttribute('data-cc-spreads-row-key') || String(i), node: tr }));
}
function ccSpreadsPatchTable(panel, tableClass, wrapClass, headers, rows, emptyText){
  if(!panel) return;
  let wrap = panel.querySelector('.' + wrapClass), table = wrap ? wrap.querySelector('table') : null, tbody = table ? table.querySelector('tbody') : null;
  if(!wrap || !table || !tbody){
    panel.innerHTML = '<div class="' + wrapClass + '"><table class="' + tableClass + '"><thead><tr>' + headers.map(h => '<th>' + ccEsc(h) + '</th>').join('') + '</tr></thead><tbody></tbody></table></div>';
    wrap = panel.querySelector('.' + wrapClass); table = wrap.querySelector('table'); tbody = table.querySelector('tbody');
  }
  if(!rows.length){
    const key = 'empty';
    const html = '<tr data-cc-spreads-row-key="' + key + '"><td class="muted" colspan="' + headers.length + '">' + ccEsc(emptyText) + '</td></tr>';
    ccSpreadsPatchTable(panel, tableClass, wrapClass, headers, [{key, html}], emptyText);
    return;
  }
  const desired = ccSpreadsRowsFromHtml(rows);
  const current = new Map([...tbody.querySelectorAll('tr[data-cc-spreads-row-key]')].map(tr => [tr.getAttribute('data-cc-spreads-row-key'), tr]));
  const seen = new Set();
  desired.forEach((row, i) => {
    seen.add(row.key);
    let tr = current.get(row.key);
    if(tr){
      ccSpreadsPatchNode(tr, row.node, true);
    }else{
      tr = row.node.cloneNode(true);
    }
    const ref = tbody.children[i] || null;
    if(ref !== tr) tbody.insertBefore(tr, ref);
  });
  [...tbody.querySelectorAll('tr[data-cc-spreads-row-key]')].forEach(tr => { if(!seen.has(tr.getAttribute('data-cc-spreads-row-key'))) tr.remove(); });
}
function ccSpreadsPopupPositionSummaryRows(row, col){
  const asset = ccSpreadsPopupAsset(row, col);
  const positions = (Array.isArray(ccLastFuturesPositions) ? ccLastFuturesPositions : []).filter(p => {
    if(!p || typeof p !== 'object' || Array.isArray(p)) return false;
    const qty = ccFuturesSignedQty(p);
    if(!Number.isFinite(qty) || Math.abs(qty) <= 0) return false;
    const base = String(ccOrderBaseAsset(p) || '').toUpperCase();
    return base === asset;
  });
  const spotItems = ccBalanceRows(ccLastAssetsSummary || {}).filter(r => {
    const ccy = String(r && r.ccy || '').toUpperCase();
    const qty = ccRowEquityValue(r);
    const valueUsd = ccSpreadsSpotPositionUsdValue(r);
    return ccy === asset && Number.isFinite(qty) && Math.abs(qty) > 0 && Number.isFinite(valueUsd) && Math.abs(valueUsd) >= 1;
  });
  const rows = [];
  if(positions.length || spotItems.length) rows.push({key:'total:' + asset, html:ccSpreadsPositionTotalRow(asset, positions, spotItems)});
  ccFuturesFlatRows(positions).filter(p => !ccFuturesIsGroup(p)).forEach((p,i) => rows.push({key:'futures:' + ccSpreadsPositionRowKey(p,i), html:ccSpreadsPositionCompactRow(p)}));
  spotItems.forEach(r => rows.push({key:'spot:' + String(r && r.ccy || '').toUpperCase(), html:ccSpreadsSpotPositionCompactRow(r)}));
  return { asset, rows };
}
function ccSpreadsPopupPositionsSummaryHtml(row, col){
  const data = ccSpreadsPopupPositionSummaryRows(row, col);
  const headers = ['Symbol','Amount','Entry Price','Mark Price','ELP','PnL (ROI)','IM','MM','Delta'];
  const body = data.rows.length ? data.rows.map(r => ccSpreadsRowWithKeyHtml(r.key, r.html)).join('') : '<tr data-cc-spreads-row-key="empty"><td class="muted" colspan="9">No ' + ccEsc(data.asset) + ' futures or spot positions.</td></tr>';
  return '<div class="okx-nitro-spread-positions-table-wrap"><table class="okx-nitro-spread-positions-table"><thead><tr>' + headers.map(h => '<th>' + ccEsc(h) + '</th>').join('') + '</tr></thead><tbody>' + body + '</tbody></table></div>';
}
function ccRenderSpreadsPositionSummary(){
  const host = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-position-summary]');
  if(!host) return;
  const data = ccSpreadsPopupPositionSummaryRows(CC_SPREADS_BOOK_STATE.items.row, CC_SPREADS_BOOK_STATE.items.col);
  const headers = ['Symbol','Amount','Entry Price','Mark Price','ELP','PnL (ROI)','IM','MM','Delta'];
  ccSpreadsPatchTable(host, 'okx-nitro-spread-positions-table', 'okx-nitro-spread-positions-table-wrap', headers, data.rows.map(r => ({ key:r.key, html:ccSpreadsRowWithKeyHtml(r.key, r.html) })), 'No ' + data.asset + ' futures or spot positions.');
}
function ccSpreadsAssetState(asset){
  return CC_SPREADS_HEADER_DATA[asset] || CC_SPREADS_HEADER_DATA.BTC || { columns: [], perp: null };
}

let ccSpreadsPrivateRefreshTimer=null;
let ccSpreadsPrivateRefreshBusy=false;
let ccSpreadsPrivateRefreshLastAt=0;
let ccSpreadsPrivateSlowRefreshLastAt=0;
const CC_SPREADS_PRIVATE_REFRESH_MS=5000;
const CC_SPREADS_PRIVATE_SLOW_REFRESH_MS=30000;
function ccSpreadsInstrumentSymbols(){
  const out=[];
  ['spot','row','col'].forEach(role=>{const symbol=String(CC_SPREADS_BOOK_STATE.symbols[role]||CC_SPREADS_BOOK_STATE.items[role]?.symbol||'').toUpperCase();if(symbol)out.push({role,type:role==='spot'?'spot':'futures',symbol})});
  return out;
}
function ccSpreadsNormalizeSymbol(value){return String(value||'').toUpperCase().replace(/[^A-Z0-9]/g,'')}
function ccSpreadsRowSymbolCandidates(row){return ['displaySymbol','displayName','symbol','instId','instrument','ticker_id','baseToken','base_currency'].map(k=>String(ccPick(row,[k])||'').toUpperCase()).filter(v=>v&&v!=='—')}
function ccSpreadsSymbolMatches(row,symbols,market){const targets=(Array.isArray(symbols)?symbols:[]).map(ccSpreadsNormalizeSymbol).filter(Boolean);if(!targets.length)return false;const values=ccSpreadsRowSymbolCandidates(row).map(ccSpreadsNormalizeSymbol).filter(Boolean);if(values.some(v=>targets.includes(v)))return true;if(market==='spot')return values.some(v=>targets.some(t=>v===t||v.replace(/USDT$/,'')===t.replace(/USDT$/,'')));return values.some(v=>targets.some(t=>v===t||ccFuturesBookSymbolKey(v)===ccFuturesBookSymbolKey(t)&&(/PERP|PERPETUAL/.test(v+t)||!ccSpreadsParseSymbolExpiry(t))));}
function ccSpreadsPopupSpotSymbols(){return ccSpreadsInstrumentSymbols().filter(x=>x.type==='spot').map(x=>x.symbol)}
function ccSpreadsPopupFuturesSymbols(){return ccSpreadsInstrumentSymbols().filter(x=>x.type==='futures').map(x=>x.symbol)}
function ccSpreadsPopupRows(){
  const spotSymbols=ccSpreadsPopupSpotSymbols(), futuresSymbols=ccSpreadsPopupFuturesSymbols();
  const rows=[];
  (Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[]).filter(p=>ccSpreadsSymbolMatches(p,futuresSymbols,'futures')).forEach(p=>rows.push({market:'Futures',type:'Position',time:ccFormatOrderTime(ccPick(p,['updateTime','createTime','time'])),symbol:ccFuturesCanonicalDisplaySymbolFromOrder(p)||ccPick(p,['displayName','symbol','instrument']),side:ccOrderSide(p),amount:ccFuturesPositionAmountCell(p).replace(/^<td>|<\/td>$/g,''),price:ccFmtDynamic(ccPick(p,['markPrice','lastPrice','avgPrice'])),value:ccFuturesPositionPnlCell(p).replace(/^<td>|<\/td>$/g,''),status:'Open'}));
  (Array.isArray(ccLastFuturesOpenOrders)?ccLastFuturesOpenOrders:[]).forEach((o,i)=>{if(!ccSpreadsRenderableFuturesOrder(o)||!ccSpreadsSymbolMatches(o,futuresSymbols,'futures'))return;const action=ccFuturesCancelActionHtml(o,i,'"');rows.push({market:'Futures',type:'Order',time:ccFormatOrderTime(ccPick(o,['ts','createTime','createdTime','time','updateTime'])),symbol:ccFuturesCanonicalDisplaySymbolFromOrder(o)||ccFuturesCanonicalDisplaySymbol(ccPick(o,['displaySymbol','displayName','symbol','instId','instrument'])),side:ccOrderSide(o),amount:ccSpreadsPopupFuturesOrderAmountInline(o),price:ccSpreadsPopupFuturesOrderAvgPriceInline(o),value:ccEsc(ccOrderValue(o)),status:ccEsc(ccFuturesOrderTypeLabel(o)),action})});
  (Array.isArray(ccLastSpotOpenOrders)?ccLastSpotOpenOrders:[]).forEach((o,i)=>{if(!ccSpreadsSymbolMatches(o,spotSymbols,'spot'))return;const filled=ccPick(o,['fillQty','filledQty','filledQuantity','filled']);const amount=ccPick(o,['qty','quantity','amount','remainQty']);const action=ccSpotOrderDisplayId(o)?'<button type="button" class="link-btn cc-spot-cancel-order" data-cc-spot-cancel-index="'+i+'">Cancel</button>':'<span class="muted">—</span>';rows.push({market:'Spot',type:'Order',time:ccFormatOrderTime(ccPick(o,['ts','createTime','createdTime','time','updateTime'])),symbol:ccPick(o,['displaySymbol','symbol','instId']),side:ccOrderSide(o),amount:ccEsc(ccSpreadsPopupSpotAmountText(filled,ccOrderBaseAsset(o)))+' | '+ccEsc(ccSpreadsPopupSpotAmountText(amount,ccOrderBaseAsset(o))),price:ccEsc(ccPick(o,['price','px'])),value:ccEsc(ccOrderValue(o)),status:ccEsc(ccSpotOrderTypeLabel(o)),action})});
  const parts=ccSpreadsSpotParts(spotSymbols[0]||'');
  const wanted=new Set([parts.base,parts.quote].filter(Boolean).map(x=>String(x).toUpperCase()));
  ccBalanceRows(ccLastAssetsSummary||{}).filter(ccIsNonZeroTradingBalance).filter(r=>wanted.has(String(r.ccy||'').toUpperCase())).forEach(r=>{const equity=ccRowEquityValue(r),available=ccCoincallAssetAvailableValue(r);rows.push({market:'Spot',type:'Balance',time:'—',symbol:r.ccy,side:'—',amount:ccEsc(ccMoney(equity,r.ccy)),price:'—',value:'<span>'+ccEsc(ccMoney(available,r.ccy))+'</span><span class="sub">Available</span>',status:'Trading'})});
  return rows;
}
function ccSpreadsPopupMixedRowsTable(rows,emptyText){const headers=['Market','Time','Symbol','Side','Filled | Amount','Average | Price','Filled | Value','Type','Actions'];const colgroup='<colgroup><col style="width:54px"><col style="width:82px"><col style="width:108px"><col style="width:42px"><col style="width:154px"><col style="width:112px"><col style="width:86px"><col style="width:82px"><col style="width:62px"></colgroup>';if(!rows.length)return '<div class="okx-spot-orders-wrap"><table class="okx-spot-orders-table cc-spreads-popup-orders-table">'+colgroup+'<thead><tr>'+headers.map(h=>'<th>'+ccEsc(h)+'</th>').join('')+'</tr></thead><tbody><tr><td class="muted" colspan="'+headers.length+'">'+ccEsc(emptyText)+'</td></tr></tbody></table></div>';return '<div class="okx-spot-orders-wrap"><table class="okx-spot-orders-table cc-spreads-popup-orders-table">'+colgroup+'<thead><tr>'+headers.map(h=>'<th>'+ccEsc(h)+'</th>').join('')+'</tr></thead><tbody>'+rows.map(r=>'<tr><td class="cc-spreads-pos-market">'+ccEsc(r.market)+'</td><td>'+ccEsc(r.time)+'</td><td>'+ccEsc(r.symbol)+'</td><td class="'+(String(r.side).toLowerCase()==='buy'?'buy':String(r.side).toLowerCase()==='sell'?'sell':'')+'">'+ccEsc(r.side)+'</td><td><span>'+r.amount+'</span></td><td><span class="cc-spreads-order-price">'+r.price+'</span></td><td><span>'+r.value+'</span></td><td>'+r.status+'</td><td>'+(r.action||'<span class="muted">—</span>')+'</td></tr>').join('')+'</tbody></table></div>'}
const CC_SPREADS_SIDE_ORDER_HEADERS=['Symbol','Side','Filled | Amount','Average | Price','Filled | Value','Type','Actions'];
function ccSpreadsSideOrderRowHtml(r){return '<tr><td>'+ccEsc(r.symbol)+'</td><td class="'+(String(r.side).toLowerCase()==='buy'?'buy':String(r.side).toLowerCase()==='sell'?'sell':'')+'">'+ccEsc(r.side)+'</td><td><span>'+r.amount+'</span></td><td><span class="cc-spreads-order-price">'+r.price+'</span></td><td><span>'+r.value+'</span></td><td>'+r.status+'</td><td>'+(r.action||'<span class="muted">—</span>')+'</td></tr>'}
function ccSpreadsSideOrdersTbodyHtml(rows){return rows.length?rows.map(ccSpreadsSideOrderRowHtml).join(''):'<tr><td class="muted" colspan="'+CC_SPREADS_SIDE_ORDER_HEADERS.length+'">'+(ccFuturesOpenOrdersLoaded?'No CoinCall open orders returned.':'Loading CoinCall open orders...')+'</td></tr>'}
function ccSpreadsSideOrdersTableHtml(){const rows=ccSpreadsPopupOpenOrders();const colgroup='<colgroup><col style="width:90px"><col style="width:42px"><col style="width:130px"><col style="width:90px"><col style="width:80px"><col style="width:60px"><col style="width:60px"></colgroup>';return '<div class="okx-spot-orders-wrap"><table class="okx-spot-orders-table cc-spreads-side-orders-table">'+colgroup+'<thead><tr>'+CC_SPREADS_SIDE_ORDER_HEADERS.map(h=>'<th>'+ccEsc(h)+'</th>').join('')+'</tr></thead><tbody>'+ccSpreadsSideOrdersTbodyHtml(rows)+'</tbody></table></div>'}
function ccRenderSpreadsSideOrdersTable(){const host=document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-side-orders]');if(!host)return;const rows=ccSpreadsPopupOpenOrders();const table=host.querySelector('.cc-spreads-side-orders-table');const tbody=table&&table.querySelector('tbody');if(!table||!tbody){host.innerHTML=ccSpreadsSideOrdersTableHtml();return}const next=ccSpreadsSideOrdersTbodyHtml(rows);if(tbody.dataset.ccLastHtml!==next){tbody.innerHTML=next;tbody.dataset.ccLastHtml=next}}
function ccSpreadsResetFundingHistoryPreload(){ccLastSpreadsFundingRows=[];ccSpreadsFundingHistoryLoaded=false}
async function ccSpreadsPreloadFundingHistory(accountQs){const symbols=ccSpreadsPopupFuturesSymbols();if(!symbols.length){ccSpreadsFundingHistoryLoaded=true;return}const fundingResults=await Promise.allSettled(symbols.map(symbol=>ccApi('/api/admin/coincall/futures/funding/history'+(accountQs?accountQs+'&':'?')+'symbol='+encodeURIComponent(symbol)+'&pageSize=50')));ccLastSpreadsFundingRows=fundingResults.flatMap(r=>r.status==='fulfilled'?ccCoincallFundingRowsFrom(r.value):[]);ccSpreadsFundingHistoryLoaded=true}
function ccSpreadsPopupFuturesPositions(){return Array.isArray(ccLastFuturesPositions)?ccLastFuturesPositions:[]}
function ccSpreadsPositionTableRows(){return ccFuturesFlatRows(ccSpreadsPopupFuturesPositions()).map((p,i)=>({key:ccSpreadsPositionRowKey(p,i),html:ccSpreadsRowWithKeyHtml(ccSpreadsPositionRowKey(p,i),ccFuturesPositionRow(p))}))}
const CC_SPREADS_POSITION_HEADERS=['Symbol','Amount','Entry Price','Mark Price','ELP','PnL (ROI)','IM','MM','Delta','Gamma','Vega','Theta','Rho','Reverse','Close Positions','Price','Amount','TP/SL'];
function ccSpreadsPositionsTableHtml(rows){return ccBottomTableAlways(CC_SPREADS_POSITION_HEADERS,(rows||ccSpreadsPositionTableRows()).map(r=>r.html),'No CoinCall futures positions returned.',CC_SPREADS_POSITION_HEADERS.length)}
function ccSpreadsDisplayedOrderPriceSortValue(row){const raw=String(row&&row.price!=null?row.price:'').replace(/<[^>]*>/g,' ').split('|').pop().replace(/,/g,'').trim();const n=Number(raw);return Number.isFinite(n)?n:Number.NEGATIVE_INFINITY}
function ccSpreadsPopupOpenOrders(){return ccSpreadsPopupRows().filter(r=>r.type==='Order').sort((a,b)=>ccSpreadsDisplayedOrderPriceSortValue(b)-ccSpreadsDisplayedOrderPriceSortValue(a))}
function ccSpreadsOrderTimeMs(o){const raw=ccPick(o,['ts','createTime','createdTime','time','updateTime']);const n=Number(raw);if(Number.isFinite(n)&&n>0)return n<1e12?n*1000:n;const text=ccFirst(raw);if(!text||text==='—')return 0;const d=new Date(typeof text==='string'&&!/[zZ]$/.test(text)?text+'Z':text);const t=d.getTime();return Number.isFinite(t)?t:0}
function ccSpreadsPopupFuturesOrderHistory(){return (Array.isArray(ccLastFuturesOrderHistory)?ccLastFuturesOrderHistory:[]).slice().sort((a,b)=>ccSpreadsOrderTimeMs(b)-ccSpreadsOrderTimeMs(a))}
function ccSpreadsPopupSpotTradeHistory(){return Array.isArray(ccLastSpotTradeHistory)?ccLastSpotTradeHistory:[]}
function ccSpreadsTradeHistorySortMs(row){return ccTradeHistoryTimeMs(row)}
const CC_SPREADS_TRADE_HISTORY_PAGE_SIZE=8;let ccSpreadsTradeHistoryPage=1;
function ccSpreadsPopupTradeHistoryRows(){const rows=[];ccSpreadsPopupFuturesTradeHistory().forEach(r=>rows.push({market:'futures',time:ccSpreadsTradeHistorySortMs(r),html:ccFuturesTradeHistoryRow(r)}));ccSpreadsPopupSpotTradeHistory().forEach(r=>rows.push({market:'spot',time:ccSpreadsTradeHistorySortMs(r),html:ccSpreadsSpotTradeHistoryRow(r)}));return rows.sort((a,b)=>(b.time||0)-(a.time||0)).map(r=>r.html)}
function ccSpreadsTradeHistoryPageCount(total){return Math.max(1,Math.ceil((Number(total)||0)/CC_SPREADS_TRADE_HISTORY_PAGE_SIZE))}
function ccSpreadsClampTradeHistoryPage(total){ccSpreadsTradeHistoryPage=Math.min(Math.max(1,ccSpreadsTradeHistoryPage||1),ccSpreadsTradeHistoryPageCount(total));return ccSpreadsTradeHistoryPage}
const CC_SPREADS_TRADE_HISTORY_HEADERS=['Time','Symbol','Side','Amount','Value','Filled Price','Mark Price','Index','Fees','Realized PnL','Role','Order ID｜Trade ID'];
function ccSpreadsTradeHistoryPagerHtml(total,active){const page=ccSpreadsClampTradeHistoryPage(total),from=(page-1)*CC_SPREADS_TRADE_HISTORY_PAGE_SIZE,to=from+CC_SPREADS_TRADE_HISTORY_PAGE_SIZE,pages=ccSpreadsTradeHistoryPageCount(total);return '<div class="cc-spreads-trade-history-pager"'+(active?'':' hidden')+'><button type="button" data-cc-spreads-trade-history-page="prev"'+(page<=1?' disabled':'')+'>Prev</button><span>'+ccEsc(total?((from+1)+'-'+Math.min(to,total)+' / '+total):'0 / 0')+'</span><button type="button" data-cc-spreads-trade-history-page="next"'+(page>=pages?' disabled':'')+'>Next</button></div>'}
function ccSpreadsTradeHistoryTableHtml(rows){const total=rows.length,page=ccSpreadsClampTradeHistoryPage(total),from=(page-1)*CC_SPREADS_TRADE_HISTORY_PAGE_SIZE,to=from+CC_SPREADS_TRADE_HISTORY_PAGE_SIZE,pageRows=rows.slice(from,to);const body=pageRows.length?pageRows.join(''):'<tr><td class="muted" colspan="12">No CoinCall spot/futures trade history returned.</td></tr>';return '<div class="okx-spot-orders-wrap"><table class="okx-spot-orders-table cc-spreads-trade-history-table"><colgroup><col style="width:112px"><col style="width:98px"><col style="width:52px"><col style="width:90px"><col style="width:90px"><col style="width:86px"><col style="width:82px"><col style="width:82px"><col style="width:72px"><col style="width:86px"><col style="width:58px"><col style="width:150px"></colgroup><thead><tr>'+CC_SPREADS_TRADE_HISTORY_HEADERS.map(h=>'<th>'+ccEsc(h)+'</th>').join('')+'</tr></thead><tbody>'+body+'</tbody></table></div>'}
function ccSpreadsTradeHistorySignature(rows){const total=rows.length,page=ccSpreadsClampTradeHistoryPage(total),from=(page-1)*CC_SPREADS_TRADE_HISTORY_PAGE_SIZE,to=from+CC_SPREADS_TRADE_HISTORY_PAGE_SIZE;return page+'|'+total+'|'+rows.slice(from,to).join('')}
function ccSpreadsPatchAttrs(dst,src){if(!dst||!src)return;[...dst.attributes].forEach(a=>{if(!src.hasAttribute(a.name))dst.removeAttribute(a.name)});[...src.attributes].forEach(a=>{if(dst.getAttribute(a.name)!==a.value)dst.setAttribute(a.name,a.value)})}
function ccSpreadsPatchElement(dst,src){if(!dst||!src)return false;if(dst.nodeName!==src.nodeName)return false;ccSpreadsPatchAttrs(dst,src);if(!dst.children.length&&!src.children.length){if(dst.textContent!==src.textContent)dst.textContent=src.textContent;return true}if(dst.innerHTML!==src.innerHTML)dst.innerHTML=src.innerHTML;return true}
function ccSpreadsPatchTableDom(dstTable,srcTable){if(!dstTable||!srcTable)return false;ccSpreadsPatchAttrs(dstTable,srcTable);const dstCols=dstTable.querySelector('colgroup'),srcCols=srcTable.querySelector('colgroup');if(dstCols&&srcCols&&dstCols.innerHTML!==srcCols.innerHTML)dstCols.innerHTML=srcCols.innerHTML;const dstHead=dstTable.tHead,srcHead=srcTable.tHead;if(dstHead&&srcHead&&dstHead.innerHTML!==srcHead.innerHTML)dstHead.innerHTML=srcHead.innerHTML;const dstBody=dstTable.tBodies[0],srcBody=srcTable.tBodies[0];if(!dstBody||!srcBody)return false;const srcRows=[...srcBody.rows];while(dstBody.rows.length>srcRows.length)dstBody.deleteRow(dstBody.rows.length-1);srcRows.forEach((srcRow,i)=>{let dstRow=dstBody.rows[i];if(!dstRow){dstBody.appendChild(srcRow.cloneNode(true));return}ccSpreadsPatchAttrs(dstRow,srcRow);const srcCells=[...srcRow.cells];while(dstRow.cells.length>srcCells.length)dstRow.deleteCell(dstRow.cells.length-1);srcCells.forEach((srcCell,j)=>{let dstCell=dstRow.cells[j];if(!dstCell){dstRow.appendChild(srcCell.cloneNode(true));return}ccSpreadsPatchElement(dstCell,srcCell)})});return true}
function ccSpreadsPatchPanelHtml(panel,html){const sig=String(html);if(panel.dataset.ccPanelHtmlSig===sig)return false;const tpl=document.createElement('template');tpl.innerHTML=sig;const src=tpl.content.firstElementChild;if(!src){if(panel.innerHTML!==sig)panel.innerHTML=sig;panel.dataset.ccPanelHtmlSig=sig;return true}const dst=panel.firstElementChild;if(dst&&dst.className===src.className&&dst.nodeName===src.nodeName){ccSpreadsPatchAttrs(dst,src);const dstTable=dst.querySelector('table'),srcTable=src.querySelector('table');if(dstTable&&srcTable&&ccSpreadsPatchTableDom(dstTable,srcTable)){panel.dataset.ccPanelHtmlSig=sig;return true}}if(panel.innerHTML!==sig)panel.innerHTML=sig;panel.dataset.ccPanelHtmlSig=sig;return true}
function ccSpreadsPatchTradeHistoryPanel(panel,rows,active){const sig=ccSpreadsTradeHistorySignature(rows);panel.hidden=!active;if(panel.dataset.ccTradeHistorySig===sig&&panel.querySelector('.cc-spreads-trade-history-table'))return;panel.dataset.ccTradeHistorySig=sig;ccSpreadsPatchPanelHtml(panel,ccSpreadsTradeHistoryTableHtml(rows))}
function ccSpreadsOrderHistoryTableHtml(rows){return ccBottomTableAlways(['Time','Symbol','Side','Average｜Price','Filled｜Amount','Status','Order ID','Fees','Realized PnL','Type','Reduce Only','Trigger Conditions','TIF'],rows,'No CoinCall futures order history returned.',13)}
function ccSpreadsOrderHistorySignature(rows){return rows.length+'|'+rows.join('')}
function ccSpreadsPatchOrderHistoryPanel(panel,rows,active){const sig=ccSpreadsOrderHistorySignature(rows);panel.hidden=!active;if(panel.dataset.ccOrderHistorySig===sig&&panel.querySelector('.okx-spot-orders-table'))return;panel.dataset.ccOrderHistorySig=sig;ccSpreadsPatchPanelHtml(panel,ccSpreadsOrderHistoryTableHtml(rows))}
function ccSpreadsPatchHtmlPanel(panel,name,html,active,scrollState,restoreScroll){panel.hidden=name!==active;ccSpreadsPatchPanelHtml(panel,html);if(restoreScroll)restoreScroll(panel,scrollState&&scrollState[name])}
function ccSpreadsFundingTableHtml(){const rows=(Array.isArray(ccLastSpreadsFundingRows)?ccLastSpreadsFundingRows:[]).map(ccFuturesFundingRowHtml);return ccBottomTableAlways(['Time','Symbol','Side','Amount','Funding','Fees','Funding Rate'],rows,'No CoinCall futures funding history returned.',7)}
function ccSpreadsSpotTradeHistoryRow(t){const qty=Number(ccPick(t,['qty','quantity','amount','fillQty','filledQty']));const px=ccSpotTradePrice(t);const symbol=ccPick(t,['displaySymbol','symbol','instId']);const symbolText=symbol&&symbol!=='—'?symbol:(cc('ccSpotInst')?.value||'—');const base=ccOrderBaseAsset(t)||ccSpreadsSpotParts(symbolText).base||ccSpotParts().base;const quote=ccSpreadsSpotParts(symbolText).quote||ccSpotParts().quote||'USDT';const value=Number.isFinite(qty)&&Number.isFinite(px)?ccMoney(qty*px,quote):ccFirst(ccPick(t,['value','filledValue','quoteQty','quoteAmount']));const amount=Number.isFinite(qty)?ccAssetAmountText(qty,base):ccFirst(ccPick(t,['qty','quantity','amount','fillQty','filledQty']));const orderId=ccPick(t,['orderId','ordId','clientOrderId']);const tradeId=ccPick(t,['tradeId','dealId','fillId','id']);const role=Number(ccPick(t,['isTaker']))===1?'Taker':(Number(ccPick(t,['isTaker']))===0?'Maker':ccFirst(ccPick(t,['role','liquidity'])));const priceText=Number.isFinite(px)?ccFmt(px,px>100?2:6):ccFirst(ccPick(t,['price','fillPrice','filledPrice','px']));return `<tr${ccTradeHistoryStaleClass(t)}><td>${ccEsc(ccFormatOrderTime(ccPick(t,['time','ts','createTime','createdTime','updateTime'])))}</td><td>${ccEsc(symbolText)}</td><td>${ccTradeHistorySideHtml(t)}</td><td>${ccEsc(amount)}</td><td>${ccEsc(value)}</td><td>${ccEsc(priceText)}</td><td>—</td><td>—</td><td>${ccEsc(ccTradeFeeText(t))}</td><td>—</td><td>${ccEsc(role||'—')}</td><td>${ccEsc(orderId)}｜${ccEsc(tradeId)}</td></tr>`}
function ccSpreadsPopupFuturesTradeHistory(){return Array.isArray(ccLastFuturesTradeHistory)?ccLastFuturesTradeHistory:[]}
function ccSpreadsPopupAssetRows(){return ccBalanceRows(ccLastAssetsSummary||{}).filter(ccIsNonZeroTradingBalance).map(r=>{const equity=ccRowEquityValue(r),available=ccCoincallAssetAvailableValue(r);return '<tr><td>'+ccEsc(r.ccy)+'</td><td>'+ccEsc(ccMoney(equity,r.ccy))+'</td><td>'+ccEsc(ccMoney(available,r.ccy))+'</td><td>'+ccEsc(ccMoney(r.borrowed??0,r.ccy))+'</td><td class="muted">—</td></tr>'})}
function ccSpreadsNormalizePanel(name){return CC_SPREADS_PANEL_NAMES.includes(name)?name:'open-positions'}
function ccSpreadsPositionsOrdersTableHtml(activePanel){const active=ccSpreadsNormalizePanel(activePanel||ccSpreadsCurrentPanel());const positions=ccSpreadsPopupFuturesPositions(),orders=ccSpreadsPopupOpenOrders(),orderHistory=ccSpreadsPopupFuturesOrderHistory(),orderHistoryRows=orderHistory.map(ccFuturesOrderHistoryRow),tradeHistoryRows=ccSpreadsPopupTradeHistoryRows(),assetRows=ccSpreadsPopupAssetRows(),logsRows=ccSpreadsLogsRows();const tab=(name,label)=>'<button type="button" class="okx-spot-bottom-tab'+(active===name?' active':'')+'" data-cc-spreads-panel="'+name+'" role="tab" aria-selected="'+(active===name?'true':'false')+'">'+ccEsc(label)+'</button>';const panel=(name,html)=>'<div class="okx-spot-panel" data-cc-spreads-panel-content="'+name+'"'+(active===name?'':' hidden')+'>'+html+'</div>';return '<div class="okx-spot-bottom-panel active cc-spreads-bottom-panel" aria-label="Spread popup account panels"><div class="okx-spot-bottom-tabs" role="tablist">'+tab('open-positions','Positions ('+positions.length+')')+tab('open-orders','Orders ('+orders.length+')')+tab('order-history','Order history ('+orderHistory.length+')')+tab('trade-history','Trade history ('+tradeHistoryRows.length+')')+tab('position-history','Position history')+tab('funding','Funding')+tab('exercise','Exercise')+tab('assets','Assets')+tab('logs','Logs ('+ccSpreadsLogs.length+')')+tab('diagnostic','Diagnostic')+ccSpreadsTradeHistoryPagerHtml(tradeHistoryRows.length,active==='trade-history')+'</div><div class="okx-spot-bottom-body">'+panel('open-positions',ccSpreadsPositionsTableHtml())+panel('open-orders',ccSpreadsPopupMixedRowsTable(orders,ccFuturesOpenOrdersLoaded?'No CoinCall open orders returned.':'Loading CoinCall open orders...'))+panel('order-history',ccBottomTableAlways(['Time','Symbol','Side','Average｜Price','Filled｜Amount','Status','Order ID','Fees','Realized PnL','Type','Reduce Only','Trigger Conditions','TIF'],orderHistory.map(ccFuturesOrderHistoryRow),'No CoinCall futures order history returned.',13))+panel('trade-history',ccSpreadsTradeHistoryTableHtml(tradeHistoryRows))+panel('position-history','<div class="okx-nitro-empty-state">Position history is not connected yet.</div>')+panel('funding',ccSpreadsFundingTableHtml())+panel('exercise','<div class="okx-nitro-empty-state">Exercise is not connected yet.</div>')+panel('assets',ccBottomTable(['Coin','Equity','Available Balance','Borrowed Amount','Actions'],assetRows,'No CoinCall assets returned.'))+panel('logs',ccBottomTableAlways(['Time','Market','Role','Symbol','Kind','Message'],logsRows,'No popup messages yet.',6))+panel('diagnostic','<div class="cc-spreads-latency-panel" id="ccSpreadsLatencyPanel"></div>')+'</div></div>'}
function ccSetSpreadsBottomPanel(name){const root=document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-positions-orders]');const activeName=ccSpreadsStorePanel(name);if(!root)return;if(root.dataset)root.dataset.ccSpreadsSelectedPanel=activeName;root.querySelectorAll('[data-cc-spreads-panel]').forEach(btn=>{const active=btn.dataset.ccSpreadsPanel===activeName;btn.classList.toggle('active',active);btn.setAttribute('aria-selected',active?'true':'false')});root.querySelectorAll('[data-cc-spreads-panel-content]').forEach(panel=>{panel.hidden=panel.dataset.ccSpreadsPanelContent!==activeName})}
function ccHandleSpreadsBottomPanelEvent(e){const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(e.target))return false;const orderDiagPage=e.target.closest('[data-cc-order-diag-page]');if(orderDiagPage&&modal.contains(orderDiagPage)){e.preventDefault();e.stopPropagation();ccOrderDiagnosticSetPage(orderDiagPage.dataset.ccOrderDiagPage);return true}const pageBtn=e.target.closest('[data-cc-spreads-trade-history-page]');if(pageBtn&&modal.contains(pageBtn)){e.preventDefault();e.stopPropagation();const dir=pageBtn.dataset.ccSpreadsTradeHistoryPage;const total=ccSpreadsPopupTradeHistoryRows().length;const pages=ccSpreadsTradeHistoryPageCount(total);ccSpreadsTradeHistoryPage=Math.min(Math.max(1,ccSpreadsTradeHistoryPage+(dir==='next'?1:-1)),pages);ccRenderSpreadsPositionsOrdersTable();return true}const spreadsTab=e.target.closest('[data-cc-spreads-panel]');if(!spreadsTab||!modal.contains(spreadsTab))return false;e.preventDefault();e.stopPropagation();ccSetSpreadsBottomPanel(spreadsTab.dataset.ccSpreadsPanel);if(spreadsTab.dataset.ccSpreadsPanel==='funding'&&!ccSpreadsFundingHistoryLoaded)ccSpreadsRefreshPrivateState();return true}
function ccHandleSpreadsCancelEvent(e){const modal=cc('ccSpreadsTradeModal');if(!modal||modal.hidden||!modal.contains(e.target))return false;const futuresCancel=e.target.closest('[data-cc-futures-cancel-index]');if(futuresCancel&&modal.contains(futuresCancel)){e.preventDefault();e.stopPropagation();if(futuresCancel.dataset.ccCancelPending==='1'||futuresCancel.getAttribute('aria-disabled')==='true')return true;ccFillAudioContext();cancelCcFuturesOrder(futuresCancel.dataset.ccFuturesCancelIndex);return true}const spotCancel=e.target.closest('[data-cc-spot-cancel-index]');if(spotCancel&&modal.contains(spotCancel)){e.preventDefault();e.stopPropagation();ccFillAudioContext();cancelCcSpotOrder(spotCancel.dataset.ccSpotCancelIndex);return true}return false}
function ccRenderSpreadsPositionsOrdersTable(){ccRenderSpreadsSideOrdersTable();const host=document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-positions-orders]');if(!host)return;const active=ccSpreadsCurrentPanel();host.dataset.ccSpreadsSelectedPanel=active;if(!host.querySelector('.cc-spreads-bottom-panel')){host.innerHTML=ccSpreadsPositionsOrdersTableHtml(active);return}const captureScroll=(panel)=>{const nodes=[panel,...panel.querySelectorAll('.okx-spot-orders-wrap,.table-wrap')];return nodes.map(n=>n.scrollTop||0)};const restoreScroll=(panel,vals)=>{const nodes=[panel,...panel.querySelectorAll('.okx-spot-orders-wrap,.table-wrap')];nodes.forEach((n,i)=>{if(vals&&vals[i])n.scrollTop=vals[i]})};const scrollState={};host.querySelectorAll('[data-cc-spreads-panel-content]').forEach(panel=>{scrollState[panel.dataset.ccSpreadsPanelContent]=captureScroll(panel)});const positions=ccSpreadsPopupFuturesPositions(),orders=ccSpreadsPopupOpenOrders(),orderHistory=ccSpreadsPopupFuturesOrderHistory(),orderHistoryRows=orderHistory.map(ccFuturesOrderHistoryRow),tradeHistoryRows=ccSpreadsPopupTradeHistoryRows(),assetRows=ccSpreadsPopupAssetRows(),logsRows=ccSpreadsLogsRows();const labels={'open-positions':'Positions ('+positions.length+')','open-orders':'Orders ('+orders.length+')','order-history':'Order history ('+orderHistory.length+')','trade-history':'Trade history ('+tradeHistoryRows.length+')','position-history':'Position history','funding':'Funding','exercise':'Exercise','assets':'Assets','logs':'Logs ('+ccSpreadsLogs.length+')','diagnostic':'Diagnostic'};host.querySelectorAll('[data-cc-spreads-panel]').forEach(btn=>{const name=btn.dataset.ccSpreadsPanel,label=labels[name]||name;const on=name===active;btn.classList.toggle('active',on);btn.setAttribute('aria-selected',on?'true':'false');if(btn.textContent!==label)btn.replaceChildren(document.createTextNode(label))});const tabs=host.querySelector('.okx-spot-bottom-tabs');if(tabs){const pager=tabs.querySelector('.cc-spreads-trade-history-pager'),nextPagerHtml=ccSpreadsTradeHistoryPagerHtml(tradeHistoryRows.length,active==='trade-history');if(pager){if(pager.outerHTML!==nextPagerHtml)pager.outerHTML=nextPagerHtml}else tabs.insertAdjacentHTML('beforeend',nextPagerHtml)}const positionsPanel=host.querySelector('[data-cc-spreads-panel-content="open-positions"]');if(positionsPanel){positionsPanel.hidden='open-positions'!==active;ccSpreadsPatchTable(positionsPanel,'okx-spot-orders-table','okx-spot-orders-wrap',CC_SPREADS_POSITION_HEADERS,ccSpreadsPositionTableRows(),'No CoinCall futures positions returned.');restoreScroll(positionsPanel,scrollState['open-positions'])}const htmls={'open-orders':ccSpreadsPopupMixedRowsTable(orders,ccFuturesOpenOrdersLoaded?'No CoinCall open orders returned.':'Loading CoinCall open orders...'),'position-history':'<div class="okx-nitro-empty-state">Position history is not connected yet.</div>','funding':ccSpreadsFundingTableHtml(),'exercise':'<div class="okx-nitro-empty-state">Exercise is not connected yet.</div>','assets':ccBottomTable(['Coin','Equity','Available Balance','Borrowed Amount','Actions'],assetRows,'No CoinCall assets returned.'),'logs':ccBottomTableAlways(['Time','Market','Role','Symbol','Kind','Message'],logsRows,'No popup messages yet.',6)};Object.keys(htmls).forEach(name=>{const panel=host.querySelector('[data-cc-spreads-panel-content="'+name+'"]');if(panel)ccSpreadsPatchHtmlPanel(panel,name,htmls[name],active,scrollState,restoreScroll)});const orderHistoryPanel=host.querySelector('[data-cc-spreads-panel-content="order-history"]');if(orderHistoryPanel){ccSpreadsPatchOrderHistoryPanel(orderHistoryPanel,orderHistoryRows,active==='order-history');restoreScroll(orderHistoryPanel,scrollState['order-history'])}const tradeHistoryPanel=host.querySelector('[data-cc-spreads-panel-content="trade-history"]');if(tradeHistoryPanel){ccSpreadsPatchTradeHistoryPanel(tradeHistoryPanel,tradeHistoryRows,active==='trade-history');restoreScroll(tradeHistoryPanel,scrollState['trade-history'])}const diagnosticPanel=host.querySelector('[data-cc-spreads-panel-content="diagnostic"]');if(diagnosticPanel){diagnosticPanel.hidden=active!=='diagnostic';if(!diagnosticPanel.querySelector('#ccSpreadsLatencyPanel'))diagnosticPanel.innerHTML='<div class="cc-spreads-latency-panel" id="ccSpreadsLatencyPanel"></div>';if(active==='diagnostic'){ccSpreadsScheduleLatencyPanelRender(100);ccRenderOrderDiagnosticPanel()}}}
async function ccSpreadsRefreshPrivateState(){
  if(!ccSpreadsTradeModalOpen()||ccSpreadsPrivateRefreshBusy)return;
  if(ccSpreadsLatencyPriorityActive()){clearTimeout(ccSpreadsPrivateRefreshTimer);ccSpreadsPrivateRefreshTimer=setTimeout(ccSpreadsRefreshPrivateState,250);return}
  const refreshStarted=ccSpreadsLatencyNow();
  const now=Date.now();
  const wait=CC_SPREADS_PRIVATE_REFRESH_MS-(now-ccSpreadsPrivateRefreshLastAt);
  if(ccSpreadsPrivateRefreshLastAt&&wait>0){clearTimeout(ccSpreadsPrivateRefreshTimer);ccSpreadsPrivateRefreshTimer=setTimeout(ccSpreadsRefreshPrivateState,wait+25);return;}
  ccSpreadsPrivateRefreshLastAt=now;ccSpreadsPrivateRefreshBusy=true;
  try{
    ccEnsureFuturesPrivateSocket().catch(()=>{});
    const sel=cc('ccAccountSelect');const accountQs=sel&&sel.value?'?accountId='+encodeURIComponent(sel.value):'';const futuresOrdersSeq=++CC_FUTURES_ORDER_REFRESH.seq,futuresOrdersStartedAt=Date.now();const activePanel=ccSpreadsCurrentPanel();
    const slowMissing=(activePanel==='trade-history'&&!(Array.isArray(ccLastFuturesTradeHistory)&&ccLastFuturesTradeHistory.length)&&!(Array.isArray(ccLastSpotTradeHistory)&&ccLastSpotTradeHistory.length))||(activePanel==='order-history'&&!(Array.isArray(ccLastFuturesOrderHistory)&&ccLastFuturesOrderHistory.length))||!ccSpreadsFundingHistoryLoaded;
    const slowDue=!ccSpreadsPrivateSlowRefreshLastAt||slowMissing||now-ccSpreadsPrivateSlowRefreshLastAt>=CC_SPREADS_PRIVATE_SLOW_REFRESH_MS;
    const futuresWsOpen=ccFuturesPrivateSocketOpen(),futuresRestDue=!ccFuturesPrivateLastReconcileAt||now-ccFuturesPrivateLastReconcileAt>=CC_FUTURES_PRIVATE_RECONCILE_MS,needFuturesOrdersRest=!ccFuturesOpenOrdersLoaded||!futuresWsOpen||futuresRestDue,needFuturesPositionsRest=!ccFuturesPositionsLoaded||!futuresWsOpen||futuresRestDue;
    const needSpotOrdersRest=ccSpotOpenOrdersRestNeeded(false,'spreads-popup-private-refresh',now);const requests=[ccApi('/api/admin/coincall/summary'+accountQs),needSpotOrdersRest?ccMaybeLoadSpotOpenOrders(false,ccSpotPrivateSocketHealthy(now)?'spreads-popup-private-refresh':'spot-ws-stale'):Promise.resolve(null),needFuturesOrdersRest?ccApi('/api/admin/coincall/futures/orders/open'+accountQs):Promise.resolve(null),needFuturesPositionsRest?ccApi('/api/admin/coincall/futures/positions'+accountQs):Promise.resolve(null)];
    if(slowDue){const spotTradeSymbol=ccSpreadsPopupSpotSymbols()[0]||cc('ccSpotInst')?.value||'';const spotTradeQs=(accountQs?accountQs+'&':'?')+'pageSize=100'+(spotTradeSymbol?'&symbol='+encodeURIComponent(spotTradeSymbol):'');requests.push(ccApi('/api/admin/coincall/futures/orders/history'+(accountQs?accountQs+'&pageSize=100':'?pageSize=100')),ccApi('/api/admin/coincall/futures/trades/history'+(accountQs?accountQs+'&pageSize=100':'?pageSize=100')),ccApi('/api/admin/coincall/spot/trades/history'+spotTradeQs));}
    const results=await Promise.allSettled(requests);const [summaryRes,spotOrdersRes,futuresOrdersRes,futuresPosRes,futuresOrderHistoryRes,futuresTradeHistoryRes,spotTradeHistoryRes]=results;
    if(summaryRes.status==='fulfilled')ccLastAssetsSummary=summaryRes.value;
    if(spotOrdersRes.status==='fulfilled'&&spotOrdersRes.value)ccApplySpotOpenOrdersResponse(spotOrdersRes.value,'popup-rest')
    if(futuresOrdersRes.status==='fulfilled'&&futuresOrdersRes.value)ccApplyFuturesOpenOrdersResponse(futuresOrdersRes.value,futuresOrdersSeq,futuresOrdersStartedAt);
    if(futuresPosRes.status==='fulfilled'&&futuresPosRes.value){ccLastFuturesPositions=ccFuturesPositionsFrom(futuresPosRes.value);ccFuturesPositionsLoaded=true}
    if((needFuturesOrdersRest&&futuresOrdersRes.status==='fulfilled')||(needFuturesPositionsRest&&futuresPosRes.status==='fulfilled'))ccFuturesPrivateLastReconcileAt=now;
    if(slowDue){ccSpreadsPrivateSlowRefreshLastAt=now;if(futuresOrderHistoryRes&&futuresOrderHistoryRes.status==='fulfilled'&&futuresOrderHistoryRes.value)ccLastFuturesOrderHistory=ccSpotOpenOrdersFrom(futuresOrderHistoryRes.value);if(futuresTradeHistoryRes&&futuresTradeHistoryRes.status==='fulfilled'&&futuresTradeHistoryRes.value){const prev=ccLastFuturesTradeHistory,next=ccSpotOpenOrdersFrom(futuresTradeHistoryRes.value);ccMaybeNotifyFuturesFills(prev,next);ccLastFuturesTradeHistory=next}if(spotTradeHistoryRes&&spotTradeHistoryRes.status==='fulfilled'&&spotTradeHistoryRes.value){const prev=ccLastSpotTradeHistory,next=ccSpotOpenOrdersFrom(spotTradeHistoryRes.value);ccMaybeNotifySpotFills(prev,next);ccLastSpotTradeHistory=next}if(!ccSpreadsFundingHistoryLoaded)await ccSpreadsPreloadFundingHistory(accountQs)}
    CC_SPREADS_LATENCY.lastPrivateRefreshMs=ccSpreadsLatencyNow()-refreshStarted;ccRenderSpreadsPositionsOrdersTable();ccRenderSpreadsAccountSummary();
  }finally{ccSpreadsPrivateRefreshBusy=false;if(ccSpreadsTradeModalOpen()){clearTimeout(ccSpreadsPrivateRefreshTimer);ccSpreadsPrivateRefreshTimer=setTimeout(ccSpreadsRefreshPrivateState, CC_SPREADS_PRIVATE_REFRESH_MS);}}
}
function ccSpreadsRowsForAsset(asset){
  const state = ccSpreadsAssetState(asset);
  const columns = Array.isArray(state.columns) ? state.columns : [];
  const rows = [state.spot || ccSpreadsSpotLeg(asset)];
  if (state.perp) rows.push({ ...state.perp, label: 'PERP' });
  if (columns.length > 1) rows.push(...columns.slice(0, -1));
  return rows;
}
function ccSpreadsColumnsForAsset(asset){
  const state = ccSpreadsAssetState(asset);
  const columns = Array.isArray(state.columns) ? state.columns : [];
  return (state.perp ? [{ ...state.perp, label:'PERP' }] : []).concat(columns);
}
function ccSpreadsFindCell(rowIndex, colIndex){
  rowIndex = Number(rowIndex);
  colIndex = Number(colIndex);
  if(!Number.isInteger(rowIndex) || !Number.isInteger(colIndex)) return null;
  const columns = ccSpreadsColumnsForAsset(ccSpreadsHeaderAsset);
  const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
  const row = rows[rowIndex];
  const col = columns[colIndex];
  if(!row || !col || ccSpreadsCellUnavailable(rowIndex, colIndex, row, col)) return null;
  const rowMark = ccSpreadsMarkPrice(row);
  const colMark = ccSpreadsMarkPrice(col);
  const spread = Number.isFinite(rowMark) && Number.isFinite(colMark) ? colMark - rowMark : NaN;
  if(!Number.isFinite(rowMark) || !Number.isFinite(colMark) || !Number.isFinite(spread)) return null;
  return { row, col, rowMark, colMark, spread };
}
function ccSpreadsFindCellBySymbols(asset, rowSymbol, colSymbol){
  const prevAsset = ccSpreadsHeaderAsset;
  ccSpreadsHeaderAsset = asset === 'ETH' ? 'ETH' : 'BTC';
  const columns = ccSpreadsColumnsForAsset(ccSpreadsHeaderAsset);
  const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
  const rowIndex = rows.findIndex(item => String(item?.symbol || '').toUpperCase() === String(rowSymbol || '').toUpperCase());
  const colIndex = columns.findIndex(item => String(item?.symbol || '').toUpperCase() === String(colSymbol || '').toUpperCase());
  ccSpreadsHeaderAsset = prevAsset;
  if(rowIndex < 0 || colIndex < 0) return null;
  const row = rows[rowIndex];
  const col = columns[colIndex];
  if(!row || !col || ccSpreadsCellUnavailable(rowIndex, colIndex, row, col)) return null;
  const rowMark = ccSpreadsMarkPrice(row);
  const colMark = ccSpreadsMarkPrice(col);
  const spread = Number.isFinite(rowMark) && Number.isFinite(colMark) ? colMark - rowMark : NaN;
  if(!Number.isFinite(rowMark) || !Number.isFinite(colMark) || !Number.isFinite(spread)) return null;
  return { row, col, rowMark, colMark, spread };
}
function ccSpreadsFormatUsd(value){
  const n = Number(value);
  return Number.isFinite(n) ? n.toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 2 }) + ' USD' : '—';
}
function ccSpreadsChartKey(row, col){
  return String(row?.symbol || '').toUpperCase() + '|' + String(col?.symbol || '').toUpperCase();
}
function ccSpreadsChartStorageKey(key){
  const safeKey = String(key || CC_SPREADS_CHART_STATE.key || '').trim().toUpperCase();
  return safeKey ? CC_SPREADS_CHART_STORAGE_PREFIX + safeKey : '';
}
function ccSpreadsPopupChartInterfaceEnabled(){
  return CC_SPREADS_CHART_STATE.interfaceMode !== 'trade-only';
}
function ccSpreadsClearPopupChartTimers(){
  clearTimeout(CC_SPREADS_CHART_STATE.liveUpdateTimer);
  CC_SPREADS_CHART_STATE.liveUpdateTimer = 0;
  clearTimeout(CC_SPREADS_CHART_STATE.fundingLiveTimer);
  CC_SPREADS_CHART_STATE.fundingLiveTimer = 0;
}
function ccSpreadsSyncPopupChartModeButtons(){
  const mode = ccSpreadsPopupChartInterfaceEnabled() ? 'interface' : 'trade-only';
  document.querySelectorAll('[data-cc-spreads-chart-mode]').forEach(btn => {
    const active = btn.dataset.ccSpreadsChartMode === mode;
    btn.classList.toggle('active', active);
    btn.setAttribute('aria-pressed', active ? 'true' : 'false');
  });
}
function ccSpreadsSetPopupChartMode(mode){
  const next = mode === 'trade-only' ? 'trade-only' : 'interface';
  if(CC_SPREADS_CHART_STATE.interfaceMode === next){ ccSpreadsSyncPopupChartModeButtons(); return; }
  CC_SPREADS_CHART_STATE.interfaceMode = next;
  try{ localStorage.setItem(CC_SPREADS_CHART_MODE_STORAGE_KEY, next); }catch(e){}
  ccSpreadsSyncPopupChartModeButtons();
  if(next === 'trade-only'){
    CC_SPREADS_CHART_STATE.loading = false;
    CC_SPREADS_CHART_STATE.requestSeq++;
    ccSpreadsClearPopupChartTimers();
    ccSpreadsSpreadChartStatus('');
    return;
  }
  if(ccSpreadsTradeModalOpen() && ccSpreadsTradeModalState){
    const cell = ccSpreadsFindCellBySymbols(ccSpreadsTradeModalState.asset, ccSpreadsTradeModalState.rowSymbol, ccSpreadsTradeModalState.colSymbol);
    if(cell) ccSpreadsLoadSpreadMinuteChart(cell.row, cell.col, cell.spread).catch(e => { if(ccSpreadsPopupChartInterfaceEnabled()){ CC_SPREADS_CHART_STATE.error = 'Chart failed: ' + e.message; CC_SPREADS_CHART_STATE.loading = false; ccSpreadsDrawSpreadChart(); } });
  }
}
function ccSpreadsValidRange(range){
  if(!range || typeof range !== 'object') return null;
  const min = Number(range.min), max = Number(range.max);
  return Number.isFinite(min) && Number.isFinite(max) && max > min ? { min, max } : null;
}
function ccSpreadsSaveChartState(){
  const st = CC_SPREADS_CHART_STATE;
  const storageKey = ccSpreadsChartStorageKey(st.key);
  if(!storageKey) return;
  const state = {
    key:String(st.key || '').toUpperCase(),
    visibleCandles:Math.round(Number(st.visibleCandles) || 0),
    panOffset:Math.round(Number(st.panOffset) || 0),
    priceRange:ccSpreadsValidRange(st.priceRange),
    fundingRange:ccSpreadsValidRange(st.fundingRange),
    fundingEnabled:!!st.fundingEnabled,
    bidAskVisible:!!st.bidAskVisible,
    bidAskDiffVisible:!!st.bidAskDiffVisible
  };
  if(!Number.isFinite(state.visibleCandles) || state.visibleCandles < 1) delete state.visibleCandles;
  try{ localStorage.setItem(storageKey, JSON.stringify(state)); }catch(e){}
}
function ccSpreadsRestoreChartState(key){
  const storageKey = ccSpreadsChartStorageKey(key);
  if(!storageKey) return false;
  try{
    const raw = localStorage.getItem(storageKey);
    const saved = raw ? JSON.parse(raw) : null;
    if(!saved || typeof saved !== 'object') return false;
    const visibleCandles = Math.round(Number(saved.visibleCandles));
    if(Number.isFinite(visibleCandles) && visibleCandles >= 1 && visibleCandles <= 10000) CC_SPREADS_CHART_STATE.visibleCandles = visibleCandles;
    const panOffset = Math.round(Number(saved.panOffset));
    if(Number.isFinite(panOffset) && Math.abs(panOffset) <= 10000) CC_SPREADS_CHART_STATE.panOffset = panOffset;
    CC_SPREADS_CHART_STATE.priceRange = ccSpreadsValidRange(saved.priceRange);
    CC_SPREADS_CHART_STATE.fundingRange = ccSpreadsValidRange(saved.fundingRange);
    if(typeof saved.fundingEnabled === 'boolean') CC_SPREADS_CHART_STATE.fundingEnabled = saved.fundingEnabled;
    if(typeof saved.bidAskVisible === 'boolean') CC_SPREADS_CHART_STATE.bidAskVisible = saved.bidAskVisible;
    if(typeof saved.bidAskDiffVisible === 'boolean') CC_SPREADS_CHART_STATE.bidAskDiffVisible = saved.bidAskDiffVisible;
    return true;
  }catch(e){
    return false;
  }
}
function ccSpreadsChartValue(row){
  const mark = Number(row && row.mark);
  if(Number.isFinite(mark)) return mark;
  return NaN;
}
function ccSpreadsHistoricalChartValue(row){
  const mark = Number(row && row.mark);
  if(Number.isFinite(mark)) return mark;
  return NaN;
}
function ccSpreadsChartBid(row){
  const bid = Number(row && row.bid);
  if(Number.isFinite(bid)) return bid;
  return ccSpreadsChartValue(row);
}
function ccSpreadsChartAsk(row){
  const ask = Number(row && row.ask);
  if(Number.isFinite(ask)) return ask;
  return ccSpreadsChartValue(row);
}
function ccSpreadsChartBidAskMedian(row){
  const bid = Number(row && row.bid), ask = Number(row && row.ask);
  return Number.isFinite(bid) && bid > 0 && Number.isFinite(ask) && ask > 0 ? (bid + ask) / 2 : NaN;
}
function ccSpreadsChartSeriesValue(row, key){
  return Number(row && row[key]);
}
function ccSpreadsBidAskMedianDiffPct(row){
  const markSpread = Number(row && (row.mark ?? row.spread));
  const bidAskSpread = Number(row && row.bidAskSpread);
  const rowMark = Number(row && row.rowMark);
  return Number.isFinite(markSpread) && Number.isFinite(bidAskSpread) && Number.isFinite(rowMark) && Math.abs(rowMark) > 1e-9 ? ((bidAskSpread - markSpread) / rowMark) * 100 : NaN;
}
function ccSpreadsBuildSpreadRows(rowRows, colRows){
  const rowByTs = new Map((Array.isArray(rowRows) ? rowRows : []).map(r => [ccChartMinuteBucket(r && r.t), r]));
  return (Array.isArray(colRows) ? colRows : []).map(colRow => {
    const t = ccChartMinuteBucket(colRow && colRow.t);
    const rowRow = rowByTs.get(t);
    const rowMark = ccSpreadsHistoricalChartValue(rowRow);
    const colMark = ccSpreadsHistoricalChartValue(colRow);
    const spread = Number.isFinite(rowMark) && Number.isFinite(colMark) ? colMark - rowMark : NaN;
    const rowBid = ccSpreadsChartBid(rowRow), rowAsk = ccSpreadsChartAsk(rowRow);
    const colBid = ccSpreadsChartBid(colRow), colAsk = ccSpreadsChartAsk(colRow);
    const bid = Number.isFinite(colBid) && Number.isFinite(rowAsk) ? colBid - rowAsk : NaN;
    const ask = Number.isFinite(colAsk) && Number.isFinite(rowBid) ? colAsk - rowBid : NaN;
    const rowBidAskMedian = ccSpreadsChartBidAskMedian(rowRow);
    const colBidAskMedian = ccSpreadsChartBidAskMedian(colRow);
    const bidAskSpread = Number.isFinite(rowBidAskMedian) && Number.isFinite(colBidAskMedian) ? colBidAskMedian - rowBidAskMedian : NaN;
    return { t, spread, mark:spread, bid, ask, rowMark, colMark, rowBidAskMedian, colBidAskMedian, bidAskSpread };
  }).filter(r => Number.isFinite(r.t) && Number.isFinite(r.spread)).sort((a,b) => a.t - b.t);
}
function ccSpreadsRawNumber(row, keys){
  for(const key of keys){
    const value = Number(row && row[key]);
    if(Number.isFinite(value)) return value;
  }
  return NaN;
}
function ccSpreadsNormalizeSpreadRow(raw){
  const t = ccChartMinuteBucket(raw && (raw.t || raw.minuteUtc || raw.minute_utc || raw.time || raw.timestamp));
  const spread = Number(raw && (raw.spread ?? raw.markSpreadClose ?? raw.midSpreadClose ?? raw.mark ?? raw.close));
  const bid = Number(raw && (raw.bidSpreadClose ?? raw.bid ?? raw.bidClose));
  const ask = Number(raw && (raw.askSpreadClose ?? raw.ask ?? raw.askClose));
  const bidAskSpread = Number(raw && (raw.bidAskSpread ?? raw.bidAskMedianSpread ?? raw.midSpreadClose ?? raw.midSpread ?? raw.mid ?? raw.midClose));
  let rowMark = ccSpreadsRawNumber(raw, ['rowMark','rowMarkClose','rowPrice','rowPriceClose','rowMarkPriceClose','longMark','longMarkClose','longPrice','longPriceClose']);
  let colMark = ccSpreadsRawNumber(raw, ['colMark','colMarkClose','colPrice','colPriceClose','colMarkPriceClose','shortMark','shortMarkClose','shortPrice','shortPriceClose']);
  if(!Number.isFinite(rowMark) && Number.isFinite(colMark) && Number.isFinite(spread)) rowMark = colMark - spread;
  if(!Number.isFinite(colMark) && Number.isFinite(rowMark) && Number.isFinite(spread)) colMark = rowMark + spread;
  return { t, spread, mark:spread, bid, ask, bidAskSpread, rowMark, colMark };
}
function ccSpreadsAttachLegMarks(rows, rowRows, colRows){
  const rowByTs = new Map((Array.isArray(rowRows) ? rowRows : []).map(r => [ccChartMinuteBucket(r && r.t), r]));
  const colByTs = new Map((Array.isArray(colRows) ? colRows : []).map(r => [ccChartMinuteBucket(r && r.t), r]));
  return (Array.isArray(rows) ? rows : []).map(row => {
    const t = ccChartMinuteBucket(row && (row.t ?? row.time));
    const rowLeg = rowByTs.get(t), colLeg = colByTs.get(t);
    const rowMark = Number.isFinite(Number(row.rowMark)) ? Number(row.rowMark) : ccSpreadsHistoricalChartValue(rowLeg);
    const colMark = Number.isFinite(Number(row.colMark)) ? Number(row.colMark) : ccSpreadsHistoricalChartValue(colLeg);
    const rowBidAskMedian = Number.isFinite(Number(row.rowBidAskMedian)) ? Number(row.rowBidAskMedian) : ccSpreadsChartBidAskMedian(rowLeg);
    const colBidAskMedian = Number.isFinite(Number(row.colBidAskMedian)) ? Number(row.colBidAskMedian) : ccSpreadsChartBidAskMedian(colLeg);
    const bidAskSpread = Number.isFinite(Number(row.bidAskSpread)) ? Number(row.bidAskSpread) : (Number.isFinite(rowBidAskMedian) && Number.isFinite(colBidAskMedian) ? colBidAskMedian - rowBidAskMedian : NaN);
    return Object.assign({}, row, {
      t:Number.isFinite(Number(row.t)) ? Number(row.t) : t,
      rowMark:Number.isFinite(rowMark) ? rowMark : row.rowMark,
      colMark:Number.isFinite(colMark) ? colMark : row.colMark,
      rowBidAskMedian:Number.isFinite(rowBidAskMedian) ? rowBidAskMedian : row.rowBidAskMedian,
      colBidAskMedian:Number.isFinite(colBidAskMedian) ? colBidAskMedian : row.colBidAskMedian,
      bidAskSpread:Number.isFinite(bidAskSpread) ? bidAskSpread : row.bidAskSpread
    });
  });
}
function ccSpreadsRollingSpreadBands(rows, spreadKey, longKey){
  const result = { mean:[], p1:[], m1:[] };
  const windowRatios = [];
  (Array.isArray(rows) ? rows : []).forEach(row => {
    const spread = Number(row && row[spreadKey || 'spread']);
    const longPrice = Number(row && row[longKey || 'rowMark']);
    if(!Number.isFinite(spread) || !Number.isFinite(longPrice) || Math.abs(longPrice) < 1e-9){
      result.mean.push(NaN); result.p1.push(NaN); result.m1.push(NaN);
      return;
    }
    windowRatios.push(spread / longPrice);
    if(windowRatios.length > CC_SPREADS_MEAN_WINDOW) windowRatios.shift();
    if(windowRatios.length < 2){
      result.mean.push(NaN); result.p1.push(NaN); result.m1.push(NaN);
      return;
    }
    const meanRatio = windowRatios.reduce((sum, value) => sum + value, 0) / windowRatios.length;
    result.mean.push(meanRatio * longPrice);
    result.p1.push((meanRatio + CC_SPREADS_ENVELOPE_STEPS[0]) * longPrice);
    result.m1.push((meanRatio - CC_SPREADS_ENVELOPE_STEPS[0]) * longPrice);
  });
  return result;
}
function ccSpreadsBandValuesForRows(rows, spreadKey, longKey){
  const bands = ccSpreadsRollingSpreadBands(rows, spreadKey, longKey);
  return ['mean','p1','m1'].flatMap(key => bands[key]).filter(Number.isFinite);
}
function ccSpreadsReferenceRowMark(rows){
  const list = Array.isArray(rows) ? rows : [];
  for(let i = list.length - 1; i >= 0; i--){
    const n = Number(list[i] && list[i].rowMark);
    if(Number.isFinite(n) && Math.abs(n) > 1e-9) return n;
  }
  return NaN;
}
function ccSpreadsPercentValue(spreadValue, rowMark){
  const spread = Number(spreadValue), ref = Number(rowMark);
  return Number.isFinite(spread) && Number.isFinite(ref) && Math.abs(ref) > 1e-9 ? spread / ref * 100 : NaN;
}
function ccSpreadsDollarText(value){
  const n = Number(value);
  if(!Number.isFinite(n)) return '$—';
  const abs = Math.abs(n).toLocaleString('en-US', { minimumFractionDigits:2, maximumFractionDigits:2 });
  return (n < 0 ? '-' : '') + '$' + abs;
}
function ccSpreadsPercentUsdLabel(percentValue, rowMark){
  const pct = Number(percentValue), ref = Number(rowMark);
  if(!Number.isFinite(pct)) return '—';
  const pctText = pct.toLocaleString('en-US', { minimumFractionDigits:2, maximumFractionDigits:2 }) + '%';
  const usd = Number.isFinite(ref) && Math.abs(ref) > 1e-9 ? pct / 100 * ref : NaN;
  return pctText + ' ' + ccSpreadsDollarText(usd);
}
function ccSpreadsSpreadAxisLabel(spreadValue, rowMark){
  return ccSpreadsPercentUsdLabel(ccSpreadsPercentValue(spreadValue, rowMark), rowMark);
}
function ccSpreadsLiveBookMid(role){
  const raw = CC_SPREADS_BOOK_STATE.raw[role];
  const sides = raw ? ccBookSides(raw) : null;
  if(!sides) return NaN;
  const bidRows = ccBookSort(sides.bidRows || [], 'bid');
  const askRows = ccBookSort(sides.askRows || [], 'ask');
  const bid = Number(bidRows[0]?.p), ask = Number(askRows[0]?.p);
  if(Number.isFinite(bid) && bid > 0 && Number.isFinite(ask) && ask > 0) return (bid + ask) / 2;
  if(Number.isFinite(bid) && bid > 0) return bid;
  if(Number.isFinite(ask) && ask > 0) return ask;
  return NaN;
}
function ccSpreadsLiveSpotPrice(symbol){
  const sym = String(symbol || '').toUpperCase();
  const asset = sym.startsWith('ETH') ? 'ETH' : 'BTC';
  const cached = Number(CC_SPREADS_SPOT_PRICE_CACHE[asset]?.price);
  return Number.isFinite(cached) && cached > 0 ? cached : NaN;
}
function ccSpreadsLiveFuturesMark(symbol){
  const sym = String(symbol || '').toUpperCase();
  for(const role of ['row','col']){
    if((CC_SPREADS_BOOK_STATE.types[role] || 'futures') !== 'futures') continue;
    if(CC_SPREADS_BOOK_STATE.symbols[role] !== sym) continue;
    const mark = ccSpreadsFuturesMarkPrice(role);
    if(Number.isFinite(mark) && mark > 0) return mark;
  }
  return NaN;
}
function ccSpreadsLiveLegMark(leg){
  const symbol = String(leg?.symbol || '').toUpperCase();
  if(!symbol) return NaN;
  if(ccSpreadsIsSpotLeg(leg)){
    const spot = ccSpreadsLiveSpotPrice(symbol);
    if(Number.isFinite(spot) && spot > 0) return spot;
  }else{
    const liveMark = ccSpreadsLiveFuturesMark(symbol);
    if(Number.isFinite(liveMark) && liveMark > 0) return liveMark;
  }
  return ccSpreadsMarkPrice(leg);
}
function ccSpreadsLiveLegBidAskMedian(leg){
  const symbol = String(leg?.symbol || '').toUpperCase();
  if(!symbol) return NaN;
  if(ccSpreadsIsSpotLeg(leg)){
    if(CC_SPREADS_BOOK_STATE.symbols.spot === symbol){
      const mid = ccSpreadsLiveBookMid('spot');
      if(Number.isFinite(mid) && mid > 0) return mid;
    }
  }else{
    for(const role of ['row','col']){
      if((CC_SPREADS_BOOK_STATE.types[role] || 'futures') !== 'futures') continue;
      if(CC_SPREADS_BOOK_STATE.symbols[role] !== symbol) continue;
      const mid = ccSpreadsLiveBookMid(role);
      if(Number.isFinite(mid) && mid > 0) return mid;
    }
  }
  return ccSpreadsRowBidAskMedian(leg);
}
function ccSpreadsLiveSpreadParts(row, col){
  const rowMark = ccSpreadsLiveLegMark(row);
  const colMark = ccSpreadsLiveLegMark(col);
  const spread = Number.isFinite(rowMark) && Number.isFinite(colMark) ? colMark - rowMark : NaN;
  const rowBidAskMedian = ccSpreadsLiveLegBidAskMedian(row);
  const colBidAskMedian = ccSpreadsLiveLegBidAskMedian(col);
  const bidAskSpread = Number.isFinite(rowBidAskMedian) && Number.isFinite(colBidAskMedian) ? colBidAskMedian - rowBidAskMedian : NaN;
  return { rowMark, colMark, spread, rowBidAskMedian, colBidAskMedian, bidAskSpread };
}
async function ccSpreadsLoadLegMinuteRows(leg, limit=720){
  const symbol = String(leg?.symbol || '').toUpperCase();
  if(!symbol) return [];
  if(ccSpreadsIsSpotLeg(leg)){
    const asset = symbol.startsWith('ETH') ? 'ETH' : 'BTC';
    const requestedLimit = Math.max(1, Math.min(129600, Math.round(Number(limit) || 720)));
    const res = await ccApi('/api/admin/coincall/spot/candles/minute?symbol=' + encodeURIComponent(asset + 'USDT') + '&limit=' + encodeURIComponent(requestedLimit), { cache:'no-store' });
    const rawRows = Array.isArray(res?.rows) ? res.rows : (Array.isArray(res?.data) ? res.data : []);
    const rows = ccSpreadsNormalizeSpotKlines(rawRows);
    const last = rows[rows.length - 1];
    const cache = CC_SPREADS_SPOT_PRICE_CACHE[asset] || (CC_SPREADS_SPOT_PRICE_CACHE[asset] = { price:NaN, ts:0, rows:[] });
    if(last){
      cache.rows = rows;
      const index = Number(last.index);
      if(Number.isFinite(index) && index > 0) cache.price = index;
      cache.ts = Date.now();
    }
    return rows.slice(-requestedLimit).map(row => ({ t:row.t, c:row.c, mark:Number.isFinite(Number(row.index)) ? Number(row.index) : NaN, bid:row.bid, ask:row.ask, isSpot:true }));
  }
  const res = await ccApi('/api/admin/coincall/futures/candles/minute?symbol=' + encodeURIComponent(symbol) + '&limit=' + encodeURIComponent(limit), { cache:'no-store' });
  return ccMarketDataNormalizeRows(res && res.rows);
}
function ccSpreadsRowsForMainChart(rowRows, colRows){
  return ccSpreadsBuildSpreadRows(rowRows, colRows).map(row => ({
    time:Number(row.t) > 10000000000 ? Math.floor(Number(row.t) / 1000) : Number(row.t),
    mark:row.spread,
    bidAskSpread:row.bidAskSpread,
    bid:row.bid,
    ask:row.ask,
    rowMark:row.rowMark,
    colMark:row.colMark,
    rowBidAskMedian:row.rowBidAskMedian,
    colBidAskMedian:row.colBidAskMedian
  }));
}
function ccSpreadsPushLiveSpread(row, col, spread, liveParts){
  if(!ccSpreadsPopupChartInterfaceEnabled()) return;
  const key = ccSpreadsChartKey(row, col);
  if(!key || CC_SPREADS_CHART_STATE.key !== key) return;
  const n = Number(spread);
  if(!Number.isFinite(n)) return;
  const t = ccChartMinuteBucket(Date.now());
  const rows = Array.isArray(CC_SPREADS_CHART_STATE.rows) ? CC_SPREADS_CHART_STATE.rows.slice() : [];
  const last = rows[rows.length - 1];
  const patch = { spread:n, mark:n, live:true };
  if(liveParts){
    const rowMark = Number(liveParts.rowMark), colMark = Number(liveParts.colMark);
    if(Number.isFinite(rowMark)) patch.rowMark = rowMark;
    if(Number.isFinite(colMark)) patch.colMark = colMark;
    const rowBidAskMedian = Number(liveParts.rowBidAskMedian), colBidAskMedian = Number(liveParts.colBidAskMedian), bidAskSpread = Number(liveParts.bidAskSpread);
    if(Number.isFinite(rowBidAskMedian)) patch.rowBidAskMedian = rowBidAskMedian;
    if(Number.isFinite(colBidAskMedian)) patch.colBidAskMedian = colBidAskMedian;
    if(Number.isFinite(bidAskSpread)) patch.bidAskSpread = bidAskSpread;
  }
  if(last && Number(last.t) === t) Object.assign(last, patch);
  else rows.push(Object.assign({ t }, patch));
  CC_SPREADS_CHART_STATE.rows = rows.slice(-720);
  ccSpreadsPushLiveFundingPoint(t);
  CC_SPREADS_CHART_STATE.source = CC_SPREADS_CHART_STATE.source || 'Live quote accumulation';
}
function ccSpreadsUpdateLiveSpreadChart(reason){
  if(!ccSpreadsPopupChartInterfaceEnabled()) return false;
  if(!ccSpreadsTradeModalOpen() || !ccSpreadsTradeModalState || !CC_SPREADS_CHART_STATE.key) return false;
  const cell = ccSpreadsFindCellBySymbols(ccSpreadsTradeModalState.asset, ccSpreadsTradeModalState.rowSymbol, ccSpreadsTradeModalState.colSymbol);
  if(!cell) return false;
  const live = ccSpreadsLiveSpreadParts(cell.row, cell.col);
  if(!Number.isFinite(live.spread)) return false;
  ccSpreadsPushLiveSpread(cell.row, cell.col, live.spread, live);
  const spreadEl = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-modal-role="spread"]');
  if(spreadEl){
    spreadEl.textContent = ccFmtSignedNumberOrDash(live.spread, 2);
    spreadEl.classList.toggle('okx-nitro-sell', live.spread < 0);
    spreadEl.classList.toggle('okx-nitro-buy', live.spread >= 0);
  }
  const rowEl = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-modal-role="row-mark"]');
  const colEl = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-modal-role="col-mark"]');
  if(rowEl) rowEl.textContent = ccSpreadsFormatUsd(live.rowMark);
  if(colEl) colEl.textContent = ccSpreadsFormatUsd(live.colMark);
  CC_SPREADS_CHART_STATE.source = 'Live quote accumulation';
  ccSpreadsScheduleLiveFundingPoint(reason);
  ccSpreadsDrawSpreadChart();
  return true;
}
function ccSpreadsScheduleLiveSpreadChartUpdate(reason){
  if(!ccSpreadsPopupChartInterfaceEnabled()){
    clearTimeout(CC_SPREADS_CHART_STATE.liveUpdateTimer);
    CC_SPREADS_CHART_STATE.liveUpdateTimer = 0;
    return;
  }
  clearTimeout(CC_SPREADS_CHART_STATE.liveUpdateTimer);
  CC_SPREADS_CHART_STATE.liveUpdateTimer = setTimeout(() => {
    CC_SPREADS_CHART_STATE.liveUpdateTimer = 0;
    ccSpreadsUpdateLiveSpreadChart(reason);
  }, 0);
}
function ccSpreadsFundingSymbol(row, col){
  const futures = [row, col].filter(item => item && !ccSpreadsIsSpotLeg(item));
  const perp = futures.find(ccSpreadsIsPerpetualFuture);
  const base = ccSpreadsFutureBase(perp || futures[0] || row || col);
  return base ? base + 'USD' : '';
}
function ccSpreadsFundingValue(row){
  const n = Number(row?.currentFunding ?? row?.fundRate ?? row?.fund_rate ?? row?.interest8h ?? row?.interest_8h);
  return Number.isFinite(n) ? n * 100 : NaN;
}
function ccSpreadsFundingTime(row){
  return ccChartMinuteBucket(row?.minuteUtc ?? row?.minute_utc ?? row?.observedMinuteUtc ?? row?.recordTimeUtc ?? row?.t);
}
async function ccSpreadsLoadFundingRows(symbol, limit=720){
  if(!symbol) return [];
  const res = await ccApi('/api/admin/coincall/futures/funding/minute?symbol=' + encodeURIComponent(symbol) + '&limit=' + encodeURIComponent(limit), { cache:'no-store' });
  const rows = Array.isArray(res?.items) ? res.items : (Array.isArray(res?.rows) ? res.rows : []);
  return rows.map(row => ({ t:ccSpreadsFundingTime(row), v:ccSpreadsFundingValue(row) })).filter(row => Number.isFinite(row.t) && Number.isFinite(row.v)).sort((a,b) => a.t - b.t);
}
function ccSpreadsMergeFundingRows(existing, incoming){
  const byTs = new Map();
  (Array.isArray(existing) ? existing : []).concat(Array.isArray(incoming) ? incoming : []).forEach(row => {
    const t = ccChartMinuteBucket(row && row.t), v = Number(row && row.v);
    if(Number.isFinite(t) && Number.isFinite(v)) byTs.set(t, { t, v });
  });
  return Array.from(byTs.values()).sort((a,b) => a.t - b.t).slice(-720);
}
function ccSpreadsLatestFundingValue(rows){
  const list = Array.isArray(rows) ? rows : [];
  for(let i = list.length - 1; i >= 0; i--){
    const v = Number(list[i] && list[i].v);
    if(Number.isFinite(v)) return v;
  }
  return NaN;
}
function ccSpreadsPushLiveFundingPoint(t){
  if(!ccSpreadsPopupChartInterfaceEnabled()) return false;
  const st = CC_SPREADS_CHART_STATE;
  if(!st.fundingEnabled || !st.fundingSymbol) return false;
  const bucket = ccChartMinuteBucket(t);
  const value = ccSpreadsLatestFundingValue(st.fundingRows);
  if(!Number.isFinite(bucket) || !Number.isFinite(value)) return false;
  st.fundingRows = ccSpreadsMergeFundingRows(st.fundingRows, [{ t:bucket, v:value }]);
  return true;
}
function ccSpreadsLatestSpreadTime(){
  const rows = Array.isArray(CC_SPREADS_CHART_STATE.rows) ? CC_SPREADS_CHART_STATE.rows : [];
  for(let i = rows.length - 1; i >= 0; i--){
    const t = ccChartMinuteBucket(rows[i] && rows[i].t);
    if(Number.isFinite(t)) return t;
  }
  return NaN;
}
async function ccSpreadsRefreshLiveFundingPoint(reason){
  if(!ccSpreadsPopupChartInterfaceEnabled()) return false;
  const st = CC_SPREADS_CHART_STATE;
  if(!ccSpreadsTradeModalOpen() || !st.fundingEnabled || !st.fundingSymbol || st.fundingLiveBusy) return false;
  const now = Date.now();
  if(st.fundingLiveLastAt && now - st.fundingLiveLastAt < CC_SPREADS_POPUP_FUNDING_LIVE_REFRESH_MS) return false;
  st.fundingLiveBusy = true;
  st.fundingLiveLastAt = now;
  const key = st.key, symbol = st.fundingSymbol, seq = st.requestSeq;
  try{
    const latest = await ccSpreadsLoadFundingRows(symbol, 1);
    if(seq !== st.requestSeq || st.key !== key || st.fundingSymbol !== symbol) return false;
    const spreadTime = ccSpreadsLatestSpreadTime();
    let rows = latest;
    const last = latest[latest.length - 1];
    if(last && Number.isFinite(spreadTime) && spreadTime > ccChartMinuteBucket(last.t)){
      rows = latest.concat([{ t:spreadTime, v:last.v }]);
    }
    st.fundingRows = ccSpreadsMergeFundingRows(st.fundingRows, rows);
    if(Number.isFinite(spreadTime)) ccSpreadsPushLiveFundingPoint(spreadTime);
    ccSpreadsDrawSpreadChart();
    return true;
  }catch(e){
    return false;
  }finally{
    st.fundingLiveBusy = false;
  }
}
function ccSpreadsScheduleLiveFundingPoint(reason){
  if(!ccSpreadsPopupChartInterfaceEnabled()){
    clearTimeout(CC_SPREADS_CHART_STATE.fundingLiveTimer);
    CC_SPREADS_CHART_STATE.fundingLiveTimer = 0;
    return;
  }
  const st = CC_SPREADS_CHART_STATE;
  if(!st.fundingEnabled || !st.fundingSymbol) return;
  clearTimeout(st.fundingLiveTimer);
  st.fundingLiveTimer = setTimeout(() => {
    st.fundingLiveTimer = 0;
    ccSpreadsRefreshLiveFundingPoint(reason);
  }, 0);
}
function ccSpreadsSpreadChartStatus(text){
  const el = cc('ccSpreadsMinuteChartStatus');
  if(el) el.textContent = text || '';
}
async function ccSpreadsSetFundingOverlay(enabled){
  if(!ccSpreadsPopupChartInterfaceEnabled()){
    CC_SPREADS_CHART_STATE.fundingEnabled = !!enabled;
    ccSpreadsClearPopupChartTimers();
    ccSpreadsSaveChartState();
    return;
  }
  const st = CC_SPREADS_CHART_STATE;
  st.fundingEnabled = !!enabled;
  if(st.fundingEnabled && st.fundingSymbol && (!Array.isArray(st.fundingRows) || !st.fundingRows.length)){
    try{ st.fundingRows = await ccSpreadsLoadFundingRows(st.fundingSymbol, 720); }
    catch(e){ st.fundingRows = []; st.error = 'Funding failed: ' + e.message; }
  }
  if(st.fundingEnabled) ccSpreadsPushLiveFundingPoint(ccSpreadsLatestSpreadTime());
  ccSpreadsDrawSpreadChart();
  if(st.fundingEnabled) ccSpreadsScheduleLiveFundingPoint('toggle');
  ccSpreadsSaveChartState();
}
function ccSpreadsSetBidAskOverlay(enabled){
  CC_SPREADS_CHART_STATE.bidAskVisible = !!enabled;
  if(!ccSpreadsPopupChartInterfaceEnabled()){ ccSpreadsSaveChartState(); return; }
  ccSpreadsDrawSpreadChart();
  ccSpreadsSaveChartState();
}
function ccSpreadsSetBidAskDiffOverlay(enabled){
  CC_SPREADS_CHART_STATE.bidAskDiffVisible = !!enabled;
  if(!ccSpreadsPopupChartInterfaceEnabled()){ ccSpreadsSaveChartState(); return; }
  ccSpreadsDrawSpreadChart();
  ccSpreadsSaveChartState();
}
function ccSpreadsHasFundingOverlayData(st){
  return !!(st && st.fundingEnabled && Array.isArray(st.fundingRows) && st.fundingRows.some(r => Number.isFinite(Number(r?.v))));
}
function ccSpreadsZoomFundingRange(st, center, span, autoSpan, factor){
  const safeCenter = Number(center);
  const safeSpan = Number(span);
  if(!Number.isFinite(safeCenter) || !Number.isFinite(safeSpan) || safeSpan <= 0) return false;
  const baseSpan = Math.max(Number(autoSpan) || safeSpan, 1e-10);
  const maxSpan = Math.max(baseSpan * 20, safeSpan * 4, 1e-10);
  const minSpan = Math.max(baseSpan * .02, Math.abs(safeCenter) * 1e-8, 1e-10);
  const nextSpan = Math.max(minSpan, Math.min(maxSpan, safeSpan * factor));
  st.fundingRange = { min:safeCenter - nextSpan / 2, max:safeCenter + nextSpan / 2 };
  return true;
}
function ccSpreadsBindSpreadChartInteractions(canvas){
  const st = CC_SPREADS_CHART_STATE;
  if(!canvas) return;
  if(st.wheelBound && st.boundCanvas === canvas) return;
  if(st.interactionAbort){
    try{ st.interactionAbort.abort(); }catch(e){}
  }
  st.wheelBound = true;
  st.boundCanvas = canvas;
  const abort = typeof AbortController !== 'undefined' ? new AbortController() : null;
  st.interactionAbort = abort;
  const listenerOptions = options => abort ? Object.assign({}, options || {}, { signal:abort.signal }) : (options || false);
  const pointFromEvent = event => {
    const rect = canvas.getBoundingClientRect();
    return { x:event.clientX - rect.left, y:event.clientY - rect.top };
  };
  const inPlot = p => {
    const v = st.view;
    return v && p.x >= v.padL && p.x <= v.w - v.padR && p.y >= v.padT && p.y <= v.padT + v.chartH;
  };
  const inPriceScale = p => {
    const v = st.view;
    return v && p.x > v.w - v.padR && p.x <= v.w && p.y >= v.padT && p.y <= v.padT + v.chartH;
  };
  const inTimeScale = p => {
    const v = st.view;
    return v && p.x >= v.padL && p.x <= v.w - v.padR && p.y > v.padT + v.chartH && p.y <= v.h;
  };
  const totalSlots = () => Math.max(1, Number(st.view?.total) || (Array.isArray(st.rows) ? st.rows.length : 0));
  const maxVisible = () => {
    const total = totalSlots();
    const v = st.view;
    const chartW = v?.chartW || Math.max(1, canvas.clientWidth - 100);
    return ccChartMaxVisibleCandles(total, chartW);
  };
  const clampPan = visible => {
    const total = totalSlots();
    const maxPan = Math.max(0, total - visible);
    const minPan = -ccChartRightBlankCap(visible);
    st.panOffset = Math.max(minPan, Math.min(maxPan, Math.round(Number(st.panOffset) || 0)));
  };
  const applyHorizontalZoomFromDrag = (drag, p) => {
    const total = totalSlots();
    if(!total) return;
    const max = maxVisible(), min = Math.min(max, 20);
    const dx = p.x - drag.x;
    const factor = Math.exp(dx / 180);
    const next = Math.round(Math.max(min, Math.min(max, drag.visible * factor)));
    const focusRatio = Math.max(0, Math.min(1, (drag.x - drag.padL) / Math.max(1, drag.chartW)));
    const oldAfter = drag.visible * (1 - focusRatio), nextAfter = next * (1 - focusRatio);
    st.visibleCandles = next;
    const maxPan = Math.max(0, total - next), minPan = -ccChartRightBlankCap(next);
    st.panOffset = Math.max(minPan, Math.min(maxPan, Math.round(drag.panOffset + oldAfter - nextAfter)));
  };
  const applyVerticalZoomFromDrag = (drag, p) => {
    const dy = p.y - drag.y;
    const factor = Math.exp(dy / 160);
    if(drag.mode === 'funding-scale'){
      ccSpreadsZoomFundingRange(st, drag.fundingCenter, drag.fundingSpan, drag.fundingAutoSpan, factor);
      return;
    }
    const maxSpan = Math.max(drag.autoSpan * 12, drag.span * 50, 1e-8);
    const minSpan = Math.max(drag.autoSpan * .02, Math.abs(drag.center) * 1e-8, 1e-8);
    const nextSpan = Math.max(minSpan, Math.min(maxSpan, drag.span * factor));
    st.priceRange = { min:drag.center - nextSpan / 2, max:drag.center + nextSpan / 2 };
  };
  canvas.addEventListener('wheel', event => {
    if(st.drag){ event.preventDefault(); return; }
    if(event.ctrlKey){
      event.preventDefault();
      const v = st.view;
      if(!ccSpreadsHasFundingOverlayData(st) || !v || !Number.isFinite(Number(v.fundingMin)) || !Number.isFinite(Number(v.fundingMax))) return;
      const factor = event.deltaY < 0 ? .82 : 1.22;
      const center = (Number(v.fundingMin) + Number(v.fundingMax)) / 2;
      if(ccSpreadsZoomFundingRange(st, center, Number(v.fundingSpan), Number(v.fundingAutoSpan), factor)){ ccSpreadsDrawSpreadChart(); ccSpreadsSaveChartState(); }
      return;
    }
    const total = totalSlots();
    if(!total) return;
    const max = maxVisible();
    const current = ccChartClampVisible(st, total, max);
    const factor = event.deltaY < 0 ? .82 : 1.22;
    const min = Math.min(max, 20);
    const next = Math.round(Math.max(min, Math.min(max, current * factor)));
    if(next !== current){
      event.preventDefault();
      st.visibleCandles = next;
      clampPan(next);
      ccSpreadsDrawSpreadChart();
      ccSpreadsSaveChartState();
    }
  }, listenerOptions({ passive:false }));
  canvas.addEventListener('dblclick', event => {
    if(event.button !== 0) return;
    const p = pointFromEvent(event);
    if(!inPriceScale(p)) return;
    event.preventDefault();
    event.stopPropagation();
    st.priceRange = null;
    st.drag = null;
    ccSpreadsDrawSpreadChart();
    ccSpreadsSaveChartState();
  }, listenerOptions());
  canvas.addEventListener('mousedown', event => {
    if(event.button !== 0) return;
    const p = pointFromEvent(event);
    const v = st.view;
    if(!v) return;
    const fundingDrag = event.ctrlKey && ccSpreadsHasFundingOverlayData(st) && (inPriceScale(p) || inPlot(p)) && Number.isFinite(Number(v.fundingMin)) && Number.isFinite(Number(v.fundingMax));
    if(event.ctrlKey && !fundingDrag){ event.preventDefault(); return; }
    const mode = fundingDrag ? (inPriceScale(p) ? 'funding-scale' : 'funding-pan') : (inPriceScale(p) ? 'price-scale' : (inTimeScale(p) ? 'time-scale' : 'pan'));
    st.drag = { mode, x:p.x, y:p.y, padL:v.padL, chartW:v.chartW, panOffset:v.panOffset, min:v.min, max:v.max, center:(v.min + v.max) / 2, span:v.span, autoSpan:Math.max(1e-8, v.autoSpan || v.span), fundingMin:Number(v.fundingMin), fundingMax:Number(v.fundingMax), fundingCenter:(Number(v.fundingMin) + Number(v.fundingMax)) / 2, fundingSpan:Number(v.fundingSpan) || (Number(v.fundingMax) - Number(v.fundingMin)), fundingAutoSpan:Math.max(1e-10, Number(v.fundingAutoSpan) || Number(v.fundingSpan) || (Number(v.fundingMax) - Number(v.fundingMin))), step:v.step, chartH:v.chartH, visible:v.visible, total:Number(v.total) || totalSlots() };
    canvas.style.cursor = (mode === 'price-scale' || mode === 'funding-scale') ? 'ns-resize' : (mode === 'time-scale' ? 'ew-resize' : 'grabbing');
    event.preventDefault();
  }, listenerOptions());
  window.addEventListener('mousemove', event => {
    const drag = st.drag;
    if(!drag) return;
    const p = pointFromEvent(event);
    if(drag.mode === 'price-scale') applyVerticalZoomFromDrag(drag, p);
    else if(drag.mode === 'funding-scale') applyVerticalZoomFromDrag(drag, p);
    else if(drag.mode === 'time-scale') applyHorizontalZoomFromDrag(drag, p);
    else if(drag.mode === 'funding-pan'){
      const dy = p.y - drag.y;
      const fundingShift = dy / Math.max(1, drag.chartH) * drag.fundingSpan;
      st.fundingRange = { min:drag.fundingMin + fundingShift, max:drag.fundingMax + fundingShift };
    }
    else{
      const total = Math.max(1, Number(drag.total) || totalSlots());
      const visible = Math.max(1, Math.round(Number(drag.visible) || Number(st.visibleCandles) || total));
      st.visibleCandles = visible;
      const dx = p.x - drag.x, dy = p.y - drag.y;
      const maxPan = Math.max(0, total - visible), minPan = -ccChartRightBlankCap(visible);
      st.panOffset = Math.max(minPan, Math.min(maxPan, Math.round(drag.panOffset + dx / Math.max(1, drag.step))));
      const priceShift = dy / Math.max(1, drag.chartH) * drag.span;
      st.priceRange = { min:drag.min + priceShift, max:drag.max + priceShift };
    }
    ccSpreadsDrawSpreadChart();
    event.preventDefault();
  }, listenerOptions());
  canvas.addEventListener('mousemove', event => {
    if(st.drag) return;
    const p = pointFromEvent(event);
    canvas.style.cursor = inPriceScale(p) ? 'ns-resize' : (inTimeScale(p) ? 'ew-resize' : (inPlot(p) ? 'grab' : 'crosshair'));
  }, listenerOptions());
  window.addEventListener('mouseup', () => {
    if(st.drag){
      st.drag = null;
      canvas.style.cursor = 'grab';
      ccSpreadsSaveChartState();
    }
  }, listenerOptions());
  canvas.addEventListener('mouseleave', () => {
    if(!st.drag) canvas.style.cursor = 'crosshair';
  }, listenerOptions());
}
function ccSpreadsDrawSpreadChart(){
  if(!ccSpreadsPopupChartInterfaceEnabled()) return;
  const canvas = cc('ccSpreadsMinuteChart');
  if(!canvas) return;
  ccSpreadsBindSpreadChartInteractions(canvas);
  const wrap = canvas.parentElement;
  const st = CC_SPREADS_CHART_STATE;
  const dpr = Math.max(1, Number(window.devicePixelRatio) || 1);
  const w = Math.max(320, Math.round(wrap?.clientWidth || canvas.clientWidth || 720));
  const h = Number(canvas.getAttribute('height')) || 190;
  const pixelW = Math.max(1, Math.round(w * dpr));
  const pixelH = Math.max(1, Math.round(h * dpr));
  if(canvas.style.width !== w + 'px') canvas.style.width = w + 'px';
  if(canvas.width !== pixelW) canvas.width = pixelW;
  if(canvas.height !== pixelH) canvas.height = pixelH;
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  ctx.fillStyle = '#050b16';
  ctx.fillRect(0, 0, w, h);
  const allRows = (Array.isArray(st.rows) ? st.rows : []).filter(r => Number.isFinite(Number(r.t)) && Number.isFinite(Number(r.spread))).slice(-720);
  st.rows = allRows;
  const showBidAsk = !!st.bidAskVisible;
  const showDiff = !!st.bidAskDiffVisible;
  ctx.font = '11px Segoe UI';
  ctx.textBaseline = 'middle';
  if(!allRows.length){
    st.view = null;
    ctx.fillStyle = '#64748b';
    ctx.textAlign = 'center';
    ctx.fillText(st.loading ? 'Loading minute spread...' : 'Waiting for minute spread data', w / 2, h / 2);
    ccSpreadsSpreadChartStatus(st.loading ? 'Loading' : (st.error || 'No data'));
    return;
  }
  const padL = showDiff ? 58 : 12, padR = 128, padT = 12, padB = 28;
  const chartW = Math.max(1, w - padL - padR), chartH = Math.max(1, h - padT - padB);
  const visible = ccChartClampVisible(st, allRows.length, chartW);
  const maxPan = Math.max(0, allRows.length - visible);
  const minPan = -ccChartRightBlankCap(visible);
  const panOffset = Math.max(minPan, Math.min(maxPan, Math.round(Number(st.panOffset) || 0)));
  st.panOffset = panOffset;
  const end = allRows.length - panOffset;
  const start = Math.max(0, Math.min(allRows.length, end - visible));
  const sliceEnd = Math.max(0, Math.min(allRows.length, end));
  const rows = allRows.slice(start, sliceEnd);
  const bands = ccSpreadsRollingSpreadBands(rows, 'spread', 'rowMark');
  const bandVals = ['mean','p1','m1'].flatMap(key => bands[key]).filter(Number.isFinite);
  const vals = rows.flatMap(r => [Number(r.spread)].concat(showBidAsk ? [Number(r.bid), Number(r.ask)] : []).filter(Number.isFinite)).concat(bandVals);
  const axisRowMark = ccSpreadsReferenceRowMark(rows);
  const rawMin = Math.min(...vals), rawMax = Math.max(...vals);
  const span0 = rawMax - rawMin || Math.max(Math.abs(rawMax) * .001, 1);
  let autoMin = rawMin - span0 * .08, autoMax = rawMax + span0 * .08;
  if(Number.isFinite(axisRowMark) && Math.abs(axisRowMark) > 1e-9){
    autoMin = Math.floor(ccSpreadsPercentValue(autoMin, axisRowMark) / CC_SPREADS_POPUP_SPREAD_AXIS_STEP_PCT) * CC_SPREADS_POPUP_SPREAD_AXIS_STEP_PCT / 100 * axisRowMark;
    autoMax = Math.ceil(ccSpreadsPercentValue(autoMax, axisRowMark) / CC_SPREADS_POPUP_SPREAD_AXIS_STEP_PCT) * CC_SPREADS_POPUP_SPREAD_AXIS_STEP_PCT / 100 * axisRowMark;
    if(autoMax <= autoMin) autoMax = autoMin + Math.abs(axisRowMark) * .001;
  }
  const manual = st.priceRange;
  const useManual = manual && Number.isFinite(Number(manual.min)) && Number.isFinite(Number(manual.max)) && Number(manual.max) > Number(manual.min);
  const min = useManual ? Number(manual.min) : autoMin, max = useManual ? Number(manual.max) : autoMax, span = max - min || 1;
  const y = value => padT + (max - value) / span * chartH;
  const step = rows.length <= 1 ? chartW : chartW / (rows.length - 1);
  const x = idx => padL + (rows.length === 1 ? chartW / 2 : idx * step);
  const crisp = v => Math.round(v) + .5;
  const diff = showDiff ? rows.map((r,i) => ({ x:x(i), t:ccChartMinuteBucket(r.t), v:ccSpreadsBidAskMedianDiffPct(r) })).filter(p => Number.isFinite(p.v)) : [];
  let diffMin = NaN, diffMax = NaN, dy = null;
  if(diff.length){
    const dVals = diff.map(p => p.v), dMin0 = Math.min(...dVals), dMax0 = Math.max(...dVals);
    const dSpan0 = dMax0 - dMin0 || Math.max(Math.abs(dMax0) * .2, CC_SPREADS_POPUP_DIFF_AXIS_STEP_PCT);
    diffMin = dMin0 - dSpan0 * .12;
    diffMax = dMax0 + dSpan0 * .12;
    if(diffMin > 0) diffMin = Math.min(0, diffMin);
    if(diffMax < 0) diffMax = Math.max(0, diffMax);
    const diffStep = CC_SPREADS_POPUP_DIFF_AXIS_STEP_PCT;
    diffMin = Math.floor((diffMin - 1e-12) / diffStep) * diffStep;
    diffMax = Math.ceil((diffMax + 1e-12) / diffStep) * diffStep;
    if(diffMax <= diffMin) diffMax = diffMin + diffStep;
    const diffSpan = diffMax - diffMin || diffStep;
    dy = value => padT + (diffMax - value) / diffSpan * chartH;
  }
  const fundingByTs = st.fundingEnabled ? new Map((Array.isArray(st.fundingRows) ? st.fundingRows : []).map(r => [ccChartMinuteBucket(r.t), Number(r.v)])) : null;
  const funding = fundingByTs ? rows.map((r,i) => ({ x:x(i), t:ccChartMinuteBucket(r.t), v:Number(fundingByTs.get(ccChartMinuteBucket(r.t))) })).filter(p => Number.isFinite(p.v)) : [];
  let fundingMin = NaN, fundingMax = NaN, fundingSpan = NaN, fundingAutoSpan = NaN, fy = null;
  if(funding.length){
    const fVals = funding.map(p => p.v), fMin0 = Math.min(...fVals), fMax0 = Math.max(...fVals);
    const fSpan0 = fMax0 - fMin0 || Math.max(Math.abs(fMax0) * .2, .0001);
    const fAutoMin = fMin0 - fSpan0 * .12, fAutoMax = fMax0 + fSpan0 * .12;
    fundingAutoSpan = fAutoMax - fAutoMin || fSpan0;
    const manualFunding = st.fundingRange;
    const useFundingManual = manualFunding && Number.isFinite(Number(manualFunding.min)) && Number.isFinite(Number(manualFunding.max)) && Number(manualFunding.max) > Number(manualFunding.min);
    fundingMin = useFundingManual ? Number(manualFunding.min) : fAutoMin;
    fundingMax = useFundingManual ? Number(manualFunding.max) : fAutoMax;
    fundingSpan = fundingMax - fundingMin || fundingAutoSpan || 1;
    fy = value => padT + (fundingMax - value) / fundingSpan * chartH;
  }
  st.view = { padL, padR, padT, padB, chartW, chartH, w, h, visible, total:allRows.length, panOffset, min, max, span, autoSpan:autoMax - autoMin || span, fundingMin, fundingMax, fundingSpan, fundingAutoSpan, step };
  ctx.strokeStyle = 'rgba(42,46,53,.72)';
  ctx.lineWidth = 1;
  ctx.textAlign = 'left';
  if(Number.isFinite(axisRowMark) && Math.abs(axisRowMark) > 1e-9){
    const minPct = ccSpreadsPercentValue(min, axisRowMark);
    const maxPct = ccSpreadsPercentValue(max, axisRowMark);
    const spreadAxisStepPct = CC_SPREADS_POPUP_SPREAD_AXIS_STEP_PCT;
    const startPct = Math.ceil((Math.min(minPct, maxPct) - 1e-9) / spreadAxisStepPct) * spreadAxisStepPct;
    const endPct = Math.floor((Math.max(minPct, maxPct) + 1e-9) / spreadAxisStepPct) * spreadAxisStepPct;
    for(let pct = startPct; pct <= endPct + 1e-9; pct = Math.round((pct + spreadAxisStepPct) * 1000) / 1000){
      const value = pct / 100 * axisRowMark;
      const yy = crisp(y(value));
      if(yy < padT - 1 || yy > padT + chartH + 1) continue;
      ctx.beginPath(); ctx.moveTo(padL, yy); ctx.lineTo(w - padR, yy); ctx.stroke();
      ctx.fillStyle = '#8A8F98';
      ctx.fillText(ccSpreadsPercentUsdLabel(pct, axisRowMark), w - padR + 8, yy);
    }
  }else{
    for(let i = 0; i < 4; i++){
      const yy = crisp(padT + chartH * i / 3);
      ctx.beginPath(); ctx.moveTo(padL, yy); ctx.lineTo(w - padR, yy); ctx.stroke();
      ctx.fillStyle = '#8A8F98';
      ctx.fillText(ccFmtSignedNumberOrDash(max - (max - min) * i / 3, 2), w - padR + 8, yy);
    }
  }
  if(funding.length && fy){
    ctx.save();
    ctx.font = '10px Segoe UI';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.strokeStyle = 'rgba(245,158,11,.22)';
    const fundingAxisStepPct = CC_SPREADS_POPUP_FUNDING_AXIS_STEP_PCT;
    const fundingStart = Math.ceil((fundingMin - 1e-12) / fundingAxisStepPct) * fundingAxisStepPct;
    const fundingEnd = Math.floor((fundingMax + 1e-12) / fundingAxisStepPct) * fundingAxisStepPct;
    for(let value = fundingStart; value <= fundingEnd + 1e-12; value = Math.round((value + fundingAxisStepPct) * 1000000) / 1000000){
      const yy = crisp(fy(value));
      if(yy < padT - 1 || yy > padT + chartH + 1) continue;
      ctx.beginPath(); ctx.moveTo(w - padR, yy); ctx.lineTo(w - 6, yy); ctx.stroke();
      ctx.fillStyle = '#f59e0b';
      ctx.fillText(Number(value).toFixed(3) + '%', w - 56 + CC_SPREADS_POPUP_FUNDING_AXIS_LABEL_X_OFFSET, yy);
    }
    ctx.restore();
  }
  if(diff.length && dy){
    ctx.save();
    ctx.font = '10px Segoe UI';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.strokeStyle = 'rgba(167,139,250,.20)';
    const diffAxisStepPct = CC_SPREADS_POPUP_DIFF_AXIS_STEP_PCT;
    for(let value = diffMin; value <= diffMax + 1e-12; value = Math.round((value + diffAxisStepPct) * 1000000) / 1000000){
      const yy = crisp(dy(value));
      if(yy < padT - 1 || yy > padT + chartH + 1) continue;
      ctx.beginPath(); ctx.moveTo(6, yy); ctx.lineTo(padL, yy); ctx.stroke();
      ctx.fillStyle = CC_SPREADS_BID_ASK_DIFF_LINE_COLOR;
      ctx.fillText((value > 0 ? '+' : '') + Number(value).toFixed(2) + '%', padL - 6, yy);
    }
    ctx.restore();
  }
  ctx.textAlign = 'center';
  const tickCount = Math.min(8, rows.length);
  for(let i = 0; i < tickCount; i++){
    const idx = tickCount === 1 ? 0 : Math.round(i * (rows.length - 1) / (tickCount - 1));
    const xx = crisp(x(idx));
    ctx.strokeStyle = 'rgba(42,46,53,.45)';
    ctx.beginPath(); ctx.moveTo(xx, padT); ctx.lineTo(xx, padT + chartH); ctx.stroke();
    ctx.fillStyle = '#8A8F98';
    ctx.fillText(ccUtcHm(rows[idx].t), xx, h - 8);
  }
  const zeroY = min < 0 && max > 0 ? y(0) : NaN;
  if(Number.isFinite(zeroY)){
    ctx.strokeStyle = 'rgba(148,163,184,.55)';
    ctx.beginPath(); ctx.moveTo(padL, crisp(zeroY)); ctx.lineTo(w - padR, crisp(zeroY)); ctx.stroke();
  }
  const drawLine = (key, color, width=1.4, dash=[]) => {
    const pts = rows.map((r,i) => { const value = ccSpreadsChartSeriesValue(r, key); return { x:x(i), y:y(value), ok:Number.isFinite(value) }; });
    ctx.save(); ctx.strokeStyle = color; ctx.lineWidth = width; ctx.setLineDash(dash); ctx.beginPath();
    let open = false;
    pts.forEach(p => { if(!p.ok){ open = false; return; } if(open) ctx.lineTo(p.x, p.y); else { ctx.moveTo(p.x, p.y); open = true; } });
    ctx.stroke(); ctx.restore();
  };
  const drawBand = (values, color, width=1, dash=[]) => {
    const pts = (Array.isArray(values) ? values : []).map((value,i) => ({ x:x(i), y:y(Number(value)), ok:Number.isFinite(Number(value)) }));
    ctx.save(); ctx.strokeStyle = color; ctx.lineWidth = width; ctx.setLineDash(dash); ctx.beginPath();
    let open = false;
    pts.forEach(p => { if(!p.ok){ open = false; return; } if(open) ctx.lineTo(p.x, p.y); else { ctx.moveTo(p.x, p.y); open = true; } });
    ctx.stroke(); ctx.restore();
  };
  drawBand(bands.p1, '#38bdf8', 1, [5,5]);
  drawBand(bands.m1, '#38bdf8', 1, [5,5]);
  if(showBidAsk){
    drawLine('bid', '#166534', 1.1);
    drawLine('ask', '#991B1B', 1.1);
  }
  if(diff.length && dy){
    ctx.save();
    ctx.strokeStyle = CC_SPREADS_BID_ASK_DIFF_LINE_COLOR; ctx.lineWidth = 1.4; ctx.setLineDash([2,3]); ctx.beginPath();
    diff.forEach((p,i) => { const yy = dy(p.v); if(i) ctx.lineTo(p.x, yy); else ctx.moveTo(p.x, yy); });
    ctx.stroke();
    ctx.fillStyle = CC_SPREADS_BID_ASK_DIFF_LINE_COLOR; ctx.textAlign = 'left'; ctx.font = '11px Segoe UI';
    ctx.fillText('BidAskMedianDiff ' + (diff[diff.length - 1].v > 0 ? '+' : '') + ccFmtNumber(diff[diff.length - 1].v, 4) + '%', padL + 4, padT + 24);
    ctx.restore();
  }
  drawLine('spread', CC_SPREADS_MARK_LINE_COLOR, 1.7);
  if(st.fundingEnabled){
    if(funding.length && fy){
      ctx.save();
      ctx.strokeStyle = '#f59e0b'; ctx.lineWidth = 1.5; ctx.beginPath();
      funding.forEach((p,i) => { const yy = fy(p.v); if(i) ctx.lineTo(p.x, yy); else ctx.moveTo(p.x, yy); });
      ctx.stroke();
      ctx.fillStyle = '#f59e0b'; ctx.textAlign = 'left'; ctx.font = '11px Segoe UI';
      ctx.fillText('Funding ' + ccFmtNumber(funding[funding.length - 1].v, 6) + '%', padL + 4, padT + 8);
      ctx.restore();
    }
  }
  const last = rows[rows.length - 1], lastY = y(Number(last.spread));
  ctx.fillStyle = CC_SPREADS_MARK_LINE_COLOR;
  ctx.beginPath(); ctx.arc(x(rows.length - 1), lastY, 3, 0, Math.PI * 2); ctx.fill();
  ccDrawPriceTag(ctx, w - padR + 4, lastY, ccSpreadsSpreadAxisLabel(last.spread, axisRowMark), CC_SPREADS_MARK_LINE_COLOR, '#020617', w, padT, chartH);
  drawBand(bands.mean, CC_SPREADS_MA_LINE_COLOR, CC_SPREADS_MA_LINE_WIDTH);
  const latestMeanIndex = Array.isArray(bands.mean) ? (() => { for(let i = bands.mean.length - 1; i >= 0; i--){ if(Number.isFinite(Number(bands.mean[i]))) return i; } return -1; })() : -1;
  const latestMean = latestMeanIndex >= 0 ? Number(bands.mean[latestMeanIndex]) : NaN;
  if(Number.isFinite(latestMean)){
    const meanY = y(latestMean);
    let meanTagY = meanY;
    if(Math.abs(meanTagY - lastY) < 22) meanTagY = Math.max(padT + 10, Math.min(padT + chartH - 10, meanTagY + (meanY < lastY ? -22 : 22)));
    ctx.save();
    ctx.strokeStyle = CC_SPREADS_MA_LINE_COLOR;
    ctx.lineWidth = 1;
    ctx.setLineDash([]);
    ctx.beginPath(); ctx.moveTo(x(latestMeanIndex), meanY); ctx.lineTo(w - padR, meanY); ctx.stroke();
    if(Math.abs(meanTagY - meanY) > .5){ ctx.beginPath(); ctx.moveTo(w - padR, meanY); ctx.lineTo(w - padR, meanTagY); ctx.stroke(); }
    ctx.restore();
    ctx.fillStyle = CC_SPREADS_MA_LINE_COLOR;
    ctx.beginPath(); ctx.arc(x(latestMeanIndex), meanY, 4, 0, Math.PI * 2); ctx.fill();
    ccDrawPriceTag(ctx, w - padR + 4, meanTagY, ccSpreadsSpreadAxisLabel(latestMean, axisRowMark), CC_SPREADS_MA_TAG_COLOR, '#020617', w, padT, chartH, { prefix:'SpreadMA' });
  }
  const first = rows[0], source = CC_SPREADS_CHART_STATE.source || 'minute mark price';
  ccSpreadsSpreadChartStatus(rows.length + ' min · ' + ccUtcHm(first.t) + '-' + ccUtcHm(last.t) + ' UTC · MA' + CC_SPREADS_MEAN_WINDOW + ' ±0.05%' + (showBidAsk ? ' · Bid-Ask' : '') + (showDiff ? ' · BidAskMedianDiff' : '') + ' · ' + source + (st.fundingEnabled ? ' · Funding ' + (st.fundingRows.length || 0) + ' rows' : ''));
}
async function ccSpreadsLoadSpreadMinuteChart(row, col, liveSpread){
  if(!ccSpreadsPopupChartInterfaceEnabled()){
    CC_SPREADS_CHART_STATE.loading = false;
    ccSpreadsClearPopupChartTimers();
    return;
  }
  const rowSymbol = String(row?.symbol || '').toUpperCase();
  const colSymbol = String(col?.symbol || '').toUpperCase();
  const key = ccSpreadsChartKey(row, col);
  if(!rowSymbol || !colSymbol) return;
  const previousKey = CC_SPREADS_CHART_STATE.key;
  if(previousKey !== key){
    CC_SPREADS_CHART_STATE.key = key;
    CC_SPREADS_CHART_STATE.rows = [];
    CC_SPREADS_CHART_STATE.fundingRows = [];
    CC_SPREADS_CHART_STATE.error = '';
    CC_SPREADS_CHART_STATE.visibleCandles = 240;
    CC_SPREADS_CHART_STATE.panOffset = 0;
    CC_SPREADS_CHART_STATE.priceRange = null;
    CC_SPREADS_CHART_STATE.fundingRange = null;
    CC_SPREADS_CHART_STATE.fundingEnabled = false;
    CC_SPREADS_CHART_STATE.drag = null;
    ccSpreadsRestoreChartState(key);
  }
  ccSpreadsPushLiveSpread(row, col, liveSpread);
  ccSpreadsDrawSpreadChart();
  if(previousKey === key && (CC_SPREADS_CHART_STATE.loading || CC_SPREADS_CHART_STATE.rows.length > 1)) return;
  const seq = ++CC_SPREADS_CHART_STATE.requestSeq;
  CC_SPREADS_CHART_STATE.loading = true;
  CC_SPREADS_CHART_STATE.source = 'CoinCall futures minute markPriceClose';
  ccSpreadsDrawSpreadChart();
  try{
    const limit = 720;
    let rows = [];
    const fundingSymbol = ccSpreadsFundingSymbol(row, col);
    CC_SPREADS_CHART_STATE.fundingSymbol = fundingSymbol;
    if(!ccSpreadsIsSpotLeg(row) && !ccSpreadsIsSpotLeg(col)){
      try{
        const spreadRes = await ccApi('/api/admin/coincall/futures/spreads/minute?rowSymbol=' + encodeURIComponent(rowSymbol) + '&colSymbol=' + encodeURIComponent(colSymbol) + '&limit=' + limit);
        rows = (Array.isArray(spreadRes && spreadRes.rows) ? spreadRes.rows : []).map(ccSpreadsNormalizeSpreadRow).filter(r => Number.isFinite(r.t) && Number.isFinite(r.spread)).sort((a,b) => a.t - b.t);
        if(rows.length && rows.some(r => !Number.isFinite(Number(r.rowMark)) || !Number.isFinite(Number(r.bidAskSpread)))){
          const [rowRows, colRows] = await Promise.all([
            ccSpreadsLoadLegMinuteRows(row, limit),
            ccSpreadsLoadLegMinuteRows(col, limit)
          ]);
          rows = ccSpreadsAttachLegMarks(rows, rowRows, colRows);
        }
        if(rows.length) CC_SPREADS_CHART_STATE.source = 'CoinCall stored futures spread minute';
      }catch(e){}
    }
    if(!rows.length){
      const [rowRows, colRows] = await Promise.all([
        ccSpreadsLoadLegMinuteRows(row, limit),
        ccSpreadsLoadLegMinuteRows(col, limit)
      ]);
      rows = ccSpreadsBuildSpreadRows(rowRows, colRows);
      CC_SPREADS_CHART_STATE.source = ccSpreadsIsSpotLeg(row) || ccSpreadsIsSpotLeg(col) ? 'CoinCall spot index + futures markPriceClose archive' : 'CoinCall futures minute markPriceClose + bid/ask closes';
    }
    if(seq !== CC_SPREADS_CHART_STATE.requestSeq || CC_SPREADS_CHART_STATE.key !== key) return;
    if(!ccSpreadsPopupChartInterfaceEnabled()) return;
    CC_SPREADS_CHART_STATE.rows = rows.slice(-720);
    if(CC_SPREADS_CHART_STATE.fundingEnabled) CC_SPREADS_CHART_STATE.fundingRows = ccSpreadsMergeFundingRows(await ccSpreadsLoadFundingRows(fundingSymbol, limit), [{ t:ccSpreadsLatestSpreadTime(), v:ccSpreadsLatestFundingValue(CC_SPREADS_CHART_STATE.fundingRows) }]);
    if(CC_SPREADS_CHART_STATE.rows.length > 20 && Number(CC_SPREADS_CHART_STATE.visibleCandles) <= 20 && !CC_SPREADS_CHART_STATE.drag){
      CC_SPREADS_CHART_STATE.visibleCandles = Math.min(240, CC_SPREADS_CHART_STATE.rows.length);
    }
    CC_SPREADS_CHART_STATE.error = rows.length ? '' : 'No shared minute mark-price rows';
  }catch(e){
    if(seq !== CC_SPREADS_CHART_STATE.requestSeq || CC_SPREADS_CHART_STATE.key !== key) return;
    if(!ccSpreadsPopupChartInterfaceEnabled()) return;
    CC_SPREADS_CHART_STATE.source = 'Live quote accumulation';
    CC_SPREADS_CHART_STATE.error = 'History failed: ' + e.message;
  }finally{
    if(seq === CC_SPREADS_CHART_STATE.requestSeq && ccSpreadsPopupChartInterfaceEnabled()){
      CC_SPREADS_CHART_STATE.loading = false;
      ccSpreadsPushLiveSpread(row, col, liveSpread);
      ccSpreadsScheduleLiveFundingPoint('history');
      ccSpreadsDrawSpreadChart();
    }
  }
}
function ccSpreadsFutureTitle(item){
  const symbol = String(item?.symbol || '').toUpperCase();
  return ccFuturesCanonicalDisplaySymbolFromOrder(item) || String(item?.displayName || symbol || item?.rawLabel || item?.label || '—');
}
function ccSpreadsSpotTitle(symbol){
  const parts = ccSpreadsSpotParts(symbol);
  return 'Spot: ' + parts.base + '/' + parts.quote;
}
function ccSpreadsActionSymbol(item){
  return String(item?.displayName || item?.rawLabel || item?.symbol || item?.label || '—').toUpperCase();
}
function ccSpreadsFutureBookTitle(item){
  const symbol = String(item?.symbol || '').toUpperCase();
  const raw = ccSpreadsActionSymbol(item).replace(/[_\s]+/g,' ').trim();
  if(ccSpreadsIsPerpetualFuture(item)){
    const display = ccFuturesCanonicalDisplaySymbolFromOrder(item);
    if(display) return display;
    const base = ccSpreadsFuturesBase(raw || symbol);
    return raw && /PERP/i.test(raw) ? raw.replace(/\s*[- ]?PERP\s*$/i,' Perp') : (base ? base + 'USDT Perp' : (raw || symbol || 'Perp'));
  }
  const expiryMatch = (raw || symbol).match(/(\d{1,2}[A-Z]{3}\d{2})$/i);
  if(expiryMatch){
    const base = ccSpreadsFuturesBase(raw || symbol);
    return (base ? base + 'USDT-' : '') + expiryMatch[1].toUpperCase();
  }
  return raw || symbol || '—';
}
function ccSpreadsInstrumentChartTitle(item){
  if(ccSpreadsIsSpotLeg(item)){
    const parts = ccSpreadsSpotParts(item?.symbol || item?.displayName || '');
    return parts.base + '/' + parts.quote;
  }
  return ccSpreadsFutureBookTitle(item);
}
function ccSpreadsShortTickerLabel(item){
  if(ccSpreadsIsSpotLeg(item)) return 'SPOT';
  if(ccSpreadsIsPerpetualFuture(item)) return 'PERP';
  const raw = String(item?.displayName || item?.rawLabel || item?.symbol || item?.label || '').toUpperCase();
  const expiry = raw.match(/(\d{1,2})(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)(\d{2})$/i);
  if(expiry) return expiry[1].padStart(2,'0') + expiry[2][0].toUpperCase() + expiry[2].slice(1).toLowerCase() + expiry[3];
  const label = String(item?.label || '').trim();
  return label || ccSpreadsInstrumentChartTitle(item);
}
function ccSpreadsShortPairLabel(row, col){
  const rowShort = ccSpreadsShortTickerLabel(row);
  const colShort = ccSpreadsShortTickerLabel(col);
  if((rowShort === 'SPOT' && colShort === 'PERP') || (rowShort === 'PERP' && colShort === 'SPOT')) return 'PERP/SPOT';
  if(colShort && colShort !== 'SPOT') return colShort;
  if(rowShort && rowShort !== 'SPOT') return rowShort;
  return colShort || rowShort || '';
}
function ccSpreadsSpreadActionLegs(row, col){
  const rowPerp = ccSpreadsIsPerpetualFuture(row), colPerp = ccSpreadsIsPerpetualFuture(col);
  let fallbackBuy = col, fallbackSell = row;
  if(rowPerp !== colPerp){ fallbackBuy = rowPerp ? col : row; fallbackSell = rowPerp ? row : col; }
  return { buySpreadBuy:ccSpreadsActionSymbol(fallbackBuy), buySpreadSell:ccSpreadsActionSymbol(fallbackSell), sellSpreadSell:ccSpreadsActionSymbol(fallbackBuy), sellSpreadBuy:ccSpreadsActionSymbol(fallbackSell) };
}
function ccSpreadsSpreadActionsHtml(row, col){
  const legs = ccSpreadsSpreadActionLegs(row, col);
  return '<div class="okx-nitro-spread-actions">' +
    '<div class="okx-nitro-spread-action"><button type="button" class="btn okx-spot-submit buy">Buy Spread</button><div class="okx-nitro-spread-action-legs"><span class="okx-nitro-leg-buy">Buy: ' + ccEsc(legs.buySpreadBuy) + '</span><span class="okx-nitro-leg-sell">Sell: ' + ccEsc(legs.buySpreadSell) + '</span></div></div>' +
    '<div class="okx-nitro-spread-action"><button type="button" class="btn okx-spot-submit sell">Sell Spread</button><div class="okx-nitro-spread-action-legs"><span class="okx-nitro-leg-sell">Sell: ' + ccEsc(legs.sellSpreadSell) + '</span><span class="okx-nitro-leg-buy">Buy: ' + ccEsc(legs.sellSpreadBuy) + '</span></div></div>' +
  '</div>';
}
function ccSpreadsEnsureTradeModalRoot(){
  const modal = cc('ccSpreadsTradeModal');
  if(modal && modal.parentElement !== document.body) document.body.appendChild(modal);
  return modal;
}
function ccSpreadsRenderTradeModalContent(cell){
  ccSpreadsCaptureTradeFocus();
  ccSpreadsRememberTradePanels();
  const bottomScrollState = ccSpreadsCaptureBottomScroll();
  const body = cc('ccSpreadsTradeBody');
  if(!body || !cell) return false;
  const { row, col, rowMark, colMark, spread } = cell;
  const spotSymbol = ccSpreadsSpotSymbolForFuture(row || col);
  const title = cc('ccSpreadsTradeTitle');
  const sub = cc('ccSpreadsTradeSubtitle');
  const updated = cc('ccSpreadsTradeUpdated');
  const spreadText = ccFmtSignedNumberOrDash(spread, 2);
  const spreadChartKey = ccSpreadsChartKey(row, col);
  if(CC_SPREADS_CHART_STATE.key && CC_SPREADS_CHART_STATE.key !== spreadChartKey) CC_SPREADS_CHART_STATE.fundingEnabled = false;
  if(!ccSpreadsPopupChartInterfaceEnabled() && CC_SPREADS_CHART_STATE.key !== spreadChartKey){
    CC_SPREADS_CHART_STATE.key = spreadChartKey;
    CC_SPREADS_CHART_STATE.rows = [];
    CC_SPREADS_CHART_STATE.loading = false;
    CC_SPREADS_CHART_STATE.requestSeq++;
    ccSpreadsClearPopupChartTimers();
  }
  const bookRoles = ccSpreadsBookRoles(row, col);
  for(const entry of bookRoles){
    CC_SPREADS_BOOK_STATE.types[entry.role] = entry.role === 'spot' ? 'spot' : 'futures';
    CC_SPREADS_BOOK_STATE.items[entry.role] = entry.item || null;
    CC_SPREADS_BOOK_STATE.labels[entry.role] = entry.label || (entry.role === 'spot' ? 'Spot' : 'Futures');
  }
  if(title) title.textContent = ccSpreadsInstrumentChartTitle(col) + ' / ' + ccSpreadsInstrumentChartTitle(row);
  if(sub) sub.textContent = '';
  if(updated) updated.textContent = cc('ccSpreadsLastUpdate')?.textContent || '';
  ccRenderSpreadsModalAccount();
  body.innerHTML =
    '<div class="okx-nitro-top-row">' +
      '<div class="okx-nitro-modal-card okx-nitro-legs-card">' +
        '<div class="okx-nitro-legs-head"><h3 class="okx-nitro-section-dim">Leg details</h3><div class="okx-nitro-leg-spread">Spread <strong class="' + (spread < 0 ? 'okx-nitro-sell' : 'okx-nitro-buy') + '" data-cc-spreads-modal-role="spread">' + ccEsc(spreadText) + '</strong></div></div>' +
        '<table class="okx-nitro-legs-table"><thead><tr><th>Leg</th><th>Future</th><th>Side</th><th>Expiration</th><th>Mark price</th></tr></thead><tbody>' +
          '<tr><td>1</td><td>' + ccEsc(ccSpreadsFutureTitle(row)) + '</td><td class="okx-nitro-leg-sell">Sell</td><td>' + ccEsc(row.label || '—') + '</td><td data-cc-spreads-modal-role="row-mark">' + ccEsc(ccSpreadsFormatUsd(rowMark)) + '</td></tr>' +
          '<tr><td>2</td><td>' + ccEsc(ccSpreadsFutureTitle(col)) + '</td><td class="okx-nitro-leg-buy">Buy</td><td>' + ccEsc(col.label || '—') + '</td><td data-cc-spreads-modal-role="col-mark">' + ccEsc(ccSpreadsFormatUsd(colMark)) + '</td></tr>' +
        '</tbody></table>' +
        ccSpreadsSpreadActionsHtml(row, col) +
      '</div>' +
      '<div class="okx-nitro-modal-card okx-nitro-spread-chart-card">' +
        '<div class="okx-nitro-spread-chart-head"><h3 class="okx-nitro-section-dim">1Min Spread</h3><div class="cc-spreads-display-switch cc-spreads-chart-mode-switch" aria-label="1Min Spread mode"><button type="button" data-cc-spreads-chart-mode="trade-only" aria-pressed="' + (!ccSpreadsPopupChartInterfaceEnabled() ? 'true' : 'false') + '">Trade Only</button><button type="button" data-cc-spreads-chart-mode="interface" aria-pressed="' + (ccSpreadsPopupChartInterfaceEnabled() ? 'true' : 'false') + '">Interface</button></div><div class="cc-spreads-chart-legend"><span><i style="background:' + CC_SPREADS_MARK_LINE_COLOR + '"></i>MarkPrice spread</span><label><input type="checkbox" data-cc-spreads-bid-ask-overlay' + (CC_SPREADS_CHART_STATE.bidAskVisible ? ' checked' : '') + '> Bid-Ask</label><label><input type="checkbox" data-cc-spreads-bidask-diff-overlay' + (CC_SPREADS_CHART_STATE.bidAskDiffVisible ? ' checked' : '') + '> BidAskMedianDiff</label><span><i style="background:' + CC_SPREADS_MA_LINE_COLOR + '"></i>MA</span><span><i style="background:#38bdf8"></i>±0.05%</span><label><input type="checkbox" data-cc-spreads-funding-overlay' + (CC_SPREADS_CHART_STATE.fundingEnabled ? ' checked' : '') + '> Funding</label></div><span class="okx-nitro-spread-chart-status" id="ccSpreadsMinuteChartStatus">' + (ccSpreadsPopupChartInterfaceEnabled() ? 'Loading' : '') + '</span></div>' +
        '<div class="okx-nitro-spread-chart-wrap"><canvas class="okx-nitro-spread-chart" id="ccSpreadsMinuteChart" height="220" aria-label="1Min Spread"></canvas></div>' +
      '</div>' +
      '<div class="okx-nitro-modal-card okx-nitro-spread-positions-card">' +
        '<h3 class="okx-nitro-section-dim">Positions</h3>' +
        '<div data-cc-spreads-position-summary>' + ccSpreadsPopupPositionsSummaryHtml(row, col) + '</div>' +
      '</div>' +
    '</div>' +
    '<div class="cc-spreads-books-orders-row">' +
      '<div class="cc-spreads-instruments-grid">' + bookRoles.map(entry => ccSpreadsInstrumentPairHtml(entry)).join('') + '</div>' +
      '<div class="okx-nitro-modal-card cc-spreads-side-orders-card">' +
        '<h3 class="okx-nitro-section-dim">Orders</h3>' +
        '<div data-cc-spreads-side-orders>' + ccSpreadsSideOrdersTableHtml() + '</div>' +
      '</div>' +
    '</div>' +
    '<div class="okx-nitro-modal-card cc-spreads-positions-card">' +
      '<div data-cc-spreads-positions-orders data-cc-spreads-selected-panel="' + ccEsc(ccSpreadsCurrentPanel()) + '">' + ccSpreadsPositionsOrdersTableHtml(ccSpreadsCurrentPanel()) + '</div>' +
    '</div>';
  ccSpreadsSyncPopupChartModeButtons();
  ccSpreadsRefreshPrivateState();
  if(ccSpreadsPopupChartInterfaceEnabled()) ccSpreadsLoadSpreadMinuteChart(row, col, spread).catch(e => { CC_SPREADS_CHART_STATE.error = 'Chart failed: ' + e.message; CC_SPREADS_CHART_STATE.loading = false; ccSpreadsDrawSpreadChart(); });
  ccSpreadsEnsureModalBooks(row, col);
  ccSpreadsApplyTradePanelsState();
  ccSpreadsUpdateTradePanels();
  ccSpreadsRenderLatencyPanel(true);
  ccSpreadsRestoreTradeFocus();
  ccSpreadsRestoreBottomScroll(bottomScrollState);
  return true;
}
function ccSpreadsUpdateTradeModalLiveContent(cell){
  if(!cell) return false;
  const key = ccSpreadsChartKey(cell.row, cell.col);
  const canvas = cc('ccSpreadsMinuteChart');
  if(!canvas || !key || CC_SPREADS_CHART_STATE.key !== key) return false;
  const { row, col, rowMark, colMark, spread } = cell;
  const title = cc('ccSpreadsTradeTitle');
  const updated = cc('ccSpreadsTradeUpdated');
  const spreadEl = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-modal-role="spread"]');
  const rowMarkEl = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-modal-role="row-mark"]');
  const colMarkEl = document.querySelector('#ccSpreadsTradeModal [data-cc-spreads-modal-role="col-mark"]');
  if(title) title.textContent = String(col.symbol || col.label || 'Column future') + ' / ' + String(row.symbol || row.label || 'Row future');
  if(updated) updated.textContent = cc('ccSpreadsLastUpdate')?.textContent || '';
  if(spreadEl){
    spreadEl.textContent = ccFmtSignedNumberOrDash(spread, 2);
    spreadEl.classList.toggle('okx-nitro-sell', spread < 0);
    spreadEl.classList.toggle('okx-nitro-buy', !(spread < 0));
  }
  if(rowMarkEl) rowMarkEl.textContent = ccSpreadsFormatUsd(rowMark);
  if(colMarkEl) colMarkEl.textContent = ccSpreadsFormatUsd(colMark);
  if(ccSpreadsPopupChartInterfaceEnabled()) ccSpreadsLoadSpreadMinuteChart(row, col, spread).catch(e => { CC_SPREADS_CHART_STATE.error = 'Chart failed: ' + e.message; CC_SPREADS_CHART_STATE.loading = false; ccSpreadsDrawSpreadChart(); });
  ccSpreadsUpdateTradePanels();
  return true;
}
function ccSpreadsUpdateTradeModalContent(){
  if(!ccSpreadsTradeModalOpen() || !ccSpreadsTradeModalState) return false;
  const cell = ccSpreadsFindCellBySymbols(ccSpreadsTradeModalState.asset, ccSpreadsTradeModalState.rowSymbol, ccSpreadsTradeModalState.colSymbol);
  if(!cell) return false;
  return ccSpreadsUpdateTradeModalLiveContent(cell) || ccSpreadsRenderTradeModalContent(cell);
}
function ccSpreadsMainChartLabel(row, col){
  return ccSpreadsInstrumentChartTitle(col) + ' / ' + ccSpreadsInstrumentChartTitle(row);
}
function ccSpreadsMainChartFormatValue(value){
  const n = Number(value);
  if(!Number.isFinite(n)) return '—';
  return n.toLocaleString('en-US', { minimumFractionDigits: Math.abs(n) >= 100 ? 2 : 6, maximumFractionDigits: Math.abs(n) >= 100 ? 2 : 6 });
}
function ccSpreadsMainChartSetMeta(row){
  const meta = cc('ccSpreadsMainChartMeta');
  if(!meta) return;
  if(!row){ meta.textContent = ''; return; }
  const mark = ccSpreadsMainChartFormatValue(row.mark);
  const bid = ccSpreadsMainChartFormatValue(row.bid);
  const ask = ccSpreadsMainChartFormatValue(row.ask);
  const diff = ccSpreadsBidAskMedianDiffPct(row);
  const when = Number.isFinite(row.time) ? new Date(row.time * 1000).toISOString().slice(5,16).replace('T',' ') + ' UTC' : '';
  meta.textContent = 'MarkPrice ' + mark + ' · Bid ' + bid + ' · Ask ' + ask + (Number.isFinite(diff) ? ' · BidAskMedianDiff ' + (diff > 0 ? '+' : '') + diff.toFixed(4) + '%' : '') + (when ? ' · ' + when : '');
}
function ccSpreadsMainChartPointTime(raw){
  const value = raw?.minuteUtc || raw?.minute || raw?.time || raw?.t || raw?.timestamp;
  if(value === undefined || value === null) return NaN;
  if(typeof value === 'number') return value > 100000000000 ? Math.floor(value / 1000) : Math.floor(value);
  const text = String(value);
  const ms = Date.parse(text.endsWith('Z') ? text : text + 'Z');
  return Number.isFinite(ms) ? Math.floor(ms / 1000) : NaN;
}
function ccSpreadsMainChartNormalizeRows(rows, days){
  const cutoff = Math.floor(Date.now() / 1000) - Number(days || 3) * 86400;
  return (Array.isArray(rows) ? rows : [])
    .map(raw => {
      const row = ccSpreadsNormalizeSpreadRow(raw);
      return {
        time: ccSpreadsMainChartPointTime(raw),
        mark:row.spread,
        bidAskSpread:row.bidAskSpread,
        bid:row.bid,
        ask:row.ask,
        rowMark:row.rowMark,
        colMark:row.colMark,
        rowBidAskMedian:row.rowBidAskMedian,
        colBidAskMedian:row.colBidAskMedian
      };
    })
    .filter(row => Number.isFinite(row.time) && row.time >= cutoff && [row.mark,row.bid,row.ask].some(Number.isFinite))
    .sort((a,b) => a.time - b.time)
    .filter((row, index, arr) => index === 0 || row.time !== arr[index - 1].time);
}
function ccSpreadsMainLiveRow(row, col){
  const live = ccSpreadsLiveSpreadParts(row, col);
  if(!Number.isFinite(Number(live.spread))) return null;
  return {
    time:Math.floor(ccChartMinuteBucket(Date.now()) / 1000),
    mark:Number(live.spread),
    bidAskSpread:Number.isFinite(Number(live.bidAskSpread)) ? Number(live.bidAskSpread) : NaN,
    bid:NaN,
    ask:NaN,
    rowMark:Number.isFinite(Number(live.rowMark)) ? Number(live.rowMark) : NaN,
    colMark:Number.isFinite(Number(live.colMark)) ? Number(live.colMark) : NaN,
    rowBidAskMedian:Number.isFinite(Number(live.rowBidAskMedian)) ? Number(live.rowBidAskMedian) : NaN,
    colBidAskMedian:Number.isFinite(Number(live.colBidAskMedian)) ? Number(live.colBidAskMedian) : NaN,
    live:true
  };
}
function ccSpreadsMainMergeLiveRow(rows, row, col){
  const liveRow = ccSpreadsMainLiveRow(row, col);
  const list = (Array.isArray(rows) ? rows : []).slice();
  if(!liveRow) return list;
  const patch = target => {
    const out = Object.assign({}, target || {}, { time:liveRow.time, mark:liveRow.mark, live:true });
    ['bidAskSpread','rowMark','colMark','rowBidAskMedian','colBidAskMedian'].forEach(key => {
      if(Number.isFinite(Number(liveRow[key]))) out[key] = liveRow[key];
    });
    return out;
  };
  const lastIndex = list.length - 1;
  if(lastIndex >= 0 && Number(list[lastIndex].time) === liveRow.time) list[lastIndex] = patch(list[lastIndex]);
  else list.push(patch(liveRow));
  return list
    .filter(item => Number.isFinite(Number(item.time)) && Number.isFinite(Number(item.mark)))
    .sort((a,b) => Number(a.time) - Number(b.time))
    .filter((item, index, arr) => index === 0 || Number(item.time) !== Number(arr[index - 1].time));
}
function ccSpreadsMainLiveRowsForSource(rows, row, col, source){
  const merged = ccSpreadsMainMergeLiveRow(rows, row, col);
  if(ccSpreadsNormalizePriceSource(source) !== 'bidask') return merged;
  return merged.map(item => ({
    time:item.time,
    mark:item.bidAskSpread,
    rowMark:item.rowBidAskMedian,
    colMark:item.colBidAskMedian,
    live:item.live
  })).filter(item => Number.isFinite(Number(item.time)) && Number.isFinite(Number(item.mark)) && Number.isFinite(Number(item.rowMark)));
}
function ccSpreadsMainChartSeries(rows, key, asPercent=false){
  return rows
    .map(row => { const value = ccSpreadsChartSeriesValue(row, key); return { time: row.time, value: asPercent ? ccSpreadsPercentValue(value, row.rowMark) : value }; })
    .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value));
}
function ccSpreadsMainBandSeries(rows, values){
  return (Array.isArray(values) ? values : [])
    .map((value, index) => ({ time:rows[index]?.time, value:ccSpreadsPercentValue(value, rows[index]?.rowMark) }))
    .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value));
}
function ccSpreadsMainFundingSeries(rows){
  const fundingByTs = new Map((Array.isArray(CC_SPREADS_MAIN_CHART_STATE.fundingRows) ? CC_SPREADS_MAIN_CHART_STATE.fundingRows : []).map(row => [Math.floor(Number(row.t) / 1000), Number(row.v)]));
  return (Array.isArray(rows) ? rows : [])
    .map(row => ({ time: row.time, value: fundingByTs.get(Number(row.time)) }))
    .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value));
}
function ccSpreadsAllColor(index){
  return CC_SPREADS_ALL_COLORS[Math.abs(Number(index) || 0) % CC_SPREADS_ALL_COLORS.length];
}
function ccSpreadsRowAvailablePairs(rowIndex){
  const columns = ccSpreadsColumnsForAsset(ccSpreadsHeaderAsset);
  const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
  rowIndex = Number(rowIndex);
  if(!Number.isInteger(rowIndex)) return [];
  const row = rows[rowIndex];
  if(!row) return [];
  const source = ccSpreadsNormalizePriceSource(ccSpreadsPriceSource);
  const pairs = [];
  columns.forEach((col, colIndex) => {
    if(ccSpreadsCellUnavailable(rowIndex, colIndex, row, col)) return;
    const rowPrice = source === 'bidask' ? ccSpreadsCellPrice(row) : ccSpreadsMarkPrice(row);
    const colPrice = source === 'bidask' ? ccSpreadsCellPrice(col) : ccSpreadsMarkPrice(col);
    const spread = Number.isFinite(rowPrice) && Number.isFinite(colPrice) ? colPrice - rowPrice : NaN;
    if(!Number.isFinite(spread)) return;
    pairs.push({ row, col, rowIndex, colIndex, label:ccSpreadsMainChartLabel(row, col) });
  });
  return pairs;
}
function ccSpreadsAllRowsFromLegs(rowRows, colRows, source){
  const rows = ccSpreadsRowsForMainChart(rowRows, colRows);
  if(source === 'bidask'){
    return rows.map(row => ({
      time:row.time,
      mark:row.bidAskSpread,
      rowMark:row.rowBidAskMedian,
      colMark:row.colBidAskMedian
    })).filter(row => Number.isFinite(row.time) && Number.isFinite(row.mark) && Number.isFinite(row.rowMark));
  }
  return rows.filter(row => Number.isFinite(row.time) && Number.isFinite(row.mark) && Number.isFinite(row.rowMark));
}
function ccSpreadsAllChartSeries(rows, key, longKey){
  return (Array.isArray(rows) ? rows : [])
    .map(row => ({ time:row.time, value:Number(row && row[key]) }))
    .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value));
}
function ccSpreadsAllMaSeries(rows){
  const bands = ccSpreadsRollingSpreadBands(rows, 'mark', 'rowMark');
  return ccSpreadsAllChartSeries(rows.map((row, index) => Object.assign({}, row, { ma:bands.mean[index] })), 'ma', 'rowMark');
}
function ccSpreadsSeriesUpdateLast(series, data){
  if(!series || !series.update || !Array.isArray(data) || !data.length) return;
  const point = data[data.length - 1];
  if(point && Number.isFinite(Number(point.time)) && Number.isFinite(Number(point.value))) series.update(point);
}
function ccSpreadsMainUtcBoundaryTimes(points, range){
  let min = Number(range?.from), max = Number(range?.to);
  if(!Number.isFinite(min) || !Number.isFinite(max)){
    const times = (Array.isArray(points) ? points : []).map(point => Number(point?.time)).filter(Number.isFinite);
    if(!times.length) return [];
    min = Math.min(...times);
    max = Math.max(...times);
  }
  if(max < min){ const tmp = min; min = max; max = tmp; }
  const day = 86400, offsets = [0, 8 * 3600, 16 * 3600], out = [];
  const start = Math.floor(min / day) * day - day;
  const end = Math.ceil(max / day) * day + day;
  for(let base = start; base <= end; base += day){
    offsets.forEach(offset => {
      const t = base + offset;
      if(t >= min - day && t <= max + day) out.push(t);
    });
  }
  return out;
}
function ccSpreadsMainEnsureUtcOverlay(){
  const container = cc('ccSpreadsMainChart');
  if(!container || !ccSpreadsMainChart) return null;
  container.style.position = container.style.position || 'relative';
  let canvas = cc('ccSpreadsMainUtcOverlay');
  if(!canvas || canvas.parentElement !== container){
    canvas = document.createElement('canvas');
    canvas.id = 'ccSpreadsMainUtcOverlay';
    canvas.setAttribute('aria-hidden', 'true');
    canvas.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;pointer-events:none;z-index:5;';
    container.appendChild(canvas);
  }
  return canvas;
}
function ccSpreadsMainDrawUtcOverlay(){
  const canvas = ccSpreadsMainEnsureUtcOverlay();
  if(!canvas || !ccSpreadsMainChart || !ccSpreadsMainChart.timeScale) return;
  const container = cc('ccSpreadsMainChart');
  const w = Math.max(0, container?.clientWidth || 0), h = Math.max(0, container?.clientHeight || 340);
  if(!w || !h) return;
  const dpr = Math.max(1, window.devicePixelRatio || 1);
  if(canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)){
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
  }
  const ctx = canvas.getContext('2d');
  if(!ctx) return;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  const range = ccSpreadsMainChart.timeScale().getVisibleRange ? ccSpreadsMainChart.timeScale().getVisibleRange() : null;
  const dataTimes = Array.isArray(CC_SPREADS_MAIN_CHART_STATE.utcBoundaryTimes) ? CC_SPREADS_MAIN_CHART_STATE.utcBoundaryTimes.map(time => ({ time })) : [];
  const times = ccSpreadsMainUtcBoundaryTimes(dataTimes, range);
  if(!times.length) return;
  ctx.save();
  ctx.strokeStyle = 'rgba(88,28,135,.82)';
  ctx.lineWidth = 2;
  ctx.setLineDash([4, 5]);
  times.forEach(time => {
    const x = ccSpreadsMainChart.timeScale().timeToCoordinate(time);
    if(!Number.isFinite(x) || x < 0 || x > w) return;
    const xx = Math.round(x) + .5;
    ctx.beginPath();
    ctx.moveTo(xx, 0);
    ctx.lineTo(xx, h);
    ctx.stroke();
  });
  ctx.restore();
}
function ccSpreadsMainScheduleUtcOverlay(){
  cancelAnimationFrame(CC_SPREADS_MAIN_CHART_STATE.utcOverlayRaf || 0);
  CC_SPREADS_MAIN_CHART_STATE.utcOverlayRaf = requestAnimationFrame(() => {
    CC_SPREADS_MAIN_CHART_STATE.utcOverlayRaf = 0;
    ccSpreadsMainDrawUtcOverlay();
  });
}
function ccSpreadsMainBindUtcOverlay(chart){
  if(!chart || !chart.timeScale) return;
  const draw = () => ccSpreadsMainScheduleUtcOverlay();
  try{ chart.timeScale().subscribeVisibleTimeRangeChange(draw); }catch(e){}
  try{ chart.timeScale().subscribeVisibleLogicalRangeChange(draw); }catch(e){}
  setTimeout(draw, 0);
}
function ccSpreadsMainAllRenderKey(){
  return [
    'row',
    ccSpreadsHeaderAsset,
    Number(CC_SPREADS_MAIN_CHART_STATE.rowIndex),
    ccSpreadsNormalizePriceSource(CC_SPREADS_MAIN_CHART_STATE.allSource || ccSpreadsPriceSource),
    Number(CC_SPREADS_MAIN_CHART_STATE.days || 3),
    (CC_SPREADS_MAIN_CHART_STATE.allPairs || []).map(pair => String(pair.row?.symbol || '') + '>' + String(pair.col?.symbol || '')).join(',')
  ].join('|');
}
function ccSpreadsAllSetMeta(items){
  const meta = cc('ccSpreadsMainChartMeta');
  if(!meta) return;
  const count = Array.isArray(items) ? items.length : 0;
  const source = ccSpreadsPriceSourceLabel();
  const rowLabel = String(CC_SPREADS_MAIN_CHART_STATE.row?.label || CC_SPREADS_MAIN_CHART_STATE.row?.symbol || '').trim();
  meta.textContent = 'Row Spreads' + (rowLabel ? ': ' + rowLabel : '') + ' · ' + source + ' · ' + count + ' pairs · Spread + SpreadMA';
}
function ccSpreadsMainPairKey(item){
  const col = item && item.col ? item.col : item;
  const symbol = String(col?.symbol || item?.colSymbol || '').trim().toUpperCase();
  if(symbol) return symbol;
  const index = Number(item?.colIndex);
  return Number.isInteger(index) ? 'col:' + index : '';
}
function ccSpreadsMainPairLabel(item){
  const col = item && item.col ? item.col : item;
  const row = item && item.row ? item.row : null;
  return String(ccSpreadsShortPairLabel(row, col) || col?.label || ccSpreadsInstrumentChartTitle(col) || col?.displayName || col?.symbol || '').trim();
}
function ccSpreadsMainPairVisible(key){
  if(!key) return true;
  const visibility = CC_SPREADS_MAIN_CHART_STATE.allPairVisibility || {};
  return visibility[key] !== false;
}
function ccSpreadsMainResetPairVisibility(pairs){
  const next = {};
  (Array.isArray(pairs) ? pairs : []).forEach(pair => {
    const key = ccSpreadsMainPairKey(pair);
    if(key) next[key] = true;
  });
  CC_SPREADS_MAIN_CHART_STATE.allPairVisibility = next;
  ccSpreadsMainRenderPairToggles([]);
}
function ccSpreadsMainRenderPairToggles(items){
  const host = cc('ccSpreadsMainPairToggles');
  if(!host) return;
  const list = (Array.isArray(items) ? items : []).filter(item => ccSpreadsMainPairKey(item) && ccSpreadsMainPairLabel(item));
  if(CC_SPREADS_MAIN_CHART_STATE.mode !== 'row' || !list.length){
    host.hidden = true;
    host.innerHTML = '';
    return;
  }
  host.hidden = false;
  host.innerHTML = list.map(item => {
    const key = ccSpreadsMainPairKey(item);
    const checked = ccSpreadsMainPairVisible(key) ? ' checked' : '';
    return '<label title="' + ccEsc(ccSpreadsMainPairLabel(item)) + '"><input type="checkbox" data-cc-spreads-main-pair-toggle="' + ccEsc(key) + '"' + checked + ' autocomplete="off">' + ccEsc(ccSpreadsMainPairLabel(item)) + '</label>';
  }).join('');
}
function ccSpreadsMainApplyPairVisibility(key, visible){
  key = String(key || '').toUpperCase();
  if(!key) return;
  const visibility = CC_SPREADS_MAIN_CHART_STATE.allPairVisibility || (CC_SPREADS_MAIN_CHART_STATE.allPairVisibility = {});
  visibility[key] = !!visible;
  ccSpreadsSaveCurrentMainChartSettings({ mode:'row', colSymbol:'', pairVisibility:visibility });
  ccSpreadsMainApplyAllPairVisibility();
}
function ccSpreadsMainAllActiveCount(entries){
  return (Array.isArray(entries) ? entries : []).filter(entry => {
    const rows = Array.isArray(entry?.item?.rows) ? entry.item.rows : [];
    return rows.length && ccSpreadsMainPairVisible(entry?.key);
  }).length;
}
function ccSpreadsMainApplyAllPairVisibility(){
  const series = CC_SPREADS_MAIN_CHART_STATE.mainSeries || {};
  if(!Array.isArray(series.all)) return;
  const activeCount = ccSpreadsMainAllActiveCount(series.all);
  series.all.forEach(entry => {
    if(!entry) return;
    const rows = Array.isArray(entry.item?.rows) ? entry.item.rows : [];
    const pairVisible = rows.length && ccSpreadsMainPairVisible(entry.key);
    const spreadVisible = pairVisible && activeCount === 1;
    const maVisible = pairVisible;
    if(entry.spread && entry.spread.setData) entry.spread.setData(spreadVisible ? ccSpreadsAllChartSeries(rows, 'mark') : []);
    if(entry.ma && entry.ma.setData) entry.ma.setData(maVisible ? ccSpreadsAllMaSeries(rows) : []);
    if(entry.spread && entry.spread.applyOptions) entry.spread.applyOptions({ visible:!!spreadVisible });
    if(entry.ma && entry.ma.applyOptions) entry.ma.applyOptions({ visible:!!maVisible });
  });
}
function ccSpreadsMainRenderAllChart(items){
  const container = cc('ccSpreadsMainChart');
  if(!container) return;
  const renderKey = ccSpreadsMainAllRenderKey();
  const activeItems = (Array.isArray(items) ? items : []).filter(item => Array.isArray(item.rows) && item.rows.length);
  if(!activeItems.length){
    if(ccSpreadsMainResizeObserver){ try{ ccSpreadsMainResizeObserver.disconnect(); }catch(e){} }
    ccSpreadsMainResizeObserver = null;
    if(ccSpreadsMainChart){ try{ ccSpreadsMainChart.remove(); }catch(e){} }
    ccSpreadsMainChart = null;
    CC_SPREADS_MAIN_CHART_STATE.mainSeries = null;
    CC_SPREADS_MAIN_CHART_STATE.mainRenderKey = '';
    container.innerHTML = '<div class="okx-nitro-status">No row spread data available for ' + ccEsc(ccSpreadsPriceSourceLabel()) + '</div>';
    ccSpreadsAllSetMeta([]);
    ccSpreadsMainRenderPairToggles([]);
    return;
  }
  if(!window.LightweightCharts){
    container.innerHTML = '<div class="okx-nitro-status danger">Chart library is unavailable</div>';
    ccSpreadsAllSetMeta(activeItems);
    ccSpreadsMainRenderPairToggles(activeItems);
    return;
  }
  container.innerHTML = '';
  if(ccSpreadsMainResizeObserver){ try{ ccSpreadsMainResizeObserver.disconnect(); }catch(e){} }
  ccSpreadsMainResizeObserver = null;
  if(ccSpreadsMainChart){ try{ ccSpreadsMainChart.remove(); }catch(e){} }
  ccSpreadsMainChart = null;
  CC_SPREADS_MAIN_CHART_STATE.mainSeries = null;
  const width = Math.max(container.clientWidth || 0, 320);
  const chart = LightweightCharts.createChart(container, {
    layout:{ background:{ color:'#0b1220' }, textColor:'#94a3b8' },
    grid:{ vertLines:{ color:'#1e293b' }, horzLines:{ color:'#1e293b' } },
    crosshair:{ mode:LightweightCharts.CrosshairMode.Normal },
    rightPriceScale:{ borderColor:'#334155' },
    timeScale:{ borderColor:'#334155', timeVisible:true, secondsVisible:false },
    width,
    height:340
  });
  ccSpreadsMainChart = chart;
  CC_SPREADS_MAIN_CHART_STATE.mainRenderKey = renderKey;
  const solidLineStyle = LightweightCharts.LineStyle && LightweightCharts.LineStyle.Solid !== undefined ? LightweightCharts.LineStyle.Solid : 0;
  const series = { all:[] };
  let longest = [];
  activeItems.forEach((item, index) => {
    const color = ccSpreadsAllColor(index);
    const priceFormat = { type:'custom', minMove:0.02, formatter:value => ccSpreadsMainChartFormatValue(value) };
    const shortLabel = ccSpreadsMainPairLabel(item) || item.label || '';
    const spreadSeries = chart.addLineSeries({ color, lineWidth:1, priceLineVisible:false, lastValueVisible:true, title:shortLabel, priceFormat });
    const maSeries = chart.addLineSeries({ color, lineWidth:2, lineStyle:solidLineStyle, priceLineVisible:false, lastValueVisible:true, title:shortLabel, priceFormat });
    const spreadData = ccSpreadsAllChartSeries(item.rows, 'mark');
    const maData = ccSpreadsAllMaSeries(item.rows);
    const key = ccSpreadsMainPairKey(item);
    const visible = ccSpreadsMainPairVisible(key);
    spreadSeries.setData(visible ? spreadData : []);
    maSeries.setData(visible ? maData : []);
    if(spreadSeries.applyOptions) spreadSeries.applyOptions({ visible });
    if(maSeries.applyOptions) maSeries.applyOptions({ visible });
    series.all.push({ spread:spreadSeries, ma:maSeries, item, color, key });
    if(spreadData.length > longest.length) longest = spreadData;
  });
  CC_SPREADS_MAIN_CHART_STATE.mainSeries = series;
  CC_SPREADS_MAIN_CHART_STATE.mainRows = activeItems;
  CC_SPREADS_MAIN_CHART_STATE.utcBoundaryTimes = ccSpreadsMainUtcBoundaryTimes(longest);
  ccSpreadsMainApplyAllPairVisibility();
  ccSpreadsAllSetMeta(activeItems);
  ccSpreadsMainRenderPairToggles(activeItems);
  if(longest.length >= 2){
    const rightOffset = CC_SPREADS_MAIN_CHART_STATE.days >= 7 ? 2 : 1;
    const barSpacing = Math.max(0.02, width / (longest.length + rightOffset));
    chart.applyOptions({ timeScale:{ minBarSpacing:0.01, barSpacing, rightOffset, fixLeftEdge:true, fixRightEdge:false } });
    chart.timeScale().setVisibleLogicalRange({ from:0, to:longest.length - 1 + rightOffset });
  }else{
    chart.timeScale().fitContent();
  }
  ccSpreadsMainResizeObserver = new ResizeObserver(() => {
    chart.applyOptions({ width:Math.max(container.clientWidth || 0, 320) });
    ccSpreadsMainScheduleUtcOverlay();
  });
  ccSpreadsMainResizeObserver.observe(container);
  ccSpreadsMainBindUtcOverlay(chart);
}
function ccSpreadsMainUpdateRowModeLive(symbols, dirtySpotIndexSymbols){
  const section = cc('ccSpreadsMainChartSection');
  const series = CC_SPREADS_MAIN_CHART_STATE.mainSeries || {};
  const activeItems = Array.isArray(CC_SPREADS_MAIN_CHART_STATE.mainRows) ? CC_SPREADS_MAIN_CHART_STATE.mainRows : [];
  if(!section || section.hidden || CC_SPREADS_MAIN_CHART_STATE.mode !== 'row' || !Array.isArray(series.all) || !activeItems.length) return false;
  const source = ccSpreadsNormalizePriceSource(CC_SPREADS_MAIN_CHART_STATE.allSource || ccSpreadsPriceSource);
  let changed = false;
  series.all.forEach(entry => {
    const item = entry && entry.item;
    if(!item || !ccSpreadsDirtySymbolsMatchSelection(symbols, item.row?.symbol, item.col?.symbol, dirtySpotIndexSymbols)) return;
    const nextRows = ccSpreadsMainLiveRowsForSource(item.rows, item.row, item.col, source);
    if(!nextRows.length) return;
    item.rows = nextRows;
    if(!ccSpreadsMainPairVisible(entry.key)){
      changed = true;
      return;
    }
    const activeCount = ccSpreadsMainAllActiveCount(series.all);
    const spreadData = ccSpreadsAllChartSeries(nextRows, 'mark');
    const maData = ccSpreadsAllMaSeries(nextRows);
    if(activeCount === 1) ccSpreadsSeriesUpdateLast(entry.spread, spreadData);
    ccSpreadsSeriesUpdateLast(entry.ma, maData);
    changed = true;
  });
  if(changed){
    CC_SPREADS_MAIN_CHART_STATE.mainRows = activeItems;
    const longest = activeItems.map(item => ccSpreadsAllChartSeries(item.rows, 'mark')).sort((a,b) => b.length - a.length)[0] || [];
    CC_SPREADS_MAIN_CHART_STATE.utcBoundaryTimes = ccSpreadsMainUtcBoundaryTimes(longest);
    ccSpreadsMainScheduleUtcOverlay();
    ccSpreadsAllSetMeta(activeItems);
  }
  return changed;
}
async function ccSpreadsMainLoadRowChart(rowIndex, days){
  const section = cc('ccSpreadsMainChartSection');
  const container = cc('ccSpreadsMainChart');
  if(!section || !container) return;
  const seq = ++CC_SPREADS_MAIN_CHART_STATE.requestSeq;
  const source = ccSpreadsNormalizePriceSource(ccSpreadsPriceSource);
  const pairs = ccSpreadsRowAvailablePairs(rowIndex);
  const row = pairs[0]?.row || ccSpreadsRowsForAsset(ccSpreadsHeaderAsset)[Number(rowIndex)] || null;
  CC_SPREADS_MAIN_CHART_STATE.mode = 'row';
  CC_SPREADS_MAIN_CHART_STATE.row = row;
  CC_SPREADS_MAIN_CHART_STATE.col = null;
  CC_SPREADS_MAIN_CHART_STATE.rowIndex = Number(rowIndex);
  CC_SPREADS_MAIN_CHART_STATE.allPairs = pairs;
  CC_SPREADS_MAIN_CHART_STATE.allSource = source;
  ccSpreadsMainResetPairVisibility(pairs);
  ccSpreadsRestoreMainPairVisibility(row, pairs);
  ccSpreadsSaveCurrentMainChartSettings({ mode:'row', rowSymbol:String(row?.symbol || '').toUpperCase(), colSymbol:'', pairVisibility:CC_SPREADS_MAIN_CHART_STATE.allPairVisibility });
  section.hidden = false;
  document.querySelectorAll('#coincallSpreadsGridHost [data-cc-spreads-clickable="1"]').forEach(el => el.classList.remove('selected'));
  const rowLabel = String(row?.label || row?.symbol || '').trim();
  cc('ccSpreadsMainChartTitle').textContent = 'Row Spreads: ' + (rowLabel || 'Row ' + (Number(rowIndex) + 1)) + ' · ' + ccSpreadsPriceSourceLabel();
  ccSpreadsAllSetMeta([]);
  container.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:340px"><span class="spinner"></span></div>';
  try{
    const limit = Math.max(60, Number(days || 3) * 1440);
    const legCache = new Map();
    const loadLeg = leg => {
      const symbol = String(leg?.symbol || '').toUpperCase();
      if(!symbol) return Promise.resolve([]);
      if(!legCache.has(symbol)) legCache.set(symbol, ccSpreadsLoadLegMinuteRows(leg, limit));
      return legCache.get(symbol);
    };
    const items = await Promise.all(pairs.map(async pair => {
      const [rowRows, colRows] = await Promise.all([loadLeg(pair.row), loadLeg(pair.col)]);
      return Object.assign({}, pair, { rows:ccSpreadsAllRowsFromLegs(rowRows, colRows, source) });
    }));
    if(seq !== CC_SPREADS_MAIN_CHART_STATE.requestSeq || CC_SPREADS_MAIN_CHART_STATE.mode !== 'row') return;
    ccSpreadsMainRenderAllChart(items);
  }catch(e){
    if(seq !== CC_SPREADS_MAIN_CHART_STATE.requestSeq) return;
    container.innerHTML = '<div class="okx-nitro-status danger">Failed: ' + ccEsc(e.message || e) + '</div>';
    ccSpreadsAllSetMeta([]);
  }
}
function ccSpreadsMainChartRenderKey(){
  return [
    ccSpreadsChartKey(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col),
    Number(CC_SPREADS_MAIN_CHART_STATE.days || 3),
    'mark',
    CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled ? 'ba1' : 'ba0',
    CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled ? 'bd1' : 'bd0',
    CC_SPREADS_MAIN_CHART_STATE.fundingEnabled ? 'fn1' : 'fn0'
  ].join('|');
}
function ccSpreadsMainChartData(rows){
  const axisRowMark = ccSpreadsReferenceRowMark(rows);
  const spreadPriceFormat = { type:'custom', minMove:0.02, formatter:value => ccSpreadsPercentUsdLabel(value, axisRowMark) };
  const diffPriceFormat = { type:'custom', minMove:0.0001, formatter:value => (Number(value) > 0 ? '+' : '') + Number(value).toFixed(4) + '%' };
  const bands = ccSpreadsRollingSpreadBands(rows, 'mark', 'rowMark');
  const fundingData = ccSpreadsMainFundingSeries(rows);
  const diffData = rows
    .map(row => ({ time:row.time, value:ccSpreadsBidAskMedianDiffPct(row) }))
    .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value));
  return {
    spreadPriceFormat,
    diffPriceFormat,
    markData:ccSpreadsMainChartSeries(rows, 'mark', true),
    bandP1:ccSpreadsMainBandSeries(rows, bands.p1),
    bandM1:ccSpreadsMainBandSeries(rows, bands.m1),
    bandMean:ccSpreadsMainBandSeries(rows, bands.mean),
    bidData:ccSpreadsMainChartSeries(rows, 'bid', true),
    askData:ccSpreadsMainChartSeries(rows, 'ask', true),
    diffData,
    fundingData,
    fundingZeroData:fundingData.length ? [{ time:fundingData[0].time, value:0 }, { time:fundingData[fundingData.length - 1].time, value:0 }] : []
  };
}
function ccSpreadsMainLatestMaPoint(data){
  const rows = Array.isArray(data && data.bandMean) ? data.bandMean : [];
  for(let i = rows.length - 1; i >= 0; i--){
    const point = rows[i];
    if(point && Number.isFinite(Number(point.value))) return point;
  }
  return null;
}
function ccSpreadsMainUpdateMaPriceLine(series, data, label){
  if(!series || !series.bandMean) return;
  const point = ccSpreadsMainLatestMaPoint(data);
  const style = window.LightweightCharts && LightweightCharts.LineStyle && LightweightCharts.LineStyle.Solid !== undefined ? LightweightCharts.LineStyle.Solid : 0;
  if(point){
    const title = String(label || ccSpreadsShortPairLabel(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col) || 'SpreadMA');
    const options = { price:Number(point.value), color:CC_SPREADS_MA_TAG_COLOR, lineWidth:1, lineStyle:style, lineVisible:false, axisLabelVisible:true, title };
    if(series.maPriceLine && series.maPriceLine.applyOptions) series.maPriceLine.applyOptions(options);
    else if(series.bandMean.createPriceLine) series.maPriceLine = series.bandMean.createPriceLine(options);
  }else if(series.maPriceLine && series.bandMean.removePriceLine){
    try{ series.bandMean.removePriceLine(series.maPriceLine); }catch(e){}
    series.maPriceLine = null;
  }
}
function ccSpreadsMainChartUpdateData(rows){
  const series = CC_SPREADS_MAIN_CHART_STATE.mainSeries || {};
  CC_SPREADS_MAIN_CHART_STATE.mainRows = Array.isArray(rows) ? rows : [];
  const data = ccSpreadsMainChartData(rows);
  CC_SPREADS_MAIN_CHART_STATE.utcBoundaryTimes = ccSpreadsMainUtcBoundaryTimes(data.markData);
  ['mark','bandP1','bandM1','bandMean','bid','ask'].forEach(key => { if(series[key] && series[key].applyOptions) series[key].applyOptions({ priceFormat:data.spreadPriceFormat }); });
  if(series.diff && series.diff.applyOptions) series.diff.applyOptions({ priceFormat:data.diffPriceFormat });
  if(series.bandP1) series.bandP1.setData(data.bandP1);
  if(series.bandM1) series.bandM1.setData(data.bandM1);
  if(series.mark) series.mark.setData(data.markData);
  if(series.bid) series.bid.setData(data.bidData);
  if(series.ask) series.ask.setData(data.askData);
  if(series.diff) series.diff.setData(data.diffData);
  if(series.fundingZero) series.fundingZero.setData(data.fundingZeroData);
  if(series.funding) series.funding.setData(data.fundingData);
  if(series.bandMean) series.bandMean.setData(data.bandMean);
  ccSpreadsMainUpdateMaPriceLine(series, data);
  const lastRow = rows[rows.length - 1] || null;
  CC_SPREADS_MAIN_CHART_STATE.mainLastRow = lastRow;
  CC_SPREADS_MAIN_CHART_STATE.mainRowByTime = new Map(rows.map(row => [row.time, row]));
  ccSpreadsMainChartSetMeta(lastRow);
  ccSpreadsMainScheduleUtcOverlay();
  return data;
}
function ccSpreadsMainChartUpdateLatestData(rows){
  const series = CC_SPREADS_MAIN_CHART_STATE.mainSeries || {};
  if(!series.mark && !series.bandMean) return ccSpreadsMainChartUpdateData(rows);
  CC_SPREADS_MAIN_CHART_STATE.mainRows = Array.isArray(rows) ? rows : [];
  const data = ccSpreadsMainChartData(rows);
  CC_SPREADS_MAIN_CHART_STATE.utcBoundaryTimes = ccSpreadsMainUtcBoundaryTimes(data.markData);
  ['mark','bandP1','bandM1','bandMean','bid','ask'].forEach(key => { if(series[key] && series[key].applyOptions) series[key].applyOptions({ priceFormat:data.spreadPriceFormat }); });
  if(series.diff && series.diff.applyOptions) series.diff.applyOptions({ priceFormat:data.diffPriceFormat });
  ccSpreadsSeriesUpdateLast(series.bandP1, data.bandP1);
  ccSpreadsSeriesUpdateLast(series.bandM1, data.bandM1);
  ccSpreadsSeriesUpdateLast(series.mark, data.markData);
  ccSpreadsSeriesUpdateLast(series.bid, data.bidData);
  ccSpreadsSeriesUpdateLast(series.ask, data.askData);
  ccSpreadsSeriesUpdateLast(series.diff, data.diffData);
  ccSpreadsSeriesUpdateLast(series.fundingZero, data.fundingZeroData);
  ccSpreadsSeriesUpdateLast(series.funding, data.fundingData);
  ccSpreadsSeriesUpdateLast(series.bandMean, data.bandMean);
  ccSpreadsMainUpdateMaPriceLine(series, data);
  const lastRow = rows[rows.length - 1] || null;
  CC_SPREADS_MAIN_CHART_STATE.mainLastRow = lastRow;
  CC_SPREADS_MAIN_CHART_STATE.mainRowByTime = new Map(rows.map(row => [row.time, row]));
  ccSpreadsMainChartSetMeta(lastRow);
  ccSpreadsMainScheduleUtcOverlay();
  return data;
}
function ccSpreadsMainChartHandleCrosshair(param){
  const lastRow = CC_SPREADS_MAIN_CHART_STATE.mainLastRow || null;
  if(!param || param.time === undefined || param.time === null){ ccSpreadsMainChartSetMeta(lastRow); return; }
  const time = typeof param.time === 'object' ? param.time.timestamp : param.time;
  const rowByTime = CC_SPREADS_MAIN_CHART_STATE.mainRowByTime;
  ccSpreadsMainChartSetMeta((rowByTime && rowByTime.get(Number(time))) || lastRow);
}
function ccSpreadsMainRenderChart(rows){
  const container = cc('ccSpreadsMainChart');
  if(!container) return;
  const renderKey = ccSpreadsMainChartRenderKey();
  if(!rows.length){
    if(ccSpreadsMainResizeObserver){ try{ ccSpreadsMainResizeObserver.disconnect(); }catch(e){} }
    ccSpreadsMainResizeObserver = null;
    if(ccSpreadsMainChart){ try{ ccSpreadsMainChart.remove(); }catch(e){} }
    ccSpreadsMainChart = null;
    CC_SPREADS_MAIN_CHART_STATE.mainSeries = null;
    CC_SPREADS_MAIN_CHART_STATE.mainRenderKey = '';
    container.innerHTML = '<div class="okx-nitro-status">No data available</div>';
    ccSpreadsMainChartSetMeta(null);
    return;
  }
  if(!window.LightweightCharts){
    container.innerHTML = '<div class="okx-nitro-status danger">Chart library is unavailable</div>';
    ccSpreadsMainChartSetMeta(null);
    return;
  }
  if(ccSpreadsMainChart && CC_SPREADS_MAIN_CHART_STATE.mainRenderKey === renderKey && CC_SPREADS_MAIN_CHART_STATE.mainSeries){
    ccSpreadsMainChartUpdateData(rows);
    return;
  }
  container.innerHTML = '';
  if(ccSpreadsMainResizeObserver){ try{ ccSpreadsMainResizeObserver.disconnect(); }catch(e){} }
  ccSpreadsMainResizeObserver = null;
  if(ccSpreadsMainChart){ try{ ccSpreadsMainChart.remove(); }catch(e){} }
  ccSpreadsMainChart = null;
  CC_SPREADS_MAIN_CHART_STATE.mainSeries = null;
  const width = Math.max(container.clientWidth || 0, 320);
  const chart = LightweightCharts.createChart(container, {
    layout: { background: { color: '#0b1220' }, textColor: '#94a3b8' },
    grid: { vertLines: { color: '#1e293b' }, horzLines: { color: '#1e293b' } },
    crosshair: { mode: LightweightCharts.CrosshairMode.Normal },
    rightPriceScale: { borderColor: '#334155' },
    leftPriceScale: { visible: CC_SPREADS_MAIN_CHART_STATE.fundingEnabled, borderColor: '#334155', scaleMargins:{ top:.68, bottom:.08 } },
    timeScale: { borderColor: '#334155', timeVisible: true, secondsVisible: false },
    width,
    height: 340
  });
  ccSpreadsMainChart = chart;
  CC_SPREADS_MAIN_CHART_STATE.mainRenderKey = renderKey;
  const data = ccSpreadsMainChartData(rows);
  const spreadPriceFormat = data.spreadPriceFormat;
  const series = {};
  const dashedLineStyle = LightweightCharts.LineStyle && LightweightCharts.LineStyle.Dashed !== undefined ? LightweightCharts.LineStyle.Dashed : 2;
  const solidLineStyle = LightweightCharts.LineStyle && LightweightCharts.LineStyle.Solid !== undefined ? LightweightCharts.LineStyle.Solid : 0;
  if(CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled){
    series.bid = chart.addLineSeries({ color:'#34d399', lineWidth:1, priceLineVisible:false, lastValueVisible:true, priceFormat:spreadPriceFormat });
    series.ask = chart.addLineSeries({ color:'#f87171', lineWidth:1, priceLineVisible:false, lastValueVisible:true, priceFormat:spreadPriceFormat });
  }
  series.bandP1 = chart.addLineSeries({ color:'#38bdf8', lineWidth:1, lineStyle:dashedLineStyle, priceLineVisible:false, lastValueVisible:false, priceFormat:spreadPriceFormat });
  series.bandM1 = chart.addLineSeries({ color:'#38bdf8', lineWidth:1, lineStyle:dashedLineStyle, priceLineVisible:false, lastValueVisible:false, priceFormat:spreadPriceFormat });
  series.mark = chart.addLineSeries({ color:CC_SPREADS_MARK_LINE_COLOR, lineWidth:1, priceLineVisible:false, lastValueVisible:true, priceFormat:spreadPriceFormat });
  if(CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled){
    series.diff = chart.addLineSeries({ color:CC_SPREADS_BID_ASK_DIFF_LINE_COLOR, lineWidth:1, lineStyle:dashedLineStyle, priceLineVisible:false, lastValueVisible:true, priceFormat:data.diffPriceFormat });
  }
  if(CC_SPREADS_MAIN_CHART_STATE.fundingEnabled){
    series.fundingZero = chart.addLineSeries({ color:'#f59e0b', lineWidth:1, priceScaleId:'left', priceLineVisible:true, lastValueVisible:false });
    series.funding = chart.addLineSeries({ color:'#f59e0b', lineWidth:1, priceScaleId:'left', priceLineVisible:false, lastValueVisible:true });
    chart.priceScale('left').applyOptions({ visible:true, scaleMargins:{ top:.68, bottom:.08 }, borderColor:'#334155' });
  }
  series.bandMean = chart.addLineSeries({ color:CC_SPREADS_MA_LINE_COLOR, lineWidth:CC_SPREADS_MA_LINE_WIDTH, lineStyle:solidLineStyle, priceLineVisible:false, lastValueVisible:true, title:ccSpreadsShortPairLabel(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col), priceFormat:spreadPriceFormat });
  CC_SPREADS_MAIN_CHART_STATE.mainSeries = series;
  ccSpreadsMainChartUpdateData(rows);
  const markData = data.markData;
  const visibleData = markData;
  if(visibleData.length >= 2){
    const rightOffset = CC_SPREADS_MAIN_CHART_STATE.days >= 7 ? 2 : 1;
    const barSpacing = Math.max(0.02, width / (visibleData.length + rightOffset));
    chart.applyOptions({ timeScale:{ minBarSpacing:0.01, barSpacing, rightOffset, fixLeftEdge:true, fixRightEdge:false } });
    chart.timeScale().setVisibleLogicalRange({ from:0, to:visibleData.length - 1 + rightOffset });
  }else{
    chart.timeScale().fitContent();
  }
  chart.subscribeCrosshairMove(ccSpreadsMainChartHandleCrosshair);
  ccSpreadsMainResizeObserver = new ResizeObserver(() => {
    const nextWidth = Math.max(container.clientWidth || 0, 320);
    chart.applyOptions({ width: nextWidth });
    ccSpreadsMainScheduleUtcOverlay();
  });
  ccSpreadsMainResizeObserver.observe(container);
  ccSpreadsMainBindUtcOverlay(chart);
}
async function ccSpreadsMainLoadChart(row, col, days){
  const section = cc('ccSpreadsMainChartSection');
  const container = cc('ccSpreadsMainChart');
  if(!section || !container || !row || !col) return;
  const seq = ++CC_SPREADS_MAIN_CHART_STATE.requestSeq;
  CC_SPREADS_MAIN_CHART_STATE.mode = 'single';
  CC_SPREADS_MAIN_CHART_STATE.allPairs = [];
  CC_SPREADS_MAIN_CHART_STATE.allPairVisibility = {};
  ccSpreadsMainRenderPairToggles([]);
  CC_SPREADS_MAIN_CHART_STATE.rowIndex = null;
  section.hidden = false;
  const bidAskToggle = cc('ccSpreadsMainBidAskToggle');
  if(bidAskToggle) bidAskToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled;
  const bidAskDiffToggle = cc('ccSpreadsMainBidAskMedianDiffToggle');
  if(bidAskDiffToggle) bidAskDiffToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled;
  const fundingToggle = cc('ccSpreadsMainFundingToggle');
  if(fundingToggle) fundingToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.fundingEnabled;
  cc('ccSpreadsMainChartTitle').textContent = ccSpreadsMainChartLabel(row, col);
  const canUpdateExisting = ccSpreadsMainChart && CC_SPREADS_MAIN_CHART_STATE.mainRenderKey === ccSpreadsMainChartRenderKey();
  if(!canUpdateExisting){
    ccSpreadsMainChartSetMeta(null);
    container.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:340px"><span class="spinner"></span></div>';
  }
  try{
    const limit = Math.max(60, Number(days || 3) * 1440);
    CC_SPREADS_MAIN_CHART_STATE.fundingSymbol = ccSpreadsFundingSymbol(row, col);
    CC_SPREADS_MAIN_CHART_STATE.fundingRows = [];
    let rows;
    if(ccSpreadsIsSpotLeg(row) || ccSpreadsIsSpotLeg(col)){
      const [rowRows, colRows] = await Promise.all([
        ccSpreadsLoadLegMinuteRows(row, limit),
        ccSpreadsLoadLegMinuteRows(col, limit)
      ]);
      rows = ccSpreadsRowsForMainChart(rowRows, colRows);
    }else{
      const params = new URLSearchParams({ rowSymbol:String(row.symbol || ''), colSymbol:String(col.symbol || ''), limit:String(limit) });
      const data = await ccApi('/api/admin/coincall/futures/spreads/minute?' + params.toString(), { cache:'no-store' });
      rows = ccSpreadsMainChartNormalizeRows(data?.rows || data?.points || [], days);
      if(rows.length && rows.some(r => !Number.isFinite(Number(r.rowMark)) || !Number.isFinite(Number(r.bidAskSpread)))){
        const [rowRows, colRows] = await Promise.all([
          ccSpreadsLoadLegMinuteRows(row, limit),
          ccSpreadsLoadLegMinuteRows(col, limit)
        ]);
        const enriched = ccSpreadsAttachLegMarks(rows.map(r => Object.assign({ t:r.time }, r)), rowRows, colRows);
        rows = enriched.map((r, i) => Object.assign({}, rows[i], { rowMark:r.rowMark, colMark:r.colMark, rowBidAskMedian:r.rowBidAskMedian, colBidAskMedian:r.colBidAskMedian, bidAskSpread:r.bidAskSpread }));
      }
    }
    if(CC_SPREADS_MAIN_CHART_STATE.fundingEnabled && CC_SPREADS_MAIN_CHART_STATE.fundingSymbol){
      CC_SPREADS_MAIN_CHART_STATE.fundingRows = await ccSpreadsLoadFundingRows(CC_SPREADS_MAIN_CHART_STATE.fundingSymbol, limit);
    }
    if(seq !== CC_SPREADS_MAIN_CHART_STATE.requestSeq) return;
    rows = ccSpreadsMainMergeLiveRow(rows, row, col);
    ccSpreadsMainRenderChart(rows);
  }catch(e){
    if(seq !== CC_SPREADS_MAIN_CHART_STATE.requestSeq) return;
    container.innerHTML = '<div class="okx-nitro-status danger">Failed: ' + ccEsc(e.message || e) + '</div>';
    ccSpreadsMainChartSetMeta(null);
  }
}
function ccSpreadsMainSetFundingEnabled(enabled){
  CC_SPREADS_MAIN_CHART_STATE.fundingEnabled = !!enabled;
  ccSpreadsSaveCurrentMainChartSettings();
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row') return;
  if(CC_SPREADS_MAIN_CHART_STATE.row && CC_SPREADS_MAIN_CHART_STATE.col){
    ccSpreadsMainLoadChart(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col, CC_SPREADS_MAIN_CHART_STATE.days);
  }
}
function ccSpreadsMainSetBidAskEnabled(enabled){
  CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled = !!enabled;
  ccSpreadsSaveMainBidAskEnabled(CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled);
  ccSpreadsSaveCurrentMainChartSettings();
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row'){
    ccSpreadsMainLoadRowChart(CC_SPREADS_MAIN_CHART_STATE.rowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
    return;
  }
  if(CC_SPREADS_MAIN_CHART_STATE.row && CC_SPREADS_MAIN_CHART_STATE.col){
    ccSpreadsMainLoadChart(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col, CC_SPREADS_MAIN_CHART_STATE.days);
  }
}
function ccSpreadsMainSetBidAskDiffEnabled(enabled){
  CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled = !!enabled;
  ccSpreadsSaveMainBidAskDiffEnabled(CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled);
  ccSpreadsSaveCurrentMainChartSettings();
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row'){
    ccSpreadsMainLoadRowChart(CC_SPREADS_MAIN_CHART_STATE.rowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
    return;
  }
  if(CC_SPREADS_MAIN_CHART_STATE.row && CC_SPREADS_MAIN_CHART_STATE.col){
    ccSpreadsMainLoadChart(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col, CC_SPREADS_MAIN_CHART_STATE.days);
  }
}
function ccSpreadsMainRefreshSelected(){
  const section = cc('ccSpreadsMainChartSection');
  if(!section || section.hidden) return false;
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row'){
    const now = Date.now();
    if(CC_SPREADS_MAIN_CHART_STATE.lastRefreshAt && now - CC_SPREADS_MAIN_CHART_STATE.lastRefreshAt < CC_SPREADS_REFRESH_MS - 1000) return false;
    CC_SPREADS_MAIN_CHART_STATE.lastRefreshAt = now;
    ccSpreadsMainLoadRowChart(CC_SPREADS_MAIN_CHART_STATE.rowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
    return true;
  }
  const rowSymbol = String(CC_SPREADS_MAIN_CHART_STATE.row?.symbol || '').toUpperCase();
  const colSymbol = String(CC_SPREADS_MAIN_CHART_STATE.col?.symbol || '').toUpperCase();
  if(!rowSymbol || !colSymbol) return false;
  const now = Date.now();
  if(CC_SPREADS_MAIN_CHART_STATE.lastRefreshAt && now - CC_SPREADS_MAIN_CHART_STATE.lastRefreshAt < CC_SPREADS_REFRESH_MS - 1000) return false;
  CC_SPREADS_MAIN_CHART_STATE.lastRefreshAt = now;
  const cell = ccSpreadsFindCellBySymbols(ccSpreadsHeaderAsset, rowSymbol, colSymbol);
  if(!cell) return false;
  CC_SPREADS_MAIN_CHART_STATE.row = cell.row;
  CC_SPREADS_MAIN_CHART_STATE.col = cell.col;
  ccSpreadsMainLoadChart(cell.row, cell.col, CC_SPREADS_MAIN_CHART_STATE.days);
  return true;
}
function ccSpreadsMainSelect(rowIndex, colIndex){
  const cell = ccSpreadsFindCell(rowIndex, colIndex);
  if(!cell) return false;
  CC_SPREADS_MAIN_CHART_STATE.mode = 'single';
  CC_SPREADS_MAIN_CHART_STATE.row = cell.row;
  CC_SPREADS_MAIN_CHART_STATE.col = cell.col;
  CC_SPREADS_MAIN_CHART_STATE.rowIndex = null;
  CC_SPREADS_MAIN_CHART_STATE.allPairs = [];
  CC_SPREADS_MAIN_CHART_STATE.allPairVisibility = {};
  ccSpreadsMainRenderPairToggles([]);
  ccSpreadsSaveCurrentMainChartSettings({ mode:'single', rowSymbol:String(cell.row?.symbol || '').toUpperCase(), colSymbol:String(cell.col?.symbol || '').toUpperCase(), pairVisibility:{} });
  document.querySelectorAll('#coincallSpreadsGridHost [data-cc-spreads-clickable="1"]').forEach(el => {
    el.classList.toggle('selected', String(el.dataset.ccSpreadsRowIndex) === String(rowIndex) && String(el.dataset.ccSpreadsColIndex) === String(colIndex));
  });
  ccSpreadsMainLoadChart(cell.row, cell.col, CC_SPREADS_MAIN_CHART_STATE.days);
  return true;
}
function ccSpreadsMainSelectBySymbols(asset, rowSymbol, colSymbol){
  const cell = ccSpreadsFindCellBySymbols(asset || ccSpreadsHeaderAsset, rowSymbol, colSymbol);
  if(!cell) return false;
  CC_SPREADS_MAIN_CHART_STATE.mode = 'single';
  CC_SPREADS_MAIN_CHART_STATE.row = cell.row;
  CC_SPREADS_MAIN_CHART_STATE.col = cell.col;
  CC_SPREADS_MAIN_CHART_STATE.rowIndex = null;
  CC_SPREADS_MAIN_CHART_STATE.allPairs = [];
  CC_SPREADS_MAIN_CHART_STATE.allPairVisibility = {};
  ccSpreadsMainRenderPairToggles([]);
  ccSpreadsSaveCurrentMainChartSettings({ mode:'single', rowSymbol:String(cell.row?.symbol || '').toUpperCase(), colSymbol:String(cell.col?.symbol || '').toUpperCase(), pairVisibility:{} });
  const rowKey = String(cell.row?.symbol || '').toUpperCase();
  const colKey = String(cell.col?.symbol || '').toUpperCase();
  document.querySelectorAll('#coincallSpreadsGridHost [data-cc-spreads-clickable="1"]').forEach(el => {
    el.classList.toggle('selected', String(el.dataset.ccSpreadsRowSymbol || '').toUpperCase() === rowKey && String(el.dataset.ccSpreadsColSymbol || '').toUpperCase() === colKey);
  });
  ccSpreadsMainLoadChart(cell.row, cell.col, CC_SPREADS_MAIN_CHART_STATE.days);
  return true;
}
function ccSpreadsMainClear(){
  CC_SPREADS_MAIN_CHART_STATE.mode = 'single';
  CC_SPREADS_MAIN_CHART_STATE.row = null;
  CC_SPREADS_MAIN_CHART_STATE.col = null;
  CC_SPREADS_MAIN_CHART_STATE.rowIndex = null;
  CC_SPREADS_MAIN_CHART_STATE.allPairs = [];
  CC_SPREADS_MAIN_CHART_STATE.allPairVisibility = {};
  ++CC_SPREADS_MAIN_CHART_STATE.requestSeq;
  document.querySelectorAll('#coincallSpreadsGridHost [data-cc-spreads-clickable="1"]').forEach(el => el.classList.remove('selected'));
  const section = cc('ccSpreadsMainChartSection');
  const container = cc('ccSpreadsMainChart');
  if(section) section.hidden = true;
  if(container) container.innerHTML = '';
  CC_SPREADS_MAIN_CHART_STATE.mainRenderKey = '';
  CC_SPREADS_MAIN_CHART_STATE.mainSeries = null;
  CC_SPREADS_MAIN_CHART_STATE.mainLastRow = null;
  CC_SPREADS_MAIN_CHART_STATE.mainRowByTime = null;
  CC_SPREADS_MAIN_CHART_STATE.mainRows = null;
  ccSpreadsMainChartSetMeta(null);
  ccSpreadsMainRenderPairToggles([]);
  const fundingToggle = cc('ccSpreadsMainFundingToggle');
  if(fundingToggle) fundingToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.fundingEnabled;
  const bidAskToggle = cc('ccSpreadsMainBidAskToggle');
  if(bidAskToggle) bidAskToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled;
  const bidAskDiffToggle = cc('ccSpreadsMainBidAskMedianDiffToggle');
  if(bidAskDiffToggle) bidAskDiffToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled;
}
function ccSpreadsMainSetDays(days, button){
  CC_SPREADS_MAIN_CHART_STATE.days = Number(days) || 3;
  ccSpreadsSyncMainDaysButtons();
  ccSpreadsSaveCurrentMainChartSettings();
  if(CC_SPREADS_MAIN_CHART_STATE.mode === 'row'){
    ccSpreadsMainLoadRowChart(CC_SPREADS_MAIN_CHART_STATE.rowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
    return;
  }
  if(CC_SPREADS_MAIN_CHART_STATE.row && CC_SPREADS_MAIN_CHART_STATE.col){
    ccSpreadsMainLoadChart(CC_SPREADS_MAIN_CHART_STATE.row, CC_SPREADS_MAIN_CHART_STATE.col, CC_SPREADS_MAIN_CHART_STATE.days);
  }
}
function ccSpreadsOpenTradeModal(rowIndex, colIndex){
  const modal = ccSpreadsEnsureTradeModalRoot();
  if(!modal) return false;
  const cell = ccSpreadsFindCell(rowIndex, colIndex);
  if(!cell) return false;
  ccSpreadsStorePanel('trade-history');
  ccSpreadsResetFundingHistoryPreload();
  ccSpreadsTradeModalState = {
    asset: ccSpreadsHeaderAsset,
    rowSymbol: String(cell.row?.symbol || '').toUpperCase(),
    colSymbol: String(cell.col?.symbol || '').toUpperCase()
  };
  ccSpreadsTradeModalOpenedAt = Date.now();
  modal.removeAttribute('hidden');
  modal.hidden = false;
  modal.setAttribute('aria-hidden','false');
  ccSpreadsRenderTradeModalContent(cell);
  ccSpreadsRefreshPrivateState();
  ccSpreadsSyncAutoRefresh();
  return true;
}
function ccSpreadsOpenTradeModalBySymbols(asset, rowSymbol, colSymbol){
  const modal = ccSpreadsEnsureTradeModalRoot();
  if(!modal) return false;
  const cell = ccSpreadsFindCellBySymbols(asset || ccSpreadsHeaderAsset, rowSymbol, colSymbol);
  if(!cell) return false;
  ccSpreadsStorePanel('trade-history');
  ccSpreadsResetFundingHistoryPreload();
  ccSpreadsTradeModalState = {
    asset: asset || ccSpreadsHeaderAsset,
    rowSymbol: String(cell.row?.symbol || '').toUpperCase(),
    colSymbol: String(cell.col?.symbol || '').toUpperCase()
  };
  ccSpreadsTradeModalOpenedAt = Date.now();
  modal.removeAttribute('hidden');
  modal.hidden = false;
  modal.setAttribute('aria-hidden','false');
  ccSpreadsRenderTradeModalContent(cell);
  ccSpreadsRefreshPrivateState();
  ccSpreadsSyncAutoRefresh();
  return true;
}
function ccSpreadsTradeModalOpen(){
  const modal = cc('ccSpreadsTradeModal');
  return !!modal && !modal.hidden;
}
function ccSpreadsWorkstationPairSymbol(item){
  if(!item) return '';
  if(ccSpreadsIsSpotLeg(item)){
    const symbol = String(item.symbol || item.displayName || '').toUpperCase().replace(/[^A-Z0-9]/g,'');
    const base = symbol.endsWith('USDT') ? symbol.slice(0, -4) : ccSpreadsSpotParts(symbol).base;
    return (base || ccSpreadsHeaderAsset || 'BTC') + 'USDT-SPOT';
  }
  return ccFuturesCanonicalDisplaySymbolFromOrder(item) || ccFuturesCanonicalDisplaySymbol(item.symbol || item.displayName || item.rawLabel || item.label || '');
}
function ccSpreadsWorkstationUrlForCell(cellEl){
  if(!cellEl || cellEl.dataset.ccSpreadsClickable !== '1') return '';
  const cell = ccSpreadsFindCellBySymbols(ccSpreadsHeaderAsset, cellEl.dataset.ccSpreadsRowSymbol, cellEl.dataset.ccSpreadsColSymbol);
  if(!cell) return '';
  const colSymbol = ccSpreadsWorkstationPairSymbol(cell.col);
  const rowSymbol = ccSpreadsWorkstationPairSymbol(cell.row);
  if(!colSymbol || !rowSymbol) return '';
  return 'coincall-spread-workstation.html?pair=' + encodeURIComponent(colSymbol + '_' + rowSymbol);
}
function ccSpreadsActivateCell(cell){
  if(!cell || cell.dataset.ccSpreadsClickable !== '1') return false;
  const url = ccSpreadsWorkstationUrlForCell(cell);
  if(!url) return false;
  window.location.href = url;
  return true;
}
function ccSpreadsCellFromEvent(event){
  const target = event?.target;
  let cell = target?.closest?.('[data-cc-spreads-clickable="1"]') || null;
  if(!cell && typeof event?.composedPath === 'function'){
    for(const node of event.composedPath()){
      if(node?.matches?.('[data-cc-spreads-clickable="1"]')){ cell = node; break; }
      const parent = node?.closest?.('[data-cc-spreads-clickable="1"]');
      if(parent){ cell = parent; break; }
    }
  }
  return cell && cc('ccSpreadsPanel')?.contains(cell) ? cell : null;
}
function ccSpreadsHandleCellActivation(event){
  if(event?.type !== 'click') return;
  if(event?.type === 'pointerdown' && event.button !== undefined && event.button !== 0) return;
  if(event?.type === 'mousedown' && event.button !== undefined && event.button !== 0) return;
  if(event.button !== undefined && event.button !== 0) return;
  const cell = ccSpreadsCellFromEvent(event);
  if(!cell) return;
  if(ccSpreadsActivateCell(cell)){
    event.preventDefault();
    event.stopPropagation();
  }
}
function ccSpreadsHandleCellContextMenu(event){
  const rowLabel = event?.target?.closest?.('[data-cc-spreads-row-label="1"]');
  if(rowLabel && cc('coincallSpreadsGridHost')?.contains(rowLabel)){
    event.preventDefault();
    event.stopPropagation();
    ccSpreadsMainLoadRowChart(rowLabel.dataset.ccSpreadsRowIndex, CC_SPREADS_MAIN_CHART_STATE.days);
    return;
  }
  const cell = ccSpreadsCellFromEvent(event);
  if(!cell) return;
  if(ccSpreadsMainSelectBySymbols(ccSpreadsHeaderAsset, cell.dataset.ccSpreadsRowSymbol, cell.dataset.ccSpreadsColSymbol)){
    event.preventDefault();
    event.stopPropagation();
  }
}
function ccSpreadsCloseTradeModal(){clearTimeout(ccSpreadsPrivateRefreshTimer);ccSpreadsPrivateRefreshTimer=null;
  ccSpreadsSaveChartState();
  const modal = ccSpreadsEnsureTradeModalRoot();
  if(modal){ modal.hidden = true; modal.setAttribute('aria-hidden','true'); }
  ccSpreadsTradeModalOpenedAt = 0;
  ccSpreadsTradeModalState = null;
  CC_SPREADS_CHART_STATE.key = '';
  CC_SPREADS_CHART_STATE.rows = [];
  CC_SPREADS_CHART_STATE.loading = false;
  CC_SPREADS_CHART_STATE.panOffset = 0;
  CC_SPREADS_CHART_STATE.priceRange = null;
  CC_SPREADS_CHART_STATE.drag = null;
  if(CC_SPREADS_CHART_STATE.interactionAbort){ try{ CC_SPREADS_CHART_STATE.interactionAbort.abort(); }catch(e){} }
  CC_SPREADS_CHART_STATE.interactionAbort = null;
  CC_SPREADS_CHART_STATE.boundCanvas = null;
  CC_SPREADS_CHART_STATE.wheelBound = false;
  CC_SPREADS_CHART_STATE.requestSeq++;
  ccSpreadsClearPopupChartTimers();
  CC_SPREADS_CHART_STATE.fundingLiveBusy = false;
  ccSpreadsStopModalBooks();
  ccSpreadsSyncAutoRefresh();
}
function ccSpreadsHandleModalPointerClose(event){
  if(event?.type === 'pointerdown' && event.button !== undefined && event.button !== 0) return;
  if(event?.type === 'mousedown' && event.button !== undefined && event.button !== 0) return;
  const target = event?.target;
  const backdropClick = target?.id === 'ccSpreadsTradeModal';
  const closeClick = !!target?.closest?.('#ccSpreadsTradeClose');
  if(!backdropClick && !closeClick) return;
  if(Date.now() - ccSpreadsTradeModalOpenedAt < 500) return;
  event.preventDefault();
  event.stopPropagation();
  ccSpreadsCloseTradeModal();
}
async function ccSpreadsLoadLeverages(force=false){
  const accountId = ccSpreadsActiveAccountId();
  if (!accountId) return;
  if (ccSpreadsLeveragePromise && !force) return ccSpreadsLeveragePromise;
  const symbols = new Set();
  ['BTC','ETH'].forEach(asset => {
    const state = ccSpreadsAssetState(asset);
    if (state?.perp?.symbol) symbols.add(String(state.perp.symbol).toUpperCase());
    (Array.isArray(state?.columns) ? state.columns : []).forEach(item => {
      if (item?.symbol) symbols.add(String(item.symbol).toUpperCase());
    });
  });
  const targets = Array.from(symbols).filter(symbol => force || !(ccSpreadsLeverageCacheKey(symbol, accountId) in CC_SPREADS_LEVERAGE_CACHE));
  if (!targets.length) return;
  ccSpreadsLeveragePromise = Promise.allSettled(targets.map(async symbol => {
    const key = ccSpreadsLeverageCacheKey(symbol, accountId);
    try {
      const res = await ccApi('/api/admin/coincall/futures/leverage?accountId=' + encodeURIComponent(accountId) + '&symbol=' + encodeURIComponent(symbol));
      const value = ccFuturesLeverageValue(res);
      CC_SPREADS_LEVERAGE_CACHE[key] = Number.isFinite(value) && value > 0 ? value : NaN;
    } catch {
      CC_SPREADS_LEVERAGE_CACHE[key] = NaN;
    }
  })).finally(() => { ccSpreadsLeveragePromise = null; });
  return ccSpreadsLeveragePromise;
}
async function ccSpreadsHydrateLocalLatestCandles(items){
  const unique = Array.from(new Map((Array.isArray(items) ? items : []).map(item => [String(item?.symbol || '').trim().toUpperCase(), item]).filter(([symbol]) => !!symbol)).entries());
  let cursor = 0;
  const workerCount = Math.min(6, unique.length);
  async function worker(){
    while(cursor < unique.length){
      const current = unique[cursor++];
      const symbol = current[0];
      const item = current[1];
      try{
        const res = await ccApi('/api/admin/coincall/futures/candles/minute?symbol=' + encodeURIComponent(symbol) + '&limit=1', { cache:'no-store' });
        const row = ccMarketDataNormalizeRows(res && res.rows).slice(-1)[0];
        if(!row) continue;
        if(Number.isFinite(Number(row.mark)) && Number(row.mark) > 0){
          item.markPrice = row.mark;
          item.markPriceClose = row.mark;
        }
        if(Number.isFinite(Number(row.bid)) && Number(row.bid) > 0){
          item.bid = row.bid;
          item.bidPx = row.bid;
        }
        if(Number.isFinite(Number(row.ask)) && Number(row.ask) > 0){
          item.ask = row.ask;
          item.askPx = row.ask;
        }
      }catch{}
    }
  }
  await Promise.all(Array.from({ length:workerCount }, worker));
}
function ccSpreadsBuildHeaderData(items, quoteRows){
  const grouped = { BTC: [], ETH: [] };
  const perps = { BTC: null, ETH: null };
  const liveRows = Array.isArray(quoteRows) ? quoteRows : [];
  for (const item of (Array.isArray(items) ? items : [])) {
    const symbol = String(item?.symbol || '').trim().toUpperCase();
    const displayName = String(item?.displayName || symbol).trim();
    const asset = symbol === 'BTCUSD' || symbol.startsWith('BTCUSD-') ? 'BTC' : symbol === 'ETHUSD' || symbol.startsWith('ETHUSD-') ? 'ETH' : '';
    if (!asset) continue;
    const expiryTs = ccSpreadsParseSymbolExpiry(symbol);
    const marketRow = Object.assign({}, item?.source || item || {}, { symbol, expiryTs });
    const normalized = ccSpreadsApplyCachedMatrixQuote({
      ...marketRow,
      symbol,
      rawLabel: displayName,
      label: ccSpreadsLabelFromExpiry(expiryTs, displayName),
      expiryTs,
      premium: ''
    });
    if (symbol === asset + 'USD') {
      perps[asset] = ccSpreadsApplyCachedMatrixQuote({ ...normalized, label: 'PERP', expiryTs: 0 });
      continue;
    }
    if (!symbol.includes('-')) continue;
    grouped[asset].push(normalized);
  }
  ['BTC','ETH'].forEach(asset => {
    const perpSource = liveRows.find(row => String(row?.symbol || row?.ticker_id || '').toUpperCase() === asset + 'USD');
    if (!perps[asset] && perpSource) {
      perps[asset] = ccSpreadsApplyCachedMatrixQuote({
        ...perpSource,
        symbol: asset + 'USD',
        rawLabel: String(perpSource?.displayName || perpSource?.symbolName || asset + 'USD'),
        label: 'PERP',
        expiryTs: 0,
        premium: ''
      });
    }
    const perpQuoteRow = perpSource ? Object.assign({}, perpSource, { symbol: asset + 'USD', expiryTs: 0 }) : perps[asset];
    const sorted = ccSpreadsSortRows(grouped[asset]);
    const monthlyColumns = sorted.filter(ccSpreadsIsMonthlyOrQuarterlyExpiryFuture).map(item => ccSpreadsApplyCachedMatrixQuote(item));
    const columns = sorted.filter(ccSpreadsExpiryFilter).map(item => ({
      ...item,
      premium: perpQuoteRow ? ccSpreadsPremiumText({ ...item, symbol: item.symbol }, perpQuoteRow) : ''
    }));
    CC_SPREADS_HEADER_DATA[asset] = { columns, monthlyColumns, perp: perps[asset], spot: ccSpreadsSpotLeg(asset) };
  });
  return CC_SPREADS_HEADER_DATA;
}
function ccSpreadsLoadHeaderCache(){
  try{
    const raw = localStorage.getItem(CC_SPREADS_HEADER_CACHE_KEY);
    if(!raw) return [];
    const parsed = JSON.parse(raw);
    const rows = Array.isArray(parsed?.quoteRows) ? parsed.quoteRows : [];
    const ts = Number(parsed?.ts || 0);
    if(!rows.length || !Number.isFinite(ts) || Date.now() - ts > 300000) return [];
    return rows;
  }catch{return []}
}
function ccSpreadsSaveHeaderCache(quoteRows){
  try{
    if(Array.isArray(quoteRows) && quoteRows.length){
      localStorage.setItem(CC_SPREADS_HEADER_CACHE_KEY, JSON.stringify({ ts:Date.now(), quoteRows }));
    }
  }catch{}
}
async function ccSpreadsLoadBundledHeaderCache(){
  try{
    const embeddedRows = Array.isArray(CC_SPREADS_EMBEDDED_HEADER_CACHE?.quoteRows) ? CC_SPREADS_EMBEDDED_HEADER_CACHE.quoteRows : [];
    if(embeddedRows.length) return embeddedRows;
    const res = await fetch('/data/coincall-spreads-header-cache.json', { cache:'no-store' });
    if(!res.ok) return [];
    const parsed = await res.json();
    const rows = Array.isArray(parsed?.quoteRows) ? parsed.quoteRows : [];
    return rows;
  }catch{return []}
}
async function ccSpreadsLoadHeaders(force=false){
  if (ccSpreadsLoadPromise && !force) return ccSpreadsLoadPromise;
  ccSpreadsLoadPromise = (async () => {
    let items = [];
    let quoteRows = [];
    // Startup must not render cached/bundled snapshots in the spreads matrix.
    // The grid should appear only after live CoinCall data is loaded.
    await ccSpreadsLoadSpotPrices(force);
    try {
      const quoteRes = await ccApi('/api/admin/coincall/public/futures/quote-symbols', { cache:'no-store' });
      quoteRows = Array.isArray(quoteRes?.data) ? quoteRes.data : [];
      ccSpreadsSaveHeaderCache(quoteRows);
    } catch {}
    if (quoteRows.length) {
      items = quoteRows.map(row => ({
        symbol: row?.symbol || row?.ticker_id || '',
        displayName: row?.displayName || row?.symbolName || row?.ticker_id || row?.symbol || '',
        source: row
      }));
    } else {
      const res = await ccApi('/api/admin/coincall/futures/candles/minute/symbols', { cache:'no-store' });
      items = Array.isArray(res?.items) ? res.items : [];
    }
    return ccSpreadsBuildHeaderData(items, quoteRows);
  })().finally(() => { ccSpreadsLoadPromise = null; });
  return ccSpreadsLoadPromise;
}
function ccSpreadsRenderGrid(){
  const host = cc('coincallSpreadsGridHost'); if(!host) return;
  const columns = ccSpreadsColumnsForAsset(ccSpreadsHeaderAsset);
  const rows = ccSpreadsRowsForAsset(ccSpreadsHeaderAsset);
  if (!columns.length) {
    host.innerHTML = '<div class="okx-nitro-status">No live CoinCall futures are available for ' + ccEsc(ccSpreadsHeaderAsset) + '.</div>';
    return;
  }
  const showDiffCol = ccSpreadsMatrixDiffColEnabled();
  const showBidCol = ccSpreadsMatrixBidColEnabled();
  const showAskCol = ccSpreadsMatrixAskColEnabled();
  let html = '<div class="okx-nitro-grid" style="--okx-nitro-cols:' + columns.length + ';grid-template-columns:' + ccEsc(ccSpreadsMatrixGridTemplate(columns.length)) + '"><div class="okx-nitro-corner"></div><div class="okx-nitro-col okx-nitro-row-price-head"><div class="okx-nitro-col-name">Index / MarkPrice</div></div><div class="okx-nitro-col okx-nitro-row-price-head"><div class="okx-nitro-col-name">BidAskMedian</div></div>';
  if(showDiffCol) html += '<div class="okx-nitro-col okx-nitro-row-price-head"><div class="okx-nitro-col-name">Diff%</div></div>';
  if(showBidCol) html += '<div class="okx-nitro-col okx-nitro-row-price-head"><div class="okx-nitro-col-name">Bid</div></div>';
  if(showAskCol) html += '<div class="okx-nitro-col okx-nitro-row-price-head"><div class="okx-nitro-col-name">Ask</div></div>';
  html += columns.map((item, colIndex) => '<div class="okx-nitro-col" data-cc-spreads-col-index="' + colIndex + '" data-cc-spreads-col-symbol="' + ccEsc(item.symbol || '') + '"><div class="okx-nitro-col-name">' + ccEsc(item.label) + '</div><div class="okx-nitro-col-lev">' + ccSpreadsMetaHtml(item) + '</div></div>').join('');
  rows.forEach((row, rowIndex) => {
    html += '<div class="okx-nitro-row" data-cc-spreads-row-label="1" data-cc-spreads-row-index="' + rowIndex + '" data-cc-spreads-row-symbol="' + ccEsc(row.symbol || '') + '" title="Right-click to chart this row">' + ccEsc(row.label) + '<small>' + ccEsc(ccSpreadsLeverageForRow(row)) + '</small></div>';
    html += '<div class="okx-nitro-cell okx-nitro-row-price" data-cc-spreads-row-mark-price-index="' + rowIndex + '" data-cc-spreads-row-price-index="' + rowIndex + '" data-cc-spreads-row-price-symbol="' + ccEsc(row.symbol || '') + '">' + ccEsc(ccSpreadsMarkPriceText(row)) + '</div>';
    html += '<div class="okx-nitro-cell okx-nitro-row-price" data-cc-spreads-row-bidask-price-index="' + rowIndex + '" data-cc-spreads-row-price-index="' + rowIndex + '" data-cc-spreads-row-price-symbol="' + ccEsc(row.symbol || '') + '">' + ccEsc(ccSpreadsRowBidAskMedianText(row)) + '</div>';
    if(showDiffCol) html += '<div class="okx-nitro-cell okx-nitro-row-price ' + ccEsc(ccSpreadsRowBidAskMarkDiffClass(row)) + '" data-cc-spreads-row-bidask-diff-index="' + rowIndex + '" data-cc-spreads-row-price-index="' + rowIndex + '" data-cc-spreads-row-price-symbol="' + ccEsc(row.symbol || '') + '">' + ccEsc(ccSpreadsRowBidAskMarkDiffText(row)) + '</div>';
    if(showBidCol) html += '<div class="okx-nitro-cell okx-nitro-row-price okx-nitro-row-book-price" data-cc-spreads-row-book-price-index="' + rowIndex + '" data-cc-spreads-row-book-price-side="bid" data-cc-spreads-row-book-price-symbol="' + ccEsc(row.symbol || '') + '">' + ccEsc(ccSpreadsRowBookPriceText(row, 'bid')) + '</div>';
    if(showAskCol) html += '<div class="okx-nitro-cell okx-nitro-row-price okx-nitro-row-book-price" data-cc-spreads-row-book-price-index="' + rowIndex + '" data-cc-spreads-row-book-price-side="ask" data-cc-spreads-row-book-price-symbol="' + ccEsc(row.symbol || '') + '">' + ccEsc(ccSpreadsRowBookPriceText(row, 'ask')) + '</div>';
    columns.forEach((col, colIndex) => {
      if (ccSpreadsCellUnavailable(rowIndex, colIndex, row, col)) {
        html += '<div class="okx-nitro-cell empty"></div>';
        return;
      }
      const spread = ccSpreadsSpreadValue(col, row);
      if (!Number.isFinite(spread)) {
        html += '<div class="okx-nitro-cell empty"></div>';
        return;
      }
      const spreadClass = ccSpreadsSpreadClass(col, row);
      html += '<div class="okx-nitro-cell clickable" role="button" tabindex="0" data-cc-spreads-clickable="1" data-cc-spreads-row-index="' + rowIndex + '" data-cc-spreads-col-index="' + colIndex + '" data-cc-spreads-row-symbol="' + ccEsc(row.symbol || '') + '" data-cc-spreads-col-symbol="' + ccEsc(col.symbol || '') + '" aria-label="Open CoinCall spread details for ' + ccEsc(col.label || col.symbol || 'column future') + ' over ' + ccEsc(row.label || row.symbol || 'row future') + '"><div class="okx-nitro-quote-row"><div class="okx-nitro-side ' + ccEsc(spreadClass) + '" style="grid-column:1 / -1;text-align:center">' + ccSpreadsCellDisplayHtml(col, row) + '<div class="okx-nitro-size"></div></div></div></div>';
    });
  });
  html += '</div>';
  host.innerHTML = html;
  ccSpreadsPatchRenderedRowPrices();
  ccSpreadsMatrixRealtimeEnsure().catch(() => {});
  const selectedRow = String(CC_SPREADS_MAIN_CHART_STATE.row?.symbol || '').toUpperCase();
  const selectedCol = String(CC_SPREADS_MAIN_CHART_STATE.col?.symbol || '').toUpperCase();
  if(selectedRow && selectedCol){
    document.querySelectorAll('#coincallSpreadsGridHost [data-cc-spreads-clickable="1"]').forEach(el => {
      el.classList.toggle('selected', String(el.dataset.ccSpreadsRowSymbol || '').toUpperCase() === selectedRow && String(el.dataset.ccSpreadsColSymbol || '').toUpperCase() === selectedCol);
    });
  }
}
function ccSpreadsSetAsset(asset, options){
  const previousAsset = ccSpreadsHeaderAsset;
  ccSpreadsHeaderAsset = asset === 'ETH' ? 'ETH' : 'BTC';
  if(previousAsset !== ccSpreadsHeaderAsset) ccSpreadsMainClear();
  const btc = cc('ccSpreadsAssetBtc');
  const eth = cc('ccSpreadsAssetEth');
  if (btc) btc.classList.toggle('active', ccSpreadsHeaderAsset === 'BTC');
  if (eth) eth.classList.toggle('active', ccSpreadsHeaderAsset === 'ETH');
  ccSpreadsRenderGrid();
  if(!options || !options.skipSave) ccSpreadsSaveCurrentMainChartSettings({ asset:ccSpreadsHeaderAsset });
}
async function ccSpreadsRefresh(force=false, options){
  if(force){
    ccSpreadsLiveRefreshLastAt = Date.now();
    if(ccSpreadsLiveRefreshTimer){
      clearTimeout(ccSpreadsLiveRefreshTimer);
      ccSpreadsLiveRefreshTimer = 0;
    }
  }
  await ccSpreadsLoadHeaders(force);
  if(!options || !options.skipRender){
    ccSpreadsRenderGrid();
    ccSpreadsPatchRenderedRowPrices();
  }
  ccSpreadsLoadLeverages(false).then(() => {
    if(options && options.skipRender) return;
    ccSpreadsRenderGrid();
    ccSpreadsPatchRenderedRowPrices();
  }).catch(() => {});
  if (ccSpreadsTradeModalOpen()) ccSpreadsUpdateTradeModalContent();
  ccSpreadsSetLastUpdate(new Date());
  if(!force) ccSpreadsScheduleLiveHeaderRefresh();
}
function ccSpreadsScheduleLiveHeaderRefresh(){
  const now = Date.now();
  if(ccSpreadsLiveRefreshTimer) return;
  if(now - ccSpreadsLiveRefreshLastAt < 15000) return;
  ccSpreadsLiveRefreshTimer = setTimeout(async () => {
    ccSpreadsLiveRefreshTimer = 0;
    if(Date.now() - ccSpreadsLiveRefreshLastAt < 15000) return;
    ccSpreadsLiveRefreshLastAt = Date.now();
    try { await ccSpreadsRefresh(true); } catch {}
  }, 2500);
}
function ccSpreadsIsActive(){
  return !document.hidden && ccActiveLayer() === 'trade' && ccTradeActiveMode() === 'spreads';
}
function ccSpreadsStopAutoRefresh(){
  if (ccSpreadsAutoRefreshTimer) {
    clearInterval(ccSpreadsAutoRefreshTimer);
    ccSpreadsAutoRefreshTimer = 0;
  }
}
function ccSpreadsStartAutoRefresh(){
  if (!ccSpreadsIsActive()) {
    ccSpreadsStopAutoRefresh();
    return;
  }
  if (ccSpreadsAutoRefreshTimer) return;
  ccSpreadsAutoRefreshTimer = window.setInterval(async () => {
    if (!ccSpreadsIsActive()) {
      ccSpreadsStopAutoRefresh();
      return;
    }
    if (ccSpreadsRefreshBusy) return;
    ccSpreadsRefreshBusy = true;
    try {
      await ccSpreadsRefresh(true);
    } finally {
      ccSpreadsRefreshBusy = false;
    }
  }, CC_SPREADS_REFRESH_MS);
}
function ccSpreadsSyncAutoRefresh(){
  if (ccSpreadsIsActive()) {
    ccSpreadsStartAutoRefresh();
    ccSpreadsMatrixRealtimeEnsure().catch(() => {});
    ccSpreadsStartMainLiveTick();
  } else {
    ccSpreadsStopAutoRefresh();
    ccSpreadsStopMainLiveTick();
    ccSpreadsMatrixRealtimeStop();
  }
}
function ccSpreadsInit(){
  if(!cc('ccSpreadsPanel')) return;
  ccSpreadsEnsureTradeModalRoot();
  ccSpreadsSyncDailyExpControl();
  ccSpreadsSyncWeeklyExpControl();
  ccSpreadsSyncMonthlyExpControl();
  ccSpreadsSyncQuarterlyExpControl();
  ccSpreadsSyncMatrixVisibilityControls();
  ccSpreadsSyncDisplaySwitch();
  document.querySelectorAll('[data-cc-spreads-display-mode]').forEach(btn => {
    btn.addEventListener('click', () => ccSpreadsSetDisplayMode(btn.dataset.ccSpreadsDisplayMode));
  });
  document.querySelectorAll('[data-cc-spreads-price-source]').forEach(btn => {
    btn.addEventListener('click', () => ccSpreadsSetPriceSource(btn.dataset.ccSpreadsPriceSource));
  });
  [
    ['ccSpreadsMatrixDiffCol', CC_SPREADS_MATRIX_DIFF_COL_KEY],
    ['ccSpreadsMatrixBidCol', CC_SPREADS_MATRIX_BID_COL_KEY],
    ['ccSpreadsMatrixAskCol', CC_SPREADS_MATRIX_ASK_COL_KEY]
  ].forEach(([id, key]) => {
    cc(id)?.addEventListener('change', event => {
      try { localStorage.setItem(key, event.target.checked ? '1' : '0'); } catch {}
      ccSpreadsRenderGrid();
    });
  });
  cc('ccSpreadsDailyExp')?.addEventListener('change', () => {
    try { localStorage.setItem(CC_SPREADS_DAILY_EXP_KEY, ccSpreadsDailyExpEnabled() ? '1' : '0'); } catch {}
    ccSpreadsMainClear();
    ccSpreadsRefresh(true).catch(err => {
      const host = cc('coincallSpreadsGridHost');
      if (host) host.innerHTML = '<div class="okx-nitro-status danger">Spreads header load failed: ' + ccEsc(err?.message || String(err)) + '</div>';
    });
  });
  cc('ccSpreadsWeeklyExp')?.addEventListener('change', () => {
    try { localStorage.setItem(CC_SPREADS_WEEKLY_EXP_KEY, ccSpreadsWeeklyExpEnabled() ? '1' : '0'); } catch {}
    ccSpreadsMainClear();
    ccSpreadsRefresh(true).catch(err => {
      const host = cc('coincallSpreadsGridHost');
      if (host) host.innerHTML = '<div class="okx-nitro-status danger">Spreads header load failed: ' + ccEsc(err?.message || String(err)) + '</div>';
    });
  });
  cc('ccSpreadsMonthlyExp')?.addEventListener('change', () => {
    try { localStorage.setItem(CC_SPREADS_MONTHLY_EXP_KEY, ccSpreadsMonthlyExpEnabled() ? '1' : '0'); } catch {}
    ccSpreadsMainClear();
    ccSpreadsRefresh(true).catch(err => {
      const host = cc('coincallSpreadsGridHost');
      if (host) host.innerHTML = '<div class="okx-nitro-status danger">Spreads header load failed: ' + ccEsc(err?.message || String(err)) + '</div>';
    });
  });
  cc('ccSpreadsQuarterlyExp')?.addEventListener('change', () => {
    try { localStorage.setItem(CC_SPREADS_QUARTERLY_EXP_KEY, ccSpreadsQuarterlyExpEnabled() ? '1' : '0'); } catch {}
    ccSpreadsMainClear();
    ccSpreadsRefresh(true).catch(err => {
      const host = cc('coincallSpreadsGridHost');
      if (host) host.innerHTML = '<div class="okx-nitro-status danger">Spreads header load failed: ' + ccEsc(err?.message || String(err)) + '</div>';
    });
  });
  cc('ccSpreadsAssetBtc')?.addEventListener('click', () => ccSpreadsSetAsset('BTC'));
  cc('ccSpreadsAssetEth')?.addEventListener('click', () => ccSpreadsSetAsset('ETH'));
  const savedInitialChart = ccSpreadsLoadMainChartSettings();
  const savedInitialAsset = savedInitialChart.asset === 'ETH' ? 'ETH' : 'BTC';
  ccSpreadsHeaderAsset = savedInitialAsset;
  cc('ccSpreadsAssetBtc')?.classList.toggle('active', savedInitialAsset === 'BTC');
  cc('ccSpreadsAssetEth')?.classList.toggle('active', savedInitialAsset === 'ETH');
  ccSpreadsRefresh().then(() => {
    ccSpreadsRestoreMainChartSettings({ skipHeavyRowAutoLoad:true });
  }).catch(err => {
    const host = cc('coincallSpreadsGridHost');
    if (host) host.innerHTML = '<div class="okx-nitro-status danger">Spreads header load failed: ' + ccEsc(err?.message || String(err)) + '</div>';
  });
  if (ccSpreadsClockTimer) { clearInterval(ccSpreadsClockTimer); ccSpreadsClockTimer = 0; }
  const gridHost = cc('coincallSpreadsGridHost');
  if(gridHost){
    if(window.PointerEvent) gridHost.addEventListener('pointerdown', ccSpreadsHandleCellActivation, true);
    else gridHost.addEventListener('mousedown', ccSpreadsHandleCellActivation, true);
    gridHost.addEventListener('click', ccSpreadsHandleCellActivation, true);
    gridHost.addEventListener('contextmenu', ccSpreadsHandleCellContextMenu, true);
  }
  document.querySelectorAll('[data-cc-spreads-main-days]').forEach(btn => btn.addEventListener('click', () => ccSpreadsMainSetDays(btn.dataset.ccSpreadsMainDays, btn)));
  const mainBidAskToggle = cc('ccSpreadsMainBidAskToggle');
  if(mainBidAskToggle){
    mainBidAskToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskEnabled;
    mainBidAskToggle.addEventListener('change', event => ccSpreadsMainSetBidAskEnabled(!!event.target.checked));
  }
  const mainBidAskDiffToggle = cc('ccSpreadsMainBidAskMedianDiffToggle');
  if(mainBidAskDiffToggle){
    mainBidAskDiffToggle.checked = !!CC_SPREADS_MAIN_CHART_STATE.bidAskDiffEnabled;
    mainBidAskDiffToggle.addEventListener('change', event => ccSpreadsMainSetBidAskDiffEnabled(!!event.target.checked));
  }
  cc('ccSpreadsMainFundingToggle')?.addEventListener('change', event => ccSpreadsMainSetFundingEnabled(!!event.target.checked));
  cc('ccSpreadsMainPairToggles')?.addEventListener('change', event => {
    const toggle = event.target?.closest?.('[data-cc-spreads-main-pair-toggle]');
    if(!toggle) return;
    ccSpreadsMainApplyPairVisibility(toggle.dataset.ccSpreadsMainPairToggle, !!toggle.checked);
  });
  document.addEventListener('click', ccSpreadsHandleCellActivation, true);
  cc('coincallSpreadsGridHost')?.addEventListener('keydown', event => {
    if(event.key !== 'Enter' && event.key !== ' ') return;
    const cell = event.target.closest('[data-cc-spreads-clickable="1"]');
    if(!cell) return;
    event.preventDefault();
    ccSpreadsActivateCell(cell);
  });
  cc('ccSpreadsTradeClose')?.addEventListener('click', ccSpreadsCloseTradeModal);
  const spreadsModal = cc('ccSpreadsTradeModal');
  if(spreadsModal){
    if(window.PointerEvent) spreadsModal.addEventListener('pointerdown', ccSpreadsHandleModalPointerClose, true);
    else spreadsModal.addEventListener('mousedown', ccSpreadsHandleModalPointerClose, true);
  }
  cc('ccSpreadsTradeModal')?.addEventListener('click', event => {
    if(event.target.id !== 'ccSpreadsTradeModal') return;
    if(Date.now() - ccSpreadsTradeModalOpenedAt < 500) return;
    ccSpreadsCloseTradeModal();
  });
  document.addEventListener('keydown', event => { if(event.key === 'Escape') ccSpreadsCloseTradeModal(); });
  window.addEventListener('resize', () => { if(ccSpreadsTradeModalOpen()) ccSpreadsDrawSpreadChart(); });
  window.addEventListener('beforeunload', ccSpreadsSaveChartState);
  document.addEventListener('visibilitychange', ccSpreadsSyncAutoRefresh);
  document.querySelectorAll('[data-cc-trade],[data-cc-layer]').forEach(el => el.addEventListener('click', () => setTimeout(ccSpreadsSyncAutoRefresh, 0)));
  ccSpreadsSyncAutoRefresh();
}
ccSpreadsInit();
