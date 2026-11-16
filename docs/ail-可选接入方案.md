# Asset Icon Library 可选接入方案（只读设计，未实施）

> 范围：仅讨论 Static Scenery Ploppables（SSP）在**用户自愿启用** Asset Icon Library（AIL）时，尽力展示其已有建筑图；AIL 不存在或图片不可用时保留内置分区图标。**不引入 PDX / 项目构建依赖，不复制或再分发 AIL 图片，不改变建筑实体或放置功能。**
>
> 固定源码：AIL `.research/mod-sources/AssetIconLibrary-CSII` 提交 `3c61fa1c740a6e075380fffc51a82c9d79555738`，SSP `0dfadede0892a387d7e28823cb2ed41c2246c263`；文内相对路径以本文所在 `docs/` 为基准。以下是实现**建议和待实测假设**，不是已上线功能。

## 结论和推荐接入点

**推荐“读取原版源 prefab 当前 `ImageSystem.GetIcon` → 验证 `coui://ail/` 候选 → 仅对可见 10 项送 UI → `<img onError>` 回退本模组自带分区 SVG”。** 不要依赖 AIL 程序集、调用内部 `IconLibraryUtil`、反射 AIL 状态、猜测 `coui://ail/ColoredPropless/BuildingPrefab.<name>.png` 路径，或把用户是否安装 AIL 等同于图片已就绪。游戏 `ImageSystem.GetIcon(prefab)` 返回的是当前 `m_Icon` 经 `ResolveUri` 的结果，适合从**原版源建筑**获取已经完成的 AIL 替换；`GetThumbnail` 在无 icon 时退回 `thumbnail://`，而本机当前游戏版本的该 URI 只查预制图集，不会给缺图生长建筑自动渲染，因此不是这个方案的备用生图途径。[游戏 ImageSystem](<../../.research/treectl/gamefull/Game.UI/ImageSystem.cs#L60-L115>)；[AIL 实际替换](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L110-L163>)；[前轮图集核验](<asset-icon-library-调研.md#L7-L17>)。

### 一、可选性：区分“启用”“替换完成”“图片加载成功”

1. SSP 本身不声明 AIL 为发布或编译依赖：目标是**没有 AIL 时目录、分区图标、放置均照常工作**。Find It 有用 `ListModsEnabled().Any(name => name.StartsWith("AssetIconLibrary, "))` 的检测先例，但这只是**一次性缓存的启用状态**，不能表明 AIL 图已经解包、索引建立、宿主注册，更不能表明指定建筑已有图。SSP 可以完全不调用这个检测，直接尝试可见源建筑 `GetIcon`；如果希望减少首次无 AIL 的重复检查，可用检测作**优化提示**，切勿当成能否安全加载图片的唯一依据。[Find It 检测](<../../.research/mod-sources/FindIt-CSII/FindIt/Mod.cs#L24-L34>)；[AIL 启动任务](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Mod.cs#L27-L79>)。
2. AIL 将 `IconReplacerSystem` 注册在 `PrefabReferences`；在 Game/Editor preload 时启用，仅运行一次更新，若 `SetupTask` 未完成则排队主线程回调进行后续替换；背景任务错误可被捕获但任务仍会完成。真正的“该项 AIL 图候选就绪”信号是**源建筑当前 icon 指向 `coui://ail/…`**，而不是 `Task.IsCompleted`、文件目录存在或用户启用列表包含 AIL。`UIObject.m_Icon` 写入后，host 安装也通过 dispatcher 排队，因此“URI 已出现”仍不是 `<img>` 最终成功的保证。[AIL Mod 注册与任务](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Mod.cs#L27-L79>)；[替换时序](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L25-L69>)；[宿主注册](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L44-L64>)。
3. AIL 自己注册 `UIManager.defaultUISystem.AddHostLocation("ail", …)`，把 `.Thumbnails`、用户自定义及扩展模组目录提供给 `coui://ail/...`；**SSP 不应自行注册同名 host** 或自行依赖 `ModsData` 私有目录是否存在。AIL 未装/未运行、解包/注册未完成、素材被删、URI 失效都由 UI 图片 `onError` 保底；`coui://ail/` 字符串检查只代表“候选 URI 来源符合预期”，**不是成功探针**。[URI 生成与宿主](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L123-L163>)；[解包/版本路径](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconUnpackerUtil.cs#L12-L67>)。

**覆盖潜力的有界样本**：用本机 2026-10-16 的历史 `prefab-diag.json` 中 897 个 `_L1_`、`_L3_`、`_L5_` 原版 `BuildingPrefab` 名，与当前本机 AIL 已解包 `.Thumbnails` 根目录 + 每种风格目录中的 `.svg/.png/.jpg/.jpeg` 文件去扩展名按 AIL 两种键形式精确相交：`ColoredPropless`、`Colored`、`White` 均为 **669/897**（约 74.6%）。这只是**旧快照 × 本机当前图库文件名**的潜在匹配，不是当前 2077 条覆盖率，也不等于 AIL 已实际写回 source prefab 或 UI 成功显示；未命中者仍需分区图。资料来源为本机上述两个只读路径与 [AIL 目录枚举/键名](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L19-L40>)、[实际 prefab 匹配](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L110-L120>)。

### 二、为什么当前 `IconUrl` 无法解决 AIL 迟到

- 目录在 `GrowableVariantSystem.BuildEntry` 中对 `source` 调用 `SafeIcon`（内部是 `ImageSystem.GetIcon`），失败时用 ZonePrefab 图标，最终一次性保存 `IconUrl`／`IconJson`；目录建完只推一次完整 JSON。**源建筑稍后被 AIL 填入图标，不会自动更新这个序列化快照。**不能仅靠前端对旧 `item.ic` 的 `<img>` 重试：若旧值是分区 SVG，加载完全正常，不会触发 `onError`。[SSP BuildEntry](<../StaticSceneryPloppables/Systems/GrowableVariantSystem.cs#L297-L362>)；[SafeIcon](<../StaticSceneryPloppables/Systems/GrowableVariantSystem.cs#L622-L633>)；[一次性目录绑定](<../StaticSceneryPloppables/SceneryPloppableUISystem.cs#L36-L59>)；[IconUrl JSON](<../StaticSceneryPloppables/SceneryPloppableUISystem.cs#L123-L156>)。
- 原版建筑图来自**源 prefab**，不是剥离版 `SSP_S_*`：AIL 的索引只按 prefab 原名或 `prefab.GetType().Name + "." + 原名` 找键，且先 `handledPrefabs.Add` 再检查是否有匹配；SSP 的剥离版会复制 `UIObject`，在 icon 为空时还会写 `src.thumbnailUrl`。请求剥离版的 icon 可能得到无效 `thumbnail://` 或非源图，AIL 也不会自动把 `BuildingPrefab.原名` 的图片映射到 `SSP_S_*`。[AIL 匹配与“已处理”状态](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L89-L120>)；[SSP 克隆 icon 逻辑](<../StaticSceneryPloppables/Systems/GrowableVariantSystem.cs#L707-L736>)。

### 三、推荐的最小变更边界（设计，不是补丁）

1. C# 在建立 `CatalogEntry` 时只保存**原版 `BuildingPrefab source` 引用**（不加入对 AIL 类型的引用），UI 行仍使用稳定目录索引 `i`；通过一个单项请求/结果绑定，让 `SceneryPloppableUISystem` 在**面板可见**且**目录已完成**时用索引检查当前源建筑的 `ImageSystem.GetIcon(source)`。只允许 `coui://ail/` 前缀的 URI 作为 AIL 候选；若无匹配返回空，不拼 `thumbnail://`、不自行猜文件；索引越界、源引用失效、`GetIcon` 抛错均返回空。多次读取最多为可见格数，避免遍历 2077 prefab，也不触碰建筑 `m_Icon`。源码对照：[目录结构和源建筑入参](<../StaticSceneryPloppables/Systems/GrowableVariantSystem.cs#L34-L59>)；[当前单次 BuildEntry](<../StaticSceneryPloppables/Systems/GrowableVariantSystem.cs#L297-L362>)；[现有触发绑定范式](<../StaticSceneryPloppables/SceneryPloppableUISystem.cs#L29-L49>)。
2. UI 始终先给可见的 **2 行 × 5 条 = 10 张分区 SVG**，再向 C# 仅询问这 10 个目录索引；可选预取下一行 5 条，但**同时挂载的条目数最多 15**，且不得预热 2077。对候选 `coui://ail/` 图片至多每帧/每短间隔挂载 1 张（这是前端请求节流，不能承诺浏览器绘制或游戏主线程绝无开销）。候选出现后 `<img onLoad>` 才算可显示并可缓存成功 URI，`onError` 回退 `iconForZone(item.z)` 并对同一候选在该场景内限次退避，不持续刷错误日志。仅缓存成功项的 URI，失败结果设置有限过期时间，以允许 AIL 迟到后的自动恢复。[窗口大小和缓冲](<../ssp-ui/src/mods/ssp-panel.tsx#L97-L102>)；[当前窗口化行](<../ssp-ui/src/mods/ssp-panel.tsx#L380-L400>)；[现有失败回退](<../ssp-ui/src/mods/ssp-panel.tsx#L224-L257>)。
3. **优先级按“当前视窗 > 缓冲行”而非全表预热**；滚动/筛选/搜索切换立即弃用未发出的旧任务，进行中的加载可忽略结果但不要承诺可中断浏览器请求。响应校验目录索引 + 面板世代（重开、重新订阅后旧响应不能更新新页）；前端成功缓存要限定容量、窗口重开时可重用但应随游戏加载/目录更换清空。**不推荐前端直接构造 AIL 图路径**：匹配名单并非静态、用户/第三方可覆盖、样式不是固定。[AIL 自定义合并](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L64-L120>)；[AIL 键名规则](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L110-L120>)；[SSP 纯前端筛选](<../ssp-ui/src/mods/ssp-panel.tsx#L380-L399>)。
4. **迟到策略必须有界**：面板打开时若未拿到 AIL 候选，短时间定时重查**仅当下可见**的 10 条；失败保持分区图、对同一图片退避；面板关闭即停、重开再查；实测 AIL 极慢或更晚替换时，不能要求 SSP 在游戏未开面板时不停扫描。轮询间隔、总时长、失败重试次数与 UX 均为待游戏内验证参数，不能以本设计数字冒充已实测性能。UILoad 失败、host 未就绪而后恢复时也应允许重试（仅针对仍可见且有同一候选的图片）。[AIL 延迟主线程回调](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L43-L69>)；[SSP 面板挂载生命周期](<../ssp-ui/src/mods/ssp-panel.tsx#L274-L322>)。

### 四、部分覆盖、用户图、风格切换的准确语义

- AIL `GetAvailableIcons()` 先加载默认目录 + 当前 `IconsStyle` 的图片，再加载第三方 `.ail`、`ail`、`*.ail` 映射/模组 `GetIconsMap()`，最后合并用户 `CustomThumbnails`；自定义图优先于已列出的默认与第三方映射，亦受 host 目录配置影响。SSP **只读取 AIL 实际写到源建筑的 URI**，才能沿用这些可见的优先级与用户选择，而不绕过其规则。AIL `Settings.OverwriteIcons=false` 时原版 builtin icon 可能保留；此时找不到 `coui://ail/` 是正常回退，不该反射内部字典强取图片。[索引/覆盖顺序](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L19-L41>)；[用户图最后合并](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L82-L120>)；[扩展接口发现](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconAPIUtil.cs#L45-L64>)；[覆盖开关](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L140-L163>)。
- AIL 对没匹配的 prefab 仍加入 `handledPrefabs`，之后同对象再次遍历时通常跳过；没有公开的“所有图片就绪/图标刷新”事件供 SSP 消费。**可选集成无法保证后来添加的用户图或模组映射当场写回先前未命中的 prefab。**这不是 UI 多轮重试能修复的；至少在下次新对象/新会话恢复机制需要实测。[AIL `handledPrefabs` 和查键顺序](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L20-L22>)；[AIL 遍历条件](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/IconReplacerSystem.cs#L89-L120>)。
- AIL 样式默认 `ColoredPropless`，还可选 `White`、`Colored`；设置 UI 明示需要重启（当前 `IconsLoaded` 影响提示显示）。AIL 自身 `IconsLoaded=true` 时直接返回先前 cache，且已处理 prefab 也不会被反复改写，因此**当前会话改设置，不应宣称 SSP 立即切换了图片风格**：按“游戏重启后重新读取”设计，重开 SSP 面板只能重读当前 prefab 的实际 URI；旧 URI 及加载成功缓存随新游戏/图标世代清空。文档不建议硬编码样式目录或试图从 `Settings.IconsStyle` 自拼图片链接。[设置和样式列表](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Settings.cs#L29-L42>)；[重启提示](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Settings.cs#L119-L128>)；[样式项](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Settings.cs#L71-L98>)；[已加载缓存分支](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L44-L59>)。

## 必须保留的降级路径与验收矩阵

| 情况 | 建议用户可见结果 | 必须验证的条件 |
|---|---|---|
| 未安装/未启用 AIL | 从第一帧到关面板均为本模组自带分区 SVG，放置可用 | **没有**对 `AssetIconLibrary.dll` 的运行时强引用 / PDX `Dependency`；不注册 `ail` host；面板正常订阅 |
| AIL 晚于 SSP 目录完成 | 首屏 10 张分区 SVG 不阻塞；可见项后续逐项升级图片 | 只重查当前可见项、有限期限、响应世代校验；重开时再查 |
| 指定 prefab 无 AIL 图片 | 该项永久或暂时维持分区 SVG；其他有图条目不受影响 | 只接受源建筑真正的 `coui://ail/` URI，不用猜路径；图片加载失败回退并有限退避 |
| AIL host 不可用 / 删除图 / 定制图错误 | `onError` 恢复分区图；不反复刷加载请求 | 成功 URI 并不代表资源可读；成功/失败按不同缓存语义处理 |
| 用户使用自定义图片、第三方映射或关闭覆盖开关 | 尊重 AIL 最终写入的源建筑 icon 或回退 | 不读取内部缓存、不覆盖用户 `m_Icon`；不把未出现 AIL URI 当成异常 |
| 用户切换 AIL 样式 | 提示需要游戏重启；新会话显示新风格或分区图 | 不推测运行中重载已处理 prefab；切换存档、重开面板须避免缓存旧图 |

**已静态验证**当前本机 `Colossal.UI.dll` 中 `Colossal.UI.UIExtensions.ResolveUri`：仅对当前 `assetdb://<hostname>/` 做转换；其它非空 URI 直接原样返回。因此 `coui://ail/` 前缀由 `ImageSystem.GetIcon` 保留，按前缀识别有静态依据。**仍需运行时验证的风险：** Gameface 加载 AIL URI 到 `<img>` 是否可靠触发 `onLoad`/`onError`、AIL host 注册/替换先后时序、游戏切换存档时 `CatalogEntry`/前端模块级缓存生命周期及图标变更失效策略。这些不能由当前静态代码审阅代替游戏内验证；不能把“可选接入设计”说成已实现或保证 10 张图都存在。[游戏 GetIcon 解析 URI](<../../.research/treectl/gamefull/Game.UI/ImageSystem.cs#L60-L67>)；[AIL 宿主注册](<../../.research/mod-sources/AssetIconLibrary-CSII/AssetIconLibrary/Utilities/IconLibraryUtil.cs#L142-L163>)；[SSP 当前缓存](<../ssp-ui/src/mods/ssp-panel.tsx#L274-L322>)。

**本次变更范围**：只新增本设计文档；未改 C#/React 模组代码，未构建/部署或发布。是否提交以本项目 Git 记录为准。
