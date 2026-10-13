# 代码库优化路线（2026-10-13）

- 状态：**定稿**——二次核对已完成（4 个只读代理对 P1 修复点调用链、5 个拆分刀口、App 拆分与退出僵尸修复的重叠、设置窗前置项做了逐符号核对），核对结论已并入正文。
- 依据：
  - 三方架构评审事实核查：`artifacts/report-verification-20261013/报告.md`
  - 设置窗 UserControl 化说明核查：`artifacts/report-verification-20261013/设置窗核查.md`
  - 本文只收录经对码证实的结论；被证伪的原始断言不进方案（存档见文末）。
- 代码基准：main @ fc9c2f49 工作树 + 并行会话未提交改动（App.xaml.cs 现 **5,894 行**、SettingsWindow.xaml 现 **3,839 行**，MaxWidth 修改在 L1282）。**所有行号为 2026-10-13 工作树口径，落地时按符号定位。**

## 0. 治理约束（先于一切批次）

1. **架构冻结点五问**（module-boundary-roadmap-20260918.md:323-333）：架构性改动须在 PR 描述回答五问。D、E 批次附答；A、B、C 不动架构。
2. **体积只作观察指标**（roadmap:156）：全部验收语义化。
3. **契约测试同步义务**：凡移动被 SliceBetween / Contains / 计数钉住的代码，同批同步改测试（钉清单已逐刀枚举，见批次 D）。
4. **回归基线**：每批 `dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64` 全量绿（2026-10-13 = 5,394）。
5. **AOT 面**：不动 XAML 的 partial 拆分不触 WMC1510（864）；改 `publish-aot-audit.ps1` 的 stage 文件列表时按仓库惯例同步升 auditProfileVersion（`AotStage4E3ContractTests` 钉了 59/55，改列表会被测试强制升级）。

## 批次总览

| 批次 | 内容 | 优先级 | 预估规模 | 风险 | 建议窗口 |
|---|---|---|---|---|---|
| A | 4 站点 async void 兜底（Glance 侧统一 12 个 handler）+ 3 处吞异常定案处置 | **P1** | 半天–1 天 | 低 | 1.5.7 首批 |
| B | TODO/HACK/FIXME 卫生契约测试 | P3 | ~半小时 | 极低 | 随 A 同批 |
| C | 对外内存口径修正（文章/官网，非代码） | P2 | 文案 | 无 | 下一篇文章前 |
| D | 巨型文件选择性拆分（3 文件、7 刀，钉清单已枚举） | P3 | 每刀半天–1 天 | 中 | 1.5.7+ 机会性 |
| E | 设置窗渐进收敛（三规则 + 前置已裁定：资源上移目标=公共字典、成色标准=三档制） | 长期 | 机会性 | 中 | 立即生效为规则 |

---

## 批次 A：异步异常兜底与吞异常处置（P1）——已核对定案

**核对结论**：4 个站点全部仍在（并行会话的退出僵尸修复只动关机序列 L5228-5445，未触碰它们）；修法素材已确认。

### A1. async void 兜底

| # | 站点（当前行号） | 调用链与抛出面（已核对） | 修法（定案） |
|---|---|---|---|
| 1 | `App.ExitApplication`（App.xaml.cs:5225-5229） | 托盘退出 → Action 重载裸调用；`ShutdownCoreAsync` 整体已有 catch，但 `Exit()` 的 COM、backstop 武装点之后的口子仍可逃逸。**僵尸修复的硬杀兜底保证异常时 ≤5s 必死**，本站点的 catch 价值=死前留下带上下文的日志（互补不重复） | 体内 try/catch，只 `App.Log`，别无动作 |
| 2 | `SearchPopupWindow.ShowPopup`（:217）/ `ShowPopupWithQuery`（:225） | 热键（SearchHotkeyService→ToggleSearchPopupAsync）/ 小组件点击汇入同一 `ShowPopupCoreAsync`（:230-369）；可抛=WinRT COM（storyboard/AppWindow/DisplayArea/材质）+ 托管（OnPopupOpenedAsync→LoadRecommendationsAsync 的应用枚举/文件扫描/图标提取）。当前最好结局=泛化日志+弹窗半开，最坏=进程崩溃 | **单点 catch 覆盖双入口**（包 ShowPopupCoreAsync 或两入口），`App.Log` 带 `[Search]` tag（同文件 :398 先例） |
| 3 | `GlanceWidgetSettingsSection.ToggleDisplayOption`（:723-754） | 先改内存后 await `store.SaveAsync`（GlanceWidgetStore→ResilientJsonStore，**确认可抛**：CreateDirectory/WriteAllText/File.Replace 过滤器外 IOException/就地校验 throw）。失败后果=UI 已显示新勾选但 widget/磁盘不更新，重启回跳。**核对新发现：同文件约 12 个同模式裸 await handler**（LayoutComboBox:756、CalendarMaterial:781、TraditionalCalendar:790、FestivalsToggle:799、BackgroundSource:802、OnlineImageCategory:811、Rotation:819、ImageTransparencySaveTimer_Tick:866-873…），且 ToggleDisplayOption 绕过了私有 `SaveAsync`(:509-519) 的 `_isLoading` 守卫 | **在私有 `SaveAsync` 加 catch 统一覆盖 12 个 handler**（一行日志 `[GlanceWidgetStore]` tag，GlanceWidgetStore.cs:202 先例）；ToggleDisplayOption 改走私有包装，顺带消除守卫绕过 |

