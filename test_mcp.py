import json, os, subprocess, sys, threading, queue, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DOTNET = os.environ.get("DOTNET", r"C:\Program Files\dotnet\dotnet.exe")
DLL = os.environ.get("MCP_DLL", str(ROOT / "mcp" / "bin" / "Debug" / "net9.0" / "nightreign-relic-mcp.dll"))
SMITHBOX_DIR = Path(os.environ.get("SMITHBOX_DIR", str(ROOT.parent / "Smithbox")))
REG = os.environ.get(
    "REGULATION_BIN",
    str(SMITHBOX_DIR / "src" / "Smithbox.Data" / "Assets" / "PARAM" / "NR" / "Regulations" / "1.03.5 (10350000)" / "regulation.bin"),
)

p = subprocess.Popen([DOTNET, DLL], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)

q = queue.Queue()
def reader():
    for line in p.stdout:
        q.put(line)
threading.Thread(target=reader, daemon=True).start()

def send(obj):
    p.stdin.write(json.dumps(obj) + "\n")
    p.stdin.flush()

def recv(timeout=20):
    return json.loads(q.get(timeout=timeout))

# 1. initialize
send({"jsonrpc":"2.0","id":1,"method":"initialize",
      "params":{"protocolVersion":"2024-11-05","capabilities":{},
                "clientInfo":{"name":"test","version":"1"}}})
init = recv()
print("INIT server:", init.get("result",{}).get("serverInfo"))
send({"jsonrpc":"2.0","method":"notifications/initialized"})

# 2. tools/list
send({"jsonrpc":"2.0","id":2,"method":"tools/list"})
tools = recv()
names = [t["name"] for t in tools["result"]["tools"]]
print("TOOLS:", names)

def call(name, arguments, id):
    send({"jsonrpc":"2.0","id":id,"method":"tools/call",
          "params":{"name":name,"arguments":arguments}})
    r = recv(60)
    if "error" in r:
        print(f"  ERROR: {r['error']}"); return None
    # tool result content is a list of content blocks; structuredContent may also exist
    res = r["result"]
    return res

# 3. list_pools
lp = [n for n in names if "pool" in n.lower()][0]
print(f"\n== {lp} ==")
res = call(lp, {}, 3)
print(json.dumps(res.get("structuredContent", res), ensure_ascii=False)[:400])

# 4. preview_change
pv = [n for n in names if "preview" in n.lower()][0]
print(f"\n== {pv} (nodlc, normal, Vigor +1 + Mind +3) ==")
res = call(pv, {"regulation":REG,"hasDlc":False,"relicType":"normal","targets":["Vigor +1","Mind +3"]}, 4)
sc = res.get("structuredContent", res)
print(json.dumps(sc, ensure_ascii=False)[:900])

# 5. apply_change
ap = [n for n in names if "apply" in n.lower()][0]
out = str(ROOT / "mcp_apply_test.bin")
print(f"\n== {ap} (dlc, both, Vigor +1) -> {out} ==")
res = call(ap, {"regulation":REG,"hasDlc":True,"relicType":"both","targets":["Vigor +1"],"outPath":out}, 5)
sc = res.get("structuredContent", res)
print(json.dumps(sc, ensure_ascii=False)[:700])

p.stdin.close()
time.sleep(0.3)
err = p.stderr.read()
if err.strip():
    print("\n--- stderr (first 600) ---")
    print(err[:600])
p.terminate()
