# Iteration log

One entry per round of the loop described in `.agent_workspace/AUTO_ITERATION.md`. Newest first.

An entry says what was found, what was taken, **what was rejected and why**, and what actually landed.
The rejections are the part that saves the next round its time: do not re-propose one of them without
saying why the reason no longer holds.

---

## 2026-08-28 — a watch the parent can poll, and the upload half of the transfer scan

Branch `cursor/release-watch-and-upload-scan-2862`, off `main` at `170f6386` (v1.3.0.22). Second round of the
release-triggered loop, and the first one started from the watch this round is about: the previous round's
GitHub CI subscription reported `deliveryCount=0` for a run that did publish a release, nothing woke the
parent, and the round opened about eight minutes late.

### What the research turned up

**Dependencies are clean.** `dotnet restore Ui/Ui.csproj` emits no `NU19xx` at all now that SSH.NET has
moved. Twenty-one direct package references, nothing outstanding.

**Neither upstream has anything to port.** `chaogei/1Remote-Plus` is 7 commits ahead and every one of them is
release plumbing this fork already has plus four bare `Model:` commits — the same answer as two rounds ago.
`1Remote/1Remote` is **0** commits ahead of `origin/main`; this fork is 174 ahead of it.

**The upload direction, which the last round wrote down as unexamined, does not have the traversal hole it
was suspected of** — and has two worse things instead. `ServerPathCombine(_destinationDirectoryPath, fi.Name)`
cannot be steered anywhere: a Win32 name holds no separator, and `RemoveFirst` is prefix-only
(`if (!value.StartsWith(find)) return value;`), so no local name can aim the remote path elsewhere. What the
walk *did* do was follow every directory the platform listed:

| | Download (already correct) | Upload (before this round) |
| --- | --- | --- |
| Symlink / junction directory | `item.IsDirectory && !item.IsSymlink` — listed, not descended | descended |
| Chosen folder with no parent | n/a | `topDirectory.Parent!.FullName` → `NullReferenceException` |

A link pointing at an ancestor makes the walk re-enter the same tree at a longer path each time. Measured:
a three-file folder with one such link produced **124 phantom entries and 376-character paths** before the
Unix symlink counter stopped it; a Windows junction has no counter, so the ceiling there is the path length.
Whatever ends it arrives as an exception into a `catch` that only logs — so the transfer sits in `Scanning`
and then uploads nothing, silently. A link pointing *elsewhere* — `AppData`, a mapped drive, all of `C:\` —
was uploaded to the remote server along with the folder the user picked.

**And the shared half of the scan was quietly losing files in both directions.** `AddTransmitItem`'s duplicate
check compared both paths with `StringComparison.CurrentCultureIgnoreCase`. That is a *linguistic*
comparison. Measured on this box, it answers "equal" for:

| | |
| --- | --- |
| `file.txt` vs `\uFB01le.txt` | the fi ligature reads as "fi" |
| `café.txt` precomposed vs `cafe\u0301.txt` decomposed | **macOS writes decomposed names** |
| `note.txt` vs `note\u200B.txt` | zero-width space |
| `note.txt` vs `note\u00AD.txt` | soft hyphen |

Each of those is a second, real file on the server. It was never queued, never listed, never mentioned. The
same check is a full scan of the pending queue per item: 1 000 files 43 ms, 5 000 files 0.6 s, 20 000 files
**9.8 s**, 50 000 files **59 s**, all before a byte moves.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `scripts/watch-release-iteration.sh` + runbook §0 | The loop stops being late because a subscription did not fire |
| 2 | `LocalUploadScan`: an upload lists a folder link but does not walk into it | The mirror of a guard the download side has always had; a hang and a disclosure, from links an ordinary Windows profile is full of |
| 3 | `TransmitItemKeySet`: the duplicate check stops eating files | Silent data loss in both directions, plus 2000× on the scan |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | Still the oldest outstanding item, still out of scope by instruction, support for .NET 9 ends 2026-11-10. Own round |
| **Write the watch as `scripts/Watch-ReleaseIteration.ps1`**, matching the rest of `scripts/` | The agent VMs have `bash`, `gh` and `jq` and do **not** have `pwsh`. A PowerShell helper the parent cannot execute is worse than an inconsistent file extension. The runbook says so where the script is described |
| **Have the watch open the round itself**, or add a workflow that does | Runbook §4: nothing in this repository summons agents. The script only answers a question; the parent decides. It is also why it makes no write call of any kind |
| **Skip *file* links on upload too** | Reading through a file link is what copying that file means. It cannot loop, and it cannot pull in anything past the one file already in the folder listing the user is looking at. Refusing it would silently lose files from any project that uses symlinks |
| **Not create the linked folder on the far side at all** | The download side lists a symlink directory as an entry and creates it empty; doing the same on upload keeps the tree shape and keeps the two directions describable in one sentence. The panel names them, so nobody finds out from the far end |
| **Follow a link the user *chose* explicitly** (`cp -H`) | Would be defensible, and was rejected for being a second rule to hold in your head for no gain: nobody drags a junction onto the panel on purpose |
| **Make the duplicate check ordinal, not ordinal-ignore-case** | A Windows path differing only in case is the same file, and de-duplicating it was the one thing the old check got right. `Makefile` and `makefile` from a Linux server still collapse into one — but Windows could not have stored both anyway, and turning that into a hard failure is a separate decision with a different blast radius |
| **Join the two paths into one hash key** | A POSIX name may contain a newline, or any other character that looks like a safe separator, so one pair's key can be spelled by a different pair. That is the same silent drop in a new place; the key is a tuple. There is a test for it |
| **Report the case-collision files that the duplicate check still swallows** | Real, but it needs a message, a place to put it, and a decision about whether the transfer should stop. Worth its own change rather than a rider on this one |
| **The `~TransmitTask()` finaliser calls `TryCancel()`, which raises `PropertyChanged` and invokes `OnTaskEnd` on the finaliser thread** | Genuinely wrong — an exception out of a finaliser takes the process with it, without a dialog. Left alone: by the time it runs the handlers have unsubscribed themselves and the bindings are gone, so it is latent rather than reachable, and removing a finaliser deserves more than a drive-by. Written down here so the next round does not have to find it again |
| **Session tab mute / read-only / lock; RDP per-monitor selection** | Carried over again. Still needs a human at a Windows keyboard |

### What landed

| Commit | |
| --- | --- |
| `a3bb7add` | `ops: let the parent decide in one read-only command whether to open a round` |
| `1e73b5bd` | `security(sftp): an upload no longer walks through a folder link` |
| `b0b674eb` | `fix(transfer): stop the scan dropping files it decided were the same word` |

New tests: `Tests/Utils/FileTransmit/LocalUploadScanTests.cs` (15, new file),
`Tests/Utils/FileTransmit/TransmitItemKeySetTests.cs` (12, new file).

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` on Linux with SDK 9.0.317 —
**0 errors**, and 118 warnings, which is exactly the count `main` builds with. The suite cannot be *run*
here; CI on `windows-latest` is the first place it executes.

