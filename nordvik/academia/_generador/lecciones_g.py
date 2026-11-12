# Tramo 5 · Lecciones L34-L39: Maestría y Técnicas Senior de SDET en Karate.
# Cubre: Mocks con karate.start, callSingle con cache, ejecución paralela, Java RuntimeHooks, Gatling y CI/CD.

from lecciones_d import LOGIN

MOCK_FEATURE = r"""
@ignore
Feature: Servidor Mock de Prueba para Fallos y Aprobaciones Actuariales

  Scenario: pathMatches('/api/v1/mock/actuary') && methodIs('get')
    * def response = { status: 'APPROVED', riskScore: 12.5, approvedBy: 'Robot-Senior' }
    * def responseStatus = 200

  Scenario: pathMatches('/api/v1/mock/actuary') && methodIs('post')
    * def response = { transactionId: 'TX-999', result: 'COMMITTED' }
    * def responseStatus = 201

  Scenario: pathMatches('/api/v1/mock/simulate-timeout')
    * karate.abort()
"""

LECCION_L34 = {
    "code": "L34",
    "slug": "mocks-con-karate-start",
    "url": "catalogUrl",
    "readme": """# L34 · Servidores Mock Dinámicos con karate.start

Cuando un sistema bancario depende de una pasarela externa (como un regulador fiscal o una agencia de crédito que cobra por consulta o se cae en sandbox), un Senior SDET no bloquea la suite: levanta un **Mock Server dinámico** con Karate dentro de la propia ejecución.

## La norma
- `karate.start('mock-server.feature')` arranca un servidor HTTP embebido en un puerto libre o asignado.
- El objeto retornado expone `mock.port`, permitiendo configurar la URL del test hacia `http://localhost:' + mock.port`.
- Al finalizar el test o feature, `mock.stop()` libera el socket.

## El fundamento
Permite simular fallos de red, códigos HTTP 500/503 y retrasos extremos de forma determinista y sin depender de la nube.
""",
    "ejemplo": """@L34 @ejemplo
Feature: L34 · Ejemplo de servidor Mock con karate.start

  Scenario: Levantar mock y consumir endpoint simulado
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * def mockPort = mock.port
    * url 'http://localhost:' + mockPort
    * path '/api/v1/mock/actuary'
    * method get
    * status 200
    * match response == { status: 'APPROVED', riskScore: 12.5, approvedBy: 'Robot-Senior' }
    * mock.stop()
""",
    "apoyo": {
        "_mock_demo.feature": MOCK_FEATURE
    },
    "ejercicios": [
        {
            "id": "E01",
            "title": "Arrancar un mock en puerto dinámico",
            "enunciado": "Usa karate.start() para iniciar el mock _mock_demo.feature en un puerto libre (port: 0) y verifica que mock.port sea un número.",
            "pista1": "karate.start recibe un objeto { mock: '...', port: 0 }",
            "pista2": "Usa match mock.port == '#number' y luego mock.stop()",
            "explicacion": "karate.start con port: 0 le pide al sistema operativo un puerto TCP efímero libre.",
            "feature": """  Scenario: E01 · Arrancar mock dinámico
    * def mock = ⟦karate.start({ mock: 'this:_mock_demo.feature', port: 0 })⟧
    * match mock.port == ⟦'#number'⟧
    * mock.stop()
"""
        },
        {
            "id": "E02",
            "title": "Consultar el endpoint GET del mock",
            "enunciado": "Arranca el mock, apunta url a http://localhost:port, haz GET a /api/v1/mock/actuary y valida status 200.",
            "pista1": "Apunta la url al mockPort",
            "pista2": "path '/api/v1/mock/actuary' y method get",
            "explicacion": "El mock responde inmediatamente con la lógica definida en sus scenarios.",
            "feature": """  Scenario: E02 · Consumir mock GET
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    Given url 'http://localhost:' + mock.port
    And path ⟦'/api/v1/mock/actuary'⟧
    When method ⟦get⟧
    Then status ⟦200⟧
    * match response.status == ⟦'APPROVED'⟧
    * mock.stop()
"""
        },
        {
            "id": "E03",
            "title": "Enviar POST al mock y verificar respuesta 201",
            "enunciado": "Haz un POST a /api/v1/mock/actuary enviando un cuerpo JSON { orderId: 'ORD-1' } y valida status 201.",
            "pista1": "Usa method post y Then status 201",
            "pista2": "Valida response.result == 'COMMITTED'",
            "explicacion": "Los mocks permiten probar que el cliente maneja respuestas 201 con payloads complejos.",
            "feature": """  Scenario: E03 · POST a mock
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    Given url 'http://localhost:' + mock.port
    And path '/api/v1/mock/actuary'
    And request ⟦{ orderId: 'ORD-1' }⟧
    When method ⟦post⟧
    Then status ⟦201⟧
    * match response.result == ⟦'COMMITTED'⟧
    * mock.stop()
"""
        },
        {
            "id": "E04",
            "title": "Detener el mock y liberar el puerto",
            "enunciado": "Verifica que llamar a mock.stop() apaga el servidor y detiene el listener.",
            "pista1": "Invoca mock.stop() al terminar",
            "pista2": "El método stop() no recibe argumentos",
            "explicacion": "Liberar los sockets previene fugas de memoria y puertos bloqueados en pipelines CI/CD.",
            "feature": """  Scenario: E04 · Apagado limpio de mock
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * def port = mock.port
    * ⟦mock.stop()⟧
    * match port == '#number'
"""
        },
        {
            "id": "E05",
            "title": "Aislamiento de tests con mock independiente",
            "enunciado": "Comprueba que dos llamadas consecutivas con mocks independientes no colisionan.",
            "pista1": "Arranca mock1, páralo, y arranca mock2",
            "pista2": "Cada uno obtiene un puerto asignado",
            "explicacion": "El aislamiento hermético garantiza cero colisiones entre hilos de prueba.",
            "feature": """  Scenario: E05 · Mocks aislados
    * def m1 = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * m1.stop()
    * def m2 = ⟦karate.start({ mock: 'this:_mock_demo.feature', port: 0 })⟧
    * match m2.port == ⟦'#number'⟧
    * m2.stop()
"""
        }
    ]
}

