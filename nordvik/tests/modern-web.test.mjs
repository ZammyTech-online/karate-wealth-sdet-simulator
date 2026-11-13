// tests/modern-web.test.mjs: Suite de verificación del estándar Modern Web Guidance (Google Chrome & Baseline Platform)
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const webDir = resolve(__dirname, '../torre/web');

const htmlContent = readFileSync(resolve(webDir, 'index.html'), 'utf-8');
const cssContent = readFileSync(resolve(webDir, 'styles.css'), 'utf-8');
const jsContent = readFileSync(resolve(webDir, 'app.js'), 'utf-8');

// ============================================================================
// 1. Verificación de HTML Semántico y APIs Nativas (Google Chrome Standard)
// ============================================================================
test('HTML: declara meta color-scheme light dark para prevenir FOUC', () => {
  assert.ok(htmlContent.includes('<meta name="color-scheme" content="light dark">'), 'Debe incluir meta color-scheme light dark');
});

test('HTML: modal y drawer implementados con <dialog> nativo sin antipatrones ARIA', () => {
  // Modal nativo
  assert.ok(htmlContent.includes('<dialog class="modal" id="modal"'), 'Debe usar elemento <dialog> para el modal');
  assert.ok(!htmlContent.includes('<div class="modal" id="modal"'), 'No debe usar <div> para el modal');
  assert.ok(!htmlContent.includes('role="dialog"'), 'El elemento <dialog> no debe llevar role="dialog" redundante');
  assert.ok(!htmlContent.includes('aria-modal="true"'), 'El elemento <dialog> no debe llevar aria-modal="true" redundante');

  // Drawer nativo
  assert.ok(htmlContent.includes('<dialog class="drawer" id="drawer"'), 'Debe usar elemento <dialog> para el drawer lateral');
  assert.ok(!htmlContent.includes('<aside class="drawer" id="drawer" hidden'), 'No debe usar <aside hidden> como modal drawer');

  // Eliminación de scrims manuales
  assert.ok(!htmlContent.includes('id="scrim"'), 'No debe existir elemento manual <div id="scrim">');
});

test('HTML: incluye indicador de progreso de scroll nativo', () => {
  assert.ok(htmlContent.includes('<div id="scroll-progress" aria-hidden="true"></div>'), 'Debe existir barra de scroll progress');
});

// ============================================================================
// 2. Verificación de CSS Moderno: OKLCH, light-dark(), Container Queries y Scroll
// ============================================================================
test('CSS: adopta color-scheme y la función nativa light-dark() con tokens OKLCH', () => {
  assert.ok(cssContent.includes('color-scheme: light dark;'), 'Debe declarar color-scheme: light dark en :root');
  assert.ok(cssContent.includes('light-dark('), 'Debe emplear la función nativa light-dark()');
  assert.ok(cssContent.includes('oklch('), 'Debe emplear el espacio perceptual oklch()');
  assert.ok(cssContent.includes('accent-color: var(--accent);'), 'Debe personalizar accent-color');
  assert.ok(cssContent.includes('scrollbar-color:'), 'Debe personalizar scrollbar-color');
});

test('CSS: modal y drawer estilizan ::backdrop nativo con sobre-desplazamiento contenido', () => {
  assert.ok(cssContent.includes('dialog:modal'), 'Debe definir estilos para dialog:modal');
  assert.ok(cssContent.includes('dialog:modal::backdrop') || cssContent.includes('dialog::backdrop'), 'Debe definir estilos para ::backdrop nativo');
  assert.ok(cssContent.includes('overscroll-behavior: contain;'), 'Debe usar overscroll-behavior: contain para bloquear scroll chaining');
  assert.ok(!cssContent.includes('.scrim {'), 'No debe incluir clase .scrim legacy');
});

test('CSS: Container Queries (@container) para componentes desacoplados', () => {
  assert.ok(cssContent.includes('container-type: inline-size;'), 'Debe definir contenedores con container-type: inline-size');
  assert.ok(cssContent.includes('@container step-card'), 'Debe definir container query para tarjetas de escalera');
  assert.ok(cssContent.includes('@container mission-card'), 'Debe definir container query para misiones');
  assert.ok(cssContent.includes('@container gym-box'), 'Debe definir container query para el gym layout');
});

test('CSS: Animaciones Scroll-Driven (CSS Scroll-Driven Animations)', () => {
  assert.ok(cssContent.includes('animation-timeline: scroll();'), 'Debe usar animación ligada a timeline de scroll nativa');
  assert.ok(cssContent.includes('@keyframes grow-progress'), 'Debe definir animación keyframes grow-progress');
});

// ============================================================================
// 3. Verificación de JavaScript Moderno: Top Layer APIs, Intl, Cero Hacks
// ============================================================================
test('JS: modal y drawer invocan showModal() y close() nativos', () => {
  assert.ok(jsContent.includes('.showModal()'), 'Debe invocar showModal() para promover a Top Layer');
  assert.ok(jsContent.includes('.close()'), 'Debe invocar close() nativo');
  assert.ok(!jsContent.includes("$('#scrim')"), 'No debe referenciar ni manipular #scrim');
});

test('JS: Bruno Copilot implementa Popover API nativa en Top Layer', () => {
  assert.ok(htmlContent.includes('popover="manual"'), 'El dock de Bruno debe usar popover="manual"');
  assert.ok(jsContent.includes('showPopover()'), 'Debe usar showPopover() nativo');
  assert.ok(cssContent.includes('[popover]'), 'Debe definir estilos para popover');
});

test('JS: Transiciones de vistas nativas con View Transitions API', () => {
  assert.ok(jsContent.includes('document.startViewTransition'), 'Debe usar document.startViewTransition() para navegación SPA');
  assert.ok(jsContent.includes('prefers-reduced-motion'), 'Debe respetar la preferencia de reducción de movimiento');
});

test('JS: formateo de fechas y horas con Intl.DateTimeFormat en lugar de manipulación de strings', () => {
  assert.ok(jsContent.includes('Intl.DateTimeFormat'), 'Debe usar Intl.DateTimeFormat para formatear fechas de reloj');
});
