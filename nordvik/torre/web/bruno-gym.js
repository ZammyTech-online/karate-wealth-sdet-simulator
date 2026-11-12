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

  _initialized: false,

  async init() {
    if (this._initialized) return;
    this._initialized = true;
    this.bindEvents();
    await this.loadCatalog();
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
        this.loadRandomExercise(this.currentLevel);
      };
    });

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
