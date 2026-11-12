// tests/gym.test.mjs: Suite de pruebas unitarias y de integración para el Gherkin BDD Gym (Given-When-Then) de Karate DSL.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const webDir = resolve(__dirname, '../torre/web');
const gymDir = resolve(webDir, 'gym');

// Cargar BrunoGym
const loadGym = () => {
  const code = readFileSync(resolve(webDir, 'bruno-gym.js'), 'utf-8');
  const context = { module: { exports: {} }, exports: {} };
  const fn = new Function('module', 'exports', code);
  fn(context.module, context.exports);
  return context.module.exports;
};

const { BrunoGym } = loadGym();

// Cargar BrunoLinter
const loadLinter = () => {
  const code = readFileSync(resolve(webDir, 'bruno-linter.js'), 'utf-8');
  const context = { module: { exports: {} }, exports: {} };
  const fn = new Function('module', 'exports', code);
  fn(context.module, context.exports);
  return context.module.exports;
};

const { BrunoLinter } = loadLinter();

// ============================================================================
// 1. Integridad de los Archivos del Gym (4,000 Ejercicios BDD)
// ============================================================================
test('Gym Files: existen los 4 archivos de nivel, catálogo y estadísticas', () => {
  assert.ok(existsSync(resolve(gymDir, 'junior.json')), 'Debe existir junior.json');
  assert.ok(existsSync(resolve(gymDir, 'mid.json')), 'Debe existir mid.json');
  assert.ok(existsSync(resolve(gymDir, 'senior.json')), 'Debe existir senior.json');
  assert.ok(existsSync(resolve(gymDir, 'staff.json')), 'Debe existir staff.json');
  assert.ok(existsSync(resolve(gymDir, 'catalog.json')), 'Debe existir catalog.json');
  assert.ok(existsSync(resolve(gymDir, 'stats.json')), 'Debe existir stats.json');
});

test('Gym Stats: valida exactamente 4,000 ejercicios y 1,000 por nivel', () => {
  const stats = JSON.parse(readFileSync(resolve(gymDir, 'stats.json'), 'utf-8'));
  assert.equal(stats.total, 4000, 'El total debe ser exactamente 4,000 ejercicios');
  assert.equal(stats.levels.junior, 1000, 'Junior debe tener 1,000 ejercicios');
  assert.equal(stats.levels.mid, 1000, 'Mid debe tener 1,000 ejercicios');
  assert.equal(stats.levels.senior, 1000, 'Senior debe tener 1,000 ejercicios');
  assert.equal(stats.levels.staff, 1000, 'Staff debe tener 1,000 ejercicios');
});

test('Gym Catalog: indexa 4,000 entradas consistentes', () => {
  const catalog = JSON.parse(readFileSync(resolve(gymDir, 'catalog.json'), 'utf-8'));
  assert.equal(catalog.length, 4000, 'El catálogo debe contener 4,000 entradas');

  // Verificar muestra de cada nivel
  const jr = catalog.find(x => x.id === 'JR-0001');
  const mid = catalog.find(x => x.id === 'MID-0001');
  const sr = catalog.find(x => x.id === 'SR-0001');
  const staff = catalog.find(x => x.id === 'STF-0001');

  assert.ok(jr && jr.level === 'junior', 'JR-0001 debe existir y ser junior');
  assert.ok(mid && mid.level === 'mid', 'MID-0001 debe existir y ser mid');
  assert.ok(sr && sr.level === 'senior', 'SR-0001 debe existir y ser senior');
  assert.ok(staff && staff.level === 'staff', 'STF-0001 debe existir y ser staff');
});

// ============================================================================
// 2. Calidad y Estructura BDD (Given - When - Then)
// ============================================================================
test('Gym Junior: 1,000 ejercicios con formato AAA y huecos requeridos', () => {
  const list = JSON.parse(readFileSync(resolve(gymDir, 'junior.json'), 'utf-8'));
  assert.equal(list.length, 1000);

  for (let i = 0; i < 50; i++) {
    const ex = list[i];
    assert.ok(ex.id.startsWith('JR-'), `${ex.id} debe iniciar con JR-`);
    assert.equal(ex.level, 'junior');
    assert.ok(ex.codeWithGaps.includes('___'), `${ex.id} debe contener huecos ___`);
    assert.ok(!ex.solution.includes('___'), `${ex.id} la solución no debe contener ___`);
    assert.ok(ex.solution.includes('Given '), `${ex.id} debe contener fase Given`);
    assert.ok(ex.solution.includes('When method'), `${ex.id} debe contener fase When`);
    assert.ok(ex.solution.includes('Then status'), `${ex.id} debe contener fase Then`);
    assert.ok(ex.gaps.length >= 1, `${ex.id} debe tener al menos un gap documentado`);
  }
});

test('Gym Mid: 1,000 ejercicios data-driven con Scenario Outline y Examples', () => {
  const list = JSON.parse(readFileSync(resolve(gymDir, 'mid.json'), 'utf-8'));
  assert.equal(list.length, 1000);

  for (let i = 0; i < 50; i++) {
    const ex = list[i];
    assert.ok(ex.id.startsWith('MID-'), `${ex.id} debe iniciar con MID-`);
    assert.equal(ex.level, 'mid');
    assert.ok(ex.solution.includes('Scenario Outline'), `${ex.id} debe contener Scenario Outline`);
    assert.ok(ex.solution.includes('Examples:'), `${ex.id} debe contener tabla Examples`);
    assert.ok(ex.codeWithGaps.includes('___'), `${ex.id} debe contener huecos ___`);
  }
});

