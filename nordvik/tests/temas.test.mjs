// tests/temas.test.mjs: Suite de pruebas para el Currículo Pedagógico Actuarial (temas.json y endpoints /api/v1/temas)
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const webDir = resolve(__dirname, '../torre/web');
const temasPath = resolve(webDir, 'temas.json');

// ============================================================================
// 1. Integridad del Dataset Pedagógico (temas.json)
// ============================================================================
test('temas.json: existe y es JSON válido', () => {
  assert.ok(existsSync(temasPath), 'Debe existir temas.json en torre/web');
  const raw = readFileSync(temasPath, 'utf-8');
  const data = JSON.parse(raw);
  assert.equal(typeof data, 'object', 'Debe parsear como objeto');
});

test('temas.json: estructura de 4 niveles y 16 módulos de dominio actuarial', () => {
  const data = JSON.parse(readFileSync(temasPath, 'utf-8'));
  assert.equal(data.totalTemas, 16, 'Debe haber exactamente 16 temas maestros');
  assert.ok(Array.isArray(data.niveles), 'niveles debe ser un array');
  assert.equal(data.niveles.length, 4, 'Debe haber 4 niveles de seniority');

  const expectedLevels = ['junior', 'mid', 'senior', 'staff'];
  data.niveles.forEach((lvl, idx) => {
    assert.equal(lvl.id, expectedLevels[idx], `Nivel ${idx} debe ser ${expectedLevels[idx]}`);
    assert.ok(lvl.name, 'El nivel debe tener nombre');
    assert.ok(lvl.tagline, 'El nivel debe tener tagline');
    assert.ok(Array.isArray(lvl.temas), 'temas debe ser un array');
    assert.equal(lvl.temas.length, 4, `El nivel ${lvl.id} debe contener exactamente 4 temas`);
  });
});

test('temas.json: cada tema contiene el embudo pedagógico de 4 fases completas', () => {
  const data = JSON.parse(readFileSync(temasPath, 'utf-8'));
  
  for (const lvl of data.niveles) {
    for (const tema of lvl.temas) {
      // Metadatos y contexto de negocio
      assert.ok(tema.id, `Tema en ${lvl.id} debe tener id`);
      assert.ok(tema.title, `Tema ${tema.id} debe tener título`);
      assert.ok(tema.subtitle, `Tema ${tema.id} debe tener subtítulo`);
      assert.ok(tema.businessContext, `Tema ${tema.id} debe explicar el contexto bancario`);
      assert.ok(tema.catastrofeFinanciera, `Tema ${tema.id} debe explicar la catástrofe financiera prevenida`);
      assert.ok(tema.karateConcept, `Tema ${tema.id} debe explicar el concepto de Karate DSL`);
      assert.ok(tema.analogia, `Tema ${tema.id} debe contener una analogía cotidiana`);

      // Fase 1: Anatomía del Código
      assert.ok(tema.fase1_anatomia, `Tema ${tema.id} debe tener fase1_anatomia`);
      assert.ok(tema.fase1_anatomia.code.includes('Given') || tema.fase1_anatomia.code.includes('Feature'), `Tema ${tema.id} fase 1 debe contener código Karate`);
      assert.ok(tema.fase1_anatomia.explanation, `Tema ${tema.id} fase 1 debe contener explicación`);

      // Fase 2: Trampa de Bruno
      assert.ok(tema.fase2_trampa, `Tema ${tema.id} debe tener fase2_trampa`);
      assert.ok(tema.fase2_trampa.badCode, `Tema ${tema.id} fase 2 debe contener código con error típico`);
      assert.ok(tema.fase2_trampa.explanation, `Tema ${tema.id} fase 2 debe explicar por qué falla`);

      // Fase 3: Reto en el BDD Gym
      assert.ok(tema.fase3_reto, `Tema ${tema.id} debe tener fase3_reto`);
      assert.ok(tema.fase3_reto.exerciseId, `Tema ${tema.id} fase 3 debe vincular a un exerciseId`);
      assert.ok(tema.fase3_reto.prompt, `Tema ${tema.id} fase 3 debe tener un prompt de reto`);
    }
  }
});

// ============================================================================
// 2. Endpoints HTTP de la Torre de Control (/api/v1/temas)
// ============================================================================
const HOST_URL = 'http://localhost:3000';

test('API /api/v1/temas: devuelve catálogo completo de 16 temas', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas`);
  assert.equal(res.status, 200, 'Debe responder HTTP 200');
  const data = await res.json();
  assert.equal(data.totalTemas, 16);
  assert.equal(data.niveles.length, 4);
});

test('API /api/v1/temas?nivel=junior: filtra los temas del nivel Junior', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas?nivel=junior`);
  assert.equal(res.status, 200, 'Debe responder HTTP 200');
  const data = await res.json();
  assert.equal(data.id, 'junior');
  assert.equal(data.temas.length, 4);
  assert.equal(data.temas[0].id, 'J-A');
});

test('API /api/v1/temas?nivel=senior: filtra los temas del nivel Senior', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas?nivel=senior`);
  assert.equal(res.status, 200, 'Debe responder HTTP 200');
  const data = await res.json();
  assert.equal(data.id, 'senior');
  assert.equal(data.temas.length, 4);
  assert.equal(data.temas[0].id, 'S-A');
});

test('API /api/v1/temas/J-A: devuelve el tema individual exacto con todas sus fases', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas/J-A`);
  assert.equal(res.status, 200, 'Debe responder HTTP 200');
  const tema = await res.json();
  assert.equal(tema.id, 'J-A');
  assert.equal(tema.domain, 'funds');
  assert.ok(tema.fase1_anatomia);
  assert.ok(tema.fase2_trampa);
  assert.ok(tema.fase3_reto);
});

test('API /api/v1/temas/T-D: devuelve el tema Staff de Manipulación Temporal', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas/T-D`);
  assert.equal(res.status, 200, 'Debe responder HTTP 200');
  const tema = await res.json();
  assert.equal(tema.id, 'T-D');
  assert.equal(tema.domain, 'quality');
  assert.equal(tema.fase3_reto.exerciseId, 'STF-0020');
});

test('API /api/v1/temas/INVALID: responde 404 para un ID inexistente', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas/XYZ-999`);
  assert.equal(res.status, 404, 'Debe responder HTTP 404');
});

test('temas.json: todos los exerciseId de fase3_reto existen en gym/*.json', () => {
  const data = JSON.parse(readFileSync(temasPath, 'utf-8'));
  const gymDir = resolve(webDir, 'gym');
  const allExercises = new Map();
  for (const file of ['junior.json', 'mid.json', 'senior.json', 'staff.json']) {
    const arr = JSON.parse(readFileSync(resolve(gymDir, file), 'utf-8'));
    for (const ex of arr) allExercises.set(ex.id, ex);
  }
  for (const lvl of data.niveles) {
    for (const tema of lvl.temas) {
      const exId = tema.fase3_reto?.exerciseId;
      assert.ok(allExercises.has(exId), `exerciseId ${exId} de tema ${tema.id} debe existir en gym/*.json`);
    }
  }
});

test('API /api/v1/gym/random?level=junior&domain=fx: filtra por dominio fx', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/gym/random?level=junior&domain=fx`);
  assert.equal(res.status, 200, 'Debe responder HTTP 200');
  const ex = await res.json();
  assert.equal(ex.domain, 'fx', 'El ejercicio devuelto debe pertenecer al dominio fx');
});
