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
function fail(e) { toast((e.body?.detail || e.message) + (e.body?.hint ? ' Â· ' + e.body.hint : ''), true); }

async function busy(btn, fn) {
  if (btn) btn.disabled = true;
  try { await fn(); } catch (e) { fail(e); } finally { if (btn) btn.disabled = false; }
}

// ---------- modal nativo (<dialog>) ----------
function modal(title, html, actions) {
  const dlg = $('#modal');
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
  if (typeof dlg.showModal === 'function') {
    if (!dlg.open) dlg.showModal();
  } else {
    dlg.hidden = false;
  }
  box.lastElementChild?.focus();
}

function closeModal() {
  const dlg = $('#modal');
  if (typeof dlg.close === 'function' && dlg.open) {
    dlg.close();
  } else {
    dlg.hidden = true;
  }
}

function openModal(title, html) {
  modal(title, html, [{ label: 'Cerrar', kind: 'quiet' }]);
}

// Light dismiss: cerrar al hacer clic en el backdrop nativo
$('#modal').addEventListener('click', (e) => {
  const card = $('#modal .modal-card');
  if (!card) return;
  const r = card.getBoundingClientRect();
  const outside = e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
  if (outside) closeModal();
});

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
    const d = new Date(now.now);
    const t = isNaN(d.getTime()) ? String(now.now).replace('T', ' ').slice(0, 19) : new Intl.DateTimeFormat('sv-SE', {
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
      hour12: false
    }).format(d);
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
    const lessons = l.lessons.length > 1 ? `${l.lessons[0].id}â€“${l.lessons[l.lessons.length - 1].id}` : l.lessons[0]?.id ?? 'â€”';
    return `<li class="step lv${l.id} ${l.id === state.current ? 'current' : ''}">
      <div class="step-num"><b>${l.id}</b><span>${esc(l.name)}</span></div>
      <div class="step-body">
        <div class="step-head">
          <div class="step-title"><h2>${esc(l.title)}</h2><small>Perfil ${esc(l.profile)} Â· niveles ${esc(l.route)} del plan Â· lecciones ${lessons}</small></div>
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
          <div class="col"><h4>Se enciende</h4><div class="chips">${l.newDomains.map((d) => `<span class="chip lv">${esc(d.name)} Â· ${d.services}</span>`).join('')}${l.externals.map((x) => `<span class="chip">${esc(x)}</span>`).join('')}</div></div>
        </div>
        <div class="chips">${l.lessons.map((x) => `<span class="chip" title="${esc(x.title)}">${x.id}${x.exercises ? ` Â· ${x.solved}/${x.exercises}` : ''}</span>`).join('')}</div>
        <div class="checkpoint"><b>Checkpoint:</b> ${esc(l.checkpoint)}</div>
        <div class="step-actions">
          <button class="btn" data-level="${l.id}" data-mode="exacto">Encender solo este nivel</button>
          <button class="btn ghost" data-level="${l.id}" data-mode="sumar">AÃ±adir a lo encendido</button>
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
  modal(`Encender solo el nivel ${id} Â· ${level.name}`,
    `<p>QuedarÃ¡n encendidos <b>${level.services}</b> servicios.${off ? ` Se apagarÃ¡n unos <b>${off}</b> que no son de este nivel (sus datos se conservan).` : ''}</p>`,
    [{ label: 'Cancelar', kind: 'quiet' }, { label: `Encender el nivel ${id}`, run: () => activateLevel(id, 'exacto') }]);
});

async function activateLevel(id, mode) {
  toast(`Preparando el nivel ${id}â€¦`);
  const r = await post(`/niveles/${id}/encender?modo=${mode}`);
  const failed = Object.keys(r.failed || {});
  toast(`Nivel ${id}: ${r.started.length} encendidos, ${r.stopped.length} apagados Â· ${r.running} en marcha`);
  if (failed.length) toast('No arrancaron: ' + failed.join(', '), true);
  await refreshAll();
}

// ---------- misiones ----------
async function loadMissions() { state.missions = await api('/misiones'); renderMissions(); }

function renderMissions() {
  const levels = [{ id: 'all', label: 'Todas' }, ...state.levels.map((l) => ({ id: String(l.id), label: `Nivel ${l.id} Â· ${l.name}` }))];
  $('#mission-filters').innerHTML = levels.map((l) => `<button data-filter="${l.id}" aria-pressed="${state.filter === l.id}">${esc(l.label)}</button>`).join('');
  const list = state.missions.filter((m) => state.filter === 'all' || String(m.level) === state.filter);
  $('#missions').innerHTML = list.map((m) => `<article class="mission lv${m.level}">
      <div class="mission-head"><h3>${esc(m.title)}</h3><span class="mission-id">${m.id} <span class="lvbadge">N${m.level}</span></span></div>
      <p>${esc(m.story)}</p>
      <div class="chips">
        ${m.lessons.map((l) => `<span class="chip">LecciÃ³n ${l}</span>`).join('')}
        ${m.flow ? `<button class="chip" data-flow="${esc(m.flow)}">Flujo ${esc(m.flow.slice(0, 4))}</button>` : ''}
        ${m.traps.map((t) => `<span class="chip">Trampa ${t}</span>`).join('')}
      </div>
      <details class="steps"><summary>${m.steps.length} pasos Â· ${m.keyServices.length} servicios clave</summary>
        <ol class="steplist">${m.steps.map((s, i) => `<li>
          <div class="endpoint"><code><span class="method ${s.method}">${s.method}</span>${esc(s.path)}</code><small>${esc(s.service)} Â· puerto ${s.port} â€” ${esc(s.what)}</small></div>
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
    toast(r.started.length ? `${r.mission}: encendidos ${r.started.length} servicios (${r.started.slice(0, 4).join(', ')}${r.started.length > 4 ? 'â€¦' : ''})` : `${r.mission}: ya estaba todo encendido`);
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
  modal(`GET ${path}`, `<p class="muted">${esc(service)} respondiÃ³ <b>${res.status}</b> Â· ${esc(res.headers.get('content-type') || '')}</p><pre class="response">${esc(pretty.slice(0, 12000))}</pre>`, [{ label: 'Cerrar', kind: 'quiet' }]);
}

async function openFlow(file) {
  const md = await api('/flujos/' + encodeURIComponent(file));
  modal(file.replace('.md', ''), `<div class="md">${markdown(md)}</div>`, [{ label: 'Cerrar', kind: 'quiet' }]);
}

// Markdown mÃ­nimo para los flujos: tÃ­tulos, listas, tablas, bloques de cÃ³digo y negrita/cÃ³digo en lÃ­nea.
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
  }).join('') || '<p class="muted">NingÃºn servicio coincide.</p>';
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
        `<p>Si lo apagas, estos ${e.body.dependents.length} servicios encendidos se quedarÃ­an sin Ã©l:</p><div class="chips">${e.body.dependents.map((d) => `<span class="chip">${esc(d)}</span>`).join('')}</div>`,
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
        [{ label: 'Cancelar', kind: 'quiet' }, { label: 'Apagar tambiÃ©n esos', kind: 'danger', run: async () => { await post(`/dominios/${code}/apagar?cascada=true`); await refreshAll(); } }]);
    }
    fail(e);
  }
  await refreshAll();
}

// ---------- ficha de un servicio ----------
async function openService(name) {
  const d = await api('/servicios/' + encodeURIComponent(name));
  const s = d.service;
  $('#d-domain').textContent = `${s.domainName} Â· nivel ${s.level}${s.external ? ' Â· sistema externo' : ''}`;
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
    <div class="section"><h4>Depende de</h4>${chipList(d.dependsOn, 'No depende de nadie mÃ¡s que del reloj.')}</div>
    ${d.optionalDependencies.length ? `<div class="section"><h4>Usa si estÃ¡n encendidos</h4>${chipList(d.optionalDependencies, '')}<p class="muted">Si estÃ¡n apagados, tiene un plan B (por ejemplo, una retenciÃ³n provisional).</p></div>` : ''}
    <div class="section"><h4>Lo necesitan</h4>${chipList(d.dependents, 'NingÃºn servicio depende de este: puedes apagarlo sin miedo.')}</div>
    <div class="section"><h4>Lecciones que lo usan</h4>${d.lessons.length ? `<div class="chips">${d.lessons.map((l) => `<span class="chip" title="${esc(l.title)}">${l.id}</span>`).join('')}</div>` : '<p class="muted">Ninguna lecciÃ³n todavÃ­a: practÃ­calo con una misiÃ³n o desde Swagger.</p>'}</div>
    <div class="section"><h4>Misiones</h4>${d.missions.length ? `<div class="chips">${d.missions.map((m) => `<span class="chip">${m.id} Â· ${esc(m.title)}</span>`).join('')}</div>` : '<p class="muted">No sale en ninguna misiÃ³n.</p>'}</div>
    <div class="section"><h4>${d.endpoints.filter((e) => !e.path.startsWith('/_test')).length} endpoints</h4><ul class="eplist">${d.endpoints.filter((e) => !e.path.startsWith('/_test')).map((e) => `<li><span class="method ${e.method}">${e.method}</span><span><code>${esc(e.path)}</code>${e.summary ? `<br><small class="muted">${esc(e.summary)}</small>` : ''}</span></li>`).join('')}</ul></div>`;
  const drawer = $('#drawer');
  if (typeof drawer.showModal === 'function') {
    if (!drawer.open) drawer.showModal();
  } else {
    drawer.hidden = false;
  }
}
function closeDrawer() {
  const drawer = $('#drawer');
  if (typeof drawer.close === 'function' && drawer.open) {
    drawer.close();
  } else {
    drawer.hidden = true;
  }
}
$('#drawer-close').onclick = closeDrawer;
$('#drawer').addEventListener('click', (e) => {
  const r = $('#drawer').getBoundingClientRect();
  if (e.clientX < r.left) closeDrawer();
});
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
    $('#faults').innerHTML = items.length ? items.map((f) => `<li><span><b>${esc(f.service)}</b> <code>${esc(f.pathPrefix || '/')}</code> â†’ ${f.status || 'latencia'}${f.delayMs ? ` Â· ${f.delayMs} ms` : ''}${f.remaining != null ? ` Â· quedan ${f.remaining}` : ''}</span><button class="btn small quiet" data-fault="${esc(f.id)}">Quitar</button></li>`).join('') : '<li class="muted">NingÃºn fallo activo.</li>';
  } catch { $('#faults').innerHTML = '<li class="muted">chaos-control estÃ¡ apagado: enciende el nivel 3.</li>'; }
}
$('#faults').addEventListener('click', (e) => { const b = e.target.closest('button[data-fault]'); if (b) busy(b, async () => { await api(`/proxy/chaos-control/api/v1/faults/${b.dataset.fault}`, { method: 'DELETE' }); loadFaults(); }); });
$('#fault-form').addEventListener('submit', (e) => {
  e.preventDefault();
  const num = (id) => ($(id).value === '' ? undefined : Number($(id).value));
  const body = { service: $('#fault-service').value, pathPrefix: $('#fault-path').value || undefined, status: num('#fault-status'), delayMs: num('#fault-delay'), times: num('#fault-times'), probability: num('#fault-prob') };
  if (!body.status && !body.delayMs) body.status = 503;
  busy(e.submitter, async () => { await post('/proxy/chaos-control/api/v1/faults', body); toast(`Fallo aÃ±adido a ${body.service}`); loadFaults(); });
});

