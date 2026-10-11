# DeskBox x Todo清单集成：开发智能体主提示词

你正在 `F:\project\20261011\DeskBox_Personalized` 中开发 DeskBox 的 Todo清单 3.14.2 集成功能。请先完整阅读仓库根目录 `AGENTS.md`，然后阅读 `Agent/skills/deskbox-todo-qingdan/SKILL.md` 及其按阶段要求的 references。不要只写计划；在完成必要调查后，按阶段实现、测试、运行并验证。

## 目标

在不改变 DeskBox 现有待办窗格视觉风格、窗口行为和通用交互的前提下，为 `WidgetKind.Todo` 增加一个可选的“Todo清单”数据后端。用户继续通过原有“功能格子 > 待办”入口启用待办格子，并可在待办设置中选择数据来源：

- `DeskBox 本地待办`：保持当前行为和磁盘格式完全兼容。
- `Todo清单`：通过隔离的桥接层读取 Todo清单 3.14.2 数据，并仅开放经验证能保持 Todo清单原有业务语义的操作。

Todo清单负责其任务业务、云同步、重复任务生成、提醒、附件、番茄和统计；DeskBox 负责格子窗口、胶囊、布局、主题、筛选展示和快速操作。无法可靠映射或写回的功能必须从 Todo清单模式的 UI 中隐藏或禁用，并提供明确状态，不能模拟一个语义不同的实现。

## 已知环境

- Todo清单版本：`3.14.2`
- 可执行文件：`E:\todolist\todo-list\Todo清单.exe`
- 用户数据目录：`%APPDATA%\todo-list`
- 可见数据库：`task.db`、`todos.db`，其中 `todos.db` 使用 WAL。
- 应用为 Electron，多进程运行，包含 `better-sqlite3-multiple-ciphers`。
- 尚未发现官方公开 API、localhost API 或 Todo清单专属 URI Scheme。
- 这些路径只是当前机器的探测结果，生产代码必须发现路径，不能硬编码。

## 不可违反的安全边界

1. 默认只读研究真实 Todo清单数据。未经用户单独、明确授权，不得修改、替换、迁移、解密或复制真实 `task.db`、`todos.db`、WAL、SHM、凭据、会话和云同步数据。
2. 架构和 schema 探测不得读取、记录或提交任务标题、备注、附件路径、账户资料、令牌、Cookie 或数据库密钥。优先只读取版本、表名、列名、索引、触发器、PRAGMA 与结构哈希。
3. 自动化写入测试只能使用脱敏夹具、隔离副本、测试账号或用户明确指定的测试数据根；禁止拿生产数据库跑 CRUD 测试。
4. 不得让 DeskBox 主进程直接加载 Todo清单附带的原生 Node/SQLite 模块。第三方格式和私有协议必须封装在独立桥接进程或同等故障隔离边界后。
5. 不得在“结构未知、版本未知、同步语义未知、Todo清单仍持有不兼容缓存”时直接写数据库。任何写能力必须由显式 capability gate、schema fingerprint 和验证证据共同放行。
6. 不得逆向绕过付费、认证、加密或访问控制。只处理当前用户合法拥有并授权访问的数据。
7. 日志、诊断包、测试失败输出和 Git 历史不得包含用户任务正文或秘密。

## 产品行为

### 功能开关

保留现有 `TodoEnabled` 作为“是否启用待办功能格子”的总开关。在现有 Todo 设置区增加数据来源选择，不创建第二种 WidgetKind，也不创建第二个独立功能格子入口：

```text
待办数据来源
  DeskBox 本地待办
  Todo清单
```

Todo清单模式下显示必要的紧凑设置：连接状态、检测到的版本、目标清单、立即刷新、重新检测兼容性、在 Todo清单中打开、切回本地待办。使用现有设置页的控件、间距、字体和反馈模式。

切换数据来源绝不能删除、覆盖或隐式合并任一侧数据。DeskBox 原生 `todo.json` 保留原样。切回本地后端后应立即恢复原有数据。

### 能力驱动 UI

桥接层返回能力集合，而不是前端根据版本字符串猜功能。至少区分：

- `ReadTasks`
- `CreateTask`
- `UpdateTitle`
- `SetCompleted`
- `DeleteTask`
- `SetDueDate`
- `SetImportant`
- `ReadLists`
- `MoveToList`
- `Reorder`
- `ReadSteps` / `WriteSteps`
- `ReadNotes` / `WriteNotes`
- `ReadAttachments`
- `OpenNativeTask`

