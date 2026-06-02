import json, subprocess, threading, queue, time
DOTNET=r"C:\Program Files\dotnet\dotnet.exe"
DLL=r"C:\Users\32445\Desktop\relic-affix-mcp\mcp\bin\Release\net9.0\relic-affix-mcp.dll"
REG=r"C:\Users\32445\Desktop\Smithbox\regulation.bin"
OUTDIR=r"C:\Users\32445\Desktop\relic-affix-mcp\batchtest"
import os; os.makedirs(OUTDIR, exist_ok=True)

p=subprocess.Popen([DOTNET,DLL],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8",bufsize=1)
q=queue.Queue()
threading.Thread(target=lambda:[q.put(l) for l in p.stdout],daemon=True).start()
def send(o): p.stdin.write(json.dumps(o)+"\n"); p.stdin.flush()
def recv(t=60): return json.loads(q.get(timeout=t))
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}})
recv(); send({"jsonrpc":"2.0","method":"notifications/initialized"})
send({"jsonrpc":"2.0","id":2,"method":"tools/list"}); tl=recv()
print("TOOLS:", [t["name"] for t in tl["result"]["tools"]])
send({"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"apply_relics","arguments":{
  "regulation":REG,"hasDlc":True,"relicType":"normal","outDir":OUTDIR,
  "relics":[
    {"name":"ironeye","targets":["7280000","7000201","7000401"]},
    {"name":"lowhp","targets":["7012300","7240000","7005600"]}
  ]}}})
r=recv(120)
if "error" in r: print("ERROR:", r["error"])
else:
    txt=r["result"]["content"][0]["text"]; d=json.loads(txt)
    print("relicCount:", d["relicCount"], "| pool:", d["pool"])
    for relic in d["relics"]:
        print("  ->", relic["outputPath"], "| selfCheck:", relic["selfCheck"],
              "| -1 rows:", relic["summary"]["targetRowsSetToMinus1"],
              "| warnings:", len(relic["warnings"]))
p.stdin.close(); time.sleep(0.2); p.terminate()
print("files:", os.listdir(OUTDIR))
