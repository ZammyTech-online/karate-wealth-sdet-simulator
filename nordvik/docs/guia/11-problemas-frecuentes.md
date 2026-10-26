# 11 · Problemas frecuentes

Cada problema tiene el síntoma, la causa y qué hacer.

## Arranque

### `Nordvik ya está en marcha (PID 1234). Usa 'nordvik down' primero.`

**Causa.** Ya hay un `Nordvik.Host` arrancado (su PID está en `data\host.pid`).
**Qué hacer.** Si quieres otro nivel, no hace falta reiniciar: cámbialo en la torre (**Encender solo este nivel**). Si de verdad quieres reiniciar: `.\nordvik.cmd down` y luego `up`.

### `El anfitrión se ha cerrado al arrancar.`

**Causa.** El proceso murió nada más arrancar. El motivo está en `logs\host.err.log`, que el comando te enseña justo encima.
**Qué hacer.** El caso más común es un puerto ocupado: `[ERROR] <servicio> no arrancó en el puerto <N>: ...`. Busca quién lo usa:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 6801 | Select-Object LocalAddress, OwningProcess
Get-Process -Id <OwningProcess>
```

### Un servicio sale `CAÍDO` en `nordvik status`

**Causa.** No contesta en `/health` en 2 segundos. Puede estar arrancando todavía, o no arrancó.
**Qué hacer.** Espera unos segundos y repite `.\nordvik.cmd status`. Si sigue caído, mira `logs\host.err.log` y `.\nordvik.cmd logs <servicio>`.

### `No existe el nivel 5. Los niveles van del 0 al 4: nordvik niveles`

Los niveles van del 0 al 4. Mira `.\nordvik.cmd niveles`.

### `No existe el perfil 'xxx'.`

Los perfiles son `nivel-0`, `nivel-3`, `nivel-6`, `nivel-9` y `completo` (`.\nordvik.cmd profiles`). Ojo: `-Profile nivel-3` es el **nivel 1** de la escalera, no el 3.

### El arranque tarda

Es normal que tarde de 10 a 40 segundos. Si hay que compilar (primera vez o `-Build`), más.

## Torre de control

### En `http://localhost:3000` aparece otra aplicación

**Causa.** Otro programa usa el puerto 3000. En este PC, `karate-debug-bot` (`node server.js`, en `D:\WORKSPACE_KARATE\karate-debug-bot`) escucha en el puerto 3000 de todas las direcciones.
**Qué pasa.** Mientras Nordvik está en marcha, la torre escucha en `127.0.0.1:3000` y `[::1]:3000`, que son más concretas, y gana: `localhost:3000` es la torre. Si Nordvik está **parado**, `localhost:3000` muestra la otra aplicación.
**Qué hacer.** Comprueba que Nordvik está en marcha (`.\nordvik.cmd status`). Si la torre no arrancó, el log dice `[AVISO] La torre de control no arrancó en el puerto 3000: ...`. Cierra el otro programa y reinicia Nordvik.

> Lo mismo afecta a la academia: si algo responde en el puerto 3000, `academia.ps1` cree que es la torre y le pide que prepare la lección. Si no es la torre, verás "La Torre de control no pudo preparar la lección: ...". Arranca Nordvik antes de usar la academia.

### La barra dice `torre sin respuesta`

La web no llega a la API de la torre. Nordvik se ha parado. Arráncalo y recarga la página.

### La barra dice `reloj apagado`

La torre no puede leer `clock-service`. No debería pasar (el reloj no se puede apagar). Reinicia Nordvik.

### No puedo apagar un servicio: "Otros servicios necesitan..."

Es a propósito: hay servicios encendidos que dependen de él (`409 HAS_RUNNING_DEPENDENTS`). Pulsa **Apagar los N** si quieres apagarlos todos, o déjalo encendido.

### El interruptor de `clock-service` está bloqueado

`clock-service` es núcleo y no se apaga nunca (`409 CORE_SERVICE`).

