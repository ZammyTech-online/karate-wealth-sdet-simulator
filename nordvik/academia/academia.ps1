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

function Test-Nordvik {
  try { $null = Invoke-RestMethod 'http://localhost:6801/health' -TimeoutSec 2; return $true } catch { return $false }
}

function Invoke-Maven([string]$modo, [string]$leccion) {
  if (-not (Test-Nordvik)) {
    Write-Host 'Nordvik no está arrancado. Lo arranco por ti (perfil nivel-0)...' -ForegroundColor Yellow
    & (Join-Path (Split-Path $Root -Parent) 'tools\nordvik.ps1') up | Out-Null
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
      $ex = $l.Value.PSObject.Properties
      $ok = ($ex | Where-Object { $_.Value -eq 'OK' }).Count
      Write-Host ("{0}  {1}/{2}  {3}" -f $l.Name, $ok, $ex.Count, ('#' * $ok + '.' * ($ex.Count - $ok)))
    }
  }
  'verificar' { Invoke-Maven 'revision' $Leccion; Invoke-Maven 'huecos' $Leccion }
  default { Get-Help $PSCommandPath -Examples }
}
