// BrunoBridge: Capa desacoplada entre la UI de la Torre de Control y los cerebros de Bruno.
// Por defecto opera en MODO LOCAL (heurístico, 0 tokens, sin red).
// Permite conmutar a MODO HÍBRIDO IA para conectar con proveedores LLM en el futuro sin reescribir la UI.
'use strict';

const BrunoBridge = {
  mode: 'LOCAL', // 'LOCAL' | 'AI'
  aiEndpoint: '/api/v1/bruno/chat',

  setMode(newMode) {
    if (newMode === 'AI' || newMode === 'LOCAL') {
      this.mode = newMode;
      return true;
    }
    return false;
  },

  getMode() {
    return this.mode;
  },

  async ask(question, context = {}) {
    // Si el modo es local o la IA no está configurada, responder con reglas heurísticas
    if (this.mode === 'LOCAL') {
      return this.resolveLocally(question, context);
    }

    // Modo híbrido IA (Plug-and-play para el futuro)
    try {
      const res = await fetch(this.aiEndpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, context, timestamp: new Date().toISOString() })
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      return {
        source: 'AI',
        answer: data.reply || 'Sin respuesta del modelo IA.',
        avatarState: 'EXPLAINING'
      };
    } catch (err) {
      // Fallback elegante e ininterrumpido al motor local
      const localFallback = this.resolveLocally(question, context);
      localFallback.note = 'Nota: Modo IA no disponible, respondiendo con motor local.';
      return localFallback;
    }
  },

  resolveLocally(question, context = {}) {
    const q = (question || '').toLowerCase();
    const lesson = context.lessonId || 'L00';

    if (q.includes('sleep') || q.includes('dormir') || q.includes('esperar')) {
      return {
        source: 'LOCAL',
        title: 'Consejo Actuarial de Bruno: Esperas Asíncronas',
        answer: "En un banco nórdico, nunca usamos pausas fijas. Usa 'retry until response.status == 'COMPLETED'' o adelanta el reloj con 'clock-service' en `/api/v1/clock/shift`.",
        avatarState: 'EXPLAINING'
      };
    }

    if (q.includes('token') || q.includes('login') || q.includes('auth')) {
      return {
        source: 'LOCAL',
        title: 'Gestión de Identidad con Bruno',
        answer: "Para autenticar peticiones en Nordvik, reutiliza '_login.feature' llamándolo con: `* def erik = call read('this:_login.feature') usuarios.erik`. Luego inyecta `headers erik.auth`.",
        avatarState: 'EXPLAINING'
      };
    }

    if (q.includes('mock') || q.includes('karate.start')) {
      return {
        source: 'LOCAL',
        title: 'Mocks Dinámicos con Bruno',
        answer: "Con `karate.start({ mock: 'this:_mock.feature', port: 0 })` levantas un servidor HTTP local en un puerto libre. Recuerda llamar a `mock.stop()` al final del feature.",
        avatarState: 'EXPLAINING'
      };
    }

    if (q.includes('pension') || q.includes('traspaso') || q.includes('saga')) {
      return {
        source: 'LOCAL',
        title: 'Sagas Financieras de Pensiones',
        answer: "Los traspasos entre entidades gestoras son sagas de larga duración. Valida la máquina de estados: REQUESTED -> VALUATION_PENDING -> COMMITTED.",
        avatarState: 'EXPLAINING'
      };
    }

    return {
      source: 'LOCAL',
      title: `Orientación de Bruno para ${lesson}`,
      answer: `Estoy monitoreando la lección ${lesson}. Revisa la teoría en la primera pestaña, completa los huecos en el editor y pulsa 'Corregir lección'. Si se produce un error, te lo traduciré al instante.`,
      avatarState: 'IDLE'
    };
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoBridge };
}
