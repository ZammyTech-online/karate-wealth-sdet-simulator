// Torre de control de Nordvik: la web habla solo con la torre (/api/v1) y, para llamar a los servicios, con su pasarela /api/v1/proxy.
'use strict';

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const state = { levels: [], current: -1, services: [], domains: [], missions: [], traps: [], filter: 'all', busy: false };

// ---------- llamadas ----------
async function api(path, opts = {}) {
  const res = await fetch('/api/v1' + path, { headers: { 'Content-Type': 'application/json' }, ...opts });
  const type = res.headers.get('content-type') || '';
  const body = type.includes('json') ? await res.json() : await res.text();
  if (!res.ok) { const err = new Error(body?.detail || res.statusText); err.status = res.status; err.body = body; throw err; }
  return body;
}
const post = (path, data) => api(path, { method: 'POST', body: data === undefined ? undefined : JSON.stringify(data) });

function toast(text, bad = false) {
  const el = document.createElement('div');
  el.className = 'toast' + (bad ? ' bad' : '');
  el.textContent = text;
  $('#toasts').append(el);
  setTimeout(() => el.remove(), bad ? 7000 : 3800);
}
function fail(e) { toast((e.body?.detail || e.message) + (e.body?.hint ? ' · ' + e.body.hint : ''), true); }

async function busy(btn, fn) {
  if (btn) btn.disabled = true;
  try { await fn(); } catch (e) { fail(e); } finally { if (btn) btn.disabled = false; }
}

// ---------- modal ----------
function modal(title, html, actions) {
  $('#modal-title').textContent = title;
  $('#modal-body').innerHTML = html;
  const box = $('#modal-actions');
  box.innerHTML = '';
  for (const a of actions) {
    const b = document.createElement('button');
    b.className = 'btn ' + (a.kind || '');
    b.textContent = a.label;
    b.onclick = async () => { if (a.run) await busy(b, a.run); closeModal(); };
    box.append(b);
  }
  $('#modal').hidden = false;
  box.lastElementChild?.focus();
}
function closeModal() { $('#modal').hidden = true; }
$('#modal').addEventListener('click', (e) => { if (e.target.id === 'modal') closeModal(); });

// ---------- estado (barra superior) ----------
async function refreshStatus() {
  try {
    const st = await api('/estado');
    const lvl = $('#st-level');
    lvl.textContent = st.currentLevel >= 0 ? `Nivel ${st.currentLevel} completo` : 'Ningún nivel completo';
    lvl.className = 'pill ' + (st.currentLevel >= 0 ? 'ok' : 'warn');
    $('#st-services').innerHTML = `<span class="dot"></span>${st.running}/${st.total} servicios`;
    $('#st-services').className = 'pill ' + (st.running === st.total ? 'ok' : 'warn');
    $('#st-memory').textContent = `${st.memoryMb} MB de RAM`;
    $('#st-traps').textContent = st.trapsOn ? `${st.trapsOn} trampa${st.trapsOn > 1 ? 's' : ''} encendida${st.trapsOn > 1 ? 's' : ''}` : 'sin trampas';
    $('#st-traps').className = 'pill ' + (st.trapsOn ? 'bad' : 'off');
    state.runningSet = new Set(st.services);
    state.current = st.currentLevel;
  } catch { $('#st-services').textContent = 'torre sin respuesta'; $('#st-services').className = 'pill bad'; }
  try {
    const now = await api('/proxy/clock-service/api/v1/clock/now');
    const t = now.now.replace('T', ' ').slice(0, 19);
    $('#st-clock').textContent = `${t}${now.mode === 'FROZEN' ? ' · congelado' : ''}`;
    $('#st-clock').className = 'pill' + (now.mode === 'REAL' ? '' : ' warn');
    $('#clock-now').textContent = `${t} · ${now.timezone} · ${now.mode === 'REAL' ? 'hora real' : now.mode === 'FROZEN' ? 'congelado' : 'desplazado'}`;
  } catch { $('#st-clock').textContent = 'reloj apagado'; }
}

// ---------- escalera ----------
async function loadLevels() {
  const data = await api('/niveles');
  state.levels = data.levels;
  state.current = data.currentLevel;
  renderLadder();
}

