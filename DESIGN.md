# 遗物词条权重 MCP — 设计文档

> 目标：用一个 MCP，对 Nightreign(NR) 的 `regulation.bin` 批量改 `AttachEffectTableParam`
> 的 Roll Weight，实现"某个池里只让指定词条 roll 出来"。
> 本文档的所有结构性结论均已用 repo 自带的 SoulsFormats 对原版 NR `1.03.5` regulation 实测验证。

---

## 0. 已验证的 Ground Truth（实测，非假设）

核对工具：`dump/`（.NET 9 console，引用 `Andre.Formats` + `Andre.SoulsFormats`），
读 `Smithbox.Data/.../NR/Regulations/1.03.5 (10350000)/regulation.bin`。

| 事实 | 实测结果 |
|---|---|
| Param | `AttachEffectTableParam`，22088 行 |
| 字段 | `chanceWeight`(Base, **u16**) / `chanceWeight_dlc`(DLC, **s16**) |
| DLC 字段语义 | `-1 = 用 base roll weight`（回落哨兵） |
| **行结构** | **重复行 ID**：一个 row ID = 一张表/一个池；表内每行 = 一个词条 entry |
| **表内 attachEffectId** | **唯一**（如 table 100：290 entry = 290 distinct，0 重复）→ 一个目标在一张表里只对应 1 行 |
| 跨表共享 | 目标 attachEffectId（如 7000000）在池内每张表各出现 1 次 |
| 原版权重存放 | base 存真实权重(52/47/41/0/100…)；dlc 多为 -1，但 **DLC 池(2200000) dlc 已有真实值 [-1,40,160]** |

**两处对最初方案的纠正：**
1. 池子里的数字 `100/200/300/...` 是 **行 ID（表）**，不是 attachEffectId。
   attachEffectId 用来在表内定位**目标词条**。
2. GPT 担心的 "u16 base 写 raw 65535 哨兵" 是**伪问题**——实际改的是 DLC(s16) 字段（已与用户确认），
   `-1` 在 s16 里合法，不需要 65535。

---

## 1. 池（scope）定义

scope 由**两个维度**决定，执行前都要问用户：①是否拥有 **DLC**；②**遗物类型** 普通(normal)/深夜(deep_night)/全部(both)。

| DLC \ 类型 | normal（普通=三 Scene） | deep_night（深夜=debuff池） |
|---|---|---|
| **无 DLC** | `100,200,300` | `2000000,2100000,3000000` |
| **有 DLC** | `110,210,310` | `2200000,3000000` |

- `3000000`（诅咒/debuff，base 全 100）与 DLC 无关，两个深夜组都含。
- ⚠️ **深夜表机制坑**：深夜表对每个词条有真实 base 权重，但**很多词条 base=0**（=该深夜遗物不原生该词条）。
  对 base=0 的词条设 `dlc=-1` 回落到 0 → **roll 不出来**。普通表 base 恒 >0 无此问题。
  工具会对每个目标输出 `willRollHere`（base>0）并对 base=0 命中发告警。
- 深夜表行名在打包数据里未标注（全 `(0% weight)`），但 attachEffectId 与普通表同一套，
  故按名字选目标时用**全局名字索引**（任意表解析出 attachEffectId）再跨表按 id 应用。

---

## 2. 编辑操作（作用于 DLC 字段 `chanceWeight_dlc`）

**位置模型**：scope 里的表代表**词条位置**（`100/200/300` = 第1/2/3 位）。
一个词条只有在对应位置表里有正权重才 roll 得出来。因此目标词条要在 scope 的**每张表**里都"放权重"，
才能规避"位置不对导致抽不出"。

**多目标**（从 MVP 起就支持）。给定一组目标 attachEffectId `T = {A, B, C, ...}`，
对所选 scope 的**每一张表**（每个 row ID）：

1. 把该表内**所有 entry** 的 `chanceWeight_dlc` 设为 `0`。
2. 对 `T` 中每个目标：把该表内 `attachEffectId == 目标` 的那一行 `chanceWeight_dlc` 设为 `-1`。

效果（开 DLC 时）：非目标 dlc=0 永不 roll；每个目标 dlc=-1 回落到各自 base 正权重 →
**任何位置都只会 roll 出 T 里的词条**。

- 写入值类型：`s16`。`0` 与 `-1` 均合法，**不涉及 u16/65535**。
- 每张表对每个存在的目标精确改 1 行为 -1（attachEffectId 表内唯一），其余全 0。

### 关于概率
目标设 `-1` 后，其实际权重 = 该词条的 **base 值**（不同词条 base 不同，如 52/47/41），
所以多个目标之间概率**不必相等**。若需等概率，应改为统一写一个正数 dlc（而非 -1）——
本设计按教程用 `-1`，等概率作为后续选项。

### 边界情况
- **某目标在某表不存在**：跳过该表的该目标，结果里记录"表 X 无 attachEffectId Y"。
- **目标跨表共享**：逐表各改 1 行（预期行为，正是位置覆盖所需）。
- **base=0 的表/词条**（如 2000000）：设 -1 回落到 base=0 仍为 0 权重，无害但该位置不生效；如实报告。

---

## 3. 架构：A —— 离线直改 regulation.bin

- 读：`SFUtil.DecryptNightreignRegulation(path)` → `BND4`，取出 `AttachEffectTableParam.param`，
  `Param.Read` + `ApplyParamdef(def, ulong.MaxValue, name)`（def 取自 NR Defs XML；该字段无版本门控）。