// ---------- navegación con View Transitions API (Baseline) ----------
const views = ['escalera', 'academia', 'gym', 'misiones', 'mapa', 'laboratorio', 'guia'];
function show(view) {
  if (!views.includes(view)) view = 'escalera';

  const updateDOM = () => {
    views.forEach((v) => { $('#view-' + v).hidden = v !== view; });
    document.querySelectorAll('.tabs button').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.view === view)));
    if (!location.hash.includes(':') && location.hash !== '#' + view) history.replaceState(null, '', '#' + view);
    if (view === 'misiones') renderMissions();
    if (view === 'academia') loadAcademia();
    if (view === 'gym') { if (typeof BrunoGym !== 'undefined') BrunoGym.init(); }
    if (view === 'laboratorio') { loadTraps(); loadFaults(); }
  };

  if (typeof document.startViewTransition === 'function' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    document.startViewTransition(updateDOM);
  } else {
    updateDOM();
  }
}
document.querySelector('.tabs').addEventListener('click', (e) => { const b = e.target.closest('button[data-view]'); if (b) show(b.dataset.view); });
// Enlaces directos: #ficha:product-catalog abre la ficha de un servicio y #flujo:F-07-traspaso.md un flujo (Ãºtil para la guÃ­a y para compartir)
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

refreshAll().then(() => {
  route();
  initBruno();
});
setInterval(refreshStatus, 5000);

