<#
.SYNOPSIS
  Regenera las capturas de la guía (docs/guia/img) con Microsoft Edge en modo headless.
.DESCRIPTION
  Necesita Nordvik en marcha con la Torre de control en http://localhost:3000 (por ejemplo: nordvik up -Nivel 0).
  El script cambia de nivel en caliente con la API de la torre (0, 1, 2 y 3), hace las capturas de cada
  vista y, al acabar, deja otra vez el nivel 0, el reloj en hora real y las trampas y fallos apagados.
  No instala nada: usa el Edge que ya trae Windows.
.EXAMPLE
  .\tools\capturas.ps1
  .\tools\capturas.ps1 -SoloNivel0          # solo las capturas del nivel 0 (no enciende más servicios)
  .\tools\capturas.ps1 -Torre http://127.0.0.1:3000
#>
param(
  [string]$Torre = 'http://localhost:3000',
  [string]$Salida,
  [switch]$SoloNivel0
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Salida) { $Salida = Join-Path $Root 'docs\guia\img' }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null

$Edge = @(
  "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Edge) { throw 'No encuentro msedge.exe en Program Files ni en Program Files (x86).' }

# Perfil de Edge propio y temporal: no toca tu Edge de siempre.
$Perfil = Join-Path $env:TEMP 'nordvik-capturas-edge'

try { $null = Invoke-RestMethod "$Torre/health" -TimeoutSec 3 }
catch { throw "La Torre de control no responde en $Torre. Arranca Nordvik primero: nordvik up -Nivel 0" }

function Captura([string]$nombre, [string]$url, [int]$ancho = 1400, [int]$alto = 1000, [int]$espera = 6000) {
  $png = Join-Path $Salida "$nombre.png"
  Remove-Item $png -ErrorAction SilentlyContinue
  $edgeArgs = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', "--user-data-dir=$Perfil",
    "--window-size=$ancho,$alto", "--virtual-time-budget=$espera", "--screenshot=$png", $url)
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  & $Edge @edgeArgs 2>$null | Out-Null
  $ErrorActionPreference = $old
  if (Test-Path $png) { Write-Host ("  OK  {0,-34} {1}" -f "$nombre.png", $url) -ForegroundColor Green }
  else { Write-Host "  !!  no se generó $nombre.png ($url)" -ForegroundColor Red }
}

function Nivel([int]$id) {
  Write-Host "Nivel $id (encender solo este nivel)..." -ForegroundColor Cyan
  $r = Invoke-RestMethod -Method Post "$Torre/api/v1/niveles/$id/encender?modo=exacto" -TimeoutSec 300
  Write-Host "  $($r.started.Count) encendidos, $($r.stopped.Count) apagados, $($r.running) en marcha"
  Start-Sleep -Seconds 2
}

function Torre-Post([string]$ruta, $cuerpo) {
  $json = if ($null -eq $cuerpo) { $null } else { $cuerpo | ConvertTo-Json -Compress }
  Invoke-RestMethod -Method Post "$Torre/api/v1/$ruta" -ContentType 'application/json' -Body $json -TimeoutSec 60 | Out-Null
}

# ---------- Nivel 0 ----------
Nivel 0
Captura 'torre-escalera'          "$Torre/#escalera"
Captura 'torre-escalera-completa' "$Torre/#escalera" 1400 3300
Captura 'torre-misiones'          "$Torre/#misiones" 1400 1300
Captura 'torre-mapa'              "$Torre/#mapa" 1400 1300
Captura 'torre-laboratorio'       "$Torre/#laboratorio" 1400 1500
Captura 'torre-guia'              "$Torre/#guia" 1400 1250
Captura 'ficha-product-catalog'   "$Torre/#ficha:product-catalog" 1400 1300
Captura 'flujo-F-07-traspaso'     "$Torre/#flujo:F-07-traspaso.md" 1400 2600
Captura 'swagger-product-catalog' 'http://localhost:6801/swagger/index.html' 1400 1300 8000
Captura 'swagger-market-data'     'http://localhost:6802/swagger/index.html' 1400 1300 8000

if (-not $SoloNivel0) {
  # ---------- Nivel 1 ----------
  Nivel 1
  Captura 'torre-escalera-nivel1'      "$Torre/#escalera" 1400 1700
  Captura 'torre-mapa-nivel1'          "$Torre/#mapa" 1400 1300
  Captura 'ficha-customer-registry'    "$Torre/#ficha:customer-registry" 1400 1300

  # ---------- Nivel 2 ----------
  Nivel 2
  Captura 'ficha-order-management'     "$Torre/#ficha:order-management" 1400 1300
  Captura 'swagger-order-management'   'http://localhost:6902/swagger/index.html' 1400 1300 8000

  # ---------- Nivel 3: laboratorio con reloj congelado, una trampa y un fallo ----------
  Nivel 3
  Captura 'ficha-payout-execution'     "$Torre/#ficha:payout-execution" 1400 1300
  Torre-Post 'proxy/clock-service/api/v1/clock/freeze' @{ at = '2026-10-20T15:30:00' }
  Invoke-RestMethod -Method Put "$Torre/api/v1/proxy/chaos-control/api/v1/traps/T15" -ContentType 'application/json' -Body '{"enabled":true}' | Out-Null
  Torre-Post 'proxy/chaos-control/api/v1/faults' @{ service = 'market-data'; pathPrefix = '/api/v1/prices'; status = 503; times = 3 }
  Captura 'torre-laboratorio-nivel3'   "$Torre/#laboratorio" 1400 1500

  # ---------- Dejarlo todo como estaba ----------
  Torre-Post 'proxy/chaos-control/api/v1/reset' $null
  Torre-Post 'proxy/clock-service/api/v1/clock/reset' $null
  Nivel 0
}

Write-Host "Capturas en $Salida" -ForegroundColor Cyan
