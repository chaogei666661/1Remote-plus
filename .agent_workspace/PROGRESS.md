# Orchestrator progress & long-running state

This file implements the "首次运行初始化" state that the orchestrator spec asks for. It is **subordinate**
to `.agent_workspace/AUTO_ITERATION.md`, which stays the normative runbook (priorities, the "Never" list,
honest verification, and merge policy). Where the two disagree, `AUTO_ITERATION.md` wins — it is battle
tested and its rules exist because breaking one cost a previous round its work.

## Model policy (current)

Every subagent this orchestrator launches runs on **the current model**, requested through the Task tool as:

```text
model: inherit
```

`inherit` means "the same model as the parent orchestrator", which is the current model
(**Claude Opus 4.8**). This is the only faithful way to say "use the current model": Claude Opus 4.8 has no
standalone Task slug, so a literal slug would either name a *different* model or fail. `inherit` therefore
supersedes the spec's "no inherit" clause, which was written on the assumption of named per-role slugs.

Every subagent's first output line must still be, per the spec:

```text
MODEL: <the slug the platform reports> (inherit -> Claude Opus 4.8)
```

Subagents run with `environment: cloud` and use the Task tool, as required. Local subagents are not used.

## Reconciliation with AUTO_ITERATION.md

The repo already runs an AI iteration loop. Two parts of the orchestrator spec are deliberately overridden
to match it, because the repo's rules are the tested ones:

| Spec asks for | This repo does instead | Why |
| --- | --- | --- |
| 6 parallel editing agents per round | One editing agent per round (§4 "Never nest subagents without being asked"); parallelism only for read-only reconnaissance | Six agents editing one WPF solution collide; the runbook forbids it |
| Auto-merge on local "build + tests pass" | Parent merges only after CI is green on `windows-latest` (§9) | The full MSTest suite cannot run on Linux; only CI runs it |
| 10-minute Watchdog clock drives cycles | Loop is **release-triggered** (§0); `WATCHDOG.md` records state, it does not start rounds | A clock starts rounds on a red `main`; a release does not |

A background agent cannot literally sleep for an infinite series of 10-minute cycles across turns. The
Watchdog here is a *state record* the next round reads, not a live daemon.

## Baseline (established this round)

- **Product:** `chaogei666661/1Remote-plus` — Windows remote-session manager (RDP, SSH, VNC, Telnet, SFTP,
  FTP, Serial, RemoteApp), WPF on .NET 9, Stylet MVVM, SQLite/MySQL/PostgreSQL.
- **Version:** `v1.3.0.27` (`Ui/AppVersion.cs`; `Build` is CI-owned — never hand-edit).
- **Target framework:** `net9.0-windows10.0.19041.0` (the only TFM CI builds; ReleaseNet6/Net48 are
  unmaintained legacy).
- **Tests:** 507 `[TestMethod]`/`[DataTestMethod]` across 46 files in `Tests/`. Full suite runs in CI on
  `windows-latest`; windowless subsets run on Linux via the §7.2 throwaway harness.
- **Submodules:** `Shawn.Utils`, `Dragablz`, `VncSharpCore`, `Ui/Resources/PuTTY`.
- **CI:** `.github/workflows/build-on-dev-push.yml` — a push to `main` bumps the version, publishes and
  cuts a `v<version>` release; that release is what starts the next round.
- **Branch / latest commit:** see `git log`; this round works on `cursor/agent-env-and-orchestrator-167c`.
- **Known-good verification path (Linux):**
  ```bash
  bash .cursor/install.sh   # .NET 9 SDK + submodules + warm build
  dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true   # 0 errors
  ```
- **Recent security/stability work (do not undo — see ISSUE_FIXES.md):** RDP .rdp password scoping,
  secret-access audit, session-script/`cmd://` trust-on-first-use, WebDAV HTTPS, Thai-locale logger guard.

## Open threads for the next round

- Candidate work lives in the "Not taken" sections of `ITERATION_LOG.md` and §5 of `AUTO_ITERATION.md`.
- Nothing is in flight on an `agent/*` or `cursor/*` branch that blocks a new round except this one.
