# Iteration log

One entry per round of the loop described in `.agent_workspace/AUTO_ITERATION.md`. Newest first.

An entry says what was found, what was taken, **what was rejected and why**, and what actually landed.
The rejections are the part that saves the next round its time: do not re-propose one of them without
saying why the reason no longer holds.

---

## 2026-08-29 — environment bring-up, then three sub-rounds of windowless fixes and test cover

Two things happened this round. First, the fork gained a committed Cloud Agent environment
(`.cursor/environment.json` + `.cursor/install.sh`): .NET 9 SDK, submodules, and a warm
`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true`, validated by a draft
environment build from a clean checkout. Branch `cursor/agent-env-and-orchestrator-167c`, PR #19.

Second, three small sub-rounds, each verified with the §7.2 throwaway `net9.0` harness (**34 tests, all
passing** on this Linux box) and the full-solution build (0 errors). The full MSTest suite still only runs
on `windows-latest` in CI, which is the merge gate.

Cloud subagent fan-out was **not** used: launching any `environment: cloud` subagent returns "You've used
all included Cloud Agent usage". The rounds were run one editing agent at a time, which is what §4 asks for
anyway.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `Ui/Utils/HostNaturalSort.cs` (new) + `SubTitleSortByNaturalIp` reduced to a thin `IComparer` | The address-column sort compared IPv4 octets and ports as text (`192.168.0.10` before `.2`, `:10` before `:9`), rejected every compressed IPv6 address, split IPv6 literals on their first colon, and ignored the ascending/descending flag for anything that parsed as an IP. Now: IPv4 by bytes, then IPv6 by bytes, then hostnames in natural order; numeric ports; bracketed/bare IPv6. 9 tests |
| 2 | `Tests/…/ProcessArgumentEscaperTests.cs` (new) | The guard that stops a shared-DB value (`root -proxycmd calc.exe`) from becoming extra command-line switches had no tests. Pinned the exact CommandLineToArgvW escaping. 7 tests. No code change — it was already correct |
| 3 | `Ui/Utils/mRemoteNG/MRemoteNgCsv.cs` (new, extracted) + bounds fix; `MRemoteNgImporter` reduced to CSV→model mapping | mRemoteNG drops trailing empty columns, so a data row is routinely shorter than the header. `GetValue` indexed the row by the header's column position with no bounds check, so the first short row threw `IndexOutOfRange` and aborted the whole import. 6 tests |
| 4 | `Tests/…/ProxyHandshakeTests.cs` (new); `ProxyHandshake` widened to public | `ProxyHandshake` builds the SOCKS5/4/4a/HTTP-CONNECT handshake bytes for every proxied session and its own summary says it is meant to be MemoryStream-tested — but had none. A scripted-stream fixture now pins the exact bytes and reply handling. 12 tests. No behaviour change |

### Rejected, and why

| Idea | Why not |
| --- | --- |
| **Quote-aware mRemoteNG CSV splitting** | The crash fix is unambiguous and cannot regress good input; changing the delimiter handling to honour `"`-quoting could mis-parse an export that does not quote, and stability outranks the extra fidelity. Left for a round that can confirm mRemoteNG's exact quoting on Windows |
| **Fix `SshConfigParser.StripUserAndPort` for bracketed/bare IPv6 `ProxyJump` hops** | Real (it can cut an IPv6 literal at a colon), but niche, and the SSH parser is dense and heavily relied on. Deferred rather than risk a regression in the same round as three other changes. Noted here so the next round can take it deliberately |
| **Natural-sort the hostname fallback of the old comparer only** | Superseded — item 1 does exactly this as part of the rewrite |

### Verify

- `dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` — 0 errors.
- §7.2 harness over the four new test files + the files under test — 34 passed, 0 failed.
- `Ui/AppVersion.cs` untouched. No hardening reverted. No new user-visible strings.

---

## 2026-08-28 — fix round: the Thai locale that made every log call throw