test('Gym Senior: 1,000 ejercicios de resiliencia, sagas, retry until y RFC 9457', () => {
  const list = JSON.parse(readFileSync(resolve(gymDir, 'senior.json'), 'utf-8'));
  assert.equal(list.length, 1000);

  const sample = list.slice(0, 50);
  const hasAsync = sample.some(ex => ex.solution.includes('retry until') || /asíncrono/i.test(ex.enunciado));
  const hasSaga = sample.some(ex => /saga/i.test(ex.title) || /saga/i.test(ex.enunciado) || ex.solution.includes('retry until'));

  assert.ok(hasAsync, 'Senior debe incluir ejercicios de retry asíncrono');
  assert.ok(hasSaga, 'Senior debe incluir sagas o flujos de consistencia eventual');
});

test('Gym Staff: 1,000 ejercicios de caos, mocks karate.start, reloj y RBAC', () => {
  const list = JSON.parse(readFileSync(resolve(gymDir, 'staff.json'), 'utf-8'));
  assert.equal(list.length, 1000);

  const sample = list.slice(0, 50);
  const hasMock = sample.some(ex => ex.solution.includes('karate.start') || ex.enunciado.includes('mock'));
  const hasChaos = sample.some(ex => ex.solution.includes('fault') || ex.enunciado.includes('caos') || ex.enunciado.includes('latencia'));
  const hasClock = sample.some(ex => ex.solution.includes('clock') || ex.enunciado.includes('reloj') || ex.enunciado.includes('congelado'));

  assert.ok(hasMock || hasChaos || hasClock, 'Staff debe incluir mocks, caos o manipulación temporal');
});

// ============================================================================
// 3. Controlador BrunoGym & Linter Integration
// ============================================================================
test('BrunoGym: funciones base y escape de entidades HTML', () => {
  assert.equal(BrunoGym.escape('<script>alert("xss")</script>'), '&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;');
  assert.equal(BrunoGym.currentLevel, 'junior');
});

test('BrunoLinter: detecta violación de contrato BDD en código de usuario', () => {
  const badBddCode = `
    Feature: Transferencias
    Scenario: Envío de fondos
      Given match response.status == 'SUCCESS'
      When method post
  `;
  const issues = BrunoLinter.audit(badBddCode);
  const ruleIds = issues.map(x => x.rule);
  assert.ok(ruleIds.includes('GHERKIN_ASSERTION_IN_GIVEN'), 'Debe señalar aserción prematura en Given');
  assert.ok(ruleIds.includes('GHERKIN_MISSING_THEN_STATUS'), 'Debe señalar omisión de Then status');
});

// ============================================================================
// 4. Integración HTTP con Nordvik.Host (Endpoints en Vivo)
// ============================================================================
test('API /api/v1/gym/stats: responde 200 con total de 4,000 ejercicios', async () => {
  const res = await fetch('http://localhost:3000/api/v1/gym/stats');
  assert.ok(res.ok, `HTTP ${res.status}`);
  const data = await res.json();
  assert.equal(data.total, 4000);
  assert.equal(data.levels.junior, 1000);
  assert.equal(data.levels.senior, 1000);
});

test('API /api/v1/gym/exercise: recupera JR-0001 con estructura completa', async () => {
  const res = await fetch('http://localhost:3000/api/v1/gym/exercise?id=JR-0001');
  assert.ok(res.ok, `HTTP ${res.status}`);
  const ex = await res.json();
  assert.equal(ex.id, 'JR-0001');
  assert.equal(ex.level, 'junior');
  assert.ok(ex.codeWithGaps.includes('___'));
  assert.ok(ex.hints.length >= 1);
});

test('API /api/v1/gym/random: devuelve ejercicio aleatorio del nivel solicitado', async () => {
  const res = await fetch('http://localhost:3000/api/v1/gym/random?level=senior');
  assert.ok(res.ok, `HTTP ${res.status}`);
  const ex = await res.json();
  assert.equal(ex.level, 'senior');
  assert.ok(ex.id.startsWith('SR-'));
});

test('API /api/v1/gym/verify: rechaza huecos incompletos y valida soluciones', async () => {
  // 1. Con huecos sin rellenar
  const res1 = await fetch('http://localhost:3000/api/v1/gym/verify', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id: 'JR-0001', code: 'Given url baseUrl\nWhen method ___\nThen status 200' })
  });
  assert.ok(res1.ok);
  const data1 = await res1.json();
  assert.equal(data1.valid, false);
  assert.ok(data1.reason.includes('___'));

  // 2. Con solución completa correcta obtenida de JR-0001
  const exRes = await fetch('http://localhost:3000/api/v1/gym/exercise?id=JR-0001');
  const ex = await exRes.json();
  const res2 = await fetch('http://localhost:3000/api/v1/gym/verify', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id: 'JR-0001', code: ex.solution })
  });
  assert.ok(res2.ok);
  const data2 = await res2.json();
  assert.equal(data2.valid, true);
  assert.equal(data2.xp, 15);
});
