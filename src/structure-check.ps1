# src/structure-check.ps1 — fails (non-zero exit) until solution + reference graph exist.
$ErrorActionPreference = 'Stop'
$slnDir = $PSScriptRoot

function Fail($msg) { Write-Error $msg; exit 1 }

if (-not (Test-Path "$slnDir/EMaigrator.sln")) { Fail "EMaigrator.sln missing" }

$expected = @(
  'EMaigrator.Core','EMaigrator.Connectors.Imap','EMaigrator.Connectors.Graph',
  'EMaigrator.Connectors.Gmail','EMaigrator.Infrastructure','EMaigrator.Workers',
  'EMaigrator.Api','EMaigrator.Cli',
  'EMaigrator.Core.Tests','EMaigrator.Connectors.Imap.Tests','EMaigrator.Connectors.Graph.Tests',
  'EMaigrator.Connectors.Gmail.Tests','EMaigrator.Infrastructure.Tests',
  'EMaigrator.Infrastructure.IntegrationTests','EMaigrator.Workers.Tests',
  'EMaigrator.Workers.IntegrationTests','EMaigrator.Api.Tests','EMaigrator.Cli.Tests'
)
$listed = dotnet sln "$slnDir/EMaigrator.sln" list
foreach ($p in $expected) {
  if (-not ($listed -match [regex]::Escape($p))) { Fail "Project not in solution: $p" }
}

# Core must reference nothing.
$coreProj = Get-Content "$slnDir/EMaigrator.Core/EMaigrator.Core.csproj" -Raw
if ($coreProj -match 'ProjectReference') { Fail "EMaigrator.Core must reference nothing" }

# Connectors + Infrastructure reference ONLY Core.
foreach ($m in @('EMaigrator.Connectors.Imap','EMaigrator.Connectors.Graph','EMaigrator.Connectors.Gmail','EMaigrator.Infrastructure')) {
  $refs = ([regex]::Matches((Get-Content "$slnDir/$m/$m.csproj" -Raw), 'ProjectReference Include="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
  foreach ($r in $refs) {
    if ($r -notmatch 'EMaigrator\.Core\.csproj$') { Fail "$m has illegal reference: $r" }
  }
}
Write-Host "structure-check OK"
exit 0