**统一素材**：`App.SafeFireAndFororget`（App.xaml.cs:788-798，public static，catch→Log+CallerMemberName）4 站点全部可直接用（跨文件先例 ContentWidgetWindow.Commands.cs:969-973）；`QuickCaptureSettingsViewModel.RunUserAction` 是 private+VM 绑定**不可照抄**。附带发现：SearchPopupWindow.xaml.cs:328 的 `_ = ShowAppsSkeletonAfterDelayAsync(...)` 是丢弃 task（unobserved），本批范围外、记档。

### A2. 三处吞异常定案

| # | 站点 | 定案（已核对） |
|---|---|---|
| 1 | App.xaml.cs:769-771（日志入队自吞） | **接受现状**。可抛源仅 OOM；4096 行上限丢弃是有意背压设计；垂死进程发旁路无诊断价值 |
| 2 | App.xaml.cs:837-839（日志文件写自吞） | **补 OutputDebugStringW 旁路**。当前失败=该批日志永久丢失（backstop 里的 DrainLogQueue :5242 同路径，关机尾部日志同样丢）。全库无 OutputDebugString 先例，需新 P/Invoke（`[LibraryImport("kernel32", EntryPoint="OutputDebugStringW", StringMarshalling=Utf16)]`，AOT 兼容）。防刷屏：`static bool s_logFileWriteFailedSinceLastSuccess` 回合制开关，仅 false→true 跳变时发一行（ex.Message+LogPath+丢弃字符数），AppendAllText 成功后复位 |
| 3 | NativeNotificationActivationEnvelopeStore.cs TryRestoreClaim（File.Move :505，catch :508-510） | **补一条 App.Log 收案**（该文件其他失败路径都经 TakeResult.Message 上报，唯此处全静默；无锁入队在 `_takeGate` 内安全）。兜底已核实完整：启动即扫+每次激活再扫+PID 复用 StartTime 守卫+first-wins，重启必恢复。`TryRecoverAbandonedClaim` 的 catch（:584-587）已有收案注释，**勿动** |

**验收**：4 站点异常路径有日志；Glance 12 handler 统一覆盖；全量测试绿；无对外行为变化。
**五问**：不适用（非架构改动）。

---

## 批次 B：源码卫生契约测试（P3）——已核对定案

- **现状**：TODO/HACK/FIXME 全库 0 但无测试钉住（碰巧为零）。
- **定案**：`EnumerateProductSources()` 是 private static（AotInteropShapeContractTests.cs:153-170），新测试文件不能直接复用——**最省方案：把 `[Fact]` 直接加进 `AotInteropShapeContractTests` 类内**（该类本就是"源码形状合同"的家，零管道）。断言 `\b(TODO|HACK|FIXME)\b` 词边界零命中（词边界已证不误伤 `DESKBOX_AOT_TODO_*`）。
- **验收**：绿；本地注入 `// TODO` 验证变红后移除。

---

## 批次 C：对外内存口径修正（P2，非代码）

- **事实**：仓库与商店文案从未出现"50MB"；实测稳态距 50MB 很远。

| 口径 | 数字 | 出处 |
|---|---|---|
| AOT 零售空闲稳态 Private | ~115–131MB | CHANGELOG.md:526（1.4.9）；deskbox-aot-curve-samples.csv |
| AOT 零售空闲稳态 WS | ~171–208MB | 同上 |
| 大批量后 idle 平台 | ~490MB（private=commit，托管堆 ~35MB） | docs/releases/v1.5.4.md:43 |
| 隐藏/空闲裁剪后 WS | <10MB | CHANGELOG.md:519；residency 文档 6.7MB |