Todo清单模式中，只显示已验证能力对应的命令。重复规则、提醒、附件修改、番茄和统计默认不由 DeskBox 实现。DeskBox 自有提醒扫描和重复任务生成在 Todo清单模式下必须停用，避免双提醒和重复创建。

### 降级

降级顺序必须是：

```text
经验证读写 -> 只读实时数据 -> 最后成功快照 -> 明确的不可用状态
```

不得在连接失败时偷偷切换到 DeskBox 本地待办，因为那会向用户展示另一组任务。版本或 schema 不兼容时自动禁止写入，并解释“任务未被修改”。

## 建议架构

目标边界如下，名称可按仓库惯例微调，但职责不可混淆：

```text
TodoWidgetContent.xaml                  保持现有视觉与宿主行为
TodoWidgetViewModel                     依赖抽象 repository/session
ITodoRepository                         DeskBox 内部稳定契约
DeskBoxTodoRepository                   包装现有 TodoWidgetStore
TodoQingdanRepository                   IPC 客户端、缓存和能力门禁
DeskBox.TodoQingdanBridge               独立进程
  Protocol                              版本化、source-generated JSON
  Discovery                             安装/数据目录发现
  Compatibility                         版本与 schema 指纹
  Adapters/TodoQingdan_3_14_2           私有格式适配器
  UnsupportedAdapter                    只读/拒绝写入
```

桥接进程优先通过命名管道与 DeskBox 通信。协议必须包含 `protocolVersion`、`requestId`、操作名、超时/取消语义和结构化错误码。不要把异常堆栈、数据库路径或任务正文写进普通日志。

Repository 的第一步是对现有 Store 做行为零变化的抽象。不要一开始重写全部 ViewModel 操作为远程 CRUD。当前 ViewModel 按整份 `TodoWidgetData` 保存；在 Repository 内部可使用“上次快照与新快照 diff -> 外部 mutation”过渡，但必须串行化写入、支持失败回滚，并防止旧快照覆盖新远端版本。

所有 Todo 调用方必须最终共享同一 repository/factory，包括：窗口、通知动作、搜索索引、搜索结果动作、格子删除/重置和相关运行时。备份恢复应继续支持 DeskBox 原生 Todo；Todo清单模式只备份集成设置、稳定 ID 映射、非敏感缓存元数据和待提交操作，不宣称备份 Todo清单云数据。

## 设置与持久化约束

`AppSettings` 是冻结的磁盘协议。先阅读 `docs/architecture/current_architecture.md` 和 `docs/architecture/json-source-generation-baseline.md`。优先考虑在 Todo slice 中以兼容默认值增加最少字段并同步既有 passthrough/迁移/契约；如果这会违反当前冻结判定，则使用独立、版本化的集成设置文档，并在架构文档中记录决策。不能随意删改已有字段。

所有新增 JSON 必须使用独立的 source-generated `JsonSerializerContext`，显式固定命名、枚举、大小写和未知字段行为。Native AOT 路径不得依赖反射序列化、动态代码、运行时生成代理或动态程序集插件。

## 分阶段执行

严格按可独立验证的阶段推进。每阶段完成后更新 `Agent/IMPLEMENTATION_STATUS.md`，记录证据、未决风险和下一步；不要提前宣称未验证能力可用。

### Phase 0：安全探测和决策

- 阅读现有 Todo 架构、测试、提醒、搜索和设置边界。
- 建立非敏感的 Todo清单安装发现和 schema fingerprint 探针。
- 只读取结构元数据，确认数据库是否加密、关键 schema 是否可访问、Todo清单运行时的只读并发行为。
- 检查 app.asar 中是否存在可合法调用的 IPC/内部服务边界，但不要提交反编译后的大段第三方源码。
- 写出 capability 证据和写入停止条件。

Phase 0 未证明安全写入通道前，后续只实现只读。

### Phase 1：零行为变化的 Repository 抽象

- 提取内部 Todo repository/session 契约。
- 让现有本地 Store 适配该契约。
- 窗口、提醒、搜索等切换到统一 factory，但默认仍使用本地实现。
- 全部既有 Todo 测试必须通过；原 `todo.json` 字节/语义兼容。

### Phase 2：桥接协议与只读模式

- 新建独立 bridge 项目并加入 solution/build/publish 的正确位置。
- 实现协议握手、发现、版本/指纹、能力报告、读取清单和任务。
- 在 DeskBox 中加入 Todo清单 repository、超时、取消、进程崩溃隔离和最后成功快照。
- Todo清单不可用或升级不兼容时，UI 保持可理解且不展示另一套数据。

### Phase 3：设置和能力驱动前端

