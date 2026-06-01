<#
.SYNOPSIS
  Fails (non-zero exit) if `dotnet list package --vulnerable` reports any vulnerability.
.PARAMETER InputFile
  Optional path to a file containing pre-captured `dotnet list package` output (for tests).
  When omitted, this script runs the real command against src/EMaigrator.sln.
#>
param([string]$InputFile)

$ErrorActionPreference = 'Stop'

if ($InputFile) {
  $output = Get-Content -Raw -Path $InputFile
} else {
  $sln = Join-Path $PSScriptRoot '..' 'src' 'EMaigrator.sln'
  dotnet restore $sln | Out-Null
  $output = dotnet list $sln package --vulnerable --include-transitive 2>&1 | Out-String
}

Write-Host $output

# A vulnerability is present when dotnet prints "has the following vulnerable packages"
# and/or advisory rows marked with a leading ">". Clean output says "no vulnerable packages".
$hasVuln = ($output -match 'has the following vulnerable packages') `
  -or ($output -match '(?m)^\s*>\s')

if ($hasVuln) {
  Write-Error 'Vulnerable NuGet packages detected — failing the build.'
  exit 1
}
Write-Host 'No vulnerable packages detected.'
exit 0