// ============================================================================
// VISTA: ACADEMIA KARATE (0 A 100)
// ============================================================================
let acadLessons = [];
let activeLesson = null;
let activeLessonData = null;

async function loadAcademia() {
  try {
    acadLessons = await api('/lecciones');
    renderAcadLessonList();
    if (!activeLesson && acadLessons.length > 0) {
      openAcadLesson(acadLessons[0].id);
    }
  } catch (err) {
    fail(err);
  }
}

function renderAcadLessonList() {
  const ul = $('#academia-lesson-list');
  if (!ul) return;
  ul.innerHTML = acadLessons.map(l => `
    <li class="chip ${activeLesson === l.id ? 'lv' : ''}" role="button" tabindex="0" aria-selected="${activeLesson === l.id}" style="cursor:pointer; display:flex; justify-content:space-between; align-items:center; padding:8px 12px; margin:0;" onclick="openAcadLesson('${l.id}')" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();openAcadLesson('${l.id}');}">
      <div>
        <b>${l.id}</b> · ${esc(l.title.replace(/L\d+\s*·\s*/, ''))}
      </div>
      <span class="badge ${l.solved === l.exercises && l.exercises > 0 ? 'good' : ''}">${l.solved}/${l.exercises}</span>
    </li>
  `).join('');
}

