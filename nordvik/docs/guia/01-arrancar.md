# 1 · Arrancar Nordvik

En este capítulo enciendes Nordvik en tu PC, compruebas que responde y aprendes a pararlo.

## Qué es lo que vas a arrancar

Nordvik es el backend simulado de una gestora de inversiones y pensiones. Tiene:

- **156 microservicios** de negocio repartidos en **17 dominios** (Utilidades, Plataforma, Identidad, Clientes, Mercado, Inversiones, Pagos, Pensiones...).
- **12 sistemas externos simulados** (E1-E12): el banco NordPay, el custodio, la Agencia Tributaria, la aseguradora...
- Todo vive dentro de **un solo proceso**: `Nordvik.Host`. Cada servicio abre su propio puerto (`6001`, `6801`, `7108`...) y tiene su Swagger en `http://localhost:<puerto>/swagger`.
- El mismo proceso sirve la **Torre de control** en `http://localhost:3000`.

Nunca enciendes los 168 a la vez para aprender. Empiezas por el **nivel 0**: 8 servicios.

## Requisitos

| Programa | Para qué | Cómo comprobarlo |
| --- | --- | --- |
| .NET 8 SDK | Compila y ejecuta Nordvik | `dotnet --version` (por ejemplo `8.0.426`) |
| Java 21 y Maven | Ejecutan Karate en la academia | `java -version` y `mvn -version` |
| Microsoft Edge (o cualquier navegador) | Torre de control y Swagger | — |
| VS Code (recomendado) | Editar los `.feature` | — |

## Paso 1 · Abre una terminal en la carpeta del proyecto

**Acción.** Abre PowerShell y entra en la carpeta:

```powershell
cd D:\WORKSPACE_KARATE\nordvik
```

