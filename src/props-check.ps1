$ErrorActionPreference = 'Stop'
$slnDir = $PSScriptRoot
function Fail($m){ Write-Error $m; exit 1 }
if (-not (Test-Path "$slnDir/Directory.Build.props")) { Fail "Directory.Build.props missing" }
if (-not (Test-Path "$slnDir/Directory.Packages.props")) { Fail "Directory.Packages.props missing" }
$props = Get-Content "$slnDir/Directory.Build.props" -Raw
foreach ($needle in @('<Nullable>enable</Nullable>','<LangVersion>13.0</LangVersion>','<TargetFramework>net10.0</TargetFramework>','<TreatWarningsAsErrors>true</TreatWarningsAsErrors>','<EnableNETAnalyzers>true</EnableNETAnalyzers>')) {
  if ($props -notmatch [regex]::Escape($needle)) { Fail "Directory.Build.props missing: $needle" }
}
# Prove warnaserror: drop a probe with an unused local, build, expect FAILURE, then remove probe.
$probe = "$slnDir/EMaigrator.Core/__WarnProbe.cs"
Set-Content $probe "namespace EMaigrator.Core; internal static class __WarnProbe { static void M() { int unused = 5; } }"
dotnet build "$slnDir/EMaigrator.Core/EMaigrator.Core.csproj" -c Release 2>&1 | Out-Null
$built = $LASTEXITCODE
Remove-Item $probe -Force
if ($built -eq 0) { Fail "warnaserror NOT enforced: unused-variable probe compiled clean" }
Write-Host "props-check OK"
exit 0
