<#
.SYNOPSIS
  Academia Nordvik de Karate: aprende Karate desde cero con lecciones, práctica y ejercicios corregidos.
.EXAMPLE
  .\academia empezar              # cómo funciona la academia
  .\academia leer L01             # teoría de la lección
  .\academia ejemplo L01          # ejecuta el ejemplo resuelto
  .\academia practica L01         # ejecuta tu campo de práctica
  .\academia corregir L01         # corrige tus ejercicios de la lección
  .\academia corregir             # corrige todo lo que llevas
  .\academia pista L01 E03        # pista 2 de un ejercicio
  .\academia solucion L01 E03     # la solución comentada (pista 3, úsala solo si te atascas)
  .\academia progreso             # tu progreso
  .\academia verificar            # (equipo) soluciones en verde y huecos que fallan
#>
param(
  [Parameter(Position = 0)][string]$Command = 'empezar',
  [Parameter(Position = 1)][string]$Leccion = 'todas',
  [Parameter(Position = 2)][string]$Ejercicio
)
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:MAVEN_OPTS = '-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8'
$Root = $PSScriptRoot
Set-Location $Root

function Find-Lesson([string]$code) {
  $dir = Get-ChildItem (Join-Path $Root 'lecciones') -Directory | Where-Object { $_.Name -like "$code*" } | Select-Object -First 1
  if (-not $dir) { throw "No existe la lección '$code'. Mira las disponibles con: academia empezar" }
  return $dir
}

# La torre se reconoce por su /health (el puerto 3000 también lo puede usar otro programa, como karate-debug-bot)
function Test-Torre {
  try { return (Invoke-RestMethod "http://localhost:3000/health" -TimeoutSec 2).service -eq 'torre-de-control' } catch { return $false }
}

function Test-Port([int]$port) {
  try { $null = Invoke-RestMethod "http://localhost:$port/health" -TimeoutSec 2; return $true } catch { return $false }
}

# Qué perfil necesita cada lección: lo dice la escalera (torre/escalera.json), la misma que usa la Torre de control.
function Get-PerfilNecesario([string]$leccion) {
  $escalera = Get-Content (Join-Path (Split-Path $Root -Parent) 'torre\escalera.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $niveles = $escalera.levels | Sort-Object id
  $nivel = if ($leccion -eq 'todas' -or -not $leccion) { $niveles[-1] } else { $niveles | Where-Object { $_.lessons -contains $leccion.Substring(0, 3).ToUpper() } | Select-Object -First 1 }
  if (-not $nivel) { $nivel = $niveles[-1] }
  # Puerto de un servicio que solo existe a partir de ese nivel: si responde, el perfil ya está encendido.
  $sonda = @{ 0 = 6801; 1 = 6200; 2 = 6700; 3 = 7108; 4 = 7604 }
  return @{ Perfil = $nivel.profile; Puerto = $sonda[[int]$nivel.id]; Nivel = $nivel.id }
}

function Invoke-Maven([string]$modo, [string]$leccion) {
  $need = Get-PerfilNecesario $leccion
  if (Test-Torre) {
    # Nordvik ya está en marcha: la Torre de control enciende en caliente solo lo que falta para esta lección.
    try {
      $ruta = if ($leccion -eq 'todas' -or -not $leccion) { "niveles/$($need.Nivel)/encender?modo=sumar" } else { "lecciones/$($leccion.Substring(0, 3).ToUpper())/preparar" }
      $r = Invoke-RestMethod -Method Post "http://localhost:3000/api/v1/$ruta" -TimeoutSec 120
      if ($r.started.Count -gt 0) { Write-Host "La Torre de control ha encendido $($r.started.Count) servicios para esta lección." -ForegroundColor Yellow }
    } catch { Write-Host "La Torre de control no pudo preparar la lección: $($_.Exception.Message)" -ForegroundColor Red }
  }
  elseif (-not (Test-Port $need.Puerto)) {
    $nordvik = Join-Path (Split-Path $Root -Parent) 'tools\nordvik.ps1'
    Write-Host "Esta lección es del nivel $($need.Nivel): necesita Nordvik con el perfil $($need.Perfil). Lo arranco por ti (tarda un poco la primera vez)..." -ForegroundColor Yellow
    if (Test-Port 6001) { & $nordvik down | Out-Null }
    & $nordvik up -Profile $need.Perfil | Out-Null
  }
  mvn -q -B test "-Dmodo=$modo" "-Dleccion=$leccion" 2>&1 | Where-Object { $_ -notmatch '^\[(ERROR|INFO)\]|^Tests run|^\s*$|surefire|To see the full stack|Re-run Maven|For more information|Failures: |^\[WARNING\]' } | Out-Host
}

switch ($Command) {
  'empezar' { Get-Content (Join-Path $Root 'README.md') -Encoding UTF8 | Out-Host }
  'leer' { Get-Content (Join-Path (Find-Lesson $Leccion).FullName 'README.md') -Encoding UTF8 | Out-Host }
  'ejemplo' { $null = Find-Lesson $Leccion; Invoke-Maven 'ejemplo' $Leccion }
  'practica' { $null = Find-Lesson $Leccion; Invoke-Maven 'practica' $Leccion }
  'corregir' { Invoke-Maven 'ejercicios' $Leccion }
  'pista' {
    $dir = Find-Lesson $Leccion
    if (-not $Ejercicio) { throw 'Dime el ejercicio: academia pista L01 E03' }
    $text = Get-Content (Join-Path $dir.FullName 'pistas.md') -Raw -Encoding UTF8
    $m = [regex]::Match($text, "(?ms)^## $Ejercicio\b.*?(?=^## |\z)")
    if ($m.Success) { Write-Host $m.Value -ForegroundColor Cyan } else { Write-Host "No hay pista para $Ejercicio." }
  }
  'solucion' {
    $dir = Find-Lesson $Leccion
    if (-not $Ejercicio) { throw 'Dime el ejercicio: academia solucion L01 E03' }
    $file = Get-ChildItem (Join-Path (Join-Path $Root 'soluciones') $dir.Name) -Filter "$Ejercicio*.feature" | Select-Object -First 1
    Write-Host 'Pista 3 · solución completa. Léela, ciérrala y escríbela tú sin mirar.' -ForegroundColor Yellow
    Get-Content $file.FullName -Encoding UTF8 | Out-Host
  }
  'progreso' {
    $file = Join-Path $Root 'progreso.json'
    if (-not (Test-Path $file)) { Write-Host 'Todavía no has corregido nada. Empieza con: academia leer L00'; return }
    $p = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($l in $p.PSObject.Properties) {
      $ex = @($l.Value.PSObject.Properties)
      $total = [int]$ex.Length
      $ok = [int]@($ex | Where-Object { $_.Value -eq 'OK' }).Length
      Write-Host ("{0}  {1}/{2}  {3}" -f $l.Name, $ok, $total, ('#' * $ok + '.' * ($total - $ok)))
    }
  }
  'verificar' { Invoke-Maven 'revision' $Leccion; Invoke-Maven 'huecos' $Leccion }
  default { Get-Help $PSCommandPath -Examples }
}
