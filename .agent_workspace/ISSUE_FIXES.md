# Static analysis follow-up — implementation checklist

Branch: `cursor/implement-static-analysis-fixes-f12f`, off `main`.
Source of the item list: `.agent_workspace/PROJECT_ANALYSIS.md` on `origin/cursor/project-analysis-report-df00`.

**Build/test status.** The whole solution targets `net9.0-windows10.0.19041.0`. Everything here compiles
(`dotnet build -p:EnableWindowsTargeting=true`, 0 errors, on Linux with the .NET 9 SDK). The tests cannot be
*run* here: the test host needs the `Microsoft.WindowsDesktop.App` runtime, which does not exist for Linux.
They run in CI, where the job is on `windows-latest` — that is what item 1 adds.

| # | Item | Status |
|---|------|--------|
| 1 | Tests in the solution + CI | DONE |
| 2 | Placeholder salt warning | DONE |
| 3 | Fork identity | DONE |
| 4 | Docs / deps for net9 | DONE |
| 5 | Gate `cmd://` external secrets (TOFU) | DONE |
| 6 | WebDAV HTTPS | DONE |
| 7 | `PasswordVaultManagerFileSystem` race / sync-over-async | DONE |
| 8 | Temp `.rdp` and private-key copies | DONE |
| 9 | Threat model docs | DONE |
| 10 | Shrink domain `IoC.Get` | DONE |
| 11 | `Clone` / `Update` aliasing | DONE |
| 12 | New leaf tests | DONE |
| 13 | net48 / net6 fate | DONE |
| 14 | Split the four largest files | PARTIAL (2 of 4, as allowed) |
| 15 | `Environment.Exit` timer | DONE |
| 16 | `.gitignore Backup*/` | DONE |
| 17 | Trivial smells | DONE |
| 18 | Dependabot | DONE |
| 19 | Version source of truth | DONE |
| 20 | VncSharpCore submodule vs NuGet | DONE |

---

## P0

### 1. Tests in the solution + CI — DONE

- `1Remote.sln`: `Tests/Tests.csproj` added under a `Tests` solution folder with configurations for
  Debug/Release/StoreDebug/StoreRelease across Any CPU/x64/x86. The `ReleaseNet48` and `ReleaseNet6`
  configurations map but do not build it, because the test project only targets net9.
- `.github/workflows/build-on-dev-push.yml`: `dotnet restore Tests/Tests.csproj` (the existing restore is an
  msbuild restore of `Ui` only) followed by `dotnet test Tests/Tests.csproj -c Release`, placed after restore
  and before the net9 publish. Parent follow-up: the workflow now also runs on `pull_request` to `main` /
  `master`. Without that, the new test step would only execute after merge, because `on.push` is limited to
  those branches. Publish/upload on PRs is skipped so a review build is restore + test only.
- Same file: `actions/cache@v3` → `@v4`, and `actions/checkout@v3` → `@v4` in `JobBuild`, `StableRelease` and
  `PreRelease`. Nothing else in the workflow was rewritten.
- `Tests/Tests.csproj` had been left on net6 after `Ui` moved to net9, which stopped it building at all; it
  now tracks `Ui`.

### 2. Placeholder salt warning — DONE

- `Ui/Assert.cs`: `IsUsingPlaceholderSalt`, true while `STRING_SALT` is still `===REPLACE_ME_WITH_SALT===`.
  The constant itself is untouched — CI substitutes it.
- `Ui/AppInit.cs`: `WarnAboutPlaceholderSaltOnce()` at the end of `InitOnLaunch`, posted to the UI thread so
  it does not hold up startup. One shot, remembered in
  `Ui/Service/ConfigurationService.cs` → `EngagementSettings.PlaceholderSaltWarned`.
- `Ui/View/AboutPageViewModel.cs` + `Ui/View/AboutPageView.xaml`: a banner that stays visible on the About
  page for as long as the build carries the placeholder. Visibility is set through a style trigger, not a
  local value, or the trigger could never win.
- `readme.md`: the same warning in the new security notes section — a placeholder-salt build must not be
  pointed at a password store written by an official release.

### 3. Fork identity — DONE

`https://github.com/chaogei/1Remote` → `https://github.com/chaogei666661/1Remote-plus` in: `readme.md`
(badges and links), `CODE_OF_CONDUCT.md`, `Ui/Ui.csproj` (`RepositoryUrl`), `Ui/AppVersion.cs` (update
check), `Ui/Service/TaskTrayService.cs`, `Ui/View/Settings/ProtocolConfig/ExternalRunnerSettingsViewModel.cs`
(issue template), `Ui/View/RequestRatingView.xaml`, `Ui/View/Guidance/Intro.xaml`,
`Ui/View/ErrorReport/ErrorReportWindow.xaml{,.cs}`, `Ui/View/AboutPageView.xaml`, and
`Tests/View/AboutPageUpdateCheckTests.cs`. Upstream `1Remote/1Remote` links (nightly release, credits) were
left alone. `rg "chaogei/1Remote"` now returns nothing.

