# Watchdog state

A state record the next round reads to know where the loop left off — not a live daemon. The real loop is
release-triggered (`.agent_workspace/AUTO_ITERATION.md` §0); a background agent cannot sleep across an
unbounded series of fixed-interval cycles.

```yaml
last_cycle: 1
last_round: 1            # reconnaissance + environment bring-up
last_success: 2026-08-29T05:00Z
current_branch: cursor/agent-env-and-orchestrator-167c
current_task: "Cloud Agent environment (.NET 9 SDK + submodules + build/test path) and orchestrator baseline"
status: environment-ready
blocker: "cloud subagent quota exhausted — enable on-demand cloud usage before the multi-agent loop can fan out"
```

## Health checks the next round should run first

- Is any `cursor/*`/`agent/*` branch ahead of `main` and still in flight? (blocks a new round unless stale)
- Is CI on `main` green and the matching release published? (green + released ⇒ start; red ⇒ fix round)
- Do `PROGRESS.md` and `ITERATION_LOG.md` agree on what last landed?
- Does `bash .cursor/install.sh` still end with a clean `dotnet build Tests/Tests.csproj` (0 errors)?

If state cannot be confirmed, stop writing, preserve the branch, and report — do not re-apply changes that
may already be merged.
