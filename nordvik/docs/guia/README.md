# Guía de Nordvik: aprende Karate de cero a senior

Esta guía te lleva paso a paso por todo lo que hay en Nordvik: cómo arrancarlo, cómo usar la Torre de control, cómo avanzar por los niveles y cómo hacer las lecciones de Karate de la academia.

Cada paso tiene tres partes: la **acción** (un comando o un clic), **lo que ves** (una captura o la salida real) y **qué significa**.

## Antes de empezar

- Carpeta del proyecto: `D:\WORKSPACE_KARATE\nordvik` (rama `nordvik-platform`).
- Necesitas .NET 8, Java 21 y Maven (ver [capítulo 1](01-arrancar.md)).
- Si tienes prisa, ve directo al [recorrido de principiante](10-recorrido-principiante.md).

## Capítulos

| # | Capítulo | Qué aprendes |
| --- | --- | --- |
| 1 | [Arrancar Nordvik](01-arrancar.md) | `nordvik niveles`, `up -Nivel 0`, `status`, `torre`, `down`, `reset` |
| 2 | [La Torre de control](02-torre-de-control.md) | La web de `http://localhost:3000`: barra de estado, pestañas, enlaces directos y su API |
| 3 | [Los niveles](03-niveles.md) | Los 5 niveles, sus checkpoints y cómo cambiar de nivel en caliente |
| 4 | [Misiones](04-misiones.md) | Las 24 misiones de negocio: preparar, seguir pasos, probar |
| 5 | [Mapa y fichas](05-mapa-y-fichas.md) | Los 168 servicios por dominio, encender y apagar, dependencias, la ficha de un servicio |
| 6 | [Laboratorio](06-laboratorio.md) | Reloj virtual, fallos a propósito y las 30 trampas |
| 7 | [La academia](07-academia.md) | Las 34 lecciones y 187 ejercicios, y el comando `academia` |
| 8 | [Swagger y primeras peticiones](08-swagger-y-primeras-peticiones.md) | Swagger, `curl.exe`, errores RFC 9457 y el paso a Karate |
| 9 | [Flujos de negocio](09-flujos-de-negocio.md) | F-01 a F-12: cómo leerlos y convertirlos en tests |
| 10 | [Recorrido de principiante](10-recorrido-principiante.md) | Una sesión completa: nivel 0, misión M01, lección L00 y subida al nivel 1 |
| 11 | [Problemas frecuentes](11-problemas-frecuentes.md) | Síntoma, causa y solución |

## Nordvik en una tabla

| | |
| --- | --- |
| Qué es | El backend simulado de una gestora de inversiones y pensiones |
| Servicios | 156 microservicios en 17 dominios + 12 sistemas externos simulados (E1-E12) |
| Cómo corre | Un solo proceso, `Nordvik.Host` (.NET 8). Cada servicio en su puerto, con Swagger en `http://localhost:<puerto>/swagger` |
| Torre de control | `http://localhost:3000` |
| Niveles | 0 Principiante, 1 Básico, 2 Intermedio, 3 Avanzado, 4 Senior (`torre/escalera.json`) |
| Academia | `academia/`: 34 lecciones (L00-L33), 187 ejercicios |
| Misiones | M01-M24 |
| Flujos | F-01 a F-12 (`docs/flows`) |
| Trampas | T1-T30 (11 activables) |

## La rutina de cada sesión

```text
1. cd D:\WORKSPACE_KARATE\nordvik
2. .\nordvik.cmd up -Nivel <tu nivel>
3. .\nordvik.cmd torre                     → Escalera: comprueba el nivel
4. cd academia
   .\academia.cmd leer Lxx                 → teoría
   .\academia.cmd ejemplo Lxx              → ejemplo resuelto
   (rellena los ___ de ejercicios\Exx.feature)
   .\academia.cmd corregir Lxx             → corrector
5. Torre → Misiones → Preparar → pasos     → la misión de esas lecciones
6. Torre → Laboratorio                     → rompe algo y mira si tu test lo detecta
7. cd .. ; .\nordvik.cmd down
```

## Capturas

Todas las imágenes están en [`img/`](img/). Se regeneran con `tools/capturas.ps1` (ver el final del [capítulo 11](11-problemas-frecuentes.md)).

| Imagen | Qué muestra |
| --- | --- |
| `torre-escalera.png` | Escalera, nivel 0 encendido |
| `torre-escalera-completa.png` | Los 5 niveles enteros |
| `torre-escalera-nivel1.png` | Escalera tras subir al nivel 1 |
| `torre-misiones.png` | Misiones |
| `torre-mapa.png` · `torre-mapa-nivel1.png` | Mapa con el nivel 0 y con el nivel 1 |
| `torre-laboratorio.png` · `torre-laboratorio-nivel3.png` | Laboratorio en el nivel 0 y en el nivel 3 (reloj congelado, un fallo y la trampa T15) |
| `torre-guia.png` | Cómo trabajar |
| `ficha-product-catalog.png` · `ficha-customer-registry.png` · `ficha-order-management.png` · `ficha-payout-execution.png` | Fichas de servicio (`#ficha:...`) |
| `flujo-F-07-traspaso.png` | Un flujo abierto (`#flujo:F-07-traspaso.md`) |
| `swagger-product-catalog.png` · `swagger-market-data.png` · `swagger-order-management.png` | Swagger de tres servicios |