### 4. Docs / deps for net9 — DONE

- `DEVELOP.md`: .NET 9 SDK, Windows 10 SDK / targeting pack `10.0.19041.0`, VS 2022, and
  `git submodule update --init --recursive`, plus sections on submodules, target frameworks, tests,
  versioning and the security-relevant behaviour.
- `prm.build.ps1`: the `Deps` task installs `dotnet-9.0-sdk` and `windows-sdk-10-version-2004-all`;
  `dotnet-6.0-sdk` stays for the legacy configurations.

## P1 — security

### 5. `cmd://` external secrets are gated (TOFU) — DONE

- `Ui/Utils/ExternalSecret/ExternalSecretTrustStore.cs` (new): approvals keyed by a hash of
  machine + user + the exact command string, stored in `.locality/known_commands.json`
  (`Ui/Service/AppPathHelper.cs` → `ExternalSecretTrustJsonPath`). Because the hash includes the machine and
  the user, a store that arrives through a restored `.1rbak`, a synced locality folder or a shared data
  source approves nothing.
- `Ui/Utils/ExternalSecret/ExternalSecretResolver.cs`: `Resolve` asks
  `ExternalSecretTrustStore.EnsureApproved` **before** starting a process; an unapproved command yields an
  empty secret and a log line, and is only prompted for once per run.
- **Documented policy:** pressing *Test* in the editor records the approval, because the user is looking at
  the command they just typed when they press it. A failing test approves nothing.
- Test hook: `ExternalSecretTrustStore.AutoApproveForTests`, set in `Tests/TestInit.cs`, so the existing
  `Resolve("cmd://echo hunter2")` tests keep their intent. `Tests/Utils/ExternalSecret/ExternalSecretTrustStoreTests.cs`
  turns it back off and covers: unapproved command does not run (observed through a marker file it would have
  written), one prompt per run, approval persisted and reused, another machine's store, exact-string
  matching, Test-approves, failing-Test-does-not.
- Strings: `external_secret_trust_title`, `external_secret_trust_new` in `en-us.xaml` and `zh-cn.xaml`.

### 6. WebDAV requires HTTPS — DONE

- `Ui/Service/Backup/WebDavConfig.cs`: `IsUsable` now needs `https://`, or `http://` **and** the new
  `AllowInsecureHttp` opt-in (default false). Derived `IsHttps` / `IsPlainHttp` / `IsInsecure`.
- `Ui/View/Settings/Backup/BackupSettingView{.xaml,Model.cs}`: the opt-in and a warning that plain HTTP sends
  the Basic auth header and the whole configuration archive in the clear appear only when the URL starts with
  `http://`. Strings `webdav_allow_http`, `webdav_http_warning`.
- `Tests/Service/WebDav/WebDavTests.cs` updated to the new contract, plus tests for the opt-in, for the
  opt-in not rescuing a non-HTTP address, and for a profile written before the option existed.

### 7. `PasswordVaultManagerFileSystem` — DONE

- `Ui/Utils/WindowsSdk/DataProtectionForLocal.cs`: `.AsTask().ConfigureAwait(false)` on the DPAPI
  protect/unprotect awaits, so nothing resumes on a captured UI context.
- `Ui/Utils/WindowsSdk/PasswordVaultManager/PasswordVaultManagerFileSystem.cs`: real `AddAsync`/`RetrieveAsync`.
  `Add` now protects and writes **before** returning — it used to fire a `Task.Factory.StartNew` and then read
  the file straight back. The synchronous wrappers exist only for the `IPasswordManager` callers that need
  them and block off the UI thread via `Task.Run(...).GetAwaiter().GetResult()`.
- `Ui/Service/SecondaryVerificationHelper.cs` uses the async pair.

### 8. Temp `.rdp` and private-key copies — DONE

- `Ui/Utils/SessionTempFile.cs` (new): a per-invocation directory under the user's temp, an ACL restricted to
  the current user on Windows (guarded by an OS check so it still compiles elsewhere), deletion hooked to
  `Process.Exited`, and a timed delete kept only as a backstop.
- `Ui/Service/SessionControlService_OpenConnection.cs`: `ConnectRdpByMstsc` writes the `.rdp` into such a
  directory and deletes it when `mstsc.exe` exits, with `TryDelete` in the failure paths. `ConnectRemoteApp`
  keeps a timed delete, because the `cmd.exe` it starts exits immediately and there is no process to hook.
