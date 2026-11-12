// tests/bruno.test.mjs: Suite de pruebas unitarias y de integración para el copiloto SDET Bruno.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs, { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));

// Cargar módulos de Bruno
const loadScript = (filename) => {
  const code = readFileSync(resolve(__dirname, '../torre/web', filename), 'utf-8');
  const context = { module: { exports: {} }, exports: {} };
  const fn = new Function('module', 'exports', code);
  fn(context.module, context.exports);
  return context.module.exports;
};

const { BrunoAvatar } = loadScript('bruno-avatar.js');
const { BrunoHeuristics } = loadScript('bruno-heuristics.js');
const { BrunoLinter } = loadScript('bruno-linter.js');
const { BrunoBridge } = loadScript('bruno-bridge.js');
const { BrunoAudioPlayer } = loadScript('bruno-audio-player.js');

// ============================================================================
// 1. Tests de BrunoAvatar (Stickman SVG Vectorial & Estados Reactivos)
// ============================================================================
test('BrunoAvatar: renderiza SVG válido en todos sus 5 estados', () => {
  const states = ['IDLE', 'THINKING', 'EXPLAINING', 'ALERT', 'SUCCESS'];

  for (const st of states) {
    const svg = BrunoAvatar.render(st, 100);
    assert.ok(svg.includes('<svg'), `El estado ${st} debe contener etiqueta <svg`);
    assert.ok(svg.includes('bruno-avatar'), `El estado ${st} debe incluir la clase bruno-avatar`);
    assert.ok(svg.includes(`bruno-${st.toLowerCase()}`), `El estado ${st} debe incluir la clase bruno-${st.toLowerCase()}`);
    // Stickman identity: cabeza blanca, camiseta negra, gafas cyan/glow
    assert.ok(svg.includes('stroke="#06b6d4"') || svg.includes('stroke="#22d3ee"') || svg.includes('stroke="#ef4444"') || svg.includes('stroke="#10b981"'), `El estado ${st} debe incluir montura de gafas reactivas`);
    assert.ok(svg.includes('fill="#0f172a"'), `El estado ${st} debe incluir la camiseta negra`);
  }
});

test('BrunoAvatar: fallback a IDLE ante estado no reconocido', () => {
  const svg = BrunoAvatar.render('UNKNOWN_STATE', 80);
  assert.ok(svg.includes('bruno-idle'), 'Debe aplicar fallback a bruno-idle');
});

// ============================================================================
// 2. Tests de BrunoHeuristics (Diagnóstico de Fallos 0 Tokens)
// ============================================================================
test('BrunoHeuristics: retorno limpio ante ausencia de errores', () => {
  const res = BrunoHeuristics.diagnose('');
  assert.equal(res.status, 'OK');
  assert.equal(res.avatarState, 'SUCCESS');
});

test('BrunoHeuristics: diagnostica ReferenceError de variable no definida', () => {
  const error = `
    org.graalvm.polyglot.PolyglotException: ReferenceError: "contractId" is not defined
    - <js>.:program(Unnamed:1)
  `;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.equal(res.avatarState, 'ALERT');
  assert.ok(res.title.includes('contractId'));
  assert.ok(res.hint.includes('* def contractId'));
});

test('BrunoHeuristics: diagnostica comparación numérica inválida con match', () => {
  const error = `syntax error, expected '==' for match`;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.equal(res.avatarState, 'EXPLAINING');
  assert.ok(res.title.includes('comparación numérica'));
  assert.ok(res.hint.includes('assert'));
});

test('BrunoHeuristics: diagnostica HTTP 401 Unauthorized', () => {
  const error = `status: 401, response: {"title":"Unauthorized","status":401}`;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.equal(res.avatarState, 'ALERT');
  assert.ok(res.title.includes('401 Unauthorized'));
  assert.ok(res.hint.includes('_login.feature'));
});