function renderLadder() {
  $('#ladder').innerHTML = state.levels.map((l) => {
    const pct = l.exercises ? Math.round((l.solved / l.exercises) * 100) : 0;
    const lessons = l.lessons.length > 1 ? `${l.lessons[0].id}–${l.lessons[l.lessons.length - 1].id}` : l.lessons[0]?.id ?? '—';
    return `<li class="step lv${l.id} ${l.id === state.current ? 'current' : ''}">
      <div class="step-num"><b>${l.id}</b><span>${esc(l.name)}</span></div>
      <div class="step-body">
        <div class="step-head">
          <div class="step-title"><h2>${esc(l.title)}</h2><small>Perfil ${esc(l.profile)} · niveles ${esc(l.route)} del plan · lecciones ${lessons}</small></div>
          <span class="state ${l.state}">${l.state === 'ON' ? 'Encendido' : l.state === 'PARTIAL' ? `${l.running}/${l.services} encendidos` : 'Apagado'}</span>
        </div>
        <p>${esc(l.summary)}</p>
        <div class="stats">
          <div class="stat"><b>${l.services}</b><span>servicios (+${l.newServices} nuevos)</span></div>
          <div class="stat"><b>${l.lessons.length}</b><span>lecciones</span></div>
          <div class="stat"><b>${l.solved}/${l.exercises}</b><span>ejercicios resueltos</span><div class="progress" aria-hidden="true"><i style="width:${pct}%"></i></div></div>
          <div class="stat"><b>${l.missions.length}</b><span>misiones</span></div>
          <div class="stat"><b>${l.traps.length}</b><span>trampas nuevas</span></div>
        </div>
        <div class="cols">
          <div class="col"><h4>Karate</h4><ul>${l.karate.map((k) => `<li>${esc(k)}</li>`).join('')}</ul></div>
          <div class="col"><h4>Negocio</h4><ul>${l.business.map((k) => `<li>${esc(k)}</li>`).join('')}</ul></div>
          <div class="col"><h4>Se enciende</h4><div class="chips">${l.newDomains.map((d) => `<span class="chip lv">${esc(d.name)} · ${d.services}</span>`).join('')}${l.externals.map((x) => `<span class="chip">${esc(x)}</span>`).join('')}</div></div>
        </div>
        <div class="chips">${l.lessons.map((x) => `<span class="chip" title="${esc(x.title)}">${x.id}${x.exercises ? ` · ${x.solved}/${x.exercises}` : ''}</span>`).join('')}</div>
        <div class="checkpoint"><b>Checkpoint:</b> ${esc(l.checkpoint)}</div>
        <div class="step-actions">
          <button class="btn" data-level="${l.id}" data-mode="exacto">Encender solo este nivel</button>
          <button class="btn ghost" data-level="${l.id}" data-mode="sumar">Añadir a lo encendido</button>
          <button class="btn quiet" data-goto-missions="${l.id}">Ver sus ${l.missions.length} misiones</button>
        </div>
      </div>
    </li>`;
  }).join('');
}

$('#ladder').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  if (b.dataset.gotoMissions !== undefined) { state.filter = b.dataset.gotoMissions; show('misiones'); return; }
  const id = Number(b.dataset.level);
  const level = state.levels.find((l) => l.id === id);
  if (b.dataset.mode === 'sumar') return busy(b, () => activateLevel(id, 'sumar'));
  const running = state.runningSet?.size ?? 0;
  const off = Math.max(0, running - level.services);
  modal(`Encender solo el nivel ${id} · ${level.name}`,
    `<p>Quedarán encendidos <b>${level.services}</b> servicios.${off ? ` Se apagarán unos <b>${off}</b> que no son de este nivel (sus datos se conservan).` : ''}</p>`,
    [{ label: 'Cancelar', kind: 'quiet' }, { label: `Encender el nivel ${id}`, run: () => activateLevel(id, 'exacto') }]);
});

async function activateLevel(id, mode) {
  toast(`Preparando el nivel ${id}…`);
  const r = await post(`/niveles/${id}/encender?modo=${mode}`);
  const failed = Object.keys(r.failed || {});
  toast(`Nivel ${id}: ${r.started.length} encendidos, ${r.stopped.length} apagados · ${r.running} en marcha`);
  if (failed.length) toast('No arrancaron: ' + failed.join(', '), true);
  await refreshAll();
}