async function openAcadLesson(id) {
  activeLesson = id;
  renderAcadLessonList();
  $('#academia-empty').hidden = true;
  $('#academia-detail').hidden = false;

  const summary = acadLessons.find(l => l.id === id);
  $('#acad-level').innerText = `Nivel ${summary ? summary.level : 0}`;
  $('#acad-title').innerText = summary ? summary.title : id;

  $('#acad-pane-theory').innerHTML = '<span class="muted">Cargando teoría...</span>';
  $('#acad-example-code').innerText = '// Cargando ejemplo...';
  $('#acad-exercises-container').innerHTML = '<span class="muted">Cargando ejercicios...</span>';

  // Actualizar contexto y consejos de Bruno para esta lección
  setBrunoLessonAdvice(id, summary ? summary.title : '');

  try {
    activeLessonData = await api(`/lecciones/${id}`);
    renderLessonTheory(activeLessonData.theory);
    $('#acad-example-code').innerText = activeLessonData.example || '// Sin ejemplo';
    renderLessonExercises(activeLessonData.exercises, activeLessonData.hints);
    switchAcadSubTab('theory');
    auditActiveLessonExercises();
    if (typeof BrunoAudioPlayer !== 'undefined') {
      BrunoAudioPlayer.loadLesson(id, activeLessonData.theory);
    }
  } catch (err) {
    fail(err);
  }
}

