# Static Scenery Ploppables

把**任意原版分区建筑**变成**静态、无属性、可随处放置**的造景件：从 Find It 式面板里选中即放置，建筑会连同原版地皮与周边随机装饰（树木、长椅、花盆、垃圾箱、灯杆……）一起生成，装饰使用游戏自身的随机抽签，所以每次摆放都不一样。

> A Cities: Skylines II code mod. Turn any vanilla zoned building into a static, property-free scenery piece — lot surface and randomized decorations included.

---

## 功能 / Features

- **全量目录**：扫描所有原版分区建筑（当前 2077 条可见条目），L2/L4 因外观与 L1/L3 相同而不生成。
- **Find It 式面板**：搜索框、类型页签（全部 / 住宅 / 混合型住宅 / 商业 / 办公 / 工业）、筛选弹层（风格 / 密度 / 等级 / 面宽 / 进深），5 列 × 2 行网格 + 滚轮翻页。
- **8 级排序**：分区 → 密度 → 占地总面积 → 面宽 → 进深 → 等级 → 标志性 → 名称自然序（字母不区分大小写、数字按数值）。
- **一步式放置**：点击条目即进入放置，左键连放，右键 / Esc 退出；Esc 认领游戏 Back 动作，不会顺带弹出暂停菜单。
- **原版地皮与装饰**：放置时按建筑自身的 `ObjectSubAreas` 生成地皮，并按 `SpawnableObject` 的概率/随机组抽取装饰——与游戏分区建筑生成时的抽签逻辑同源。
- **纯净外壳**：无居民、无岗位、无住户/公司、无模拟开销。
- **多语言**：English、简体中文、繁體中文、Deutsch、Français、Español、Português (BR)、Русский、Polski、日本語、한국어；其余语言回退英文。

## 使用 / Usage

1. 工具栏点击本模组图标（图标为「菱形地皮 + 方块」）。
2. 搜索或筛选目标建筑，点击条目。
3. 在地图上左键放置；右侧「筛选」按钮打开筛选弹层。
4. 右键或 Esc 退出手动放置；面板打开时按 Esc 也会关闭面板。

## 兼容与安全 / Compatibility & safety

- 装饰在**放置那一刻**一次性生成，之后不会反复重建；缺失的装饰 prefab 会被跳过。
- **迁移**（原版迁移工具）与**拆除**都会正确清理地皮——放置与迁移走的是引擎同一套地皮生成逻辑，因此不需要额外的残留清理。
- **卸载安全（实测）**：移除本模组后存档**能正常载入、不会崩溃**；已摆放的造景物会变成**白模占位**（prefab 缺失），用推土机拆除白模时**其装饰会一并清除**，不会留下残骸。
  > 建议：卸载前先把已摆放的造景物拆掉；或卸载后进图清理白模占位，再另存存档。
- 无前置依赖，可与 Find It / Move It / Anarchy / ctrlC 共存。

## 设计说明 / Design notes

- 等级 2 / 4 **完全不生成**（不克隆、不注册、不进目录）：外观分别与 1 / 3 级相同，只占载入时间；由此早期开发存档里的 L2/L4 造景会变白模，线上用户不存在这种存档。
- 建筑按「直接放置剥离版建筑（Create 模式）」处理，与迁移路径共用引擎的地皮重建逻辑，因此放置、迁移、拆除三条路径的地皮归属一致。
- 目录在进入城市后一次性构建（约 4–5 秒，分帧执行不卡帧），构建完成后整份目录一次推给前端；搜索、筛选、排序都在前端完成，打字不触发任何 C# 计算。

## 构建 / Build

前置（本机已配置）：

- 官方 Modding Toolchain（`CSII_TOOLPATH`）
- `CSII_USERDATAPATH` 指向 `C:\Users\10135\AppData\LocalLow\Colossal Order\Cities Skylines II`
- .NET 6 运行时（ModPostProcessor 需要）

```bat
build.cmd
```

产物自动部署到 `%CSII_USERDATAPATH%\Mods\StaticSceneryPloppables\`（C# 产物 + `npm run build` 生成的 `.mjs` 与 `images/`）。

> **构建前必须完全退出游戏**：游戏会锁定部署目录下的 DLL，导致部署阶段报 `MSB3231`，并可能留下**缺少主 dll** 的不完整部署（症状：面板没有条目、存档中已摆放建筑变白模）。

## 排查 / Diagnostics

诊断代码默认**不参与编译**，需要时用开关重新构建：

```bat
dotnet build -c Release /p:SspDiagnostics=true
```

开启后可用的日志（`Mod.DiagnosticsEnabled`，`StaticSceneryPloppables/Mod.cs`，默认 `false`）：

- 实体树 dump（放置/迁移前后的地皮归属）；
- 构建分段耗时（`build breakdown`: clone / addPrefab / entry+name）；
- 目录排序样例（前 15 条，核对 8 级排序）。

这些日志每轮都要拼字符串并写盘，**默认关闭以保帧率**。

## 目录 / Layout

```
StaticSceneryPloppables/           C# 模组（net48, ECS）
  Systems/GrowableVariantSystem.cs 目录生成（剥离 prefab、分类、排序）
  Systems/HandleSSPDeleteInXFramesSystem.cs  延迟删除（兼容旧存档遗留组件）
  SceneryPloppableUISystem*.cs     UI 绑定（目录推送、选择、工具事件）
  Tools/SceneryPlacementTool.cs    放置工具（直接放剥离建筑）
  Localization/LocaleSSP.cs        11 语言词条
ssp-ui/                            前端（React + Gameface）
assets/publish/                    发布素材（缩略图等）
```

## 链接 / Links

- Paradox Mods: <https://mods.paradoxplaza.com/mods/167410/Windows>
- GitHub: <https://github.com/LiangHaoXiang/StaticSceneryPloppables>
- 问题反馈请走 GitHub Issues。
