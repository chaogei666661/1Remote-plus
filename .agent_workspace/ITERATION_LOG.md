# Iteration log

One entry per round of the loop described in `.agent_workspace/AUTO_ITERATION.md`. Newest first.

An entry says what was found, what was taken, **what was rejected and why**, and what actually landed.
The rejections are the part that saves the next round its time: do not re-propose one of them without
saying why the reason no longer holds.

---

## 2026-08-28 — SSH.NET advisory, and the same bug class in our own code

Branch `cursor/sshnet-advisory-and-hardening-8713`, off `main` at `c809ebe4` (v1.3.0.21). First round of the
release-triggered loop: `main` published v1.3.0.21, so this round started. §0 of the runbook now says that in
so many words, and the "Never" item that used to describe a parent timer says it too.

### What the research turned up

**`SSH.NET 2023.0.0` was the P0 the last round flagged, and it was worse than the advisory.**
GHSA-q939-rpr3-3284 / CVE-2026-48798 (published 2026-08-09, CVSS 7.1) is a path traversal in
`ScpClient.Download(string, DirectoryInfo)`: server-supplied names go into `Path.Combine` with no containment
check, so a malicious or MITM SCP server writes anywhere the client can. Fixed in 2026.0.0, no configuration
workaround. This app never calls `ScpClient`, so the advisory itself was not reachable — but `dotnet restore`
has been emitting NU1903 on every build, and the *reason* the package was three years old turned out to
matter much more:

| | 2023.0.0 offers | 2026.0.0 offers |
| --- | --- | --- |
| AEAD ciphers | none | `aes128/256-gcm@openssh.com`, `chacha20-poly1305@openssh.com` |
| Encrypt-then-MAC | **none** | `hmac-sha2-256/512-etm@openssh.com`, `hmac-sha1-etm` |
| Post-quantum KEX | none | `mlkem768x25519-sha256`, `sntrup761x25519-sha512` |
| Still on the wire | arcfour/128/256, blowfish-cbc, cast128-cbc, twofish-{,128,192,256}-cbc, hmac-md5{,-96}, hmac-sha{1,2-256,2-512}-96, hmac-ripemd160, ssh-dss | — |

No ETM and no AEAD means OpenSSH's strict key exchange can never engage, and strict KEX *is* the mitigation
for Terrapin (CVE-2023-48795). Every SFTP session, jump-host tunnel and standing port forward this app opened
was negotiable down to a vulnerable transcript. That is a bigger finding than the advisory that led to it.

**Following the advisory into our own code found the same bug here.** SSH.NET's fix does not reach this app,
because the app does not call their recursive download — it recurses itself, in
`TransmitTask.AddServerDirectory`, over `SftpClient` and FluentFTP. And it built every local path with
`Path.Combine(_destinationDirectoryPath, <name the server sent>)`. `Path.Combine` discards everything before
a rooted second argument and never resolves `..`, so a listing entry of `C:\Windows\System32\evil.dll` was
written exactly there and one of `..\..\Startup\x.exe` climbed out of the download folder. Double-click
preview had the same combine against `%TEMP%` and then handed the result to `ShellExecute`, and it did so
even when the task ended in `Cancel`.

**And one step further along the same path.** The browser drew names exactly as sent while `ShellExecute`
picks the program from the real extension. `invoice\u202Egnp.exe` renders as `invoiceexe.png` everywhere,
including in this list.