**Qué significa.** Todos los comandos de esta guía se lanzan desde aquí (o desde `academia\` cuando se diga).

> En PowerShell los comandos llevan `.\` delante: `.\nordvik.cmd up`. En la consola clásica (`cmd.exe`) basta con `nordvik up`. En esta guía verás la forma de PowerShell.

## Paso 2 · Mira la escalera de niveles

**Acción.**

```powershell
.\nordvik.cmd niveles
```

**Lo que ves** (salida real):

```text
Nivel            Tema                    Perfil   Servicios Lecciones Ruta del plan
-----            ----                    ------   --------- --------- -------------
0 · Principiante Leer el mercado         nivel-0          8 L00-L15   0-2
1 · Básico       Clientes y permisos     nivel-3         30 L16-L17   3-5
2 · Intermedio   El dinero se mueve      nivel-6         79 L18-L21   6-8
3 · Avanzado     Datos sensibles y sagas nivel-9        137 L22-L27   9-10
4 · Senior       La empresa completa     completo       168 L28-L33   11-12


  Arranca un nivel con: nordvik up -Nivel 0   · y cámbialo en caliente desde la Torre de control: nordvik torre
```

**Qué significa.**

- Hay 5 niveles. Cada uno usa un **perfil** (`profiles/<perfil>.json`) que dice qué servicios se encienden.
- Cada nivel trae sus **lecciones** de la academia. El nivel 0 tiene 16 (L00-L15).
- "Ruta del plan" es la numeración antigua del plan de estudios (niveles 0 a 12). No la necesitas para trabajar.

La escalera sale de `torre/escalera.json`. Es la misma fuente que usan la torre y la academia.

## Paso 3 · Arranca el nivel 0

**Acción.**

```powershell
.\nordvik.cmd up -Nivel 0
```

**Qué pasa.** `nordvik.cmd` llama a `tools\nordvik.ps1`. Este:

1. Traduce `-Nivel 0` al perfil `nivel-0`.
2. Si no existe `platform\Nordvik.Host\bin\Debug\net8.0\Nordvik.Host.dll`, compila primero (la primera vez tarda).
3. Lanza `Nordvik.Host` en segundo plano, guarda su PID en `data\host.pid` y su salida en `logs\host.out.log` y `logs\host.err.log`.
4. Espera a que todos los servicios respondan `UP` en `/health` y te enseña el estado.

Primero verás `Arrancando perfil nivel-0 (PID ...)...` y, al acabar, la misma tabla que `nordvik status` (paso 4).

> Arrancar tarda unos segundos (en las pruebas de esta guía, entre 10 y 40). Ten paciencia antes de abrir la torre.

Otras formas de arrancar (todas existen en `tools/nordvik.ps1`):

| Comando | Qué hace |
| --- | --- |
| `.\nordvik.cmd up` | Igual que `-Nivel 0` (el perfil por defecto es `nivel-0`). |
| `.\nordvik.cmd up -Profile nivel-6` | Arranca un perfil por su nombre. |
| `.\nordvik.cmd up -Only order-management` | Un servicio suelto más sus dependencias. |
| `.\nordvik.cmd up -Build` | Fuerza la compilación antes de arrancar. |
| `.\nordvik.cmd profiles` | Lista los perfiles con su número de servicios y presupuesto de RAM. |

## Paso 4 · Comprueba que todo está UP

**Acción.**

```powershell
.\nordvik.cmd status
```

**Lo que ves** (salida real del nivel 0; el PID y la RAM cambian en tu PC):

```text
  Perfil nivel-0 · PID 28276 · RAM 233 MB (privada 146 MB) · 8 servicios encendidos
  Torre de control: http://localhost:3000  (enciende y apaga niveles, dominios y servicios)

Puerto Servicio            Salud Swagger
------ --------            ----- -------
  6001 uuid-service        UP    http://localhost:6001/swagger
  6002 sequence-service    UP    http://localhost:6002/swagger
  6003 clock-service       UP    http://localhost:6003/swagger
  6004 random-seed-service UP    http://localhost:6004/swagger
  6801 product-catalog     UP    http://localhost:6801/swagger
  6802 market-data         UP    http://localhost:6802/swagger
  6803 fx-rates            UP    http://localhost:6803/swagger
  6804 calendar-service    UP    http://localhost:6804/swagger
```

**Qué significa.**

- `UP`: el servicio contesta en `GET /health`. Si ves `CAÍDO`, mira [Problemas frecuentes](11-problemas-frecuentes.md).
- Los 4 primeros son **utilidades** (identificadores, secuencias, reloj virtual, semillas aleatorias).
- Los 4 últimos son **mercado** (catálogo de fondos, precios NAV, tipos de cambio, calendario bursátil).
- `clock-service` (6003) es el **reloj virtual**: todos los servicios le preguntan la hora. Nunca se apaga.

Prueba de humo (salud de todo y RAM dentro del presupuesto del perfil):

```powershell
.\nordvik.cmd smoke
```

## Paso 5 · Abre la Torre de control

**Acción.**

```powershell
.\nordvik.cmd torre
```

**Qué pasa.** Se abre `http://localhost:3000` en tu navegador. Si Nordvik no estaba en marcha, el comando arranca antes el nivel 0. Sigue en el [capítulo 2](02-torre-de-control.md).

## Paso 6 · Para Nordvik al terminar

**Acción.**

```powershell
.\nordvik.cmd down
```

**Qué significa.** Para el proceso cuyo PID está en `data\host.pid`. Los datos (bases SQLite en `data\`) se conservan para la próxima vez.

Si quieres volver a los datos de fábrica:

```powershell
.\nordvik.cmd reset
```

`reset` para Nordvik y borra `data\*.db`. Al arrancar otra vez se recrean con los datos semilla.

## Resumen de comandos de `nordvik`

| Comando | Qué hace |
| --- | --- |
| `up [-Nivel N] [-Profile p] [-Only a,b] [-Build] [-NoLearning]` | Arranca |
| `down` | Para |
| `status` | Puertos, salud y RAM |
| `smoke` | Prueba de humo |
| `niveles` | La escalera de 5 niveles |
| `torre` | Abre la Torre de control |
| `profiles` | Perfiles disponibles |
| `logs [servicio]` | Últimas 30 líneas del log de un servicio (sin nombre, lista los logs) |
| `trace <correlationId>` | Cadena de llamadas de un `X-Correlation-Id` (necesita `correlation-tracer`, nivel 2) |
| `docs` | Regenera fichas, OpenAPI y colecciones Bruno de lo encendido (necesita Node.js) |
| `test` | Pruebas unitarias y de API del backend (`dotnet test`) |
| `reset` | Para y borra las bases de datos |

Siguiente: [2 · La Torre de control](02-torre-de-control.md)