A fix round on the same branch, `cursor/rdp-dpapi-secret-audit-e31b`, not a feature round. No new
capability, no `Ui/AppVersion.cs` change, nothing reverted. PR #15's Windows CI came back **525 passed, 1
failed** ([run 33167197981](https://github.com/chaogei666661/1Remote-plus/actions/runs/33167197981)) on
`BackupServiceTests.TheManifestRecordsWhenItWasTakenInUtcAndInTheGregorianCalendar`, a test the previous
round had added:

```
System.ArgumentOutOfRangeException: startIndex cannot be larger than length of string.
  at Shawn.Utils.SimpleLogHelperObject.MakeLog(...)  SimpleLogHelper.cs:line 393
  at _1RM.Service.Backup.BackupService.Create        BackupService.cs:line 108
```

### What was actually wrong

Not the backup, and not the test. `SimpleLogHelper.MakeLog` reads the calling frame's source file out of
the PDB and cuts the directory off the front of it:

```csharp
if (fileName.Contains("/"))
    fileName = fileName.Substring(fileName.LastIndexOf("/", StringComparison.Ordinal) + 1);
if (fileName.Contains("\\"))
    fileName = fileName.Substring(fileName.LastIndexOf("\\") + 1);   // <- no StringComparison
```

The second one takes the **current culture's collation**. ICU's Thai tailoring treats a backslash as
ignorable, so under `th-TH` the search answers the *length* of the string rather than the position of the
last separator, and the `Substring` that follows is one past the end. Measured here, not guessed:

| culture | `path.LastIndexOf("\\")` | ordinal | `path.Length` |
| --- | --- | --- | --- |
| invariant / en-US | 48 | 48 | 65 |
| **th-TH** | **65** | 48 | 65 |

`/`, `_` and `.` behave the same way under `th-TH`; `Contains` is ordinal and is not affected, which is
what gets execution into the branch in the first place.

Three consequences, in order of importance:

1. **On a Thai desktop this is every log call in the app, not one test.** Windows source paths are full of
   backslashes and 106 files call `SimpleLogHelper`. Linux paths have none, the branch is never taken, and
   that is the only reason this had never been seen.
2. `BackupService.Create` had already written the entire archive to disk by the line that logs. The caller
   was told the backup had failed.
3. The line number in the CI stack trace (393) is the Release build's, four lines off the `Substring` at
   399. The submodule is at the pinned `7479754`, unmodified.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `Ui/Utils/BestEffortLog.cs`, and `BackupService`'s four log calls through it | The crash is in a submodule that is not ours to correct. What is ours is the rule that a finished piece of work is not failed by a line written about it |
| 2 | `StringComparison.Ordinal` in `VmFileTransmitHost.CmdGoToParent` and `CredentialPrompt.LogonUser` | Found by grepping our own code for the same call shape. Neither crashes; both quietly do the wrong thing under `th-TH` |

`BestEffortLog.Write` takes the log call as an `Action` rather than as a message. That is not decoration:
a lambda is compiled into the type that wrote it, so the frame `MakeLog` walks back to is still the call
site's own file and line. A helper that took a string would have put `BestEffortLog.cs` on every warning
in the app — which is exactly the field the crashing code was computing.

### Rejected, and why

| Idea | Why not |
| --- | --- |
| **Patch `SimpleLogHelper.cs`** (one `StringComparison.Ordinal`) | It is the `VShawn/Shawn.Utils` submodule, pinned at `7479754` and not committed from this repository. A local edit would be invisible to CI, which checks the submodule out at the pin, and would be lost on the next update. The right home for it is a PR upstream — written down below |
| **Turn logging off in the test** | Hides a live product bug behind a test setting. `Create` would still throw on a real Thai desktop, and the test that is supposed to be about the manifest would have acquired a reason to be read as being about the logger |
| **Change the log message** | Nothing about the message is sliced. `MakeLog` slices the *source path of the caller*, which no call site can influence |
| **Pin `CultureInfo` inside `BackupService`** | The bug is not the backup's; every other caller would still have it, and a method that quietly changes the ambient culture to write a log line is worse than the line being lost |
| **`<DeterministicSourcePaths>` / `<PathMap>` in `Ui.csproj`** so the PDB records `/_/…` with no backslash | Would fix CI and release builds by coincidence and leave a developer's local Debug build on a Thai machine crashing on the first log line. Also changes what every stack trace and every debugger session sees, to work around a bug in one `Substring` |
| **A `try`/`catch` at each of the four call sites instead of a helper** | Same behaviour, four copies of a paragraph explaining why. And nowhere to point the next call site that needs it |
| **Route all 106 files through `BestEffortLog`** | A fix round is not the place for a 106-file diff, and most of those call sites are already inside a `try` that would swallow it. `BackupService` is where CI caught it and where a lost line costs a completed backup |
| **Fix `Substring(0, lastSlash)` giving `""` for `/foo` in `CmdGoToParent`** | Real, and pre-existing under every locale — the parent of `/foo` should be `/`, not the empty string. It is a behaviour change on the file-transmit host, which is not what this round is. Written down below |

### What landed

| Commit | |
| --- | --- |
| `e1019d9c` | `fix(backup): a Thai locale made every log call throw, and took the finished backup with it` |
| `3c4dd940` | `fix(i18n): two more places where a Thai locale made a string search answer the wrong thing` |

New file: `Ui/Utils/BestEffortLog.cs` (37 lines). New tests: `Tests/Utils/BestEffortLogTests.cs` (3) and
two added to `Tests/Service/Backup/BackupServiceTests.cs` — 5 in all. No language keys, no settings, no
README change; nothing user-visible changed except that a Thai desktop can now take a backup.

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` with SDK 9.0.x — **0 errors, 120
warnings**, the count `main` builds with.

Level 2 of §7, and this time the Windows-only failure was reproduced **on Linux**. A throwaway `net9.0`
MSTest project outside the repository compiled the real `BestEffortLog.cs`, `BackupService.cs`,
`TimestampedFileName.cs` and the two real test files by absolute path, against a `ProjectReference` to the
real `Shawn.Utils.csproj`, with a stub `AppPathHelper` / `Assert` / `AppVersion` and a no-op `TestInit`.
**18 passed, 0 failed. Nothing excluded.** The project is not in the repository and was deleted afterwards.

The reproduction is a `#line` directive. `MakeLog` only misbehaves when the frame's recorded path contains
a backslash, which on Linux it never does — but `#line 100 "D:\a\…\BackupService.cs"` makes the compiler
record exactly that path for the code under it, with the real, unmodified `SimpleLogHelper` doing the
slicing. `<PathMap>` was tried first and is no good for this: Roslyn normalises the replacement's
separators to `/`.

Each change was checked against the bug it claims to catch, by mutating and re-running:

- With `BestEffortLog.Write`'s `catch` changed to `throw;`, **2** fail:
  `ALoggerThatThrowsDoesNotReachTheCaller` and the harness's `BestEffortLogTurnsThatIntoNothing`.
- With a `#line` directive at the top of the real `BackupService.cs` giving it a Windows frame path and the
  four `BestEffortLog.Write(…)` calls reverted to bare `SimpleLogHelper` calls, **3** fail — including
  `TheManifestRecordsWhenItWasTakenInUtcAndInTheGregorianCalendar` with the identical
  `ArgumentOutOfRangeException`, which is the CI failure, on Linux. With the `#line` in place and the
  guard restored, all 18 pass again. Both mutations were reverted; the tree is the two commits above.
- The harness's `TheFrameThisHarnessFakesReallyDoesCarryABackslash` asserts the throw itself, so the
  reproduction cannot rot into a test that passes because nothing happens.

The two `StringComparison.Ordinal` fixes are **not covered by a test** and were not executed: one is a
`RelayCommand` inside the file-transmit view model, the other a `LogonUser` P/Invoke. They are one-word
changes whose old behaviour is in the table above.

Needing a Windows reviewer: nothing new beyond what the previous round's entry already lists. The Thai
behaviour itself is worth one check if a reviewer has a machine to spare — set the Windows display
language to Thai, take a backup, and confirm it reports success.

### For the next round

1. **A PR to `VShawn/Shawn.Utils`** adding `StringComparison.Ordinal` to `MakeLog`'s
   `LastIndexOf("\\")` and to `CleanUpLogFiles`'s `LastIndexOf("_")`. One word each, and it fixes the
   crash for every caller of that library rather than for the four call sites this round guarded. Until
   it lands and the submodule pin moves, a Thai desktop still loses log lines from the other 106 files —
   it just no longer crashes in `BackupService`.
2. `CmdGoToParent` returns `""` rather than `"/"` as the parent of `/foo`, under every locale.
3. Everything on the previous round's list is unchanged and still stands: .NET 10 first.

---

## 2026-08-28 — the .rdp password every account on the PC could read, and the export nobody recorded

Branch `cursor/rdp-dpapi-secret-audit-e31b`, off `main` at `c24c26d6` (v1.3.0.26). Opened on the release, as
§0 says, about three hours late through no fault of the round. Took item 2 and item 3 of the last round's
"For the next round" list, and went looking for one new finding rather than filling the round out of the
backlog.

### What the research turned up

**Nothing to port and nothing to patch, for the seventh round running.** `1Remote/1Remote`'s newest release
is still `1.3-prerelease` (2026-04-29), stable still 1.2.1 from August 2025. `chaogei/1Remote-Plus`'s newest
commits are the release plumbing this fork already has plus the four bare `Model:` commits. `dotnet restore
Ui/Ui.csproj` emits no `NU19xx`: no direct or transitive package has a live advisory.

**The new finding is in the oldest file in the tree.** `Ui/Utils/RdpFile/DataProtection.cs` is third-party
code from 2007 that nothing since has read. Its `ProtectData` overloads default to
`CRYPTPROTECT_UI_FORBIDDEN | CRYPTPROTECT_LOCAL_MACHINE`, and `RdpConfig`'s constructor is the only caller:
it is what produces the `password 51:b:` line of every `.rdp` this app writes.

`CRYPTPROTECT_LOCAL_MACHINE` keys the blob to the **machine** rather than to the user. Any account on that
PC, and any service running on it, can call `CryptUnprotectData` on the file and get the cleartext password
back — no master key theft, no privilege needed beyond being able to read the file.

| Where a .rdp with that blob ends up | Who can read the password out of it |
| --- | --- |
| The per-session temp directory mstsc is launched from | any other account on the machine that can reach the file |
| Wherever **Export \*.rdp** was told to write | the same, plus whoever the user copied it to on that machine |
| `RdpFormView` / `RdpAppFormView`'s preview file | the same |

The ACL work of an earlier round narrows the temp case; it does nothing for the export. mstsc protects a
saved password with the user's key — that is exactly why a `.rdp` somebody else opens just prompts — so the
flag bought nothing and cost the property that makes a saved password safe to have on disk at all.

Two smaller defects in the same four lines. `ProtectData` returns `null` when DPAPI declines and the caller
did `BitConverter.ToString(null)`, so a DPAPI failure was a crash on the connect path rather than a prompt.
And the `CryptProtectData` DllImport had no `SetLastError`, so the `GetLastWin32Error` two lines below it
read whatever an unrelated call had left behind — then formatted the message into a `StringBuilder` and
dropped it on the floor.

**Item 2, carried over: the audit log stops at connections.** It records who reached which host. It records
nothing when an operator exports every password in the list to a JSON file in cleartext, copies one to the
clipboard, or packs the credential database into a `.1rbak` and carries it off — which is what an
insider-threat or leaver review asks for first. The last round deferred this because it wants a record shape
that is not connection-shaped, which is right: half of these events involve no host at all, and they have a
destination and a count where a connection has a port and a duration.

**Item 3, carried over, confirmed:** `BackupService.SuggestedFileName` interpolated `DateTime.Now`, so the
year came out in the ambient calendar. The manifest's `created=` line had the same problem and was in local
time as well.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | Drop `CRYPTPROTECT_LOCAL_MACHINE`; `EncodePassword`; `SetLastError` | A stored password readable by every other account on the machine, from a file this app writes on every mstsc connect. One flag |
| 2 | `SecretAccessRecord` / `SecretAccessLog` / `SecretAccessCsv`, five call sites | The credential-disclosure events, which no log in the app recorded. Top of the last round's list |
| 3 | `BackupService.SuggestedFileName` and the manifest stamp | One line each, and the last round was told to stay out of the file |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | Out of scope by instruction for the seventh round. Support for .NET 9 ends **2026-11-10**, now about ten weeks out. Still needs a round with nothing else in it |
| **Replace the hand-rolled DPAPI P/Invoke with `System.Security.Cryptography.ProtectedData`** | That type is a NuGet package on .NET Core, not part of the framework. Adding a dependency to change one flag is a worse trade than changing the flag, and the P/Invoke is otherwise correct |
| **Match mstsc's `psw` description string too** | `szDataDescr` is metadata that plays no part in decryption. Changing it would alter every blob this app has ever written for no observable effect |
| **Stop writing a password into the exported `.rdp` at all** | The export exists so the file can be double-clicked. A `.rdp` that always prompts is a `.rdp` the user will edit by hand, and user-scoped DPAPI is the protection mstsc itself considers adequate |
| **Throw a `CryptographicException` when DPAPI declines** | Also a crash, just a better-labelled one. A file without a password line makes mstsc ask, which is what the user would have done anyway |
| **Four more values of `EAuditEvent` instead of a second record type** | `ServerListExported` has no host, no port and no duration, and a count and a destination that a connection record has nowhere to put. Squeezing them into `Reason` and `DurationSeconds` produces a log nobody can read back — and, worse, an export would show up in an access report as a connection |
| **One day file for both record kinds** | Newtonsoft ignores unknown fields and defaults missing ones, so a credential line read as a connection would come back as a `ConnectStarted` to port 0: a fabricated connection in an access report. Two prefixes, and two tests that each log refuses to read the other's lines |
| **Put the credential events under the existing `AuditConnections` switch** | The section is called "Connection audit" and an organisation may want one trail without the other. A second checkbox, `AuditSecretAccess`, on by default, sharing the retention setting, the folder and the delete button |
| **One CSV with both record kinds** | The columns do not overlap enough for one table; a union would be mostly empty cells and would change `AuditCsv.Header`, which is a format that has shipped. The export writes a `-secrets` sibling next to the file the user named |
| **A second save dialog for the credential CSV** | Two dialogs for one button, to produce two files that belong in the same folder anyway |
| **Record the *contents* of what was exported (which servers were in the selection)** | The count and the destination answer the review question. A list of server names in an audit line would make the log itself an inventory of the estate, which is the thing an audit file is most likely to be forwarded outside the company |
| **Audit-log a *reveal* of a password in the editor** | There is no single choke point for it — the editor binds the field, and Windows Hello already gates it. Would mean touching the editor's data binding for a weaker event than the four that were taken |
| **Also record `DiagnosticsExported`** | The bundle is scrubbed: no database, no vault, no `cmd://` command, no host trust. Nothing leaves with it, so it is not a credential-access event |
| **Rename the `audit_title` string to something wider than "Connection audit"** | The key exists in `en-us.xaml` and `zh-cn.xaml` only, so the value could be edited — but the section still is the connection audit, with the credential switch under it. Changing the heading would make the first checkbox read oddly |
| **Fix `RdpConfig`'s `rdp.DisplayName + ".rdp"` export name** | Real — the connect path strips invalid file-name characters and the export path does not — but it is a category-4 defect and this round already had two security changes and a refactor of the audit log in it. Written down below |
| **Remove `~VmFileTransmitHost()`** | Carried over unchanged for the fourth round: it cancels a token nobody registered on, so it cannot throw |
| **Session tab mute / read-only / lock; RDP per-monitor selection; the legacy-SSH toggle** | Carried over for the seventh round. All three still need a human at a Windows keyboard |

### What landed

| Commit | |
| --- | --- |
| `a4c2075d` | `security(rdp): the password in a generated .rdp could be read by any other account on the PC` |
| `83f02074` | `security(audit): nothing was recorded when a password or the whole server list left the app` |
| `225eb89f` | `fix(backup): a backup taken on a Thai-locale desktop was named 2569 and dated in local time` |

New files: `Ui/Service/Audit/IAuditRecord.cs`, `AuditDayFiles.cs`, `AuditLogBase.cs`, `SecretAccessRecord.cs`,
`SecretAccessLog.cs`, `SecretAccessCsv.cs`, `SecretAccessAudit.cs`.
`ConnectionAuditLog.cs` shrank from 279 lines to 89 by moving its file mechanics and its writer thread into
the two shared files; its public surface and all fifteen of its tests are unchanged. (The commit message on
`83f02074` says "seventeen tests" — there are fifteen. Left as written rather than rewriting a pushed
commit.)

New tests: `Tests/Service/Audit/SecretAccessLogTests.cs` (20), `SecretAccessCsvTests.cs` (6),
`Tests/Utils/RdpFile/RdpConfigPasswordTests.cs` (5), and three added to
`Tests/Service/Backup/BackupServiceTests.cs` — 34 in all.

Three new language keys in both `en-us.xaml` and `zh-cn.xaml` (540 keys each, no key in one and not the
other), and two existing values reworded because "delete every recorded connection" now deletes more than
that. One new setting, `AuditSecretAccess`, with a checkbox under **Settings → General → Connection audit**.
`README.md` and `README.zh-CN.md` updated for all three changes.

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true --no-incremental` with SDK
9.0.317 — **0 errors, 120 warnings**, which is the count `main` builds with.

Level 2 of §7 for all three: a throwaway `net9.0` MSTest project compiling the nine audit files,
`RdpConfig.cs`, `DataProtection.cs`, `BackupService.cs`, `Assert.cs`, `AppVersion.cs` and
`TimestampedFileName.cs` by absolute path, together with seven real test files, a no-op `TestInit`, a stub
`AppPathHelper` (the real one has `using System.Windows;`) and a `ProjectReference` to `Shawn.Utils.csproj`.
**71 passed, 0 failed. Nothing excluded.** That project is not in the repository and was deleted afterwards.

Thirty-seven of those 71 are pre-existing tests — the fifteen `ConnectionAuditLogTests` in particular, which
are the safety net for the `AuditLogBase` / `AuditDayFiles` extraction and which pass unchanged.

Each change was checked against the bug it claims to catch, by mutating the file under test and re-running:

- With `CRYPTPROTECT_LOCAL_MACHINE` back in `SECRET_FOR_THIS_USER`, **2** of the 5 RDP cases fail, including
  `ThePasswordIsNotProtectedWithTheMachineKey`.
- With `SecretAccessLog.FILE_PREFIX` set to `connections-`, **4** of the audit cases fail:
  `TheConnectionLogNeverReadsACredentialRecord`, `TheCredentialLogNeverReadsAConnectionRecord`,
  `PruningOneLogLeavesTheOtherAlone` and `ClearRemovesEveryDayFileOfThisLogOnly`.
- With `SuggestedFileName` and the manifest stamp back to interpolated `DateTime.Now`, **2** of the backup
  cases fail.

One honest negative: removing the `line.Replace("\r","").Replace("\n","")` from `AuditDayFiles.Append`
changes nothing, because Newtonsoft escapes a newline inside a JSON string before it can break the line.
`ANewlineInADestinationCannotForgeAnExtraRecord` documents the property rather than guarding that statement.
The strip is kept for the same reason it was kept in the code this was extracted from.

Not executed anywhere, and needing a Windows reviewer:

- **The DPAPI change itself.** `crypt32` does not exist here, so the flags are asserted rather than the
  ciphertext. What to check: connect an RDP server with a saved password via the mstsc runner and confirm it
  still logs in without prompting; then **Export \*.rdp**, and confirm mstsc run as a *different* local
  account prompts for the password instead of connecting.
- The **Record when a credential leaves this app** checkbox and its hint in `GeneralSettingView.xaml`, and
  that **Export to CSV** now produces two files and names both in the message box.
- The five recording call sites: copy password, JSON export, `.rdp` export, backup create, WebDAV upload.
- `SecretAccessLog`'s registration in `Bootstrapper`/`AppInit` and its disposal at shutdown.

### For the next round

1. **.NET 10.** About ten weeks to 2026-11-10. Own round, nothing else in it. Seventh round at the top of
   this list.
2. `ProtocolActionHelper`'s **Export \*.rdp** offers `rdp.DisplayName + ".rdp"` as the file name without
   stripping the characters a file name cannot hold — `ConnectRdpByMstsc` strips them for exactly this
   reason twenty lines away. A server called `web01 / dmz` gives the save dialog a path it cannot use.
3. `RdpFormView.xaml.cs` and `RdpAppFormView.xaml.cs` each write a preview `.rdp`; check they go through
   `SessionTempFile` like the connect path does, now that the file's password is only user-readable and the
   directory is the remaining protection.
4. An "allow legacy SSH algorithms" per-server toggle, so the remaining CBC / SHA-1 set can leave the
   default. Still wants an editor page.
5. Session tab mute / read-only / lock, and RDP per-monitor selection — still waiting on a human at a
   Windows keyboard.

---

## 2026-08-28 — the script that ran without being asked, and the password that stayed on the clipboard

Branch `cursor/session-script-gate-clipboard-secret-1e44`, off `main` at `c795bf9d` (v1.3.0.25). Opened on the
release, as §0 says. This round took the top of the last round's "Not taken" list rather than going looking:
item 2 (gate the pre/post-connect scripts at *connect* time) was written down there explicitly so it would
not have to be found again, and items 3 and 4 were the two small defects.

### What the research turned up

**Nothing to port and nothing to patch, for the sixth round running.** `1Remote/1Remote`'s newest release is
still `1.3-prerelease` (2026-04-29), stable still 1.2.1 from August 2025. `chaogei/1Remote-Plus`'s newest
commits are the release plumbing this fork already has plus the four bare `Model:` commits. `dotnet restore
Ui/Ui.csproj` emits no `NU19xx`: no direct or transitive package has a live advisory.

**One of the two carried-over defects turned out not to exist.** Item 4 said `CmdExportSelectedToJson` warns
with `IoC.Translate("Caution: Your data will be saved unencrypted!")`, "an English sentence used as a key, so
no locale translates it". The key *is* an English sentence, which is ugly, but it is present and translated
in all fifteen locale files and in every `glossary/*.csv`. There is nothing to fix, and renaming the key
would mean touching fifteen files that are otherwise upstream's. Recorded here so the next round does not
re-find it. The other half of item 4 — the twelve-hour file-name stamp — was real.

**The remaining work is three findings, two of them new.**

**1. The connect-time half of the command-injection vector was still open.** The last round closed the
"here is our server list, please import it" path. `CommandBeforeConnected` and `CommandAfterDisconnected`
were still executed with no gate at all on every connect and every disconnect, and they are ordinary columns
of the server list. The delivery mechanisms that need no import step:

| Where the list lives | Who else can write it |
| --- | --- |
| MySQL / PostgreSQL data source | any other admin of that database |
| SQLite on a network share | anyone with write access to the share |
| A synced profile folder | whatever else syncs to it |
| A restored `.1rbak` | whoever produced the archive |

An operator opens the shared source they open every morning and the script runs — with their account, and
with `HideCommandBeforeConnectedWindow`, with no window to notice. The README already admitted the gap in
so many words: "Pre/post-connect scripts are the same class of feature and carry the same caveat."

**2. A copied password went into Windows clipboard history and stayed on the clipboard.** "Copy password"
was `Clipboard.SetDataObject(password)` and nothing else. On Windows 10 1809 and later that is two leaks at
once. Win+V keeps the last 25 clipboard entries in cleartext, readable by anyone who reaches an unlocked
desktop; with *Sync across your devices* on, the cloud clipboard uploads them to the user's Microsoft account
and pushes them to their other machines — a destination the operator did not choose and, on a managed fleet,
may not be permitted. And nothing ever took the value off the clipboard, so the next paste into a chat
window, a ticket or a terminal was whatever had been forgotten there. Windows publishes three registered
formats for the first problem and every password manager uses them; this app used none of them.

**3. The after-disconnect test button printed an exit code nobody measured.** Item 3 was the wrong command
in the preview, which is real. Reading the method turned up a second defect in the same four lines: the
disconnect script is started with `isAsync: true`, and `WinCmdRunner.RunFile` returns a constant `0` without
waiting in that mode, so "The exit code of the script = 0." was shown whether the script succeeded, failed
or did not exist. Both come from the block having been copied out of `RunScriptBeforeConnect` and not
adapted.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `SessionScriptTrustStore`, gating both script methods | The one remaining unprompted path from "somebody else can write your server list" to "code runs on your desktop". Top of the last round's list |
| 2 | `SecretClipboard` + `SecretClipboardHost` | A cleartext password retained by the OS in two places the user never chose, from the one action whose whole purpose is handling a secret |
| 3 | `ShowWhatWillRun`, and no fabricated exit code | A test button that reports the wrong command and invents a result is worse than no test button |
| 4 | `TimestampedFileName` | Two exports in one day silently overwrote each other, on the path that writes every password in cleartext |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | Out of scope by instruction for the sixth round. Support for .NET 9 ends **2026-11-10**, now about ten weeks out. Still needs a round with nothing else in it |
| **Rename the `Caution: Your data will be saved unencrypted!` key** | The premise of the carried-over item was wrong — see above, it is translated everywhere. Renaming it means editing fifteen `.xaml` files and fifteen `glossary/*.csv` files that are otherwise upstream's, to change nothing a user sees |
| **A per-server "this script is trusted" flag instead of a trust store** | The flag would live in the server list, which is the thing that cannot be trusted. Anything able to add the command can set the flag |
| **Refuse or rewrite a script that looks dangerous** | Same answer the import round gave. A before-connect script is usually `cmd`, `powershell` or a `.bat`, all of which run anything, so a heuristic would either refuse every legitimate script or catch nothing |
| **Let the before-connect refusal continue the connect without the script** | The script may be the VPN, the drive mount or the credential refresh the session needs. The existing contract already says a non-zero pre-connect script aborts the connect, and the `cmd://` gate aborts too. Consistency beats a connection that half-works |
| **Approve on save for the whole bulk-edit selection** | `CmdSave` approves the two fields of the editor's merged template, which is what is on screen. Walking `_serversInBuckEdit` would approve command lines the user never looked at, which is the thing the gate exists to prevent |
| **Put the confirm prompt inside `SessionScriptTrustStore`** | Same reason `SshHostKeyGate` did not: `MessageBoxHelper` and `IoC.Translate` reach WPF, and the store would then be the one file in this change that cannot be run here. Both arrive as delegates from `Bootstrapper` |
| **Reuse `ExternalSecretTrustStore` for the session scripts** | It is on the do-not-touch list, and the two should not share a store anyway: approving a password-fetch command is not approving a connect script, and merging them would silently widen both |
| **Clear the clipboard unconditionally when the timer fires** | Deletes whatever the user copied in the meantime, from a timer they never saw. The expiry checks that the clipboard still holds what was put there, and there is a test for it |
| **Keep the password on the clipboard until the app closes** | That is the current behaviour and the bug. 30 seconds by default, 0 restores the old behaviour for anyone whose clipboard manager needs it |
| **Also exclude the copied *address* and *user name* from clipboard history** | Neither is a secret, and users do paste them into tickets, where the history is a convenience. Excluding them would cost something and protect nothing |
| **`Clipboard Viewer Ignore`, the fourth format some implementations add** | It is a third-party convention rather than a documented Windows one, and Microsoft's three cover the history and the cloud, which are the two things that persist. Not worth a format nobody can point at documentation for |
| **Fix `BackupService.SuggestedFileName`'s culture too** | It is in the part of the tree this round was instructed to stay out of, and its format string is already the 24-hour one, so the only defect left there is the calendar. Written down for a round that is allowed to touch it |
| **Audit-log the cleartext JSON export and the password copy** | The audit log records connections; "who exported every password in cleartext" is the event a compliance review would actually ask for, and it is not recorded anywhere. Considered and deferred: it wants a record shape that is not connection-shaped, and this round already had two security changes in it. Best candidate for the next round |
| **Remove `~VmFileTransmitHost()`** | Carried over unchanged for the third round: it cancels a token nobody registered on, so it cannot throw |
| **Rework `scripts/watch-release-iteration.sh`** | Instructed not to unless it misreports. It did not: run for real this round (`--peek`, after the fourth commit was pushed) it read v1.3.0.25 as published, [run 33150799553](https://github.com/chaogei666661/1Remote-plus/actions/runs/33150799553) as `success`, and decided `0 (idle) — 1 iteration branch(es) still ahead of main`, naming this branch and correctly writing off `cursor/project-analysis-report-df00` as stale |
| **Session tab mute / read-only / lock; RDP per-monitor selection; the legacy-SSH toggle** | Carried over for the sixth round. All three still need a human at a Windows keyboard |

### What landed

| Commit | |
| --- | --- |
| `e00e2961` | `security(connect): a server list you did not write could still run a command on your desktop` |
| `61ce6314` | `security(clipboard): a copied password went into Windows clipboard history and stayed there` |
| `870558ca` | `fix(editor): testing the after-disconnect script showed the wrong command and a made-up exit code` |
| `c4b86a13` | `fix(export): a morning export and an evening one were offered the same file name` |

New files: `Ui/Utils/SessionScript/SessionScriptTrustStore.cs`, `Ui/Utils/SecretClipboard.cs`,
`Ui/Utils/SecretClipboardHost.cs`, `Ui/Utils/TimestampedFileName.cs`.
New tests: `Tests/Utils/SessionScript/SessionScriptTrustStoreTests.cs` (18),
`Tests/Utils/SecretClipboardTests.cs` (15), `Tests/Utils/TimestampedFileNameTests.cs` (8) — 41 in all.

Seven new language keys in both `en-us.xaml` and `zh-cn.xaml` (537 keys each, no key in one and not the
other); four of the seven replace English literals that were built with string interpolation in
`ProtocolBase`. One new setting, `SecretClipboardSeconds`, with a row under **Settings → General → Copied
passwords**. `README.md` and `README.zh-CN.md` updated for all four changes.

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true --no-incremental` with SDK
9.0.317 — **0 errors, 120 warnings**, which is the count `main` builds with.

Level 2 of §7 for all four: a throwaway `net9.0` MSTest project compiling `SessionScriptTrustStore.cs`,
`SecretClipboard.cs`, `TimestampedFileName.cs` and the three test files by absolute path, with a no-op
`TestInit` and a `ProjectReference` to `Shawn.Utils.csproj` for `SimpleLogHelper`. **41 passed, 0 failed.**
Nothing excluded. That project is not in the repository and was deleted afterwards.

Each was checked against the bug it claims to catch, by mutating the file under test and re-running:

- With `EnsureApproved` returning `true` unconditionally — the behaviour before this class existed —
  **11** of the 18 gate cases fail. With only the unwired default flipped to allow, exactly
  `WithNoPromptWiredNothingRuns` fails.
- With `Expire` clearing unconditionally instead of checking the token and the current contents,
  **6** of the 15 clipboard cases fail, `SomethingTheUserCopiedSinceIsLeftAlone` among them.
- With the stamp back to `yyyyMMdd-hhmmss` and the ambient culture, **3** of the 8 file-name cases fail.

Not executed anywhere, and needing a Windows reviewer:

- The `Bootstrapper.Configure` wiring of `SessionScriptTrustStore.Confirm` and `StorePathProvider`, and the
  prompt itself. `MessageBoxHelper.Confirm` goes through `Execute.OnUIThreadSync`, and
  `RunScriptAfterDisconnected` reaches it from `Process.Exited` and from `SessionControlService` — both
  outside the session lock, both already inside a `try`/`catch` — so the marshalling should hold, but it has
  not been seen.
- The three clipboard formats. Whether Win+V really skips the entry can only be seen on a Windows desktop
  with clipboard history on. The format *names* are asserted in a test, because a typo there would leave
  the password in the history while the code looked as though it had excluded it.
- The new **Copied passwords** row in `GeneralSettingView.xaml`, which mirrors the audit retention row.
- `ServerEditorPageViewModel.CmdSave`'s two `Approve` calls, and the editor's Test button.

### For the next round

1. **.NET 10.** Ten weeks to 2026-11-10. Own round, nothing else in it. Sixth round at the top of this list.
2. Audit-log the credential-disclosure events. The log records who connected where; it records nothing when
   an operator exports every password in cleartext to a file, or copies one to the clipboard. That is the
   event an insider-threat review asks for first, and it is the natural follow-up to both of this round's
   security changes. Considered and deferred this round — see the rejection table.
3. `BackupService.SuggestedFileName` still formats its year in the ambient culture, so a backup taken on a
   Thai-locale desktop is named `2569…`. One line, in a file this round was told to stay out of.
4. An "allow legacy SSH algorithms" per-server toggle, so the remaining CBC / SHA-1 set can leave the
   default. Still wants an editor page.
5. Session tab mute / read-only / lock, and RDP per-monitor selection — still waiting on a human at a
   Windows keyboard.

---

## 2026-08-28 — the bastion nobody verified, the import that brought its own commands, and the crash that left no note

Branch `cursor/bastion-key-import-scan-crashlog-4b12`, off `main` at `df91984f` (v1.3.0.24). Opened on the
release, as §0 says. The last round's "Not taken" list was thin — `.NET 10` (out of scope by instruction),
the legacy-SSH toggle (still wants an editor page), `~VmFileTransmitHost()` (still cannot throw), a branch
unreachable on Windows, and two items waiting on a human at a Windows keyboard — so this round went looking
instead of taking the top of the list. It stayed out of the transfer pane, which the last three rounds had to
themselves.

### What the research turned up

**Nothing to port and nothing to patch, for the fifth round running.** `1Remote/1Remote`'s newest release is
still `1.3-prerelease` (2026-04-29), stable still 1.2.1 from August 2025. `chaogei/1Remote-Plus`'s newest
commits are the release plumbing this fork already has plus the four bare `Model:` commits. `dotnet restore
Ui/Ui.csproj` emits no `NU19xx`: 22 direct references, no live advisory, direct or transitive.

So the round is three findings in code that no previous round had read.

**1. The bastion was the one SSH host this app never checked.** `HostTrustService` was written because SFTP
"never subscribed to `HostKeyReceived`" — its own comment says so. `SshConnectionFactory.Connect` still
did not, and it is the connection that matters more:

| | Host key checked? | What rides on it |
| --- | --- | --- |
| SFTP (`TransmitterSFtp`) | yes, since the trust-store round | one file browser |
| SSH jump host (`SshConnectionFactory`) | **no** | every proxied session — RDP and VNC included, because they go through the local relay — plus every standing port forward and the proxy tester |

SSH.NET's default is `e.CanTrust = true`. So the jump host's password, or the passphrase-unlocked private
key, went to whatever answered on that address, and one intercepted handshake yielded the credentials of
everything routed through it. An auto-started forward does that at launch with nobody watching. Three call
sites, one choke point: `SshJumpTunnel.Start`, `PortForwardService.GetOrConnectSession` and `ProxyTester`
all go through `SshConnectionFactory.Connect`.

**2. A server list is not only addresses — three of its fields are command lines this app runs locally.**
`CommandBeforeConnected` and `CommandAfterDisconnected` are executed by `RunScriptBeforeConnect` /
`RunScriptAfterDisconnected` on every connect and every disconnect, with the user's account and, with
`HideCommandBeforeConnectedWindow`, no window; a `LocalApp` entry's `ExePath` is a program wearing a
server's icon. All three are serialised by `JsonConvert.SerializeObject(list)` into the JSON export, live in
the PRemoteM/1Remote database, and ride inside the backup archive. Every importer —
`CmdImportFromJson`, `CmdImportFromDatabase`, `CmdImportFromCsv`, `CmdImportFromSshConfig`,
`CmdImportFromRdp` — called `Database_InsertServer` without showing any of it.

This is the threat the `cmd://` gate exists for. That gate's own comment in `AppPathHelper` says an approval
to execute something "is about this machine and must not travel with a synced or shared database" — and the
README already admits "pre/post-connect scripts are the same class of feature and carry the same caveat".
They had no gate at all. "Here is our server list" was a way to put a command on somebody's desktop that runs
the next time they open the entry, which they will, because that is why they imported it.

**3. Only the UI thread had a crash handler.** `Bootstrapper.OnUnhandledException` is
`DispatcherUnhandledException`. There is no `AppDomain.CurrentDomain.UnhandledException` and no
`TaskScheduler.UnobservedTaskException` anywhere in the repository. Almost nothing this app does is on the
dispatcher: the `1Rm.AuditLog` writer thread, the SFTP/FTP transfer threads, SSH.NET's receive threads, the
retention pass, the reachability timer, and every `Task.Factory.StartNew` body — the import and export paths
alone have five. An exception out of one of those wrote **nothing**: no log line, no Sentry event, no dialog.
The process either disappeared, or — for a faulted `Task` nobody awaited, which on .NET Core does not end the
process — carried on with the work silently not done. That second shape is what the last two rounds kept
chasing from the far end: a transfer that reported success and moved nothing.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `SshHostKeyGate`, subscribed by `SshConnectionFactory` | A credential handed to an unverified peer, on the one connection every proxied session depends on. The mirror of a guard this fork already built for the lesser case |
| 2 | `ImportedCommandScan`, and the five importers ask before writing | Local code execution arriving inside a file the user was sent, through the one feature whose whole point is trusting somebody else's list |
| 3 | `UnhandledFailureLog` + `UnhandledFailureReporter` | Every background crash in this app was invisible, which is also why the previous two rounds' bugs were so hard to find |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | Out of scope by instruction for the fifth round. Support for .NET 9 ends **2026-11-10**, now about ten weeks out. Still needs a round with nothing else in it |
| **A `TrustUnverifiedHost` equivalent on `ProxyConfig`, so a jump host can opt out** | Would need a checkbox in the proxy editor, which cannot be tried from here, and there is nothing to opt out *of*: trust-on-first-use asks once and remembers, exactly like PuTTY. The per-server flag exists on `ProtocolBase` because RDP/FTPS certificate errors recur; a pinned SSH host key does not |
| **Let `SshHostKeyGate` default to allowing when nothing wired it** | That is the hole, restored, while looking like a check. It refuses instead, so a wiring mistake is a login that fails rather than a verification that silently does not happen. There is a test for the default |
| **Put the `HostTrustService` call inside `SshHostKeyGate` rather than wiring it from `Bootstrapper`** | `HostTrustService.cs` reaches `MessageBoxHelper` and therefore WPF, so the gate would have been the one file in this change that could not be run here. `HostTrustService` itself is untouched — instructed to leave it alone, and nothing about it needed to change |
| **Gate the pre/post-connect scripts at *connect* time**, the way `cmd://` is gated | The right answer for the shared-database vector, and it is a second trust store, a second prompt and a decision about what happens when the answer is no during an auto-connect. Import confirmation covers the file-you-were-sent vector, which is the one with a delivery mechanism, and it is revertible on its own. Written down here so the next round does not have to find it again |
| **Refuse or rewrite an imported command** | Renaming or stripping somebody's script behind their back, rejected for the same reason the deceptive-name round rejected sanitising a file name. A legitimate before-connect script is a real feature that real users have |
| **Scan the backup restore path too** | The backup zip is on the do-not-touch list this round, and restore replaces the whole configuration rather than merging a list, which is a different question to ask the user |
| **Put the `ProtocolBase` → `ImportedCommandSource` mapping in `ImportedCommandScan`** | It would drag WPF imaging and the IoC container into the one file that has to be runnable here. The mapping is five property reads in `ServerPageViewModelBase.ConfirmLocalCommands`, and it is the part CI covers rather than the harness |
| **Show a dialog from `AppDomain.UnhandledException`** | It fires while the runtime is already ending the process, and the thread that died may be the UI thread. Writing the failure down is the whole of what is achievable; claiming more would be theatre |
| **Log every unhandled failure without a cap** | A background loop that throws every iteration would write the same stack trace until the disk fills, and the user's database is on that disk. Twenty per run, with the twentieth line saying so, because a log that just stops reads as an app that recovered |
| **Move `Bootstrapper.IsTransientGdiError` into the new class so both handlers share it** | The GDI+ suppression is about `WindowsFormsHost` painting, which is a dispatcher-thread event, and `AppDomain.UnhandledException` cannot suppress anything anyway. Moving it would also have meant rewriting `GdiErrorHandlingTests`, which reflects on the private method, for no behaviour change |
| **Remove `~VmFileTransmitHost()`** | Carried over. Last round's reasoning holds unchanged: it cancels a token nobody registered on, so it cannot throw |
| **Rework `scripts/watch-release-iteration.sh`** | Instructed not to unless it misreports. It did not: run for real this round (`--peek`, after this branch was pushed) it read v1.3.0.24 as published, [run 33149157431](https://github.com/chaogei666661/1Remote-plus/actions/runs/33149157431) as `success`, and decided `0 (idle) — 1 iteration branch(es) still ahead of main`, naming this branch and correctly writing off `cursor/project-analysis-report-df00` as stale. The `10` on this release was consumed by the parent before this round started, so that transition was not observed here |
| **Session tab mute / read-only / lock; RDP per-monitor selection** | Carried over for the fifth round. Still needs a human at a Windows keyboard |

### What landed

| Commit | |
| --- | --- |
| `c3aa8193` | `security(proxy): the bastion was the one SSH host nobody checked` |
| `f180aeaf` | `security(import): a server list you were sent could bring its own commands` |
| `7f0cfeb0` | `fix(crash): a failure off the UI thread left nothing behind at all` |

New files: `Ui/Utils/Proxy/SshHostKeyGate.cs`, `Ui/Utils/Import/ImportedCommandScan.cs`,
`Ui/Utils/Tracing/UnhandledFailureLog.cs`, `Ui/Utils/Tracing/UnhandledFailureReporter.cs`.
New tests: `Tests/Utils/Proxy/SshHostKeyGateTests.cs` (8),
`Tests/Utils/Import/ImportedCommandScanTests.cs` (20),
`Tests/Utils/Tracing/UnhandledFailureLogTests.cs` (13) — 41 in all.

Eight new language keys in both `en-us.xaml` and `zh-cn.xaml` (527 keys each, no key in one and not the
other). Change 1 needed none: it reuses the existing `host_trust_*` prompt. `README.md` and
`README.zh-CN.md` updated for all three.

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true --no-incremental` with SDK
9.0.317 — **0 errors, 120 warnings**, which is the count `main` builds with.

Level 2 of §7 for all three: a throwaway `net9.0` MSTest project compiling `SshHostKeyGate.cs`,
`ImportedCommandScan.cs`, `RemoteNameInspector.cs`, `UnhandledFailureLog.cs` and four test files by
absolute path, with a no-op `TestInit` and a `ProjectReference` to `Shawn.Utils.csproj` for
`SimpleLogHelper`. **50 passed, 0 failed** — the 41 new cases plus the 9 existing `RemoteNameInspector`
ones, which share a file under test. Nothing excluded.

Each was checked against the bug it claims to catch:

- With `SshHostKeyGate`'s unwired default and its missing-key branch both returning `true` — the
  pre-change behaviour — **2** of the 8 gate cases fail.
- With `Present()` no longer going through `RemoteNameInspector`, and `ServerCount` counting names instead
  of positions, **4** of the 20 import cases fail.
- With `Interlocked.Increment` replaced by `++`, `TheLimitHoldsWhenSeveralThreadsFailAtOnce` fails; with
  the non-`Exception` branch replaced by a bare `ToString()`, **2** more fail.

**The two hooks were also fired for real, outside the repository.** A `net9.0` console program compiling
the real `UnhandledFailureLog.cs`, run twice — once with no hooks (the state before this change) and once
with the wiring `UnhandledFailureReporter.Install()` puts in place:

| | unobserved faulted `Task` | throw on a plain background thread |
| --- | --- | --- |
| before | nothing at all | runtime's own stderr dump, which a windowed app has nowhere to send |
| after | `Unhandled failure [TaskScheduler.UnobservedTaskException, the process continues, thread 2]` with the inner `TimeoutException` intact | `Unhandled failure [AppDomain.UnhandledException, the process is terminating, thread 7]` with the stack |

That program is not in the repository.

Not executed anywhere, and needing a Windows reviewer: the `HostKeyReceived` subscription itself (SSH.NET
must raise it and `HostKeyEventArgs` cannot be constructed outside the library), the `Bootstrapper.Configure`
wiring line, and `ServerPageViewModelBase.ConfirmLocalCommands`'s five call sites. See the pull request for
the manual steps.

### For the next round

1. **.NET 10.** Ten weeks to 2026-11-10. Own round, nothing else in it. Fifth round at the top of this list.
2. Gate `CommandBeforeConnected` / `CommandAfterDisconnected` at **connect** time, the way `cmd://` is
   gated. This round covers the file-you-were-sent vector; a shared MySQL/PostgreSQL data source another
   admin can write to is still an unprompted command on every operator's desktop.
3. `RunScriptAfterDisconnected`'s test-run message prints `CommandBeforeConnected` — testing the
   after-disconnect script shows you the wrong one. `Ui/Model/Protocol/Base/ProtocolBase.cs:476`.
4. `CmdExportSelectedToJson` warns with `IoC.Translate("Caution: Your data will be saved unencrypted!")`,
   an English sentence used as a key, so no locale translates it — on the one path that writes every
   password in cleartext. Its file name uses `yyyyMMddhhmmss`, a 12-hour clock with no AM/PM.
5. An "allow legacy SSH algorithms" per-server toggle, so the remaining CBC / SHA-1 set can leave the
   default. Still wants an editor page.
6. Session tab mute / read-only / lock, and RDP per-monitor selection — still waiting on a human at a
   Windows keyboard.

---

## 2026-08-28 — the upload scan stops losing the whole transfer, and starts saying what it left out

Branch `cursor/upload-scan-unreadable-folders-d60d`, off `main` at `f7d73fe0` (v1.3.0.23). Opened on the
release, as §0 says. The previous round's "Not taken" was the brief: `Enumerate` aborting on one unreadable
subfolder.

### What the research turned up

**Nothing to port and nothing to patch.** `1Remote/1Remote`'s newest release is still `1.3-prerelease`
(2026-04-29); the newest stable is 1.2.1 from August 2025. `chaogei/1Remote-Plus`'s newest eight commits are
the release plumbing this fork already has plus four bare `Model:` commits — the same answer as the last
three rounds. `dotnet restore Ui/Ui.csproj` emits no `NU19xx`: no direct or transitive package has a live
advisory.

**So the work was where the last round pointed, and there was more of it than one bug.** Four things, all in
the transfer pane, all of them the same shape: *the transfer finished, the panel said nothing, and something
the user asked for is not on the far side.*

**1. One folder the platform would not list cost the entire upload.** `LocalUploadScan.Enumerate` called
`GetDirectories()` with nothing around it, so the first `UnauthorizedAccessException` left the whole call,
landed in `TransmitTask.AddLocalDirectory`'s log-only `catch`, and the task went on to transmit an empty
queue — success on screen, zero bytes sent. This is not an exotic input. Every Windows machine has folders
their own owner cannot list:

| Folder | Why it refuses |
| --- | --- |
| `C:\System Volume Information` | SYSTEM-only ACL |
| `C:\$Recycle.Bin\S-1-5-21-…` | another account's bin |
| `C:\Users\<someone else>` | another account's profile |
| `C:\Documents and Settings` | junction with a deny-list ACE |

So uploading a drive root — which the round before last had *just* made nameable — failed every single time,
and so did uploading any folder with one such child anywhere below it. It is exactly what
[run 33147128602](https://github.com/chaogei666661/1Remote-plus/actions/runs/33147128602) demonstrated on
`windows-latest` before the fix round retired that test case.

**2. The notice that was supposed to cover this kind of thing could not be read.** The status line is a
`Border` of `Height="30"` holding one `TextBlock`; `LinksNotFollowed` was rendered with
`string.Join(", ", …)` over the whole list. An ordinary Windows profile carries a dozen compatibility
junctions (`Application Data`, `My Documents`, `Start Menu`, `Recent`, `SendTo`, …) and a drive-root upload
can now leave hundreds of folders unread, so the string ran to tens of kilobytes of which one line was
visible — and the visible line was names, not the count that tells the user something went wrong.

**3. The duplicate check still swallows a file, and this time it is on purpose.** Ordinal-ignore-case was
the right call for the upload direction and half-right for the download direction: an SFTP server is
normally case-sensitive, so `Makefile` and `makefile` in one remote directory are two different files, and
Windows can hold exactly one of them. Which one wins is not the interesting part. That there is a second one
is, and nothing said so. The last round wrote this down as "worth its own change rather than a rider"; this
is that change.

**4. `~TransmitTask()` was still there.** Also written down last round. `TryCancel()` raises
`PropertyChanged` — a WPF binding update — and invokes `OnTaskEnd`, the transfer pane's handler. On the
finaliser thread neither is legal, and an exception out of a finaliser ends the process with no dialog and
no log line.

### Taken

| # | Change | Why this one |
| --- | --- | --- |
| 1 | `TransferNoticeText`: a notice names a few and counts the rest | Prerequisite for the rest: three more notices into a one-line control would have made the existing one worse |
| 2 | `LocalUploadScan`: catch per directory, report `FoldersNotRead` | The gap the last round left. A silent, total upload failure on the most ordinary Windows folder layout there is |
| 3 | `TransmitItemKeySet.CaseOnlyDuplicates` | A downloaded file that does not arrive and is not mentioned |
| 4 | Remove `~TransmitTask()` | A process kill with no diagnostic, waiting for the right GC timing |

### Rejected, and why

| Idea | Why not this round |
| --- | --- |
| **Move to .NET 10** | Out of scope by instruction again, and the instruction is right that it is not a rider on a transfer round. Support for .NET 9 ends **2026-11-10**, which is now about ten weeks out. It has been the top of "for the next round" four rounds running and it needs a round that does nothing else |
| **`chmod`-based tests for the unreadable folder** | Works here and is meaningless on CI: Windows has no `chmod`, and the equivalent is an ACL edit through `System.Security.AccessControl`, which does not exist on Linux. That is precisely the "only holds on one platform" shape that turned `main` red last round. The seam (`ILocalDirectoryLister`) stages the refusal instead, and the real `chmod` version was run **outside** the repository to confirm the staged failure matches the platform's |
| **Skip an unreadable folder entirely rather than create it empty** | The folder does exist. Leaving it out of the listing would be a second, quieter lie, and the download side already creates an unwalkable directory as an empty one |
| **Abort the upload when a folder cannot be read** | That is the old behaviour with a message bolted on. A user uploading `C:\Users\me` wants the 40 000 files they can read, not a refusal because `Application Data` exists |
| **Catch every exception per directory** | `IsListingFailure` takes `UnauthorizedAccessException`, `IOException` and `SecurityException` — access, deletion mid-scan, path length, a share going away, a drive being pulled. An `OutOfMemoryException` is about the process, and absorbing it would upload a tree with holes in it and call that success. There is a test |
| **Upload both `Makefile` and `makefile` under mangled names** | Renaming the user's file behind their back, rejected for the same reason the deceptive-name round rejected sanitising. And on upload the destination *can* hold both, so a rename would be wrong in the one direction it would help |
| **Stop the transfer on a case collision** | It is one file out of a folder, and the other files are fine. A warning that names them lets the user fetch the odd one out by hand, which is the only thing that can be done anyway |
| **Report the entries `IsSafeSegment` rejects during the walk** | A local name cannot contain a separator or a colon on Windows, so on the platform this app runs on the branch is unreachable. Adding a fourth notice for it would be code with no caller |
| **Remove `~VmFileTransmitHost()` too** | It only calls `CancellationTokenSource.Cancel(false)` on a source that is never disposed and, by the time it could run, has no registered callbacks — so it cannot throw. Moving the cancel into `Release()` would be tidier but `Release()` is called from `Close()` while `ReConn()` does not re-create the source, so it is a behaviour change on a path nobody can exercise from here. Left alone deliberately |
| **Make `TransmitTask` `IDisposable` to dispose the `CancellationTokenSource`** | Every caller would have to change, and a CTS with no timer and no wait handle taken does not need it. Separate decision |
| **A `ToolTip` on the status line carrying the full list** | The list is now cut down before it reaches the property, so a tooltip would show the same truncated text. Making it show the whole list means a second property and a XAML change that cannot be tried here |
| **Rework `scripts/watch-release-iteration.sh`** | Instructed not to unless it misreports. It does not: run for real this round it reported `idle` with `1 iteration branch(es) still ahead of main`, naming this branch, on top of a green v1.3.0.23 |
| **Session tab mute / read-only / lock; RDP per-monitor selection** | Carried over for the fourth round. Still needs a human at a Windows keyboard |

### What landed

| Commit | |
| --- | --- |
| `45585008` | `fix(transfer): a notice that lists everything fills a one-line status bar with nothing` |
| `95495b91` | `fix(sftp): one unreadable folder no longer cancels the entire upload` |
| `e0be8659` | `fix(transfer): say which files the case rule swallowed instead of losing them quietly` |
| `56d12959` | `fix(transfer): drop the finaliser that ran transfer-pane code on the GC thread` |

New tests: `Tests/Utils/FileTransmit/TransferNoticeTextTests.cs` (13, new file),
`Tests/Model/Protocol/FileTransmit/TransmitTaskFinalizerTests.cs` (1, new file),
`LocalUploadScanTests.cs` 15 cases → 23, `TransmitItemKeySetTests.cs` 12 → 18.

Three new language keys in both `en-us.xaml` and `zh-cn.xaml` (520 keys each, no key in one and not the
other). `README.md` and `README.zh-CN.md` both updated for the two user-visible changes.

### Verification

`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` with SDK 9.0.317 — **0 errors,
120 warnings**, which is the count `main` builds with.

Level 2 of §7 for everything except the finaliser: a throwaway `net9.0` MSTest project compiling
`TransferNoticeText.cs`, `LocalUploadScan.cs`, `TransmitItemKeySet.cs`, `DownloadPathGuard.cs` and the four
test files by absolute path, with a no-op `TestInit`. **76 passed, 0 failed**, nothing excluded. Both new
behaviours were checked against the bug they claim to catch:

- With `IsListingFailure` forced to `false`, **6** of the new scan cases fail.
- With the exact-pair check dropped from `TransmitItemKeySet.Add`, `AnIdenticalPairIsADuplicateAndIsNotReported`
  fails — so the "do not cry wolf on a real duplicate" half is load-bearing.

**The real refusal was also run, outside the repository.** A `chmod 000` directory under `/tmp`: before the
fix, `Enumerate` threw `UnauthorizedAccessException` out of the whole call — the exact failure — and after
it, the folder is skipped, named in `FoldersNotRead`, and the other four entries still come back. That file
is not in the repository, because it can only pass on Unix and only for a non-root account.

The finaliser removal is level 1 plus a shape assertion: the new test reads `Finalize`'s `DeclaringType`,
which is `System.Object` when a type declares no finaliser and the type itself when it does — confirmed
here on a two-class scratch program, since the assertion needs `TransmitTask` loaded and that needs the app.
Its *behaviour* cannot be tested anywhere: it is a garbage collection nobody can schedule.

Not executed anywhere: the `VmFileTransmitHost.AddTransmitTask` notice block, which needs a window. See the
pull request for the manual steps.

### For the next round

1. **.NET 10.** Ten weeks to 2026-11-10. Own round, nothing else in it.
2. An "allow legacy SSH algorithms" per-server toggle, so the remaining CBC / SHA-1 set can leave the default.
3. `~VmFileTransmitHost()` — benign today for the reasons in the rejection table, but the same shape as the
   finaliser this round removed, and it would stop being benign the moment anything registers on that token.
4. The upload side's `IsSafeSegment` skip is silent. Unreachable on Windows today; if the app ever reads a
   case-sensitive network mount it stops being unreachable.
5. Session tab mute / read-only / lock, and RDP per-monitor selection — still waiting on a human at a
   Windows keyboard.

---

## 2026-08-28 — fix round: the upload-scan refusal case only refused on Linux

Branch `cursor/fix-upload-scan-refusal-test-220f`, off `main` at `be003d3d`. No feature work: `main` was red.

[Run 33147128602](https://github.com/chaogei666661/1Remote-plus/actions/runs/33147128602) failed the `Run tests 🧪`
step on `windows-latest` with 381 passed and one failed —
`LocalUploadScanTests.AFolderThatCannotBeNamedOnTheServerIsRefusedLoudly` expected `ArgumentException` and got
`UnauthorizedAccessException`. The case was written on Linux and only holds there:

| | Linux | Windows |
| --- | --- | --- |
| `new DirectoryInfo(Path.DirectorySeparatorChar.ToString()).FullName` | `/` | `C:\` — `\` resolves against the current drive |
| `RemoteFolderName` of that | `""` → `ArgumentException`, as the case wants | `"C"` — nameable **by design**, from the round above |
| what `Enumerate` then does | throws | walks `C:\`, and `GetDirectories()` throws `UnauthorizedAccessException` |

So the case picked an input that Windows can name, and the production behaviour it tripped over is the
drive-root fix the previous round deliberately added (`D:\` → remote `D/`). The test was wrong, not the code.

### Taken

**The refusal case now uses an input neither platform can name.** A colon past the drive qualifier —
`…/1remote-upload-xxxx/stream:evil`. Unix allows a folder to be called that, Win32 reads it as an alternate
data stream, and `DownloadPathGuard.IsSafeSegment` refuses it on both, which is exactly the rule the case is
about. It survives `Path.GetFullPath` on both: Windows normalisation is `GetFullPathNameW` plus short-name
expansion, and neither touches a colon in a non-root component (.NET Core 2.1 stopped validating path
characters during normalisation). The path is never created — the name is decided before anything is listed —
and the case now asserts that the input really is unnameable before asserting the throw, so a future
normalisation change fails with a sentence instead of a puzzle. `APathWithNoNameableComponentIsRefusedRatherThanGuessed`
gained the two path shapes as strings, which is pure string work and therefore gives the same answer here as
on CI. `Ui/` is untouched.

### Verified

Level 2 of §7: the real `LocalUploadScanTests.cs` and `DownloadPathGuardTests.cs` compiled against the real
`LocalUploadScan.cs` and `DownloadPathGuard.cs` in a throwaway `net9.0` project — **37 passed, 0 failed**.
Mutating `RemoteFolderName` to return the unsafe name anyway turns the case red, so it is testing something.
`dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` — 0 errors. The Windows half is
CI's to confirm; the naming decision it depends on is covered by the string assertions, which do run here.

### Not taken

**`Enumerate` still aborts the whole walk on one unreadable subfolder.** That is what the red CI actually
demonstrated: `GetDirectories()` raised `UnauthorizedAccessException` on `C:\`, and in the app that lands in a
`catch` that only logs — so uploading a drive root, or any folder containing one folder the user cannot read,
still ends in silence. Fixing it means catching per-directory and reporting skipped folders the way
`LinksNotFollowed` reports links. Out of scope for a fix round; worth a round of its own.

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

`scripts/watch-release-iteration.sh` was driven through **28 cases** by a stubbed `gh`, covering every
decision it can reach: green-release-fires-once, `--peek` not consuming, a failed run, a cancelled run,
newest-run-wins over list order, a run still in progress, no run, no release, a live branch blocking, a stale
branch not blocking, `--stale-hours 0`, drafts and pre-releases being skipped, newest-`publishedAt`-wins over
list order, `--seed`, a `gh` failure reading as 2 rather than 0, `--json` validity, `--help`, an unknown
option, and six ways of spelling the `origin` URL. **28 passed, 0 failed.**

It was also run for real against this repository, where it reported `10` on a fresh state, `0` on the next
poll, a stale `cursor/*` branch as not blocking, and — once this round's own branch was pushed — `0` with
that branch named as the thing in flight.

**Running it for real is what caught its one real bug.** After the research step added the `upstream` and
`original` remotes the runbook's §2 asks for, `gh repo view` started answering with the *parent fork*: the
watch read this fork's branches while reporting the parent fork's releases, and would have opened a round
for a release that is not ours. The repository now comes from `origin`, parsed out of the remote URL, which
is where the branch list was coming from all along. Six of the 28 cases are that.

Neither harness is in the repository.

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
