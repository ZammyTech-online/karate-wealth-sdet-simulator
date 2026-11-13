// tests/pedagogia.test.mjs: Suite de pruebas para Features ejecutables, Diagramas y Certificación SDET
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const webDir = resolve(__dirname, '../torre/web');
const featDir = resolve(__dirname, '../features/pedagogicos');
const temasPath = resolve(webDir, 'temas.json');

// Cargar Certificacion
const loadCert = () => {
  const code = readFileSync(resolve(webDir, 'certificacion.js'), 'utf-8');
  const context = { module: { exports: {} }, exports: {} };
  const fn = new Function('module', 'exports', code);
  fn(context.module, context.exports);
  return context.module.exports;
};

const { Certificacion } = loadCert();

// ============================================================================
// 1. Integridad de los Archivos .feature Ejecutables en Disco (P-01)
// ============================================================================
test('Features en Disco: existen exactamente 16 archivos .feature en features/pedagogicos', () => {
  assert.ok(existsSync(featDir), 'Debe existir la carpeta features/pedagogicos');
  const files = readdirSync(featDir).filter(f => f.endsWith('.feature'));
  assert.equal(files.length, 16, 'Debe haber exactamente 16 archivos .feature');

  const expectedPrefixes = [
    'J-A', 'J-B', 'J-C', 'J-D',
    'M-A', 'M-B', 'M-C', 'M-D',
    'S-A', 'S-B', 'S-C', 'S-D',
    'T-A', 'T-B', 'T-C', 'T-D'
  ];

  expectedPrefixes.forEach(prefix => {
    const found = files.find(f => f.startsWith(`${prefix}-`));
    assert.ok(found, `Debe existir archivo feature para el tema ${prefix}`);
    const content = readFileSync(resolve(featDir, found), 'utf-8');
    assert.ok(content.includes('Feature:'), `Feature ${found} debe contener directiva Feature`);
    assert.ok(content.includes('Scenario:') || content.includes('Scenario Outline:'), `Feature ${found} debe contener escenarios`);
    assert.ok(content.includes('When method'), `Feature ${found} debe contener When method HTTP`);
    assert.ok(content.includes('Then status'), `Feature ${found} debe validar status HTTP`);
  });
});

// ============================================================================
// 2. Enriquecimiento Arquitectónico en temas.json (P-02 & P-03)
// ============================================================================
test('temas.json: los 16 temas contienen microservicios, diagramas y broken tests', () => {
  const data = JSON.parse(readFileSync(temasPath, 'utf-8'));
  
  for (const lvl of data.niveles) {
    for (const t of lvl.temas) {
      assert.ok(Array.isArray(t.microservicios), `Tema ${t.id} debe tener array de microservicios`);
      assert.ok(t.microservicios.length >= 1, `Tema ${t.id} debe listar al menos 1 microservicio`);
      assert.ok(typeof t.diagramaSecuencia === 'string' && t.diagramaSecuencia.includes('sequenceDiagram'), `Tema ${t.id} debe contener diagrama de secuencia Mermaid`);
      assert.ok(t.brokenTest, `Tema ${t.id} debe contener brokenTest (modo fix-the-bug)`);
      assert.ok(t.brokenTest.badScenario.includes('Feature:'), `BrokenTest en ${t.id} debe ser un escenario feature real`);
      assert.ok(t.brokenTest.hint, `BrokenTest en ${t.id} debe incluir pista`);
      assert.ok(t.brokenTest.fixTarget, `BrokenTest en ${t.id} debe definir target de corrección`);
    }
  }
});

// ============================================================================
// 3. Descarga de Features vía API Backend (P-06)
// ============================================================================
const HOST_URL = 'http://localhost:3000';

test('API /api/v1/temas/{id}/feature: descarga el archivo .feature correspondiente', async () => {
  const testIds = ['J-A', 'M-A', 'S-C', 'T-B'];
  for (const id of testIds) {
    const res = await fetch(`${HOST_URL}/api/v1/temas/${id}/feature`);
    assert.equal(res.status, 200, `Debe responder HTTP 200 para ${id}`);
    const text = await res.text();
    assert.ok(text.includes(`Feature: ${id}`), `El contenido debe corresponder a la feature ${id}`);
    assert.ok(text.includes('When method'), `Debe contener sintaxis de método HTTP`);
  }
});

