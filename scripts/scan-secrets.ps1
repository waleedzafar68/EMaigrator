<#
.SYNOPSIS
  Full-history secret scan with gitleaks, using the repo's .gitleaks.toml allowlist.
  Fails (non-zero exit) if any UNALLOWLISTED secret is found. Mirrors the CI gate.
.DESCRIPTION
  gitleaks is not a repo dependency. If it isn't already on PATH, this fetches the
  pinned release binary into .tools/ (gitignored) and runs that. Works on Windows,
  Linux, and macOS — it selects the matching release asset.
.PARAMETER Version
  gitleaks release to use (no leading 'v'). Bump deliberately.
.EXAMPLE
  pwsh -NoProfile -File scripts/scan-secrets.ps1
#>
param([string]$Version = '8.30.1')

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$config   = Join-Path $repoRoot '.gitleaks.toml'

function Resolve-Gitleaks {
  $onPath = Get-Command gitleaks -ErrorAction SilentlyContinue
  if ($onPath) { return $onPath.Source }

  $arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
  if     ($IsWindows) { $os = 'windows'; $ext = 'zip';    $bin = 'gitleaks.exe' }
  elseif ($IsMacOS)   { $os = 'darwin';  $ext = 'tar.gz'; $bin = 'gitleaks' }
  else                { $os = 'linux';   $ext = 'tar.gz'; $bin = 'gitleaks' }

  $cache = Join-Path $repoRoot ".tools/gitleaks-$Version"
  $exe   = Join-Path $cache $bin
  if (Test-Path $exe) { return $exe }

  New-Item -ItemType Directory -Force $cache | Out-Null
  $asset = "gitleaks_${Version}_${os}_${arch}.${ext}"
  $url   = "https://github.com/gitleaks/gitleaks/releases/download/v$Version/$asset"
  $dl    = Join-Path $cache $asset
  Write-Host "Fetching gitleaks $Version ($os/$arch) ..."
  Invoke-WebRequest $url -OutFile $dl
  if ($ext -eq 'zip') { Expand-Archive $dl -DestinationPath $cache -Force }
  else { tar -xzf $dl -C $cache }
  if (-not $IsWindows) { chmod +x $exe }
  return $exe
}

$gitleaks = Resolve-Gitleaks
& $gitleaks detect --source $repoRoot --config $config --redact --no-banner
$code = $LASTEXITCODE

if ($code -ne 0) {
  Write-Error "gitleaks reported unallowlisted secrets (exit $code). Review the findings above; if a finding is a genuine non-secret test fixture, add a surgical allowlist entry to .gitleaks.toml."
  exit $code
}
Write-Host 'No unallowlisted secrets found.'
exit 0