- `Ui/Model/ProtocolRunner/RunnerHelper.cs`: the private-key copy goes into its own directory, deleted when
  the runner process exits (`RunWithoutHosting`) or on a timer when a host owns the process.
- `Ui/Model/ProtocolRunner/Default/PuttyRunner.cs`: same treatment, replacing `Path.GetTempPath()` plus a
  `Thread.Sleep` delete.

### 9. Threat model docs — DONE

`readme.md`, new security notes section: `1Remote.db` is obfuscated with a build-time salt and is not
per-user encryption; what Windows Hello does and does not gate; `cmd://` shells out and is approved per
machine; WebDAV needs HTTPS; SFTP/FTPS host identity is verified and remembered.

## P2

### 10. Domain `IoC.Get` — DONE

`Ui/Model/Protocol/Base/ProtocolBase.cs`: `SelectedRunnerIsInternalRunner` is gone; in its place
`IsSelectedRunnerInternal(ProtocolConfigurationService)` takes the service as an argument, and the
`SelectedRunnerName` setter no longer resolves anything. The no-argument property XAML binds to now lives on
`Ui/View/Editor/Forms/ProtocolBaseFormViewModel.cs`, which already has the service and re-raises the property
when the runner changes. Bindings updated in `SshFormView.xaml`, `SftpFormView.xaml`, `FtpFormView.xaml`.

### 11. `Clone` aliasing — DONE

`Update`'s reflection was left alone, as instructed. Audit result and fixes:

- `Ui/Model/Protocol/Base/ProtocolBase.cs`: `AlternateCredentials` is declared on
  `ProtocolBaseWithAddressPort`, but `Clone` only copied it when the object was a
  `ProtocolBaseWithAddressPortUserPwd` — so **Telnet handed its clone the same collection object**. Fixed,
  and the summary now says which members are deliberately still shared (`DataSource`, strings, value types)
  and that a subclass adding a mutable member must override.
- `Ui/Model/Protocol/AppArgument.cs`: `Clone` assigned the copied dictionary through the `Selections`
  setter, and that setter re-picks `Value` for a selection argument — cloning silently discarded the chosen
  option. It now writes the field. The copy also inherited the original's lazily built
  `CmdSelectArgumentFile` command, which captures the instance that created it, so the file the user picked
  in the copy was written into the original; the command is reset on clone.
- Everything else checked and found sound: `LocalApp` already deep-copies its `ArgumentList`, `RDP` has no
  mutable reference members of its own (its enums and `int?`s are value types, `_autoSetting` is commented
  out), `Serial`'s arrays are rebuilt per read, `Credential` holds only strings plus the shared
  `DataSource`.
- `Tests/Model/Protocol/ProtocolCloneTests.cs` (new): a reflection sweep over every `ProtocolBase` subclass
  that seeds each list-like property and then clears it on the clone, plus targeted tests for RDP/SSH/SFTP
  tags, tree nodes and alternate credentials (by value, not just the list), the Telnet regression, app
  argument lists, and the two `AppArgument` fixes.

### 12. New leaf tests — DONE

- `Tests/Service/Backup/BackupServiceTests.cs`: round trip; a `locality/../../x` entry is not written;
  the same with backslashes; an absolute path; an entry for a file the app does not own; a zip without the
  manifest; a non-zip. `AppPathHelper.Instance` is pointed at a temp folder for the duration.
- `Tests/Service/HostTrustServiceTests.cs`: accept, reject, silent second connection, changed fingerprint
  goes back to the user, accepting a change replaces the old one, trust is per host/port/kind, it survives a
  restart, an unreadable store falls back to asking, and the fingerprint is stable.
- `Tests/Service/DataServiceTests.cs`: cipher round trip, `EncryptOnce` idempotence, plain text passes
  through, a whole server through `EncryptToDatabaseLevel`/`DecryptToConnectLevel` including alternate
  credentials and the RDP gateway password, only `Secret` app arguments are enciphered, blanks stay blank.
- `ProxyConfig` key/port determinism was already covered by `Tests/Utils/Proxy/ProxyConfigTests.cs`.
- No Connect-ordering test: it needs the container and would be flaky, so it was deliberately skipped rather
  than faked.

### 13. net48 / net6 — DONE

Nothing deleted. `DEVELOP.md` states that net9 is the only CI-built target and that net6/net48 are
unmaintained legacy configurations; `Ui/Ui.csproj` carries a short comment next to those TFM conditions.
`Ui/AppInit.cs` no longer reports `"App start with - Net" = "6.x"` for every non-`NETFRAMEWORK` build — it
reports `Environment.Version`, which needs no editing the next time the target moves.

