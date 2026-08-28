# 企业级审查记录 — 第二轮

分支：`cursor/enterprise-audit-hardening-ac60`，基于 `main`（`1987b786`）。

对标产品：Devolutions RDM、Royal TS、mRemoteNG、Termius、Windows Admin Center、
Keeper Connection Manager、Apache Guacamole。

上一轮（`.agent_workspace/ISSUE_FIXES.md` 的 20 项）已经把凭据加密、代理/跳板、备份 Zip-Slip、
主机身份校验、`cmd://` TOFU、临时文件生命周期这些做掉了。这一轮不重复那些，只找上一轮没覆盖的面。

## 构建与测试状态

- `dotnet build Tests/Tests.csproj -p:EnableWindowsTargeting=true` → **0 error**（Linux + .NET 9 SDK
  9.0.317，`git submodule update --init --recursive` 之后）。
- 测试宿主需要 `Microsoft.WindowsDesktop.App`，Linux 上没有，所以 `dotnet test` 跑不了整个测试工程；
  CI 的 `windows-latest` job 会跑。
- **但本轮新增的叶子逻辑是真的跑过的**：在 `/tmp` 里建了一个 `net9.0`（非 windows）的临时测试工程，
  把 `Ui/Utils/Diagnostics`、`Ui/Utils/SessionRecording`、`Ui/Service/Audit`、`Ui/Service/Diagnostics`
  连同对应的测试文件一起编进去，用两个 shim 顶掉 `AppPathHelper` 和 `Assert`。
  结果：**76 passed / 0 failed**。这个临时工程不在仓库里。
  没进去的只有 `ConfigurationServiceSaveTests`（要 `ConfigurationService`，拽进整个 WPF 依赖）和
  已有的 `SessionLogPathTests`（断言 Windows 路径分隔符，在 Linux 上必然失败，与本次改动无关）。
- 这个跑测试的过程抓到一个真 bug：`DiagnosticsRedactor` 不幂等，第二遍会把已经脱敏过的
  `[redacted]:7` 再脱敏一次变成 `[redacted]:12`，把报告出来的原始长度弄错。已修，见下。

## 发现了什么 / 改了什么

### P0-1 设置保存失败后，本次会话内所有后续设置更改被静默丢弃

`ConfigurationService.Save()` 进门把 `CanSave` 置 false、出门置回 true，但中间有三条路会跳过置回：

1. 建目录失败时的 `return`；
2. `JsonConvert.SerializeObject` 抛异常；
3. `DataSourceService.AdditionalSourcesSaveToProfile` 抛异常——它内部是 `try { … } finally`
   而不是 `try { … } catch`，`fi.Delete()` 遇到文件被占用会直接往外抛。

任意一条触发之后，`Save()` 会在第一行 `if (!CanSave) return;` 就返回，**这个进程剩余生命周期内所有
设置更改都不落盘，也不报错**，用户要到下次启动才发现。

同一个方法还有第二个问题：写失败时也把内容记进了 `_lastSavedJson`。下一次 `Save()` 看到「内容没变」
就走跳过分支，于是一次被占用的文件把一次性的失败变成了永久的失败。

**改法**：`try/catch/finally` 保证 `CanSave` 复位；`_lastSavedJson` 只在 `RetryHelper.Try` 报告
成功之后才更新。测试：`Tests/Service/ConfigurationServiceSaveTests.cs`（3 例，需 Windows 宿主）。

### P0-2 SSH / SFTP / FTP / VNC / 代理的失败信息不可操作

RDP 早就有 `RdpDisconnectClassifier`（因为 ActiveX 给的是数字码）。其余协议全是异常，而
`VmFileTransmitHost.Conn`、`VncHost.ConnectAfterReachableAsync`、`ProxyService.ApplyTo` 三处
都是直接把 `e.Message` 打到面板上。SSH.NET / FluentFTP / VncSharpCore 的措辞是写给读堆栈的人看的：
用户看到 `No such host is known.` 不会知道问题出在名字上，UI 也无从判断要不要给重试按钮。