test('API /api/v1/temas/INVALID/feature: responde 404 para feature inexistente', async () => {
  const res = await fetch(`${HOST_URL}/api/v1/temas/XYZ-999/feature`);
  assert.equal(res.status, 404, 'Debe responder 404');
});

// ============================================================================
// 4. Simulador de Certificación y Generador de Diplomas (P-04 & P-05)
// ============================================================================
test('Certificacion: preguntas y opciones válidas para todos los niveles', () => {
  const levels = ['junior', 'mid', 'senior', 'staff'];
  levels.forEach(lvl => {
    const qList = Certificacion.examQuestions[lvl];
    assert.ok(Array.isArray(qList), `Nivel ${lvl} debe tener preguntas`);
    assert.equal(qList.length, 5, `Nivel ${lvl} debe tener exactamente 5 preguntas maestras`);

    qList.forEach((q, idx) => {
      assert.ok(q.q, `Pregunta ${idx + 1} de ${lvl} debe tener enunciado`);
      assert.equal(q.opts.length, 4, `Pregunta ${idx + 1} de ${lvl} debe tener 4 opciones`);
      assert.ok(q.answer >= 0 && q.answer < 4, `Respuesta en ${idx + 1} de ${lvl} debe ser índice válido`);
      assert.ok(q.explanation, `Pregunta ${idx + 1} de ${lvl} debe tener explicación didáctica`);
    });
  });
});

test('Certificacion: nombres legibles de niveles y escape seguro', () => {
  assert.equal(Certificacion.getLevelName('junior'), 'Junior SDET');
  assert.equal(Certificacion.getLevelName('staff'), 'Staff / Lead SDET');
  assert.equal(Certificacion.escape('<script>alert("xss")</script>'), '&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;');
});

test('Certificacion: generación de Diploma Digital Oficial con firma de Bruno y SHA-256', () => {
  let innerHtmlSet = '';
  globalThis.document = {
    getElementById: (id) => {
      if (id === 'diploma-render-area') return { set innerHTML(val) { innerHtmlSet = val; } };
      if (id === 'diploma-modal') return { showModal: () => {} };
      if (id === 'diploma-student-name') return { value: 'Zammy Test Engineer' };
      return null;
    }
  };

  Certificacion.showDiploma('senior', 95);
  assert.ok(innerHtmlSet.includes('Maestría en Karate DSL &amp; Sistemas Actuariales'), 'Debe incluir título del certificado');
  assert.ok(innerHtmlSet.includes('SENIOR SDET'), 'Debe incluir nivel Senior');
  assert.ok(innerHtmlSet.includes('Zammy Test Engineer'), 'Debe incluir nombre del estudiante');
  assert.ok(innerHtmlSet.includes('Bruno SDET'), 'Debe incluir firma de Bruno');
  assert.ok(innerHtmlSet.includes('CERT-NORDVIK-SENIOR-'), 'Debe incluir código único de certificado');
});

test('Certificacion: updateDiplomaName actualiza el nombre sin regenerar certId ni reabrir modal', () => {
  let displayText = '';
  let showModalCalls = 0;
  globalThis.document = {
    getElementById: (id) => {
      if (id === 'diploma-render-area') return { innerHTML: '' };
      if (id === 'diploma-modal') return { open: true, showModal: () => { showModalCalls++; } };
      if (id === 'diploma-student-name') return { value: 'Ana QA' };
      if (id === 'diploma-student-display') return { set textContent(v) { displayText = v; } };
      return null;
    }
  };

  Certificacion.showDiploma('mid', 80);
  const initialCertId = Certificacion.currentCertId;
  assert.equal(showModalCalls, 0, 'No debe llamar showModal si el dialog ya está abierto');
  assert.equal(Certificacion.lastPercentage, 80);

  Certificacion.updateDiplomaName('Carlos Senior SDET');
  assert.equal(displayText, 'Carlos Senior SDET');
  assert.equal(Certificacion.currentCertId, initialCertId, 'No debe regenerar certId al editar nombre');
  assert.equal(Certificacion.lastPercentage, 80, 'No debe sobrescribir la calificación');
});
