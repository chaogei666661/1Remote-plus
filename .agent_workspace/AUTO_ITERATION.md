# Runbook for the next iteration agent

You are one round of a loop. A parent agent wakes on a timer and starts you with a task; you research,
pick a small number of changes, implement them, open a pull request, and report back. The parent verifies
and merges. This file is the part that does not have to be reinvented each time.

Read it before you plan. It is normative: the "Never" list in particular is there because breaking one of
those items has cost a previous round its work.

---

## 1. What this repository is

`chaogei666661/1Remote-plus` — a Windows remote-session manager (RDP, SSH, VNC, Telnet, SFTP, FTP, Serial,
RemoteApp), WPF on .NET 9, Stylet MVVM, SQLite/MySQL/PostgreSQL data sources.

| | |
| --- | --- |
| This fork | `chaogei666661/1Remote-plus` — publishes the binaries, owns its own update-check URLs |
| Parent fork | `chaogei/1Remote-Plus` (capital `P`) |
| Original | `1Remote/1Remote` by Shawn Veck |

Existing notes worth reading before you start: `.agent_workspace/ISSUE_FIXES.md` (what the security and
static-analysis round did), `.agent_workspace/UPSTREAM_MERGE.md` (how upstream was merged and what was
deliberately not taken), `.agent_workspace/ITERATION_LOG.md` (every round so far, and what was rejected —
do not re-propose something that was rejected without saying why the reason no longer holds), and
`DEVELOP.md`.

---

## 2. Research first

Run the read-only briefing:

```powershell
pwsh ./scripts/Get-ResearchBriefing.ps1
```

It prints the branch, the version, the target frameworks, how far ahead of and behind each upstream this
fork is, the direct NuGet dependencies, and the list of sources below. It fetches remote-tracking refs and
nothing else: no commits, no pushes, no tags, no paid API.

Then read, by hand:

