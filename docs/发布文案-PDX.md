# PDX 发布页面文案（待审阅）

> 用途：Paradox Mods 上传页面的填写内容。**本文件仅作文案准备，未执行任何发布操作。**
> 文件名中的「待审阅」标记请在上线后删除。

## 1. 基本信息

| 字段 | 值 |
|---|---|
| 名称 / Title | Static Scenery Ploppables |
| 版本 / Version | 0.1.0 |
| 适用游戏版本 | 1.6.* |
| 标签 / Tags | Code Mod、Tools |
| 外部链接 | https://github.com/LiangHaoXiang/StaticSceneryPloppables |
| 可见性 | Public（首发前可临时改 Private） |
| 缩略图 | `assets/publish/thumbnail.png`（512×512，已生成） |
| 截图 | **待补**：建议 3 张（面板全貌 / 筛选弹层 / 已摆放的成片造景） |

## 2. 一句话简介（Short description）

**中文**
> 把任意原版分区建筑变成无属性的静态造景件：连同地皮与随机装饰一起摆放，无居民、无岗位、无模拟开销。

**English**
> Plop any vanilla zoned building as a property-free scenery piece. Lot surfaces and randomized decorations (trees, benches, planters, bins) come with it, exactly like the game does it.

## 3. 详细描述（Long description）

### 中文版

**Static Scenery Ploppables**

把**任意原版分区建筑**变成**静态、无属性、可随处放置**的造景件——不受分区限制，也不产生任何模拟负担。

**它做什么**

- 在 Find It 式面板里浏览全部原版分区建筑（2000+ 条），支持搜索、类型页签、以及按密度 / 等级 / 面宽 / 进深筛选。
- 点击条目即可放置。建筑会连同**原版地皮**与其**随机装饰**一起出现：树木、长椅、花盆、垃圾箱、灯杆……
- 每次摆放都会用游戏自身的随机逻辑抽一份装饰方案，所以没有两处完全相同。
- 放置结果是一栋纯净外壳：**没有居民、没有岗位、没有住户与公司、不产生模拟开销**。

**典型用途**

- 在分区系统不允许的地方摆放建筑，做街区造景；
- 填充后巷、工业区、滨水杂项；
- 保留城市外观的同时，让地块脱离模拟系统。

**兼容与安全**

- 装饰在**放置那一刻**一次性生成，之后不会反复重建；缺失的装饰 prefab 会被跳过。
- **迁移**与**拆除**都会正确清理地皮。
- **卸载安全（实测）**：移除本模组后存档能正常载入、不会崩溃；已摆放的造景物会变成白模占位，用推土机拆除白模时其装饰会一并清除。建议卸载前先拆掉已摆放的造景物。
- 无前置依赖，可与 Find It / Move It / Anarchy / ctrlC 共存。

**语言支持**

面板文案跟随游戏语言：English、简体中文、繁體中文、Deutsch、Français、Español、Português (BR)、Русский、Polski、日本語、한국어；其余语言回退英文。

**说明**

- 源码与问题反馈：https://github.com/LiangHaoXiang/StaticSceneryPloppables

### English version

**Static Scenery Ploppables**

Turn **any** vanilla zoned building into a **static, property-free scenery piece** you can place anywhere — no zoning requirements, no simulation load.

**What it does**

- Browse all 2000+ vanilla zoned buildings in a Find It-style panel: search, zone tabs, and filters for density / level / width / depth.
- Click an entry and place it. The building comes **with its vanilla lot surface and randomized surroundings** — trees, benches, planters, bins, light poles.
- Every placement rolls its own decoration variant using the game's own randomization, so no two placements look identical.
- The result is a pure shell: **no residents, no jobs, no households, no companies, no simulation cost**.

**Typical uses**

- Decorate districts with buildings the zoning system would never allow.
- Fill back lots, alleys, industrial parks, waterfront clutter.
- Keep a city's look while freeing lots from simulation.

**Compatibility & safety**

