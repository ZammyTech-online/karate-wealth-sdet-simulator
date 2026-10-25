# 🎯 Karate Wealth SDET Simulator: Guía de Arranque y Prácticas

¡Tu ecosistema empresarial completo ya está instalado, compilado y en ejecución en `D:\WORKSPACE_KARATE`!

---

## 🧭 ¿Qué se ha creado para ti?

### 1. Backend Core Bancario de Pensiones (.NET 8 Web API)
- **Ubicación:** `D:\WORKSPACE_KARATE\soderberg-wealth-api`
- **Puerto:** `http://localhost:5125`
- **Swagger / OpenAPI:** [http://localhost:5125](http://localhost:5125) (puedes abrirlo en tu navegador para ver la documentación interactiva de todas las APIs).
- **Módulos implementados:**
  - `POST /api/v1/auth/token`: OAuth2 / JWT con roles (`Advisor`, `Admin`, `Customer`).
  - `GET /api/v1/funds`: Catálogo de fondos de pensiones con filtros y validación de ISIN.
  - `POST /api/v1/clients`: Alta de clientes con validación de identidad nórdica (`personnummer` tipo `YYYYMMDD-XXXX`) y perfil MiFID II.
  - `POST /api/v1/clients/{id}/portfolios`: Creación de carteras con asignación porcentual de activos (validación estricta del 100%).
  - `POST /api/v1/portfolios/{id}/contributions`: Aportaciones con retención fiscal actuarial (15% individual, 5% corporativo).
  - `POST /api/v1/orders/rebalance`: Órdenes asíncronas con patrón `202 Accepted` y polling hacia `/api/v1/orders/{id}/status`.
  - `POST /api/v1/pension/transfers`: Endpoints seguros que requieren firma criptográfica `X-Signature: HMAC-SHA256`.

---

### 2. Karate Automation Suite (Enterprise)
- **Ubicación:** `D:\WORKSPACE_KARATE\karate-test-suite`
- **Ejecutor:** JUnit 5 Paralelo (`WealthRegressionRunner.java`) con Karate 1.4.1 y Java 21.
- **Estructura de Features creados:**
  1. [`01_funds_catalog.feature`](file:///D:/WORKSPACE_KARATE/karate-test-suite/src/test/java/com/soderberg/qa/features/basics/01_funds_catalog.feature): Nivel 0 - Métodos HTTP, query params y fuzzy matchers (`#string`, `#regex`, `#?`).
  2. [`02_client_lifecycle.feature`](file:///D:/WORKSPACE_KARATE/karate-test-suite/src/test/java/com/soderberg/qa/features/reusability/02_client_lifecycle.feature): Nivel 1 - Reutilización de features (`karate.call` vs `karate.callonce`), manejo de tokens y validación de errores (`409 Conflict`, `422 Unprocessable`).
  3. [`03_async_rebalance.feature`](file:///D:/WORKSPACE_KARATE/karate-test-suite/src/test/java/com/soderberg/qa/features/async_orders/03_async_rebalance.feature): Nivel 2 - Polling asíncrono con patrón nativo `retry until`.
  4. [`04_crypto_transfer.feature`](file:///D:/WORKSPACE_KARATE/karate-test-suite/src/test/java/com/soderberg/qa/features/crypto_interop/04_crypto_transfer.feature): Nivel 3 - **Java Interop** (`Java.type('com.soderberg.qa.helpers.CryptoSigner')`) para firmar payloads bancarios.
- **Reportes HTML:** `D:\WORKSPACE_KARATE\karate-test-suite\target\karate-reports\karate-summary.html`

---

### 3. Karate Debug Bot & Árbol de Dependencias
- **Ubicación:** `D:\WORKSPACE_KARATE\karate-debug-bot`
- **Dashboard Web:** [http://localhost:3000](http://localhost:3000)
- **Funcionalidades:**
  - **Árbol de Llamadas:** Te muestra gráficamente de qué archivo o función sale cada variable (`* def`), qué features son invocados (`call` o `callonce`) y qué clases Java están interactuando.
  - **Ejecución en Vivo:** Botón de un solo clic para correr la regresión Maven desde la web y ver la salida de logs en tiempo real.
  - **Consejos Senior:** Guía paso a paso sobre cómo interpretar los errores más crípticos de Karate.