| Source | Looking for |
| --- | --- |
| `1Remote/1Remote` releases and nightly | Features and fixes worth porting; the 1.3 line is still in beta |
| `chaogei/1Remote-Plus` commits | The parent fork's direction |
| [OpenSSH release notes](https://www.openssh.com/releasenotes.html) | Client and agent changes: config keywords, key types, agent socket location, deprecations |
| [.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) | Whether the target framework is still supported |
| [WPF release notes](https://learn.microsoft.com/dotnet/desktop/wpf/whats-new) | Fluent theming, performance, obsoletions |
| [MSRC update guide](https://msrc.microsoft.com/update-guide) | RDP, CredSSP, NLA, Windows Hello advisories |
| [GitHub advisories](https://github.com/advisories) | The packages the briefing listed |
| Devolutions RDM, Royal TS, mRemoteNG, Termius, Tabby, MobaXterm, Guacamole, RustDesk, MeshCentral | What users are being offered elsewhere |

Competitor research is for finding gaps, not for copying feature lists. A feature only counts if a user of
*this* app, on Windows, would reach for it.

---

## 3. Priorities

In this order. A lower item never displaces a higher one.

1. **Security.** A credential that leaks, a file written where anyone can read it, an identity that is not
   verified, a dependency with a live advisory.
2. **Crashes and data loss.** An unhandled exception on a normal path, a session that cannot be closed, a
   database or backup that can be corrupted.
3. **Enterprise fitness.** Deployment, silent install, portable mode, group policy, logging, accessibility,
   whatever a fleet of managed desktops needs and one enthusiast's laptop does not.
4. **Everyday experience.** Import fidelity, empty states, discoverability, keyboard paths, quality of the
   information the UI already shows.

A round that is entirely category 4 is a fine round, as long as nothing in 1–3 was found and skipped.

---

## 4. Never

- **Never edit `Ui/AppVersion.cs`'s `Build`.** `.github/workflows/build-on-dev-push.yml` bumps it on every
  push to `main`, via `scripts/Bump-BuildVersion.ps1`. Touching it by hand produces a conflict or a
  duplicated release.
- **Never undo hardening that is already there.** The `cmd://` trust-on-first-use gate, the WebDAV
  HTTPS requirement, the SFTP/FTPS host-key store, the per-session temp directories with restricted ACLs,
  the placeholder-salt warning. If one of them is genuinely in the way, say so in the report and leave it
  alone. See `.agent_workspace/ISSUE_FIXES.md` for why each exists.
- **Never force-push, never amend a pushed commit, never rewrite history.**
- **Never merge your own pull request** or turn on auto-merge. The parent merges.
- **Never leave the branch you were told to work on** unless you were asked to.
- **Never nest subagents without being asked.** One round, one agent, doing its own searching and editing.
- **Never add CI that pushes, tags, releases, or calls a paid API.** The loop is driven by the parent's
  timer, not from inside the repository. A workflow that summons agents needs an external key and burns
  budget with no one watching.
- **Never claim something was tested when it was compiled.** See §7.

---

## 5. Choosing the work

Pick **two to four** changes. Fewer than two and the round is not worth its overhead; more than four and
the pull request stops being reviewable and stops being revertible one piece at a time.

Each one has to be:

- **Finishable here.** No feature that needs a Windows GUI to be developed, a paid service, or a secret
  this repository does not have.
- **Independently valuable.** If the parent drops one commit, the rest still make sense.
- **Small in blast radius.** Prefer new files and additive properties over rewrites of the connect path.

Files another agent is likely to be rewriting at the same time — `ExternalSecret*`, `PasswordVault*`,
`HostTrustService`, the backup zip path validation — should be left alone. If you must touch one, make the
smallest backwards-compatible change you can and say so prominently in the report.

Ideas that keep coming up and have not been done: see the "Not taken" sections of
`.agent_workspace/ITERATION_LOG.md` first, then consider session tab mute/read-only/lock, richer batch
send-command, a reduce-transparency / high-contrast mode for managed desktops, portable and silent-install
documentation, Royal TS and Devolutions import, first-run and empty states, SSH agent (Pageant / Windows
OpenSSH agent) support, and the .NET 10 move.

---

## 6. Implementing

- Match the surrounding code: naming, comment density, `SetAndNotifyIfChanged`, `RelayCommand`, Stylet
  view models, `IoC.Translate` for user-visible text.
- Every user-visible string goes in **both** `Ui/Resources/Languages/en-us.xaml` and `zh-cn.xaml`. The other
  locales fall back and are left alone — that is the existing convention for fork-added strings.
- Put logic that can be tested without a window in a plain class under `Ui/Utils/` or `Ui/Model/`, and let
  the view model be a thin wrapper over it. This is the single thing that most decides whether a change can
  be verified at all from here.
- Update `README.md` **and** `README.zh-CN.md` when behaviour a user can see changes.
- One commit per logical change, with a message that says what was wrong before, not just what was added.

---

## 7. Verifying, honestly

The whole solution targets `net9.0-windows10.0.19041.0`. On the Linux box an agent runs on:

```bash
# .NET 9 SDK, if it is not already there
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

git submodule update --init --recursive
dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true
```

`-c Debug` matters: the `PreBuild` target of `Ui.csproj` shells out to `powershell.exe` for any other
configuration.

**The test suite cannot be executed here.** The test host needs `Microsoft.WindowsDesktop.App`, which does
not exist for Linux. CI runs it on `windows-latest` for every push and pull request, and that is the first
place the tests actually run.

What to do about that, in order of preference:

1. Write the tests anyway, in `Tests/`, following the conventions there (`[TestInitialize] TestInit.Init()`,
   names that are sentences, comments that say why the case matters). CI runs them.
2. **Additionally**, for pure logic, mirror the assertions in a throwaway `net9.0` console project outside
   the repository that `<Compile Include="…" />`s the file under test, and run it. This actually executes
   the logic and catches the arithmetic and off-by-one mistakes that a compile cannot. It is cheap and it
   is the difference between "compiles" and "works". Delete it afterwards; it is not part of the repository.
3. For anything that needs a window, write down the manual steps a human would follow, in the pull request,
   precisely enough to be run without re-reading the diff.

Say which of the three you did, per change. "Compiles" is not "passes".

---

## 8. Branch, pull request, report

```bash
git checkout -b cursor/<short-descriptive-name>-<suffix>   # lowercase; the parent gives you the suffix
git add -A && git commit -m "…"
git push -u origin cursor/<short-descriptive-name>-<suffix>
```

Push before opening the pull request, and push again after every further commit — the parent may look at
any time.

The pull request is written **in Chinese** and contains:

- What was researched and what came of it, including what was considered and rejected, and why.
- Each change: what was wrong before, what it does now, which files.
- How each was verified, using the three levels above.
- What was deliberately not done.
- Anything the reviewer has to check on Windows.

If the tooling available to you cannot create a pull request (the GitHub CLI in this environment is
read-only), push the branch, say so plainly in the report, and give the parent the branch name and the
compare URL.

Report to the parent, in Chinese: research summary, what landed, branch name, pull request URL, how to
verify, what was left, and a merge recommendation.

---

## 9. Merge policy

The parent agent, not you:

1. Waits for CI on `windows-latest` to be green.
2. Reviews the diff against this runbook — especially §4.
3. Merges into `main` and deletes the branch.
4. Appends the round to `.agent_workspace/ITERATION_LOG.md` if the round did not.

`main` pushes trigger a version bump and a GitHub release, so a merge is a publish. Do not merge anything
whose Windows behaviour nobody has looked at.

---

## 10. Before you finish

- [ ] `dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true` — 0 errors.
- [ ] `Ui/AppVersion.cs` untouched.
- [ ] New user-visible strings in both `en-us.xaml` and `zh-cn.xaml`.
- [ ] `README.md` and `README.zh-CN.md` updated if a user can see the change.
- [ ] A round appended to `.agent_workspace/ITERATION_LOG.md`, including what was rejected.
- [ ] Branch pushed; every commit on it pushed.
- [ ] Report written in Chinese, verification claims accurate.
