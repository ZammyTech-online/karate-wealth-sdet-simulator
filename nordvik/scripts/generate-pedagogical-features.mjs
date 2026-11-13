// scripts/generate-pedagogical-features.mjs: Generador industrial de 16 Features pedagógicas ejecutables de Karate DSL
import { writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const outDir = resolve(__dirname, '../features/pedagogicos');

if (!existsSync(outDir)) {
  mkdirSync(outDir, { recursive: true });
}

const features = [
  // ==========================================
  // JUNIOR SDET
  // ==========================================
  {
    id: 'J-A',
    filename: 'J-A-el-contrato-http.feature',
    title: 'El Contrato HTTP en Sistemas Bancarios',
    content: `@pedagogico @junior @funds
Feature: J-A: El Contrato HTTP en Sistemas Bancarios (GET Seguro vs Mutación)
  # [CONTEXTO ACTUARIAL]: En la gestora Nordvik, una consulta de catálogo jamás debe alterar
  # el patrimonio de un cliente. Los métodos GET son de solo lectura y seguros por contrato HTTP.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: [ARRANGE-ACT-ASSERT] Obtener catálogo público de fondos de inversión aprobados (HTTP 200)
    # [ARRANGE]: Definimos la ruta del recurso según la especificación OpenAPI
    Given path '/api/v1/funds'

    # [ACT]: Disparamos la petición HTTP de lectura
    When method get

    # [ASSERT]: Validamos el código de estado 200 y la integridad del catálogo
    Then status 200
    And match response.total == '#number'
    And match response.items == '#[]'
    And match response.items[0] contains { isin: '#string', name: '#string', nav: '#number', currency: 'SEK' }
`
  },
  {
    id: 'J-B',
    filename: 'J-B-inspeccion-json-cartera.feature',
    title: 'Inspección de JSON & Estructuras de Cartera',
    content: `@pedagogico @junior @accounts
Feature: J-B: Inspección de JSON & Estructuras de Cartera Multidivisa
  # [CONTEXTO ACTUARIAL]: Una cartera bancaria contiene cuentas en coronas (SEK) y euros (EUR).
  # No podemos hacer aserciones de texto plano porque los valores liquidativos (NAV) fluctúan a diario.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Validar cartera del cliente y posiciones mediante operador contains
    Given path '/api/v1/portfolios/P-100234'
    When method get
    Then status 200
    # Verificamos campos raíz sin importar el orden exacto
    And match response contains { portfolioId: 'P-100234', owner: '#string', status: 'ACTIVE' }
    # Verificamos que el array de posiciones contiene activos denominados en SEK
    And match response.holdings == '#[] #object'
    And match response.holdings contains deep { isin: 'SE0001', currency: 'SEK' }
`
  },
  {
    id: 'J-C',
    filename: 'J-C-validacion-fuzzy-kyc.feature',
    title: 'Validación de Tipos Fuzzy & Marcadores de Karate',
    content: `@pedagogico @junior @kyc
Feature: J-C: Validación de Tipos Fuzzy & Marcadores de Karate en Expedientes KYC
  # [CONTEXTO ACTUARIAL]: La regulación sueca exige validar la identidad nacional (Personnummer).
  # En pruebas no fijamos datos quemados (hardcodeados), sino patrones regulatorios (#regex).

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Verificar esquema del cliente con marcadores fuzzy (#string, #number, #regex)
    Given path '/api/v1/customers/C-9011'
    When method get
    Then status 200
    And match response ==
      """
      {
        id: '#string',
        fullName: '#string',
        personalNumber: '#regex ^(19|20)\\d{6}-\\d{4}$',
        riskCategory: '#? _ == "LOW" || _ == "MEDIUM" || _ == "HIGH"',
        kycVerified: '#boolean',
        tier: '#number'
      }
      """
`
  },
  {
    id: 'J-D',
    filename: 'J-D-parametros-filtro-mercado.feature',
    title: 'Parámetros de Consulta & Filtrado de Activos',
    content: `@pedagogico @junior @market
Feature: J-D: Filtrado de Activos Financieros por Perfil de Riesgo (Query Params)
  # [CONTEXTO ACTUARIAL]: La directiva MiFID II prohíbe ofrecer fondos de alto riesgo
  # a clientes con perfil conservador. El backend debe filtrar exactamente por query params.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Filtrar fondos de renta fija de bajo riesgo denominados en SEK
    Given path '/api/v1/funds'
    And param assetClass = 'FixedIncome'
    And param maxRisk = 2
    And param currency = 'SEK'
    When method get
    Then status 200
    And match each response.items contains { assetClass: 'FixedIncome', currency: 'SEK' }
    And match each response.items[*].riskGrade == '#? _ <= 2'
`
  },

  // ==========================================
  // MID SDET
  // ==========================================
  {
    id: 'M-A',
    filename: 'M-A-escenarios-parametrizados.feature',
    title: 'Escenarios Parametrizados (Scenario Outline & Examples)',
    content: `@pedagogico @mid @orders
Feature: M-A: Tablas de Decisión Parametrizadas para Órdenes de Suscripción
  # [CONTEXTO ACTUARIAL]: Las órdenes de inversión tienen diferentes reglas de validación
  # según el importe mínimo por divisa y el tipo de activo.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario Outline: Validar suscripción de fondos según divisa y monto mínimo
    Given path '/api/v1/orders/validate'
    And request { isin: '<isin>', amount: <amount>, currency: '<currency>' }
    When method post
    Then status <expectedStatus>
    And match response.valid == <isValid>

    Examples:
      | isin    | amount  | currency | expectedStatus | isValid |
      | SE0001  | 1000.0  | SEK      | 200            | true    |
      | SE0001  | 50.0    | SEK      | 422            | false   |
      | EU0092  | 500.0   | EUR      | 200            | true    |
      | EU0092  | 10.0    | EUR      | 422            | false   |
`
  },
  {
    id: 'M-B',
    filename: 'M-B-carga-modular-read.feature',
    title: 'Carga Modular de Payloads Externos (read)',
    content: `@pedagogico @mid @pensions
Feature: M-B: Carga Externa de Payloads Complejos de Pensiones ITP1
  # [CONTEXTO ACTUARIAL]: Un cálculo actuarial de pensión colectiva tiene más de 40 campos.
  # Quemar ese JSON dentro del test ensucia el código. Usamos read('...') para desacoplar.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'
    * def basePayload =
      """
      {
        planType: "ITP1",
        employee: {
          age: 42,
          monthlySalary: 52000,
          salaryCurrency: "SEK"
        },
        collectiveAgreement: "SAF-LO"
      }
      """

  Scenario: Calcular aportación patronal obligatoria desacoplando datos y aserciones
    Given path '/api/v1/pensions/calculate'
    And request basePayload
    When method post
    Then status 200
    And match response.mandatoryContribution == '#number'
    And match response.mandatoryContribution > 0
    And match response.pensionTier == 'TIER_1'
`
  },
  {
    id: 'M-C',
    filename: 'M-C-reutilizacion-karate-call.feature',
    title: 'Reutilización de Microservicios (karate.call)',
    content: `@pedagogico @mid @auth
Feature: M-C: Orquestación Modular con Invocación karate.call
  # [CONTEXTO ACTUARIAL]: No hacemos login manual en cada test. Invocamos un feature auxiliar
  # una sola vez (karate.callSingle) para inyectar el Bearer Token en la cabecera.

  Background:
    * url 'http://localhost:5125'
    # Simulamos el token obtenido mediante karate.call
    * def authResult = { token: 'eyJhGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.nordvik.test' }
    * header Authorization = 'Bearer ' + authResult.token
    * header Accept = 'application/json'

  Scenario: Consultar movimientos restringidos con sesión corporativa autenticada
    Given path '/api/v1/accounts/ACC-7721/transactions'
    When method get
    Then status 200
    And match response.accountNumber == 'ACC-7721'
    And match response.transactions == '#[]'
`
  },
  {
    id: 'M-D',
    filename: 'M-D-manipulacion-js-fx.feature',
    title: 'Manipulación de Respuestas con JavaScript Embebido',
    content: `@pedagogico @mid @fx
Feature: M-D: Manipulación de Tasas de Cambio FX con JavaScript Embebido (GraalVM)
  # [CONTEXTO ACTUARIAL]: Para liquidar una orden internacional SEK/EUR, Karate debe
  # calcular en memoria si la conversión aplicada coincide con el spread actuarial pactado.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Filtrar tasas activas y calcular contravalor en SEK con función JS
    Given path '/api/v1/fx/rates'
    When method get
    Then status 200
    # Función JS pura ejecutada en el motor GraalVM de Karate
    * def filterEur = function(x){ return x.pair == 'EURSEK' }
    * def rate = karate.filter(response.rates, filterEur)[0].midRate
    * def amountEur = 1000
    * def expectedSek = amountEur * rate
    * match expectedSek > 10000
`
  },

  // ==========================================
  // SENIOR SDET
  // ==========================================
  {
    id: 'S-A',
    filename: 'S-A-asincronia-polling-retry.feature',
    title: 'Asincronía & Polling sin Sleep (retry until)',
    content: `@pedagogico @senior @orders @async
Feature: S-A: Sondeo Inteligente sin Sleep para Órdenes Asíncronas (retry until)
  # [CONTEXTO ACTUARIAL]: La compensación de órdenes en bolsa toma entre 2 y 5 segundos.
  # Usar Thread.sleep() hace que los tests sean lentos y frágiles. Usamos 'retry until'.

  Background:
    * url 'http://localhost:5125'
    * configure retry = { count: 10, interval: 500 }
    * header Accept = 'application/json'

  Scenario: Crear orden de compra de participaciones y esperar confirmación del custodio
    # Paso 1: Enviamos la orden de compra
    Given path '/api/v1/orders'
    And request { isin: 'SE0001', units: 25.5, accountId: 'ACC-8801' }
    When method post
    Then status 201
    * def orderId = response.orderId

    # Paso 2: Hacemos sondeo no bloqueante hasta que el custodio liquide la orden
    Given path '/api/v1/orders/' + orderId
    And retry until response.status == 'SETTLED'
    When method get
    Then status 200
    And match response.settledUnits == 25.5
`
  },
  {
    id: 'S-B',
    filename: 'S-B-sagas-pensiones-compensacion.feature',
    title: 'Sagas Distribuidas de Pensiones & Compensación',
    content: `@pedagogico @senior @pensions @saga
Feature: S-B: Verificación de Transacción de Compensación en Saga de Traspaso de Plan
  # [CONTEXTO ACTUARIAL]: En un traspaso de pensión entre entidades (Flytträtt), si la entidad
  # receptora rechaza el alta, el dinero debitado debe reembolsarse inmediatamente a la cuenta de origen.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario: Provocar rechazo en paso 3 y certificar que la saga ejecuta el reembolso (Compensación)
    Given path '/api/v1/pensions/transfer-saga'
    # Forzamos un rechazo regulatorio con un PNR simulado con alerta de embargo
    And request
      """
      {
        sourceAccountId: "ACC-5001",
        targetInstitution: "BLOCKED_INSTITUTION",
        amount: 150000,
        personalNumber: "19800101-9999"
      }
      """
    When method post
    Then status 409
    And match response.sagaStatus == 'COMPENSATED'
    And match response.compensationAction == 'FUNDS_REFUNDED_TO_SOURCE'
    And match response.sourceBalanceRestored == true
`
  },
  {
    id: 'S-C',
    filename: 'S-C-idempotencia-doble-cobro.feature',
    title: 'Idempotencia & Prevención de Doble Cobro',
    content: `@pedagogico @senior @payments @idempotency
Feature: S-C: Garantía de Idempotencia en Pagos con Idempotency-Key
  # [CONTEXTO ACTUARIAL]: Si el cliente pulsa dos veces "Transferir" o hay un corte de red,
  # el servidor debe procesar el pago exactamente una sola vez gracias a la clave de idempotencia.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'
    * def uniqueKey = 'IDEMP-' + java.util.UUID.randomUUID()
    * def paymentPayload = { fromAccount: 'ACC-1100', toAccount: 'ACC-2200', amount: 45000, currency: 'SEK' }

  Scenario: Emitir la misma orden dos veces con el mismo Idempotency-Key y evitar doble cargo
    # Primer intento: Petición original (Crea el débito)
    Given path '/api/v1/payments'
    And header Idempotency-Key = uniqueKey
    And request paymentPayload
    When method post
    Then status 201
    * def paymentId = response.paymentId

    # Segundo intento inmediato: Reintento de red con la misma clave
    Given path '/api/v1/payments'
    And header Idempotency-Key = uniqueKey
    And request paymentPayload
    When method post
    # El servidor debe reconocer la clave previa y devolver el mismo recurso sin duplicar el cobro
    Then status 200
    And match response.paymentId == paymentId
    And match response.replayed == true
`
  },
  {
    id: 'S-D',
    filename: 'S-D-contratos-error-rfc9457.feature',
    title: 'Contratos de Error RFC 9457 & Seguridad IDOR',
    content: `@pedagogico @senior @security @rfc9457
Feature: S-D: Conformidad con RFC 9457 (Problem Details) y Prevención IDOR
  # [CONTEXTO ACTUARIAL]: Si un atacante intenta acceder al extracto fiscal de otro cliente
  # alterando el ID en la URL, el sistema debe responder HTTP 403 con esquema estándar RFC 9457.

  Background:
    * url 'http://localhost:5125'
    * header Authorization = 'Bearer TOKEN_USER_A'
    * header Accept = 'application/problem+json'

  Scenario: Intentar acceder a la declaración fiscal del Cliente B siendo el Cliente A
    Given path '/api/v1/customers/C-USER-B/tax-statement'
    When method get
    Then status 403
    # Verificamos que la respuesta cumple estrictamente el estándar RFC 9457
    And match response ==
      """
      {
        type: '#regex ^https?://.*',
        title: 'Forbidden',
        status: 403,
        detail: '#string',
        instance: '#string',
        code: 'ACCESS_DENIED_IDOR'
      }
      """
`
  },

  // ==========================================
  // STAFF / LEAD SDET
  // ==========================================
  {
    id: 'T-A',
    filename: 'T-A-mocks-memoria-karate-start.feature',
    title: 'Mocks Dinámicos en Memoria (karate.start)',
    content: `@pedagogico @staff @mock
Feature: T-A: Servidores Mock en Memoria para Simulación de Custodios Internacionales
  # [CONTEXTO ACTUARIAL]: Las pruebas no pueden depender de la disponibilidad de SWIFT o Euroclear.
  # Un Staff SDET levanta un microservicio Mock HTTP en memoria en el puerto dinámico de Karate.

  Background:
    # Levantamos un mock HTTP que responderá a la liquidación de la orden
    * def port = karate.env == 'ci' ? 8999 : 8998
    * def mockServer = karate.start({ mock: 'classpath:mocks/custodian-mock.feature', port: port })
    * url 'http://localhost:' + port

  Scenario: Validar intercambio de mensajes MT541 con custodio simulado
    Given path '/swift/settlement'
    And request { instructionType: 'MT541', isin: 'SE0001', volume: 1000 }
    When method post
    Then status 200
    And match response.acknowledgement == 'ACK_MT541_PROCESSED'
    # Detenemos el servidor mock de forma limpia
    * mockServer.stop()
`
  },
  {
    id: 'T-B',
    filename: 'T-B-inyeccion-caos-circuit-breaker.feature',
    title: 'Inyección de Caos & Circuit Breakers',
    content: `@pedagogico @staff @chaos @resilience
Feature: T-B: Inyección de Caos en el Proveedor de Precios y Activación de Circuit Breaker
  # [CONTEXTO ACTUARIAL]: Si el feed de precios de la Bolsa de Estocolmo (OMX) sufre un 50% de latencia,
  # el Gateway debe abrir el Circuit Breaker y responder con el precio del último cierre sin caerse.

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Inyectar latencia de 3000ms y verificar fallback a precio de cierre anterior
    # Activamos caos en el microservicio de precios
    Given path '/api/v1/chaos/latency'
    And request { targetService: 'price-feed', latencyMs: 3000, enabled: true }
    When method post
    Then status 200

    # Petición del cliente: El circuit breaker debe activarse ante timeout y devolver fallback
    Given path '/api/v1/market/prices/SE0001'
    When method get
    Then status 200
    And match response.source == 'CIRCUIT_BREAKER_FALLBACK'
    And match response.priceType == 'LAST_KNOWN_CLOSE'

    # Restauramos el estado del cluster
    Given path '/api/v1/chaos/latency'
    And request { targetService: 'price-feed', enabled: false }
    When method post
    Then status 200
`
  },
  {
    id: 'T-C',
    filename: 'T-C-autorizacion-rbac-aislamiento.feature',
    title: 'Autorización Fina RBAC & Aislamiento de Perfiles',
    content: `@pedagogico @staff @security @rbac
Feature: T-C: Matriz de Control de Acceso Basado en Roles (RBAC) para Asesores y Clientes
  # [CONTEXTO ACTUARIAL]: La Ley de Distribución de Seguros (IDD) prohíbe que un asesor
  # modifique la cláusula de beneficiarios de un seguro de vida sin la firma expresa del tomador.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario Outline: Validar matriz de permisos sobre modificación de póliza de seguro
    Given path '/api/v1/insurance/POL-992/beneficiary'
    And header Authorization = 'Bearer ' + '<tokenRole>'
    And request { primaryBeneficiary: 'Carl von Linne', percentage: 100 }
    When method put
    Then status <expectedStatus>

    Examples:
      | tokenRole       | expectedStatus |
      | POLICY_HOLDER   | 200            |
      | JUNIOR_ADVISOR  | 403            |
      | AUDITOR_READ    | 403            |
      | CHIEF_ACTUARY   | 200            |
`
  },
  {
    id: 'T-D',
    filename: 'T-D-manipulacion-temporal-reloj.feature',
    title: 'Manipulación Temporal & Devengo de Comisiones',
    content: `@pedagogico @staff @time @audit
Feature: T-D: Aceleración Temporal con Reloj Virtual para Cierres Contables y Devengos
  # [CONTEXTO ACTUARIAL]: El cálculo del impuesto sueco sobre cuentas ISK ocurre cada trimestre.
  # En lugar de esperar 90 días, adelantamos el reloj virtual de Nordvik para verificar el cargo fiscal.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario: Adelantar el reloj al 31 de diciembre y certificar el devengo fiscal automático
    # Paso 1: Consultamos la fecha actual del reloj virtual
    Given path '/api/v1/clock'
    When method get
    Then status 200
    * def originalDate = response.virtualTime

    # Paso 2: Adelantamos el reloj al cierre fiscal (31 de diciembre)
    Given path '/api/v1/clock/fast-forward'
    And request { days: 90, targetEvent: 'YEAR_END_TAX_CLOSE' }
    When method post
    Then status 200
    And match response.virtualTime contains '12-31'

    # Paso 3: Verificamos que el motor fiscal calculó el devengo del impuesto ISK
    Given path '/api/v1/accounts/ACC-ISK-100/tax-accruals'
    When method get
    Then status 200
    And match response.iskTaxAmount == '#number'
    And match response.taxYear == 2026
    And match response.status == 'ACCRUED'
`
  }
];

console.log(`Generando ${features.length} archivos .feature pedagógicos en ${outDir}...`);
for (const f of features) {
  const targetPath = resolve(outDir, f.filename);
  writeFileSync(targetPath, f.content.trim() + '\n', 'utf-8');
  console.log(`  ✓ Creado ${f.filename} (${f.title})`);
}

console.log(`\n¡Éxito! 16 archivos .feature generados y listos para ejecución local.`);
