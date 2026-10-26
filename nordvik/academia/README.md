# Academia Nordvik de Karate

Aprende a probar APIs con Karate **desde cero**, practicando contra un sistema de inversiones y pensiones que corre en tu PC.

## Cómo funciona

Cada lección tiene siempre las mismas cinco piezas:

| Pieza | Fichero | Qué haces |
| --- | --- | --- |
| 1. Teoría | `README.md` | La idea, la **norma** (las reglas), el **fundamento** (por qué) y los errores típicos. 5 minutos. |
| 2. Ejemplo resuelto | `ejemplo.feature` | Un test que ya funciona, comentado. Lo ejecutas y lo lees. |
| 3. Campo de práctica | `practica.feature` | Tu zona libre: prueba lo que quieras, no cuenta para nada. |
| 4. Ejercicios | `ejercicios/E01.feature` ... | Tests pequeños con huecos `___`. Cada uno trae enunciado y pista 1. |
| 5. Corrector | `academia corregir L01` | Te dice qué has resuelto, el error en lenguaje humano y la pista. |

Pistas en tres niveles: la **pista 1** está en el ejercicio, la **pista 2** con `academia pista L01 E03` y la **pista 3** (la solución) con `academia solucion L01 E03`, solo si te atascas.

## Empezar en 3 pasos

```text
1.  cd D:\WORKSPACE_KARATE\nordvik
    nordvik up                    (arranca los 8 servicios de la oleada 0, unos 70 MB de RAM)

2.  cd academia
    academia leer L00             (lee la teoría)
    academia ejemplo L00          (ejecuta el ejemplo)

3.  Abre lecciones\L00-que-es-una-api\ejercicios\E01.feature en VS Code,
    cambia los ___ y ejecuta:     academia corregir L00
```

## Tramo 1 · Fundamentos (L00-L15)

| Lección | Tema |
| --- | --- |
| L00 | ¿Qué es una API? Método, URL, código de estado y JSON |
| L01 | Tu primera petición: url, path, method y status |
| L02 | Leer la respuesta: response, print y match |
| L03 | Navegar por el JSON y match contains |
| L04 | Parámetros de consulta: param y params |
| L05 | Variables, expresiones y Background |
| L06 | Los errores también se prueban: 400, 404 y RFC 9457 |
| L07 | Enviar datos: POST, request y cuerpos JSON |
| L08 | Validar sin fragilidad: fuzzy matchers |
| L09 | Listas: match each, karate.filter y karate.map |
| L10 | Muchos casos con una sola prueba: Scenario Outline |
| L11 | Cabeceras, correlación y caché |
| L12 | El tiempo en tus tests: el reloj virtual |
| L13 | Reutilizar: call y features llamables |
| L14 | Funciones JavaScript propias |
| L15 | Proyecto del tramo: una suite de mercado completa |

## Tramo 2 · El Nordvik de verdad (L16-L21)

Necesita el perfil grande. La academia lo arranca sola si no está encendido, o hazlo tú: `nordvik up -Profile nivel-6`.

| Lección | Qué aprendes |
| --- | --- |
| L16 | Autenticación y roles: tokens OAuth2, 401 frente a 403, `callonce` |
| L17 | IDOR y máscaras: que cada uno vea solo lo suyo |
| L18 | Consistencia eventual: `retry until` en vez de dormir |
| L19 | Idempotencia: la misma orden dos veces no compra dos veces |
| L20 | Contratos y firma electrónica: una máquina de estados con NordID |
| L21 | Dos APIs, un JSON: probar una composición |

Los ayudantes `_login.feature`, `_iban.feature` y `_cliente-con-dinero.feature` están en la carpeta `ejercicios` de cada lección: léelos, son la mejor documentación de cada flujo.

## Todos los comandos

| Comando | Qué hace |
| --- | --- |
| `academia leer L03` | Muestra la teoría |
| `academia ejemplo L03` | Ejecuta el ejemplo resuelto |
| `academia practica L03` | Ejecuta tu campo de práctica |
| `academia corregir L03` | Corrige los ejercicios de la lección |
| `academia corregir` | Corrige todas las lecciones |
| `academia pista L03 E02` | Pista 2 |
| `academia solucion L03 E02` | Pista 3: la solución comentada |
| `academia progreso` | Tu avance por lección |

## Consejos

- **Predice antes de ejecutar.** Antes de rellenar un `___`, di en voz alta qué esperas. Si fallas, has aprendido algo.
- **Lee el error entero.** El corrector te lo traduce, y el reporte HTML (`target/academia/...`) te enseña la petición y la respuesta completas.
- **Una lección al día** es mejor que cinco el domingo.

## Para el equipo (no hace falta que lo uses)

- Las lecciones se generan desde `_generador/` con `python generar.py`. Nunca edites a mano `lecciones/` ni `soluciones/`.
- `academia verificar` comprueba que todos los ejemplos y soluciones pasan y que ningún ejercicio pasa con huecos.
