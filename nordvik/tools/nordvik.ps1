<#
.SYNOPSIS
  CLI de Nordvik: arranca, para y vigila el backend simulado.
.EXAMPLE
  .\nordvik up                 # arranca el perfil nivel-0
  .\nordvik up -Nivel 2        # arranca el nivel 2 de la escalera (0 = principiante ... 4 = senior)
  .\nordvik niveles            # la escalera: qué enciende cada nivel y qué lecciones y misiones trae
  .\nordvik torre              # abre la Torre de control (http://localhost:3000) en el navegador
  .\nordvik up -Profile nivel-3
  .\nordvik up -Only order-management,payments-gateway   # un dominio suelto + sus dependencias (menos RAM)
  .\nordvik profiles           # perfiles disponibles y cuántos servicios enciende cada uno
  .\nordvik docs               # regenera fichas, OpenAPI y colecciones Bruno de lo que está encendido
  .\nordvik trace corr-1234    # cadena de llamadas de un X-Correlation-Id (correlation-tracer)
  .\nordvik status             # servicios, puertos, salud y RAM
  .\nordvik smoke              # prueba de humo: salud de todo + presupuesto de RAM
  .\nordvik test               # pruebas unitarias y de API del backend
  .\nordvik logs market-data   # últimas líneas del log de un servicio
  .\nordvik reset              # para y borra las bases de datos (datos semilla limpios)
  .\nordvik down
