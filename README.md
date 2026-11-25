# Static Scenery Ploppables

把**任意原版建筑**（分区生长 / 标志性 / 服务类）变成**静态、无属性、可随处放置**的造景件：从 Find It 式面板里选中即放置，建筑会连同原版地皮与周边随机装饰（树木、长椅、花盆、垃圾箱、灯杆……）一起生成，装饰使用游戏自身的随机抽签，所以每次摆放都不一样。

> A Cities: Skylines II code mod. Turn any vanilla building — zoned, signature or service — into a static, property-free scenery piece, lot surface and randomized decorations included.

---

## 功能 / Features

- **全量目录**：收录所有原版分区生长建筑（L2/L4 因外观与 L1/L3 相同而不生成）、全部标志性建筑（地标），以及八类服务建筑（教育 / 医疗 / 垃圾回收 / 消防 / 治安行政 / 通讯 / 交通 / 公园），含已拥有的 DLC 内容；构建完成时日志输出各类命中数。
- **Find It 式面板**：搜索框、图标页签（全部 / 住宅 / 混合型住宅 / 商业 / 办公 / 工业 / 8 类服务，共 14 个，图标运行时引用原版素材不复制）、筛选弹层（风格 / 密度 / 等级 / 面宽 / 进深，面宽进深含「>6」选项），5 列 × 2 行网格 + 滚轮翻页；点选条目蓝色高亮。
- **筛选按页签联动**：风格行仅住宅 / 混合 / 商业显示且生效（办公 / 工业实际无风格区分），密度 / 等级仅分区页签显示且生效——隐藏即暂停（已选值保留，切回自动恢复）；面宽 / 进深所有页签恒生效。
- **建筑图标**：标志性与服务建筑开箱即用原版专属图标；安装免费的 Asset Icon Library 后常规分区建筑也显示各自图标（完全可选，无任何前置依赖）。
- **两段式 Esc**：放置激活中按 Esc 先取消放置（面板保持打开），再按一次 Esc 才关闭面板；右键取消放置。
- **信息面板模组卡**：点击已放置的造景建筑，原版信息面板显示「用户创作内容」卡（名称 / 作者 / 模组 ID / 版本 / 订阅数实时来自 Paradox Mods）。
- **纯净外壳**：无居民、无岗位、无住户/公司、无费用、无解锁、无模拟开销；地皮上的原版车位照常可用（市民停车 / 取车与普通建筑一致）。
- **多语言**：English、简体中文、繁體中文、Deutsch、Français、Español、Português (BR)、Русский、Polski、日本語、한국어；其余语言回退英文。

## 使用 / Usage

1. 工具栏点击本模组图标。
2. 搜索或筛选目标建筑，点击条目进入放置。
3. 左键连放；右键取消放置；Esc 第一次取消放置、第二次关闭面板；右侧「筛选」按钮打开筛选弹层。

## 兼容与安全 / Compatibility & safety

- 装饰在**放置那一刻**一次性生成，之后不会反复重建；缺失的装饰 prefab 会被跳过。
- **迁移**（原版迁移工具）与**拆除**都会正确清理地皮——放置与迁移走的是引擎同一套地皮生成逻辑，因此不需要额外的残留清理。
- **卸载安全（实测）**：移除本模组后存档**能正常载入、不会崩溃**；已摆放的造景物会变成**白模占位**（prefab 缺失），用推土机拆除白模时**其装饰会一并清除**，不会留下残骸。
  > 建议：卸载前先把已摆放的造景物拆掉；或卸载后进图清理白模占位，再另存存档。
- 无前置依赖，可与 Find It / Move It / Anarchy / ctrlC / Asset Icon Library 共存。

## 设计说明 / Design notes

- 等级 2 / 4 **完全不生成**（不克隆、不注册、不进目录）：外观分别与 1 / 3 级相同，只占载入时间。
- 建筑按「直接放置剥离版建筑（Create 模式）」处理，与迁移路径共用引擎的地皮重建逻辑，因此放置、迁移、拆除三条路径的地皮归属一致；克隆一律与原版 UI 分组脱钩，绝不混入原版菜单。
- 服务建筑经 `CityService` 枚举精确分类（`ServiceObject → ServicePrefab → ServiceData`）；持续排除带功能性线网的类别（水电等），避免克隆产生异常网络节点。
- 目录在进入城市后一次性构建（分帧执行不卡帧），构建完成后整份目录一次推给前端；搜索、筛选、排序都在前端完成，打字不触发任何 C# 计算。
- 信息卡复用原生 PdxModsSection 渲染器（按 modId 实时取数），不碰 prefab 身份与存档。

## 构建 / Build

前置要求：

- 官方 Modding Toolchain（环境变量 `CSII_TOOLPATH`）
- `CSII_USERDATAPATH` 指向你的 CS2 用户数据目录（`<用户目录>\AppData\LocalLow\Colossal Order\Cities Skylines II`）
- .NET 6 运行时（ModPostProcessor 需要）
- Node.js（前端 `ssp-ui` 构建需要）

```bat
build.cmd
```

产物自动部署到 `%CSII_USERDATAPATH%\Mods\StaticSceneryPloppables\`（C# 产物 + `npm run build` 生成的 `.mjs` 与 `images/`）。

> **构建前必须完全退出游戏**：游戏会锁定部署目录下的 DLL，导致部署阶段报 `MSB3231`，并可能留下**缺少主 dll** 的不完整部署（症状：面板没有条目、存档中已摆放建筑变白模）。

本机环境记录见 `LOCAL.md`（含具体路径，不入库）。

## 发布 / Publish

`dotnet publish` + 官方 ModPublisher 三档 profile（`Publish` 首发 / `NewVersion` 新版 / `Update` 仅页面），详见 `Properties/PublishProfiles/` 与 `Properties/PublishConfiguration.xml`。发布前游戏必须退出。

## 排查 / Diagnostics

诊断代码默认**不参与编译**，需要时用开关重新构建：

```bat
dotnet build -c Release /p:SspDiagnostics=true
```

开启后可用的日志（`Mod.DiagnosticsEnabled`，`StaticSceneryPloppables/Mod.cs`，默认 `false`）：实体树 dump、构建分段耗时、目录排序样例。这些日志每轮都要拼字符串并写盘，**默认关闭以保帧率**。

## 目录 / Layout

```
StaticSceneryPloppables/           C# 模组（net48, ECS）
  Systems/GrowableVariantSystem.cs 目录生成（剥离 prefab、分类、排序）
  SceneryPloppableUISystem*.cs     UI 绑定（目录推送、选择、工具事件、Esc 两段式）
  SspModInfoSection.cs             信息面板「用户创作内容」卡
  Tools/SceneryPlacementTool.cs    放置工具（直接放剥离建筑）
  Localization/LocaleSSP.cs        11 语言词条
ssp-ui/                            前端（React + Gameface）
  src/mods/ssp-panel.tsx           主面板（页签/筛选/窗口化渲染/AIL 图标）
  src/mods/ssp-info-section.tsx    信息卡渲染器注册
assets/publish/                    发布素材（缩略图等）
docs/                              开发文档（优化清单、服务建筑接入方案等）
```

## 链接 / Links

- Paradox Mods: <https://mods.paradoxplaza.com/mods/167410/Windows>
- GitHub: <https://github.com/LiangHaoXiang/StaticSceneryPloppables>
- 问题反馈请走 GitHub Issues。