**Other dependencies.** `dotnet build`'s NuGet audit (mode `all`, direct + transitive) reports nothing else
on `Ui.csproj` once SSH.NET moves. `FluentFTP 51.0.0`, `Npgsql 9.0.2`, `Newtonsoft.Json 13.0.1`,
`MySql.Data 8.0.30`, `System.Data.SQLite.Core 1.0.117`, `Portable.BouncyCastle 1.9.0` — clean.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `SSH.NET` 2023.0.0 → 2026.0.0, plus tests that pin the negotiated algorithm list | Clears NU1903 and, far more importantly, gets Terrapin mitigation, AEAD and PQ key exchange onto every SSH transport the app opens |
| 2 | `DownloadPathGuard`: a downloaded file cannot leave the folder the user picked | The advisory's own bug class, in our code, reachable from any SFTP or FTP server the user connects to |
| 3 | `RemoteNameInspector`: names are shown as they are, and previewing a disguised one asks first | Completes #2 — containment stops the write going elsewhere, this stops the user starting a program they did not ask for |
| 4 | Runbook §0 and §4, and this entry | The loop trigger is a release, not a timer |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | Still the right call and still out of scope by instruction. Support for .NET 9 ends 2026-11-10. Own round, before then. |
| **Prune SSH.NET's remaining legacy algorithms** (`3des-cbc`, the CBC ciphers, `hmac-sha1`, `diffie-hellman-group1-sha1`, `ssh-rsa`) | 2026.0.0 still offers all of these, below the modern ones. Tightening the list is a two-line change and a bad idea without a UI: a fixed policy silently strands whoever has one old switch, with no escape hatch and an error message from deep inside the library. Wants an "allow legacy algorithms" toggle per server, which wants an editor page. A test now asserts the modern ciphers come *first*, which is what decides what actually gets used |
| **Reject `* ? " < > \|` in a downloaded name** | Illegal in a Win32 name but harmless — they cannot redirect a write. Refusing them in the guard would abort a whole folder download over one file the file system would have reported more accurately by itself |
| **Reject a name containing `:` as always hostile** | Kept, but narrowly: it is an alternate data stream or a drive qualifier, both of which put bytes somewhere the folder listing will not show. The cost is that a POSIX server with `2026-08-28T10:00:00.log` style names now fails the transfer early rather than mid-way; Windows could never have stored that name either |
| **Sanitise deceptive characters out of the local file name** | Considered instead of warning. Rejected: it renames the user's file behind their back, and a legitimate name with a zero-width joiner in it — some CJK and emoji sequences need one — would arrive different from what is on the server. Showing the truth and asking costs nothing and lies about nothing |
| **Warn on `invoice.pdf.exe` (double extension, no hidden characters)** | Explorer hides known extensions and so gets caught by this; this browser shows the whole name, so there is nothing hidden to reveal and the warning would fire on every `logs.tar.gz` |
| **`RemoteItem.DisplayName` for the inline rename box too** | The rename `TextBox` still binds `Name`, because renaming has to round-trip the original bytes and a spelled-out `<U+202E>` typed back would create a differently named file |
| **Per-session temp directory for double-click preview** | `SessionTempFile.CreateDirectory` already exists and would be tidier than `%TEMP%`, but the previewed file is opened by another program that owns it for an unknown time, so the directory cannot be cleaned up on any schedule this class offers. Separate problem from the traversal, which is fixed |
| **Session tab mute / read-only / lock; RDP per-monitor selection** | Carried over from the last round's list, still needs Windows |

### What landed

| Commit | |
| --- | --- |
| `1ada85e5` | `security(deps): move SSH.NET off a release with a live advisory` |
| `c2368f65` | `security(sftp): keep a download inside the folder the user picked` |
| `e9dbd2aa` | `security(sftp): show a remote file name as it really is, not as it renders` |

