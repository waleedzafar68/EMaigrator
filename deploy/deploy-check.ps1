$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
function Fail($m){ Write-Error $m; exit 1 }
$compose = "$dir/docker-compose.yml"
if (-not (Test-Path $compose)) { Fail "docker-compose.yml missing" }
# Validate the compose schema via docker.
docker compose -f $compose config --quiet
if ($LASTEXITCODE -ne 0) { Fail "docker compose config failed" }
$text = Get-Content $compose -Raw
foreach ($svc in @('postgres','rabbitmq','redis','api','workers')) {
  if ($text -notmatch "(?m)^\s{2,4}$svc\s*:") { Fail "service missing: $svc" }
}
foreach ($img in @('postgres:17','rabbitmq:4-management','redis:7')) {
  if ($text -notmatch [regex]::Escape($img)) { Fail "pinned image missing: $img" }
}
if (-not (Test-Path "$dir/Dockerfile.api")) { Fail "Dockerfile.api missing" }
if (-not (Test-Path "$dir/.env.example")) { Fail ".env.example missing" }
# No real secrets: .env must NOT be committed.
if (Test-Path "$dir/.env") { Fail "deploy/.env must not be committed" }
Write-Host "deploy-check OK"
exit 0