// ---------- misiones ----------
async function loadMissions() { state.missions = await api('/misiones'); renderMissions(); }

function renderMissions() {
  const levels = [{ id: 'all', label: 'Todas' }, ...state.levels.map((l) => ({ id: String(l.id), label: `Nivel ${l.id} · ${l.name}` }))];
  $('#mission-filters').innerHTML = levels.map((l) => `<button data-filter="${l.id}" aria-pressed="${state.filter === l.id}">${esc(l.label)}</button>`).join('');
  const list = state.missions.filter((m) => state.filter === 'all' || String(m.level) === state.filter);
  $('#missions').innerHTML = list.map((m) => `<article class="mission lv${m.level}">
      <div class="mission-head"><h3>${esc(m.title)}</h3><span class="mission-id">${m.id} <span class="lvbadge">N${m.level}</span></span></div>
      <p>${esc(m.story)}</p>
      <div class="chips">
        ${m.lessons.map((l) => `<span class="chip">Lección ${l}</span>`).join('')}
        ${m.flow ? `<button class="chip" data-flow="${esc(m.flow)}">Flujo ${esc(m.flow.slice(0, 4))}</button>` : ''}
        ${m.traps.map((t) => `<span class="chip">Trampa ${t}</span>`).join('')}
      </div>
      <details class="steps"><summary>${m.steps.length} pasos · ${m.keyServices.length} servicios clave</summary>
        <ol class="steplist">${m.steps.map((s, i) => `<li>
          <div class="endpoint"><code><span class="method ${s.method}">${s.method}</span>${esc(s.path)}</code><small>${esc(s.service)} · puerto ${s.port} — ${esc(s.what)}</small></div>
          ${s.method === 'GET' && !s.path.includes('{') ? `<button class="btn small ghost" data-try="${m.id}:${i}">Probar</button>` : `<a class="btn small quiet" href="http://localhost:${s.port}/swagger" target="_blank" rel="noopener">Swagger</a>`}
        </li>`).join('')}</ol>
      </details>
      <div class="mission-foot">
        <span class="ready ${m.ready ? 'yes' : 'no'}">${m.ready ? 'Lista: todo encendido' : `Faltan ${m.missing.length} de ${m.needed} servicios`}</span>
        <button class="btn ${m.ready ? 'quiet' : ''}" data-prepare="${m.id}">${m.ready ? 'Preparada' : 'Preparar'}</button>
      </div>
    </article>`).join('') || '<p class="muted">No hay misiones en este nivel.</p>';
}

$('#mission-filters').addEventListener('click', (e) => {
  const b = e.target.closest('button[data-filter]');
  if (b) { state.filter = b.dataset.filter; renderMissions(); }
});

$('#missions').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  if (b.dataset.prepare) return busy(b, async () => {
    const r = await post(`/misiones/${b.dataset.prepare}/preparar`);
    toast(r.started.length ? `${r.mission}: encendidos ${r.started.length} servicios (${r.started.slice(0, 4).join(', ')}${r.started.length > 4 ? '…' : ''})` : `${r.mission}: ya estaba todo encendido`);
    await refreshAll();
  });
  if (b.dataset.flow) return busy(b, () => openFlow(b.dataset.flow));
  if (b.dataset.try) {
    const [id, i] = b.dataset.try.split(':');
    const step = state.missions.find((m) => m.id === id).steps[Number(i)];
    return busy(b, () => tryCall(step.service, step.path));
  }
});

async function tryCall(service, path) {
  const res = await fetch(`/api/v1/proxy/${service}${path}`);
  const text = await res.text();
  let pretty = text;
  try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch { /* no es JSON */ }
  modal(`GET ${path}`, `<p class="muted">${esc(service)} respondió <b>${res.status}</b> · ${esc(res.headers.get('content-type') || '')}</p><pre class="response">${esc(pretty.slice(0, 12000))}</pre>`, [{ label: 'Cerrar', kind: 'quiet' }]);
}

async function openFlow(file) {
  const md = await api('/flujos/' + encodeURIComponent(file));
  modal(file.replace('.md', ''), `<div class="md">${markdown(md)}</div>`, [{ label: 'Cerrar', kind: 'quiet' }]);
}