test('BrunoHeuristics: diagnostica HTTP 403 Forbidden (RBAC)', () => {
  const error = `status: 403, response: {"title":"Forbidden","detail":"Missing role: ACTUARY_SIGNER"}`;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.ok(res.title.includes('403 Forbidden'));
});

test('BrunoHeuristics: diagnostica HTTP 409 Conflict (Saga de Pensiones)', () => {
  const error = `status: 409, response: {"title":"Conflict","detail":"Transfer already IN_PROGRESS"}`;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.ok(res.title.includes('409 Conflict'));
  assert.ok(res.hint.includes('saga de pensión') || res.hint.includes('Idempotency-Key'));
});

test('BrunoHeuristics: diagnostica FileNotFoundException en llamada a feature', () => {
  const error = `java.io.FileNotFoundException: src/test/java/helpers/not_found.feature`;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.ok(res.title.includes('no encontrado'));
  assert.ok(res.hint.includes('classpath:'));
});

test('BrunoHeuristics: diagnostica BindException de puerto en colisión', () => {
  const error = `java.net.BindException: Address already in use: bind`;
  const res = BrunoHeuristics.diagnose(error);
  assert.equal(res.status, 'ERROR');
  assert.ok(res.title.includes('Agotamiento de sockets') || res.title.includes('TCP'));
  assert.ok(res.hint.includes('-Dkarate.threads=1'));
});

// ============================================================================
// 3. Tests de BrunoLinter (Inspector Estático de Calidad SDET)
// ============================================================================
test('BrunoLinter: detecta anti-patrón de sleep()', () => {
  const code = `
    Feature: Orden de Inversión
    Scenario: Esperar ejecución
      * karate.sleep(5000)
  `;
  const issues = BrunoLinter.audit(code);
  assert.equal(issues.length, 1);
  assert.equal(issues[0].rule, 'ANTI_PATTERN_SLEEP');
  assert.equal(issues[0].level, 'CRITICAL');
  assert.ok(issues[0].solution.includes('retry until'));
});

test('BrunoLinter: detecta número personal sueco hardcodeado (PNR)', () => {
  const code = `
    Scenario: Buscar cliente
      * def pnr = "19850412-1234"
  `;
  const issues = BrunoLinter.audit(code);
  assert.equal(issues.length, 1);
  assert.equal(issues[0].rule, 'HARDCODED_PNR');
  assert.equal(issues[0].level, 'WARNING');
  assert.ok(issues[0].solution.includes('_persona.feature'));
});

test('BrunoLinter: detecta IBAN estático hardcodeado', () => {
  const code = `
    Scenario: Liquidación
      * def account = "SE4550000000058694246801"
  `;
  const issues = BrunoLinter.audit(code);
  assert.equal(issues.length, 1);
  assert.equal(issues[0].rule, 'HARDCODED_IBAN');
  assert.equal(issues[0].level, 'WARNING');
  assert.ok(issues[0].solution.includes('_iban.feature'));
});

test('BrunoLinter: detecta comparación inválida con match <', () => {
  const code = `
    Scenario: Latencia de API
      * match response.latency < 250
  `;
  const issues = BrunoLinter.audit(code);
  assert.equal(issues.length, 1);
  assert.equal(issues[0].rule, 'INVALID_MATCH_COMPARISON');
  assert.equal(issues[0].level, 'CRITICAL');
  assert.ok(issues[0].solution.includes('assert'));
});

test('BrunoLinter: aprueba código limpio sin advertencias', () => {
  const code = `
    Feature: Consulta de Fondos
    Background:
      * url baseUrl
      * def auth = call read('this:_login.feature')
    Scenario: Listado de fondos ESG
      Given path '/api/v1/funds'
      And headers auth.headers
      When method get
      Then status 200
      And match response.items == '#[]'
  `;
  const issues = BrunoLinter.audit(code);
  assert.equal(issues.length, 0, 'No debe emitir advertencias en código idiomático');
});

