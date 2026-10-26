# Torre de control y escalera de niveles

La Torre de control es la web desde la que manejas Nordvik sin perderte: eliges un **nivel** (0 a 4), la torre enciende
solo lo que ese nivel necesita y te da sus lecciones de Karate, sus **misiones** y su laboratorio.

```powershell
.\nordvik.cmd up -Nivel 0     # arranca el nivel 0 (8 servicios)
.\nordvik.cmd torre           # abre http://localhost:3000
.\nordvik.cmd niveles         # la escalera en la consola
```

| Nivel | Nombre | Qué se enciende | Servicios | Lecciones | Misiones |
|---|---|---|---|---|---|
| 0 | Principiante · Leer el mercado | utilidades y mercado | 8 | L00-L15 | M01-M03 |
| 1 | Básico · Clientes y permisos | identidad, clientes, carteras, generadores | 30 | L16-L17 | M04-M06 |
| 2 | Intermedio · El dinero se mueve | pagos, órdenes, contratos, composición, E1, E4, E5, E6 | 79 | L18-L21 | M07-M11 |
| 3 | Avanzado · Datos sensibles y sagas | privacidad, cumplimiento, voz, pensiones, caos, E2, E3, E7, E9, E10, E12 | 137 | L22-L27 | M12-M18 |
| 4 | Senior · La empresa completa | seguros, fiscalidad, RGPD, heredados, calidad, E8, E11 | 168 | L28-L33 | M19-M24 |

## Qué hay aquí

| Fichero | Para qué |
|---|---|
| `escalera.json` | La fuente única: niveles, perfiles, lecciones, checkpoints y misiones. Los servicios de cada nivel salen del código (nivel N = servicios con oleada ≤ N). |
| `web/` | La web (HTML, CSS y JS sin dependencias). La sirve `Nordvik.Host` en el puerto 3000. |

## La API de la torre (`http://localhost:3000/api/v1`)

| Método y ruta | Qué hace |
|---|---|
| `GET /estado` | Servicios encendidos, memoria, nivel actual, trampas encendidas |
| `GET /niveles` · `POST /niveles/{n}/encender?modo=exacto\|sumar` | La escalera; dejar encendido solo un nivel o sumarlo |
| `GET /servicios` · `GET /servicios/{nombre}` | Lista (filtros `nivel`, `dominio`, `q`, `encendidos`) y ficha |
| `POST /servicios/{nombre}/encender` · `/apagar?cascada=true` | En caliente; si otros dependen de él, 409 `HAS_RUNNING_DEPENDENTS` |
| `GET /dominios` · `POST /dominios/{código}/encender\|apagar` | Un dominio entero (por ejemplo `L-pensions`) |
| `GET /misiones` · `POST /misiones/{id}/preparar` | Enciende lo que necesita la misión |
| `GET /lecciones` · `POST /lecciones/{Lxx}/preparar` | Lo usa `academia.cmd` para no tener que reiniciar |
| `GET /trampas` · `GET /flujos/{fichero}` | Catálogo de trampas y flujos F-01..F-12 |
| `ANY /proxy/{servicio}/...` | Llama a cualquier servicio encendido a través de la torre |

Enlaces directos de la web: `#escalera`, `#misiones`, `#mapa`, `#laboratorio`, `#guia`, `#ficha:pii-vault`, `#flujo:F-07-traspaso.md`.

Decisión y reglas de coherencia: [ADR-014](../docs/adr/ADR-014-torre-de-control-y-escalera.md). Guía paso a paso con capturas: [docs/guia](../docs/guia/).
