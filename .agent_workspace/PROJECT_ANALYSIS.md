# 1Remote-plus — Project Analysis

Analysis of `chaogei666661/1Remote-plus` at commit `19def90d` ("Bump the build to 1.3.0.7-beta"), branch `main`.

**Method and limits.** This is a static, read-only analysis. The repository was read in full on a Linux
VM with no .NET SDK, no MSBuild and no Windows SDK installed (`dotnet` is not on `PATH`), and the product
targets `net9.0-windows10.0.19041.0` with WPF, WinForms interop and the MSTSC ActiveX control — so it
cannot be compiled or run here even with a toolchain. In addition, all four git submodules
(`Shawn.Utils`, `Dragablz`, `VncSharpCore`, `Ui/Resources/PuTTY`) are **not checked out** in this
workspace: those directories are empty. Every claim below therefore comes from reading source in `Ui/`,
`Tests/`, the build scripts and the git history, and types living in the submodules
(`SimpleLogHelper`, `NotifyPropertyChangedBase`, `WinCmdRunner`, `VersionHelper`, `DragablzItem`, …) are
described from their call sites rather than their definitions.

---

## 1. Executive overview

1Remote is a **Windows desktop remote-session manager and launcher**: one list of saved servers, each with
credentials, tags, icons, colours and pre/post-connect scripts, that can be opened either into a tabbed
host window inside the app or handed off to an external tool. It is a single-user desktop product, not a
service — there is no server component, no multi-user model and no agent.

* **Product name / assembly:** `1Remote` (`Ui.csproj` → `<AssemblyName>1Remote</AssemblyName>`), root
  namespace `_1RM`, entry point `_1RM.Program` (`Ui/App.xaml.cs`).
* **Target users:** sysadmins, developers and homelab users on Windows who juggle many RDP/SSH/VNC
  endpoints and want a launcher (`Alt+M`) plus a session manager rather than separate mstsc/PuTTY windows.
* **License:** GPL-3.0 (`LICENSE`). Copyright is split in `Ui.csproj`:
  `Copyright (C) Shawn Veck and 1Remote contributors. Fork maintained by chaogei.`