LECCION_L35 = {
    "code": "L35",
    "slug": "callsingle-y-entornos",
    "url": "catalogUrl",
    "readme": """# L35 · karate.callSingle y Cache de Autenticación Enterprise

En suites grandes (con más de 500 tests), hacer login en cada Scenario destruye el servidor de IAM y ralentiza la regresión. `karate.callSingle()` ejecuta una rutina **una sola vez para toda la suite** y cachea el resultado en memoria.

## La norma
- Se usa típicamente en `karate-config.js`: `karate.callSingle('classpath:auth.feature', config)`.
- El objeto retornado es inmutable y compartido entre todos los hilos paralelos.

## El fundamento
Ahorra hasta un 90% del tiempo de ejecución en CI/CD y protege los límites de tasa (rate limiting) de IAM.
""",
    "ejemplo": """@L35 @ejemplo
Feature: L35 · Ejemplo de karate.callSingle

  Scenario: Reutilizar token globalmente
    * def session = karate.callSingle('this:_mock_auth.feature')
    * match session.token == '#present'
    * match session.role == 'SYSTEM_ADMIN'
""",
    "apoyo": {
        "_mock_auth.feature": """@ignore
Feature: Auth Mock
  Scenario:
    * def token = 'nordvik-bearer-cache-token-xyz'
    * def role = 'SYSTEM_ADMIN'
"""
    },
    "ejercicios": [
        {
            "id": "E01",
            "title": "Invocar rutina con karate.callSingle",
            "enunciado": "Llama a _mock_auth.feature usando karate.callSingle y verifica que devuelva el token cacheado.",
            "pista1": "Usa karate.callSingle('this:_mock_auth.feature')",
            "pista2": "match res.token == 'nordvik-bearer-cache-token-xyz'",
            "explicacion": "callSingle memoriza la ejecución y la comparte entre todos los escenarios.",
            "feature": """  Scenario: E01 · callSingle básico
    * def auth = ⟦karate.callSingle('this:_mock_auth.feature')⟧
    * match auth.token == ⟦'nordvik-bearer-cache-token-xyz'⟧
"""
        },
        {
            "id": "E02",
            "title": "Validar propiedad de rol administrativo",
            "enunciado": "Verifica que el rol obtenido desde la sesión cacheada sea 'SYSTEM_ADMIN'.",
            "pista1": "auth.role debe coincidir",
            "pista2": "match auth.role == 'SYSTEM_ADMIN'",
            "explicacion": "Los claims de rol permiten armar cabeceras Authorization de forma transparente.",
            "feature": """  Scenario: E02 · Validar rol cacheado
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * match auth.role == ⟦'SYSTEM_ADMIN'⟧
"""
        },
        {
            "id": "E03",
            "title": "Comprobar persistencia idéntica en memoria",
            "enunciado": "Llama a callSingle por segunda vez y confirma que el objeto devuelto contiene la misma propiedad token.",
            "pista1": "Segunda llamada a karate.callSingle",
            "pista2": "No vuelve a ejecutar la red, lee del mapa de memoria",
            "explicacion": "Karate indexa el feature path en su runtime JVM y devuelve la referencia ya evaluada.",
            "feature": """  Scenario: E03 · Cache garantizado
    * def s1 = karate.callSingle('this:_mock_auth.feature')
    * def s2 = ⟦karate.callSingle('this:_mock_auth.feature')⟧
    * match s1.token == ⟦s2.token⟧
"""
        },
        {
            "id": "E04",
            "title": "Inyección en cabeceras de petición",
            "enunciado": "Usa el token obtenido para construir una cabecera Authorization: Bearer <token>.",
            "pista1": "Concatena 'Bearer ' + auth.token",
            "pista2": "header Authorization = 'Bearer ' + auth.token",
            "explicacion": "Patrón estándar para autenticar peticiones contra microservicios seguros.",
            "feature": """  Scenario: E04 · Cabecera construida
    * def auth = karate.callSingle('this:_mock_auth.feature')
    * def headerAuth = ⟦'Bearer ' + auth.token⟧
    * match headerAuth == ⟦'Bearer nordvik-bearer-cache-token-xyz'⟧
"""
        },
        {
            "id": "E05",
            "title": "Configuración multientorno con karate.env",
            "enunciado": "Valida que karate.env esté definido o adopte un valor por defecto seguro como 'dev'.",
            "pista1": "karate.env || 'dev'",
            "pista2": "match env == '#string'",
            "explicacion": "Garantiza que la suite pueda ejecutarse en local, dev, staging o producción.",
            "feature": """  Scenario: E05 · Entorno seguro
    * def env = ⟦karate.env || 'dev'⟧
    * match env == ⟦'#string'⟧
"""
        }
    ]
}

