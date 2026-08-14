[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& "$root\scripts\setup-python.cmd"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet restore "$root\LearnDotnetCSharp.slnx" --ignore-failed-sources --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build "$root\LearnDotnetCSharp.slnx" --configuration Release --no-restore --nologo `
    -p:EnforceCodeStyleInBuild=true
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# Folder mode verifies whitespace without loading the mixed C#/F#/VB project graph.
dotnet format whitespace "$root" --folder --verify-no-changes `
    --include "$root\src" "$root\tests" --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test --solution "$root\LearnDotnetCSharp.slnx" --configuration Release --no-build --minimum-expected-tests 30
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run --project "$root\src\LearnDotnetCSharp.App" --configuration Release --no-build -- self-test
