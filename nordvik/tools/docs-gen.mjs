// Generador de documentación de Nordvik.
// Lee el OpenAPI y /info de cada servicio EN MARCHA y genera:
//   docs/services/<servicio>.md   ficha del servicio con todos sus endpoints
//   docs/openapi/<servicio>.json  contrato publicado
//   docs/README.md                índice del portal (catálogo por dominio)
//   collections/<equipo>/...      colecciones Bruno por equipo, una petición por endpoint
// Uso: node tools/docs-gen.mjs   (con Nordvik arrancado)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const hostFile = path.join(root, 'data', 'host.json');
if (!fs.existsSync(hostFile)) { console.error('Arranca Nordvik primero: nordvik up'); process.exit(1); }
const host = JSON.parse(fs.readFileSync(hostFile, 'utf8'));
const out = (p, text) => { fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, text.replace(/\r?\n/g, '\n')); };
const slug = s => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');

function resolveRef(spec, schema) {
  if (!schema) return null;
  if (schema.$ref) return resolveRef(spec, spec.components?.schemas?.[schema.$ref.split('/').pop()]);
  return schema;
}
function typeOf(spec, s) {
  s = resolveRef(spec, s) || {};
  if (s.type === 'array') return `lista de ${typeOf(spec, s.items)}`;
  if (s.properties) return 'objeto';
  return (s.type || 'cualquiera') + (s.format ? ` (${s.format})` : '') + (s.nullable ? ', puede ser null' : '');
}
function fields(spec, schema) {
  const s = resolveRef(spec, schema);
  if (!s) return [];
  if (s.type === 'array') return fields(spec, s.items).map(f => ({ ...f, name: '[] ' + f.name }));
  return Object.entries(s.properties || {}).map(([name, p]) => ({ name, type: typeOf(spec, p) }));
}

const services = [];
for (const s of host.services) {
  try {
    const info = await (await fetch(`http://localhost:${s.port}/info`)).json();
    const spec = await (await fetch(`http://localhost:${s.port}/swagger/v1/swagger.json`)).json();
    services.push({ info, spec });
  } catch (e) {
    console.error(`No pude leer ${s.name}: ${e.message}`);
  }
}