LECCION_L36 = {
    "code": "L36",
    "slug": "paralelismo-y-tags",
    "url": "catalogUrl",
    "readme": """# L36 · Ejecución Paralela, Tags y Runner JUnit 5

En un entorno empresarial bancario, una suite completa puede tener miles de assertions. Ejecutarlas secuencialmente tardaría horas. Karate soporta **ejecución paralela nativa** controlada por etiquetas (`@tags`) y el runner de JUnit 5.

## La norma
- `Runner.path("classpath:lecciones").tags("@smoke", "~@ignore").parallel(5)`
- La tilde `~` niega una etiqueta (excluye tests inestables o lentos).
- Los escenarios paralelos deben ser **herméticos** y no compartir datos mutables en disco o BD.
""",
    "ejemplo": """@L36 @ejemplo
Feature: L36 · Tags y preparación de ejecución paralela

  @smoke @fast
  Scenario: Escenario rápido de comprobación
    * def status = 'HEALTHY'
    * match status == 'HEALTHY'

  @regression
  Scenario: Escenario de regresión actuarial
    * def balance = 50000
    * assert balance > 10000
""",
    "ejercicios": [
        {
            "id": "E01",
            "title": "Etiquetado semántico con @smoke",
            "enunciado": "Asegura la presencia del tag @smoke en un escenario crítico de salud de servicio.",
            "pista1": "Usa la convención de tags de Karate con arroba",
            "pista2": "El escenario debe marcar @smoke",
            "explicacion": "Los tags @smoke se ejecutan en cada commit en los pipelines de GitHub Actions.",
            "feature": """  @smoke
  Scenario: E01 · Smoke test marcado
    * def ready = ⟦true⟧
    * match ready == ⟦true⟧
"""
        },
        {
            "id": "E02",
            "title": "Filtrado condicional por tag en runtime",
            "enunciado": "Comprueba mediante karate.tagSelector o tags nativos que el escenario responde a la suite @senior.",
            "pista1": "Etiqueta @senior",
            "pista2": "Valida lógica simple con match",
            "explicacion": "Permite disparar únicamente los módulos financieros aprobados.",
            "feature": """  @senior
  Scenario: E02 · Tag senior
    * def level = ⟦'SENIOR'⟧
    * match level == ⟦'SENIOR'⟧
"""
        },
        {
            "id": "E03",
            "title": "Exclusión de escenarios con tilde",
            "enunciado": "Documenta la sintaxis de exclusión para saltar tests etiquetados con @flaky (~@flaky).",
            "pista1": "El selector de tags usa ~ para negar",
            "pista2": "def tagFilter = '~@flaky'",
            "explicacion": "Excluir pruebas inestables mientras se reparan previene falsos positivos en el gate de despliegue.",
            "feature": """  Scenario: E03 · Exclusión de flaky tests
    * def selector = ⟦'~@flaky'⟧
    * match selector == ⟦'~@flaky'⟧
"""
        },
        {
            "id": "E04",
            "title": "Configurar número de hilos paralelos",
            "enunciado": "Define el parámetro de paralelismo para un runner Java con 4 hilos concurrentes.",
            "pista1": "def hilos = 4",
            "pista2": "match hilos == 4",
            "explicacion": "El número de hilos debe alinearse con los cores de CPU de la máquina o contenedor.",
            "feature": """  Scenario: E04 · Hilos paralelos
    * def threads = ⟦4⟧
    * match threads == ⟦4⟧
"""
        },
        {
            "id": "E05",
            "title": "Validar aserción independiente de tiempo",
            "enunciado": "Escribe una aserción que no dependa de sleeps ni orden secuencial de ejecución.",
            "pista1": "Verifica invariantes puras de datos",
            "pista2": "match item.completed == true",
            "explicacion": "Los tests concurrentes jamás deben suponer que otro test corrió antes.",
            "feature": """  Scenario: E05 · Invariante concurrente
    * def testData = { id: 'TX-1', status: 'OK' }
    * match testData.status == ⟦'OK'⟧
"""
        }
    ]
}