#>
param(
  [Parameter(Position = 0)][ValidateSet('up', 'down', 'status', 'smoke', 'test', 'reset', 'logs', 'docs', 'trace', 'profiles', 'niveles', 'torre', 'help')][string]$Command = 'help',
  [Parameter(Position = 1)][string]$Target,
  [string]$Profile = 'nivel-0',
  [int]$Nivel = -1,
  [string]$Only,
  [switch]$Build,
  [switch]$NoLearning
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Root = Split-Path -Parent $PSScriptRoot
$Data = Join-Path $Root 'data'
$Logs = Join-Path $Root 'logs'
$PidFile = Join-Path $Data 'host.pid'
$HostDll = Join-Path $Root 'platform\Nordvik.Host\bin\Debug\net8.0\Nordvik.Host.dll'
New-Item -ItemType Directory -Force -Path $Data, $Logs | Out-Null

function Get-HostProcess {
  if (-not (Test-Path $PidFile)) { return $null }
  $id = [int](Get-Content $PidFile)
  return Get-Process -Id $id -ErrorAction SilentlyContinue
}

function Get-Escalera {
  return Get-Content (Join-Path $Root 'torre\escalera.json') -Raw -Encoding UTF8 | ConvertFrom-Json
}

# -Nivel N elige el perfil de ese nivel de la escalera (la misma fuente que la Torre de control y la academia)
if ($Nivel -ge 0) {
  $lvl = (Get-Escalera).levels | Where-Object { $_.id -eq $Nivel }
  if (-not $lvl) { throw "No existe el nivel $Nivel. Los niveles van del 0 al 4: nordvik niveles" }
  $Profile = $lvl.profile
}

function Get-ProfileData([string]$name) {
  $file = Join-Path $Root "profiles\$name.json"
  if (-not (Test-Path $file)) { throw "No existe el perfil '$name'. Perfiles: $((Get-ChildItem (Join-Path $Root 'profiles') -Filter *.json | ForEach-Object BaseName) -join ', ')" }
  return Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Test-Health([int]$port) {
  try {
    $r = Invoke-RestMethod -Uri "http://localhost:$port/health" -TimeoutSec 2
    return $r.status -eq 'UP'
  } catch { return $false }
}

function Get-Services {
  $info = Join-Path $Data 'host.json'
  if (-not (Test-Path $info)) { return @() }
  return (Get-Content $info -Raw -Encoding UTF8 | ConvertFrom-Json).services
}

function Invoke-Up {
  $p = Get-HostProcess
  if ($p) { Write-Host "Nordvik ya está en marcha (PID $($p.Id)). Usa 'nordvik down' primero." -ForegroundColor Yellow; return }
  $null = Get-ProfileData $Profile
  if ($Build -or -not (Test-Path $HostDll)) {
    Write-Host 'Compilando Nordvik...' -ForegroundColor Cyan
    dotnet build (Join-Path $Root 'platform\Nordvik.Host\Nordvik.Host.csproj') -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'La compilación ha fallado.' }
  }
  $hostArgs = @("`"$HostDll`"", '--profile', $Profile)
  if ($NoLearning) { $hostArgs += '--no-learning' }
  if ($Only) { $hostArgs += @('--only', $Only) }
  Remove-Item (Join-Path $Data 'host.json') -ErrorAction SilentlyContinue
  $proc = Start-Process -FilePath 'dotnet' -ArgumentList $hostArgs -WorkingDirectory $Root -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $Logs 'host.out.log') -RedirectStandardError (Join-Path $Logs 'host.err.log')
  Set-Content -Path $PidFile -Value $proc.Id
  Write-Host "Arrancando perfil $Profile (PID $($proc.Id))..." -ForegroundColor Cyan
  $deadline = (Get-Date).AddSeconds(120)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ($proc.HasExited) { Get-Content (Join-Path $Logs 'host.err.log') | Out-Host; throw 'El anfitrión se ha cerrado al arrancar.' }
    $svcs = Get-Services
    if ($svcs.Count -gt 0 -and -not ($svcs | Where-Object { -not (Test-Health $_.port) })) { break }
  }
  $null = Invoke-Status
}

function Invoke-Down {
  $p = Get-HostProcess
  if (-not $p) { Write-Host 'Nordvik no está en marcha.'; Remove-Item $PidFile -ErrorAction SilentlyContinue; return }
  Stop-Process -Id $p.Id -Force
  Remove-Item $PidFile, (Join-Path $Data 'host.json') -ErrorAction SilentlyContinue
  Write-Host "Nordvik parado (PID $($p.Id))." -ForegroundColor Cyan
}

function Invoke-Status {
  $p = Get-HostProcess
  if (-not $p) { Write-Host "Nordvik no está en marcha. Arráncalo con: nordvik up -Profile $Profile" -ForegroundColor Yellow; return $false }
  $p.Refresh()
  $ram = [math]::Round($p.WorkingSet64 / 1MB, 0)
  $priv = [math]::Round($p.PrivateMemorySize64 / 1MB, 0)
  $hostInfo = Get-Content (Join-Path $Data 'host.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  Write-Host ""
  Write-Host "  Perfil $($hostInfo.profile) · PID $($p.Id) · RAM $ram MB (privada $priv MB) · $($hostInfo.services.Count) servicios encendidos" -ForegroundColor Cyan
  if ($hostInfo.tower) { Write-Host "  Torre de control: $($hostInfo.tower)  (enciende y apaga niveles, dominios y servicios)" -ForegroundColor Cyan }
  $rows = foreach ($s in $hostInfo.services) {
    [pscustomobject]@{ Puerto = $s.port; Servicio = $s.name; Salud = $(if (Test-Health $s.port) { 'UP' } else { 'CAÍDO' }); Swagger = "http://localhost:$($s.port)/swagger" }
  }
  $rows | Sort-Object Puerto | Format-Table -AutoSize | Out-Host
  return -not ($rows | Where-Object Salud -ne 'UP')
}

function Invoke-Smoke {
  if (-not (Get-HostProcess)) { Invoke-Up }
  else { Write-Host 'Usando el Nordvik que ya está en marcha.' }
  $hostInfo = Get-Content (Join-Path $Data 'host.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $profileData = Get-ProfileData $hostInfo.profile
  $ok = Invoke-Status 6>$null
  $p = Get-HostProcess; $p.Refresh()
  $ram = [math]::Round($p.WorkingSet64 / 1MB, 0)
  $budget = $profileData.ramBudgetMb
  $expected = if ($hostInfo.only) { $hostInfo.services.Count } else { $profileData.services.Count }
  $running = $hostInfo.services.Count
  Write-Host "  Servicios: $running/$expected · RAM: $ram MB de $budget MB de presupuesto"
  if ($ok -and $running -eq $expected -and $ram -le $budget) { Write-Host '  PRUEBA DE HUMO SUPERADA' -ForegroundColor Green; exit 0 }
  Write-Host '  PRUEBA DE HUMO FALLIDA' -ForegroundColor Red; exit 1
}

switch ($Command) {
  'up' { Invoke-Up }
  'down' { Invoke-Down }
  'status' { $null = Invoke-Status }
  'smoke' { Invoke-Smoke }
  'test' { dotnet test (Join-Path $Root 'Nordvik.sln') -nologo; exit $LASTEXITCODE }
  'reset' { Invoke-Down; Get-ChildItem $Data -Filter *.db* | Remove-Item -Force; Write-Host 'Bases de datos borradas: al arrancar se recrean con los datos semilla.' -ForegroundColor Cyan }
  'profiles' {
    Get-ChildItem (Join-Path $Root 'profiles') -Filter *.json | ForEach-Object {
      $d = Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
      [pscustomobject]@{ Perfil = $d.name; Oleada = $d.wave; Servicios = $d.services.Count; 'RAM máx. (MB)' = $d.ramBudgetMb; Descripción = $d.description }
    } | Format-Table -AutoSize -Wrap | Out-Host
  }
  'niveles' {
    (Get-Escalera).levels | Sort-Object id | ForEach-Object {
      $p = Get-ProfileData $_.profile
      [pscustomobject]@{ Nivel = "$($_.id) · $($_.name)"; Tema = $_.title; Perfil = $_.profile; Servicios = $p.services.Count; Lecciones = "$($_.lessons[0])-$($_.lessons[-1])"; 'Ruta del plan' = $_.route }
    } | Format-Table -AutoSize | Out-Host
    Write-Host '  Arranca un nivel con: nordvik up -Nivel 0   · y cámbialo en caliente desde la Torre de control: nordvik torre' -ForegroundColor Cyan
  }
  'torre' {
    if (-not (Get-HostProcess)) { Write-Host 'Nordvik no está en marcha: arranco el nivel 0 primero.' -ForegroundColor Yellow; Invoke-Up }
    Start-Process 'http://localhost:3000'
  }
  'docs' {
    if (-not (Get-HostProcess)) { throw "Arranca primero Nordvik (nordvik up -Profile ...): la documentación se genera desde los servicios encendidos." }
    node (Join-Path $Root 'tools\docs-gen.mjs')
  }
  'trace' {
    if (-not $Target) { throw 'Uso: nordvik trace <correlationId>' }
    $t = Invoke-RestMethod -Uri "http://localhost:6108/api/v1/traces/$Target"
    Write-Host "  $($t.hops) llamadas · $($t.services.Count) servicios · $($t.totalMs) ms · errores: $($t.errors)" -ForegroundColor Cyan
    Write-Host $t.tree
  }
  'logs' {
    if (-not $Target) { Get-ChildItem $Logs -Filter *.log | Select-Object Name, Length, LastWriteTime | Format-Table | Out-Host; return }
    Get-Content (Join-Path $Logs "$Target.log") -Tail 30 -Encoding UTF8
  }
  default { Get-Help $PSCommandPath -Examples }
}
