# relic-affix-mcp

Nightreign(NR) 遗物词条 Roll Weight 修改工具。它离线读取 `regulation.bin`，只修改
`AttachEffectTableParam` 的 `chanceWeight_dlc` 字段：同一池内所有词条设为 `0`，目标词条设为
`-1`（回落 base 权重）。工具只写新文件，不覆盖输入文件，并在写后重新解密自检。

> 已用原版 Nightreign `1.03.5 (10350000)` regulation 验证。游戏更新后请先用 `preview` /
> `list_affixes` 检查结果，再替换文件。

## Safety

- 输入 `regulation.bin` 不会被覆盖。
- 输出路径等于输入文件、输出文件已存在、目标为空、目标名称无法解析、目标在所选池内无命中时，工具会拒绝写入。
- 这是离线修改游戏参数的工具。请自己备份文件，避免在游戏或 Smithbox 正在写同一个文件时替换。
- 是否适合联机、反作弊环境或公开视频发布，由使用者自行判断并承担风险。

## Pools

scope 由两个参数决定：是否有 DLC，以及遗物类型。

| DLC | `normal` 普通三 Scene | `deep_night` 深夜/debuff |
|---|---|---|
| `false` | `100,200,300` | `2000000,2100000,3000000` |
| `true` | `110,210,310` | `2200000,3000000` |

`3000000` 是共享 debuff 池。`relicType` 也可以传 `both`。

深夜表里有些词条 base 权重为 `0`，即使 `dlc=-1` 也不会 roll 出来。返回结果里的
`willRollHere=false` 和 warnings 会标出来。

## Release Install

从 GitHub Releases 下载发布 zip，解压后把 `relic-affix-mcp.exe` 配进 Claude Code：

```powershell
claude mcp add relic-affix -- "C:\path\to\relic-affix-mcp.exe"
```

或者手动加入 MCP 配置：

```json
{
  "mcpServers": {
    "relic-affix": {
      "command": "C:\\path\\to\\relic-affix-mcp.exe"
    }
  }
}
```

## MCP Tools

- `list_pools()`
- `list_affixes(regulation, hasDlc, relicType, query?)`
- `preview_change(regulation, hasDlc, relicType, targets[])`
- `apply_change(regulation, hasDlc, relicType, targets[], outPath?)`
- `apply_relics(regulation, hasDlc, relicType, relics[], outDir?)`

`targets` 可以是词条名，如 `Vigor +1`，也可以是 `attachEffectId` 数字。多个不同遗物请用
`apply_relics`，它会为每个遗物生成一个独立 bin；不要把多套遗物词条混到同一个 bin。

## CLI

构建后也可以直接用命令行工具：

```powershell
relic-affix list-pools
relic-affix dump-table   "<regulation.bin>" 2100000
relic-affix list-affixes "<regulation.bin>" dlc normal Vigor
relic-affix preview      "<regulation.bin>" dlc normal "Vigor +1" "Mind +3"
relic-affix apply        "<regulation.bin>" dlc normal "<out.bin>" "Vigor +1"
```

## Build From Source

源码复用 Smithbox 的 `Andre.Formats` / `SoulsFormats`。默认假设目录结构如下：

```text
Desktop/
  Smithbox/
  relic-affix-mcp/
```

如果 Smithbox 在别处，构建时传 `SmithboxDir`：

```powershell
dotnet build mcp\RelicAffix.Mcp.csproj -c Release -p:SmithboxDir="C:\path\to\Smithbox"
dotnet build app\RelicAffix.csproj -c Release -p:SmithboxDir="C:\path\to\Smithbox"
```

项目内置了 `AttachEffectTableParam.xml` 和行名 JSON。需要测试其他 Smithbox 数据时，可以用环境变量覆盖：

```powershell
$env:DEFS_DIR="C:\path\to\Smithbox\src\Smithbox.Data\Assets\PARAM\NR\Defs"
$env:ROWNAMES="C:\path\to\AttachEffectTableParam.json"
```

## Test

```powershell
dotnet build mcp\RelicAffix.Mcp.csproj -c Debug
python test_safety.py
dotnet build mcp\RelicAffix.Mcp.csproj -c Release
```

CI 会在 Windows 上 checkout Smithbox、构建 Debug/Release，并运行安全回归测试。

## License

MIT. See [LICENSE](LICENSE).