LECCION_L37 = {
    "code": "L37",
    "slug": "java-runtime-hook",
    "url": "catalogUrl",
    "readme": """# L37 · Interceptores y Java RuntimeHook

Cuando la regulación bancaria exige registrar cada petición HTTP, firmar auditorías criptográficas o enmascarar tokens antes de que toquen el reporte HTML, un SDET implementa un **ExecutionHook / RuntimeHook** en Java.

## La norma
- Implementa `com.intuit.karate.RuntimeHook`.
- Métodos clave: `beforeScenario()`, `afterScenario()`, `beforeStep()`, `afterStep()`.
- Permite inyectar lógica pura en el ciclo de vida de Karate sin ensuciar los archivos `.feature`.
""",
    "ejemplo": """@L37 @ejemplo
Feature: L37 · Configuración de Hooks de Ciclo de Vida

  Scenario: Hook de escenario ejecutado
    * configure afterScenario = function(){ karate.log('Scenario completado con éxito') }
    * def x = 100
    * match x == 100
""",
    "ejercicios": [
        {
            "id": "E01",
            "title": "Configurar afterScenario en línea",
            "enunciado": "Usa configure afterScenario para registrar un mensaje en el log al finalizar.",
            "pista1": "configure afterScenario = function(){ ... }",
            "pista2": "karate.log('done')",
            "explicacion": "afterScenario se ejecuta siempre, incluso si el escenario falló.",
            "feature": """  Scenario: E01 · afterScenario configurado
    * ⟦configure afterScenario = function(){ karate.log('cleanup') }⟧
    * def a = 1
    * match a == 1
"""
        },
        {
            "id": "E02",
            "title": "Intercepción de errores y limpieza",
            "enunciado": "Define una función de limpieza para resetear el reloj virtual o base de datos en caso de fallo.",
            "pista1": "Usa una función JS asignada a afterScenario",
            "pista2": "function() { karate.log('reset') }",
            "explicacion": "Garantiza que el estado de la plataforma quede limpio para el siguiente test.",
            "feature": """  Scenario: E02 · Limpieza tras fallo
    * def cleanup = ⟦function(){ return 'CLEAN'; }⟧
    * def res = cleanup()
    * match res == ⟦'CLEAN'⟧
"""
        },
        {
            "id": "E03",
            "title": "Lectura de metadatos del escenario",
            "enunciado": "Accede a la variable karate.scenario para inspeccionar el nombre del escenario en ejecución.",
            "pista1": "karate.scenario.name",
            "pista2": "match name == '#present'",
            "explicacion": "Permite generar auditorías y logs detallados con el nombre exacto de la prueba.",
            "feature": """  Scenario: E03 · Metadatos de escenario
    * def name = ⟦karate.scenario.name⟧
    * match name == ⟦'#present'⟧
"""
        },
        {
            "id": "E04",
            "title": "Medición de tiempo transcurrido con karate.info",
            "enunciado": "Verifica que karate.info contenga las claves del feature en ejecución.",
            "pista1": "karate.info",
            "pista2": "match karate.info.featureDir == '#present'",
            "explicacion": "Facilita la ubicación de recursos relativos dentro del proyecto.",
            "feature": """  Scenario: E04 · Directorio del feature
    * def info = ⟦karate.info⟧
    * match info.featureDir == ⟦'#present'⟧
"""
        },
        {
            "id": "E05",
            "title": "Java Interop en Hook de Validación",
            "enunciado": "Usa Java.type para invocar una clase utilitaria del sistema (ej. java.lang.System).",
            "pista1": "Java.type('java.lang.System')",
            "pista2": "System.currentTimeMillis() devuelve un timestamp",
            "explicacion": "Karate permite enlazar bibliotecas de cifrado y cálculo actuarial directamente desde Java.",
            "feature": """  Scenario: E05 · Java Interop en ciclo de test
    * def System = ⟦Java.type('java.lang.System')⟧
    * def now = ⟦System.currentTimeMillis()⟧
    * match now == ⟦'#number'⟧
"""
        }
    ]
}

