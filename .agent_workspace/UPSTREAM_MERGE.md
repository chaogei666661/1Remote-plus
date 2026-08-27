# Merging chaogei/1Remote-Plus into this fork

This note records how the 69 upstream commits were combined with the 18 commits of static-analysis and
security work that PR #2 had already reviewed. It is written for whoever has to redo this the next time
upstream moves.

## What was merged

| | |
| --- | --- |
| This fork | `chaogei666661/1Remote-plus` |
| Parent (upstream) | `chaogei/1Remote-Plus` — note the capital `P` |
| Original | `1Remote/1Remote` by Shawn Veck |
| Merge base | `19def90d` "Bump the build to 1.3.0.7-beta" |
| Our branch | `cursor/implement-static-analysis-fixes-f12f` @ `7b5009ec`, 18 commits |
| Upstream head | `cdfdb547` "Bump the build to 1.3.0.17-beta", 69 commits |
| Result | version `1.3.0.18-beta` |

`git rev-list --left-right --count origin/main...upstream/main` was `0 69` before the merge, so `main` was
strictly behind: nothing of ours had to be rebased over upstream, only combined with it.

## Strategy

`main` was never moved to a half-merged state. The work was done on
`cursor/merge-upstream-plus-our-fixes-4d4b`, branched from `origin/main`:

1. Merge `origin/cursor/implement-static-analysis-fixes-f12f` — clean, `89c31dd1`.
2. Merge `upstream/main` — 12 conflicts, resolved by hand, `84bb53cc`.
3. Merge that branch into `main` and push. No force push, no rewritten history.

Nothing was resolved with `-X ours` or `-X theirs`. Every conflict was read and both sides' intent kept.

## Conflicts and how each was resolved

Eleven of the twelve were the same disagreement: upstream `46c58902` repointed the product's own URLs at
`chaogei/1Remote-Plus`, while our `5dfe58c1` had pointed them at `chaogei666661/1Remote-plus`. This
repository is the one that builds and publishes these binaries, so its own URLs have to be its own —
an update check aimed at a repository that does not publish this build was the bug that `5dfe58c1` fixed
in the first place. All of them resolved to `chaogei666661/1Remote-plus`.

| File | Conflict | Resolution |
| --- | --- | --- |
| `CODE_OF_CONDUCT.md` | issue URL | ours (fork URL) |
| `Ui/AppVersion.cs` | update-check and publish URLs | ours (fork URL), plus `Build` bumped 17 → 18 |
| `Ui/Service/TaskTrayService.cs` | "report a bug" URL | ours |
| `Ui/View/AboutPageView.xaml` | repository link, issues link | ours |
| `Ui/View/ErrorReport/ErrorReportWindow.xaml{,.cs}` | issues URL and tooltip | ours |
| `Ui/View/Guidance/Intro.xaml` | project URL | ours |
| `Ui/View/RequestRatingView.xaml` | issues URL | ours |
| `Ui/View/Settings/ProtocolConfig/ExternalRunnerSettingsViewModel.cs` | runner-sharing issue template URL | ours |
| `Tests/View/AboutPageUpdateCheckTests.cs` | fork URL vs upstream's new `ignore` argument | **both**: the fork URL *and* the `ignore` parameter, so upstream's two new ignore-a-build tests still exercise what they were written for |
| `Ui/Model/Protocol/Base/ProtocolBaseWithAddressPort.cs` | one blank line | upstream's blank line; the real content of that hunk (upstream dropping a `using static` for CS0104, ours deep-copying `AlternateCredentials` on clone) merged cleanly |
| `readme.md` | modify/delete — upstream deleted it and wrote `README.md` + `README.zh-CN.md` | took the deletion, folded our security section into both new READMEs (below) |

### README

Upstream replaced the old `readme.md` with a real usage guide in English and Simplified Chinese. That guide
is kept in full. On top of it:

- A **Security notes** section in both files, carrying the whole threat model from our old readme —
  placeholder salt, what `1Remote.db` actually protects against, what Windows Hello gates, `cmd://` being a
  shell-out gated by a per-machine approval, WebDAV https-only, SFTP/FTPS host identity — plus a line about
  per-session temp files. Both tables of contents were updated.
- A **Hardening added in `chaogei666661/1Remote-plus`** block inside "What is different in this fork", so
  the reader can tell which changes come from which fork.
- The fork disclaimer now names the whole chain: this repository, the parent `chaogei/1Remote-Plus`, and the
  original `1Remote/1Remote`. Credits and the copyright line name all three.
- Every badge and link points at this repository.

## Files that merged cleanly but needed checking

Auto-merged text can still be a semantic conflict. These were read after the merge:

- **`Ui/View/Host/ProtocolHosts/AxMsRdpClient09Host*`** — the one that could have gone wrong. We had split
  the class into `AxMsRdpClient09Host.xaml.cs` and a new `AxMsRdpClient09Host.Settings.cs` partial; upstream
  independently rewrote `Conn()` in the same file and added `WaitForEndpointReadyAsync` /
  `ConnectWhenEndpointReadyAsync` for the retry-after-reboot path. The 510 lines we moved out and the hunks
  upstream changed do not overlap, so both survive: the retry logic sits in `AxMsRdpClient09Host.cs` and
  `.xaml.cs`, the settings mapping stays in our `.Settings.cs`, and there are no duplicate members.
