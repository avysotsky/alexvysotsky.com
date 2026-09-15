#!/usr/bin/env python3

from pathlib import Path

FILES = [
    Path("webclient/dist/coincall-spread-workstation.html"),
    Path("webclient/dist/coincall.html"),
    Path("webclient/dist/coincall-spreads.html"),
]


def extract_function(source: str, name: str) -> str:
    start = source.find(f"function {name}(")
    if start < 0:
        raise AssertionError(f"missing {name}")
    brace = source.find("{", start)
    depth = 0
    for idx in range(brace, len(source)):
        if source[idx] == "{":
            depth += 1
        elif source[idx] == "}":
            depth -= 1
            if depth == 0:
                return source[start : idx + 1]
    raise AssertionError(f"unterminated {name}")


def check_file(path: Path) -> None:
    body = extract_function(path.read_text(), "ccApplyFuturesPrivateTradeEvent")
    push = body.find("ccOrderDiagnosticPush('futures-ws/fill'")
    looks = body.find("ccFuturesPrivateTradeLooksLikeFill(trade)")
    key = body.find("const key=ccFuturesPrivateTradeKey(trade)")
    dedupe = body.find("if(!key||seen.has(key))continue")
    seen = body.find("seen.add(key)")

    assert "action:'fill-message'" not in body, f"{path}: stale pre-dedupe fill action remains"
    assert -1 not in (looks, key, dedupe, seen, push), f"{path}: expected validation/dedupe/fill diagnostic markers"
    assert looks < key < dedupe < seen < push, f"{path}: fill diagnostic is not after validation and dedupe"
    assert "action:'unique-fill'" in body, f"{path}: unique fill diagnostic action missing"


for file_path in FILES:
    check_file(file_path)

print(f"CoinCall futures trade diagnostic static checks passed for {len(FILES)} files.")