New tests: `Tests/Utils/Proxy/SshAlgorithmPolicyTests.cs` (9, new file),
`Tests/Utils/FileTransmit/DownloadPathGuardTests.cs` (22, new file),
`Tests/Utils/FileTransmit/RemoteNameInspectorTests.cs` (9, new file).

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` on Linux with SDK 9.0.317 —
**0 errors**, and no `NU19xx` from the restore any more. The suite cannot be *run* here; CI on
`windows-latest` is the first place it executes.

All 40 new tests were nonetheless **executed here**, against the real source files, by the §7.2 harness: a
throwaway `net9.0` MSTest project that compiles the three test files and `DownloadPathGuard.cs`,
`RemoteNameInspector.cs`, `SshConnectionFactory.cs`, `ProxyConfig.cs`, `EProxyType.cs`,
`UnSafeStringEncipher.cs` and `NotifyPropertyChangedBase.cs` by absolute path, with a `TestInit` that only
seeds the string cipher. **40 passed, 0 failed.** Nothing had to be excluded.

One of the 40 is weaker than it looks and is labelled as such in its own doc comment:
`ADestinationThatIsTheRootOfADriveStillAcceptsItsOwnChildren` covers a Windows-only quirk — `D:` is
drive-relative, so `Path.GetFullPath` answers with that drive's working directory instead of its root and
`IsContained` would reject every file of a transfer downloaded to a drive root. Removing the fix and
re-running still passes on Linux, because `D:` is an ordinary relative name there. CI is where that case
means anything.

Two things that do check the checks:

- The algorithm tests were re-run with the package reference pinned back to 2023.0.0: **7 of the 9 fail.**
  They are a real guard, not a restatement of whatever the library happens to do.
- Two path-guard cases failed on the first run — `/etc/cron.d/x` and `\\attacker\share\evil.exe` were
  asserted to be *refused* and are in fact *re-rooted* under the destination. The guard was right and the
  assertions were wrong: the caller strips a parent prefix off an entry's full remote path and so produces a
  leading separator legitimately, which cannot be told apart from an absolute path. Both tests now assert
  containment, which is the property that actually matters.

Not executed anywhere: the XAML. `FileTransmitHost.xaml`'s `TbName` binding moving from `Name` to
`DisplayName`, and the two new `MessageBoxHelper.Confirm` calls, were checked by reading. See the pull
request for the manual steps.

### For the next round

1. **.NET 10.** Support for .NET 9 ends 2026-11-10. Own round. This is now the oldest outstanding item.
2. An "allow legacy SSH algorithms" per-server toggle, which would let the remaining CBC / SHA-1 /
   `diffie-hellman-group1-sha1` set be pruned from the default.
3. Session tab mute / read-only / lock, and RDP per-monitor selection for full-screen multi-monitor — both
   still waiting on a human at a Windows keyboard.
4. The upload direction of `TransmitTask` was not touched. It builds *remote* paths from *local* names, which
   is the mirror image and a much smaller problem, but nobody has looked at it closely.

---

## 2026-08-28 — parent merge of both cloud-agent branches

Parent agent (`bc-33912fb3-71cf-4f51-90e2-b63447466687`) launched two cloud agents with
`model=claude-opus-5-thinking-high-fast`, `environment=cloud`:

| Agent | Branch | PR |
| --- | --- | --- |
| Enterprise audit | `cursor/enterprise-audit-hardening-ac60` | #7 |
| Research + auto-iteration | `cursor/research-driven-feature-expansion-6a7b` | #6 |

Merged locally onto `cursor/merge-enterprise-and-research-6687` (PR #8) with no conflicts. Overlap was
only README pair + `en-us.xaml` / `zh-cn.xaml`; language keys match 1:1. `Ui/AppVersion.cs` still
`Build = 20`.

Parent verification: `dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` on Linux
with SDK 9.0.317 — **0 errors**. Feature branches are deleted after this lands on `main`.

---

## 2026-08-28 — research-driven feature expansion

Branch `cursor/research-driven-feature-expansion-6a7b`, off `main` at `1987b786` (v1.3.0.20).

### What the research turned up

**Upstreams.** `chaogei/1Remote-Plus` is 7 commits ahead of this fork's `main`, and all seven are the
automatic release plumbing this fork already has (`chore(release)`, `ci: bump version and publish`, plus
four bare `Model:` commits). Nothing to port. `1Remote/1Remote` has still not shipped a stable 1.3: the
public test build is `1.3.0.0-beta-net9`, nightlies through June 2026, stable users are on 1.2.1.

**.NET.** `.NET 9 leaves support on 2026-11-10` — the same day as .NET 8 — and .NET 10 is the LTS that
replaces both, supported to November 2028. `Ui.csproj` targets `net9.0-windows10.0.19041.0`. This is the
single most consequential thing found this round and it is not a feature. WPF on .NET 10 also brings
Fluent styling for more controls, XAML parsing performance, and a clipboard API that replaces the
`BinaryFormatter` paths .NET 9 obsoleted.

**OpenSSH.** 10.x removed DSA outright, moved agent sockets from `/tmp` to `~/.ssh/agent`, added the IANA
codepoints and the `query` extension for agent forwarding, and added `Match version` and `RefuseConnection`
to the client config. For a Windows client the config-file surface is what matters, and the gap that
mattered was older and duller than any of it: `Include`, which OpenSSH has had since 7.3 and which this
app did not read at all.

**RDP identity.** Microsoft Entra authentication for RDP (`enablerdsaadauth:i:1`, "Use a web account to
sign in to the remote computer") is the direction the whole field is moving: Royal TS V26 added it to both
its FreeRDP and ActiveX plugins this year, along with RD Gateway access tokens for CyberArk SIA. Passkeys
and FIDO2 work at *session initiation* but not at the remote lock screen, which is why the guidance is to
disconnect on lock rather than lock.

**Competitors.** Devolutions RDM 2026.1: MCP-driven AI automation, PowerShell Universal as an entry type,
custom dashboards, session recording in the free edition. Royal TS V26: year-based versioning, its own
FreeRDPKit, per-monitor selection for full-screen RDP, launching through Microsoft's own client process
instead of embedding the ActiveX control, a Proxmox plugin. mRemoteNG remains the free connection manager
this app competes most directly with. Tabby has no read-only pane lock, so that idea is a genuine gap
rather than a copy.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `~/.ssh/config`: follow `Include`, apply pattern blocks, read decidable `Match` sections | A config split across `~/.ssh/config.d/*` imported as *nothing*, and a `Host *` block carrying `User deploy` was dropped, so servers arrived set to log in as the local account. Pure logic, fully testable from here |
| 2 | Connection quality: grade the reachability dot on latency, jitter and loss | The dot answered "is the port open", which is the least interesting thing about a link you are about to type into. Costs nothing extra on the wire — the window is built from the sweep that was already running |
| 3 | External runner health check | A mistyped macro produced no message anywhere: runners start with `UseShellExecute = false`, so `%1RM_HOSTNAM%` reaches PuTTY verbatim and the user sees a client that opens and cannot connect |
| 4 | This runbook, this log, `scripts/Get-ResearchBriefing.ps1` | Task B |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | The right call and the most valuable thing on the list, but it is a target-framework change across `Ui`, `Tests`, three build configurations, the installer and CI, it wants Visual Studio 2026, and it cannot be smoke-tested from Linux. It does not belong bundled with three unrelated features. **Give it a round of its own, before 2026-11-10.** |
| **Migrate the UI to Avalonia** | Evaluated as instructed and rejected as instructed. The app is an RDP ActiveX host, a VNC WinForms control, Windows Hello, DPAPI, WinAPI window embedding and Dragablz tab tearing. Royal Apps is building its cross-platform client (Royal Connect, Avalonia) as a *new product* alongside Royal TS rather than as a port, which is the same conclusion from people with more resources |
| **Entra ID / passkey RDP sign-in** | Genuinely where the field is going, and reachable — `enablerdsaadauth` is an `.rdp` property and the ActiveX control exposes the same flow. But it cannot be developed, let alone verified, without a tenant and a Windows box, and getting it half right produces a connection that fails in a way the user cannot diagnose. Wants a human with an Entra tenant |
| **Multi-sample probing for the quality grade** | The obvious way to measure jitter is a burst of connects. Rejected: the existing sweep is already careful not to look like a port scan, and a burst per server per interval is exactly what a corporate IDS reacts to. The sliding window over the sweeps that were happening anyway gives the same three numbers for no extra traffic |
| **`Match exec`, `user`, `localuser`, `localnetwork`, `tagged`, `command`, `version`, `canonical`, `final`** | `exec` would run a command from a config file during an import. The rest cannot be answered by a program filling in a dialog rather than opening a connection. Such a section is skipped whole, so nothing is half-applied |
| **Multiple `IdentityFile` entries per host** | ssh collects them all and offers them in turn; a stored connection holds one. It takes the first, which is the one ssh would try first. Representing the list needs a model change |
| **Flagging every unknown `%TOKEN%` in a runner command line** | Would report the percent-encoding in a WinSCP session URL (`pa%25ss%3Aword`, where `25ss%3` sits between two percent signs) as a broken macro. The scan requires an underscore, which every macro the app defines has. The cost is that a typo which also loses the underscore is missed; crying wolf on a command line that works is worse |
| **Session tab mute / read-only / lock** | Real gap — Tabby does not have it either — but it lives in `TabWindowView`, `IntegrateHost` and the RDP ActiveX host, none of which can be exercised without Windows. Good candidate for a round that ends with a human at a keyboard |
| **Reduce transparency / high contrast** | `EnableAcrylic` and `AcrylicOpacity` already exist under `Options → Theme`, so the enterprise "turn the acrylic off" need is met. A true high-contrast mode means auditing every hard-coded brush in `Ui/Resources/Theme` and is its own round |

### What landed

| Commit | |
| --- | --- |
| `e4ea8636` | `feat(ssh-config): follow Include and apply pattern blocks when importing` |
| `ac7cbff8` | `feat(reachability): grade the dot on latency, jitter and loss` |
| `546f79bb` | `feat(runner): check external runners and say what is wrong before a session does` |
| `15f139a0` | `perf(runner): hold the health result instead of re-inspecting the disk on every read` |

Plus this file, `AUTO_ITERATION.md` and `scripts/Get-ResearchBriefing.ps1`.

New tests: `Tests/Utils/SshConfig/SshConfigParserTests.cs` grew from 14 cases to 33, including
`Include` cases that write real temp directories; `Tests/Utils/Reachability/ConnectionQualityTrackerTests.cs`
(13, new file); `Tests/Model/ProtocolRunner/RunnerHealthTests.cs` (13, new file).

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` — 0 errors. The suite could not
be *run*: the test host needs `Microsoft.WindowsDesktop.App`, which has no Linux build. CI on
`windows-latest` is the first place it executes.

The new tests were nonetheless **executed here**, against the real sources, by the harness now written up
in §7.2 of the runbook: a throwaway `net9.0` MSTest project that compiles the three test files and
`SshConfigParser.cs`, `ConnectionQuality.cs`, `RunnerHealth.cs` by absolute path, with a no-op `TestInit`
and a stub `ExternalRunner` so the WPF-only adapter overload compiles.

**56 passed, 0 failed.** The three excluded are the pre-existing `SshConfigImporter` cases
(`ImportingBuildsServersAndTheJumpHostTheyNeed`, `ReimportingReusesAJumpHostAlreadyOnThePage`,
`TheAliasBecomesTheDisplayNameAndTheHostNameTheAddress`), which construct real `ProtocolBase` servers and
so need the app; this round did not touch the importer. An earlier, weaker version of this check — the
assertions retyped into a console app — is what caught the grading arithmetic being off in the first draft
of the quality thresholds. `scripts/Get-ResearchBriefing.ps1` was run under PowerShell 7.4.

Not executed anywhere: the XAML. The dot's colour ramp in `ServerLineItem.xaml` and the warning panel in
`ExternalRunnerSettings.xaml` / `ExternalSshRunnerSettings.xaml` were checked by reading. See the pull
request for the manual steps.

### For the next round

1. **.NET 10.** Support for .NET 9 ends 2026-11-10. Own round.
2. `SSH.NET 2023.0.0` is the oldest direct dependency by some distance. Check it against the advisory
   database and against what the 2024/2025 releases changed.
3. Session tab mute / read-only / lock, if a human will be available to try it.
4. RDP: per-monitor selection for full-screen multi-monitor, which Royal TS added this year and which the
   ActiveX control has supported for a long time.
