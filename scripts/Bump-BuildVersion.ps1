<#
.SYNOPSIS
    Increment AppVersion.Build by one and clear AppVersion.PreRelease.

.DESCRIPTION
    CI owns the build number on main/master: every push there publishes a release, so the number has to
    move without anyone editing it by hand. PreRelease has to end up empty as well, otherwise
    Get-Version.ps1 appends "-beta" to the version and AppVersion.UpdateCheckUrls stops pointing at
    /releases/latest.

    Pass -Build to write a known number instead of incrementing. The workflow uses that to re-apply the
    bump after the MSBuild PreBuild/PostBuild targets have rewritten the file, so the commit it makes
    contains only the two constants this script owns.

.OUTPUTS
    The new version string, in the same shape scripts/Get-Version.ps1 would produce for it.
#>
param (
    [string]$FilePath = (Join-Path $PSScriptRoot '../Ui/AppVersion.cs'),
    [int]$Build = -1
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $FilePath -PathType Leaf)) {
    Write-Error "Error: $FilePath does not exist."
    exit 2
}

$content = [System.IO.File]::ReadAllText($FilePath)

function Read-UintConstant([string]$name) {
    $match = [regex]::Match($content, "public const uint $name = (\d+);")
    if (!$match.Success) {
        Write-Error "Error: $name not found in $FilePath."
        exit 3
    }
    return [int]$match.Groups[1].Value
}

$major = Read-UintConstant 'Major'
$minor = Read-UintConstant 'Minor'
$patch = Read-UintConstant 'Patch'
$oldBuild = Read-UintConstant 'Build'

$newBuild = if ($Build -ge 0) { $Build } else { $oldBuild + 1 }

$content = [regex]::Replace($content, '(public const uint Build = )\d+(;)', "`${1}$newBuild`${2}")
# Only the literal is replaced, so the trailing '// e.g. "alpha" "beta.2"' comment survives.
$content = [regex]::Replace($content, '(public const string PreRelease = ")[^"]*(";)', '${1}${2}')

# UTF8 without a BOM, and WriteAllText leaves the newlines that are already in the text alone, so the
# file comes back byte-identical apart from the two constants.
[System.IO.File]::WriteAllText($FilePath, $content, (New-Object System.Text.UTF8Encoding($false)))

# Same rule as Get-Version.ps1, so the two scripts can never disagree about what this build is called.
if ($newBuild -eq 0) {
    $version = "$major.$minor.$patch"
} else {
    $version = "$major.$minor.$patch.$newBuild"
}

Write-Host "AppVersion: $major.$minor.$patch.$oldBuild -> $version (PreRelease cleared)"

if ($env:GITHUB_OUTPUT) {
    "build=$newBuild" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "tag=v$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

Write-Output $version
