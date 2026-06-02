import json
import os
import queue
import shutil
import subprocess
import tempfile
import threading
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parent
DOTNET = os.environ.get("DOTNET", r"C:\Program Files\dotnet\dotnet.exe")
APP = ROOT / "app" / "RelicAffix.csproj"
MCP_DLL = ROOT / "mcp" / "bin" / "Debug" / "net9.0" / "nightreign-relic-mcp.dll"
SMITHBOX_DIR = Path(os.environ.get("SMITHBOX_DIR", str(ROOT.parent / "Smithbox")))
SOURCE_REG = Path(os.environ.get(
    "REGULATION_BIN",
    str(SMITHBOX_DIR / "src" / "Smithbox.Data" / "Assets" / "PARAM" / "NR" / "Regulations" / "1.03.5 (10350000)" / "regulation.bin"),
))


def run_cli(*args):
    return subprocess.run(
        [DOTNET, "run", "--project", str(APP), "--", *map(str, args)],
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=120,
    )


def start_mcp():
    process = subprocess.Popen(
        [DOTNET, str(MCP_DLL)],
        cwd=ROOT,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        bufsize=1,
    )
    outq = queue.Queue()

    def reader():
        for line in process.stdout:
            outq.put(line)

    threading.Thread(target=reader, daemon=True).start()

    def send(obj):
        process.stdin.write(json.dumps(obj) + "\n")
        process.stdin.flush()

    def recv(timeout=60):
        return json.loads(outq.get(timeout=timeout))

    send(
        {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "safety-test", "version": "1"},
            },
        }
    )
    recv()
    send({"jsonrpc": "2.0", "method": "notifications/initialized"})
    return process, send, recv


def call_tool(send, recv, name, arguments, request_id):
    send(
        {
            "jsonrpc": "2.0",
            "id": request_id,
            "method": "tools/call",
            "params": {"name": name, "arguments": arguments},
        }
    )
    return recv()


def is_tool_error(response):
    return "error" in response or response.get("result", {}).get("isError") is True


def assert_cli_rejects_unresolved_target():
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / "bad.bin"
        result = run_cli("apply", SOURCE_REG, "dlc", "normal", out, "definitely-not-an-affix")
        assert result.returncode != 0, result.stdout + result.stderr
        assert not out.exists(), "apply wrote an output file despite an unresolved target"


def assert_cli_rejects_output_equal_to_input():
    with tempfile.TemporaryDirectory() as td:
        copied = Path(td) / "regulation.bin"
        shutil.copy2(SOURCE_REG, copied)
        before = copied.read_bytes()
        result = run_cli("apply", copied, "dlc", "normal", copied, "Vigor +1")
        assert result.returncode != 0, "apply accepted an output path equal to the input path"
        assert copied.read_bytes() == before, "input regulation was modified in place"


def assert_mcp_rejects_empty_relic_targets_and_duplicate_outputs():
    process, send, recv = start_mcp()
    try:
        with tempfile.TemporaryDirectory() as td:
            response = call_tool(
                send,
                recv,
                "apply_relics",
                {
                    "regulation": str(SOURCE_REG),
                    "hasDlc": True,
                    "relicType": "normal",
                    "outDir": td,
                    "relics": [{"name": "empty", "targets": []}],
                },
                2,
            )
            assert is_tool_error(response), "apply_relics accepted a relic with no targets"

            response = call_tool(
                send,
                recv,
                "apply_relics",
                {
                    "regulation": str(SOURCE_REG),
                    "hasDlc": True,
                    "relicType": "normal",
                    "outDir": td,
                    "relics": [
                        {"name": "same", "targets": ["Vigor +1"]},
                        {"name": "same", "targets": ["Mind +3"]},
                    ],
                },
                3,
            )
            assert is_tool_error(response), "apply_relics accepted duplicate output file names"
    finally:
        process.stdin.close()
        time.sleep(0.2)
        process.terminate()


if __name__ == "__main__":
    assert_cli_rejects_unresolved_target()
    assert_cli_rejects_output_equal_to_input()
    assert_mcp_rejects_empty_relic_targets_and_duplicate_outputs()
    print("safety tests passed")
