# Dossier de Sprint V3: Industrialización y Cierre del Tramo 5 Senior Karate SDET

**Fecha**: 2026-11-12  
**Entorno**: `D:\WORKSPACE_KARATE`  
**Rama Git**: `nordvik-platform`  
**Estado de Entrega**: **100% COMPLETADO (TRIPLE VERIFICACIÓN PASSED)**

---

## 1. 🟢 Servidor en Vivo y URLs Directas

| Servicio / Interfaz | URL Local | Descripción | Estado |
| :--- | :--- | :--- | :--- |
| **Torre de Control & Academia** | [http://localhost:3000](http://localhost:3000) | Panel interactivo central y visor visual de la Academia | 🟢 **ACTIVO** |
| **Catálogo de Lecciones API** | [http://localhost:3000/api/v1/lecciones](http://localhost:3000/api/v1/lecciones) | Endpoint REST que entrega las 40 lecciones (L00-L39) | 🟢 **ACTIVO (200 OK)** |
| **Lección Senior L34 API** | [http://localhost:3000/api/v1/lecciones/L34](http://localhost:3000/api/v1/lecciones/L34) | Endpoint con teoría, ejemplos, pistas y ejercicios de mocks | 🟢 **ACTIVO (200 OK)** |
| **Soderberg Wealth Core API** | [http://localhost:5125](http://localhost:5125) | Backend bancario .NET 8 con Swagger y endpoints financieros | 🟢 **ACTIVO** |
| **Swagger Core API** | [http://localhost:5125/swagger](http://localhost:5125/swagger) | Documentación interactiva OpenAPI de fondos y transferencias | 🟢 **ACTIVO (200 OK)** |

---

## 2. 🧭 Guía Paso a Paso: Dónde Probar y Qué Debes Ver

| Paso | Acción a Realizar | Dónde Ejecutar / Abrir | Qué Debes Ver |
| :---: | :--- | :--- | :--- |
| **1** | Abrir la Torre de Control | Navegador en [http://localhost:3000](http://localhost:3000) | Dashboard de control con el perfil activo, servicios encendidos y selector de pestañas. |
| **2** | Abrir la pestaña Academia | Clic en `#academia` en el menú superior | Catálogo interactivo de lecciones de L00 a L39, con el Tramo 5 Senior destacado. |
| **3** | Inspeccionar lección L34 | Abrir `L34 · Servidores Mock Dinámicos` | Editor interactivo con teoría de `karate.start()`, ejemplo funcional y 5 ejercicios guiados. |
| **4** | Ejecutar ejemplo resuelto L34 | En consola: `powershell -File D:\WORKSPACE_KARATE\nordvik\academia\academia.ps1 ejemplo L34` | Salida en consola: `[OK] L34 · Ejemplo resuelto · L34 · Ejemplo de servidor Mock con karate.start`. |
| **5** | Verificar soluciones completas | En consola: `powershell -File D:\WORKSPACE_KARATE\nordvik\academia\academia.ps1 verificar L34` | Resultados: `5/5 100% OK` en soluciones y comprobación de que los huecos `___` fallan como corresponde. |
| **6** | Ejecutar suite de regresión | En consola: `mvn test -f D:\WORKSPACE_KARATE\karate-test-suite\pom.xml` | `BUILD SUCCESS`: 9 de 9 escenarios en verde de fondos, ciclo de vida, transferencias y HMAC criptográfico. |

---

## 3. 🛠️ Bitácora de Auto-Correcciones Realizadas por el Squad

1. **Resolución de Dependencias Relativas en Mocks (`this:` syntax)**:
   - *Problema*: `_mock_demo.feature` se escribía únicamente dentro de `ejercicios/`, lo que causaba un `FileNotFoundException` al ejecutar el `ejemplo.feature` situado en la raíz de la lección.
   - *Corrección*: Se actualizó `generar.py` para copiar todos los archivos de apoyo (`lesson["apoyo"]`) tanto en `ldir` como en `ldir/ejercicios` y `sdir/`.
2. **Sintaxis de Aserciones Relativas en Karate DSL**:
   - *Problema*: Instrucciones de tipo `* match latency < maxMs` arrojaban error de parseo (`syntax error, expected '==' for match`).
   - *Corrección*: Se sustituyeron por aserciones nativas con palabra clave `assert` (`* assert latency < maxMs`, `* assert p95Threshold <= 300`).
3. **Estabilización de Conexiones Loopback en Windows (`BindException`)**:
   - *Problema*: La concurrencia paralela rápida reciclaba puertos TCP efímeros en `TIME_WAIT` contra `localhost:5125`.
   - *Corrección*: Se ajustó `WealthRegressionRunner.java` para parametrizar dinámicamente el número de hilos mediante `-Dkarate.threads` (por defecto 1 en loopback intensivo local), logrando 100% de pasadas estables.
4. **Mapeo de Escalera de Aprendizaje (`escalera.json`)**:
   - *Problema*: Las lecciones L34 a L39 figuraban en `level: -1` al no estar vinculadas en el esquema JSON de niveles.
   - *Corrección*: Se asociaron formalmente al Nivel 4 (Senior) con sus respectivas competencias de mocking, caching, tags, hooks, Gatling y CI/CD.

---

## 4. 🤖 Certificado de Triple Verificación

```json
{
  "version": "3.0.0",
  "status": "PASSED",
  "timestamp": "2026-11-12T14:39:10.979Z",
  "workspaceRoot": "D:\\WORKSPACE_KARATE",
  "checks": [
    {
      "name": "SPRINT.md Tickets Completed",
      "passed": true,
      "details": "16 tickets DONE, 0 tickets pendientes"
    },
    {
      "name": "Typecheck Gate",
      "passed": true,
      "details": "dotnet build D:\\WORKSPACE_KARATE\\nordvik\\Nordvik.sln --nologo -v q -> exit 0"
    },
    {
      "name": "Automated Tests Gate",
      "passed": true,
      "details": "mvn test -f D:\\WORKSPACE_KARATE\\karate-test-suite\\pom.xml -> exit 0"
    }
  ]
}
```

- **Anillo 1 (Robot CLI)**: Verificación de compilación .NET sin errores y ejecución Maven 9/9 en verde certificada en `.agents/gate-receipt.json`.
- **Anillo 2 (Robot Browser / Network)**: Inspección de rutas `/api/v1/lecciones`, `/api/v1/lecciones/L34` y `/api/v1/estado` con 0 errores HTTP 404, 0 excepciones de consola y 0 textos elipsados.
- **Anillo 3 (Juicio Cruzado de Subagentes)**:
  - `qa_auto_verifier`: **APROBADO** (Cobertura completa de soluciones L34-L39 y suite Karate en verde).
  - `security_guardian`: **APROBADO** (Cero credenciales expuestas, tokens sintéticos de prueba aislados).
  - `ux_persona_tester`: **APROBADO** (Layout WCAG 2.1 AA claro, sin truncamientos de texto).
  - `stakeholder_guardian`: **APROBADO** (Cierre formal del Sprint).

---

## 5. 📂 Links Clicables a Archivos, Tests y Artefactos

- **Recibo de Gates**: [gate-receipt.json](file:///D:/WORKSPACE_KARATE/.agents/gate-receipt.json)
- **Sprint Backlog Auditado**: [SPRINT.md](file:///D:/WORKSPACE_KARATE/.agents/SPRINT.md)
- **Memoria de Causa Raíz**: [LEARNINGS.md](file:///D:/WORKSPACE_KARATE/.agents/LEARNINGS.md)
- **Generador de Tramo 5**: [lecciones_g.py](file:///D:/WORKSPACE_KARATE/nordvik/academia/_generador/lecciones_g.py)
- **Script Compilador de Lecciones**: [generar.py](file:///D:/WORKSPACE_KARATE/nordvik/academia/_generador/generar.py)
- **Definición de Escalera de Aprendizaje**: [escalera.json](file:///D:/WORKSPACE_KARATE/nordvik/torre/escalera.json)
- **Documentación General de Academia**: [README.md](file:///D:/WORKSPACE_KARATE/nordvik/academia/README.md)
- **Runner Paralelo de Regresión**: [WealthRegressionRunner.java](file:///D:/WORKSPACE_KARATE/karate-test-suite/src/test/java/com/soderberg/qa/WealthRegressionRunner.java)
- **Reporte HTML Interactivo Karate**: [karate-summary.html](file:///D:/WORKSPACE_KARATE/karate-test-suite/target/karate-reports/karate-summary.html)

---

## 6. 🚀 Backlog Sugerido para el Próximo Sprint

1. **Dashboard de Cobertura Actuarial en Torre de Control**: Gráfico de araña interactivo que visualice en tiempo real qué porcentaje de los 168 microservicios tiene al menos un test Karate asociado.
2. **Generador Automático de Escenarios Negativos**: CLI helper en `academia.ps1` que genere mutaciones de payloads con valores límites (fuzzing) para desafiar a los alumnos en lecciones avanzadas.
3. **Pipeline de GitHub Actions Pre-configurado**: Añadir `.github/workflows/karate-regression.yml` al repositorio para ejecutar la suite completa en cada PR hacia la rama `nordvik-platform`.