- Decorations are created **once**, at placement time; missing decoration prefabs are skipped.
- The vanilla **relocate** tool and **bulldozing** clean up lot surfaces correctly.
- **Uninstall-safe (verified in game)**: the save still loads and does not crash after removing the mod. Placed scenery objects turn into missing-prefab placeholders (white models); bulldozing a placeholder removes its decorations along with it. Recommendation: bulldoze your placed scenery before uninstalling.
- No dependencies. Works alongside Find It, Move It, Anarchy and ctrlC.

**Localization**

Panel text follows your game language: English, 简体中文, 繁體中文, Deutsch, Français, Español, Português (BR), Русский, Polski, 日本語, 한국어. Other languages fall back to English.

**Notes**

- Source & issues: https://github.com/LiangHaoXiang/StaticSceneryPloppables

## 4. 更新日志（Change log）

**0.1.0**
- 首个公开版本：全量原版分区建筑目录、Find It 式面板（搜索 / 类型 / 密度 / 等级 / 尺寸筛选）、原版地皮与随机装饰、11 种语言界面。

## 5. 参考与致谢（References & credits）

本模组的实现参考了以下开源模组与社区文档（均为公开仓库，仅作机制与写法参考，未直接复制其代码）：

| 项目 | 作者 / 维护者 | 本模组参考了什么 | 链接 |
|---|---|---|---|
| **Find It** | JadHajjar | 面板交互范式（全量 prefab 列表、搜索/筛选、点击即放置）；`ToolSystem.ActivatePrefabTool(prefab)` 的放置链路 | https://github.com/JadHajjar/FindIt-CSII |
| **ctrlC** | jk1422 | 图章（AssetStamp）复制地皮的写法；工具栏入口按钮的注入方式与主题取用 | https://github.com/jk1422/ctrlC |
| **Move It** | Quboid | 工程结构与构建/部署布局约定（UI bundle 与 dll 同级部署） | https://github.com/Quboid/CS2-MoveIt |
| **Better Bulldozer** | yenyang | 「删除建筑自带子元素」的可靠做法：`DeleteInXFrames` 倒计时 + owner 仍在 `Updated` 时重置 + 经 barrier 加 `Deleted` | https://github.com/yenyang/BetterBulldozer |
| **Plop the Growables** | algernon-A | 关键结论来源：CS2 允许直接放置自长建筑（dev prefab menu / Find It），据此放弃图章、改走标准 Create 管线 | https://github.com/algernon-A/PlopTheGrowables |
| **cs2-modding-guide** | BrokeAssSoftware | 源验证手册：子元素删除规范（结构变更必须走 barrier 命令缓冲、`Deleted` 只是标记、`BatchesUpdated` 强制重渲染） | https://github.com/BrokeAssSoftware/cs2-modding-guide |
| **Cities Skylines 2 Modding Guide** | ps1ke | 游戏 API 速查（`Game.Areas.SubArea`、工具系统等） | https://ps1ke.github.io/Cities-Skylines-2-Modding-Guide/ |

> 说明：以上仅为「思路与写法参考」。本模组除游戏本身（Colossal Order / Paradox Interactive）外，不打包、不依赖任何第三方模组资源。

## 6. 发布前检查清单

- [ ] `PublishConfiguration.xml` 中 `ModId` 留空（首次发布由 PDX 回填）
- [ ] 缩略图已就位：`assets/publish/thumbnail.png`
- [ ] 截图补齐（3 张，建议 16:9，含面板与成片造景）
- [ ] `ModVersion` 与 `ChangeLog` 一致
- [ ] 中文 / 英文描述二选一或都贴（PDX 单语言字段，建议英文为主、中文附后）
- [ ] GitHub 仓库公开且 README 已更新
- [ ] 发布前最后跑一次：放置 → 迁移 → 拆除 → 存档 → 读档 → 卸载读档
- [ ] 无需发布时不要把 `AccessLevel` 误设为 Private 后忘记改回