### 14. Split the largest files — PARTIAL (two of the four)

Both splits are partial-class only, no behaviour change, public API identical, and both follow the existing
`SessionControlService` convention.

- `Ui/Model/Protocol/RDP.cs` 1065 → 570 lines, with `RDP.AdditionalSettings.cs` (the free-text settings box
  for the ActiveX control: the accepted key list, the parser, and the reflection that applies it) and
  `RDP.RdpFile.cs` (`ToRdpConfig` / `FromRdpConfig`).
- `Ui/View/Host/ProtocolHosts/AxMsRdpClient09Host.xaml.cs` 1123 → 613 lines, with
  `AxMsRdpClient09Host.Settings.cs` (server info, connection bar, redirection, display, performance,
  gateway). What is left is the part that does something: create the control, connect, resize, tear down.

Not split, and why: `VmFileTransmitHost.cs` (1381) is one viewmodel whose transfer queue, remote-directory
state and command surface all touch the same fields, so any seam would be arbitrary rather than a
responsibility; `ServerTreeViewModel.cs` (968) is similar around its selection and drag-drop state. Both are
worth doing but need a real look at their state, not a mechanical cut.

## P3

### 15. `Environment.Exit` timer — DONE

`Ui/Service/ShutdownWatchdog.cs` (new) replaces the two copies of "sleep five seconds then `Environment.Exit(1)`"
in `Ui/Bootstrapper.cs` (`OnExit`) and `Ui/App.xaml.cs` (`Close`). It is armed once — quitting through
`App.Close` and then falling into `OnExit` gives the teardown one deadline, not two — and before it pulls the
plug it logs the sessions still in `SessionControlService.ConnectionId2Hosts`, the OS thread count, and the
windows still open (read through the dispatcher with a one-second deadline, so a UI thread that never answers
is itself reported). A shutdown the user asked for exits with the code that was asked for; anything else
still exits 1. The failsafe was kept, with a comment saying why: teardown reaches into an ActiveX control,
child processes and the tray icon, any of which can leave a foreground thread behind, and the next launch
would then find the old instance's named pipe and quietly exit.

### 16. `.gitignore Backup*/` — DONE

Replaced with `/Backup/` and `/Backup[0-9]*/`, anchored to the repository root where the Visual Studio
converter writes them, and the two negations were dropped. This was not theoretical: while implementing item
12, `git add Tests` silently skipped `Tests/Service/Backup/BackupServiceTests.cs` under the old rule — git
never looks inside a directory it has excluded — and the file only became committable after this change.

### 17. Trivial smells — DONE

Duplicate `using _1RM.Utils.PuTTY.Model;` in `Ui/AppInit.cs`; the stray
`using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;` in
`ProtocolBaseWithAddressPort.cs`; the unused catch variable in `WritePermissionCheck`; the `"6.x"` telemetry
(item 13).

### 18. Dependabot — DONE

`.github/dependabot.yml`: nuget for `/Ui` and `/Tests`, plus github-actions, monthly, five open PRs each.

### 19. Version source of truth — DONE

`Ui/AppVersion.cs` stays the human-edited source. `Ui/Ui.csproj` says next to `<AssemblyVersion>` that the
pre-build `scripts/Set-AssemblyVersion.ps1` overwrites it from `AppVersion.cs`, and `DEVELOP.md` has a
one-line pointer. No new versioning system.

### 20. VncSharpCore — DONE

The submodule is in `1Remote.sln` but `Ui.csproj` takes VNC from the `1Remote.VncSharpCore` NuGet package.
Nothing deleted; the dual situation is documented in `.gitmodules` and `DEVELOP.md` — the package is what
`Ui` builds against, the submodule is for building and comparing a patched control locally.

---

## Remaining risk

- **Nothing here has been executed.** Compilation is verified; behaviour is not. The riskiest changes to
  watch on a Windows run are the temp-file lifetime in item 8 (a `.rdp` deleted too eagerly would break
  `mstsc` startup — the delete is hooked to process exit, not to launch) and the `cmd://` gate in item 5,
  which by design refuses to run something that used to run.
- The `cmd://` gate is a deliberate behaviour change: existing users with a `cmd://` password will be asked
  to approve it once per machine, and a connect that is never answered fails the login instead of hanging.
- WebDAV over plain HTTP stops working until the user ticks the new box. That is the point of item 6, but it
  will look like a regression to anyone using it.
- The two file splits move code verbatim; the risk is limited to something private having been left on the
  wrong side of the cut, which the compiler would have caught.
