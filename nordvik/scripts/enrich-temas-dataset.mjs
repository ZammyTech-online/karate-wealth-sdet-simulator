// scripts/enrich-temas-dataset.mjs: Enriquece temas.json con diagramas de secuencia, microservicios y broken tests
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const temasPath = resolve(__dirname, '../torre/web/temas.json');

const data = JSON.parse(readFileSync(temasPath, 'utf-8'));

// Diccionario de enriquecimiento arquitectónico y de depuración para cada uno de los 16 temas
const enrichments = {
  'J-A': {
    microservicios: ['api-gateway', 'product-catalog', 'market-data'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test (SDET)
  participant GW as API Gateway (6080)
  participant Cat as Product Catalog (6801)
  participant DB as Catalog Store

  Test->>GW: GET /api/v1/funds (Accept: application/json)
  GW->>Cat: Proxy Request (Inmutable)
  Cat->>DB: Query Approved Funds (Read-Only)
  DB-->>Cat: List<FundEntity> (4 items)
  Cat-->>GW: HTTP 200 OK + JSON Catalog
  GW-->>Test: HTTP 200 OK (Safe & Idempotent)`,
    brokenTest: {
      title: 'Bug J-A: Verbo Mutador en Consulta y Aserción Prematura',
      badScenario: `Feature: Consulta de Fondos con Error
  Scenario: Consultar catálogo de fondos de forma insegura
    Given url 'http://localhost:5125'
    And path '/api/v1/funds'
    # ERROR 1: Aserción colocada antes del disparo HTTP
    And match response.total == 4
    # ERROR 2: Se usa POST en lugar de GET para una lectura
    When method post
    Then status 200`,
      hint: 'Mueve el match después del status y cambia el método a get.',
      fixTarget: 'When method get',
      validSolution: `Feature: Consulta de Fondos Corregida
  Scenario: Consultar catálogo de fondos de forma segura
    Given url 'http://localhost:5125'
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match response.total == 4`
    }
  },
  'J-B': {
    microservicios: ['api-gateway', 'portfolio-service', 'account-registry'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant GW as API Gateway
  participant Port as Portfolio Service (6902)
  participant Acc as Account Registry

  Test->>GW: GET /api/v1/portfolios/P-100234
  GW->>Port: Get Holdings & Balances
  Port->>Acc: Fetch Multi-Currency Accounts (SEK/EUR)
  Acc-->>Port: Accounts Data
  Port-->>GW: Portfolio View with Dynamic NAV
  GW-->>Test: HTTP 200 OK + Nested JSON Tree`,
    brokenTest: {
      title: 'Bug J-B: Aserción Frágil por Comparación de Cadena Exacta',
      badScenario: `Feature: Cartera Multidivisa
  Scenario: Validar cartera mediante string estático
    Given url 'http://localhost:5125'
    And path '/api/v1/portfolios/P-100234'
    When method get
    Then status 200
    # ERROR: Comparar JSON con igualdad estricta de string falla al fluctuar el NAV
    And match response == '{"portfolioId":"P-100234","nav":145.2}'`,
      hint: 'Usa el operador "contains" o marcadores "#object" en vez de string idéntico.',
      fixTarget: 'contains',
      validSolution: `Feature: Cartera Multidivisa Corregida
  Scenario: Validar cartera mediante contains
    Given url 'http://localhost:5125'
    And path '/api/v1/portfolios/P-100234'
    When method get
    Then status 200
    And match response contains { portfolioId: 'P-100234', status: 'ACTIVE' }`
    }
  },
  'J-C': {
    microservicios: ['api-gateway', 'customer-registry', 'synthetic-person-service'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant GW as API Gateway
  participant Cust as Customer Registry (6301)
  participant Synth as Person Verifier (6008)

  Test->>GW: GET /api/v1/customers/C-9011
  GW->>Cust: Fetch KYC Profile
  Cust->>Synth: Validate Swedish Personnummer (Luhn Check)
  Synth-->>Cust: Valid Personal ID
  Cust-->>GW: Customer Entity with KYC State
  GW-->>Test: HTTP 200 + Schema compliant JSON`,
    brokenTest: {
      title: 'Bug J-C: Número de Identidad Personal (PNR) Hardcodeado',
      badScenario: `Feature: KYC Customer
  Scenario: Validar expediente KYC con valor quemado
    Given url 'http://localhost:5125'
    And path '/api/v1/customers/C-9011'
    When method get
    Then status 200
    # ERROR: PNR hardcodeado que se rompe en entornos de pruebas rotativos
    And match response.personalNumber == '19850504-2384'`,
      hint: 'Reemplaza el PNR fijo por el marcador fuzzy de expresión regular #regex.',
      fixTarget: '#regex',
      validSolution: `Feature: KYC Customer Corregido
  Scenario: Validar expediente KYC con fuzzy regex
    Given url 'http://localhost:5125'
    And path '/api/v1/customers/C-9011'
    When method get
    Then status 200
    And match response.personalNumber == '#regex ^(19|20)\\\\d{6}-\\\\d{4}$'`
    }
  },
  'J-D': {
    microservicios: ['api-gateway', 'market-data', 'fund-factsheet'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant GW as API Gateway
  participant Mkt as Market Data (6802)
  participant Fact as Fund Factsheet (6805)

  Test->>GW: GET /api/v1/funds?assetClass=FixedIncome&maxRisk=2
  GW->>Mkt: Filter Request with Query Params
  Mkt->>Fact: Verify Risk Classification
  Fact-->>Mkt: Filtered Assets
  Mkt-->>GW: Filtered Collection (Risk <= 2)
  GW-->>Test: HTTP 200 + Items array`,
    brokenTest: {
      title: 'Bug J-D: Parámetros de Consulta Concatenados Manualmente',
      badScenario: `Feature: Filtro de Mercado
  Scenario: Filtrar fondos concatenando query string a mano
    Given url 'http://localhost:5125'
    # ERROR: Concatenar parámetros en la URL rompe la codificación de caracteres y la legibilidad
    And path '/api/v1/funds?assetClass=FixedIncome&maxRisk=2'
    When method get
    Then status 200`,
      hint: 'Usa la palabra clave nativa "param" de Karate para cada parámetro.',
      fixTarget: 'param assetClass',
      validSolution: `Feature: Filtro de Mercado Corregido
  Scenario: Filtrar fondos con directiva param
    Given url 'http://localhost:5125'
    And path '/api/v1/funds'
    And param assetClass = 'FixedIncome'
    And param maxRisk = 2
    When method get
    Then status 200`
    }
  },

  // MID SDET
  'M-A': {
    microservicios: ['api-gateway', 'order-management', 'validation-engine'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Scenario Outline
  participant GW as API Gateway
  participant Ord as Order Management (6901)
  participant Val as Validation Engine

  loop Por cada fila en Examples
    Test->>GW: POST /api/v1/orders/validate (amount, currency)
    GW->>Ord: Route Validation
    Ord->>Val: Check Min/Max Limits per Currency
    Val-->>Ord: Valid/Invalid Flag
    Ord-->>GW: Result Status (200 / 422)
    GW-->>Test: HTTP Response matched against Examples
  end`,
    brokenTest: {
      title: 'Bug M-A: Scenario Outline sin Tabla Examples o Variables Desalineadas',
      badScenario: `Feature: Validación de Órdenes
  Scenario Outline: Validar órdenes con tabla rota
    Given url 'http://localhost:5125'
    And path '/api/v1/orders/validate'
    And request { isin: '<isin>', amount: <monto> }
    When method post
    # ERROR: Falta Then status antes del match y la cabecera Examples no coincide
    And match response.valid == true

    Examples:
      | isin   | amount |
      | SE0001 | 1000.0 |`,
      hint: 'Agrega "Then status <expectedStatus>" y alinea el nombre de la columna <amount>.',
      fixTarget: 'Then status',
      validSolution: `Feature: Validación de Órdenes Corregida
  Scenario Outline: Validar órdenes con tabla
    Given url 'http://localhost:5125'
    And path '/api/v1/orders/validate'
    And request { isin: '<isin>', amount: <amount> }
    When method post
    Then status 200
    And match response.valid == true

    Examples:
      | isin   | amount |
      | SE0001 | 1000.0 |`
    }
  },
  'M-B': {
    microservicios: ['api-gateway', 'collective-agreement-rules', 'calculation-service'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant FS as File System (read('...'))
  participant GW as API Gateway
  participant Pen as Collective Rules (7101)

  Test->>FS: read('classpath:data/pension-itp1.json')
  FS-->>Test: Raw JSON Payload
  Test->>GW: POST /api/v1/pensions/calculate (Payload)
  GW->>Pen: Execute Actuarial Formula (SAF-LO / ITP1)
  Pen-->>GW: Mandatory Contribution Breakdown
  GW-->>Test: HTTP 200 + Calculated Tier`,
    brokenTest: {
      title: 'Bug M-B: Inyección de JSON como String con Comillas Escapadas',
      badScenario: `Feature: Cálculo de Pensión
  Scenario: Enviar payload sucio con strings escapados
    Given url 'http://localhost:5125'
    And path '/api/v1/pensions/calculate'
    # ERROR: Pasar JSON serializado como string escapado causa HTTP 400 Bad Request
    And request "{\\"planType\\":\\"ITP1\\",\\"salary\\":52000}"
    When method post
    Then status 200`,
      hint: 'Usa bloques de triples comillas (""") o define una variable def con JSON nativo.',
      fixTarget: 'def basePayload',
      validSolution: `Feature: Cálculo de Pensión Corregido
  Scenario: Enviar payload JSON limpio
    Given url 'http://localhost:5125'
    And path '/api/v1/pensions/calculate'
    And request { planType: 'ITP1', salary: 52000 }
    When method post
    Then status 200`
    }
  },
  'M-C': {
    microservicios: ['api-gateway', 'iam-service', 'account-service'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Main Feature
  participant Sub as Sub-Feature (auth.feature)
  participant IAM as IAM Service (6201)
  participant Acc as Account Service (6902)

  Test->>Sub: karate.call('classpath:auth.feature')
  Sub->>IAM: POST /oauth/token (credentials)
  IAM-->>Sub: Bearer JWT Token
  Sub-->>Test: { token: 'eyJ...' }
  Test->>Acc: GET /api/v1/accounts/ACC-7721 (Authorization: Bearer)
  Acc-->>Test: HTTP 200 + Account Statement`,
    brokenTest: {
      title: 'Bug M-C: Login Repetido en Cada Petición sin Reutilización',
      badScenario: `Feature: Consulta de Cuentas
  Scenario: Iniciar sesión manualmente antes de cada consulta
    Given url 'http://localhost:5125'
    # ERROR: Login directo quemado en el escenario en lugar de reutilizar token
    And path '/api/v1/login'
    And request { user: 'admin', pass: 'secret' }
    When method post
    * def token = response.token
    Given path '/api/v1/accounts/ACC-7721'
    When method get
    Then status 200`,
      hint: 'Separa la autenticación en un Background o reutiliza la cabecera Authorization.',
      fixTarget: 'header Authorization',
      validSolution: `Feature: Consulta de Cuentas Corregida
  Background:
    * def token = 'MOCK_TOKEN_7721'
    * header Authorization = 'Bearer ' + token

  Scenario: Consultar cuenta autenticada
    Given url 'http://localhost:5125'
    And path '/api/v1/accounts/ACC-7721'
    When method get
    Then status 200`
    }
  },
  'M-D': {
    microservicios: ['api-gateway', 'fx-rates', 'pricing-calculator'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant GW as API Gateway
  participant FX as FX Rates Service (6803)
  participant JVM as GraalVM JavaScript Engine

  Test->>GW: GET /api/v1/fx/rates
  GW->>FX: Query Real-time Rates
  FX-->>GW: Collection of Currency Pairs
  GW-->>Test: HTTP 200 + Rates Array
  Test->>JVM: Execute .filter(r => r.pair == 'EURSEK')
  JVM-->>Test: Mid-Market Exchange Rate
  Test->>Test: Assert expectedSek > threshold`,
    brokenTest: {
      title: 'Bug M-D: Comparación de Tipos Incompatibles en JavaScript',
      badScenario: `Feature: FX Rates Conversion
  Scenario: Comparar número contra string en Karate
    Given url 'http://localhost:5125'
    And path '/api/v1/fx/rates'
    When method get
    Then status 200
    * def rate = response.rates[0].midRate
    # ERROR: Comparar un número de tipo float con una cadena causa discrepancia en GraalVM
    And match rate == '11.45'`,
      hint: 'Usa comparación numérica (#number o valor float sin comillas).',
      fixTarget: '#number',
      validSolution: `Feature: FX Rates Conversion Corregida
  Scenario: Comparar tipo numérico
    Given url 'http://localhost:5125'
    And path '/api/v1/fx/rates'
    When method get
    Then status 200
    * def rate = response.rates[0].midRate
    And match rate == '#number'`
    }
  },

  // SENIOR SDET
  'S-A': {
    microservicios: ['api-gateway', 'order-management', 'custodian-sim'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant GW as API Gateway
  participant Ord as Order Management (6901)
  participant Cust as Custodian Broker

  Test->>GW: POST /api/v1/orders (Create Order)
  GW->>Ord: Enqueue Order
  Ord-->>Test: HTTP 201 Created (orderId, status: 'PENDING')
  
  loop Polling Inteligente (retry until status == 'SETTLED')
    Test->>GW: GET /api/v1/orders/{orderId}
    GW->>Ord: Check Settlement State
    Ord-->>Test: status: 'PROCESSING'
  end
  Ord->>Cust: Confirm Stock Clearing
  Cust-->>Ord: Clearing Done
  Test->>GW: GET /api/v1/orders/{orderId}
  GW-->>Test: HTTP 200 OK (status: 'SETTLED')`,
    brokenTest: {
      title: 'Bug S-A: Uso del Anti-Patrón java.lang.Thread.sleep()',
      badScenario: `Feature: Liquidación Asíncrona
  Scenario: Esperar liquidación con pausa estática
    Given url 'http://localhost:5125'
    And path '/api/v1/orders'
    And request { isin: 'SE0001', units: 10 }
    When method post
    Then status 201
    * def id = response.orderId
    # ERROR GRAVE: Pausa fija de 5 segundos que ralentiza el pipeline
    * java.lang.Thread.sleep(5000)
    Given path '/api/v1/orders/' + id
    When method get
    Then status 200`,
      hint: 'Reemplaza sleep con la directiva nativa "retry until response.status == \'SETTLED\'".',
      fixTarget: 'retry until',
      validSolution: `Feature: Liquidación Asíncrona Corregida
  Scenario: Esperar liquidación con retry until
    Given url 'http://localhost:5125'
    * configure retry = { count: 10, interval: 500 }
    And path '/api/v1/orders'
    And request { isin: 'SE0001', units: 10 }
    When method post
    Then status 201
    * def id = response.orderId
    Given path '/api/v1/orders/' + id
    And retry until response.status == 'SETTLED'
    When method get
    Then status 200`
    }
  },
  'S-B': {
    microservicios: ['api-gateway', 'pension-transfer-orchestrator', 'source-bank', 'target-bank'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant Orch as Saga Orchestrator
  participant Src as Source Bank (Debit)
  participant Tgt as Target Institution (Credit)

  Test->>Orch: POST /api/v1/pensions/transfer-saga
  Orch->>Src: Step 1: Debit Accumulated Capital (150,000 SEK)
  Src-->>Orch: Debit OK
  Orch->>Tgt: Step 2: Register Policy at Target
  Tgt-->>Orch: REJECTED (Target Institution Blocked)
  Note over Orch,Src: Disparo de Compensación Automática
  Orch->>Src: Compensation: Refund 150,000 SEK to Source Account
  Src-->>Orch: Balance Restored
  Orch-->>Test: HTTP 409 Conflict + { sagaStatus: 'COMPENSATED' }`,
    brokenTest: {
      title: 'Bug S-B: Ignorar el Código de Estado de Compensación de la Saga',
      badScenario: `Feature: Saga de Pensiones
  Scenario: Validar compensación esperando HTTP 200
    Given url 'http://localhost:5125'
    And path '/api/v1/pensions/transfer-saga'
    And request { targetInstitution: 'BLOCKED' }
    When method post
    # ERROR: Una compensación ante fallo debe responder HTTP 409 Conflict o 422, nunca 200
    Then status 200
    And match response.sagaStatus == 'COMPENSATED'`,
      hint: 'Cambia Then status 200 a Then status 409.',
      fixTarget: 'status 409',
      validSolution: `Feature: Saga de Pensiones Corregida
  Scenario: Validar compensación con status 409
    Given url 'http://localhost:5125'
    And path '/api/v1/pensions/transfer-saga'
    And request { targetInstitution: 'BLOCKED' }
    When method post
    Then status 409
    And match response.sagaStatus == 'COMPENSATED'`
    }
  },
  'S-C': {
    microservicios: ['api-gateway', 'idempotency-store', 'cash-management', 'ledger-service'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Karate Test
  participant GW as API Gateway
  participant Idemp as Idempotency Store (6102)
  participant Cash as Cash Management (7002)

  Test->>GW: POST /api/v1/payments (Idempotency-Key: K-991)
  GW->>Idemp: Check Key K-991
  Idemp-->>GW: Key Not Found -> Lock Acquired
  GW->>Cash: Execute Transfer
  Cash-->>GW: Payment Created (PAY-001)
  GW->>Idemp: Save Response Cached
  GW-->>Test: HTTP 201 Created

  Note over Test,GW: Reintento de Red con la Misma Clave
  Test->>GW: POST /api/v1/payments (Idempotency-Key: K-991)
  GW->>Idemp: Check Key K-991
  Idemp-->>GW: Key Exists -> Return Cached Response
  GW-->>Test: HTTP 200/201 (replayed: true, Cero Doble Débito)`,
    brokenTest: {
      title: 'Bug S-C: Omisión de la Cabecera Idempotency-Key',
      badScenario: `Feature: Pago Idempotente
  Scenario: Enviar reintento sin Idempotency-Key
    Given url 'http://localhost:5125'
    And path '/api/v1/payments'
    # ERROR: Sin la cabecera Idempotency-Key el servidor procesará dos transferencias
    And request { from: 'ACC-1', to: 'ACC-2', amount: 5000 }
    When method post
    Then status 201`,
      hint: 'Agrega "And header Idempotency-Key = uniqueKey" para asegurar idempotencia.',
      fixTarget: 'header Idempotency-Key',
      validSolution: `Feature: Pago Idempotente Corregido
  Scenario: Enviar pago con Idempotency-Key
    Given url 'http://localhost:5125'
    And path '/api/v1/payments'
    And header Idempotency-Key = 'IDEMP-TEST-9921'
    And request { from: 'ACC-1', to: 'ACC-2', amount: 5000 }
    When method post
    Then status 201`
    }
  },
  'S-D': {
    microservicios: ['api-gateway', 'customer-registry', 'audit-logger'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Attacker as Test (User A Token)
  participant GW as API Gateway
  participant Cust as Customer Registry (6301)
  participant Audit as Security Audit Log

  Attacker->>GW: GET /api/v1/customers/USER-B/tax-statement
  GW->>Cust: Forward Token & Route
  Cust->>Cust: Compare Token Identity with Target URL ID
  Note over Cust: IDOR Vulnerability Prevented
  Cust->>Audit: Log Security Violation (ACCESS_DENIED_IDOR)
  Cust-->>GW: RFC 9457 Problem Details (403 Forbidden)
  GW-->>Attacker: HTTP 403 (type, title, status, detail)`,
    brokenTest: {
      title: 'Bug S-D: Aserción Incompleta del Estándar RFC 9457',
      badScenario: `Feature: Seguridad IDOR
  Scenario: Validar error sin verificar esquema RFC 9457
    Given url 'http://localhost:5125'
    And path '/api/v1/customers/USER-B/tax-statement'
    When method get
    Then status 403
    # ERROR: Validar solo el texto simple ignora los campos obligatorios del RFC 9457
    And match response == 'Error 403'`,
      hint: 'Verifica los campos estándar de Problem Details: type, title, status, detail.',
      fixTarget: 'title: \'Forbidden\'',
      validSolution: `Feature: Seguridad IDOR Corregida
  Scenario: Validar esquema RFC 9457
    Given url 'http://localhost:5125'
    And path '/api/v1/customers/USER-B/tax-statement'
    When method get
    Then status 403
    And match response contains { title: 'Forbidden', status: 403, code: 'ACCESS_DENIED_IDOR' }`
    }
  },

  // STAFF SDET
  'T-A': {
    microservicios: ['karate-mock-server', 'order-management', 'swift-adapter'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Staff SDET Suite
  participant Mock as karate.start({ mock: 'custodian.feature' })
  participant App as Order Management

  Test->>Mock: Start In-Memory Mock Server on Dynamic Port
  Mock-->>Test: Mock Listening on localhost:8998
  Test->>App: Trigger Settlement against Mock URL
  App->>Mock: POST /swift/settlement (MT541)
  Mock-->>App: HTTP 200 (ACK_MT541_PROCESSED)
  App-->>Test: Order Settled
  Test->>Mock: mockServer.stop()`,
    brokenTest: {
      title: 'Bug T-A: Puerto Fijo en Servidor Mock que Provoca BindException',
      badScenario: `Feature: Mock en Memoria
  Scenario: Iniciar mock en puerto 80 quemado
    # ERROR: Intentar vincular puerto 80 o fijo causa java.net.BindException en CI/CD
    * def server = karate.start({ mock: 'mock.feature', port: 80 })
    Given url 'http://localhost:80'
    When method get
    Then status 200`,
      hint: 'Usa un puerto de pruebas no privilegiado o variable dinámica.',
      fixTarget: 'port: 8998',
      validSolution: `Feature: Mock en Memoria Corregido
  Scenario: Iniciar mock en puerto dinámico
    * def server = karate.start({ mock: 'mock.feature', port: 8998 })
    Given url 'http://localhost:8998'
    When method get
    Then status 200
    * server.stop()`
    }
  },
  'T-B': {
    microservicios: ['api-gateway', 'chaos-control', 'price-feed-adapter'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Staff SDET
  participant Chaos as Chaos Control (7601)
  participant GW as API Gateway (Resilience4j)
  participant Feed as Price Feed Adapter (7503)

  Test->>Chaos: POST /chaos/latency { target: 'price-feed', latencyMs: 3000 }
  Chaos-->>Test: Latency Injected
  Test->>GW: GET /api/v1/market/prices/SE0001
  GW->>Feed: Call Price Feed (Timeout 1000ms)
  Feed-->>GW: [Delayed Response...]
  Note over GW: Circuit Breaker OPEN: Fallback Triggered
  GW-->>Test: HTTP 200 (source: 'CIRCUIT_BREAKER_FALLBACK', price: LastKnownClose)`,
    brokenTest: {
      title: 'Bug T-B: Olvidar Restaurar el Estado del Clúster tras la Prueba de Caos',
      badScenario: `Feature: Prueba de Caos
  Scenario: Activar latencia sin limpiar estado
    Given url 'http://localhost:5125'
    And path '/api/v1/chaos/latency'
    And request { target: 'price-feed', latencyMs: 3000, enabled: true }
    When method post
    Then status 200
    # ERROR: Dejar el caos encendido contamina las pruebas de los siguientes tests de la suite`,
      hint: 'Añade un bloque de limpieza enviando enabled: false para restaurar el clúster.',
      fixTarget: 'enabled: false',
      validSolution: `Feature: Prueba de Caos Corregida
  Scenario: Activar y limpiar caos
    Given url 'http://localhost:5125'
    And path '/api/v1/chaos/latency'
    And request { target: 'price-feed', enabled: true }
    When method post
    Then status 200
    # Restauración obligatoria
    Given path '/api/v1/chaos/latency'
    And request { target: 'price-feed', enabled: false }
    When method post
    Then status 200`
    }
  },
  'T-C': {
    microservicios: ['api-gateway', 'permission-service', 'insurance-policy'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Advisor as Junior Advisor Token
  participant GW as API Gateway
  participant RBAC as Permission Service (6202)
  participant Ins as Insurance Policy (7201)

  Advisor->>GW: PUT /api/v1/insurance/POL-992/beneficiary
  GW->>RBAC: Validate Permissions for Token Role
  RBAC->>RBAC: Check Matrix (Role: JUNIOR_ADVISOR, Action: EDIT_BENEFICIARY)
  RBAC-->>GW: ACCESS_DENIED (Policyholder consent required)
  GW-->>Advisor: HTTP 403 Forbidden
  Note over Ins: La póliza permanece inmutable`,
    brokenTest: {
      title: 'Bug T-C: Autorización con Token Elevado en Caso Negativo',
      badScenario: `Feature: Matriz RBAC
  Scenario: Validar restricción usando el token de Administrador
    Given url 'http://localhost:5125'
    And path '/api/v1/insurance/POL-992/beneficiary'
    # ERROR: Probar rechazo con token de ADMIN causa falso negativo (recibe 200 en vez de 403)
    And header Authorization = 'Bearer ADMIN_TOKEN'
    When method put
    Then status 403`,
      hint: 'Usa el rol restringido (JUNIOR_ADVISOR) para validar la regla de bloqueo 403.',
      fixTarget: 'JUNIOR_ADVISOR',
      validSolution: `Feature: Matriz RBAC Corregida
  Scenario: Validar restricción con rol adecuado
    Given url 'http://localhost:5125'
    And path '/api/v1/insurance/POL-992/beneficiary'
    And header Authorization = 'Bearer JUNIOR_ADVISOR'
    When method put
    Then status 403`
    }
  },
  'T-D': {
    microservicios: ['api-gateway', 'clock-service', 'tax-engine', 'statement-service'],
    diagramaSecuencia: `sequenceDiagram
  autonumber
  actor Test as Staff SDET
  participant Clock as Clock Service (6003)
  participant Tax as Tax Engine (7301)
  participant Acc as Account Registry

  Test->>Clock: GET /api/v1/clock
  Clock-->>Test: Current Virtual Time (e.g., 2026-10-01)
  Test->>Clock: POST /api/v1/clock/fast-forward { days: 90 }
  Clock-->>Test: Time Advanced to 2026-12-31T23:59:59
  Clock->>Tax: Publish Event: FiscalYearClosed
  Tax->>Acc: Calculate ISK Tax on Average Quarterly Balances
  Test->>Tax: GET /api/v1/accounts/ACC-ISK-100/tax-accruals
  Tax-->>Test: HTTP 200 (status: 'ACCRUED', taxYear: 2026)`,
    brokenTest: {
      title: 'Bug T-D: Aserción sobre Fecha Real del Sistema en Lugar del Reloj Virtual',
      badScenario: `Feature: Reloj Virtual
  Scenario: Verificar devengo usando reloj del sistema local
    Given url 'http://localhost:5125'
    And path '/api/v1/clock/fast-forward'
    And request { days: 90 }
    When method post
    Then status 200
    # ERROR: Comparar con java.time.LocalDate.now() ignora el reloj virtual acelerado
    And match response.virtualTime contains java.time.LocalDate.now().toString()`,
      hint: 'Compara contra la fecha del evento objetivo (12-31) devuelta por el reloj virtual.',
      fixTarget: '12-31',
      validSolution: `Feature: Reloj Virtual Corregido
  Scenario: Verificar devengo con reloj virtual
    Given url 'http://localhost:5125'
    And path '/api/v1/clock/fast-forward'
    And request { days: 90 }
    When method post
    Then status 200
    And match response.virtualTime contains '12-31'`
    }
  }
};

let enrichedCount = 0;
for (const lvl of data.niveles) {
  for (const tema of lvl.temas) {
    const extra = enrichments[tema.id];
    if (extra) {
      tema.microservicios = extra.microservicios;
      tema.diagramaSecuencia = extra.diagramaSecuencia;
      tema.brokenTest = extra.brokenTest;
      enrichedCount++;
    }
  }
}

writeFileSync(temasPath, JSON.stringify(data, null, 2), 'utf-8');
console.log(`¡Éxito! Enriquecidos ${enrichedCount} temas con diagramas y broken tests en ${temasPath}.`);
