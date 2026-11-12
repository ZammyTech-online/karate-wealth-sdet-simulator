#!/usr/bin/env node
/**
 * Engine Generador Industrial de Ejercicios BDD para Karate DSL (Given - When - Then)
 * Sintetiza 4,000 ejercicios combinatorios estructurados en 4 niveles de seniority:
 * - Junior SDET: 1,000 ejercicios (Fundamentos AAA, HTTP methods, status, headers)
 * - Mid SDET: 1,000 ejercicios (Data-Driven, Scenario Outline, Examples tables, Schemas)
 * - Senior SDET: 1,000 ejercicios (Sagas, Async assertions, retry until, RFC 9457, Idempotencia)
 * - Staff SDET: 1,000 ejercicios (Caos, Mocks dinámicos, Clock shift, RBAC e IDOR)
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, '..');
const GYM_DIR = path.join(REPO_ROOT, 'torre', 'web', 'gym');

fs.mkdirSync(GYM_DIR, { recursive: true });

console.log('Iniciando generación masiva de 4,000 ejercicios BDD en:', GYM_DIR);

// Catálogos combinatorios actuariales
const CURRENCIES = ['SEK', 'EUR', 'USD', 'NOK', 'DKK'];
const FUNDS = [
  { isin: 'SE0001234567', name: 'Nordvik Global ESG Equity', category: 'EQUITY', risk: 'HIGH' },
  { isin: 'SE0009876543', name: 'Stockholm Fixed Income SEK', category: 'FIXED_INCOME', risk: 'LOW' },
  { isin: 'SE0005558881', name: 'Nordic Clean Tech Innovation', category: 'EQUITY', risk: 'VERY_HIGH' },
  { isin: 'SE0003332211', name: 'Scandi Balanced Pension 60', category: 'BALANCED', risk: 'MEDIUM' },
  { isin: 'SE0007774433', name: 'Gothenburg Liquid Cash Asset', category: 'MONEY_MARKET', risk: 'LOW' },
  { isin: 'SE0004449922', name: 'Malmo Green Infrastructure', category: 'INFRASTRUCTURE', risk: 'MEDIUM' },
  { isin: 'SE0008881177', name: 'Nordic Small Cap Index Fund', category: 'SMALL_CAP', risk: 'HIGH' },
  { isin: 'SE0002226655', name: 'Soderberg Horizon Retirement 2050', category: 'TARGET_DATE', risk: 'MEDIUM' }
];

const DOMAINS = ['funds', 'accounts', 'orders', 'fx', 'pensions', 'kyc', 'chaos', 'market'];

// ============================================================================
// 1. GENERADOR JUNIOR (1,000 Ejercicios: Fundamentos Given - When - Then)
// ============================================================================
function generateJuniorExercises() {
  const exercises = [];
  const operations = [
    { name: 'Consulta de catálogo', method: 'get', path: '/api/v1/funds', status: 200, resKey: 'items', resVal: '#[]' },
    { name: 'Detalle de fondo por ISIN', method: 'get', path: '/api/v1/funds/', status: 200, resKey: 'isin', resVal: '#string' },
    { name: 'Generación de UUID para orden', method: 'post', path: '/api/v1/uuid/generate', status: 201, resKey: 'uuid', resVal: '#string' },
    { name: 'Lectura de tipos de cambio FX', method: 'get', path: '/api/v1/fx/rates', status: 200, resKey: 'rates', resVal: '#object' },
    { name: 'Validación de día hábil bancario', method: 'get', path: '/api/v1/calendar/is-business-day', status: 200, resKey: 'isBusinessDay', resVal: '#boolean' },
    { name: 'Consulta de secuencia numérica', method: 'post', path: '/api/v1/sequence/next', status: 200, resKey: 'current', resVal: '#number' },
    { name: 'Validación de recurso inexistente', method: 'get', path: '/api/v1/funds/UNKNOWN_', status: 404, resKey: 'title', resVal: 'Not Found' },
    { name: 'Petición con parámetro de categoría', method: 'get', path: '/api/v1/funds', status: 200, resKey: 'category', resVal: '#string' },
    { name: 'Consulta de cotizaciones en tiempo real', method: 'get', path: '/api/v1/market/quote/', status: 200, resKey: 'price', resVal: '#number' },
    { name: 'Lectura de hora de corte de mercado', method: 'get', path: '/api/v1/clock/cutoff', status: 200, resKey: 'cutoffTime', resVal: '#string' }
  ];

  for (let i = 1; i <= 1000; i++) {
    const id = `JR-${String(i).padStart(4, '0')}`;
    const op = operations[(i - 1) % operations.length];
    const fund = FUNDS[(i - 1) % FUNDS.length];
    const ccy = CURRENCIES[(i - 1) % CURRENCIES.length];
    const domain = DOMAINS[(i - 1) % DOMAINS.length];

    let fullPath = op.path;
    if (op.path.endsWith('/')) {
      fullPath = `${op.path}${fund.isin}`;
    } else if (op.path.endsWith('UNKNOWN_')) {
      fullPath = `${op.path}${i}`;
    }

    const title = `Junior BDD #${String(i).padStart(4, '0')}: ${op.name} (${ccy})`;
    const enunciado = `Como SDET Junior en Nordvik, estructura una prueba BDD para ${op.name.toLowerCase()} en divisa ${ccy}. Completa los pasos 'Given' (Arrange), 'When' (Act) y 'Then' (Assert) respetando la convención de Karate.`;

    const codeWithGaps = `Feature: ${title}

  Scenario: Ejecución atómica de ${op.name.toLowerCase()}
    Given url baseUrl
    And ___ '${fullPath}'
    When ___ ${op.method}
    Then ___ ${op.status}
    And match response.${op.resKey} == ___`;

    const solution = `Feature: ${title}

  Scenario: Ejecución atómica de ${op.name.toLowerCase()}
    Given url baseUrl
    And path '${fullPath}'
    When method ${op.method}
    Then status ${op.status}
    And match response.${op.resKey} == ${op.resVal}`;

    exercises.push({
      id,
      level: 'junior',
      levelName: 'Junior SDET (Fundamentos AAA)',
      domain,
      exerciseNumber: i,
      title,
      enunciado,
      codeWithGaps,
      solution,
      gaps: ['path', 'method', 'status', op.resVal],
      hints: [
        `Pista 1: La fase de preparación 'Given' define la ruta del recurso con 'path'.`,
        `Pista 2: La fase 'Act' dispara la llamada HTTP usando la palabra clave 'method' seguida del verbo (${op.method}).`,
        `Pista 3: La aserción valida 'status ${op.status}' y comprueba el tipo con '${op.resVal}'.`
      ]
    });
  }
  return exercises;
}

// ============================================================================
// 2. GENERADOR MID (1,000 Ejercicios: Data-Driven, Scenario Outline & Schemas)
// ============================================================================
function generateMidExercises() {
  const exercises = [];
  const midTemplates = [
    { domain: 'orders', type: 'BUY', maxAmount: 50000 },
    { domain: 'orders', type: 'SELL', maxAmount: 25000 },
    { domain: 'fx', type: 'SWAP', maxAmount: 100000 },
    { domain: 'kyc', type: 'RISK_ASSESSMENT', maxAmount: 1000000 },
    { domain: 'statements', type: 'TAX_AUDIT', maxAmount: 500000 }
  ];

  for (let i = 1; i <= 1000; i++) {
    const id = `MID-${String(i).padStart(4, '0')}`;
    const tmpl = midTemplates[(i - 1) % midTemplates.length];
    const fund = FUNDS[(i - 1) % FUNDS.length];
    const ccy = CURRENCIES[(i - 1) % CURRENCIES.length];
    const amount = 1000 * ((i % 50) + 1);

    const title = `Mid SDET BDD #${String(i).padStart(4, '0')}: Matriz Data-Driven de ${tmpl.type} para ${fund.name}`;
    const enunciado = `Diseña un 'Scenario Outline' parametrizado con tabla 'Examples:' para verificar la suscripción actuarial con diferentes importes y divisas. Garantiza que la respuesta cumpla el esquema estricto de tipos.`;

    const codeWithGaps = `Feature: ${title}

  Background:
    * url baseUrl
    * def orderSchema = { orderId: '#regex ^ORD-[0-9]+$', status: '#string', amount: '#number', currency: '#string' }

  ___: Suscripción de orden paramétrica
    Given path '/api/v1/orders'
    And request { isin: '<isin>', amount: <amount>, currency: '<currency>' }
    When method post
    Then status <expectedStatus>
    And match response == ___

    ___:
      | isin | amount | currency | expectedStatus |
      | ${fund.isin} | ${amount} | ${ccy} | 201 |
      | ${fund.isin} | 0 | ${ccy} | 400 |`;

    const solution = `Feature: ${title}

  Background:
    * url baseUrl
    * def orderSchema = { orderId: '#regex ^ORD-[0-9]+$', status: '#string', amount: '#number', currency: '#string' }

  Scenario Outline: Suscripción de orden paramétrica
    Given path '/api/v1/orders'
    And request { isin: '<isin>', amount: <amount>, currency: '<currency>' }
    When method post
    Then status <expectedStatus>
    And match response == orderSchema

    Examples:
      | isin | amount | currency | expectedStatus |
      | ${fund.isin} | ${amount} | ${ccy} | 201 |
      | ${fund.isin} | 0 | ${ccy} | 400 |`;

    exercises.push({
      id,
      level: 'mid',
      levelName: 'Mid SDET (Data-Driven & Schemas)',
      domain: tmpl.domain,
      exerciseNumber: i,
      title,
      enunciado,
      codeWithGaps,
      solution,
      gaps: ['Scenario Outline', 'orderSchema', 'Examples'],
      hints: [
        `Pista 1: Para pruebas parametrizadas en Gherkin, usamos 'Scenario Outline:'.`,
        `Pista 2: La validación estructural compara contra el esquema definido en el Background ('orderSchema').`,
        `Pista 3: La tabla de datos se declara con la palabra clave 'Examples:'.`
      ]
    });
  }
  return exercises;
}

// ============================================================================
// 3. GENERADOR SENIOR (1,000 Ejercicios: Sagas, Async Assertions & RFC 9457)
// ============================================================================
function generateSeniorExercises() {
  const exercises = [];
  const seniorTemplates = [
    { domain: 'pensions', action: 'Traspaso de pensión', endState: 'COMPLETED', retries: 10 },
    { domain: 'payments', action: 'Liquidación interbancaria SEPA', endState: 'SETTLED', retries: 8 },
    { domain: 'compliance', action: 'Auditoría de consistencia eventual', endState: 'VERIFIED', retries: 5 }
  ];

  for (let i = 1; i <= 1000; i++) {
    const id = `SR-${String(i).padStart(4, '0')}`;
    const tmpl = seniorTemplates[(i - 1) % seniorTemplates.length];
    const fund = FUNDS[(i - 1) % FUNDS.length];
    const sagaId = `SAGA-${fund.isin.slice(-4)}-${i}`;

    const title = `Senior SDET BDD #${String(i).padStart(4, '0')}: Saga Asíncrona de ${tmpl.action}`;
    const enunciado = `Valida una saga financiera de larga duración sin usar pausas pasivas (cero sleep). Implementa el patrón 'retry until' de Karate y valida la idempotencia y las cabeceras de correlación.`;

    const codeWithGaps = `Feature: ${title}

  Background:
    * url baseUrl
    * configure retry = { count: ${tmpl.retries}, interval: 500 }
    * def auth = call ___ 'this:_login.feature'

  Scenario: Seguimiento no bloqueante de saga financiera
    Given path '/api/v1/sagas/${sagaId}'
    And header Idempotency-Key = 'IDEM-${i}'
    And header Authorization = auth.token
    And ___ until response.status == '${tmpl.endState}'
    When method get
    Then status 200
    And match response.status == '${tmpl.endState}'
    And ___ response.latency < 400`;

    const solution = `Feature: ${title}

  Background:
    * url baseUrl
    * configure retry = { count: ${tmpl.retries}, interval: 500 }
    * def auth = call read('this:_login.feature')

  Scenario: Seguimiento no bloqueante de saga financiera
    Given path '/api/v1/sagas/${sagaId}'
    And header Idempotency-Key = 'IDEM-${i}'
    And header Authorization = auth.token
    And retry until response.status == '${tmpl.endState}'
    When method get
    Then status 200
    And match response.status == '${tmpl.endState}'
    And assert response.latency < 400`;

    exercises.push({
      id,
      level: 'senior',
      levelName: 'Senior SDET (Sagas & Resiliencia Asíncrona)',
      domain: tmpl.domain,
      exerciseNumber: i,
      title,
      enunciado,
      codeWithGaps,
      solution,
      gaps: ["read('this:_login.feature')", 'retry', 'assert'],
      hints: [
        `Pista 1: Reutilizamos autenticación modularmente con 'call read(...)'.`,
        `Pista 2: La espera asíncrona reactiva se configura con la cláusula 'retry until'.`,
        `Pista 3: Para aserciones numéricas relativas (<, >) se usa la instrucción nativa 'assert'.`
      ]
    });
  }
  return exercises;
}

// ============================================================================
// 4. GENERADOR STAFF / LEAD (1,000 Ejercicios: Caos, Mocks & Gobernanza)
// ============================================================================
function generateStaffExercises() {
  const exercises = [];
  const staffTemplates = [
    { domain: 'chaos', scenario: 'latencia inyectada de 1500ms y status 503', chaosHdr: 'latency=1500;status=503', fallbackCode: 503 },
    { domain: 'mocks', scenario: 'simulación de pasarela externa bancaria con karate.start', chaosHdr: 'mock-sim', fallbackCode: 200 },
    { domain: 'security', scenario: 'detección de IDOR y escalamiento de privilegios RBAC', chaosHdr: 'role=AUDITOR', fallbackCode: 403 }
  ];

  for (let i = 1; i <= 1000; i++) {
    const id = `STF-${String(i).padStart(4, '0')}`;
    const tmpl = staffTemplates[(i - 1) % staffTemplates.length];
    const portMock = 9000 + (i % 500);

    const title = `Staff SDET BDD #${String(i).padStart(4, '0')}: Gobernanza y ${tmpl.scenario}`;
    const enunciado = `Como Staff SDET, diseña una prueba de resiliencia ante fallos sistémicos. Utiliza inyección de caos con cabeceras de red o gestiona el ciclo de vida de un mock dinámico en un puerto efímero.`;

    const codeWithGaps = `Feature: ${title}

  Background:
    * url baseUrl
    * def mockServer = karate.___({ mock: 'this:_mock_bank.feature', port: 0 })

  Scenario: Verificación de tolerancia a fallos y fallback
    Given path '/api/v1/resilience/clearing'
    And header X-Chaos-Scenario = '${tmpl.chaosHdr}'
    When method post
    Then status ${tmpl.fallbackCode}
    And match response.error == '#present'
    * mockServer.___()`;

    const solution = `Feature: ${title}

  Background:
    * url baseUrl
    * def mockServer = karate.start({ mock: 'this:_mock_bank.feature', port: 0 })

  Scenario: Verificación de tolerancia a fallos y fallback
    Given path '/api/v1/resilience/clearing'
    And header X-Chaos-Scenario = '${tmpl.chaosHdr}'
    When method post
    Then status ${tmpl.fallbackCode}
    And match response.error == '#present'
    * mockServer.stop()`;

    exercises.push({
      id,
      level: 'staff',
      levelName: 'Staff / Lead SDET (Caos, Mocks & Gobernanza)',
      domain: tmpl.domain,
      exerciseNumber: i,
      title,
      enunciado,
      codeWithGaps,
      solution,
      gaps: ['start', 'stop'],
      hints: [
        `Pista 1: Para levantar un servidor HTTP simulado en memoria usamos 'karate.start({ mock: ..., port: 0 })'.`,
        `Pista 2: Para liberar el socket TCP y evitar puertos huérfanos al finalizar el escenario, llamamos a 'mockServer.stop()'.`
      ]
    });
  }
  return exercises;
}

// ============================================================================
// EJECUCIÓN Y ESCRITURA EN DISCO
// ============================================================================
console.log('Generando 1,000 ejercicios Junior SDET...');
const junior = generateJuniorExercises();
console.log('Generando 1,000 ejercicios Mid SDET...');
const mid = generateMidExercises();
console.log('Generando 1,000 ejercicios Senior SDET...');
const senior = generateSeniorExercises();
console.log('Generando 1,000 ejercicios Staff SDET...');
const staff = generateStaffExercises();

console.log('Guardando bases de datos particionadas en JSON...');
fs.writeFileSync(path.join(GYM_DIR, 'junior.json'), JSON.stringify(junior, null, 2), 'utf8');
fs.writeFileSync(path.join(GYM_DIR, 'mid.json'), JSON.stringify(mid, null, 2), 'utf8');
fs.writeFileSync(path.join(GYM_DIR, 'senior.json'), JSON.stringify(senior, null, 2), 'utf8');
fs.writeFileSync(path.join(GYM_DIR, 'staff.json'), JSON.stringify(staff, null, 2), 'utf8');

// Índice ligero para el catálogo rápido
const catalog = [
  ...junior.map(x => ({ id: x.id, level: x.level, domain: x.domain, title: x.title, num: x.exerciseNumber })),
  ...mid.map(x => ({ id: x.id, level: x.level, domain: x.domain, title: x.title, num: x.exerciseNumber })),
  ...senior.map(x => ({ id: x.id, level: x.level, domain: x.domain, title: x.title, num: x.exerciseNumber })),
  ...staff.map(x => ({ id: x.id, level: x.level, domain: x.domain, title: x.title, num: x.exerciseNumber }))
];

fs.writeFileSync(path.join(GYM_DIR, 'catalog.json'), JSON.stringify(catalog), 'utf8');

// Estadísticas de la base de datos
const stats = {
  total: catalog.length,
  levels: {
    junior: junior.length,
    mid: mid.length,
    senior: senior.length,
    staff: staff.length
  },
  generatedAt: new Date().toISOString()
};
fs.writeFileSync(path.join(GYM_DIR, 'stats.json'), JSON.stringify(stats, null, 2), 'utf8');

console.log(`✅ ¡Generación completada con éxito!`);
console.log(`Total ejercicios BDD generados: ${stats.total}`);
console.log(`- Junior SDET: ${stats.levels.junior}`);
console.log(`- Mid SDET: ${stats.levels.mid}`);
console.log(`- Senior SDET: ${stats.levels.senior}`);
console.log(`- Staff SDET: ${stats.levels.staff}`);
