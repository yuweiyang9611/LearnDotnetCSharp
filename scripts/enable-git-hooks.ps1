[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $NoreplyEmail
)

$ErrorActionPreference = 'Stop'

$expectedRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (& git -C $expectedRoot rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not locate the Git repository root.'
}

if (-not [string]::Equals(
        [IO.Path]::GetFullPath($expectedRoot),
        [IO.Path]::GetFullPath($repositoryRoot),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "This script must run from its LearnDotnetCSharp clone: $expectedRoot"
}

$hookPath = Join-Path $repositoryRoot '.githooks\pre-commit'
if (-not (Test-Path -LiteralPath $hookPath -PathType Leaf)) {
    throw "The tracked pre-commit hook was not found: $hookPath"
}

$noreplyPattern = '^(?:noreply@github\.com|[^@\s]+@users\.noreply\.github\.com)$'
if ($NoreplyEmail -and $NoreplyEmail -notmatch $noreplyPattern) {
    throw 'NoreplyEmail must be a GitHub noreply address.'
}

git -C $repositoryRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not configure core.hooksPath.' }

git -C $repositoryRoot config --local user.useConfigOnly true
if ($LASTEXITCODE -ne 0) { throw 'Could not configure user.useConfigOnly.' }

if ($NoreplyEmail) {
    git -C $repositoryRoot config --local user.email $NoreplyEmail
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure user.email.' }
}

$configuredEmail = git -C $repositoryRoot config --local --get user.email
if ($LASTEXITCODE -ne 0 -or -not $configuredEmail -or $configuredEmail -notmatch $noreplyPattern) {
    throw @'
Hooks are enabled, but commits remain blocked until this clone has a GitHub noreply email.
Run this script again with -NoreplyEmail "YOUR_ID+YOUR_USERNAME@users.noreply.github.com".
'@
}

$configuredHooksPath = git -C $repositoryRoot config --local --get core.hooksPath
$useConfigOnly = git -C $repositoryRoot config --local --get user.useConfigOnly
if ($configuredHooksPath -ne '.githooks' -or $useConfigOnly -ne 'true') {
    throw 'Git privacy configuration verification failed.'
}

Write-Host "Git hooks enabled for $repositoryRoot"
Write-Host "hooksPath=$configuredHooksPath"
Write-Host "user.useConfigOnly=$useConfigOnly"
Write-Host "user.email=$configuredEmail"