- **建议表述**："空闲稳态私有内存 ~120MB；隐藏空闲后工作集可换出至 10MB 以下；托管堆全程 ~35MB"。framework JIT 数字（Private 160–250MB）不对外。Simon 执行。

---

## 批次 D：巨型文件选择性拆分（P3，机会性）——刀口已核对

**总原则**：每文件 2–3 刀即止、partial 物理拆分（XAML 零改动已核实：处理器按名绑定到 partial 类，落任一文件均可；WMC1510=864 不动）、每刀同批同步钉、语义验收。

### D1. SearchPopupWindow.xaml.cs（4,318 行）——第一优先

| 刀 | 修正后边界 | 内容 | 独占字段 | 钉同步 |
|---|---|---|---|---|
| 1 | **L836–1222**（11 方法；1224 起是 TryApplyCustomBounds 的 doc 注释不可带入） | 材质控制器 → `SearchPopupWindow.Backdrop.cs` | `_micaController` 等 5 个 | `SearchPopupMaterialContractTests.cs:45-57` 改读取路径（另 3 个测试调静态方法不用改） |
| 2 | **两段不连续：L1573–1676 + L1983–2464**（原方案连续区间错误；搜索框/结果面板逻辑 L1678-1748 不随迁，`ResolveThemeBrush` 留原文件） | 推荐应用+图标+骨架屏+入场动画 → `RecommendedApps.cs`（两段合并进同一 partial） | `_pendingEntranceStyle` 1 个 | `AotStage4E3ContractTests.cs:120-129` 改拼接读；**`publish-aot-audit.ps1` L1400/1404 两个 pattern 改指向新文件 + 升 auditProfileVersion** |
| 3 | **L3226–3493**（15 方法；L3495 起批量操作菜单不随迁） | 橡皮筋多选 → `RubberBand.cs` | `_rubberBandStart` 等 4 个 | `SearchPopupVisualContractTests.cs:152` 改读取路径 |

跨刀公共项：`SettingsSliceOwnershipContractTests` FacadeAccessManifest 需为新文件加条目（Backdrop=7、RecommendedApps=1），可选收紧 SPW 22→14；AotSmoke 桩只引用共享字段（留原文件），零阻碍；ModuleBoundary manifest 不变（块内无文件操作）。

### D2. WidgetShell.xaml.cs（5,750 行）——第二优先

| 刀 | 修正后边界 | 内容 | 独占字段 | 钉同步 |
|---|---|---|---|---|
| 1 | **L2381–4176**（57 方法+嵌套类 `CompactParticleAnimationState` 随迁；收入尾部 `CollapsedChromeLayer_SizeChanged`） | 紧凑呈现渲染 → `WidgetShell.CompactPresentation.cs` | 21 个（粒子/marquee/vinyl/边缘光晕族） | `WidgetVisualActivityContractTests` 3 个测试、`GlanceWidgetPhotoControlsTests.cs:174-176`、`MusicWidgetContentLayoutTests.cs:252-253` 改读取路径；Facade manifest 加 `CompactPresentation.cs`=2 |
| 2 | **L1145–1699**（22 方法+1 属性；L1701 起 responsive 布局转场不随迁） | 内容转场 → `ContentTransition.cs` | 4 个（`_transition*`/`_interactiveNeighbor*`） | `WidgetContentViewportClippingContractTests` 整测试改读新文件（顺序断言因相对顺序不变而免改）、`MarkdownAndSplitterContractTests.cs:327-336/471-479` 改读取路径 |