**改法**：新增 `Ui/Utils/Diagnostics/ConnectionFailureClassifier`——先按 `SocketException.SocketErrorCode`，
再按异常类型名，最后才按报错文本，映射到 15 个类别，每类带一句可操作的说明和一个 `IsRetryable`。
故意不引用 SSH.NET / FluentFTP：按类型名匹配，这样这些包升大版本挪 namespace 时不会静默失效。
`ConnectionFailureMessage` 负责组装「说明 + 目标 + 原始报错」，原始报错**永远保留**——说明是从类别
猜出来的，藏掉服务器实际说了什么会让误分类无从发现。

主机身份类的判定刻意排在通用「denied / failed」之前：`Host key verification failed` 里也有 failed，
但用户该先看的是身份变没变。

测试：`Tests/Utils/Diagnostics/`（24 例，Linux 上跑过）。

### P0-3 审计条目泄漏（本轮自己引入的，同一提交内修掉）

`ConnectWithTab` 在目标标签窗口正在关闭时返回 `""`。审计已经记了 ConnectStarted，但既不会记
SessionOpened 也不会记 ConnectFailed，这次尝试会永远停在「进行中」。已补 `AuditConnectFailed`。

### P1-1 连接审计日志（新功能）

原来只有 `LocalityConnectRecorder` 里每台服务器一个「最后连接时间」，用来给列表排序。
事故之后要问的「谁、从哪台机器、什么时候连了那台主机、成没成功」，它一个都答不了。

`Ui/Service/Audit/ConnectionAuditLog`：

- JSON Lines，按 UTC 日期一天一个文件，落在 `.locality/audit/`。
  - 行式：一次追加就是一次写，不会破坏已经写好的内容；换成 JSON 数组就得每次连接重写整个文件，
    写到一半断电就把当月记录一起赔进去。
  - 按天：保留策略变成删文件而不是重写，而且 `grep` / `Get-Content` 直接就能用。
  - 放 locality 不放 profile 旁边：审计说的是**这台机器**上发生的事，不能跟着同步/共享数据源走。
- 写入走一条后台线程 + 有界队列。连接路径上本来就有太多磁盘 IO，审计行绝不该成为会话打开慢的原因；
  队列满了就丢记录，不阻塞连接——磁盘挂了不该升级成业务中断。
- 每次尝试四个事件（started / opened / failed / closed），用 connection id 串起来，关闭时带时长。
  地址是在 `ProxyService.ApplyTo` 改写成 loopback **之前**取的，所以记的是用户想连的那台主机。
- **结构上不含密钥**：`ConnectionAuditRecord` 里没有 password / privatekey / secret 字段，
  `AuditCsvTests.NoPasswordShapedFieldExistsToLeak` 用反射盯着这一点。
- CSV 导出对 `=`、`+`、`-`、`@` 开头的字段加单引号前缀。服务器名是自由文本，共享数据源下未必是导出的人
  自己填的，而 Excel / LibreOffice 会把这种单元格当公式执行（DDE 可以起进程）。
- 默认开，保留 90 天，启动时在工作线程上清理。

测试：`Tests/Service/Audit/`（22 例，Linux 上跑过），含截断行不影响其余记录、服务器名里塞换行
不能伪造出第二条记录、保留策略不碰不属于自己的文件。

### P1-2 会话录像保留策略（补齐既有功能的另一半）

录像功能之前没有任何上限，目录只增不减。会话日志里是屏幕上出现过的一切，所以这在成为磁盘问题之前
先是个信息泄露问题。

`Ui/Utils/SessionRecording/SessionLogRetention`：两个互相独立、都可关闭的上限——天数（策略通常按天写）
和总大小 MB（防止一个超长会话在天数生效前先把盘写满）。默认 30 天 / 1 GB，先删最旧的，只处理目录顶层
的 `*.log`（用户自己建的子目录不动）。

测试：`Tests/Utils/SessionRecording/SessionLogRetentionTests.cs`（9 例，Linux 上跑过）。

### P1-3 诊断包（新功能）

「你的环境是什么样的」以前要用户自己去翻 `.logs\1Remote.log.md`、凭记忆描述启动器设置、再报一下版本号。
实际拿到的报告通常缺日志，而且恰好没提到关键的那条设置。之所以一直不是「把目录打包发过来」，是因为
目录里有凭据库，启动器命令行里有密码。

