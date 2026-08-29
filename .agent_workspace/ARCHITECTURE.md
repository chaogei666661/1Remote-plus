# ARCHITECTURE — 架构概览基线

> 由 Parent Orchestrator 在 Cycle 1 建立，作为后续架构监督（Fable）与研发（Opus）的共享基线。
> 信息分级：**【事实】** / **【推断】** / **【建议】**。

## 1. 总体分层 【事实】
- `Ui/`（工程 `Ui.csproj`，产物 `1Remote.dll`）是唯一应用工程，采用 WPF + Stylet MVVM。
  - `Ui/View/**`：视图与视图模型（含各协议宿主 `View/Host/ProtocolHosts/*`）。
  - `Ui/Service/**`：应用服务（数据源 DAO/Dapper、配置、备份、审计、诊断等）。
  - `Ui/Model/**`：领域模型（协议、Runner 等）。
  - `Ui/Utils/**`：可独立测试的纯工具（如 `WakeOnLan`、`TimestampedFileName`、诊断分类器等）。
  - `Ui/Resources/Languages/*.xaml`：本地化资源（en-us / zh-cn 为一等公民）。
- 子模块提供基础能力：`Shawn.Utils`（通用工具/日志/加密/版本）、`Shawn.Utils.Wpf(+Resources)`、
  `Dragablz`（可拖拽标签）、`VncSharpCore`（VNC 控件，实际经 NuGet 包消费）、`PuTTY`（内置二进制）。

## 2. 关键跨切面 【事实】
- **安全边界**：凭据保险库、`ExternalSecret*`/`cmd://` TOFU 门、`HostTrustService`、
  WebDAV HTTPS、备份 zip 路径校验——AUTO_ITERATION §4/§5 将其列为“高改动风险、勿轻动”。
- **数据源**：`Ui/Service/DataSource/DAO/Dapper/*` 通过 Dapper 访问 SQLite/MySQL/PostgreSQL。
- **平台耦合**：RDP 宿主依赖 `AxMsRdpClient*`（Windows ActiveX），本质 Windows-only；
  这是无法在 Linux 端到端运行的根因。

## 3. 可测试性设计（重要） 【事实】
- 项目刻意把可测逻辑下沉到无窗口纯类，view model 作薄包装（AUTO_ITERATION §6）。
- 这使得 Linux 侧可用 §7.2 “一次性 harness”按绝对路径编入真实源码 + 真实测试运行部分纯逻辑用例
  （本会话已验证 29 个纯逻辑用例在 Linux 通过）。

## 4. 已识别的架构风险 / 技术债（初判，待团队复核） 【推断】
- R1：RDP/VNC 宿主与 Windows ActiveX 强耦合，难以自动化 UI 测试（结构性，非本轮目标）。
- R2：`Ui.csproj` 编译产生大量 nullable/CA 警告（本会话构建计 120 条），存在渐进式清理空间。
- R3：遗留多 TFM 配置（Net6/Net48）与 `#if NETFRAMEWORK` 分支为不维护死代码，易误导（AUTO_ITERATION 已警示）。
- R4：缺少标准化性能 benchmark，性能回归无量化门禁。

## 5. 演进方向（候选，遵守“稳定性 > 安全 > 用户价值 > 性能 > 新技术”） 【建议】
- 优先低风险高价值：可测纯逻辑的补测与告警清理、诊断/导入保真度、空状态与可发现性。
- 中期：为关键路径建立可量化 benchmark（先度量后优化）。
- 谨慎项：.NET 版本演进、主题/无障碍模式——需 Fable 架构评审与 Windows 侧人工验收。

## 6. 变更红线 【事实】
见 `PROGRESS.md` 第二节与 `.agent_workspace/AUTO_ITERATION.md` §4 “Never”。任何触及安全硬化、
`AppVersion.Build`、历史改写、跨 Agent 文件冲突的改动，均需升级为人工确认或更严格门禁。