const byDomain = {};
for (const { info, spec } of services) {
  out(path.join(root, 'docs', 'openapi', `${info.name}.json`), JSON.stringify(spec, null, 2));
  const endpoints = [];
  for (const [p, ops] of Object.entries(spec.paths || {})) {
    for (const [method, op] of Object.entries(ops)) endpoints.push({ method: method.toUpperCase(), path: p, op });
  }
  endpoints.sort((a, b) => a.path.localeCompare(b.path) || a.method.localeCompare(b.method));
  let md = `# ${info.name}\n\n${info.description}\n\n`;
  md += `| Dato | Valor |\n| --- | --- |\n| Dominio | ${info.domain} |\n| Equipo dueño | ${info.team} |\n| Puerto | ${info.port} |\n| Oleada | ${info.wave} |\n| Versión | ${info.version} |\n| Convención JSON | ${info.jsonNaming} |\n| Depende de | ${(info.dependsOn || []).join(', ') || '—'} |\n| Usa si está encendido | ${(info.optionalDependsOn || []).join(', ') || '—'} |\n| Nivel de la escalera | ${Math.min(info.wave, 4)} |\n| Publica eventos | ${(info.emits || []).map(t => '`' + t + '`').join(', ') || '—'} |\n| Consume eventos | ${(info.consumes || []).map(t => '`' + t + '`').join(', ') || '—'} |\n| Swagger | http://localhost:${info.port}/swagger |\n| Colección Bruno | \`collections/${slug(info.team)}\` |\n\n`;
  md += `## Endpoints (${endpoints.length})\n\n| Método | Ruta | Para qué |\n| --- | --- | --- |\n`;
  for (const e of endpoints) md += `| \`${e.method}\` | \`${e.path}\` | ${e.op.summary || ''} |\n`;
  for (const e of endpoints) {
    md += `\n### ${e.method} ${e.path}\n\n${e.op.summary || ''}\n\n`;
    const params = (e.op.parameters || []);
    if (params.length) {
      md += `**Parámetros**\n\n| Nombre | Dónde | Tipo | Obligatorio |\n| --- | --- | --- | --- |\n`;
      for (const p of params) md += `| \`${p.name}\` | ${p.in} | ${typeOf(spec, p.schema)} | ${p.required ? 'sí' : 'no'} |\n`;
      md += '\n';
    }
    const body = e.op.requestBody?.content?.['application/json']?.schema;
    if (body) {
      md += `**Cuerpo que envías (JSON)**\n\n| Campo | Tipo |\n| --- | --- |\n`;
      for (const f of fields(spec, body)) md += `| \`${f.name}\` | ${f.type} |\n`;
      md += '\n';
    }
    md += `**Respuestas**\n\n| Código | Contenido |\n| --- | --- |\n`;
    for (const [code, r] of Object.entries(e.op.responses || {})) {
      const sch = r.content?.['application/json']?.schema;
      const fs2 = fields(spec, sch).map(f => `\`${f.name}\``).join(', ');
      md += `| ${code} | ${fs2 || r.description || ''} |\n`;
    }
    md += `| 4xx | Error RFC 9457: \`type\`, \`title\`, \`status\`, \`detail\`, \`instance\`, \`code\`, \`correlationId\`, \`service\`, \`hint\` (modo didáctico), \`errors[]\` (validación) |\n`;
  }
  out(path.join(root, 'docs', 'services', `${info.name}.md`), md);

  // Colección Bruno del equipo
  const team = slug(info.team);
  const cdir = path.join(root, 'collections', team);
  out(path.join(cdir, 'bruno.json'), JSON.stringify({ version: '1', name: `Nordvik · ${info.team}`, type: 'collection' }, null, 2));
  const envFile = path.join(cdir, 'environments', 'local.bru');
  const prev = fs.existsSync(envFile) ? fs.readFileSync(envFile, 'utf8') : 'vars {\n}\n';
  const varName = info.name.replace(/-([a-z])/g, (_, c) => c.toUpperCase()) + 'Url';
  if (!prev.includes(`${varName}:`)) out(envFile, prev.replace(/}\s*$/, `  ${varName}: http://localhost:${info.port}\n}\n`));
  const loginFile = path.join(cdir, '00-login.bru');
  if (!fs.existsSync(loginFile)) out(loginFile, `meta {\n  name: 00 · Login (guarda el token)\n  type: http\n  seq: 0\n}\n\npost {\n  url: http://localhost:6200/api/v1/oauth/token\n  body: formUrlEncoded\n  auth: none\n}\n\nbody:form-urlencoded {\n  grant_type: password\n  username: asesor.erik\n  password: Erik2026!\n}\n\nscript:post-response {\n  bru.setEnvVar("token", res.body.access_token);\n}\n\ndocs {\n  Ejecútalo primero: guarda el token (5 minutos de reloj virtual) en la variable token del entorno.\n  Usuarios: cliente.ana / Ana2026!, asesor.erik / Erik2026!, operaciones.olof / Olof2026!, compliance.karin / Karin2026!, admin / Admin2026!\n}\n`);
  let seq = 1;
  for (const e of endpoints) {
    const example = e.path.replace(/\{isin\}/g, 'SE0000000001').replace(/\{market\}/g, 'XSTO').replace(/\{from\}/g, 'EUR').replace(/\{to\}/g, 'SEK').replace(/\{name\}/g, 'demo');
    const hasBody = !!e.op.requestBody;
    const bodyEx = hasBody ? JSON.stringify(Object.fromEntries(fields(spec, e.op.requestBody.content['application/json'].schema).map(f => [f.name, f.type.startsWith('integer') || f.type.startsWith('number') ? 1 : 'valor'])), null, 2) : null;
    let bru = `meta {\n  name: ${e.method} ${e.path}\n  type: http\n  seq: ${seq++}\n}\n\n${e.method.toLowerCase()} {\n  url: {{${varName}}}${example}\n  body: ${hasBody ? 'json' : 'none'}\n  auth: bearer\n}\n\nauth:bearer {\n  token: {{token}}\n}\n\nheaders {\n  X-Correlation-Id: bruno-{{$timestamp}}\n}\n`;
    if (bodyEx) bru += `\nbody:json {\n${bodyEx.split('\n').map(l => '  ' + l).join('\n')}\n}\n`;
    bru += `\ndocs {\n  ${e.op.summary || ''}\n  Ficha completa: docs/services/${info.name}.md\n}\n`;
    out(path.join(cdir, info.name, `${String(seq - 1).padStart(2, '0')}-${slug(e.method + '-' + e.path)}.bru`), bru);
  }
  (byDomain[info.domain] ||= []).push({ info, count: endpoints.length });
}