`Ui/Service/Diagnostics/DiagnosticsBundle` 打包：日志、环境报告、profile、协议启动器定义。
**不打包**：服务器数据库、凭据库、主机信任、`cmd://` 批准、会话录像、审计日志。环境报告里既不写账号名
也不写机器名——这个包是专门为了转发给别人而生成的。

`DiagnosticsRedactor` 在写入前扫一遍每个文本文件：字段名含 password / passphrase / secret / token /
privatekey / credential 等的值、PEM 私钥块、`cmd://` 命令、`-pw` / `--password` 参数。
是替换不是删除，并报告长度——「到底设没设」是个真实的支持问题。清单文件里写明脱掉了什么，并且明说
脱敏是对自由文本的过滤而不是证明。

测试：`Tests/Service/Diagnostics/`（21 例，Linux 上跑过）。幂等性那一例抓到了上面提到的真 bug。

## 刻意没改的

- **`ProtocolBase.Update` 的反射**——上一轮就明确留着，本轮没有理由动它。
- **拆那四个大文件里剩下的两个**（`VmFileTransmitHost.cs` 1412 行、`ServerTreeViewModel.cs` 968 行）。
  本轮在 `VmFileTransmitHost.Conn` 里改了几行，绕得开，不为拆而拆。
- **连接时明文凭据在内存中的停留时间。** `protocolClone.DecryptToConnectLevel()` 之后，明文要活到
  会话结束——外部 runner 要拿它拼命令行，ActiveX 控件要拿它做认证。改成 `SecureString` 只会在
  marshal 出来的那一刻回到同一个位置，是安慰剂。真正的收敛要求把凭据交付方式整个换掉（例如只交
  一次性凭据句柄），不是这个 PR 的体量。
- **备份还原会带回 `Protocols/`（启动器定义）和 profile。** 一个恶意 `.1rbak` 因此可以在下次连接时
  执行任意命令。这是「还原备份」这件事本身的性质，`backup_restore_confirm` 已经在问了；要真正解决
  得给备份包签名，那是独立的一块。
- **审计日志本身没有防篡改。** 本地文件，用户自己就能改。真正的不可否认需要外送到 syslog / SIEM 或者
  做哈希链。当前这一版解决的是「有没有记录」，不是「记录能不能抵赖」。
- **`LocalityTagService.Save` / `LocalityConnectRecorder.Save` 里同样形状的 `CanSave` 模式。**
  查过：这两处所有可能抛异常的语句都在 `RetryHelper.Try` 的 lambda 里（会被吞掉）或者
  `AppPathHelper.CreateDirIfNotExist` 里（自己 catch），所以 `CanSave` 卡不住。没有为了对称去改。

## 剩余风险

- **本轮改动没有在 Windows 上运行过。** 叶子逻辑用临时工程真跑了 76 个用例，但接线部分
  （`SessionControlService` 的四个审计调用点、设置页新增的绑定、`AppInit` 的清理调用）只验证了能编译。
  最值得在 Windows 上先看一眼的：
  - 设置页新增的三块 XAML 是否正常渲染、`AuditDetailVisibility` 的显隐是否跟手；
  - `DetachHost` 里的 `AuditSessionClosed` 是在 `_dictLock` 里调用的。它只做一次
    `ConcurrentDictionary.TryRemove` 加一次非阻塞入队，不违反那条「持锁期间不得阻塞 UI 线程」的
    不变式，但这是这个锁上最需要保持警惕的地方。
- **审计默认开启是一个行为变更。** 每次连接会多写一行到 `.locality/audit/`。不含密钥，但确实是一份
  之前不存在的、会记录用户行为的文件。开关在 `设置 → 常规 → 连接审计`。
- **失败分类里的文本匹配会随库版本漂移。** 只用作最后兜底，且只收了跨版本稳定、且能唯一映射到某一类
  的短语；匹配不上就是 `Unknown`，UI 照样显示原始报错，所以最坏情况是退回改动前的行为。
- **`-P` 曾被当作密码参数。** 早期版本的 `PasswordSwitch` 正则里包含 `-P` 和 `-p`，但 PuTTY 用 `-P`
  指定端口、`-pw` 指定密码，会把端口号误脱敏。已收窄为 `-pw` / `--password` / `/pass`。
