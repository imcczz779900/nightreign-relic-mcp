# relic-affix-mcp

改 Nightreign(NR) 遗物词条 Roll Weight 的工具。离线直改 `regulation.bin`（架构 A），
对一个池里的每张表把所有词条的 `chanceWeight_dlc` 设为 `0`、目标词条设为 `-1`（回落 base 权重），
**输出到新文件、绝不覆盖原文件**，写后自带往返自检。设计依据见 [DESIGN.md](DESIGN.md)。

## 组成

| 目录 | 产物 | 用途 |
|---|---|---|
| `core/` | RelicAffix.Core | 共享核心库（复用 Smithbox 的 `Andre.Formats` 解/加密 NR regulation） |
| `app/`  | `relic-affix.exe` | 命令行工具 |
| `mcp/`  | `relic-affix-mcp.exe` | MCP server（stdio） |

> 池/字段/位置等结论均已对原版 1.03.5 regulation 实测验证。

## 构建

```powershell
dotnet build "C:\Users\32445\Desktop\relic-affix-mcp\mcp\RelicAffix.Mcp.csproj" -c Debug
dotnet build "C:\Users\32445\Desktop\relic-affix-mcp\app\RelicAffix.csproj"    -c Debug
```

## 池（scope）= DLC有无 × 遗物类型

| DLC \ 类型 | normal（普通=三 Scene） | deep_night（深夜=debuff池） |
|---|---|---|
| **nodlc** | 100,200,300 | 2000000,2100000,3000000 |
| **dlc** | 110,210,310 | 2200000,3000000 |

> ⚠️ 执行前先问玩家：**有没有 DLC**、要**普通还是深夜**（或 both=全部）。`3000000`(诅咒/debuff) 两个深夜组都含。
>
> ⚠️ **深夜表**很多词条 base=0（非该遗物原生）→ 设 -1 也 roll 不出来；工具会标 `willRollHere=false` 并告警。
> 深夜表名字未标注，但按名字选目标仍可用（全局名字索引解析 attachEffectId 后跨表应用）。

目标可用**词条名**（如 `Vigor +1`，忽略前缀、大小写不敏感）或 **attachEffectId**（数字），可混用、可多个。

## CLI 用法

```powershell
relic-affix list-pools
relic-affix dump-table   "<regulation.bin>" 2100000
relic-affix list-affixes "<regulation.bin>" nodlc normal Vigor
relic-affix preview      "<regulation.bin>" nodlc normal "Vigor +1" "Mind +3"
relic-affix apply        "<regulation.bin>" dlc   both   "<out.bin>" "Vigor +1" "Mind +3"
```

`<dlc|nodlc>` = 是否有 DLC；`<normal|deep_night|both>` = 遗物类型。

`apply` 写出新文件后会自检（重新解密读回校验），打印 `SELF-CHECK OK`。把输出文件手动替换你的 regulation.bin。
如果目标词条名无法解析、目标在所选池内没有命中、输出路径等于输入文件、或输出文件已存在，工具会拒绝写入。

## MCP 工具

`list_pools` / `list_affixes(regulation, hasDlc, relicType, query?)` /
`preview_change(regulation, hasDlc, relicType, targets[])` /
`apply_change(regulation, hasDlc, relicType, targets[], outPath?)` — **单个遗物**，一次=一个 bin /
`apply_relics(regulation, hasDlc, relicType, relics[], outDir?)` — **多个遗物**，每个遗物一个 bin

（`hasDlc`: bool；`relicType`: `normal|deep_night|both`；`relics[]` 每项 = `{targets[], name?}`）

> ⚠️ **一个遗物一个 bin**：多遗物**不能**塞进一个 bin（否则每个槽会从所有词条里随机，拿不到指定那套）。
> 想要的遗物对应的 bin 改名成 `regulation.bin` 替换、roll 出来，再换下一个。`apply_relics` 已把这条规则固化。

> 部署用的是 **Release** 产物（`mcp/bin/Release/net9.0/relic-affix-mcp.exe`），`.claude.json` 已指向它。
> 改完代码重新部署：`dotnet build mcp -c Release`（运行中的 server 会锁旧 exe，故用 Release/重启后再 build）。

注册到 Claude Code：

```powershell
claude mcp add relic-affix -- "C:\Users\32445\Desktop\relic-affix-mcp\mcp\bin\Release\net9.0\relic-affix-mcp.exe"
```

或在 MCP 配置 JSON 里加：

```json
{
  "mcpServers": {
    "relic-affix": {
      "command": "C:\\Users\\32445\\Desktop\\relic-affix-mcp\\mcp\\bin\\Release\\net9.0\\relic-affix-mcp.exe"
    }
  }
}
```

## 依赖说明

PARAMDEF 与词条名取自 Smithbox repo（`Config.cs` 硬编码当前机器路径），
可用环境变量 `DEFS_DIR` / `ROWNAMES` 覆盖。换机或 Smithbox 移动后需相应调整。