function renderLessonTheory(md) {
  let html = esc(md)
    .replace(/^# (.*$)/gim, '<h2 style="color:var(--accent); margin:12px 0 6px 0;">$1</h2>')
    .replace(/^## (.*$)/gim, '<h3 style="color:var(--text); margin:10px 0 4px 0;">$1</h3>')
    .replace(/^### (.*$)/gim, '<h4>$1</h4>')
    .replace(/\*\*(.*?)\*\*/gim, '<b>$1</b>')
    .replace(/\*(.*?)\*/gim, '<i>$1</i>')
    .replace(/`(.*?)`/gim, '<code style="background:rgba(255,255,255,0.08); padding:2px 6px; border-radius:4px;">$1</code>')
    .replace(/\n\n/gim, '<br/><br/>');
  $('#acad-pane-theory').innerHTML = html;
}

function renderLessonExercises(exercises, hints) {
  const container = $('#acad-exercises-container');
  if (!exercises || exercises.length === 0) {
    container.innerHTML = '<span class="muted">Esta lección no tiene ejercicios con huecos.</span>';
    return;
  }

  container.innerHTML = exercises.map((ex, idx) => `
    <article class="panel" style="margin-bottom:16px; border:1px solid var(--line); padding:14px; border-radius:8px; background:var(--surface-2);">
      <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
        <h4 style="margin:0;"><label for="acad-code-${idx}">Ejercicio ${idx + 1}: <code>${esc(ex.file)}</code></label></h4>
        <span class="muted" style="font-size:0.8rem;">Rellena los huecos <code>___</code></span>
      </div>
      <textarea id="acad-code-${idx}" data-file="${esc(ex.file)}" oninput="auditActiveLessonExercises()" aria-label="Código para ${esc(ex.file)}" style="width:100%; height:180px; font-family:var(--mono, monospace); font-size:0.85rem; background:var(--surface); color:var(--fg); border:1px solid var(--line); border-radius:6px; padding:10px; resize:vertical;">${esc(ex.code)}</textarea>
    </article>
  `).join('') + `
    <div style="margin-top:16px;">
      <details style="background:var(--surface-2); border:1px solid var(--line); border-radius:6px; padding:10px;">
        <summary style="cursor:pointer; font-weight:600; color:var(--accent);">💡 Pistas disponibles para esta lección</summary>
        <div style="margin-top:10px; font-size:0.9rem; line-height:1.5;">${esc(hints).replace(/\n/g, '<br/>')}</div>
      </details>
    </div>
  `;
}

function switchAcadSubTab(tab) {
  $('#acad-pane-theory').hidden = tab !== 'theory';
  $('#acad-pane-example').hidden = tab !== 'example';
  $('#acad-pane-exercises').hidden = tab !== 'exercises';

  const tBtn = $('#acad-tab-theory');
  const eBtn = $('#acad-tab-example');
  const xBtn = $('#acad-tab-exercises');

  tBtn.className = 'btn small ' + (tab === 'theory' ? 'active' : 'ghost');
  tBtn.setAttribute('aria-selected', tab === 'theory' ? 'true' : 'false');

  eBtn.className = 'btn small ' + (tab === 'example' ? 'active' : 'ghost');
  eBtn.setAttribute('aria-selected', tab === 'example' ? 'true' : 'false');

  xBtn.className = 'btn small ' + (tab === 'exercises' ? 'active' : 'ghost');
  xBtn.setAttribute('aria-selected', tab === 'exercises' ? 'true' : 'false');
}

$('#acad-btn-services').onclick = async () => {
  if (!activeLesson) return;
  busy($('#acad-btn-services'), async () => {
    const res = await post(`/lecciones/${activeLesson}/preparar`);
    toast(`Servicios de ${activeLesson} listos (${res.running} activos)`);
    refreshStatus();
  });
};

$('#acad-btn-correct').onclick = async () => {
  if (!activeLesson) return;
  const btn = $('#acad-btn-correct');
  busy(btn, async () => {
    // 1. Guardar cambios en los ejercicios
    const textareas = document.querySelectorAll('#acad-exercises-container textarea');
    const payload = {};
    textareas.forEach(t => { payload[t.dataset.file] = t.value; });
    await post(`/lecciones/${activeLesson}/guardar`, payload);

    // 2. Bruno pasa a estado de análisis
    updateBrunoStage('THINKING', 'Evaluando ejecución...', 'Karate DSL está corriendo en tu PC contra los microservicios bancarios.');

    // 3. Corregir ejecutando academia.ps1
    toast('Ejecutando corrector de Karate...');
    const result = await post(`/lecciones/${activeLesson}/corregir`);
    
    // 4. Diagnóstico inteligente de Bruno (Local Heurístico)
    const rawOutput = (result.output || '') + '\n' + (result.error || '');
    const isPassed = !result.error && (rawOutput.includes('BUILD SUCCESS') || rawOutput.includes('PASSED') || rawOutput.includes('0 failures'));
    
    let diagnosis;
    if (isPassed) {
      diagnosis = {
        status: 'OK',
        title: `¡${activeLesson} Completada con Éxito!`,
        message: 'Todos los tests han pasado sin regresiones ni fallos.',
        avatarState: 'SUCCESS',
        hint: '¡Gran trabajo actuarial! Puedes avanzar al siguiente reto o lección en la Escalera.'
      };
    } else {
      diagnosis = BrunoHeuristics.diagnose(rawOutput, { lessonId: activeLesson });
    }

    updateBrunoStage(diagnosis.avatarState, diagnosis.title, diagnosis.message, diagnosis.hint);

    // 5. Mostrar modal enriquecido con el diagnóstico de Bruno
    openModal(`Resultado de Corrección · ${activeLesson}`, `
      <div style="display:flex; gap:12px; align-items:center; background:${isPassed ? 'var(--ok-soft)' : 'var(--bad-soft)'}; color:${isPassed ? 'var(--ok)' : 'var(--bad)'}; padding:12px 14px; border-radius:8px; margin-bottom:12px; border:1px solid ${isPassed ? 'var(--ok)' : 'var(--bad)'};">
        <div style="flex-shrink:0;">${BrunoAvatar.render(diagnosis.avatarState, 48)}</div>
        <div style="flex:1; min-width:0;">
          <h3 style="margin:0 0 3px 0; font-size:1rem; color:inherit;">${esc(diagnosis.title)}</h3>
          <p style="margin:0; font-size:0.86rem; line-height:1.4;">${esc(diagnosis.message)}</p>
          ${diagnosis.hint ? `<small style="display:block; margin-top:5px; font-weight:700; color:var(--accent);">💡 Consejo de Bruno: ${esc(diagnosis.hint)}</small>` : ''}
        </div>
      </div>
      <pre style="background:#090d16; color:#cbd5e1; padding:14px; border-radius:6px; max-height:45vh; overflow-y:auto; font-family:var(--mono, monospace); font-size:0.84rem;">${esc(result.output || result.error || 'Sin salida')}</pre>
    `);
    
    // 6. Recargar estado de lecciones
    acadLessons = await api('/lecciones');
    renderAcadLessonList();
  });
};

// ============================================================================
// COPILOTO SDET BRUNO (CONTROLADOR UI & EVENTOS)
// ============================================================================
const brunoState = {
  open: false,
  mode: 'LOCAL',
  activeTab: 'tips',
  avatarState: 'IDLE'
};

function initBruno() {
  if (typeof BrunoAvatar === 'undefined') return;
  if (typeof BrunoAudioPlayer !== 'undefined') {
    BrunoAudioPlayer.init();
  }

  $('#bruno-trigger-avatar').innerHTML = BrunoAvatar.render('IDLE', 34);
  $('#bruno-header-avatar').innerHTML = BrunoAvatar.render('IDLE', 24);
  $('#bruno-main-avatar').innerHTML = BrunoAvatar.render('IDLE', 75);

  $('#bruno-trigger').onclick = () => toggleBruno(true);
  $('#bruno-close-btn').onclick = () => toggleBruno(false);

  $('#bruno-mode-toggle').onclick = () => {
    const nextMode = brunoState.mode === 'LOCAL' ? 'AI' : 'LOCAL';
    brunoState.mode = nextMode;
    BrunoBridge.setMode(nextMode);
    $('#bruno-mode-indicator').textContent = nextMode === 'LOCAL' ? 'Modo Local (0 Tokens)' : 'Modo Híbrido IA';
    $('#bruno-mode-toggle').textContent = nextMode === 'LOCAL' ? 'Conmutar IA' : 'Modo Local';
    toast(`Bruno: ${nextMode === 'LOCAL' ? 'Modo Local Offline activo (0 tokens)' : 'Modo Híbrido IA activo'}`);
    updateBrunoStage(
      'THINKING',
      nextMode === 'LOCAL' ? 'Modo Local Offline' : 'Modo Híbrido IA',
      nextMode === 'LOCAL' ? 'Operando con reglas actuariales locales al 100% sin coste ni red.' : 'Conectado al endpoint /api/v1/bruno/chat para futuras consultas generativas.'
    );
  };

  const tabs = document.querySelectorAll('.bruno-tab');
  tabs.forEach(t => {
    t.onclick = () => switchBrunoTab(t.dataset.tab);
  });

  document.querySelectorAll('.bruno-quick').forEach(chip => {
    chip.onclick = () => {
      $('#bruno-chat-input').value = chip.dataset.query;
      handleBrunoChatSubmit();
    };
  });

  $('#bruno-chat-form').onsubmit = (e) => {
    e.preventDefault();
    handleBrunoChatSubmit();
  };
}

function toggleBruno(open) {
  brunoState.open = open;
  const dock = $('#bruno-dock');
  if (typeof dock.showPopover === 'function') {
    if (open) {
      try { dock.showPopover(); } catch { dock.hidden = false; }
    } else {
      try { dock.hidePopover(); } catch { dock.hidden = true; }
    }
  } else {
    dock.hidden = !open;
  }
  $('#bruno-trigger').setAttribute('aria-expanded', open ? 'true' : 'false');
  if (open) {
    auditActiveLessonExercises();
  }
}

function updateBrunoStage(avatarState, title, text, hint) {
  brunoState.avatarState = avatarState;
  $('#bruno-trigger-avatar').innerHTML = BrunoAvatar.render(avatarState, 34);
  $('#bruno-header-avatar').innerHTML = BrunoAvatar.render(avatarState, 24);
  $('#bruno-main-avatar').innerHTML = BrunoAvatar.render(avatarState, 75);
  if (title) $('#bruno-bubble-title').textContent = title;
  if (text) $('#bruno-bubble-text').textContent = text;
  if (hint !== undefined) $('#bruno-bubble-hint').textContent = hint;
}

function switchBrunoTab(tabName) {
  brunoState.activeTab = tabName;
  document.querySelectorAll('.bruno-tab').forEach(b => {
    const isAct = b.dataset.tab === tabName;
    b.classList.toggle('active', isAct);
    b.setAttribute('aria-selected', isAct ? 'true' : 'false');
  });
  $('#bruno-pane-tips').hidden = tabName !== 'tips';
  $('#bruno-pane-linter').hidden = tabName !== 'linter';
  $('#bruno-pane-chat').hidden = tabName !== 'chat';
}

function setBrunoLessonAdvice(lessonId, lessonTitle) {
  const tipsMap = {
    L00: { tip: 'L00 es tu primer contacto con Karate DSL. Valida HTTP 200 y comprueba la estructura JSON sin añadir aserciones redundantes.', state: 'EXPLAINING' },
    L01: { tip: 'En L01 trabajamos con listas bancarias y arrays. Usa "match response.items == \'#[]\'" para verificar que devuelve una colección.', state: 'EXPLAINING' },
    L02: { tip: 'L02 exige tipado estricto con Fuzzy Matchers: "#string", "#number", "#regex". Nunca dejes tipos sin validar.', state: 'EXPLAINING' },
    L03: { tip: 'En L03 reutilizamos autenticación. Llama a "_login.feature" con karate.call() y reutiliza el token OAuth2.', state: 'EXPLAINING' },
    L04: { tip: 'L04 aborda peticiones asíncronas. ¡Cero sleeps! Emplea "retry until" para consultar el estado del fondo.', state: 'ALERT' },
    L34: { tip: 'L34 inicia Mocks dinámicos con karate.start({ mock: ..., port: 0 }). Detén siempre el mock al finalizar.', state: 'EXPLAINING' },
    L35: { tip: 'L35 optimiza llamadas con karate.callSingle(). Cachea tokens para no saturar el servidor de identidad.', state: 'EXPLAINING' },
    L36: { tip: 'L36 ejecuta tests en paralelo con tags @basics, @async. Cuida el aislamiento de datos actuariales.', state: 'EXPLAINING' },
    L37: { tip: 'L37 extiende Karate con RuntimeHook en Java para auditoría actuarial antes y después de cada escenario.', state: 'EXPLAINING' },
    L38: { tip: 'L38 define SLAs con Gatling. Asegúrate de que el 95% de las peticiones respondan en menos de 250ms.', state: 'ALERT' },
    L39: { tip: 'L39 automatiza CI/CD con GitHub Actions. Los tests deben ejecutarse sin dependencias locales no mockeadas.', state: 'SUCCESS' }
  };

  const advice = tipsMap[lessonId] || {
    tip: `Estás explorando ${lessonId} (${lessonTitle || 'Lección Actuarial'}). Sigue el ciclo Red -> Green -> Refactor.`,
    state: 'IDLE'
  };

  updateBrunoStage(advice.state, `Bruno en ${lessonId}`, advice.tip, 'Completa los huecos y pulsa Corregir.');
  
  const tipsBox = $('#bruno-tips-content');
  if (tipsBox) {
    tipsBox.innerHTML = `
      <div style="background:var(--surface-2); padding:10px 12px; border-radius:8px; border:1px solid var(--line);">
        <h4 style="margin:0 0 6px 0; color:var(--accent); font-size:0.88rem;">Norma Actuarial para ${lessonId}</h4>
        <p style="font-size:0.84rem; margin-bottom:8px; line-height:1.4;">${esc(advice.tip)}</p>
        <ul style="font-size:0.8rem; padding-left:16px; margin:0; color:var(--muted); line-height:1.4;">
          <li>Cero esperas pasivas (sleep).</li>
          <li>Usa "assert" para comparaciones booleanas relativas.</li>
          <li>Respeta el estándar RFC 9457 en errores HTTP.</li>
        </ul>
      </div>
    `;
  }
}

function auditActiveLessonExercises() {
  if (typeof BrunoLinter === 'undefined') return;
  const textareas = document.querySelectorAll('#acad-exercises-container textarea');
  let combinedCode = '';
  textareas.forEach(t => { combinedCode += t.value + '\n'; });
  
  const issues = BrunoLinter.audit(combinedCode);
  const countBadge = $('#bruno-linter-count');
  const linterBox = $('#bruno-linter-content');
  if (!linterBox) return;

  if (issues.length > 0) {
    if (countBadge) {
      countBadge.textContent = issues.length;
      countBadge.hidden = false;
    }
    linterBox.innerHTML = issues.map(iss => `
      <div class="bruno-linter-card ${iss.level}" style="margin-bottom:8px;">
        <div style="margin-bottom:2px;">
          <strong style="color:${iss.level === 'CRITICAL' ? 'var(--bad)' : 'var(--warn)'}">[${iss.level}] Línea ${iss.line}: ${esc(iss.title)}</strong>
        </div>
        <p style="margin:2px 0; font-size:0.82rem;">${esc(iss.message)}</p>
        <small style="color:var(--accent); font-weight:600;">💡 Solución: ${esc(iss.solution)}</small>
      </div>
    `).join('');
  } else {
    if (countBadge) countBadge.hidden = true;
    linterBox.innerHTML = `
      <div style="padding:10px 12px; border-radius:6px; background:var(--ok-soft); color:var(--ok); font-size:0.84rem; border:1px solid var(--ok);">
        ✅ <strong>Código sin antipatrones:</strong> Cero sleeps, datos fijos ni sintaxis inválidas detectadas.
      </div>
    `;
  }
}

async function handleBrunoChatSubmit() {
  const input = $('#bruno-chat-input');
  const q = input.value.trim();
  if (!q) return;
  input.value = '';

  addBrunoChatMsg('user', q);
  updateBrunoStage('THINKING', 'Consultando base SDET...', 'Evaluando reglas heurísticas...');

  const response = await BrunoBridge.ask(q, { lessonId: activeLesson });
  updateBrunoStage(response.avatarState || 'EXPLAINING', response.title || 'Respuesta de Bruno', response.answer);
  addBrunoChatMsg('bruno', response.answer);
}

function addBrunoChatMsg(sender, text) {
  const box = $('#bruno-chat-history');
  if (!box) return;
  const msg = document.createElement('div');
  msg.className = `bruno-chat-msg ${sender}`;
  msg.textContent = text;
  box.appendChild(msg);
  box.scrollTop = box.scrollHeight;
}