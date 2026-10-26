# 7 · La academia de Karate

La academia está en `academia/`. Son **34 lecciones (L00-L33) con 187 ejercicios**, ordenadas de cero a senior y repartidas por los 5 niveles. Se maneja con el comando `academia.cmd`.

## Las cinco piezas de cada lección

Cada lección es una carpeta `academia\lecciones\Lxx-nombre\` con:

| Pieza | Fichero | Qué haces |
| --- | --- | --- |
| 1. Teoría | `README.md` | La idea, la norma, el fundamento y los errores típicos. 5 minutos. |
| 2. Ejemplo resuelto | `ejemplo.feature` | Un test que ya funciona, comentado. Lo ejecutas y lo lees. |
| 3. Campo de práctica | `practica.feature` | Zona libre. No cuenta para nada. |
| 4. Ejercicios | `ejercicios\E01.feature`... | Tests con huecos `___`. Cada uno trae enunciado y **pista 1**. |
| 5. Corrector | `academia corregir Lxx` | Dice qué has resuelto, el error en lenguaje humano y la pista. |

Además: `pistas.md` (pista 2 de cada ejercicio) y `academia\soluciones\Lxx-...\Exx.feature` (pista 3, la solución).

## Paso 1 · Entra en la carpeta de la academia

```powershell
cd D:\WORKSPACE_KARATE\nordvik\academia
```

## Paso 2 · Conoce los comandos

| Comando | Qué hace |
| --- | --- |
| `.\academia.cmd` o `.\academia.cmd empezar` | Enseña el README de la academia |
| `.\academia.cmd leer L00` | La teoría de la lección |
| `.\academia.cmd ejemplo L00` | Ejecuta el ejemplo resuelto |
| `.\academia.cmd practica L00` | Ejecuta tu campo de práctica |
| `.\academia.cmd corregir L00` | Corrige tus ejercicios y guarda el progreso en `progreso.json` |
| `.\academia.cmd corregir` | Corrige todas las lecciones |
| `.\academia.cmd pista L00 E01` | Pista 2 |
| `.\academia.cmd solucion L00 E01` | Pista 3: la solución. Úsala solo si te atascas. |
| `.\academia.cmd progreso` | Tu avance por lección |

## Paso 3 · Entiende cómo encuentra los servicios

Antes de ejecutar Karate, `academia.ps1` mira qué nivel necesita la lección (lo lee de `torre/escalera.json`):

- **Si la torre responde en el puerto 3000**, le pide `POST /api/v1/lecciones/L00/preparar`. La torre enciende en caliente lo que falte. Si enciende algo, verás: "La Torre de control ha encendido N servicios para esta lección."
- **Si Nordvik está parado**, arranca el perfil del nivel de la lección (por ejemplo `nivel-9` para L22).

Las URL de cada servicio están en `academia\config\karate-config.js` (`catalogUrl` = `http://localhost:6801`, `clockUrl` = `http://localhost:6003`...). En tus `.feature` escribes `Given url catalogUrl`, nunca el puerto a mano.

## Paso 4 · Ejecuta el ejemplo

```powershell
.\academia.cmd ejemplo L00
```

Salida real:

```text
 [OK] L00 · Ejemplo resuelto · L00 · Mi primera conversación con una API
```

**Qué significa.** Los dos escenarios del ejemplo pasan. Abre `lecciones\L00-que-es-una-api\ejemplo.feature` en VS Code y léelo línea a línea.

## Paso 5 · Corrige tus ejercicios

```powershell
.\academia.cmd corregir L00
```

Salida real con los huecos sin rellenar:

```text
══════════════════════════════════════════════════════════════════
 L00 · ¿Qué es una API?
══════════════════════════════════════════════════════════════════
 [  ] E01  El método para leer
        Qué ha pasado: Todavía quedan huecos ___ sin rellenar en este ejercicio.
        Pista: Leer en HTTP es el método más común de todos. Se escribe en minúsculas en Karate.
        Más pistas: academia pista L00 E01   ·   Detalle: target/academia/L00/E01/karate-summary.html
 [  ] E02  Predice el código de éxito
        ...
──────────────────────────────────────────────────────────────────
 Resultado L00: 0/5  ░░░░░░░░░░░░░░░░░░░░ 0 %
 Rellena los huecos ___ que faltan y vuelve a ejecutar: academia corregir L00
 Progreso guardado en progreso.json · 2026-10-26T09:44:34
```

**Qué significa cada línea:**

