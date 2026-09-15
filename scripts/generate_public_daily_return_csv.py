#!/usr/bin/env python3
import csv
import json
import tempfile
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path

API_URL = "http://127.0.0.1:38031/api/public/daily-equity-chart"
OUTPUT_PATH = Path("/home/user/VAN/webclient/dist/downloads/VAN_Combo_1_daily_returns.csv")

with urllib.request.urlopen(API_URL, timeout=30) as response:
    payload = json.load(response)

points = payload.get("points") or []
initial_investment = payload.get("initialInvestmentUsd")
if not points:
    raise RuntimeError("daily-equity-chart returned no points")
if initial_investment is None:
    raise RuntimeError("daily-equity-chart returned no initialInvestmentUsd")

initial_investment = float(initial_investment)
OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)

generated_at_utc = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

with tempfile.NamedTemporaryFile("w", delete=False, dir=str(OUTPUT_PATH.parent), newline="") as tmp:
    writer = csv.writer(tmp)
    writer.writerow(["date_utc", "equity_usd", "daily_pnl_usd", "daily_return_pct"])

    first_dt = datetime.fromisoformat(str(points[0]["dt"]).replace("Z", "+00:00")).date()
    writer.writerow([(first_dt - timedelta(days=1)).isoformat(), f"{initial_investment:.2f}", "", ""])

    previous_equity = initial_investment
    for point in points:
        dt = datetime.fromisoformat(str(point["dt"]).replace("Z", "+00:00")).date().isoformat()
        equity = float(point["v"])
        daily_pnl = equity - previous_equity
        daily_return_pct = (daily_pnl / previous_equity) * 100 if previous_equity else 0.0
        writer.writerow([dt, f"{equity:.2f}", f"{daily_pnl:.2f}", f"{daily_return_pct:.6f}"])
        previous_equity = equity

    temp_name = tmp.name

Path(temp_name).replace(OUTPUT_PATH)
OUTPUT_PATH.chmod(0o644)
print(f"wrote {OUTPUT_PATH} with {len(points) + 1} rows at {generated_at_utc}")
