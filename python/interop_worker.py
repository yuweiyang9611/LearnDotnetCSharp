"""Line-delimited JSON worker used by the C# Python interop demo."""

from __future__ import annotations

import json
import math
import os
import statistics
import sys
import time
from typing import Any

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")


def read_generation() -> int:
    if len(sys.argv) == 1:
        return 0
    if len(sys.argv) == 3 and sys.argv[1] == "--generation":
        generation = int(sys.argv[2])
        if generation < 0:
            raise ValueError("generation must not be negative")
        return generation
    raise ValueError("expected optional --generation <number>")


GENERATION = read_generation()


def runtime_info() -> dict[str, Any]:
    return {
        "version": sys.version.split()[0],
        "executable": sys.executable,
        "prefix": sys.prefix,
        "basePrefix": sys.base_prefix,
        "inVenv": sys.prefix != sys.base_prefix,
        "processId": os.getpid(),
        "generation": GENERATION,
    }


def analyze(request: dict[str, Any]) -> dict[str, Any]:
    values = request.get("values")
    if not isinstance(values, list) or not values:
        raise ValueError("values must be a non-empty JSON array")
    if any(isinstance(value, bool) or not isinstance(value, (int, float)) for value in values):
        raise ValueError("all values must be JSON numbers")

    numeric_values = [float(value) for value in values]
    if not all(math.isfinite(value) for value in numeric_values):
        raise ValueError("all values must be finite")

    return {
        "count": len(numeric_values),
        "sum": math.fsum(numeric_values),
        "mean": statistics.fmean(numeric_values),
        "median": statistics.median(numeric_values),
    }


def handle(request: dict[str, Any]) -> dict[str, Any]:
    request_id = request.get("id")
    if request.get("protocolVersion") != 1:
        raise ValueError("unsupported protocolVersion")

    operation = request.get("operation")
    if operation == "crash":
        print(f"forced crash for {request_id}", file=sys.stderr, flush=True)
        os._exit(86)

    if operation == "handshake":
        return {
            "id": request_id,
            "ok": True,
            "echo": None,
            "runtime": runtime_info(),
            "result": None,
            "error": None,
        }

    if operation != "analyze":
        raise ValueError("unsupported operation")

    delay_milliseconds = request.get("delayMilliseconds", 0)
    if (
        isinstance(delay_milliseconds, bool)
        or not isinstance(delay_milliseconds, int)
        or not 0 <= delay_milliseconds <= 5000
    ):
        raise ValueError("delayMilliseconds must be an integer between 0 and 5000")
    if delay_milliseconds:
        time.sleep(delay_milliseconds / 1000)

    if request.get("crashBeforeResponse") is True:
        print(f"forced analyze crash for {request_id}", file=sys.stderr, flush=True)
        os._exit(86)

    return {
        "id": request_id,
        "ok": True,
        "echo": request.get("label"),
        "runtime": runtime_info(),
        "result": analyze(request),
        "error": None,
    }


def main() -> None:
    print(
        f"worker started pid={os.getpid()} generation={GENERATION}",
        file=sys.stderr,
        flush=True,
    )
    for raw_line in sys.stdin:
        request_id: Any = None
        try:
            request = json.loads(raw_line)
            if not isinstance(request, dict):
                raise ValueError("request must be a JSON object")
            request_id = request.get("id")
            response = handle(request)
        except (TypeError, ValueError, json.JSONDecodeError) as error:
            response = {
                "id": request_id,
                "ok": False,
                "echo": None,
                "runtime": runtime_info(),
                "result": None,
                "error": f"{type(error).__name__}: {error}",
            }

        print(json.dumps(response, ensure_ascii=False, allow_nan=False), flush=True)


if __name__ == "__main__":
    main()