- 在现有 Todo 设置 editor/coordinator 中增加数据来源和连接状态。
- 保持现有 Todo 窗口 XAML 的视觉基线；仅按 capability 隐藏或禁用不支持的命令。
- 保持胶囊、分组、响应式布局、主题和键盘导航。
- Todo清单模式停用 DeskBox 自有提醒和重复生成。

### Phase 4：最小安全写入

仅对通过隔离测试和同步验证的操作逐项放行，推荐顺序：

1. 完成/取消完成
2. 新建
3. 修改标题
4. 截止日期/重要级别
5. 删除
6. 子任务、备注、分类、排序

每项都要有：版本 gate、schema gate、乐观更新、失败回滚、并发冲突策略、Todo清单 UI 可见验证、云同步后不回滚的证据。没有证据就保持 capability=false。

### Phase 5：升级兼容工具

- 增加 `scripts/Test-TodoQingdanCompatibility.ps1` 或等价脚本。
- 自动输出版本、路径、schema 指纹、adapter、能力和风险，不输出任务内容。
- adapter 以版本/结构组合选择；相同 schema 的小版本可通过兼容清单复用。
- 未知指纹默认只读。
- 用脱敏 fixture 固化 3.14.2 结构和协议测试。

### Phase 6：完整验证和交付

- 运行定向测试、x64 全量测试、Debug 构建。
- 遵循根 `AGENTS.md`：停止仓库内 DeskBox、构建、从规范 Debug 路径重启，并核验唯一运行实例路径。
- 根据改动范围运行 AOT/发布契约；不得通过放宽门禁来过测试。
- 人工验证真实 UI，但涉及真实 Todo清单写入前必须再次获得用户明确授权，并先说明将执行的精确操作和恢复方式。

## 质量门槛

- 原生 DeskBox 待办模式行为、数据、备份和 AOT 证据不得回归。
- Todo清单集成关闭时，不启动 bridge、不加载第三方组件、不增加常驻轮询。
- bridge 崩溃、超时、升级或格式未知不能拖垮 DeskBox。
- 用户操作必须串行化或有明确并发控制，不能整份旧快照覆盖更新的数据。
- UI 主线程不执行数据库、IPC 或文件监视阻塞操作。
- 所有后台生命周期由现有 `IFeatureRuntime`/关闭序列模式管理。
- 不支持的能力从 UI 中移除或解释，不得静默丢字段。
- 12 份本地化资源保持键和占位符一致。
- 日志和诊断包经过隐私审计。
- 新代码遵循项目现有 partial、Coordinator、Provider、Platform 和测试模式，不做无关重构。

## 必须覆盖的测试

- Repository 抽象保持原有 Todo 行为。
- 数据来源切换不删除或合并数据。
- Todo清单模式的能力驱动 UI。
- bridge 握手、协议版本、超时、取消、重启和崩溃。
- 未知 Todo清单版本/schema 自动只读。
- 快照缓存不覆盖新数据，缓存损坏可恢复。
- 写入失败 UI 回滚，重试不会重复创建。
- Todo清单模式不启动 DeskBox 提醒、不生成下一重复任务。
- 搜索与窗口使用同一数据来源。
- 删除格子不会删除 Todo清单账户数据。
- JSON source generation 与 Native AOT 契约。
- 日志不包含 fixture 中的敏感哨兵文本。
- x64 主路径；ARM64 若 bridge 暂不支持，要显式 capability/平台降级，不能崩溃。

## 工具与协作

- 文件搜索优先 `rg` / `rg --files`。
- 代码编辑保持小批次，每批均可构建和测试。
- 可使用 Git 检查历史和差异，但保留工作区中用户已有的 `.gitignore` 等修改。
- 不执行破坏性 Git 或文件命令。
- 不提交 assistant/tool attribution；遵守仓库提交身份规则。
- 互联网搜索仅用于核实 Todo清单公开信息；不要把搜索结果当成私有协议证据。
- 若可用，使用独立子智能体做只读代码审查或测试矩阵审查；禁止子智能体接触真实任务正文和秘密。

## 交付格式

每个阶段完成时报告：

1. 已实现行为。
2. 修改的关键边界。
3. 实际运行的验证命令与结果。
4. 当前 capability 矩阵。
5. 对真实 Todo清单数据是否执行过任何读取/写入；读取了什么类别，是否包含正文。
6. 已知风险、主动禁用的能力和下一阶段停止条件。
7. 重启后的 DeskBox 可执行文件绝对路径。

最终交付必须包含升级说明：Todo清单更新后如何运行兼容检查、如何添加新 adapter、未知版本为何自动只读，以及如何无损切回 DeskBox 本地待办。