// ============================================================================
// 4. Tests de BrunoBridge (Conmutación Local vs IA Desacoplada)
// ============================================================================
test('BrunoBridge: conmutación de modo e inferencia local', async () => {
  BrunoBridge.setMode('LOCAL');
  assert.equal(BrunoBridge.getMode(), 'LOCAL');

  const answer1 = await BrunoBridge.ask('¿Por qué no debo usar sleep?');
  assert.equal(answer1.source, 'LOCAL');
  assert.ok(answer1.answer.includes('retry until'));

  const answer2 = await BrunoBridge.ask('¿Cómo obtengo un token auth?');
  assert.equal(answer2.source, 'LOCAL');
  assert.ok(answer2.answer.includes('_login.feature'));
});

test('BrunoBridge: modo IA desacoplado con endpoint en vivo o fallback elegante', async () => {
  BrunoBridge.setMode('AI');
  assert.equal(BrunoBridge.getMode(), 'AI');

  // En entorno Node.js sin navegador, fetch a ruta relativa /api/... fallará o necesitará URL absoluta
  const answer = await BrunoBridge.ask('¿Cómo estructuro mis tests?');
  // Debe haber respondido con IA o fallback local sin lanzar excepción no capturada
  assert.ok(answer.answer.length > 0);
  assert.ok(answer.source === 'AI' || answer.source === 'LOCAL');
});

// ============================================================================
// 5. Tests de BrunoAudioPlayer y Metadatos de ElevenLabs
// ============================================================================
test('BrunoAudioPlayer: formateo de tiempo y escape HTML', () => {
  assert.equal(BrunoAudioPlayer.formatTime(0), '00:00');
  assert.equal(BrunoAudioPlayer.formatTime(65), '01:05');
  assert.equal(BrunoAudioPlayer.formatTime(103.5), '01:43');
  assert.equal(BrunoAudioPlayer.escape('<script>alert("xss")</script>'), '&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;');
});

test('Archivos de Audio ElevenLabs: L00, L01 y L02 presentes y conformes', () => {
  const audioDir = resolve(__dirname, '../torre/web/audio');
  const requiredLessons = ['L00', 'L01', 'L02'];

  for (const lid of requiredLessons) {
    const mp3 = resolve(audioDir, `${lid}.mp3`);
    const json = resolve(audioDir, `${lid}.json`);

    assert.ok(fs.existsSync(mp3), `El archivo ${lid}.mp3 debe existir en disco`);
    assert.ok(fs.existsSync(json), `El archivo ${lid}.json debe existir en disco`);

    const stats = fs.statSync(mp3);
    assert.ok(stats.size > 100 * 1024, `${lid}.mp3 debe superar 100 KB (tamaño real: ${stats.size} bytes)`);

    const meta = JSON.parse(fs.readFileSync(json, 'utf8'));
    assert.equal(meta.lessonId, lid);
    assert.equal(meta.language, 'en');
    assert.ok(meta.duration > 20, `Duración debe ser mayor a 20s (obtenida: ${meta.duration}s)`);
    assert.ok(Array.isArray(meta.words), 'Debe contener arreglo de palabras');
    assert.ok(meta.words.length > 50, `Debe contener más de 50 palabras (obtenidas: ${meta.words.length})`);
    
    // Verificar estructura de timestamps por palabra
    const firstWord = meta.words[0];
    assert.ok('word' in firstWord);
    assert.ok('start' in firstWord);
    assert.ok('end' in firstWord);
    assert.ok(firstWord.end >= firstWord.start);

    // Verificar presencia de retos predictivos
    assert.ok(Array.isArray(meta.challenges), 'Debe incluir retos predictivos');
    assert.ok(meta.challenges.length >= 1, 'Debe incluir al menos 1 reto predictivo');
    assert.ok(meta.challenges[0].time > 0);
    assert.ok(meta.challenges[0].options.length >= 2);
  }
});