外部 API 面（跨类调用 `SetContent`/`BeginContentTransition`/`SetCompactPresentation` 等）全 public 不受影响；`WidgetShell.xaml.cs` 在全部 scripts/*.ps1 零出现，无 audit 脚本耦合。

### D3. App.xaml.cs（5,894 行）——第三优先，分批

| 刀 | 修正后边界/调整 | 钉同步 |
|---|---|---|
| 1 | 静态诊断块 **L437-716 照做**（11 方法全 static 零实例状态已证实；自带 5 个 using；仅 2 个调用方留原文件；ElevationNotice/AotSmoke/诊断服务均不引用） | 仅 1 钉：`ExternalActivationPolicyTests.cs:88` 改拼接读 |
| 2 | 内存清理机器 **起点修正为 L3869**（含 4 个静态字段+`MemoryCleanupEpoch` 属性+事件，原 3888 会孤儿化字段）、终点 L5211；**不合入** `App.QuiescenceWorkingSetTrim.cs`（160 行独立单元，反向依赖本块）；独占字段 7+4 静态 | **8 处钉**：`WidgetVisualActivityContractTests` 6 个切片+**GC 白名单必须扩到新文件**（按路径白名单，不扩=出现旁路）；`PerformanceSettingsContractTests.cs:96-140` 拼接读；Facade manifest App.xaml.cs 28→25、新文件=3 |
| 3 | OnLaunched **1.5.7 不动**。核对发现：启动管线首阶段**已落地**（StartupPipeline.cs 208 行+App.Startup.cs 238 行+故障注入测试），OnLaunched 已步化（5 critical+39 optional+14 smoke 簇）；roadmap 自己欠两项验收（僵尸路径实跑复测、启动计时无回退），先补 | —（拆它会碎 14+ 个 AotStage 顺序钉） |
| 4 | Todo/通知路由：**真块=L2199-2854（~656 行）**（原区间头 L2105-2198 是 QuickCapture+lease 装配，被独立切片钉住，排除）；**不合入 TodoReminderRuntime**（只管生命周期零通知感知）；激活信封路由 L2951-3190 同域在原区间外。**调整为两步：先便宜地整块搬 `App.Notifications.cs` partial（同刀 1/2 口径），Coordinator 化单独立项排后**（非 partial 抽取要拆 4 个 AotSmoke 桩私有直调+2 个 partial void 钩子+ModuleBoundary File.Delete manifest 3→2+1，成本远超刀 1/2） | 搬 partial 时：≥6 个测试文件改读取路径；smoke 桩是 partial 零改动 |

**落地顺序（已核对裁定）**：僵尸修复提交（已在工作树）→ 刀 1 → 刀 2 →（可选）刀 4 partial 搬家；刀 2 与僵尸修复区域相邻零重叠（L5211/L5228 接缝），但 `DelayedCleanup` 切片终点标记正是接缝处，先提交再做刀 2 可一次完成测试同步。

### D4. 明确不拆

- `FileSurfaceContent.xaml.cs`：已拆 16 个职责 partial，剩余是受 `[重要勿删]file_drag_stack_contract.md`+SliceBetween 顺序钉保护的 DragDrop 状态机本体。
- `WidgetWindowBase.Collapse.cs` 核心状态机：时序敏感，只在被碰到时顺手拆外围（展开预热 ~470 行、19 个事件转发器 ~750 行）。

**五问（D 批次统一作答）**：① ownership：渲染/诊断/清理/通知职责获得独立文件归属；② 边界：文件边界=职责边界（partial 不断耦，定位与并行冲突面改善）；③ 依赖方向不变；④ 既有测试全部保留并同步钉；⑤ 删旧职责：旧文件行数下降、无新增层。

**两文件合计测试同步工作量（已逐钉枚举）**：10 处（8 个测试文件+1 个 Facade manifest+1 个 audit 脚本）。

---

## 批次 E：设置窗渐进收敛（长期规则）——前置已裁定

**终态**：每分区=自包含 UserControl，窗口退化为按需创建+可见性切换的薄路由壳。**不做一次性迁移**。

### 三条规则（立即生效）

1. **新分区一律自包含 UserControl**（roadmap:102 既定），写法照 `SearchSettingsSection`。
2. **改到哪迁到哪**。
3. **契约测试随迁分区化**；搜索目录生成器本就递归 SettingsSections（`update-settings-search-catalog.ps1:20-28`）。

### 前置 1：资源上移（已裁定：进 `Styles/SettingsOverviewResources.xaml`，不进 App.xaml）

- **理由**：5 个独有 key 的消费集精确等于"设置面"（窗口+分区都已合并公共字典，零额外接线）；仓内已有跨字典 BasedOn 先例（公共字典 L83 `AccentActionButtonStyle BasedOn AccentButtonStyle`）；App.xaml 方案会进程启动即常驻设置样式+命名空间污染，无增益。
- **做法**：5 个 key（两转换器+三样式）上移进公共字典，**同批删除**窗口 Grid.Resources 的 5 条本地定义（L45/46/81-86/88-92/94-100）与分区副本（AppearanceSettingsSection L15 **零引用纯冗余**/L16、CapsuleModeSettingsSection L15/L17-23/L24-29）。公共字典根需新增 `xmlns:controls="using:DeskBox.Controls"`；新条目放文件尾（`SliderSettingValueTextStyle` BasedOn 的 `SettingValueTextStyle` 在 L51-54，同文件前向引用 parse 期会炸）。
- **陷阱（必须同批完成的原因）**：字典直接条目优先于 MergedDictionaries，残留本地副本会静默遮蔽公共字典。
- **核对附赠发现**：窗口 Grid.Resources 另有 **4 个死样式零引用**（`SettingRowStyle` L61-67、`SettingTextPanelStyle` L69-72、`SettingDividerStyle` L74-79、`CompactTextActionButtonStyle` L102-110）可一并删（账面 18 key 实为 ~13 活）；Appearance 分区 `Localization` 属性（:11）是死代码。

