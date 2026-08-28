<#
.SYNOPSIS
    Collects, read-only, the facts an iteration agent needs before it decides what to work on.

.DESCRIPTION
    Prints where this fork stands against its upstreams, what this repository targets, and the list of
    sources that have to be checked by hand. Nothing here writes to the repository, commits, pushes,
    tags, or calls an API that costs money. The one network operation is `git fetch`, and it only ever
    updates remote-tracking refs; pass -Offline to skip even that.

    The runbook that says what to do with the output is .agent_workspace/AUTO_ITERATION.md.

.PARAMETER Offline
    Do not contact any remote. Comparisons then reflect whatever was last fetched.

.EXAMPLE
    pwsh ./scripts/Get-ResearchBriefing.ps1
#>

[CmdletBinding()]
param(
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Section($title) {
    Write-Host ""
    Write-Host "== $title " -NoNewline
    Write-Host ("=" * [Math]::Max(0, 76 - $title.Length))
}

function Invoke-Git {
    # Read-only by construction: every call goes through here, and the caller passes the arguments.
    # A failure is reported rather than thrown, because a missing remote is a normal state for a
    # freshly cloned agent workspace and should not stop the rest of the briefing.
    param([string[]] $Arguments)
    $output = & git -C $repoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { return $null }
    return $output
}

# Upstreams. Neither is added if it is already there, and neither is ever pushed to.
$remotes = @{
    'upstream' = 'https://github.com/chaogei/1Remote-Plus'   # the fork this one came from
    'original' = 'https://github.com/1Remote/1Remote'        # Shawn Veck's project
}

Write-Section "This working copy"
Write-Host ("branch : " + (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')))
Write-Host ("head   : " + (Invoke-Git @('log', '-1', '--pretty=%h %s')))
$dirty = Invoke-Git @('status', '--porcelain')
Write-Host ("state  : " + $(if ([string]::IsNullOrWhiteSpace($dirty -join '')) { 'clean' } else { 'uncommitted changes present' }))

Write-Section "Version"
$appVersion = Get-Content (Join-Path $repoRoot 'Ui/AppVersion.cs') -Raw
foreach ($field in 'Major', 'Minor', 'Patch', 'Build') {
    $match = [regex]::Match($appVersion, "public const uint $field = (\d+)")
    if ($match.Success) { Write-Host ("{0,-6}: {1}" -f $field, $match.Groups[1].Value) }
}
Write-Host "note  : the release workflow owns Build. Do not edit it by hand."

Write-Section "Target framework"
$uiProject = Get-Content (Join-Path $repoRoot 'Ui/Ui.csproj') -Raw
foreach ($match in [regex]::Matches($uiProject, '<TargetFramework[s]?>([^<]+)</TargetFramework[s]?>')) {
    Write-Host ("  " + $match.Groups[1].Value)
}
Write-Host ".NET 9 leaves support on 2026-11-10; .NET 10 is the LTS after it. Check where that stands."

foreach ($name in $remotes.Keys | Sort-Object) {
    $url = $remotes[$name]
    Write-Section "Upstream: $name ($url)"

    if (-not (Invoke-Git @('remote', 'get-url', $name))) {
        Write-Host "not configured. To compare against it, add it read-only:"
        Write-Host "  git remote add $name $url"
        continue
    }

    if (-not $Offline) {
        $null = Invoke-Git @('fetch', '--quiet', $name)
    }

    $counts = Invoke-Git @('rev-list', '--left-right', '--count', "origin/main...$name/main")
    if (-not $counts) {
        Write-Host "no main branch fetched yet."
        continue
    }

    $ahead, $behind = ($counts -split '\s+')
    Write-Host "commits only here      : $ahead"
    Write-Host "commits only upstream  : $behind"

    if ([int]$behind -gt 0) {
        Write-Host ""
        Write-Host "what is waiting upstream (newest first, at most 25):"
        Invoke-Git @('log', '--oneline', '--no-merges', '-25', "origin/main..$name/main") | ForEach-Object { Write-Host "  $_" }
    }
}

Write-Section "Sources to read by hand"
@(
    'https://github.com/1Remote/1Remote/releases              original project releases'
    'https://github.com/chaogei/1Remote-Plus/commits/main     parent fork'
    'https://www.openssh.com/releasenotes.html                OpenSSH client and agent changes'
    'https://dotnet.microsoft.com/platform/support/policy/dotnet-core   .NET support dates'
    'https://learn.microsoft.com/dotnet/desktop/wpf/whats-new WPF release notes'
    'https://msrc.microsoft.com/update-guide                  RDP / CredSSP / Windows advisories'
    'https://github.com/advisories                            advisories for the NuGet packages in Ui.csproj'
    'https://devolutions.net/blog/  https://royalapps.com/blog  https://mremoteng.org  competitors'
) | ForEach-Object { Write-Host "  $_" }

Write-Section "Direct dependencies"
# Deduplicated: a package referenced once per target framework would otherwise be listed several times.
[regex]::Matches($uiProject, '<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"') |
    ForEach-Object { "  {0,-45} {1}" -f $_.Groups[1].Value, $_.Groups[2].Value } |
    Sort-Object -Unique |
    ForEach-Object { Write-Host $_ }
Write-Host "Check each of these against https://github.com/advisories before adding anything new."

Write-Host ""
Write-Host "Next: .agent_workspace/AUTO_ITERATION.md"
Write-Host ""