* **Fork relationship:** this is a hard fork of [`1Remote/1Remote`](https://github.com/1Remote/1Remote) by
  Shawn Veck. The fork point is `5b9d8441` ("fix: fix multiple bugs found in code audit", authored by
  Shawn, 2026-07-14); the first fork commit is `ee7ea193` (2026-08-12). There are **44 fork commits**
  totalling **185 files changed, 12,604 insertions, 2,696 deletions**. `readme.md` states plainly that the
  fork is not affiliated with or endorsed by upstream and redirects issues to the fork's tracker.

Note that `readme.md`, `AppVersion.cs` and `Ui.csproj` all point at `github.com/chaogei/1Remote`, while
the git remote is `github.com/chaogei666661/1Remote-plus`. The in-app update check
(`AppVersion.UpdateCheckUrls`) therefore polls a repository that is not this one.

---

## 2. Tech stack

| Concern | Choice | Evidence |
|---|---|---|
| Language | C# `latest`, nullable enabled | `Ui/Ui.csproj` |
| UI framework | WPF (+ `UseWindowsForms` for the RDP ActiveX host) | `Ui/Ui.csproj` |
| MVVM / IoC | **Stylet** 1.3.6 and StyletIoC | `Ui/Bootstrapper.cs`, `Ui/Ioc.cs` |
| Primary TFM | **`net9.0-windows10.0.19041.0`** | `Ui/Ui.csproj` |
| Alternate TFMs | `net6.0-windows10.0.19041.0` (config `*Net6`), `net48` (config `*Net48`) | `Ui/Ui.csproj` |
| Persistence | SQLite (`System.Data.SQLite.Core` 1.0.117) + Dapper 2.1.66; optional MySQL and PostgreSQL | `Ui/Service/DataSource/**` |
| SSH | `SSH.NET` 2023.0.0 | `Ui/Utils/Proxy/SshConnectionFactory.cs`, `PortForwardService` |
| FTP | `FluentFTP` 51.0.0 | `Ui/Model/Protocol/FileTransmit/Transmitters/TransmitterFtp.cs` |
| VNC | `1Remote.VncSharpCore` 1.2.1 (NuGet) — note the `VncSharpCore` submodule also exists | `Ui/Ui.csproj` |
| Crypto helper | `1Remote.Security` 1.1.0 (NuGet) | `Ui/Utils/UnSafeStringEncipher.cs` |
| Telemetry | `Sentry` 4.13.0 | `Ui/Utils/Tracing/*` |
| Tabs | `Dragablz` (submodule, project reference) | `Ui/View/Host/TabWindowView.xaml` |
| Serialisation | `Newtonsoft.Json` 13.0.1 + `JsonKnownTypes` 0.5.4 (polymorphic protocols/data sources) | `Ui/Ui.csproj` |
| Editors / misc | AvalonEdit, Markdig.Wpf, NUlid, VirtualizingWrapPanel, `VariableKeywordMatcherIn1`, `System.IO.Ports` | `Ui/Ui.csproj` |
| Native / COM | `lib/AxMSTSCLib.dll`, `lib/MSTSCLib.dll` (RDP ActiveX interop), plus P/Invoke into `user32`, `dwmapi`-adjacent accent APIs, `credui`, `shell32` | `Ui/View/Host/ProtocolHosts/AxMsRdpClient09Host.xaml.cs`, `Ui/Utils/Theme/AcrylicHelper.cs`, `Ui/Utils/WindowsApi/**` |
| Build | MSBuild + `dotnet publish` with pubxml profiles; `Invoke-Build`/`prm.build.ps1` for local convenience | `.github/workflows/build-on-dev-push.yml`, `prm.build.ps1` |
| CI | GitHub Actions, `windows-latest`, publishes framework-dependent and self-contained x64 net9 artifacts | `.github/workflows/build-on-dev-push.yml` |

Two project-wide settings matter for everything downstream:

```29:30:Ui/Ui.csproj
        <CheckForOverflowUnderflow>True</CheckForOverflowUnderflow>
        <AllowUnsafeBlocks>True</AllowUnsafeBlocks>
```

`CheckForOverflowUnderflow` makes every integer operation in the assembly checked, which is why the fork's
"checked-arithmetic overflow" fixes exist at all (see §8).

**Documentation drift:** `DEVELOP.md` still says "`.NET6 SDK`" and "Windows 10 SDK 10.0.17763.0", and
`prm.build.ps1`'s `Deps` task installs `dotnet-6.0-sdk`. Neither matches the net9 default TFM.

---

## 3. Architecture

### 3.1 Module map

```
1Remote.sln
├── Ui/                     main WPF app — 294 .cs files, ~52.7k LOC, 95 .xaml
│   ├── Program/App/Bootstrapper/AppInit/Ioc      startup + composition root
│   ├── Model/                                    domain: protocols, runners, tags, global data
│   │   ├── Protocol/Base/                        ProtocolBase hierarchy
│   │   ├── Protocol/FileTransmit/                SFTP/FTP transfer engine
│   │   └── ProtocolRunner/                       internal + external runners
│   ├── Service/                                  application services (singletons via IoC)
│   │   ├── DataSource/                           SQLite/MySQL/PgSQL sources + Dapper DAO
│   │   ├── Locality/                             per-machine, non-synced state
│   │   └── Backup/                               archive + WebDAV
│   ├── Utils/                                    proxy, port-forward, PuTTY, RDP file, WinAPI, theme…
│   ├── View/                                     Stylet views + viewmodels
│   └── Resources/                                icons, 14 language dictionaries, themes, bundled PuTTY/KiTTY
├── Tests/                  MSTest, 13 files, 101 [TestMethod]s
├── Installer/              MSIX packaging project (.wapproj) for the Microsoft Store
├── lib/                    AxMSTSCLib.dll / MSTSCLib.dll + BuildAxMSTSCLib.ps1
├── scripts/                version stamping, secret injection, sandbox build
└── submodules: Shawn.Utils, Shawn.Utils.Wpf, Shawn.Utils.WpfResources, Dragablz, VncSharpCore, PuTTY
```

`Tests/Tests.csproj` is **not listed in `1Remote.sln`** (`rg "Tests" 1Remote.sln` returns nothing). It is
only reachable via an explicit `dotnet test Tests/Tests.csproj`.

### 3.2 Startup sequence

The bootstrap is a Stylet `Bootstrapper<LauncherWindowViewModel>` with a four-phase lifecycle, and
`AppInitHelper` (in `Ui/AppInit.cs`) supplies one method per phase.

1. **`Program.Main`** (`Ui/App.xaml.cs`) — `AppInitHelper.Init()` disables logging, seeds the string cipher
   salt from `Assert.STRING_SALT`, pins the working directory to the app base directory, and kicks off
   Sentry on a background task. Then `AppStartupHelper.Init(args)` handles CLI arguments and single-instance
   behaviour (it may call `Environment.Exit`). For Store builds, UWP activation args are folded in.
2. **`Bootstrapper.OnStart`** → `AppInitHelper.InitOnStart()`. This is the heavyweight phase: pick
   **portable vs AppData** storage, run write-permission checks on every path, show the first-run guidance
   window, create directories, initialise the logger, load `ConfigurationService`, construct `ThemeService`
   and `GlobalData`.
3. **`Bootstrapper.ConfigureIoC`** — registers ~30 singletons. Four objects created in phase 2
   (`LanguageService`, `KeywordMatchService`, `ConfigurationService`, `ThemeService`, `GlobalData`) are
   bound with `ToInstance`; everything else is `ToSelf().InSingletonScope()`.
4. **`Bootstrapper.Configure`** → `IoC.Init(Container)` then `AppInitHelper.InitOnConfigure()`: wires
   `DataSourceService` into `GlobalData` and opens the local SQLite source.
5. **`Bootstrapper.OnLaunch`** → `AppInitHelper.InitOnLaunch()`: `GlobalData.ReloadAll(true)`, tray menu,
   telemetry snapshot of settings, PuTTY/KiTTY config cleanup, show the main window (unless started
   minimised), attach additional data sources, start the ticker, register the launcher hotkey. Then
   `PortForwardService.StartAutoStartsAsync()` (fire-and-forget, deliberately off the UI thread) and
   `ServerReachabilityService.ApplyConfiguration()`.

`OnExit` disposes the tray, session control, reachability, port forwards and the proxy pool, and — as a
"workaround" — schedules `Environment.Exit(1)` after five seconds in case something refuses to shut down.
The same pattern appears in `App.Close`.

### 3.3 Service locator vs. injection

`Ui/Ioc.cs` is a **static service locator** over the StyletIoC container, with `IoC.Get<T>()`,
`IoC.TryGet<T>()` and `IoC.Translate(...)`. Some services take constructor dependencies
(`PortForwardService(ConfigurationService, ProxyService)`, `SessionControlService(DataSourceService,
ConfigurationService, GlobalData)`), but a great deal of code — including domain types like `ProtocolBase`
— reaches into `IoC.Get<...>()` directly:

```261:262:Ui/Model/Protocol/Base/ProtocolBase.cs
        [JsonIgnore] 
        public bool SelectedRunnerIsInternalRunner => RunnerHelper.GetRunner(IoC.Get<ProtocolConfigurationService>(), this, this.Protocol) is InternalDefaultRunner;
```

This is the single largest structural constraint in the codebase (see §9). `IoC.GetByType` is a mutable
public delegate, which is precisely what the test project overrides in `Tests/TestInit.cs` to make anything
testable at all.

`Ui/Model/GlobalEventHelper.cs` provides a second, event-based decoupling channel — `SessionControlService`
subscribes to `OnRequestServerConnect`, `OnRequestQuickConnect` and `OnRequestServersConnect` in its
constructor and unsubscribes in `Release()`.

### 3.4 Protocol model hierarchy

```
ProtocolBase                                    (abstract, NotifyPropertyChangedBase, IDataErrorInfo)
├── Dummy                                       (ProtocolName = "")
├── Serial            : IPuttyConnectable
└── ProtocolBaseWithAddressPort                 (abstract) — Address/Port, MAC, alternate credentials
    ├── Telnet        : IPuttyConnectable
    └── ProtocolBaseWithAddressPortUserPwd      (abstract) — UserName/Password/PrivateKey
        ├── RDP        (sealed, 1065 LOC)
        ├── RdpApp     (sealed, "RemoteApp")
        ├── SSH        : IPuttyConnectable
        ├── SFTP       : IFileTransmittable
        ├── FTP        : IFileTransmittable
        ├── VNC
        └── LocalApp   ("App" — arbitrary local executable, e.g. NoMachine)
```

Protocols are discovered reflectively:

```485:495:Ui/Model/Protocol/Base/ProtocolBase.cs
        public static List<ProtocolBase> GetAllSubInstance()
        {
            var assembly = typeof(ProtocolBase).Assembly;
            var types = assembly.GetTypes();
            // reflect remote protocols
            var protocolList = types.Where(item => item.IsSubclassOf(typeof(ProtocolBase)) && !item.IsAbstract)
                .Select(type => (ProtocolBase)Activator.CreateInstance(type)!)
                .Where(x => string.IsNullOrEmpty(x.Protocol) == false)
                .OrderBy(x => x.GetListOrder()).ToList();
            return protocolList;
        }
```

Two other reflection-heavy pieces live on the base class: `Update(ProtocolBase, Type?)` copies **all**
fields and properties between instances via `BindingFlags.NonPublic | Public | Instance`, and `Clone()` is
a documented shallow `MemberwiseClone` with hand-written deep copies for `Tags`, `TreeNodes` and
`AlternateCredentials`. Both are correctness hazards when new reference-typed members are added.

### 3.5 Runners

`Ui/Model/ProtocolRunner/` models "what program actually opens this session":

* `Runner` (base) → `InternalDefaultRunner` (built-in host), `InternalExeRunner` → `PuttyRunner`,
  `KittyRunner`, and `ExternalRunner` → `ExternalRunnerForSSH`.
* `RunnerHelper.GetRunner(...)` resolves in priority order: explicitly assigned name → the server's
  `SelectedRunnerName` → the protocol's default → the first available → `InternalDefaultRunner`.
* `RunnerHelper.GetHost(...)` maps a resolved runner to a `HostBase`: `AxMsRdpClient09Host` for RDP,
  `VncHost` for VNC, `FileTransmitHost` for SFTP/FTP, `IntegrateHost` for anything driven by an external
  executable (PuTTY/KiTTY/WinSCP/…), which reparents the child process's window into the tab.
* `ExternalRunner` with `RunWithHosting: false` bypasses hosting entirely
  (`RunnerHelper.RunWithoutHosting`), starting a detached process watched by
  `SessionControlService.AddUnHostingWatch`.

### 3.6 Session lifecycle

`SessionControlService` is a partial class split over five files:

| File | Responsibility |
|---|---|
| `SessionControlService.cs` | dictionaries, locking, cleanup, `Release()` |
| `SessionControlService_OpenConnection.cs` | the `Connect` pipeline |
| `SessionControlService_AlternateCredential.cs` | choosing/prompting for credentials |
| `SessionControlService_WindowControl.cs` | tab ↔ full-screen movement |
| `SessionControlService_WatchingUnhosting.cs` | tracking detached processes |

The connect pipeline in `Connect(...)` runs in a deliberate order, and the comments say why:

1. bump `Engagement.ConnectCount`, record last-connect time, refresh the tray menu;
2. `protocol.Clone()` then `DecryptToConnectLevel()` then `GenerateSessionId()` — the original in-memory
   object is never decrypted;
3. apply an alternate credential (`GetCredential`);
4. prompt for a password if `AskPasswordWhenConnect`;
5. `ActivateOrReConnIfServerSessionIsOpened` for single-instance protocols;
6. `RunScriptBeforeConnect()` — abort on non-zero exit code;
7. **`ProxyService.ApplyTo(protocolClone)`** — after (5) and (6) so both still see the real address;
8. dispatch: RemoteApp → `mstsc`; RDP needing mstsc → `mstsc`; RDP full-screen → `ConnectWithFullScreen`;
   SSH with `OpenSftpOnConnected` → recursive `Connect` for an SFTP sibling; `LocalApp` without hosting →
   `Process.Start`; otherwise `ConnectWithTab`.

Concurrency is governed by one documented invariant:

```59:67:Ui/Service/SessionControlService.cs
        /// Guards compound reads/writes over the session dictionaries below.
        ///
        /// INVARIANT: never block on the UI thread while holding this lock — no Execute.OnUIThreadSync,
        /// no Dispatcher.Invoke, no Task.Wait, no external process. ConnectWithTab enters this lock from
        /// the UI thread, so a holder that waits for the UI thread deadlocks against it. Collect the UI
        /// work into a local list inside the lock and run it afterwards instead.
```

That invariant is the fork's fix for the windowed-mode deadlock (`ee7ea193`).

### 3.7 Persistence and data sources

* `DataSourceBase` (partial: `.Config.cs` + `.Source.cs`) is the abstract source. Concrete:
  `SqliteSource`, `MysqlSource`, `PgsqlSource`. One local source (`LOCAL_DATA_SOURCE_NAME = "Local"`) plus
  a `ConcurrentDictionary` of additional sources in `DataSourceService`.
* The DAO layer is Dapper over three tables (`TableServer`, `TableCredential`, `TableConfig`) in
  `Ui/Service/DataSource/DAO/Dapper/`. Freshness is decided by comparing a per-table `UpdateTimestamp`
  against a cached read timestamp (`DataSourceBase.NeedRead`), so remote sources poll rather than push.
* A sentinel row `EncryptionTest` in the config table validates that the salt matches the database
  (`Database_SelfCheck` → `CheckEncryptionTest`), producing `EnumDatabaseStatus.EncryptKeyError`.
* **On-disk layout** is defined entirely in `Ui/Service/AppPathHelper.cs`, split into "Remoting" (syncable)
  and "Locality" (machine-local): `1Remote.json`, `1Remote.dataSources.json`, `1Remote.db`, `Protocols/`
  and `.logs/`, `.locality/`, `.locality/known_hosts.json`, `.icons/`, `.sessionlogs/`, `KiTTY/`, `PuTTY/`.
* **Portable vs AppData** is decided by the presence of marker files `FORCE_INTO_PORTABLE_MODE` /
  `FORCE_INTO_APPDATA_MODE` plus a real write test (`AppInit.cs` writes and deletes `PermissionCheck.txt`).
  Store builds are forced to AppData via `#if FOR_MICROSOFT_STORE_ONLY`.
* `Ui/Service/Locality/` keeps everything that should *not* sync between machines: connect history
  (`LocalityConnectRecorder`), list/tree view state, tag state.

### 3.8 Credential storage

There are three distinct layers, and they are easy to confuse:

1. **At-rest obfuscation of stored secrets.** `Ui/Utils/UnSafeStringEncipher.cs` wraps
   `_1Remote.Security.SimpleStringEncipher` with a compile-time salt (`Assert.STRING_SALT`). The class name
   is honest: this is obfuscation with a shared, build-embedded key, not per-user encryption.
   `Ui/Service/DataBaseService.cs` is the only place that converts between levels
   (`EncryptToDatabaseLevel` / `DecryptToConnectLevel`).
2. **Windows-backed storage for the app's own flags.** `Ui/Utils/WindowsSdk/DataProtectionForLocal.cs`
   (DPAPI/UWP `DataProtectionProvider`), `PasswordVaultManagerWindowsApi` / `PasswordVaultManagerFileSystem`
   behind `IPasswordManager`, and `Ui/Utils/WindowsApi/Credential/Credential.cs` (Credential Manager).
   `SecondaryVerificationHelper` writes the "Windows Hello enabled" flag through three fallbacks in order:
   Credential Manager → `HKCU\Software\1Remote` → a DPAPI-protected file under `.locality`.
3. **Second-factor prompts.** `SecondaryVerificationHelper.VerifyAsyncUi` uses
   `WindowsHelloHelper.HelloVerifyAsync` when Hello is available, otherwise falls back to the
   `CredUIPromptForWindowsCredentials` wrapper in `Ui/Utils/WindowsApi/Credential/CredentialPrompt.cs`.

**External secret resolution** is the fork's fourth path. `Ui/Utils/ExternalSecret/ExternalSecretResolver.cs`
treats any stored value beginning with `cmd://` as a shell command whose stdout is the secret, cached
per-process by command string. It is wired in exactly one place, so every protocol gets it at once:

```58:62:Ui/Service/DataBaseService.cs
        private static string ToUsableSecret(string stored)
        {
            var plain = UnSafeStringEncipher.DecryptOrReturnOriginalString(stored);
            return ExternalSecretResolver.IsReference(plain) ? ExternalSecretResolver.Resolve(plain) : plain;
        }
```

---

## 4. Protocol & connection subsystem

### 4.1 How each protocol is launched

| Protocol | Default path | Key files |
|---|---|---|
| RDP | `AxMsRdpClient09Host` (MSTSC ActiveX in a `WindowsFormsHost`), or `mstsc.exe` with a generated `.rdp` file when `IsNeedRunWithMstsc()` | `Ui/View/Host/ProtocolHosts/AxMsRdpClient09Host.xaml.cs` (1123 LOC), `Ui/Model/Protocol/RDP.cs` (1065 LOC), `Ui/Utils/RdpFile/RdpConfig.cs` |
| RemoteApp | always a temp `.rdp` handed to `mstsc` via `cmd.exe`, deleted after ~10 s | `SessionControlService_OpenConnection.ConnectRemoteApp` |
| SSH / Telnet / Serial | bundled PuTTY or KiTTY, window reparented into a tab by `IntegrateHost` | `Ui/Model/ProtocolRunner/Default/PuttyRunner.cs` (612 LOC), `KittyRunner.cs`, `Ui/Utils/PuTTY/**` |
| VNC | in-process `VncHost` over `1Remote.VncSharpCore` | `Ui/View/Host/ProtocolHosts/VncHost.xaml.cs` |
| SFTP / FTP | in-app dual-pane file manager (`FileTransmitHost` + `VmFileTransmitHost`, 1381 LOC) over `ITransmitter` (`TransmitterSFtp` via SSH.NET, `TransmitterFtp` via FluentFTP) | `Ui/Model/Protocol/FileTransmit/**` |
| App (LocalApp) | arbitrary executable, hosted or detached, with a typed argument model (`AppArgument`, `AppArgumentHelper`) | `Ui/Model/Protocol/AppProtocol.cs` |

Any of these can be replaced by a user-defined `ExternalRunner` with macro-substituted arguments
(`%1RM_HOSTNAME%`, `%1RM_PORT%`, `%1RM_PRIVATE_KEY_PATH%`, …), resolved through `OtherNameAttributeExtensions`.

### 4.2 Proxy design

The design decision is stated at the top of `ProxyService`:

```27:33:Ui/Service/ProxyService.cs
    /// Owns the global proxy list and the live tunnels built from it.
    ///
    /// Protocols are never taught to speak SOCKS or HTTP CONNECT themselves. Instead every proxied session
    /// is pointed at a loopback port that relays through the proxy, so RDP (an ActiveX control) and VNC (a
    /// pre-built package) get proxy support for free, and there is exactly one implementation to maintain.
```

* `EProxyType`: `None`, `Socks5`, `Socks4`, `Socks4A`, `Http` (CONNECT), `SshJump`.
* `ProxyHandshake.Perform(...)` implements the SOCKS4/4a/5 and HTTP CONNECT handshakes by hand.
* `ITunnel` has two implementations: `ProxyTunnel` (loopback listener + handshake relay) and
  `SshJumpTunnel` (SSH.NET direct-tcpip channel, the equivalent of OpenSSH `-J`).
* `ProxyTunnelPool` keeps one tunnel per `(proxy endpoint, target)` for the app's lifetime, keyed by
  `ProxyConfig.GetEndPointKey()`. Entries are `Lazy<ITunnel>` so authentication happens outside the pool
  lock. The local port is derived deterministically with **FNV-1a** rather than `string.GetHashCode()`,
  because .NET Core randomises the latter per process and a moving port would invalidate PuTTY's cached
  host key and RDP certificate trust on every launch. That FNV loop is one of the explicit `unchecked`
  blocks required by the project-wide checked arithmetic.
* Servers reference a proxy **by name**, not by value, so `ProxyService.RenameInServers` migrates every
  affected server when an entry is renamed, and `FindServersUsing` tells the settings page the blast radius
  of a delete.
* `IsLocalAddress` deliberately treats only loopback/`localhost`/this machine's own adapter addresses as
  local — RFC1918 ranges are **not** bypassed, on the grounds that reaching a LAN machine from outside is
  the main reason to configure a proxy at all.
* When a proxy is missing, incomplete or fails to build, `AskToFallBackToDirect` asks the user rather than
  silently connecting direct (`EProxyApplyResult.Abort` cancels the connection).

### 4.3 Keeping the real address

The fork's headline fix. `ProtocolBaseWithAddressPort` now records where the session was actually going:

```79:89:Ui/Model/Protocol/Base/ProtocolBaseWithAddressPort.cs
        public void RedirectThroughTunnel(string loopbackHost, int loopbackPort)
        {
            TunnelledFromAddress ??= Address;
            TunnelledFromPort ??= Port;
            // The Address setter renames the server when the display name still mirrors the old address,
            // which would retitle a session named after its IP to the loopback address.
            var displayName = DisplayName;
            Address = loopbackHost;
            Port = loopbackPort.ToString();
            DisplayName = displayName;
        }
```

`RealAddress`/`RealPort` then back `GetSubTitle()`, `BuildConnectionId()`, the `SERVER_HOST` script
environment variable and `ServerProbe`, so a tunnelled session is never mistaken for a connection to
`127.0.0.1`.

### 4.4 Standing port forwards

`PortForwardService` owns long-lived forwards independent of any session. Forwards run over the SSH entries
from the proxy list (`AvailableHosts` filters `EProxyType.SshJump`), one authenticated `SshClient` shared
per host (`_sessions` keyed by `GetEndPointKey()`), supporting `Local`, `Remote` and `Dynamic`
(`ForwardedPortLocal/Remote/Dynamic`). A 15-second `System.Timers.Timer` reconciles claimed status against
reality, because a dropped session takes its forwards with it silently.

### 4.5 Session recording

`Ui/Utils/SessionRecording/SessionLogPath.cs` only *names* the file; the recording itself is PuTTY's own
`-sessionlog`. Names carry millisecond timestamps so two tabs opened in the same second do not collide,
and `Sanitize` clips to 48 characters and replaces invalid path characters. Default location is
`.sessionlogs/` (`AppPathHelper.SessionLogDirPath`), overridable via `GeneralConfig.SessionLogFolder`.

### 4.6 Backup and WebDAV

`BackupService` zips six locations (profile, data-source list, SQLite DB, `Protocols/`, `.locality/`,
`.icons/`) into a `.1rbak` archive with a `1remote-backup.txt` manifest. Files are opened with
`FileShare.ReadWrite | FileShare.Delete` because the SQLite DB and log are open in-process. `ResolveTarget`
rejects any entry that would escape its declared root — a Zip-Slip guard. `WebDavClient` implements only
PUT/GET/PROPFIND directly on `HttpClient`, sends Basic auth pre-emptively, uses `Depth: 1`, and parses the
multistatus XML in a separately testable `ParseFileNames`.

---

## 5. UI / UX architecture

* **Pattern:** Stylet MVVM with convention-based view location (`XxxView` ↔ `XxxViewModel`). Most
  viewmodels are IoC singletons; `NotifyPropertyChangedBaseScreen` and `DisposableViewModel` are the shared
  bases.
* **Shell:** `MainWindowView` hosts a page enum (`EnumMainWindowPage`: CardView, ListView, TreeView, About,
  SettingsGeneral/Data/Runners/Launcher/Theme). Server browsing has three presentations —
  `ServerListPageViewModel` (card + line items), `ServerTreeViewModel` (968 LOC, with a separate
  `_Filter` partial), and a tag panel.
* **Launcher:** `LauncherWindowViewModel` + `ServerSelectionsViewModel` + `QuickConnectionViewModel`,
  bound to a global hotkey registered in `InitOnLaunch`. Matching goes through `KeywordMatchService` over
  the `VariableKeywordMatcherIn1` package (direct match plus pinyin/initials providers).
* **Session host:** `TabWindowView` uses **Dragablz** for tear-off tabs; `TabItemViewModel` wraps a
  `HostBase`. `FullScreenWindowView` is the alternate container, and `SessionControlService_WindowControl`
  moves a host between them without reconnecting.
* **Modal/overlay system:** `Ui/View/Utils/MaskAndPop/` — `MaskLayerController`, `MaskLayer`, `PopupBase`,
  `IMaskLayerContainer` — provides in-window dialogs and the processing ring instead of OS message boxes.
* **Theming:** `ThemeService` (394 LOC) builds a `ResourceDictionary` from a `ThemeConfig` and ships **17
  presets**, of which the eight added by this fork are acrylic-tuned: *Mica Slate, Nord Frost, Tokyo Night,
  Catppuccin Mocha, Emerald Glass, Cyber Neon, Rose Pine, macOS Light Glass*. `Ui/Resources/Theme/Glass.xaml`
  holds the frosted surface styles.
* **Acrylic:** `AcrylicHelper` calls the undocumented `SetWindowCompositionAttribute` accent policy rather
  than the Windows 11 `DWMWA_SYSTEMBACKDROP_TYPE`, because every window here is `WindowStyle=None` +
  `AllowsTransparency=True` and only the accent policy composites correctly on a layered window. Windows 11
  (build ≥ 22000) gets `EnableAcrylicBlurBehind`; Windows 10 gets the cheaper `EnableBlurBehind`, because
  Win10 re-samples the desktop every frame and made window drags stutter. Failure degrades to an opaque
  window. `AcrylicBehavior` is the attached-property front end, and `ForceRedraw` fixes the
  restore-from-tray repaint problem.
* **Icons:** the fork replaced font glyphs with vector paths in `Ui/Resources/Icons/SVG.xaml`; raster icons
  for servers still live in `Resources/Icons/000_OS`, `001_APP`, `20210106`.
* **Localization:** `LanguageService` swaps merged `ResourceDictionary` files;
  **14 languages** in `Ui/Resources/Languages/` (cs-cz, de-de, en-us, es-ar, fr-fr, gl-es, it-it, ja-jp,
  pl-pl, pt-br, pt-pt, ru-ru, zh-cn, zh-tw), with a `glossary/` folder plus `glossary_maker.py` and
  `conver_glossary_to_xaml.bat` to regenerate them. Translation is reached from anywhere through
  `IoC.Translate`, which falls back to the key when no service is registered.
* **DPI:** `Ui/app.manifest` declares `PerMonitorV2,PerMonitor` awareness, which is what makes the
  multi-monitor 4K RDP scenario work.

---

## 6. Testing

`Tests/` is an MSTest project (`MSTest.TestAdapter`/`TestFramework` 3.6.1, `Microsoft.NET.Test.Sdk`
17.11.1, `coverlet.collector` 6.0.2) targeting `net9.0-windows10.0.19041.0` — the csproj carries a comment
explaining it had to follow `Ui` from net6 to net9 or it would not build at all.

**13 files, 101 `[TestMethod]`s:**

| Area | File | Count |
|---|---|---|
| SSH config parsing | `Utils/SshConfig/SshConfigParserTests.cs` | 14 |
| External secrets | `Utils/ExternalSecret/ExternalSecretResolverTests.cs` | 12 |
| Proxy config | `Utils/Proxy/ProxyConfigTests.cs` | 11 |
| Port-forward config | `Utils/PortForward/PortForwardConfigTests.cs` | 11 |
| Session input | `Utils/SessionInput/SessionInputTests.cs` | 10 |
| WebDAV | `Service/WebDav/WebDavTests.cs` | 8 |
| Reachability probe | `Utils/Reachability/ServerProbeTests.cs` | 8 |
| Wake-on-LAN | `Utils/WakeOnLan/WakeOnLanTests.cs` | 8 |
| Session log naming | `Utils/SessionRecording/SessionLogPathTests.cs` | 6 |
| GDI error suppression | `Service/GdiErrorHandlingTests.cs` | 5 |
| Update check | `View/AboutPageUpdateCheckTests.cs` | 5 |
| Version helper | `Utils/VersionHelperTests.cs` | 3 |

`Tests/TestInit.cs` is the seam: it seeds the cipher salt with `"tests-only-salt"` and replaces
`IoC.GetByType` with a stub returning a `MockLanguageService`. That is the only way to run any of this
outside a live container.

**How to run:** `dotnet test Tests/Tests.csproj -c Debug` on Windows with the .NET 9 SDK and the Windows
10.0.19041 targeting pack, after `git submodule update --init --recursive` (the project transitively
references three submodule projects).

**Gaps — this is the weakest area of the project:**

* **CI never runs the tests.** `.github/workflows/build-on-dev-push.yml` restores, substitutes secrets,
  stamps the version and runs two `dotnet publish` invocations. There is no `dotnet test` step anywhere.
* **The test project is not in `1Remote.sln`,** so "build solution" and Test Explorer both miss it.
* Every test covers a **leaf utility**. Nothing covers: the `Connect` pipeline in
  `SessionControlService_OpenConnection`, the `_dictLock` invariant, `ProxyTunnelPool`/`ProxyTunnel`
  behaviour (only `ProxyConfig` value semantics are tested), the Dapper DAO or any data source, the
  encrypt/decrypt round trip in `DataBaseService`, `BackupService` create/restore including the Zip-Slip
  guard in `ResolveTarget`, `HostTrustService`, `ProtocolBase.Update`/`Clone` reflection, the mRemoteNG
  importer, or `AppArgumentHelper` (721 LOC).
* No integration tests, no UI tests, no coverage gate despite `coverlet.collector` being referenced.

---

## 7. Build & release

**Configurations** (`Ui.csproj` / `1Remote.sln`): `Debug`, `Release`, `StoreDebug`, `StoreRelease`,
`ReleaseNet48`, `ReleaseNet6` — each × `Any CPU`/`x64`/`x86`. `Debug` and `StoreDebug` build as `Exe`
(console attached via `ConsoleManager.Show()`); everything else is `WinExe`. Compile symbols: `DEV`/`DEBUG`
in debug configs, `FOR_MICROSOFT_STORE_ONLY` in Store configs.

**Publish profiles** (`Ui/Properties/PublishProfiles/`): `x64-net90.pubxml`,
`x64-net90-self-contained.pubxml`, `x64-net60.pubxml`, `x64-net48.pubxml`, `x64-net48-win7.pubxml`,
`x64-single.file.application.pubxml`. Only the two net9 profiles are live in CI; the net48 and net6 steps
are commented out.

**Versioning** is duplicated and stitched together by scripts:

* `Ui/AppVersion.cs` is the source of truth: `Major=1, Minor=3, Patch=0, Build=7, PreRelease="beta"`.
* `scripts/Get-Version.ps1` parses those constants and exports `BuildVersion`/`PreRelease` to
  `$GITHUB_ENV`; the `PreRelease` value is what routes CI to the `PreRelease` job instead of `StableRelease`.
* `scripts/Set-AssemblyVersion.ps1` rewrites `<AssemblyVersion>` in `Ui.csproj` during `PreBuild`. The
  value currently committed is `1.3.0.10825` — a date-stamped build number that does **not** match
  `1.3.0.7-beta`, which is expected given the script but confusing when reading the csproj cold.
* `scripts/Set-BuildDate.ps1` stamps `AppVersion.BuildDate` in `PreBuild` and reverts it in `PostBuild`.

**Secret injection.** `Assert.cs` ships two placeholders, `===REPLACE_ME_WITH_SENTRY_IO_DEN===` and
`===REPLACE_ME_WITH_SALT===`. `scripts/Set-Secret.ps1` substitutes them before the build and reverts
afterwards; locally it reads from `C:\1Remote_Secret\*.txt` (the `PreBuild`/`PostBuild` targets in
`Ui.csproj`), and in CI from repository secrets. The fork made the CI step tolerant of a fork without
secrets — see §8.

**Installer.** `Installer/Installer.wapproj` + `Package.appxmanifest` produce an MSIX for the Microsoft
Store. The portable distribution is just the published folder zipped by the workflow — `readme.md` says
"Download the zip, unpack, and run".

**Local build.** `Invoke-Build.ps1` (vendored) + `prm.build.ps1` expose `Deps`, `Build`, `BuildInSandbox`,
`Clean`. `Resolve-MSBuild.ps1` locates MSBuild. `scripts/Test-Sandbox.ps1` drives a Windows Sandbox build.
`prm.build.ps1`'s `Deps` still installs `dotnet-6.0-sdk`.

**CI jobs** (`build-on-dev-push.yml`): `JobBuild` on `windows-latest` (checkout with
`submodules: recursive`, .NET 6 + 9, MSBuild, NuGet cache, secret substitution, version, two publishes,
two artifacts) → `StableRelease` (tag, no prerelease), `PreRelease` (tag with prerelease), `NightlyRelease`
(gated on `github.repository == '1Remote/1Remote'`, so it is inert in this fork). `close-inactive-issues.yml`
is the only other workflow.

---

## 8. Security & privacy

### What is handled well

* **Secrets are decrypted as late as possible.** `Connect` clones the protocol and decrypts the clone only
  (`SessionControlService_OpenConnection.cs:213-214`); the object in the server list stays enciphered.
* **Host identity verification exists and defaults to on.** `HostTrustService` implements TOFU with a
  SHA-256 fingerprint store at `.locality/known_hosts.json`, keyed `"kind|host:port"` where kind is `ssh`
  or `tls`. Its own doc comment records what it replaced: *"SFTP never subscribed to HostKeyReceived and
  FTP's certificate callback was the unmodified `e.Accept = true` sample — which left the password readable
  to anyone able to intercept the connection."* `TrustUnverifiedHost` is a documented per-server opt-out,
  off by default, and it also drives `AuthenticationLevel = 0 : 2` for RDP and RemoteApp.
* **Proxy failures are never silent.** `AskToFallBackToDirect` requires an explicit confirmation before a
  session that asked for a proxy goes out direct.
* **Backup restore is Zip-Slip safe** (`BackupService.ResolveTarget` requires the resolved path to stay
  under its declared root).
* **`ProxyConfig` / `WebDavConfig` keep the plaintext/ciphertext split in the property accessors**, and an
  empty password stays empty rather than becoming a block of ciphertext in the settings editor.
* **Checked arithmetic is on assembly-wide** (`CheckForOverflowUnderflow`), and the places that legitimately
  need wraparound opt out explicitly and say why — the FNV-1a loop in `ProxyTunnelPool.PreferredLocalPort`,
  the `0xAABBGGRR` packing in `AcrylicHelper.Apply` (widening each byte to `uint` first, because an alpha
  ≥ 0x80 packed as `int` goes negative and the checked conversion back to `uint` throws), the `uint`
  literals in `AxMsRdpClient09Host` `AuthenticationLevel`, and the COM HRESULTs in `WindowsShortcutFactory`
  and `CredentialPrompt`.
* **Sentry initialisation is deferred** to a background task (`UnifyTracing.Init`) and the only bulk
  telemetry is a settings snapshot in `InitOnLaunch` (theme, language, view mode, whether Hello is on,
  distributor, framework) — no server names, addresses or credentials.
* **`ExternalSecretResolver` never throws on the connect path** and trims the trailing newline CLIs emit.

### Remaining risks

1. **`UnSafeStringEncipher` is obfuscation, not encryption.** The key is `Assert.STRING_SALT`, a constant
   compiled into the binary. Anyone with the binary can decrypt any `1Remote.db` produced by it. The class
   name is candid about this, but the threat model should be explicit in user-facing docs: the database
   protects against casual inspection, not against an attacker with file access.
2. **A fork build silently falls back to a publicly known salt.** The CI step now skips substitution when a
   secret is absent, and the workflow says so in a comment: *"the encryption salt then falls back to a
   publicly known constant, so a build produced this way must not be pointed at a password store created by
   an official release."* This is correctly documented in CI but **nowhere in the app or the readme**, and
   a user downloading a fork release has no way to know.
3. **`cmd://` external secrets execute an arbitrary shell line.** `ExternalSecretResolver.Run` invokes
   `cmd.exe /c <command>` with no allow-list and no confirmation. The command string is stored in the
   database alongside the servers, which means anyone who can write the database (a shared SQLite file on a
   network share, a compromised MySQL/PgSQL data source, a malicious `.1rbak` restore, an mRemoteNG import)
   achieves code execution at connect time. Resolved secrets are then cached process-wide in a plain
   `ConcurrentDictionary<string,string>`.
4. **Pre/post-connect scripts are the same class of risk** (`ProtocolBase.RunScriptBeforeConnect` →
   `WinCmdRunner.RunFile`), and they are an intentional, long-standing feature — but the same "the database
   is now an execution vector" argument applies to any shared or remote data source.
5. **Secrets are handled as `string`, not `SecureString`.** `SecureStringHelper` exists but the connect
   path uses plain strings throughout (`pb.Password = pwdDlg.Password`), so passwords sit in the managed
   heap until GC. `PasswordPopupDialogViewModel` at least clears its own fields after use.
6. **Temporary `.rdp` files are written to `Path.GetTempPath()`** containing the full session configuration,
   and deleted on a best-effort 30 s (`ConnectRdpByMstsc`) / 10 s (`ConnectRemoteApp`) timer. A crash in
   that window leaves the file behind. `Ui/Utils/RdpFile/DataProtection.cs` exists for DPAPI-protecting the
   password field, but the file itself is world-readable within the user's temp directory.
7. **Private keys are copied to `%TEMP%`** when non-ASCII paths force it, then deleted after a 30-second
   `Thread.Sleep` (`RunnerHelper.GetStartInfo`) — again best-effort.
8. **`PasswordVaultManagerFileSystem.Add` fires and forgets**: it starts a `Task.Factory.StartNew` to
   protect and write the value, then immediately calls `Retrieve(key)` on the same key, so the read races
   the write. `Retrieve` also calls `.Result` on an async DPAPI operation, which is a sync-over-async
   deadlock risk on a UI-thread caller.
9. **`WebDavClient` sends Basic auth pre-emptively** and `WebDavConfig.IsUsable` accepts `http://` as well
   as `https://`, so a mistyped scheme puts the credentials and the whole configuration archive on the wire
   in the clear. The archive contains the SQLite database and the profile.
10. **`SecondaryVerificationHelper` writes its enabled flag to three places** (Credential Manager, `HKCU`,
    a file) and treats *any* readable value as enabled, so the weakest of the three effectively wins. Its
    `async void Init`/`SetEnabled` signatures also swallow failures.
11. **Global exception handling shows a report window and closes the app** (`Bootstrapper.OnUnhandledException`
    → `ErrorReportWindow` → `App.Close(100)`), with a narrowly scoped exemption for transient GDI+ errors
    from `WindowsFormsHost` matched on message text *and* stack-trace substrings. That heuristic is
    brittle across .NET/OS versions but errs toward reporting rather than swallowing.
12. **File permissions are checked by attempting a write** (`PermissionCheck.txt`) rather than by ACL
    inspection, which is pragmatic and correct, but the app never restricts the ACLs of the files it
    creates — the SQLite DB, profile and `.sessionlogs` inherit directory defaults.

---

## 9. Technical debt & risks

* **Static service locator coupling.** `IoC.Get<T>()` is called from viewmodels, services *and* domain
  models. `IoC.GetByType` being a mutable public delegate is the only reason tests work. This is what makes
  most of the codebase untestable without a container, and it hides the real dependency graph.
* **Reflection-based object copying.** `ProtocolBase.Update` walks the type hierarchy setting every field
  and property, and `Clone()` is a shallow `MemberwiseClone` with three hand-maintained deep copies. Adding
  a reference-typed member to any protocol silently gets aliasing behaviour unless someone remembers to
  extend `Clone`.
* **Large multi-responsibility files.** `VmFileTransmitHost.cs` (1381), `AxMsRdpClient09Host.xaml.cs` (1123),
  `RDP.cs` (1065), `ServerTreeViewModel.cs` (968), `ServerEditorPageViewModel.cs` (852),
  `TransmitTask.cs` (852), `ServerPageViewModelBase.cs` (771), `AppArgumentHelper.cs` (721).
* **Three live target frameworks, only one exercised.** `net48` and `net6` configurations and publish
  profiles still exist and shape the code (`#if NETFRAMEWORK` branches, `Microsoft.Windows.SDK.Contracts`
  reference, the `ZipArchive`-over-`FileStream` choice in `BackupService`, the `Environment.OSVersion`
  shim discussion in `AcrylicHelper`), but CI builds only net9 — so those paths can rot undetected. The
  net48 target additionally cannot compile the `net9.0-windows`-only test project.
* **Stale telemetry.** `InitOnLaunch` reports `"App start with - Net" = "6.x"` for any non-`NETFRAMEWORK`
  build, which is now always net9.
* **Native COM dependency.** RDP goes through prebuilt `lib/AxMSTSCLib.dll` / `lib/MSTSCLib.dll` interop
  assemblies checked into the repo, regenerated by `lib/BuildAxMSTSCLib.ps1` / `scripts/MSTSCLib-Maker.ps1`.
  These are binary artifacts with no provenance in-repo and hard-bind the app to Windows and to the
  behaviour of the installed MSTSC control.
* **Vendored / submoduled third-party code.** `Shawn.Utils` (three projects), `Dragablz`, `VncSharpCore`
  and `PuTTY` are submodules pointing at forks under `1Remote/` and `VShawn/`. `VncSharpCore` is
  simultaneously a submodule *and* a NuGet package (`1Remote.VncSharpCore` 1.2.1) referenced by
  `Ui.csproj` — the submodule appears unused by the build. Bundled binaries `Resources/PuTTY/putty.exe`
  and `Resources/KiTTY/kitty_portable.exe` ship inside the app and need their own patch cadence.
* **Aging pinned dependencies.** `Newtonsoft.Json` 13.0.1, `MySql.Data` 8.0.30, `SSH.NET` 2023.0.0,
  `Microsoft.NETCore.UniversalWindowsPlatform` 6.2.14. No Dependabot or Renovate configuration exists.
* **Shutdown by `Environment.Exit` timer.** Both `Bootstrapper.OnExit` and `App.Close` schedule a hard
  `Environment.Exit(1)` five seconds later, labelled "workaround". This masks whatever is failing to
  release and can truncate an in-flight config or database write.
* **`.gitignore` still contains `Backup*/`.** The fork added `!Ui/Service/Backup/` and
  `!Ui/View/Settings/Backup/` negations after two commits (`1fcadef4`, `4d7e224a`) had to rescue files git
  had silently skipped. Any future directory matching `Backup*` is still invisible to `git add`.
* **Dead / commented-out code** in `DataSourceService` (`DataSourceServiceExtend`), `ProtocolBase.Clone`,
  `DataSourceBase.Database_UpdateCredential`, and large commented blocks in the CI workflow.
* **Duplicate `using` and unused-variable warnings** — e.g. `Ui/AppInit.cs` imports
  `_1RM.Utils.PuTTY.Model` twice (lines 20-21), and `WritePermissionCheck` catches into an unused `e`.
* **`ProtocolBaseWithAddressPort.cs` has a stray Windows Forms import**
  (`using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;`), a clear IDE
  auto-import accident in a domain model.
* **Duplicated version state:** `AppVersion.cs` constants, `<AssemblyVersion>` in `Ui.csproj`, and the
  `Package.appxmanifest` version each have to be kept in step by scripts.
* **Fork identity mismatch:** the readme badges, `AppVersion.UpdateCheckUrls` and `RepositoryUrl` all point
  at `chaogei/1Remote`, but the remote is `chaogei666661/1Remote-plus`. In-app update checks will not find
  this repository's releases.

---

## 10. Fork delta vs upstream 1Remote

Everything below is new in this fork (`5b9d8441..HEAD`), with the file that proves it.

### New subsystems

| Feature | Evidence |
|---|---|
| **Working proxy support** — SOCKS4/4a/5, HTTP CONNECT, SSH jump host, via a loopback tunnel pool | `Ui/Service/ProxyService.cs`, `Ui/Utils/Proxy/{ProxyConfig,ProxyHandshake,ProxyTunnel,ProxyTunnelPool,SshJumpTunnel,SshConnectionFactory,ProxyTester,ITunnel,EProxyType}.cs`, `Ui/View/Settings/Proxy/*` |
| **Real-address preservation** for tunnelled sessions | `TunnelledFromAddress`/`RealAddress`/`RedirectThroughTunnel` in `Ui/Model/Protocol/Base/ProtocolBaseWithAddressPort.cs` |
| **Standing SSH port forwards** (local/remote/dynamic) with shared sessions and health checking | `Ui/Service/PortForwardService.cs`, `Ui/Utils/PortForward/*`, `Ui/View/Settings/PortForward/*` |
| **Host identity verification (TOFU)** for SFTP and FTPS | `Ui/Service/HostTrustService.cs`, `TransmitterFtp.OnValidateCertificate`, `TransmitterSFtp`, `ProtocolBase.TrustUnverifiedHost` |
| **Backup / restore** to a `.1rbak` archive | `Ui/Service/Backup/BackupService.cs`, `Ui/View/Settings/Backup/*` |
| **WebDAV upload of backups** | `Ui/Service/Backup/WebDavClient.cs`, `WebDavConfig.cs`, `Tests/Service/WebDav/WebDavTests.cs` |
| **External password managers at connect time** (`cmd://` references) | `Ui/Utils/ExternalSecret/ExternalSecretResolver.cs`, `Ui/Service/DataBaseService.ToUsableSecret` |
| **Terminal session recording** | `Ui/Utils/SessionRecording/SessionLogPath.cs`, `GeneralConfig.SessionLogFolder`, `AppPathHelper.SessionLogDirPath` |
| **Reachability indicator** ("is this server actually up") | `Ui/Service/ServerReachabilityService.cs`, `Ui/Utils/Reachability/ServerProbe.cs` |
| **Wake-on-LAN from a server entry** | `Ui/Utils/WakeOnLan/WakeOnLan.cs`, `MacAddress`/`CanWakeOnLan` on `ProtocolBaseWithAddressPort` |
| **Import from `~/.ssh/config`** (creating proxies for `ProxyJump`) | `Ui/Utils/SshConfig/SshConfigParser.cs`, `SshConfigImporter.cs` |
| **Send one command to several sessions** | `Ui/View/Host/SendCommand/*`, `Ui/Utils/SessionInput/{SessionTextSender,CommandSnippet}.cs` |
| **Expose PuTTY's SSH forwarding in the UI** | `Ui/Utils/PuTTY/SshPortForwardingRules.cs` |

### UI rework

* Acrylic/frosted design language: `Ui/Utils/Theme/AcrylicHelper.cs`, `AcrylicBehavior.cs`,
  `Ui/Resources/Theme/Glass.xaml`.
* Eight new acrylic-tuned themes in `Ui/Service/ThemeService.cs`.
* Font-glyph icons replaced with vector paths (`Ui/Resources/Icons/SVG.xaml`); settings, proxy, popup and
  data-view pages reworked (`ef8468ad`, `bb786ab8`, `66d09a0c`).
* `Ui/Utils/InstalledFonts.cs` for font resolution, `Ui/Utils/BulkObservableCollection.cs` for batched list
  updates.

### Correctness and performance fixes

* Windowed-mode deadlock: the `_dictLock` invariant in `SessionControlService`.
* `DeferRefresh` crash on opening the server list (`0310fa01`).
* Checked-arithmetic overflows in icon decoding (`ProtocolBase.DecodeIcon` now decodes WPF-end-to-end with
  a `DecodePixelWidth` cap rather than via a leaking GDI+ `Bitmap`), the acrylic colour packing, RDP
  `AuthenticationLevel` literals, and the shortcut/credential COM HRESULTs.
* Startup cost: Sentry moved off the startup path; `ConfigurationService.Save()` skips no-op writes;
  server viewmodels marshalled to the UI thread in one batch instead of per server
  (`DataSourceBase.GetServers`).
* Event-subscription memory leaks and `IDisposable` handling on tab items.

### Project hygiene

* `Tests/` revived and moved to net9 (`67341f8e`), then grown from ~3 files to 13.
* CI made fork-friendly: build works without upstream secrets, releases publish to the fork's own
  repository, nightly stays pinned upstream (`9a5253d6`, `c67f7c0b`, `6e9319c8`, `f07a9e5d`).
* `.gitignore` negations so `Backup` source folders are not silently dropped.
* Attribution rewritten in `readme.md`, `Ui.csproj` and `AppVersion.cs` (`d9f311b0`).

---

## 11. Recommendations

Ordered by value-per-unit-of-risk. None of these were implemented.

### P0 — cheap, high leverage

1. **Run the tests in CI and add the test project to the solution.** Add `Tests/Tests.csproj` to
   `1Remote.sln` and insert a `dotnet test Tests/Tests.csproj` step in `JobBuild` after restore. Today 101
   tests exist and nothing ever runs them; a regression in `SshConfigParser` or `ProxyConfig` would ship
   unnoticed. This is a two-file change with no production impact.
2. **Warn in-app when the build has the placeholder salt.** `Assert.STRING_SALT` still equal to
   `===REPLACE_ME_WITH_SALT===` is detectable at runtime. Surface it once on the settings/about page, and
   state the consequence in `readme.md`. The CI comment already explains the danger; users cannot see it.
3. **Reconcile the fork's identity.** `AppVersion.UpdateCheckUrls`, `RepositoryUrl` and the readme badges
   point at `chaogei/1Remote`, but this repository is `chaogei666661/1Remote-plus`, so the update check is
   broken. Pick one and make all three agree.
4. **Update `DEVELOP.md` and `prm.build.ps1`.** Both still tell a new contributor to install the .NET 6
   SDK. Document the net9 SDK, the Windows 10.0.19041 targeting pack, and
   `git submodule update --init --recursive` — the build cannot succeed without the last one, and it is
   mentioned nowhere in the docs.

### P1 — security

5. **Gate `cmd://` external secrets behind an explicit per-entry confirmation, or an allow-list of
   executables.** As written, write access to the data source is code execution. At minimum: require the
   user to approve a new command string once (the `HostTrustService` TOFU pattern already in the codebase
   fits exactly), and never auto-resolve a `cmd://` reference that arrived via `.1rbak` restore, mRemoteNG
   import or a remote data source without re-approval.
6. **Require `https://` for WebDAV, or make `http://` an explicit, warned-about opt-in.**
   `WebDavConfig.IsUsable` currently accepts both, and the payload is the entire configuration including
   the credential database.
7. **Fix the write/read race and the sync-over-async in `PasswordVaultManagerFileSystem`.** Make `Add`
   await the protect-and-write, and drop the `.Result` in `Retrieve`.
8. **Harden the temp-file paths.** Write the generated `.rdp` and the copied private key into a
   per-invocation directory with restrictive ACLs and delete deterministically in a `finally`/`Process.Exited`
   handler rather than on a sleep timer.
9. **Document the threat model.** One short section in the readme: what `1Remote.db` protects against
   (casual inspection) and what it does not (an attacker with the file and a copy of the binary), plus what
   Windows Hello verification actually gates.

### P2 — architecture and testability

10. **Shrink the `IoC.Get<T>()` surface.** Do not attempt a global rewrite. Target the domain layer first:
    `ProtocolBase.SelectedRunnerIsInternalRunner` and the `IoC.Translate` calls in `ProtocolBase` /
    `ProtocolBaseWithAddressPort` are the ones that make the model untestable. Pass the
    `ProtocolConfigurationService` in, or move the computed property to the viewmodel.
11. **Replace `ProtocolBase.Update`'s reflection walk and the shallow `Clone`** with generated or explicit
    per-type copy code, or at least add a test that reflects over every `ProtocolBase` subclass and asserts
    that every reference-typed member is deep-copied. This is a latent aliasing bug generator.
12. **Add tests for the areas where a bug is expensive:** the `Connect` ordering contract in
    `SessionControlService_OpenConnection` (proxy applied after the instance check and the pre-connect
    script), `BackupService` create/restore including a malicious `../` entry, the
    `EncryptToDatabaseLevel`/`DecryptToConnectLevel` round trip, `HostTrustService` accept/reject/changed,
    and `ProxyTunnelPool` key/port determinism. All are already free of UI dependencies.
13. **Decide the fate of `net48` and `net6`.** Either build them in CI so they cannot rot, or delete the
    configurations, publish profiles, `Microsoft.Windows.SDK.Contracts` reference and `#if NETFRAMEWORK`
    branches. Carrying an untested target is worse than either.
14. **Break up the four largest files.** `VmFileTransmitHost`, `AxMsRdpClient09Host.xaml.cs`, `RDP.cs` and
    `ServerTreeViewModel` are each doing several jobs; the existing partial-class convention already used
    for `SessionControlService` is the natural, low-risk vehicle.

### P3 — developer experience and hygiene

15. **Investigate and remove the `Environment.Exit(1)` shutdown timers.** Log what is still alive after
    `OnExit` in a debug build; the timer is currently hiding the real bug and can truncate a write.
16. **Delete or scope the `Backup*/` ignore rule** to the paths it was meant for
    (`_UpgradeReport_Files/`-style artifacts), rather than maintaining a growing list of negations.
17. **Fix the trivial smells:** the duplicate `using _1RM.Utils.PuTTY.Model;` in `AppInit.cs`, the stray
    `System.Windows.Forms.VisualStyles` import in `ProtocolBaseWithAddressPort.cs`, the unused `e` in
    `WritePermissionCheck`, and the `"Net" = "6.x"` telemetry constant that is now always wrong.
18. **Add Dependabot or Renovate** for the NuGet feed and the GitHub Actions (`actions/checkout@v3` and
    `@v4` are both in use in the same workflow; `actions/cache@v3` and `actions/create-release@v1` are
    unmaintained).
19. **Consider a single source of truth for the version.** Generate `AppVersion.cs`,
    `<AssemblyVersion>` and the appx manifest version from one file instead of three scripts that rewrite
    tracked source during the build and revert it afterwards.
20. **Resolve the `VncSharpCore` duplication** — the submodule and the `1Remote.VncSharpCore` NuGet package
    both exist, but only the package is referenced. Drop whichever is dead.
