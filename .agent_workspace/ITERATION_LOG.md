# Iteration log

One entry per round of the loop described in `.agent_workspace/AUTO_ITERATION.md`. Newest first.

An entry says what was found, what was taken, **what was rejected and why**, and what actually landed.
The rejections are the part that saves the next round its time: do not re-propose one of them without
saying why the reason no longer holds.

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