All 27 new tests were **executed here** by the §7.2 harness, together with the 22 `DownloadPathGuard` cases
that share the file under test: a throwaway `net9.0` MSTest project compiling `LocalUploadScan.cs`,
`TransmitItemKeySet.cs`, `DownloadPathGuard.cs` and the three test files by absolute path, with a no-op
`TestInit`. **49 passed, 0 failed.** Nothing had to be excluded. The link cases build real symlinks under
`/tmp`, so what is asserted is what the file system does and not what a string says.

Both were checked against the bug they claim to catch:

- With `if (IsLink(sub))` forced to `false`, three of the link tests fail and the ancestor-link test fails
  with 124 entries where 4 are expected.
- With the comparer put back to `CurrentCultureIgnoreCase`, five of the twelve key-set tests fail — the four
  collisions above, and the 20 000-item case, which takes 8 seconds.

`scripts/watch-release-iteration.sh` was driven through **22 cases** by a stubbed `gh`, covering every
decision it can reach: green-release-fires-once, `--peek` not consuming, a failed run, a cancelled run,
newest-run-wins over list order, a run still in progress, no run, no release, a live branch blocking, a stale
branch not blocking, `--stale-hours 0`, drafts and pre-releases being skipped, newest-`publishedAt`-wins over
list order, `--seed`, a `gh` failure reading as 2 rather than 0, `--json` validity, `--help`, and an unknown
option. **22 passed, 0 failed.** It was also run for real against this repository, where it correctly
reported `10` on a fresh state, `0` on the next poll, and a stale `cursor/*` branch as not blocking. Neither
harness is in the repository.

Not executed anywhere: the one line of view-model wiring that turns `TransmitTask.LinksNotFollowed` into an
`IoMessage`. See the pull request for the manual steps.

### For the next round

1. **.NET 10.** Support for .NET 9 ends 2026-11-10. Own round. Still the oldest outstanding item.
2. The `~TransmitTask()` finaliser (see the rejection table) — raising `PropertyChanged` and invoking
   `OnTaskEnd` from the finaliser thread is a process-killer waiting for the right timing.
3. Two downloaded names that differ only in case still collapse into one, silently. Windows cannot store
   both; saying so is the missing part.
4. An "allow legacy SSH algorithms" per-server toggle, which would let the remaining CBC / SHA-1 set be
   pruned from the default.
5. Session tab mute / read-only / lock, and RDP per-monitor selection — both still waiting on a human at a
   Windows keyboard.

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