- **`.github/workflows/build-on-dev-push.yml`** — union of both. Still has recursive submodules, the
  fork-safe secret substitution that warns instead of failing when a secret is absent, `actions/cache@v4`,
  `checkout@v4`, `dotnet restore` + `dotnet test` before any publish step, the `pull_request` trigger on
  main/master with the publish steps skipped, and upstream's release-notes script and tag-titled releases.
- **`Ui/Assert.cs`** — upstream's `APP_DISPLAY_NAME` = "1Remote Plus" together with our
  `IsUsingPlaceholderSalt`.
- **`Ui/AppInit.cs`, `Ui/Bootstrapper.cs`, `Ui/App.xaml.cs`** — our one-shot placeholder-salt warning and
  `ShutdownWatchdog` alongside upstream's glass and session work.
- **`Ui/View/Settings/Backup/BackupSettingView.xaml`** — our `AllowInsecureHttp` checkbox and red warning on
  top of upstream's glass restyle.
- **`Ui/Resources/Languages/*.xaml`** — our `external_secret_trust_*` and `webdav_*` keys alongside
  upstream's "1Remote Plus" retitles, in both `en-us` and `zh-cn`.
- **CS0104 fixes** — upstream removed the stray `using static` directives and the ambiguous `Task` import in
  `CredentialViewModel`. Confirmed still removed after the merge; only two deliberate `using static`
  directives remain in `Ui/`.
- **`.gitignore`** — our root-anchored `/Backup/` and `/Backup[0-9]*/` replace the unanchored `Backup*/`.
- **`Ui/Ui.csproj`** — upstream's `AssemblyTitle` "1Remote Plus" with `RepositoryUrl` pointing here; the
  copyright line now names Shawn Veck, chaogei and chaogei666661.
- **API shapes** — `ProtocolBase.IsSelectedRunnerInternal(ProtocolConfigurationService)` and
  `ExternalSecretResolver` have exactly one definition and consistent call sites; upstream added no callers
  of the old signatures.

## One upstream change deliberately not taken

Upstream commit `58b8dd59` added `.cursor/mcp.json` and `.cursor/rules/mcp-messenger-1Remote.mdc`. These are
one developer's local editor tooling: the config points at `c:\Users\Administrator\...`, and the rule is
`alwaysApply: true`, so it instructs any AI assistant opening this repository to route all communication
through an MCP server that does not exist for anyone else. They are not part of the product and they change
the behaviour of tooling for every contributor, so they were dropped here. Nothing else from upstream was
discarded.

## Verification

Done on Linux with the .NET 9 SDK (9.0.317) and `-p:EnableWindowsTargeting=true`, after
`git submodule update --init --recursive`:

- `Ui/Ui.csproj` — **build succeeded, 0 errors** (169 warnings, all pre-existing nullable/analyzer noise).
- `Tests/Tests.csproj` — **build succeeded, 0 errors**.
- No conflict markers anywhere in the tree.
- The suite could **not** be executed here: the test host needs `Microsoft.WindowsDesktop.App` 9.0.0, which
  does not exist on Linux. Execution happens in CI on `windows-latest`, which the merged workflow runs on
  every push and pull request. The 172 tests that passed on PR #2 have not been re-run against this tree.

The pre-build target shells out to `powershell.exe`; the two build checks above used a no-op stub on `PATH`
in its place. That skips the assembly-version stamp and the secret substitution, neither of which affects
whether the code compiles.

## Remaining risk

- **Nothing here was run.** WPF, the RDP ActiveX host and the acrylic work are Windows-only and were checked
  by compilation and by reading, not by launching the app. The riskiest single area is
  `AxMsRdpClient09Host`, where a partial-class split and a rewritten connect path landed on the same file
  from two directions.
- **The test suite has not executed against the merged tree.** CI on `windows-latest` is the first place it
  will.
- **`1.3.0.18-beta` is one build ahead of upstream's `1.3.0.17-beta`.** If upstream publishes an
  `1.3.0.18-beta` of its own, the two tags mean different things — but they live in different repositories
  and this fork's update check only reads its own releases, so nothing is misled.
- **Placeholder salt still applies.** This repository has no `GLOBAL_STRING_ENCRYPTION_SLAT` secret unless
  one was added, so released builds keep the public constant and say so at launch and on the About page.
- **PR #2 is now obsolete**: its 18 commits are ancestors of `main`. It was left open rather than closed
  through the API; GitHub usually closes it automatically once the commits appear on the base branch.

## Resulting history

```
84bb53cc Merge upstream 1Remote Plus 1.3.0.17-beta with this fork's hardening work
|\
| * cdfdb547 Bump the build to 1.3.0.17-beta          (upstream/main, 69 commits)
| * ...
* | 89c31dd1 Merge remote-tracking branch 'origin/cursor/implement-static-analysis-fixes-f12f'
|\|
| * 7b5009ec ci: run tests on pull requests against main   (our branch, 18 commits)
| * ...
|/
* 19def90d Bump the build to 1.3.0.7-beta             (merge base, old origin/main)
```