- 写：改完 `Param.Write` 回 `BinderFile.Bytes`，`SFUtil.EncryptNightreignRegulation(bnd)` →
  **输出到新文件**（如 `regulation.edited.bin`），**绝不覆盖**输入。用户手动替换。
- Smithbox 操作期间无需关心并发（我们不碰它的项目状态；输出独立文件）。

**不选 Soapstone**：Smithbox 内置的 Soapstone gRPC 服务对 param 只读
（`SearchObjects`/`GetObjects`/`OpenObject`，无写/保存 RPC），不满足写入需求。
**不选改 Smithbox 源码**：架构 A 零侵入、确定性强、不需要用户跑自编译版本。

---

## 4. MCP 工具表面

> 运行时：.NET（SoulsFormats 是 .NET，机器已装 SDK 9.0.314）。
> 实现选项见 §6。坐标统一用 attachEffectId（数字）；名称查找为后续增强。

| 工具 | 入参 | 行为 |
|---|---|---|
| `list_pools` | — | 返回两个 scope 及其行 ID |
| `search_affixes` | `scope`, `query?` | 列出 scope 内（去重的）attachEffectId 及其当前 base/dlc，支持按 id 过滤 |
| `preview_change` | `scope`, `target_affixes[]` | 干跑：返回将改动的 (表, 行, 旧值→新值) 清单，不写盘 |
| `apply_change` | `regulation_in`, `scope`, `target_affixes[]`, `out_path?` | 执行编辑，写到新文件，返回改动汇总（表数、总行数、各目标命中表、写入值、输出路径、缺失告警） |

返回里明确列出：改了哪些表、每表多少 entry 置 0、目标行 ID/attachEffectId、写入值、输出文件路径、
以及"目标在某表缺失"的告警。

---

## 5. 安全 / 正确性约束

- 输入 regulation.bin 以只读方式打开；唯一产物是新文件。
- 写盘前**先做一次 encrypt→decrypt 往返自检**（解出来重新读 param，校验目标行 dlc==-1、
  同表其它行 dlc==0），确保 `EncryptNightreignRegulation` 产物可被正确读回。
- 校验 paramType / 行数与读入一致，避免 def 不匹配写坏。

---

## 6. 已定决策

1. **MCP 运行时**：纯 .NET MCP（`ModelContextProtocol` C# SDK），直接吃 SoulsFormats。✅
2. **目标输入**：**名字与 attachEffectId 都支持**。名字（如 `Vigor +1`）来自 repo 打包的
   `Param Row Names/English/AttachEffectTableParam.json`（按 table ID 的有序名字列表），
   与同 ID 的 param 行按行序 zip 得到 名字↔attachEffectId；匹配时忽略 `<Scene>` 前缀、大小写不敏感。
   数量不符则告警并回退 attachEffectId。✅
   - 池语义已核实：`100/200/300` = Delicate/Polished/Grand **三种 Scene 遗物**（非词条位置），
     三表词条列表一致；`110/210/310` 为其 base+DLC 版。`2000000/2100000` 为 0% 空表（改动 no-op），
     `3000000` 为诅咒类。三 Scene 全覆盖（用户确认）。
3. **多目标**：从 MVP 起支持（见 §2 位置模型）。✅
4. **目标值**：按教程用 `-1`（回落 base）。等概率（统一正数 dlc）为后续选项。✅
5. **输出**：写到新文件、不覆盖。默认命名待定（`regulation.edited.bin` 或带时间戳）—— 实现时定。

## 7. 实现里程碑

1. **[✅ 完成] encrypt 往返自检**（`roundtrip/`）：解密→改目标→重加密到新文件→再解密读回，
   校验目标行 dlc==-1、同表其它 dlc==0、未触碰数据不变。在原版 1.03.5 上 PASS（15 行-1 / 1838 行归零 / 输出 1.98MB）。
2. **[✅ 完成] `RelicAffix.Core`**（`core/`）：load / query pools / 名字↔attachEffectId join /
   compute(multi-target) / apply / encrypt-to-new / self-check。
3. **[✅ 完成] CLI**（`app/` → `relic-affix.exe`）：`list-pools` / `list-affixes` / `preview` / `apply`。
   在 vanilla 1.03.5 上 list/preview/apply+self-check 全过。
4. **[✅ 完成] .NET MCP server**（`mcp/` → `nightreign-relic-mcp.exe`）：
   `list_pools` / `list_affixes` / `preview_change` / `apply_change`。
   stdio JSON-RPC 冒烟测试通过（握手 / tools/list / 三个工具调用 / apply self-check=ok）。

## 8. 目录结构

```
nightreign-relic-mcp/
  core/   RelicAffix.Core   —— 共享核心库（引用 Smithbox 的 Andre.Formats）
  app/    relic-affix.exe   —— CLI
  mcp/    nightreign-relic-mcp.exe —— MCP server (stdio)
  dump/, roundtrip/         —— 早期验证工具（一次性，可留作参考）
```

运行时默认使用项目内置的 PARAMDEF + 行名资源；`DEFS_DIR` / `ROWNAMES` 环境变量可覆盖为其他
Smithbox 数据集。源码构建仍复用 Smithbox 的 `Andre.Formats`，可用 MSBuild 属性 `SmithboxDir`
指向 Smithbox checkout。

---

## 附：验证工具

`dump/`（已编译通过、已实测）。当前是探查版，后续会重构为 §4 的只读查询后端。
