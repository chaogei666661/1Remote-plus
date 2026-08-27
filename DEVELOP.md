# Development

This guide provides information on how to setup development environment on local machine.

It assumes no local tools and empty Windows 10 OS.

## Prerequisites

1. `Windows 10` 1703 or later
2. `Microsoft Visual Studio 2022` or higher, with the following workloads:
    - .NET desktop development
    - `.NET 9 SDK` — the default target framework is `net9.0-windows10.0.19041.0`
    - Windows 10 SDK / targeting pack `10.0.19041.0` — nothing builds without this exact one,
      because the TFM names it
3. Git with submodule support (see below)

The build task `Deps` automates entire installation locally (except OS). More details on running tasks are given bellow.

### Submodules

`Ui.csproj` has project references into `Shawn.Utils`, `Shawn.Utils.Wpf`, `Shawn.Utils.WpfResources` and
`Dragablz`, and the bundled PuTTY binaries are a submodule too. A plain `git clone` leaves those
directories empty and the build fails on missing projects, so after cloning run:

```ps1
git submodule update --init --recursive
```

`VncSharpCore` is the odd one out. It is a submodule *and* it is in `1Remote.sln`, but `Ui.csproj`
consumes VNC through the `1Remote.VncSharpCore` NuGet package rather than the project — the submodule is
there for building a patched VNC control locally and diffing it against the package. Both are kept
deliberately; do not remove either assuming the other is dead.

## Build

### Manual