LECCION_L38 = {
    "code": "L38",
    "slug": "rendimiento-con-gatling",
    "url": "catalogUrl",
    "readme": """# L38 · Pruebas de Carga y Estrés con Karate Gatling

Un SDET moderno no reescribe los tests de API en otra herramienta para medir rendimiento: **reutiliza los mismos archivos .feature de Karate como escenarios de simulación de carga en Gatling**.

## La norma
- Clase Scala o Java que extiende `Simulation`.
- `karateFeature("classpath:flujo-pension.feature")`
- Inyección de carga: `rampUsers(50).during(Duration.ofSeconds(30))`.
- Métricas: Percentil 95 (p95 < 200ms), tasa de error (< 0.1%).
""",
    "ejemplo": """@L38 @ejemplo
Feature: L38 · Definición de SLA de Rendimiento

  Scenario: Comprobar tiempo de respuesta bajo SLA
    * def maxMs = 250
    * def latency = 120
    * assert latency < maxMs
""",
    "ejercicios": [
        {
            "id": "E01",
            "title": "Validación de SLA con responseTime",
            "enunciado": "Comprueba que la variable simulatedTime sea menor al umbral estipulado de 500 ms usando assert.",
            "pista1": "assert simulatedTime < 500",
            "pista2": "Usa una aserción booleana directa con assert",
            "explicacion": "assert evalúa expresiones JavaScript booleanas nativas.",
            "feature": """  Scenario: E01 · Validación de SLA
    * def simulatedTime = 180
    * ⟦assert simulatedTime < 500⟧
"""
        },
        {
            "id": "E02",
            "title": "Definir rampa de usuarios virtuales",
            "enunciado": "Modela el perfil de inyección de carga con 100 usuarios en 60 segundos.",
            "pista1": "Usa un objeto con users y duration",
            "pista2": "{ users: 100, durationSec: 60 }",
            "explicacion": "Simula la apertura de mercado bancario con llegada paulatina de clientes.",
            "feature": """  Scenario: E02 · Perfil de inyección
    * def profile = ⟦{ users: 100, durationSec: 60 }⟧
    * match profile.users == ⟦100⟧
    * match profile.durationSec == ⟦60⟧
"""
        },
        {
            "id": "E03",
            "title": "Umbral de percentil 95 (p95)",
            "enunciado": "Define el criterio de aceptación actuarial: p95 debe ser inferior o igual a 300 milisegundos con assert.",
            "pista1": "assert p95Threshold <= 300",
            "pista2": "Usa assert booleano",
            "explicacion": "El p95 asegura que el 95% de las transacciones son fluidas para los clientes.",
            "feature": """  Scenario: E03 · Umbral de percentil 95
    * def p95Threshold = ⟦300⟧
    * ⟦assert p95Threshold <= 300⟧
"""
        },
        {
            "id": "E04",
            "title": "Tasa máxima de error tolerada",
            "enunciado": "Verifica que la tasa de errores de la simulación sea inferior al 1% con assert.",
            "pista1": "assert errorRate < 0.01",
            "pista2": "Usa assert booleano",
            "explicacion": "Cero fallos HTTP 500 en transacciones financieras de traspaso de fondos.",
            "feature": """  Scenario: E04 · Tolerancia a fallos
    * def errorRate = ⟦0.005⟧
    * ⟦assert errorRate < 0.01⟧
"""
        },
        {
            "id": "E05",
            "title": "Reutilización de escenarios funcionales",
            "enunciado": "Indica el path del feature funcional que se reutiliza como prueba de carga.",
            "pista1": "classpath:lecciones/L01-primera-peticion/ejemplo.feature",
            "pista2": "Reutilización sin duplicidad de código",
            "explicacion": "El mayor superpoder de Karate-Gatling es no tener que mantener dos suites distintas.",
            "feature": """  Scenario: E05 · Ruta de feature reutilizado
    * def featurePath = ⟦'classpath:lecciones/L01-primera-peticion/ejemplo.feature'⟧
    * match featurePath == ⟦'#present'⟧
"""
        }
    ]
}

