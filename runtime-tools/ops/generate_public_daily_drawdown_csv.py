#!/usr/bin/env python3
import csv
import json
import tempfile
import urllib.request
from pathlib import Path

API_URL = "http://127.0.0.1:38131/api/public/daily-equity-chart"
OUTPUT_PATH = Path("/home/user/VAN-v2/webclient/dist/downloads/VAN_Combo_1_daily_drawdowns.csv")

with urllib.request.urlopen(API_URL, timeout=30) as response:
    payload = json.load(response)

drawdown_points = payload.get("drawdownPoints") or []
OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)

with tempfile.NamedTemporaryFile("w", delete=False, dir=str(OUTPUT_PATH.parent), newline="") as tmp:
    writer = csv.writer(tmp)
    writer.writerow(["date_utc", "max_drawdown_pct"])
    for point in drawdown_points:
        dt = str(point.get("dt") or "")[:10]
        value = point.get("maxDrawdownPct")
        writer.writerow([dt, value])
    temp_name = tmp.name

Path(temp_name).replace(OUTPUT_PATH)
OUTPUT_PATH.chmod(0o644)
print(f"wrote {OUTPUT_PATH} with {len(drawdown_points)} rows")