### 前置 2：分区成色标准（已裁定：三档制，非深浅二分）

- **底线禁止项（所有分区必须过，来自 Glance 反例归纳）**：① 禁 `App.Current.*` 服务定位器（Glance 13 处）；② 禁视图内 new 服务/静态数据工厂（Glance :26-28 三个）；③ 禁全局 `SettingsChanged` 广播当分区刷新源。
- **全套 Search 机制（8 条 checklist，仅对有异步/跨分区状态/文件选择器/后台刷新的分区强制）**：Configure(editorVM, localization, ownerHwnd) 注入（SearchSettingsSection.xaml.cs:41-47）｜构造只挂生命周期钩子（:29-37）｜SetActive×IsLoaded 双开关同步（:49-74）｜VM push+_isLoading 防回环（:123-170）｜RefreshFromSettings 可见刷新（:116-121）｜VisitToken+HWND+XamlRoot 纪律（:358-371/:210）｜LoadContent 主题补丁自挂 ActualThemeChanged（:93-103）｜薄模板包装+ProcessBindings 挂载（DeferredSections.cs:57-99）。
- **纯绑定档是合法终态**（核对新发现）：Appearance/CapsuleMode/FileWidget 走 DataContext/x:Bind DP 直绑，code-behind 极薄——这类分区不必套全套 Search 机制，避免过度工程。注意 CapsuleMode 的 ViewModel DP 是 x:Bind 锚点（XAML:64），与 DataContext 双通道并存是**有意设计**，去重时勿当冗余删。
- **存量不回改**：Glance/DesktopOrganization/Display 的 App.Current 直连（各 ~13/25/20 处）只在被碰到时收敛。

### 前置 3：InfoBar hack 不计收益（维持原裁定）

severity 画刷/字号问题与 `DataTemplate.LoadContent` 正交，迁移不消失；如治理是独立小线（static helper 挪出窗口类，消除 SearchSettingsSection:88-102 反向耦合）。

**五问（E 批次作答）**：① 分区视图归属从窗口壳明确到分区自身；② 窗口=路由、分区=自描述；③ 分区→编辑器 VM（既有），不再→窗口；④ 全局计数锁降级为分区级锁；⑤ 每迁一区删一份挂载特判与 SectionElements 属性。

---

## 明确不做清单（防重开）

| 项 | 理由 |
|---|---|
| 空 catch 全量治理（225 处） | 逐处分类 B 类=0；A 类 134 处为取消/拆卸/探测正当惯用法 |
| GC.Collect 治理 | 产品代码仅 2 处（MemoryReclaimer，带冷却），契约测试白名单管制 |
| Services 目录专项搬家（371 文件扁平） | 按 module-boundary-roadmap 既定管线走，不单独开工 |
| FileSurfaceContent DragDrop 本体拆分 | 合同文档+顺序钉保护 |
| Collapse 核心状态机拆分 | 时序敏感，拆=物理切块 |
| 设置窗一次性全迁 | 热区双路径成本；progress:52 明文"保持现有 DataTemplate 按需创建机制" |
| OnLaunched 结构化改造（1.5.7） | 管线首阶段已落地；roadmap 欠两项验收先补 |
| 测试计数对外用 3,982 | 静态方法数；实际执行 5,394（Theory 展开） |

## 附：被证伪不采纳的原始建议（存档）

- 三方报告"139 空 catch 是稳定性根因" → 实测 225 处 B 类=0。
- 三方报告"9 个 GC.Collect" → 实测 2 处。
- 设置窗说明"已迁一半/存在 re-mount/5387 全绿上线/P1 系忘登记" → 分别为 7/33、无 re-mount、批内即修未上线、根因系 AOT 裁反射根。
