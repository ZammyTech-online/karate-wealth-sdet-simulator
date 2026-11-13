// BrunoGym: Controlador interactivo del Gherkin BDD Gym de Karate DSL (Given - When - Then).
// Soporta 4,000 ejercicios combinatorios de Junior a Senior con evaluación en vivo.
'use strict';

const BrunoGym = {
  currentLevel: 'junior',
  currentDomain: 'all',
  currentExercise: null,
  catalog: [],
  score: 0,
  streak: 0,
  mode: 'endless', // 'endless' | 'catalog'
  temas: [],
  temasCollapsed: false,

  _initialized: false,

  async init() {
    if (this._initialized) return;
    this._initialized = true;
    this.bindEvents();
    if (typeof Certificacion !== 'undefined' && typeof Certificacion.init === 'function') {
      Certificacion.init();
    }
    await this.loadCatalog();
    await this.loadTemas();
    await this.loadRandomExercise(this.currentLevel);
  },

  bindEvents() {
    // Selector de niveles (Junior, Mid, Senior, Staff)
    const levelBtns = document.querySelectorAll('.gym-level-btn');
    levelBtns.forEach(btn => {
      btn.onclick = () => {
        levelBtns.forEach(b => {
          b.classList.remove('active');
          b.classList.add('ghost');
        });
        btn.classList.add('active');
        btn.classList.remove('ghost');
        this.currentLevel = btn.dataset.level;
        this.renderTopicCards();
        this.loadRandomExercise(this.currentLevel);
      };
    });

    // Toggle de colapso de temas
    const toggleTemasBtn = document.getElementById('gym-topic-toggle-btn');
    if (toggleTemasBtn) {
      toggleTemasBtn.onclick = () => this.toggleTopicHub();
    }

    // Filtros de dominio
    const domainFilter = document.getElementById('gym-domain-filter');
    if (domainFilter) {
      domainFilter.onchange = (e) => {
        this.currentDomain = e.target.value;
        this.filterCatalogList();
      };
    }

    // Toggle de Modo (Endless vs Catálogo)
    const modeCatalogBtn = document.getElementById('gym-mode-catalog');
    const modeEndlessBtn = document.getElementById('gym-mode-endless');
    if (modeCatalogBtn && modeEndlessBtn) {
      modeCatalogBtn.onclick = () => this.switchMode('catalog');
      modeEndlessBtn.onclick = () => this.switchMode('endless');
    }

    // Botón Siguiente / Aleatorio
    const nextBtn = document.getElementById('gym-next-btn');
    if (nextBtn) {
      nextBtn.onclick = () => this.loadRandomExercise(this.currentLevel);
    }

    // Botón Verificar con Bruno
    const verifyBtn = document.getElementById('gym-verify-btn');
    if (verifyBtn) {
      verifyBtn.onclick = () => this.verifyCurrentExercise();
    }

    // Linter en tiempo real sobre el editor
    const textarea = document.getElementById('gym-code-editor');
    if (textarea) {
      textarea.oninput = () => this.auditCodeRealtime();
    }
  },

  switchMode(newMode) {
    this.mode = newMode;
    const catBox = document.getElementById('gym-catalog-sidebar');
    const modeCatalogBtn = document.getElementById('gym-mode-catalog');
    const modeEndlessBtn = document.getElementById('gym-mode-endless');

    if (modeCatalogBtn && modeEndlessBtn) {
      modeCatalogBtn.classList.toggle('active', newMode === 'catalog');
      modeEndlessBtn.classList.toggle('active', newMode === 'endless');
    }

    if (catBox) {
      catBox.hidden = newMode !== 'catalog';
    }
  },

  async loadCatalog() {
    try {
      const res = await fetch('/gym/catalog.json');
      if (res.ok) {
        this.catalog = await res.json();
        this.renderCatalogList();
      }
    } catch (err) {
      console.warn('Error cargando catálogo del gym:', err);
    }
  },

  renderCatalogList() {
    const listEl = document.getElementById('gym-catalog-items');
    if (!listEl) return;

    const filtered = this.catalog.filter(item => {
      const matchLvl = item.level === this.currentLevel;
      const matchDom = this.currentDomain === 'all' || item.domain === this.currentDomain;
      return matchLvl && matchDom;
    });

    listEl.innerHTML = filtered.slice(0, 100).map(item => `
      <li class="chip ${this.currentExercise?.id === item.id ? 'lv' : ''}" style="cursor:pointer; display:flex; justify-content:space-between; padding:6px 10px; font-size:0.82rem;" onclick="BrunoGym.loadExerciseById('${item.id}')">
        <span><b>${item.id}</b> · ${this.escape(item.title.replace(/.*BDD\s*#\d+:\s*/, ''))}</span>
        <span class="muted" style="text-transform:uppercase; font-size:0.7rem;">${item.domain}</span>
      </li>
    `).join('');
  },

  filterCatalogList() {
    this.renderCatalogList();
  },

  async loadExerciseById(id) {
    try {
      const res = await fetch(`/api/v1/gym/exercise?id=${id}`);
      if (!res.ok) throw new Error('Ejercicio no encontrado');
      const ex = await res.json();
      this.displayExercise(ex);
    } catch (err) {
      console.error(err);
    }
  },

  async loadRandomExercise(level = 'junior') {
    try {
      const res = await fetch(`/api/v1/gym/random?level=${level}`);
      if (!res.ok) throw new Error('Error al cargar ejercicio aleatorio');
      const ex = await res.json();
      this.displayExercise(ex);
    } catch (err) {
      console.error(err);
    }
  },

  displayExercise(ex) {
    this.currentExercise = ex;

    // Actualizar encabezados y metadata
    document.getElementById('gym-ex-id').textContent = ex.id;
    document.getElementById('gym-ex-level').textContent = ex.levelName;
    document.getElementById('gym-ex-domain').textContent = ex.domain.toUpperCase();
    document.getElementById('gym-ex-title').textContent = ex.title;
    document.getElementById('gym-ex-enunciado').textContent = ex.enunciado;

    // Editor con el código con huecos ___
    const editor = document.getElementById('gym-code-editor');
    if (editor) {
      editor.value = ex.codeWithGaps;
    }

    // Pistas
    const hintsBox = document.getElementById('gym-hints-list');
    if (hintsBox) {
      hintsBox.innerHTML = (ex.hints || []).map((h, i) => `<li><strong>Pista ${i + 1}:</strong> ${this.escape(h.replace(/^Pista \d+:\s*/, ''))}</li>`).join('');
    }

    // Reset de feedback
    const feedbackEl = document.getElementById('gym-feedback-box');
    if (feedbackEl) feedbackEl.hidden = true;

    // Actualizar estado de Bruno
    if (typeof updateBrunoStage === 'function') {
      updateBrunoStage('IDLE', `Reto BDD: ${ex.id}`, `Rellena los huecos '___' respetando Given (Arrange), When (Act) y Then (Assert).`);
    }

    this.auditCodeRealtime();
    this.renderCatalogList();
  },

  auditCodeRealtime() {
    const editor = document.getElementById('gym-code-editor');
    const linterBox = document.getElementById('gym-linter-feedback');
    if (!editor || !linterBox || typeof BrunoLinter === 'undefined') return;

    const code = editor.value;
    const issues = BrunoLinter.audit(code);

    if (issues.length > 0) {
      linterBox.hidden = false;
      linterBox.innerHTML = issues.map(iss => `
        <div style="font-size:0.8rem; padding:4px 8px; border-radius:4px; margin-bottom:4px; background:var(--bad-soft); color:var(--bad); border-left:3px solid var(--bad);">
          <strong>[${iss.rule}]</strong> ${this.escape(iss.title)}: ${this.escape(iss.solution)}
        </div>
      `).join('');
    } else {
      linterBox.hidden = true;
    }
  },

  async verifyCurrentExercise() {
    if (!this.currentExercise) return;
    const editor = document.getElementById('gym-code-editor');
    const feedbackEl = document.getElementById('gym-feedback-box');
    const verifyBtn = document.getElementById('gym-verify-btn');
    if (!editor || !feedbackEl) return;

    if (verifyBtn) verifyBtn.disabled = true;

    // Manejo de Modo Fix-the-Bug (Trampa de Bruno)
    if (this.currentExercise.isBrokenTest) {
      const code = editor.value;
      const target = this.currentExercise.fixTarget;
      feedbackEl.hidden = false;
      if (code.includes(target) && !code.includes('___')) {
        this.score += 25;
        this.streak += 1;
        this.updateScoreUI();
        feedbackEl.style.background = 'var(--ok-soft)';
        feedbackEl.style.color = 'var(--ok)';
        feedbackEl.style.borderColor = 'var(--ok)';
        feedbackEl.innerHTML = `
          <div style="display:flex; justify-content:space-between; align-items:center;">
            <div>
              <strong>✅ ¡Bug de Contrato Financiero Reparado! (+25 XP)</strong>
              <p style="margin:4px 0 0 0; font-size:0.85rem;">Has eliminado la vulnerabilidad o anti-patrón de Bruno con éxito.</p>
            </div>
            <button class="btn small" onclick="BrunoGym.loadRandomExercise('${this.currentLevel}')">Siguiente Reto ➔</button>
          </div>
        `;
        if (typeof updateBrunoStage === 'function') {
          updateBrunoStage('SUCCESS', '¡Bug Resuelto con Éxito!', 'El contrato ahora es seguro, robusto y testeable.');
        }
        if (verifyBtn) verifyBtn.disabled = false;
        return;
      } else {
        this.streak = 0;
        this.updateScoreUI();
        feedbackEl.style.background = 'var(--bad-soft)';
        feedbackEl.style.color = 'var(--bad)';
        feedbackEl.style.borderColor = 'var(--bad)';
        feedbackEl.innerHTML = `
          <strong>❌ El fallo aún persiste en el escenario:</strong>
          <p style="margin:4px 0 0 0; font-size:0.85rem;">Revisa la pista: ${this.escape(this.currentExercise.hints?.[0] || '')}</p>
        `;
        if (typeof updateBrunoStage === 'function') {
          updateBrunoStage('ALERT', 'El bug sigue activo', 'Asegúrate de aplicar la corrección recomendada.');
        }
        if (verifyBtn) verifyBtn.disabled = false;
        return;
      }
    }

    try {
      // 1. Bruno pasa a modo análisis
      if (typeof updateBrunoStage === 'function') {
        updateBrunoStage('THINKING', 'Evaluando estructura BDD...', 'Analizando contrato Given-When-Then.');
      }

      const res = await fetch('/api/v1/gym/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: this.currentExercise.id,
          code: editor.value
        })
      });

      const data = await res.json();
      feedbackEl.hidden = false;

      if (data.valid) {
        this.score += (data.xp || 15);
        this.streak += 1;
        this.updateScoreUI();

        feedbackEl.style.background = 'var(--ok-soft)';
        feedbackEl.style.color = 'var(--ok)';
        feedbackEl.style.borderColor = 'var(--ok)';
        feedbackEl.innerHTML = `
          <div style="display:flex; justify-content:space-between; align-items:center;">
            <div>
              <strong>✅ ¡Prueba Aprobada! (+${data.xp || 15} XP)</strong>
              <p style="margin:4px 0 0 0; font-size:0.85rem;">${this.escape(data.message)}</p>
            </div>
            <button class="btn small" onclick="BrunoGym.loadRandomExercise('${this.currentLevel}')">Siguiente Reto ➔</button>
          </div>
        `;

        if (typeof updateBrunoStage === 'function') {
          updateBrunoStage('SUCCESS', '¡Aserción BDD Impecable!', `Has estructurado el contrato con éxito. Racha actual: ${this.streak} seguidos.`);
        }
      } else {
        this.streak = 0;
        this.updateScoreUI();

        feedbackEl.style.background = 'var(--bad-soft)';
        feedbackEl.style.color = 'var(--bad)';
        feedbackEl.style.borderColor = 'var(--bad)';
        feedbackEl.innerHTML = `
          <strong>❌ Discrepancia en el Contrato:</strong>
          <p style="margin:4px 0 0 0; font-size:0.85rem;">${this.escape(data.reason)}</p>
          ${data.feedback ? `<small style="display:block; margin-top:4px;">💡 ${this.escape(data.feedback)}</small>` : ''}
        `;

        if (typeof updateBrunoStage === 'function') {
          updateBrunoStage('ALERT', 'Contrato No Satisfecho', data.reason);
        }
      }
    } catch (err) {
      console.error(err);
    } finally {
      if (verifyBtn) verifyBtn.disabled = false;
    }
  },

  async loadTemas() {
    try {
      const res = await fetch('/api/v1/temas');
      if (res.ok) {
        const data = await res.json();
        this.temas = data.niveles || [];
        this.renderTopicCards();
      }
    } catch (err) {
      console.warn('Error cargando temas pedagógicos:', err);
    }
  },

  renderTopicCards() {
    const container = document.getElementById('gym-topic-cards');
    const taglineEl = document.getElementById('gym-topic-tagline');
    const chipEl = document.getElementById('gym-active-level-chip');
    if (!container) return;

    const lvlData = this.temas.find(l => l.id === this.currentLevel);
    if (!lvlData) return;

    if (taglineEl) taglineEl.textContent = lvlData.tagline || '';
    if (chipEl) chipEl.textContent = `${lvlData.name} (${lvlData.temas?.length || 0} temas)`;

    container.innerHTML = (lvlData.temas || []).map(t => `
      <div class="topic-card" style="background:var(--surface-2); border:1px solid var(--line); border-radius:10px; padding:12px; display:flex; flex-direction:column; justify-content:space-between; gap:10px; transition:border-color 0.2s;">
        <div>
          <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:6px;">
            <div style="display:flex; align-items:center; gap:6px;">
              <span style="font-size:1.15rem;">${t.icon || '📘'}</span>
              <strong style="font-size:0.85rem; color:var(--accent);">${t.id}</strong>
            </div>
            <span class="chip" style="font-size:0.7rem; text-transform:uppercase;">${t.domain}</span>
          </div>
          <h4 style="margin:0 0 4px 0; font-size:0.92rem; line-height:1.3;">${this.escape(t.title)}</h4>
          <p class="muted" style="margin:0; font-size:0.8rem; line-height:1.35; display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; overflow:hidden;">${this.escape(t.subtitle)}</p>
        </div>
        <div style="display:flex; justify-content:space-between; align-items:center; border-top:1px solid var(--line); padding-top:8px; margin-top:4px;">
          <button class="btn small quiet" onclick="BrunoGym.openPedModal('${t.id}')" style="padding:4px 8px; font-size:0.78rem;">📘 Guía</button>
          <button class="btn small" onclick="BrunoGym.loadChallengeFromTopic('${t.id}')" style="padding:4px 8px; font-size:0.78rem;">🥋 Reto ➔</button>
        </div>
      </div>
    `).join('');
  },

  openPedModal(temaId) {
    let matched = null;
    for (const lvl of this.temas) {
      const found = (lvl.temas || []).find(t => t.id === temaId);
      if (found) {
        matched = found;
        break;
      }
    }
    if (!matched) return;

    const modal = document.getElementById('pedagogical-modal');
    if (!modal) return;

    const setTxt = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val || ''; };
    setTxt('ped-modal-id', matched.id);
    setTxt('ped-modal-domain', matched.domain?.toUpperCase());
    setTxt('ped-modal-xp', `+${matched.xp || 15} XP`);
    setTxt('ped-modal-title', `${matched.icon || '📘'} ${matched.title}`);
    setTxt('ped-modal-subtitle', matched.subtitle);
    setTxt('ped-analogy-text', matched.analogia);
    setTxt('ped-disaster-text', matched.catastrofeFinanciera);
    setTxt('ped-concept-text', matched.karateConcept);

    setTxt('ped-anatomy-title', matched.fase1_anatomia?.title);
    setTxt('ped-anatomy-code', matched.fase1_anatomia?.code);
    setTxt('ped-anatomy-explanation', matched.fase1_anatomia?.explanation);

    setTxt('ped-trap-title', matched.fase2_trampa?.title);
    setTxt('ped-trap-code', matched.fase2_trampa?.badCode);
    setTxt('ped-trap-explanation', matched.fase2_trampa?.explanation);

    setTxt('ped-challenge-id', matched.fase3_reto?.exerciseId);
    setTxt('ped-challenge-prompt', matched.fase3_reto?.prompt);

    // Microservicios y Diagrama de Secuencia Arquitectónico
    const chipsEl = document.getElementById('ped-services-chips');
    if (chipsEl) {
      chipsEl.innerHTML = (matched.microservicios || []).map(s => `<span class="chip" style="font-size:0.75rem;">⚙️ ${this.escape(s)}</span>`).join('');
    }
    setTxt('ped-sequence-diagram', matched.diagramaSecuencia || 'Diagrama no disponible');

    // Botón de Cargar Reto
    const challengeBtn = document.getElementById('ped-load-challenge-btn');
    if (challengeBtn) {
      challengeBtn.onclick = () => {
        modal.close();
        this.loadExerciseById(matched.fase3_reto?.exerciseId);
      };
    }

    // Botón de Cargar Trampa (Fix Bug Mode)
    const brokenBtn = document.getElementById('ped-load-broken-btn');
    if (brokenBtn) {
      brokenBtn.onclick = () => {
        modal.close();
        this.loadBrokenTest(matched);
      };
    }

    // Botón de Descarga Directa de la Feature
    const dlBtn = document.getElementById('ped-download-feature-btn');
    if (dlBtn) {
      dlBtn.onclick = () => {
        window.open(`/api/v1/temas/${matched.id}/feature`, '_blank');
      };
    }

    this.switchPedTab('analogy');

    // Light dismiss fallback
    if (!('closedBy' in HTMLDialogElement.prototype) && !modal._hasLightDismiss) {
      modal._hasLightDismiss = true;
      modal.addEventListener('click', (event) => {
        if (event.target !== modal) return;
        const rect = modal.getBoundingClientRect();
        const inside = (
          rect.top <= event.clientY &&
          event.clientY <= rect.top + rect.height &&
          rect.left <= event.clientX &&
          event.clientX <= rect.left + rect.width
        );
        if (!inside) modal.close();
      });
    }

    modal.showModal();
  },

  switchPedTab(tabName) {
    const tabs = ['analogy', 'anatomy', 'trap', 'challenge'];
    tabs.forEach(t => {
      const btn = document.getElementById(`ped-tab-${t}`);
      const pane = document.getElementById(`ped-pane-${t}`);
      const isActive = (t === tabName);
      if (btn) {
        btn.classList.toggle('active', isActive);
        btn.classList.toggle('ghost', !isActive);
        btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
      }
      if (pane) {
        pane.hidden = !isActive;
      }
    });
  },

  loadChallengeFromTopic(temaId) {
    for (const lvl of this.temas) {
      const found = (lvl.temas || []).find(t => t.id === temaId);
      if (found && found.fase3_reto?.exerciseId) {
        this.loadExerciseById(found.fase3_reto.exerciseId);
        return;
      }
    }
  },

  loadBrokenTest(matched) {
    if (!matched.brokenTest) return;
    const bt = matched.brokenTest;
    this.currentExercise = {
      id: `BUG-${matched.id}`,
      levelName: 'MODO FIX-THE-BUG',
      domain: matched.domain,
      title: bt.title,
      enunciado: `Este test contiene un fallo o vulnerabilidad común. Diagnostica la causa raíz y corrígelo en el editor. Pista: ${bt.hint}`,
      codeWithGaps: bt.badScenario,
      hints: [bt.hint, `Objetivo esperado: ${bt.fixTarget}`],
      isBrokenTest: true,
      validSolution: bt.validSolution,
      fixTarget: bt.fixTarget
    };
    this.displayExercise(this.currentExercise);
    if (typeof updateBrunoStage === 'function') {
      updateBrunoStage('ALERT', '🔧 Modo Fix-the-Bug Activo', `Detecta el fallo en ${matched.id}: ${bt.hint}`);
    }
  },

  toggleTopicHub() {
    this.temasCollapsed = !this.temasCollapsed;
    const cards = document.getElementById('gym-topic-cards');
    const btn = document.getElementById('gym-topic-toggle-btn');
    if (cards) cards.hidden = this.temasCollapsed;
    if (btn) btn.textContent = this.temasCollapsed ? 'Expandir Temas ▼' : 'Colapsar ▲';
  },

  updateScoreUI() {
    const scoreEl = document.getElementById('gym-score-xp');
    const streakEl = document.getElementById('gym-score-streak');
    if (scoreEl) scoreEl.textContent = `${this.score} XP`;
    if (streakEl) streakEl.textContent = `🔥 ${this.streak}`;
  },

  escape(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoGym };
}
