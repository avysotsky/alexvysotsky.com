#!/usr/bin/env bash
set -euo pipefail

cd /home/user/VAN
mkdir -p webclient/dist/_dev
rsync -a --delete \
  --exclude '_dev' \
  --exclude 'rollback' \
  --exclude '*.bak*' \
  --exclude '*.before*' \
  webclient/dist/ \
  webclient/dist/_dev/

find webclient/dist/_dev -type f \( -name '*.html' -o -name '*.js' -o -name '*.css' \) -print0 \
  | xargs -0 perl -pi -e \
      "s#(['\"])\/api/#\$1/dev-api/#g; s#(['\"])\/mutations\b#\$1/dev-mutations#g"

find webclient/dist/_dev -type f \( -name '*.html' -o -name '*.js' -o -name '*.css' \) -print0 \
  | xargs -0 perl -pi -e \
      "s#van_token#van_dev_token#g; s#van_session#van_dev_session#g"

find webclient/dist/_dev -type f -name '*.html' -print0 \
  | xargs -0 perl -pi -e \
      "s#location\.replace\('/app\?logout=1'\)#location.replace('/_dev/app.html?logout=1')#g; s#location\.replace\('/app'\)#location.replace('/_dev/app.html')#g; s#history\.replaceState\(\{\}, '', '/app'\)#history.replaceState({}, '', '/_dev/app.html')#g"

perl -0pi -e "s#localStorage\.clear = function \(\) \{\n\s*const res = origClear\(\);\n\s*hardRedirectToWrapper\(\);\n\s*return res;\n\s*\};#localStorage.clear = function () {\n          try { origRemoveItem(TOKEN_KEY); } catch (e) {}\n          try { origRemoveItem(SESSION_KEY); } catch (e) {}\n          hardRedirectToWrapper();\n        };#s" webclient/dist/_dev/app.html

find webclient/dist/_dev -type f -name '*.html' -print0 \
  | xargs -0 perl -pi -e \
      "s#href=\"/(app|trading(?:/[^\"?]*)?|deribit|okx|bybit|coincall(?:\.html|-spreads\.html|-spread-workstation\.html)?|mexc(?:\.html)?|binance(?:\.html)?|hyperliquid(?:\.html)?|bitget(?:\.html)?|kraken(?:\.html)?|kucoin(?:\.html)?|gate(?:\.html)?|coinbase(?:\.html)?|arbitrage|market-data|position-builder|backtester|visitors)\"#href=\"/_dev/\$1\"#g; s#href=\"/_dev/app\"#href=\"/_dev/app.html\"#g"

find webclient/dist/_dev -type f \( -name '*.html' -o -name '*.js' \) -print0 \
  | xargs -0 perl -pi -e \
      's#(window\.location\.href|location\.href|location\.assign)=(["\x27])/(app|trading(?:/[^"\x27?]*)?|deribit|okx|bybit|coincall(?:\.html|-spreads\.html|-spread-workstation\.html)?|mexc(?:\.html)?|binance(?:\.html)?|hyperliquid(?:\.html)?|bitget(?:\.html)?|kraken(?:\.html)?|kucoin(?:\.html)?|gate(?:\.html)?|coinbase(?:\.html)?|arbitrage|market-data|position-builder|backtester|visitors)\2#$1=$2/_dev/$3$2#g; s#(window\.open\()(["\x27])/(app|trading(?:/[^"\x27?]*)?|deribit|okx|bybit|coincall(?:\.html|-spreads\.html|-spread-workstation\.html)?|mexc(?:\.html)?|binance(?:\.html)?|hyperliquid(?:\.html)?|bitget(?:\.html)?|kraken(?:\.html)?|kucoin(?:\.html)?|gate(?:\.html)?|coinbase(?:\.html)?|arbitrage|market-data|position-builder|backtester|visitors)\2#$1$2/_dev/$3$2#g'

echo "Dev frontend updated: https://www.alexvysotsky.com/_dev/"
