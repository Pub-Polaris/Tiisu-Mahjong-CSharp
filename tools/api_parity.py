#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""API parity check: Python server.py (7777) vs ASP.NET Core Tiisu.Api (7778).

Both servers are started against *separate* temporary copies of the data
directory (so POSTs do not leak into each other), then given the same requests.
The script compares HTTP status + parsed JSON semantics, and also compares the
resulting settings.json / state.json / stats.json files.

Usage (from anywhere):
    python NET/tools/api_parity.py
    python NET/tools/api_parity.py --api-dll NET/src/Tiisu.Api/bin/Debug/net10.0/Tiisu.Api.dll

Exit code: 0 = all compared cases equal, 1 = mismatch, 2 = environment problem.
"""

import argparse
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
NET = os.path.dirname(HERE)
REPO = os.path.dirname(NET)
WEB = os.path.join(REPO, "Web")
DEFAULT_DLL = os.path.join(NET, "src", "Tiisu.Api", "bin", "Debug", "net10.0", "Tiisu.Api.dll")
PY_PORT = 7777
CS_PORT = 7778


def log(msg=""):
    sys.stdout.write(str(msg) + "\n")
    sys.stdout.flush()


def port_open(port):
    sock = socket.socket()
    sock.settimeout(0.25)
    try:
        sock.connect(("127.0.0.1", port))
        return True
    except OSError:
        return False
    finally:
        sock.close()


def wait_port(port, timeout=30):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if port_open(port):
            return True
        time.sleep(0.2)
    return False


def free_port(preferred):
    """Return preferred if free, otherwise an OS-assigned loopback port."""
    if not port_open(preferred):
        return preferred
    sock = socket.socket()
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
    sock.close()
    return port


def call(port, method, path, body=None):
    url = "http://127.0.0.1:%d%s" % (port, path)
    data = None
    headers = {}
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            status = response.status
            raw = response.read()
            content_type = response.headers.get("Content-Type", "")
    except urllib.error.HTTPError as error:
        status = error.code
        raw = error.read()
        content_type = error.headers.get("Content-Type", "")
    except Exception as error:  # noqa: BLE001 - report environment failures plainly
        return None, {"__error__": repr(error)}, ""

    text = raw.decode("utf-8", errors="replace")
    try:
        parsed = json.loads(text) if text else None
    except json.JSONDecodeError:
        parsed = text
    return status, parsed, content_type


def start_process(command, cwd, out_path):
    out = open(out_path, "wb")
    return subprocess.Popen(command, cwd=cwd, stdout=out, stderr=subprocess.STDOUT), out


def stop_process(process):
    if process is None or process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=8)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=8)


def load_json_file(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def parse_jsonl(text):
    events = []
    for line in text.splitlines():
        line = line.strip()
        if line:
            events.append(json.loads(line))
    return events


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--api-dll", default=DEFAULT_DLL)
    parser.add_argument("--py-port", type=int, default=PY_PORT)
    parser.add_argument("--cs-port", type=int, default=CS_PORT)
    parser.add_argument("--keep", action="store_true")
    args = parser.parse_args()

    if not os.path.isfile(args.api_dll):
        log("C# API dll not found: %s" % args.api_dll)
        log("Build first: cd NET; dotnet build Tiisu.slnx")
        return 2
    if not os.path.isfile(os.path.join(WEB, "server.py")):
        log("Python server not found under %s" % WEB)
        return 2

    py_port = free_port(args.py_port)
    cs_port = free_port(args.cs_port)
    if py_port == cs_port:
        cs_port = free_port(cs_port + 1)

    root = tempfile.mkdtemp(prefix="tiisu_api_parity_")
    py_dir = os.path.join(root, "python")
    cs_dir = os.path.join(root, "csharp")
    os.makedirs(py_dir)
    os.makedirs(cs_dir)
    # Copy server.py and, only for this isolated run, point it at the chosen port.
    server_text = open(os.path.join(WEB, "server.py"), "r", encoding="utf-8").read()
    server_text = server_text.replace("PORT = 7777", "PORT = %d" % py_port, 1)
    with open(os.path.join(py_dir, "server.py"), "w", encoding="utf-8") as handle:
        handle.write(server_text)
    os.makedirs(os.path.join(py_dir, "logs"), exist_ok=True)
    os.makedirs(os.path.join(cs_dir, "logs"), exist_ok=True)

    py_proc = cs_proc = None
    py_out = cs_out = None
    failures = []
    checks = 0

    try:
        py_proc, py_out = start_process(
            [sys.executable, os.path.join(py_dir, "server.py")],
            py_dir,
            os.path.join(root, "python.stdout.log"))
        if not wait_port(py_port):
            log("Python server did not start on port %d" % py_port)
            return 2

        cs_proc, cs_out = start_process(
            ["dotnet", args.api_dll,
             "--port", str(cs_port),
             "--web-root", cs_dir,
             "--data-dir", cs_dir,
             "--content-root", cs_dir],
            cs_dir,
            os.path.join(root, "csharp.stdout.log"))
        if not wait_port(cs_port):
            log("C# server did not start on port %d" % cs_port)
            return 2

        log("=" * 72)
        log("API parity: Python :%d vs ASP.NET Core :%d" % (py_port, cs_port))
        log("=" * 72)
        log("%-5s %-7s %-42s %s" % ("#", "method", "path", "result"))
        log("-" * 72)

        cases = [
            ("GET", "/api/settings", None),
            ("POST", "/api/settings", {"thinkSeconds": 11, "recordTiles": True, "showMa": True}),
            ("GET", "/api/settings", None),
            ("POST", "/api/settings", {}),
            ("GET", "/api/state", None),
            ("POST", "/api/state", {
                "inProgress": True,
                "handNumber": 3,
                "scores": [100, 200, 300, 400],
                "playerUUID": "parity-uuid",
            }),
            ("GET", "/api/state", None),
            ("GET", "/api/stats", None),
            ("POST", "/api/stats", {"wins": 1, "tiisuin": 0, "player": "AI 南"}),
            ("POST", "/api/stats", {"witness": ["你", "AI 西"], "rounds": 1}),
            ("GET", "/api/stats", None),
            ("POST", "/api/stats", {"kind": "reset"}),
            ("POST", "/api/log", {"message": "parity hello"}),
            ("POST", "/api/chrlog", {"session": "parity", "entry": "line"}),
            ("POST", "/api/roundlog", {
                "session": "parity_s",
                "round": 3,
                "player": 1,
                "playerUUID": "uuid-1",
                "events": [
                    {"type": "round", "round": 3, "honba": 0, "player": 1,
                     "name": "ai_south", "wind": "south", "dealer": False,
                     "riichi": False, "uuid": "uuid-1", "session": "parity_s",
                     "roundWindIdx": 0, "dealerCount": 0, "extension": False},
                    {"type": "step", "a": "draw", "p": 1, "t": "1m",
                     "hand": "123m456p789s1122z", "melds": [], "discards": ["z2"],
                     "wall": 79, "rinshan": 4},
                ],
            }),
            ("GET", "/api/roundlog?session=parity_s&player=1&round=3", None),
        ]

        for index, (method, path, body) in enumerate(cases, 1):
            py_status, py_value, _ = call(py_port, method, path, body)
            cs_status, cs_value, _ = call(cs_port, method, path, body)
            ok = py_status == cs_status and py_value == cs_value
            if not ok:
                failures.append((method, path, py_status, py_value, cs_status, cs_value))
            checks += 1
            log("%-5d %-7s %-42s %s" % (index, method, path, "PASS" if ok else "FAIL"))
            if not ok:
                log("      python: %s %s" % (py_status, json.dumps(py_value, ensure_ascii=False)))
                log("      csharp: %s %s" % (cs_status, json.dumps(cs_value, ensure_ascii=False)))

        # The JSONL GET must be compared as events, not as one opaque string.
        py_status, py_value, _ = call(py_port, "GET", "/api/roundlog?session=parity_s", None)
        cs_status, cs_value, _ = call(cs_port, "GET", "/api/roundlog?session=parity_s", None)
        checks += 1
        ok = py_status == cs_status and parse_jsonl(py_value) == parse_jsonl(cs_value)
        log("%-5d %-7s %-42s %s" % (checks, "GET", "/api/roundlog?session=parity_s",
                                    "PASS" if ok else "FAIL"))
        if not ok:
            failures.append(("GET", "/api/roundlog?session=parity_s",
                             py_status, py_value, cs_status, cs_value))

        # File-level semantics: same JSON after the same request sequence.
        for name in ("settings.json", "state.json", "stats.json"):
            checks += 1
            py_path = os.path.join(py_dir, name)
            cs_path = os.path.join(cs_dir, name)
            py_value = load_json_file(py_path) if os.path.isfile(py_path) else None
            cs_value = load_json_file(cs_path) if os.path.isfile(cs_path) else None
            ok = py_value == cs_value
            log("%-5d %-7s %-42s %s" % (checks, "FILE", name, "PASS" if ok else "FAIL"))
            if not ok:
                failures.append(("FILE", name, None, py_value, None, cs_value))

        # Logs: JSONL, no CJK in the written file, on both sides.
        for label, directory in (("python", py_dir), ("csharp", cs_dir)):
            checks += 1
            path = os.path.join(directory, "logs", "parity_s", "round_003_p1_uuid-1",
                                "events.jsonl")
            text = open(path, "r", encoding="utf-8").read() if os.path.isfile(path) else ""
            has_cjk = any("\u4e00" <= ch <= "\u9fff" for ch in text)
            ok = bool(text) and not has_cjk
            log("%-5d %-7s %-42s %s" % (checks, "CJK", "%s events.jsonl" % label,
                                        "PASS" if ok else "FAIL"))
            if not ok:
                failures.append(("CJK", label, None, text[:200], None, ""))

        log("-" * 72)
        if failures:
            log("API PARITY: FAIL (%d/%d)" % (len(failures), checks))
            for failure in failures:
                log("  mismatch: %s" % (failure,))
            log("Temp dir kept for inspection: %s" % root)
            return 1

        log("API PARITY: %d/%d PASS" % (checks, checks))
        return 0
    finally:
        stop_process(py_proc)
        stop_process(cs_proc)
        if py_out:
            py_out.close()
        if cs_out:
            cs_out.close()
        if not args.keep and not failures:
            shutil.rmtree(root, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