// Catálogo de eventos: quién publica cada tema, quién lo consume y un ejemplo real si el bus lo tiene
const topics = {};
for (const { info } of services) for (const t of info.emits || []) (topics[t] ||= { producers: [], consumers: [] }).producers.push(info.name);
const matches = (pattern, topic) => pattern === '*' || pattern === topic || (pattern.endsWith('.*') && topic.startsWith(pattern.slice(0, -1)));
for (const { info } of services) for (const c of info.consumes || []) for (const t of Object.keys(topics)) if (matches(c, t)) topics[t].consumers.push(info.name);
const busUp = host.services.some(s => s.name === 'event-bus');
let ev = `# Catálogo de eventos\n\nCada servicio guarda sus eventos en su **outbox**; \`outbox-relay\` los publica en \`event-bus\` (puerto 7400) y los consumidores los leen con \`GET /api/v1/events?topics=...&after=<cursor>\`. Puedes suscribir un mock de Karate con \`POST /api/v1/subscriptions\`.\n\nSobre (envelope) de todos los eventos: \`eventId\`, \`seq\`, \`topic\`, \`key\`, \`producer\`, \`occurredAt\`, \`correlationId\`, \`schemaVersion\`, \`payload\`.\n\n| Tema | Publica | Consumen |\n| --- | --- | --- |\n`;
for (const t of Object.keys(topics).sort()) ev += `| \`${t}\` | ${topics[t].producers.join(', ')} | ${[...new Set(topics[t].consumers)].join(', ') || '—'} |\n`;
for (const t of Object.keys(topics).sort()) {
  ev += `\n## ${t}\n\nPublica: ${topics[t].producers.join(', ')} · Consumen: ${[...new Set(topics[t].consumers)].join(', ') || 'nadie todavía (puedes suscribirte tú)'}\n`;
  if (busUp) {
    try {
      const page = await (await fetch(`http://localhost:7400/api/v1/events?topics=${t}&limit=1`)).json();
      if (page.items?.length) ev += `\nEjemplo real (el primero que hay en el bus):\n\n\`\`\`json\n${JSON.stringify(page.items[0], null, 2)}\n\`\`\`\n`;
    } catch { }
  }
}
out(path.join(root, 'docs', 'events.md'), ev);

let index = `# Portal de desarrolladores de Nordvik\n\nGenerado automáticamente a partir de los servicios en marcha (perfil **${host.profile}**). No lo edites a mano: ejecuta \`node tools/docs-gen.mjs\`.\n\n`;
index += `| Sección | Dónde |\n| --- | --- |\n| Fichas de servicio | \`docs/services/\` |\n| Catálogo de eventos | \`docs/events.md\` |\n| Contratos OpenAPI | \`docs/openapi/\` |\n| Colecciones Bruno por equipo | \`collections/\` |\n| Decisiones de arquitectura | \`docs/adr/\` |\n| Flujos | \`docs/flows/\` |\n| Informes de la noche | \`docs/informes/\` |\n| Escalera de niveles (0-4) y misiones | \`torre/escalera.json\` · ADR-014 |\n| Torre de control (encender y apagar en caliente) | http://localhost:3000 |\n| Guía paso a paso con capturas | \`docs/guia/\` |\n\n`;
let total = 0;
for (const domain of Object.keys(byDomain).sort()) {
  index += `## ${domain}\n\n| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |\n| --- | --- | --- | --- | --- | --- |\n`;
  for (const { info, count } of byDomain[domain].sort((a, b) => a.info.port - b.info.port)) {
    index += `| [${info.name}](services/${info.name}.md) | ${info.port} | ${info.team} | ${info.wave} | ${count} | ${info.description} |\n`;
    total += count;
  }
  index += '\n';
}
index += `**${services.length} servicios · ${total} endpoints documentados.**\n`;
out(path.join(root, 'docs', 'README.md'), index);
console.log(`Documentados ${services.length} servicios y ${total} endpoints.`);
