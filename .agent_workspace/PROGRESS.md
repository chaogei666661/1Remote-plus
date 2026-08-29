# PROGRESS — 长期运行进度与项目基线

> 维护者：Parent Orchestrator（模型 `claude-fable-5-thinking-xhigh`）。
> 本文件是长期自治开发系统的“唯一事实来源”，用于 Watchdog 恢复与跨 Cycle 上下文延续。
> 信息分级：**【事实】** 已确认 / **【推断】** 基于证据 / **【建议】** 待验证。

## 恢复指针（Watchdog 用）

- 阶段：Cycle 1 / Round 1（技术侦察与基础分析）。
- 已完成：编排层初始化、工作区状态文件、项目状态基线、Round 1 结论简报（见 `ITERATIONS/CYCLE-001-ROUND-1.md`）。
- 阻塞：无法启动云端子代理（用量耗尽）。见 `WATCHDOG.md`。
- 下一步：云端配额恢复后，按规范启动 Round 1 的 6 个并发云端 Task；**跳过**已完成的初始化/基线。

---

## 一、项目状态基线（Cycle 1 建立）

### 1. 基本信息 【事实】
- 仓库：`chaogei666661/1Remote-plus`（fork 自 `chaogei/1Remote-Plus`，源头 `1Remote/1Remote`）。
- 产品：Windows 远程会话管理器（RDP/SSH/VNC/Telnet/SFTP/FTP/Serial/RemoteApp）。
- 技术栈：WPF on .NET 9，Stylet MVVM；数据源 SQLite/MySQL/PostgreSQL。
- 目标框架：`net9.0-windows10.0.19041.0`（CI 唯一构建目标；`ReleaseNet6`/`ReleaseNet48` 为不维护的遗留配置）。
- 当前版本：`1.3.0.27`（`Ui/AppVersion.cs`；`main` 上 `Build` 由 CI 自动递增，禁止手改）。
- 当前分支/最新提交：`main` @ `fe903b00`（`chore(release): v1.3.0.27 [skip ci]`）。

### 2. 功能清单（概览） 【事实】
- 多协议远程会话；凭据保险库（Credential Vault）；会话录制；端口转发；代理；
  会话脚本；外部密钥引用（`cmd://` / ExternalSecret）；WebDAV 备份；导入（SSH config 等）；
  可达性探测；批量命令；主题与多语言（`Ui/Resources/Languages/*.xaml`，至少 en-us + zh-cn）。

### 3. 解决方案结构 【事实】
- 8 个工程：`Ui`（主 WPF 应用，产物 `1Remote.dll`）、`Tests`、子模块工程
  `Dragablz`、`Shawn.Utils`(+`.Wpf`、`.WpfResources`)、`VncSharpCore`，以及打包工程 `Installer.wapproj`。
- 子模块：`Dragablz`、`Shawn.Utils`、`Ui/Resources/PuTTY`、`VncSharpCore`（`VncSharpCore` 同时在 sln 中但 Ui 通过 NuGet 包 `1Remote.VncSharpCore` 消费）。

### 4. 测试覆盖 【事实】
- `Tests/Tests.csproj`：**507 个测试方法，47 个测试文件**；与 `Ui` 同 TFM。
- CI 在 `windows-latest` 每次 push/PR 运行全量测试。
- 测试宿主需要 `Microsoft.WindowsDesktop.App`，**Linux 无法运行全量测试**；Linux 仅能
  `dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true`（见 AUTO_ITERATION §7）。

### 5. 构建/发布基线 【事实】
- CI：`.github/workflows/build-on-dev-push.yml`。push 到 `main` → 版本自动 bump + 发布正式 release。
- 非 Debug 构建的 `PreBuild` 目标会调用 `powershell.exe`（Linux 不可用），故 Linux 校验必须 `-c Debug`。

### 6. 已知缺陷 / 进行中工作 【事实】
- 未合并 PR：`#16`（RDP 预览/导出文件名修复，草稿）、`#1`（静态分析报告，草稿）、`#17`（Cloud Agent .NET9 环境，本会话产出，已就绪待评审）。
- 仓库 Issue 功能已禁用（`gh issue list` 返回 disabled），缺陷来源以历史文档与 PR 为主。
- 历史迭代记录见 `.agent_workspace/ITERATION_LOG.md`（97KB，逐轮记录，含被否决项——勿重复提出）。

### 7. 性能基线 【推断】
- 目前仓库内**未见**标准化 benchmark 工程；性能改进需先由 QA(GPT-Sol) 建立可量化基线（属候选项）。

### 8. 安全风险与硬化现状 【事实】
- 已存在的硬化（AUTO_ITERATION §4 明令**不得撤销**）：`cmd://` 首次使用信任门（TOFU）、
  WebDAV 强制 HTTPS、SFTP/FTPS host-key 存储、每会话受限 ACL 临时目录、占位盐警告。
- 可选 CI secret：`GLOBAL_STRING_ENCRYPTION_SLAT`、`SENTRY_IO_DEN`（缺失则遥测停用、使用公开占位盐）。

---

## 二、开发系统约束（本仓库特有，供全体 Agent 遵守） 【事实】
1. **禁止手改** `Ui/AppVersion.cs` 的 `Build`（CI 会 bump，手改必冲突）。
2. **禁止撤销**既有安全硬化（见上）。
3. 禁止 force-push / amend 已推送提交 / 改写历史。
4. 禁止自行合并自己的 PR 或开启 auto-merge（由 Parent 合并）。
5. 用户可见字符串必须同时进 `en-us.xaml` 与 `zh-cn.xaml`。
6. 用户可见行为变化需同步 `README.md` 与 `README.zh-CN.md`。
7. 可无窗口测试的逻辑应放入 `Ui/Utils/` 或 `Ui/Model/` 的纯类，便于在 Linux 上按 §7.2 harness 验证。

## 三、当前阻塞（已确认事实）
- 云端子代理用量耗尽：`Task(environment=cloud, model=claude-fable-5-thinking-xhigh)` 立即返回
  “You've used all included Cloud Agent usage. Enable on-demand usage to continue using Cloud Agents.”
- 规则禁止本地子代理 / `inherit` / 模型降级，故团队无法启动；编排层保持阻塞并保留现场，不做降级。

## 四、尚未完成的任务
- 启动 Round 1 的 6 个并发云端 Task（阻塞中）。
- Round 2 研发实施、Round 3 SOTA 验收与发布（依赖 Round 1）。