| Línea | Significado |
| --- | --- |
| `[  ]` / `[OK]` | Ejercicio pendiente / resuelto |
| Qué ha pasado | El error traducido a lenguaje humano |
| Pista | La pista 1 (la misma que está en el ejercicio) |
| Detalle | Informe HTML de Karate con la petición y la respuesta completas (`academia\target\academia\L00\E01\karate-summary.html`) |
| Resultado | Ejercicios resueltos de la lección |

## Paso 6 · Si te atascas, pide pistas

```powershell
.\academia.cmd pista L00 E01
```

```text
## E01 · El método para leer

Los métodos son get, post, put, patch y delete. Para leer sin cambiar nada se usa get.
```

```powershell
.\academia.cmd solucion L00 E01
```

```text
Pista 3 · solución completa. Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
# L00 · E01 · El método para leer
...
@L00 @E01 @solucion
Feature: L00 E01 · El método para leer

  Scenario: Leer la lista de fondos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
```

## Paso 7 · Mira tu avance

Tu avance se guarda en `academia\progreso.json`. La forma más cómoda de verlo es la **Escalera** de la torre: cada nivel muestra `resueltos/total` y cada lección un chip como `L00 · 1/5`.

También existe `.\academia.cmd progreso`, pero en Windows PowerShell 5.1 falla con el error `Method invocation failed because [System.Object[]] does not contain a method named 'op_Subtraction'` (ver [Problemas frecuentes](11-problemas-frecuentes.md)).

## Las 34 lecciones

| Lección | Tema | Ejercicios |
| --- | --- | --- |
| L00 | ¿Qué es una API? | 5 |
| L01 | Tu primera petición: url, path, method y status | 5 |
| L02 | Leer la respuesta: response, print y match | 5 |
| L03 | Navegar por el JSON y match contains | 6 |
| L04 | Parámetros de consulta: param y params | 5 |
| L05 | Variables, expresiones y Background | 5 |
| L06 | Los errores también se prueban: 400, 404 y RFC 9457 | 6 |
| L07 | Enviar datos: POST, request y cuerpos JSON | 6 |
| L08 | Validar sin fragilidad: fuzzy matchers | 6 |
| L09 | Listas: match each, karate.filter y karate.map | 6 |
| L10 | Muchos casos con una sola prueba: Scenario Outline | 5 |
| L11 | Cabeceras, correlación y caché | 5 |
| L12 | El tiempo en tus tests: el reloj virtual | 6 |
| L13 | Reutilizar: call y features llamables | 5 |
| L14 | Funciones JavaScript propias | 5 |
| L15 | Proyecto del tramo: una suite de mercado completa | 1 |
| L16 | Autenticación y roles: tokens OAuth2 en Karate | 6 |
| L17 | IDOR y máscaras: que cada uno vea solo lo suyo | 6 |
| L18 | Consistencia eventual: esperar sin dormir | 5 |
| L19 | Idempotencia: la misma orden dos veces no compra dos veces | 5 |
| L20 | Contratos y firma electrónica: un flujo con estados | 6 |
| L21 | Dos APIs, un JSON: probar una composición | 5 |
| L22 | Privacidad: tokens, máscaras y pasaportes de un solo uso | 6 |
| L23 | Caos y resiliencia: provocar fallos a propósito | 6 |
| L24 | Cumplimiento: listas de sanciones y coincidencia difusa | 6 |
| L25 | Llamadas de voz: procesos asíncronos que se autodestruyen | 6 |
| L26 | Webhooks firmados y ficheros de empresas (pensiones) | 6 |
| L27 | Sagas largas y reloj virtual: el traspaso de una pensión | 6 |
| L28 | XML y SOAP: hablar con el sistema de pensiones antiguo | 6 |
| L29 | Ficheros de ancho fijo: el mainframe | 6 |
| L30 | OAuth2 entre máquinas: la aseguradora | 6 |
| L31 | Contratos y esquemas: que nadie rompa a nadie | 6 |
| L32 | Tests inestables: medir la calidad de tu propia suite | 6 |
| L33 | Proyecto final: el derecho al olvido de punta a punta | 6 |

Nivel de cada lección (también en la sección "Niveles de la escalera y lecciones" de `academia/README.md`): L00-L15 nivel 0, L16-L17 nivel 1, L18-L21 nivel 2, L22-L27 nivel 3, L28-L33 nivel 4.

## Consejos de la academia

- **Predice antes de ejecutar.** Antes de rellenar un `___`, di qué esperas.
- **Lee el error entero.** Y abre el informe HTML cuando no lo entiendas.
- **Una lección al día** es mejor que cinco el domingo.
- **No edites** `lecciones/` ni `soluciones/` salvo tus `___`: se generan desde `academia\_generador`.

Siguiente: [8 · Swagger y primeras peticiones](08-swagger-y-primeras-peticiones.md)
