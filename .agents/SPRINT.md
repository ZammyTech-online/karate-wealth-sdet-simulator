<!-- SPRINT_AUTOPILOT: ON -->
# Sprint V4: Proyecto Bruno — Mentor y Copiloto SDET Híbrido

**Meta**: Desarrollar e integrar a "Bruno" como copiloto y mentor inteligente interactivo en la Academia y Torre de Control ([http://localhost:3000](http://localhost:3000)). El sistema cuenta con avatar SVG reactivo (stickman 2D con camiseta negra y gafas cian), motor heurístico local (diagnóstico RFC 9457, linter anti-patrones y explicador socrático sin coste de tokens) y puente híbrido preparado para conexión con IA futura.

| ID | Oleada | Tarea | Archivos Propios | Test Exigido | Auto-Correcciones | Estado & Verificador |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **B-01** | OLEADA 0 | Auditoría y escaneo de puntos de enganche de UI y eventos de Karate en Torre | `torre/web/index.html`, `app.js` | Smoke check de UI y listeners DOM | Detección bug openModal en app.js | **DONE** · pm_sprint_lead |
| **B-02** | OLEADA 1 | Avatar Reactivo SVG de Bruno (`bruno-avatar.js`): estados IDLE, THINKING, EXPLAINING, ALERT, SUCCESS | `torre/web/bruno-avatar.js` | Test unitario de renderizado SVG | Normalización de estado en fallback CSS | **DONE** · dev_executor |
| **B-03** | OLEADA 1 | Motor Heurístico de Diagnóstico (`bruno-heuristics.js`): traductor RFC 9457 y excepciones Karate | `torre/web/bruno-heuristics.js` | Test unitario de diagnósticos de error | Refinamiento de patrones 409 y BindException | **DONE** · dev_executor |
| **B-04** | OLEADA 1 | Linter de Calidad SDET Actuarial: detección de `karate.sleep`, datos hardcodeados y fuzzy matchers | `torre/web/bruno-linter.js` | Test unitario de reglas de linting | Detección de sintaxis match < | **DONE** · dev_executor |
| **B-05** | OLEADA 2 | Puente Híbrido Desacoplado (`bruno-bridge.js`): conmutador Modo Local Heurístico / Asistente IA | `torre/web/bruno-bridge.js` | Test de routing local vs stub IA | Fallback silencioso ante caída de red | **DONE** · dev_executor |
| **B-06** | OLEADA 2 | Endpoint Backend `/api/v1/bruno/chat` y `/api/v1/bruno/diagnostico` en Nordvik.Host | `platform/Nordvik.Host/ControlTower.cs` | Smoke curl HTTP 200 en endpoint Bruno | Deserialización segura con StreamReader | **DONE** · dev_executor |
| **B-07** | OLEADA 2 | Dock interactivo colapsable de Bruno en la Torre de Control (`index.html`, `styles.css`) | `torre/web/index.html`, `styles.css` | Verificación de apertura y cierre A11y | Integración de variables CSS y safe-area | **DONE** · dev_executor |
| **B-08** | OLEADA 3 | Integración de Bruno en el flujo de corrección de lecciones de la Academia (`app.js`) | `torre/web/app.js` | Test de feedback reactivo tras corrección | Conexión con linter en tiempo real | **DONE** · dev_executor |
| **B-09** | OLEADA 3 | Suite de tests unitarios y de integración para Bruno (`bruno.test.mjs`) | `tests/bruno.test.mjs` | `node tests/bruno.test.mjs` en verde | 17/17 tests passing en Node.js | **DONE** · qa_auto_verifier |
| **B-10** | OLEADA 3 | Documentación de arquitectura y guía de uso de Bruno en `docs/BRUNO.md` | `docs/BRUNO.md` | Markdown linter | Guía exhaustiva y diagrama Mermaid | **DONE** · dev_executor |
| **B-11** | OLEADA 4 | Anillo 1: Verificación de compilación .NET y tests Maven Karate | `Nordvik.sln`, `karate-test-suite` | `dotnet build` & `mvn test` exitosos | 136 unit tests + 3 Karate basics OK | **DONE** · qa_auto_verifier |
| **B-12** | OLEADA 4 | Anillo 2: Robot Navegador en vivo sobre Torre de Control (0 errores, 0 404s) | `http://localhost:3000` | Browser network & console audit | Verificación interactiva de Bruno Dock | **DONE** · browser |
| **B-13** | OLEADA 4 | Anillo 3: Juicio cruzado entre agentes (`qa_auto_verifier`, `ux_persona_tester`, `security_guardian`) | `.agents/gate-receipt.json` | Veredicto de subagentes en flash | Firma de auditoría | **DONE** · qa_auto_verifier |
| **B-14** | OLEADA 4 | Generación del Dossier Auditable Final `.agents/SPRINT_REPORT.md` | `.agents/SPRINT_REPORT.md` | Reporte final con 6 secciones | Entrega y enlaces locales file:/// | **DONE** · stakeholder_guardian |