LECCION_L39 = {
    "code": "L39",
    "slug": "cicd-github-actions",
    "url": "catalogUrl",
    "readme": """# L39 · Integración Continua con GitHub Actions y Reportes HTML

Un framework de pruebas no aporta valor si no se ejecuta automáticamente en cada Pull Request. Un SDET Senior diseña el pipeline CI/CD, publica los reportes interactivos de Karate (`target/karate-reports`) y bloquea el merge si la regresión falla.

## La norma
- Workflow en `.github/workflows/karate-tests.yml`.
- Cache de dependencias de Maven (`~/.m2/repository`).
- Ejecución `mvn test -Dkarate.options="--tags @smoke"`.
- Publicación de artefactos HTML con `actions/upload-artifact`.
""",
    "ejemplo": """@L39 @ejemplo
Feature: L39 · Comprobación de configuración CI/CD

  Scenario: Pipeline status exitoso
    * def exitCode = 0
    * match exitCode == 0
""",
    "ejercicios": [
        {
            "id": "E01",
            "title": "Comando Maven de ejecución en pipeline",
            "enunciado": "Define el comando Maven estándar que ejecuta Karate filtrando por tags de regresión.",
            "pista1": "mvn test -Dkarate.options='--tags @smoke'",
            "pista2": "Usa mvn test",
            "explicacion": "El flag karate.options sobreescribe los tags desde la línea de comandos de CI.",
            "feature": """  Scenario: E01 · Comando de ejecución CI
    * def cmd = ⟦'mvn test -Dkarate.options="--tags @smoke"'⟧
    * match cmd contains ⟦'mvn test'⟧
"""
        },
        {
            "id": "E02",
            "title": "Ruta del reporte interactivo de Karate",
            "enunciado": "Especifica la ruta estándar donde Karate genera el resumen HTML de resultados.",
            "pista1": "target/karate-reports/karate-summary.html",
            "pista2": "target/karate-reports",
            "explicacion": "Este archivo HTML contiene el desglose visual de cada petición y aserción.",
            "feature": """  Scenario: E02 · Ruta del reporte HTML
    * def reportPath = ⟦'target/karate-reports/karate-summary.html'⟧
    * match reportPath contains ⟦'karate-summary.html'⟧
"""
        },
        {
            "id": "E03",
            "title": "Estrategia de cache de dependencias Maven",
            "enunciado": "Indica la clave de cache para acelerar los runners en GitHub Actions.",
            "pista1": "maven-deps-${{ hashFiles('**/pom.xml') }}",
            "pista2": "Basada en el hash de pom.xml",
            "explicacion": "Evita descargar Karate y dependencias Java en cada ejecución de PR.",
            "feature": """  Scenario: E03 · Cache key
    * def cacheKey = ⟦'maven-deps-pom-hash'⟧
    * match cacheKey == ⟦'maven-deps-pom-hash'⟧
"""
        },
        {
            "id": "E04",
            "title": "Política de fallo ante regresión rota",
            "enunciado": "Valida que el código de salida esperado para un fallo de test sea distinto de 0.",
            "pista1": "exitCode != 0",
            "pista2": "Bloquea el PR en GitHub",
            "explicacion": "El pipeline falla y protege la rama main contra regresiones.",
            "feature": """  Scenario: E04 · Regla de bloqueo de merge
    * def exitCode = 1
    * match ⟦exitCode != 0⟧ == true
"""
        },
        {
            "id": "E05",
            "title": "Generación de badge de cobertura",
            "enunciado": "Define el estado final de la suite para el badge del README.",
            "pista1": "status: 'PASSED'",
            "pista2": "Color verde en el dashboard",
            "explicacion": "Otorga visibilidad inmediata a los desarrolladores y stakeholders del banco.",
            "feature": """  Scenario: E05 · Badge de estado
    * def badge = ⟦{ status: 'PASSED', color: 'green' }⟧
    * match badge.status == ⟦'PASSED'⟧
    * match badge.color == ⟦'green'⟧
"""
        }
    ]
}

LECCIONES = [LECCION_L34, LECCION_L35, LECCION_L36, LECCION_L37, LECCION_L38, LECCION_L39]