### El panel de fallos dice "chaos-control está apagado: enciende el nivel 3."

Los fallos y las trampas activables necesitan `chaos-control` (7600), que es del nivel 3. Enciende el nivel 3 o enciende `chaos-control` desde el Mapa.

### `nordvik status` dice "Perfil nivel-0" pero hay 30 servicios encendidos

**Causa.** `status` enseña el perfil con el que **arrancó** el anfitrión. Si después cambias de nivel en la torre, el perfil no cambia, pero la lista de servicios sí.
**Qué hacer.** Para saber el nivel real, mira la barra de la torre (`Nivel 1 completo`) o `GET http://localhost:3000/api/v1/estado` (`currentLevel`).

### `nordvik smoke` falla después de cambiar de nivel en la torre

**Causa.** `smoke` compara los servicios encendidos con los del perfil de arranque. Si subiste de nivel en caliente, no cuadran.
**Qué hacer.** Usa `smoke` justo después de `up`, o reinicia en el nivel que quieras (`down` y `up -Nivel N`).

## Academia

### "No hay ningún servicio escuchando"

Nordvik no está arrancado, o el servicio de la lección está apagado. Arranca Nordvik. Con la torre en marcha, la academia enciende sola lo que falte.

### `academia progreso` da `Method invocation failed because [System.Object[]] does not contain a method named 'op_Subtraction'`

**Causa.** Un fallo de `academia.ps1` con Windows PowerShell 5.1 (el que trae Windows) al contar los ejercicios de `progreso.json`.
**Qué hacer.** Mira tu avance en la **Escalera** de la torre (chips `L00 · 5/5`) o vuelve a ejecutar `.\academia.cmd corregir`, que imprime el resultado de cada lección.

### El corrector tarda

Cada ejercicio es una ejecución de Karate. Una lección de 5 ejercicios tarda alrededor de un minuto. La primera vez Maven puede descargar dependencias.

### Todo pasaba y de repente fallan tests de fechas

Mira la barra de la torre: si el reloj dice `congelado`, alguien lo dejó parado. Ve al **Laboratorio** y pulsa **Hora real**.

### Un test falla "sin motivo" y la barra dice `1 trampa encendida`

Hay una trampa activable encendida. Es lo que tu suite debe detectar. Para volver a la normalidad: **Laboratorio → Apagar trampas y fallos**.

## Terminal

### `curl` en PowerShell no se comporta como en los ejemplos

En Windows PowerShell, `curl` es un alias de `Invoke-WebRequest`. Escribe **`curl.exe`**.

### `nordvik` o `academia` "no se reconoce como nombre de un cmdlet"

En PowerShell, la carpeta actual no está en la ruta de búsqueda. Escribe `.\nordvik.cmd ...` desde `D:\WORKSPACE_KARATE\nordvik` y `.\academia.cmd ...` desde `D:\WORKSPACE_KARATE\nordvik\academia`.

### Quiero los datos como el primer día

```powershell
.\nordvik.cmd reset
.\nordvik.cmd up -Nivel 0
```

`reset` borra **todas** las bases de datos de `data\` (clientes, órdenes, todo lo que hayas creado). Tu progreso de la academia (`academia\progreso.json`) no se toca.

## Regenerar las capturas de esta guía

Con Nordvik en marcha (cualquier nivel):

```powershell
cd D:\WORKSPACE_KARATE\nordvik
.\tools\capturas.ps1
```

(Si tu política de ejecución no deja lanzar scripts, usa `powershell -NoProfile -ExecutionPolicy Bypass -File tools\capturas.ps1`, igual que hace `nordvik.cmd`.)

El script usa Edge en modo headless, recorre los niveles 0 a 3 con la API de la torre, guarda los PNG en `docs\guia\img` y al acabar deja el nivel 0, el reloj en hora real y las trampas apagadas. Con `-SoloNivel0` solo hace las capturas del nivel 0.

Volver al [índice](README.md)