1. Clone repository
2. `git submodule update --init --recursive`
3. Open solution in Visual Studio 2022
4. [Restore all NuGet packages](https://docs.microsoft.com/en-us/nuget/consume-packages/package-restore#restore-packages-manually-using-visual-studio)
5. Build

Now you can build solution.

### Target frameworks

**`net9.0-windows10.0.19041.0` is the only target CI builds**, through the `Release` configuration and the
two `x64-net90*` publish profiles. The `ReleaseNet6` and `ReleaseNet48` configurations, their publish
profiles and the `#if NETFRAMEWORK` branches they feed are unmaintained legacy: they are kept because
Visual Studio users and the Store packaging project still reference the configuration names, but nothing
verifies that they still compile. Treat a break in them as expected, not as a regression, and do not add
code that only works on one of them.

## Tests

```ps1
dotnet test Tests/Tests.csproj -c Debug
```

`Tests/Tests.csproj` is in `1Remote.sln` and runs in CI on every push, before the publish steps. It
targets the same TFM as `Ui` — a test project cannot reference a project built for a newer framework — so
it needs the same SDK and targeting pack, and it will not run on a non-Windows machine.

`Tests/TestInit.cs` is the seam that makes any of this testable: it seeds the string cipher with a
test-only salt and replaces the `IoC.GetByType` delegate with a stub. Call it from a `[TestInitialize]`
in any new fixture that touches app statics.

## Versioning

`Ui/AppVersion.cs` is the source of truth for the version, but on `main` it is **not** hand-edited: every
push there runs `scripts/Bump-BuildVersion.ps1` in CI, which increments `Build` by one and clears
`PreRelease`, and the workflow commits that back as `chore(release): v<version> [skip ci]`. So do not
edit `Build` in a commit destined for `main` — CI will bump on top of whatever it finds, and the number
you wrote is simply the base it counts from. `Major`/`Minor`/`Patch` are still yours to change.

`PreRelease` is deliberately kept empty on this fork. It is what `AppVersion.UpdateCheckUrls` keys off:
empty means the in-app check reads `/releases/latest`, which only ever resolves to a full release, and it
is also what keeps `scripts/Get-Version.ps1` from appending `-beta` to the version. Setting it back to
`"beta"` would take effect for exactly one build, because the next CI bump clears it again.

During a non-Debug build the `PreBuild` target runs `scripts/Set-AssemblyVersion.ps1`, which overwrites
`<AssemblyVersion>` in `Ui.csproj` with `Major.Minor.Patch.<date stamp>`; the value committed in the
csproj is therefore always a leftover from whoever built last and is not worth editing.
`scripts/Set-BuildDate.ps1` stamps `AppVersion.BuildDate` the same way and reverts it in `PostBuild`. The
release job restores both files before committing, so the bump commit carries only the two constants it
owns. CI reads the same constants through `scripts/Get-Version.ps1`, which is what names the artifacts,
the tag and the release.

## Automatic releases

`.github/workflows/build-on-dev-push.yml`:

| event | bump | publish | release |
| --- | --- | --- | --- |
| push to `main`/`master` | yes | yes | yes, `v<version>`, never a pre-release |
| push to another `*main*`/`*master*` branch | no | artifacts only | no |
| pull request | no | no | no |
| `workflow_dispatch` | only on `main`/`master` | as above | as above |

The release is created with `gh release create --latest` and no `--prerelease`, so it is always a full
release and `/releases/latest` points at it. Re-running the workflow over a tag that already shipped
updates that release instead of failing.

Three things stop this from looping:

- the bump commit's subject contains `[skip ci]`, which GitHub honours by not starting a workflow, and
  the job's `if` rejects `chore(release):` subjects a second time in case that ever changes
- there is no `tags:` trigger, so the version tag the workflow pushes cannot start a second build
- the release step is an upsert, so nothing double-publishes

If you need to push to `main` without cutting a release, put `[skip ci]` in your own commit subject.

## Security-relevant behaviour

See the "Security notes" section of [readme.md](readme.md) for the threat model: what the placeholder
encryption salt means for a fork build, what `1Remote.db` does and does not protect against, what Windows
Hello actually gates, why `cmd://` secret references need a per-machine approval, and why WebDAV backups
require HTTPS.

### Command line

Build is automated using [Invoke-Build] PowerShell module which is included in the repository, but can be also [installed in the system](https://github.com/nightroman/Invoke-Build#install-as-module).

1. open `administrative PowerShell`
2. go to repository root
3. run `Set-Alias ib $pwd\Invoke-Build.ps1` (For convenience, set alias to it)
4. run `ib ?` to get list of available tasks (anywhere in the repository directory hierarchy):

```
PS C:\Projects\PRemoteM> ib ?

Name           Jobs Synopsis
----           ---- --------
Deps           {}   Ensure local dependencies
Build          {}   Build the application
BuildInSandbox {}   Build in Windows Sandbox
Clean          {}   Clean generated data

```

Tasks are defined in the [prm.build.ps1] PowerShell script.

For example, to clean any existing builds and then build fresh PRemoteM as portable Win32 application invoke:

```ps1
ib Clean, Build -aReleaseType Release

# Equivalent without setting alias, must be run in root of the repository
./Invoke-Build.ps1 Clean, Build -aReleaseType Release

# Equivalent with system install of Invoke-Build
Invoke-Build Clean, Build -aReleaseType Release
```

Please check out [invoke-build](https://chocolatey.org/packages/invoke-build) package notes on how to enable task auto completion and other tips.

Task `BuildInSandbox` starts [Windows Sandbox] and executes `ib Deps, Build` tasks. This takes some time (~20 minutes) as all dependencies are downloaded from the Internet and installed, using [Chocolatey] package manager, but it guaranties pristine environment. Note that when you close the sandbox entire environment is gone.


## Releases

### GitHub lists releases by tag name, not by date

The releases page and `GET /repos/:owner/:repo/releases` both order by tag name as a string, in spite of
what the API docs say about reverse chronological order. Once the build number reaches two digits the list
stops looking chronological:

```
v1.3.0.9-beta     <- sorts first, but is not the newest
v1.3.0.8-beta
...
v1.3.0.2-beta
v1.3.0.10-beta    <- the newest build, published hours after v1.3.0.9-beta
v1.3.0.1-beta
```

After the shared `v1.3.0.` prefix the next character decides, so `9` and `2` both beat the `1` that starts
`10`. Nothing on GitHub's side can reorder this: the sort key is the tag name, so the only way to change the
order is to rename the tags. Renaming published tags breaks the download links people already have, so we
live with it and read the list correctly instead — `AboutPageViewModel.CustomCheckMethod` compares every tag
it finds numerically rather than trusting the page order.

Anything else reading that list is subject to the same order and is outside our control. The release badge
in the readme, for one, reports `v1.3.0.9-beta` while `v1.3.0.10-beta` is out, because shields.io takes the
first entry the API returns.

If the order itself ever needs to be right on github.com, it takes a new tag scheme applied going forward,
neither of which is in place today:

- zero-pad the build, `v1.3.0.010-beta`, which sorts correctly as a string up to 999 builds
- move the build into the pre-release part, `v1.3.0-beta.10`, which is what SemVer intends

[Microsoft Visual Studio 2019]: https://visualstudio.microsoft.com/vs
[Windows 10]:       https://www.microsoft.com/en-us/software-download/windows10
[Invoke-Build]:     https://github.com/nightroman/Invoke-Build
[Windows Sandbox]:  https://docs.microsoft.com/en-us/windows/security/threat-protection/windows-sandbox/windows-sandbox-overview
[Chocolatey]:       http://chocolatey.org
[prm.build.ps1]:    https://github.com/VShawn/PRemoteM/blob/dev/prm.build.ps1