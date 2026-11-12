# Gherkin BDD Gym de Karate DSL (Given - When - Then)
### Centro de Entrenamiento Actuarial SDET de Cero a Staff

El **Gherkin BDD Gym** es el gimnasio interactivo de Karate DSL integrado en la Torre de Control de Nordvik (`http://localhost:3000/#gym`). Ofrece **4,000 ejercicios combinatorios** clasificados en 4 niveles de seniority técnica (Junior, Mid, Senior y Staff), respaldados por el copiloto actuarial **Bruno SDET**, validación en tiempo real con linter semántico y verificación automática de contratos BDD.

---

## 1. Filosofía BDD y el Patrón AAA en Karate DSL

Aunque Karate DSL internamente trata las palabras clave de Gherkin (`Given`, `When`, `Then`, `And`, `*`) con el mismo motor de ejecución, las mejores prácticas de ingeniería SDET exigen una separación estricta para garantizar mantenibilidad, legibilidad de negocio y contratos limpios:

```gherkin
Feature: Gestión de Fondos de Inversión Nordvik

  Background:
    * url baseUrl
    * def auth = call read('classpath:karate-auth.feature')
    * header Authorization = 'Bearer ' + auth.token

  Scenario: Suscripción a fondo ISIN SE0001234567 (Patrón AAA)
    # [ARRANGE] Preparar estado, parámetros y payload
    Given path '/api/v1/funds/SE0001234567/subscribe'
    And request { amount: 50000, currency: 'SEK' }

    # [ACT] Disparo exclusivo de la operación HTTP
    When method post

    # [ASSERT] Validación del contrato de respuesta
    Then status 201
    And match response.transactionId == '#regex ^TX-[0-9]{8}$'
    And match response.status == 'CONFIRMED'
```

### Reglas Semánticas Innegociables
1. **Given (Arrange)**: Solo configura URLs, paths, headers, query params o request body. **Prohibido** colocar `match` o `assert` dentro de Given.
2. **When (Act)**: Contiene **un único** disparo HTTP (`When method get`, `When method post`, etc.). Múltiples disparos en el mismo escenario violan la atomicidad de la prueba y son bloqueados por el linter.
3. **Then (Assert)**: Inmediatamente después de `When method`, debe validarse el código de estado (`Then status 200/201/400`).
4. **And (Assert Continued)**: Valida el cuerpo JSON, cabeceras de respuesta o esquemas (`#string`, `#number`, `#regex`).

---

## 2. Mapa Curricular: De Junior a Staff (4,000 Retos)

| Nivel | Rango de IDs | Total | Enfoque Técnico | Conceptos Clave |
| :--- | :--- | :--- | :--- | :--- |
| **Junior SDET** | `JR-0001` a `JR-1000` | 1,000 | Fundamentos AAA y Métodos HTTP | `Given path`, `When method`, `Then status`, `And match ==`, Fuzzy matchers (`#string`, `#number`) |
| **Mid SDET** | `MID-0001` a `MID-1000` | 1,000 | Data-Driven Testing y Esquemas | `Scenario Outline`, `Examples:` tabular, validación de esquemas JSON estrictos |
| **Senior SDET** | `SR-0001` a `SR-1000` | 1,000 | Asincronía, Sagas y Resiliencia | Polling no bloqueante con `retry until`, consistencia eventual, contratos de error RFC 9457 |
| **Staff / Lead** | `STF-0001` a `STF-1000` | 1,000 | Mocks, Caos, Reloj y Seguridad | Mocks dinámicos con `karate.start`, inyección de latencia/fallos, salto temporal y RBAC |

---

## 3. Características del Gimnasio Interactivo

### Modos de Práctica
- **Modo Endless (Continuo)**: Bruno presenta un reto aleatorio del nivel seleccionado. Tras verificar con éxito, acumulas XP (+15 XP por acierto), incrementas tu racha de aciertos (`🔥 Racha`) y avanzas inmediatamente al siguiente reto.
- **Modo Catálogo**: Permite explorar el índice de 4,000 ejercicios filtrando por dominio de negocio (Fondos, Cuentas, Órdenes, Divisas FX, Pensiones, KYC, Caos, Mercado).

### Linter Semántico en Vivo
A medida que escribes en el editor de código, el motor de `BrunoLinter` audita en tiempo real sin llamar al servidor:
- ❌ **Aserciones en Given**: Detecta si intentas validar `response` antes de ejecutar la llamada.
- ❌ **Múltiples When methods**: Alerta si combinas dos peticiones en el mismo escenario.
- ❌ **Falta de Then status**: Recuerda validar el código HTTP tras el disparo.
- ❌ **Anti-patrón de sleep()**: Bloquea cualquier intento de usar pausas pasivas.
- ❌ **Datos fijos hardcodeados**: Detecta números personales suecos (PNR) o IBANs estáticos.

### Audio & Teleprompter Multilingüe (ElevenLabs)
En la sección de Academia (`#academia`), Bruno ofrece lecciones locutadas en audio con sincronización palabra por palabra:
- Resaltado visual interactivo (`.kw-active`) estilo karaoke.
- Desafíos predictivos que pausan la narración para que el estudiante prediga la sintaxis de Karate antes de escucharla.

---

## 4. Referencia de la API REST del Gym

| Método | Endpoint | Parámetros | Descripción |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/v1/gym/stats` | Ninguno | Estadísticas globales (total de 4,000 ejercicios y desglose por nivel). |
| `GET` | `/api/v1/gym/exercise` | `id` (ej. `JR-0001`) | Recupera un ejercicio específico con enunciado, huecos (`___`) y pistas. |
| `GET` | `/api/v1/gym/random` | `level` (`junior`, `mid`, `senior`, `staff`) | Devuelve un ejercicio aleatorio del nivel especificado. |
| `POST` | `/api/v1/gym/verify` | Body JSON: `{ id, code }` | Evalúa el código enviado contra las reglas BDD y los huecos esperados. |

---

## 5. Ejemplo de Verificación vía CLI

```powershell
# Obtener un ejercicio aleatorio Senior
$exercise = Invoke-RestMethod -Uri "http://localhost:3000/api/v1/gym/random?level=senior"
Write-Host "Reto: $($exercise.title)"

# Resolver y verificar
$body = @{
    id = $exercise.id
    code = $exercise.solution
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "http://localhost:3000/api/v1/gym/verify" -Method Post -Body $body -ContentType "application/json"
Write-Host "Resultado: $($result.message) [XP: $($result.xp)]"
```