// Markdown mínimo para los flujos: títulos, listas, tablas, bloques de código y negrita/código en línea.
function markdown(src) {
  const inline = (s) => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>');
  const out = []; const lines = src.split('\n'); let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (line.startsWith('```')) { const buf = []; i++; while (i < lines.length && !lines[i].startsWith('```')) buf.push(lines[i++]); i++; out.push(`<pre><code>${esc(buf.join('\n'))}</code></pre>`); continue; }
    const h = line.match(/^(#{1,3})\s+(.*)/);
    if (h) { out.push(`<h${h[1].length}>${inline(h[2])}</h${h[1].length}>`); i++; continue; }
    if (line.startsWith('|')) {
      const rows = []; while (i < lines.length && lines[i].startsWith('|')) rows.push(lines[i++]);
      const cells = (r) => r.split('|').slice(1, -1).map((c) => c.trim());
      const body = rows.slice(2).map((r) => `<tr>${cells(r).map((c) => `<td>${inline(c)}</td>`).join('')}</tr>`).join('');
      out.push(`<div class="table-wrap"><table><thead><tr>${cells(rows[0]).map((c) => `<th>${inline(c)}</th>`).join('')}</tr></thead><tbody>${body}</tbody></table></div>`);
      continue;
    }
    if (/^\s*([-*]|\d+\.)\s/.test(line)) {
      const items = []; while (i < lines.length && /^\s*([-*]|\d+\.)\s/.test(lines[i])) items.push(lines[i++].replace(/^\s*([-*]|\d+\.)\s/, ''));
      out.push(`<ul>${items.map((x) => `<li>${inline(x)}</li>`).join('')}</ul>`); continue;
    }
    if (line.trim()) out.push(`<p>${inline(line)}</p>`);
    i++;
  }
  return out.join('');
}

// ---------- mapa ----------
async function loadMap() {
  const [svc, dom] = await Promise.all([api('/servicios'), api('/dominios')]);
  state.services = svc.items; state.domains = dom;
  renderMap();
  fillFaultServices();
}

function renderMap() {
  const q = $('#map-search').value.trim().toLowerCase();
  const max = $('#map-level').value === '' ? 9 : Number($('#map-level').value);
  const onlyOn = $('#map-on').checked;
  $('#domains').innerHTML = state.domains.map((d) => {
    const rows = state.services.filter((s) => s.domain === d.code)
      .filter((s) => !q || s.name.includes(q) || s.description.toLowerCase().includes(q))
      .filter((s) => !onlyOn || s.running);
    if (!rows.length) return '';
    const allOn = d.running === d.services;
    return `<section class="domain">
      <div class="domain-head">
        <h3><span class="letter">${esc(d.code.slice(0, 1))}</span>${esc(d.name)}</h3>
        <span class="count">${d.running}/${d.services}</span>
        <label class="switch" title="${allOn ? 'Apagar' : 'Encender'} el dominio"><input type="checkbox" data-domain="${esc(d.code)}" ${allOn ? 'checked' : ''} aria-label="Dominio ${esc(d.name)}"><span></span></label>
      </div>
      ${rows.map((s) => `<div class="svc ${s.level > max ? 'dim' : ''}">
        <span class="dot ${s.running ? 'on' : ''}" title="${s.running ? 'encendido' : 'apagado'}"></span>
        <button class="name" data-open="${esc(s.name)}">${esc(s.name)}</button>
        <span class="port"><span class="lvbadge lv${s.level}">N${s.level}</span> ${s.port}</span>
        <label class="switch"><input type="checkbox" data-service="${esc(s.name)}" ${s.running ? 'checked' : ''} ${s.core ? 'disabled title="El reloj no se apaga"' : ''} aria-label="${esc(s.name)}"><span></span></label>
      </div>`).join('')}
    </section>`;
  }).join('') || '<p class="muted">Ningún servicio coincide.</p>';
}
['#map-search', '#map-level', '#map-on'].forEach((s) => $(s).addEventListener('input', renderMap));

$('#domains').addEventListener('click', (e) => { const b = e.target.closest('button[data-open]'); if (b) openService(b.dataset.open); });
$('#domains').addEventListener('change', (e) => {
  const input = e.target;
  if (input.dataset.service) toggleService(input.dataset.service, input.checked, input);
  if (input.dataset.domain) toggleDomain(input.dataset.domain, input.checked, input);
});

async function toggleService(name, on, input) {
  try {
    if (on) { const r = await post(`/servicios/${name}/encender`); toast(`Encendidos: ${r.started.join(', ') || name}`); }
    else await post(`/servicios/${name}/apagar`).then(() => toast(`${name} apagado`));
  } catch (e) {
    if (e.body?.code === 'HAS_RUNNING_DEPENDENTS') {
      if (input) input.checked = true;
      return modal(`Otros servicios necesitan ${name}`,
        `<p>Si lo apagas, estos ${e.body.dependents.length} servicios encendidos se quedarían sin él:</p><div class="chips">${e.body.dependents.map((d) => `<span class="chip">${esc(d)}</span>`).join('')}</div>`,
        [{ label: 'Dejarlo encendido', kind: 'quiet' }, { label: `Apagar los ${e.body.dependents.length + 1}`, kind: 'danger', run: async () => { const r = await post(`/servicios/${name}/apagar?cascada=true`); toast(`Apagados ${r.stopped.length} servicios`); await refreshAll(); } }]);
    }
    fail(e);
  }
  await refreshAll();
}

async function toggleDomain(code, on, input) {
  try {
    const r = on ? await post(`/dominios/${code}/encender`) : await post(`/dominios/${code}/apagar`);
    toast(on ? `Dominio encendido: ${r.started.length} servicios nuevos` : `Dominio apagado: ${r.stopped.length} servicios`);
  } catch (e) {
    if (e.body?.code === 'HAS_RUNNING_DEPENDENTS') {
      if (input) input.checked = true;
      return modal('Otros dominios dependen de este',
        `<p>Estos servicios encendidos de otros dominios lo necesitan:</p><div class="chips">${e.body.dependents.map((d) => `<span class="chip">${esc(d)}</span>`).join('')}</div>`,
        [{ label: 'Cancelar', kind: 'quiet' }, { label: 'Apagar también esos', kind: 'danger', run: async () => { await post(`/dominios/${code}/apagar?cascada=true`); await refreshAll(); } }]);
    }
    fail(e);
  }
  await refreshAll();
}

// ---------- ficha de un servicio ----------
async function openService(name) {
  const d = await api('/servicios/' + encodeURIComponent(name));
  const s = d.service;
  $('#d-domain').textContent = `${s.domainName} · nivel ${s.level}${s.external ? ' · sistema externo' : ''}`;
  $('#d-name').textContent = s.name;
  const chipList = (arr, empty) => arr.length ? `<div class="chips">${arr.map((x) => `<button class="chip" data-open="${esc(x.name)}"><span class="dot ${x.running ? 'on' : ''}" style="width:8px;height:8px;border-radius:50%;background:${x.running ? 'var(--ok)' : 'var(--off)'}"></span>${esc(x.name)}</button>`).join('')}</div>` : `<p class="muted">${empty}</p>`;
  $('#d-body').innerHTML = `
    <p>${esc(s.description)}</p>
    <div class="row wrap">
      <span class="state ${s.running ? 'ON' : 'OFF'}">${s.running ? 'Encendido' : 'Apagado'}</span>
      <button class="btn small" data-toggle="${esc(s.name)}" data-on="${!s.running}" ${s.core ? 'disabled' : ''}>${s.running ? 'Apagar' : 'Encender'}</button>
      <a class="btn small ghost" href="${esc(d.swagger)}" target="_blank" rel="noopener">Abrir Swagger</a>
      <button class="btn small quiet" data-health="${esc(s.name)}">Probar /health</button>
    </div>
    <dl class="kv">
      <dt>Puerto</dt><dd class="mono">${s.port}</dd>
      <dt>Equipo</dt><dd>${esc(d.info.team)}</dd>
      <dt>Oleada</dt><dd>${s.wave}</dd>
      <dt>JSON</dt><dd>${esc(d.info.naming)}</dd>
      ${d.info.emits.length ? `<dt>Publica</dt><dd class="mono">${d.info.emits.map(esc).join(', ')}</dd>` : ''}
      ${d.info.consumes.length ? `<dt>Escucha</dt><dd class="mono">${d.info.consumes.map(esc).join(', ')}</dd>` : ''}
    </dl>
    <div class="section"><h4>Depende de</h4>${chipList(d.dependsOn, 'No depende de nadie más que del reloj.')}</div>
    ${d.optionalDependencies.length ? `<div class="section"><h4>Usa si están encendidos</h4>${chipList(d.optionalDependencies, '')}<p class="muted">Si están apagados, tiene un plan B (por ejemplo, una retención provisional).</p></div>` : ''}
    <div class="section"><h4>Lo necesitan</h4>${chipList(d.dependents, 'Ningún servicio depende de este: puedes apagarlo sin miedo.')}</div>
    <div class="section"><h4>Lecciones que lo usan</h4>${d.lessons.length ? `<div class="chips">${d.lessons.map((l) => `<span class="chip" title="${esc(l.title)}">${l.id}</span>`).join('')}</div>` : '<p class="muted">Ninguna lección todavía: practícalo con una misión o desde Swagger.</p>'}</div>
    <div class="section"><h4>Misiones</h4>${d.missions.length ? `<div class="chips">${d.missions.map((m) => `<span class="chip">${m.id} · ${esc(m.title)}</span>`).join('')}</div>` : '<p class="muted">No sale en ninguna misión.</p>'}</div>
    <div class="section"><h4>${d.endpoints.filter((e) => !e.path.startsWith('/_test')).length} endpoints</h4><ul class="eplist">${d.endpoints.filter((e) => !e.path.startsWith('/_test')).map((e) => `<li><span class="method ${e.method}">${e.method}</span><span><code>${esc(e.path)}</code>${e.summary ? `<br><small class="muted">${esc(e.summary)}</small>` : ''}</span></li>`).join('')}</ul></div>`;
  $('#drawer').hidden = false; $('#scrim').hidden = false;
}
function closeDrawer() { $('#drawer').hidden = true; $('#scrim').hidden = true; }
$('#drawer-close').onclick = closeDrawer;
$('#scrim').onclick = closeDrawer;
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') { closeModal(); closeDrawer(); } });
$('#d-body').addEventListener('click', async (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  if (b.dataset.open) return openService(b.dataset.open);
  if (b.dataset.health) return busy(b, () => tryCall(b.dataset.health, '/health'));
  if (b.dataset.toggle) { await toggleService(b.dataset.toggle, b.dataset.on === 'true'); if ($('#modal').hidden) openService(b.dataset.toggle); }
});

// ---------- laboratorio ----------
const clockPost = (path, body) => post('/proxy/clock-service/api/v1/clock/' + path, body);
$('#clock-freeze').onclick = (e) => busy(e.target, async () => { await clockPost('freeze', { at: $('#clock-at').value }); toast('Reloj congelado'); refreshStatus(); });
$('#clock-reset').onclick = (e) => busy(e.target, async () => { await clockPost('reset'); toast('Hora real'); refreshStatus(); });
document.querySelectorAll('[data-advance]').forEach((b) => (b.onclick = () => busy(b, async () => { await clockPost('advance', JSON.parse(b.dataset.advance)); refreshStatus(); })));
document.querySelectorAll('[data-freeze]').forEach((b) => (b.onclick = () => busy(b, async () => { await clockPost('freeze', { at: b.dataset.freeze }); toast('Reloj en ' + b.dataset.freeze.replace('T', ' ')); refreshStatus(); })));

async function loadTraps() {
  state.traps = await api('/trampas');
  $('#traps').innerHTML = state.traps.map((t) => `<tr>
    <td class="id">${t.id}</td><td><b>${esc(t.name)}</b><br><small class="muted">${esc(t.howToFind)}</small></td><td>${esc(t.symptom)}</td>
    <td><span class="lvbadge lv${t.level}">N${t.level}</span></td><td><span class="nature ${t.nature}">${t.nature}</span></td>
    <td>${t.nature === 'ACTIVABLE' ? `<label class="switch"><input type="checkbox" data-trap="${t.id}" ${t.enabled ? 'checked' : ''} aria-label="Trampa ${t.id}"><span></span></label>` : '<span class="muted">siempre</span>'}</td>
  </tr>`).join('');
}
$('#traps').addEventListener('change', async (e) => {
  const id = e.target.dataset.trap;
  if (!id) return;
  try { await api(`/proxy/chaos-control/api/v1/traps/${id}`, { method: 'PUT', body: JSON.stringify({ enabled: e.target.checked }) }); toast(`Trampa ${id} ${e.target.checked ? 'encendida' : 'apagada'}`); }
  catch (err) { e.target.checked = !e.target.checked; fail(err); }
  refreshStatus();
});
$('#traps-reset').onclick = (e) => busy(e.target, async () => { await post('/proxy/chaos-control/api/v1/reset'); toast('Trampas y fallos apagados'); await Promise.all([loadTraps(), loadFaults(), refreshStatus()]); });

function fillFaultServices() {
  const sel = $('#fault-service'); const current = sel.value;
  sel.innerHTML = state.services.filter((s) => s.running && !s.external).map((s) => `<option ${s.name === current ? 'selected' : ''}>${esc(s.name)}</option>`).join('');
}
async function loadFaults() {
  try {
    const r = await api('/proxy/chaos-control/api/v1/faults');
    const items = r.items ?? r;
    $('#faults').innerHTML = items.length ? items.map((f) => `<li><span><b>${esc(f.service)}</b> <code>${esc(f.pathPrefix || '/')}</code> → ${f.status || 'latencia'}${f.delayMs ? ` · ${f.delayMs} ms` : ''}${f.remaining != null ? ` · quedan ${f.remaining}` : ''}</span><button class="btn small quiet" data-fault="${esc(f.id)}">Quitar</button></li>`).join('') : '<li class="muted">Ningún fallo activo.</li>';
  } catch { $('#faults').innerHTML = '<li class="muted">chaos-control está apagado: enciende el nivel 3.</li>'; }
}
$('#faults').addEventListener('click', (e) => { const b = e.target.closest('button[data-fault]'); if (b) busy(b, async () => { await api(`/proxy/chaos-control/api/v1/faults/${b.dataset.fault}`, { method: 'DELETE' }); loadFaults(); }); });
$('#fault-form').addEventListener('submit', (e) => {
  e.preventDefault();
  const num = (id) => ($(id).value === '' ? undefined : Number($(id).value));
  const body = { service: $('#fault-service').value, pathPrefix: $('#fault-path').value || undefined, status: num('#fault-status'), delayMs: num('#fault-delay'), times: num('#fault-times'), probability: num('#fault-prob') };
  if (!body.status && !body.delayMs) body.status = 503;
  busy(e.submitter, async () => { await post('/proxy/chaos-control/api/v1/faults', body); toast(`Fallo añadido a ${body.service}`); loadFaults(); });
});

// ---------- navegación ----------
const views = ['escalera', 'misiones', 'mapa', 'laboratorio', 'guia'];
function show(view) {
  if (!views.includes(view)) view = 'escalera';
  views.forEach((v) => { $('#view-' + v).hidden = v !== view; });
  document.querySelectorAll('.tabs button').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.view === view)));
  if (!location.hash.includes(':') && location.hash !== '#' + view) history.replaceState(null, '', '#' + view);
  if (view === 'misiones') renderMissions();
  if (view === 'laboratorio') { loadTraps(); loadFaults(); }
}
document.querySelector('.tabs').addEventListener('click', (e) => { const b = e.target.closest('button[data-view]'); if (b) show(b.dataset.view); });
// Enlaces directos: #ficha:product-catalog abre la ficha de un servicio y #flujo:F-07-traspaso.md un flujo (útil para la guía y para compartir)
function route() {
  const h = decodeURIComponent(location.hash.slice(1));
  if (h.startsWith('ficha:')) { show('mapa'); openService(h.slice(6)).catch(fail); return; }
  if (h.startsWith('flujo:')) { show('misiones'); openFlow(h.slice(6)).catch(fail); return; }
  show(h);
}
window.addEventListener('hashchange', route);

async function refreshAll() {
  await refreshStatus();
  await Promise.all([loadLevels(), loadMissions(), loadMap()]).catch(fail);
}

refreshAll().then(route);
setInterval(refreshStatus, 5000);
