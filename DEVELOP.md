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

`Ui/AppVersion.cs` is the human-edited source of truth — bump `Major`/`Minor`/`Patch`/`Build`/`PreRelease`
there and nowhere else. During a non-Debug build the `PreBuild` target runs
`scripts/Set-AssemblyVersion.ps1`, which overwrites `<AssemblyVersion>` in `Ui.csproj` with
`Major.Minor.Patch.<date stamp>`; the value committed in the csproj is therefore always a leftover from
whoever built last and is not worth editing. `scripts/Set-BuildDate.ps1` stamps `AppVersion.BuildDate` the
same way and reverts it in `PostBuild`. CI reads the same constants through `scripts/Get-Version.ps1`,
which is what decides whether a tag publishes as a stable release or a pre-release.

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

[Microsoft Visual Studio 2019]: https://visualstudio.microsoft.com/vs
[Windows 10]:       https://www.microsoft.com/en-us/software-download/windows10
[Invoke-Build]:     https://github.com/nightroman/Invoke-Build
[Windows Sandbox]:  https://docs.microsoft.com/en-us/windows/security/threat-protection/windows-sandbox/windows-sandbox-overview
[Chocolatey]:       http://chocolatey.org
[prm.build.ps1]:    https://github.com/VShawn/PRemoteM/blob/dev/prm.build.ps1