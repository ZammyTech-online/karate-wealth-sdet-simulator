// BrunoAudioPlayer: Reproductor y Teleprompter con Sincronización Palabra a Palabra
// y Desafíos Predictivos Interactivos para Karate DSL.
'use strict';

const BrunoAudioPlayer = {
  audioEl: null,
  metadata: null,
  currentLessonId: null,
  isPlaying: false,
  predictiveEnabled: true,
  currentWordIndex: -1,
  activeChallenge: null,
  completedChallenges: new Set(),
  useWebSpeechFallback: false,
  webSpeechUtterance: null,

  init() {
    this.audioEl = new Audio();
    this.audioEl.preload = 'auto';

    // Eventos del elemento de audio HTML5
    this.audioEl.addEventListener('timeupdate', () => this.onTimeUpdate());
    this.audioEl.addEventListener('ended', () => this.onEnded());
    this.audioEl.addEventListener('pause', () => this.updatePlayButton(false));
    this.audioEl.addEventListener('play', () => this.updatePlayButton(true));
    this.audioEl.addEventListener('loadedmetadata', () => {
      this.updateTimeDisplay();
      const slider = document.getElementById('audio-seek-slider');
      if (slider) slider.max = this.audioEl.duration || 100;
    });

    // Enlazar controles de la interfaz
    const playBtn = document.getElementById('audio-play-btn');
    if (playBtn) playBtn.onclick = () => this.togglePlay();

    const slider = document.getElementById('audio-seek-slider');
    if (slider) {
      slider.oninput = (e) => {
        if (this.audioEl && this.audioEl.duration) {
          this.audioEl.currentTime = Number(e.target.value);
        }
      };
    }

    const speedSelect = document.getElementById('audio-speed-select');
    if (speedSelect) {
      speedSelect.onchange = (e) => {
        const rate = Number(e.target.value);
        if (this.audioEl) this.audioEl.playbackRate = rate;
      };
    }

    const predToggle = document.getElementById('audio-predictive-toggle');
    if (predToggle) {
      predToggle.onchange = (e) => {
        this.predictiveEnabled = e.target.checked;
      };
    }
  },

  async loadLesson(lessonId, rawMarkdown) {
    this.currentLessonId = lessonId;
    this.completedChallenges.clear();
    this.currentWordIndex = -1;
    this.activeChallenge = null;
    this.hidePredictiveChallenge();

    if (this.audioEl) {
      this.audioEl.pause();
      this.audioEl.currentTime = 0;
    }

    const voiceBadge = document.getElementById('audio-voice-badge');
    const audioBar = document.getElementById('acad-audio-bar');
    if (audioBar) audioBar.hidden = false;

    try {
      // 1. Intentar cargar metadatos de ElevenLabs pre-generados
      const res = await fetch(`/audio/${lessonId}.json`);
      if (res.ok) {
        this.metadata = await res.json();
        this.useWebSpeechFallback = false;
        this.audioEl.src = `/audio/${lessonId}.mp3`;
        if (voiceBadge) {
          voiceBadge.textContent = `🎙️ Bruno (ElevenLabs EN · ${this.metadata.voiceId ? 'Liam' : 'Studio'})`;
          voiceBadge.title = 'Audio generado en alta fidelidad con ElevenLabs con timestamps exactos';
        }
        this.renderKaraokeTheory(rawMarkdown);
        return;
      }
    } catch {
      // Ignorar fallo y pasar a fallback
    }

    // 2. Fallback elegante: Web Speech API del navegador (gratis, offline)
    this.useWebSpeechFallback = true;
    this.metadata = null;
    if (voiceBadge) {
      voiceBadge.textContent = '🎙️ Bruno (Voz Local del Navegador)';
      voiceBadge.title = 'Audio en tiempo real mediante síntesis nativa del navegador';
    }
  },

  togglePlay() {
    if (this.useWebSpeechFallback) {
      this.toggleWebSpeech();
      return;
    }

    if (!this.audioEl || !this.audioEl.src) return;

    if (this.audioEl.paused) {
      this.audioEl.play().catch(err => console.warn('Audio play blocked:', err));
      if (typeof updateBrunoStage === 'function') {
        updateBrunoStage('EXPLAINING', `Locución Activa · ${this.currentLessonId}`, 'Escuchando la lección explicada con rigor actuarial.');
      }
    } else {
      this.audioEl.pause();
      if (typeof updateBrunoStage === 'function') {
        updateBrunoStage('IDLE', `Bruno en Pausa · ${this.currentLessonId}`, 'Audio pausado. Pulsa reproducir para reanudar.');
      }
    }
  },

  updatePlayButton(playing) {
    this.isPlaying = playing;
    const btn = document.getElementById('audio-play-btn');
    if (btn) {
      btn.textContent = playing ? '⏸' : '▶';
      btn.setAttribute('aria-label', playing ? 'Pausar clase' : 'Reproducir clase');
    }
  },

  onTimeUpdate() {
    if (!this.audioEl || this.useWebSpeechFallback) return;

    const cur = this.audioEl.currentTime;
    const dur = this.audioEl.duration || 0;

    // Actualizar slider y tiempo
    const slider = document.getElementById('audio-seek-slider');
    if (slider && !slider.matches(':active')) {
      slider.value = cur;
    }
    this.updateTimeDisplay();

    // 1. Sincronización de palabra activa (Word-level Teleprompter)
    if (this.metadata && this.metadata.words) {
      const words = this.metadata.words;
      let matchedIdx = -1;

      // Buscar palabra correspondiente al segundo actual
      for (let i = 0; i < words.length; i++) {
        if (cur >= words[i].start && cur <= words[i].end + 0.15) {
          matchedIdx = i;
          break;
        }
      }

      if (matchedIdx !== -1 && matchedIdx !== this.currentWordIndex) {
        this.highlightWord(matchedIdx);
      }
    }

    // 2. Disparador de retos predictivos interactivos
    if (this.predictiveEnabled && this.metadata && this.metadata.challenges) {
      for (const ch of this.metadata.challenges) {
        if (!this.completedChallenges.has(ch.id) && cur >= ch.time && cur <= ch.time + 1.2) {
          this.triggerPredictiveChallenge(ch);
          break;
        }
      }
    }
  },

  highlightWord(idx) {
    // Quitar resalte anterior
    if (this.currentWordIndex !== -1) {
      const oldEl = document.getElementById(`kw-${this.currentWordIndex}`);
      if (oldEl) oldEl.classList.remove('kw-active');
    }

    this.currentWordIndex = idx;
    const newEl = document.getElementById(`kw-${idx}`);
    if (newEl) {
      newEl.classList.add('kw-active');
      // Auto-scroll suave
      newEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  },

  triggerPredictiveChallenge(ch) {
    this.activeChallenge = ch;
    this.completedChallenges.add(ch.id);

    // Pausar audio automáticamente
    if (this.audioEl) this.audioEl.pause();

    // Bruno cambia a estado de alerta/pensamiento
    if (typeof updateBrunoStage === 'function') {
      updateBrunoStage('ALERT', '🎯 ¡Desafío Predictivo!', 'Anticípate a la sintaxis antes de continuar escuchando.');
    }

    const card = document.getElementById('acad-predictive-card');
    const promptEl = document.getElementById('predictive-prompt');
    const optionsBox = document.getElementById('predictive-options');
    const feedbackEl = document.getElementById('predictive-feedback');

    if (!card || !promptEl || !optionsBox) return;

    promptEl.textContent = ch.prompt;
    feedbackEl.hidden = true;
    optionsBox.innerHTML = '';

    ch.options.forEach((opt, optIdx) => {
      const btn = document.createElement('button');
      btn.className = 'btn quiet small';
      btn.style.textAlign = 'left';
      btn.style.justifyContent = 'flex-start';
      btn.style.padding = '8px 12px';
      btn.innerHTML = `<strong>${String.fromCharCode(65 + optIdx)}.</strong> ${this.escape(opt)}`;
      btn.onclick = () => this.handleChallengeAnswer(optIdx, btn);
      optionsBox.appendChild(btn);
    });

    card.hidden = false;
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });
  },

  handleChallengeAnswer(selectedIdx, clickedBtn) {
    const ch = this.activeChallenge;
    if (!ch) return;

    const feedbackEl = document.getElementById('predictive-feedback');
    const buttons = document.querySelectorAll('#predictive-options button');
    buttons.forEach(b => b.disabled = true);

    const isCorrect = selectedIdx === ch.answerIndex;

    if (isCorrect) {
      clickedBtn.style.background = 'var(--ok-soft)';
      clickedBtn.style.borderColor = 'var(--ok)';
      clickedBtn.style.color = 'var(--ok)';

      if (feedbackEl) {
        feedbackEl.hidden = false;
        feedbackEl.style.background = 'var(--ok-soft)';
        feedbackEl.style.color = 'var(--ok)';
        feedbackEl.innerHTML = `✅ <strong>¡Correcto!</strong> ${this.escape(ch.explanation)}`;
      }

      if (typeof updateBrunoStage === 'function') {
        updateBrunoStage('SUCCESS', '¡Predicción Acertada!', ch.explanation);
      }

      // Reanudar automáticamente tras 2 segundos
      setTimeout(() => {
        this.hidePredictiveChallenge();
        if (this.audioEl) this.audioEl.play().catch(() => {});
      }, 2000);

    } else {
      clickedBtn.style.background = 'var(--bad-soft)';
      clickedBtn.style.borderColor = 'var(--bad)';
      clickedBtn.style.color = 'var(--bad)';

      if (feedbackEl) {
        feedbackEl.hidden = false;
        feedbackEl.style.background = 'var(--bad-soft)';
        feedbackEl.style.color = 'var(--bad)';
        feedbackEl.innerHTML = `❌ <strong>Casi.</strong> La opción esperada era: <code>${this.escape(ch.options[ch.answerIndex])}</code>.<br/><small>${this.escape(ch.explanation)}</small>`;
      }

      if (typeof updateBrunoStage === 'function') {
        updateBrunoStage('ALERT', 'Aserción Descalibrada', ch.explanation);
      }

      // Añadir botón para continuar
      const continueBtn = document.createElement('button');
      continueBtn.className = 'btn small ghost';
      continueBtn.style.marginTop = '8px';
      continueBtn.textContent = 'Reanudar Audio';
      continueBtn.onclick = () => {
        this.hidePredictiveChallenge();
        if (this.audioEl) this.audioEl.play().catch(() => {});
      };
      feedbackEl.appendChild(continueBtn);
    }
  },

  hidePredictiveChallenge() {
    const card = document.getElementById('acad-predictive-card');
    if (card) card.hidden = true;
    this.activeChallenge = null;
  },

  onEnded() {
    this.updatePlayButton(false);
    this.highlightWord(-1);
    if (typeof updateBrunoStage === 'function') {
      updateBrunoStage('SUCCESS', `Clase Completada · ${this.currentLessonId}`, 'Has terminado la sesión auditiva. ¡Pasa ahora al editor de ejercicios!');
    }
  },

  updateTimeDisplay() {
    const cur = this.formatTime(this.audioEl?.currentTime || 0);
    const dur = this.formatTime(this.audioEl?.duration || 0);
    const label = document.getElementById('audio-time-label');
    if (label) label.textContent = `${cur} / ${dur}`;
  },

  formatTime(secs) {
    if (isNaN(secs)) return '00:00';
    const m = Math.floor(secs / 60);
    const s = Math.floor(secs % 60);
    return `${m < 10 ? '0' : ''}${m}:${s < 10 ? '0' : ''}${s}`;
  },

  renderKaraokeTheory(md) {
    const container = document.getElementById('acad-pane-theory');
    if (!container) return;

    if (!this.metadata || !this.metadata.words || this.metadata.words.length === 0) {
      return;
    }

    // Dividir palabras del markdown e inyectar spans data-w sincronizados
    const words = this.metadata.words;
    let wordIdx = 0;

    // Sustituir palabras en el texto asegurando spans enriquecidos
    const paragraphs = md.split(/\n\n+/);
    const htmlParts = paragraphs.map(para => {
      if (para.startsWith('#')) {
        return `<h3 style="color:var(--accent); margin:12px 0 6px 0;">${this.escape(para.replace(/^#+\s*/, ''))}</h3>`;
      }
      if (para.startsWith('```')) {
        return `<pre><code class="language-gherkin">${this.escape(para.replace(/```\w*\n?|```$/g, ''))}</code></pre>`;
      }
      
      const tokens = para.split(/(\s+)/);
      const spanTokens = tokens.map(t => {
        if (/^\s+$/.test(t)) return t;
        if (wordIdx < words.length) {
          const wId = wordIdx++;
          return `<span class="kw" id="kw-${wId}" data-idx="${wId}">${this.escape(t)}</span>`;
        }
        return this.escape(t);
      });

      return `<p style="margin:8px 0; line-height:1.6;">${spanTokens.join('')}</p>`;
    });

    container.innerHTML = htmlParts.join('');
  },

  toggleWebSpeech() {
    if (!('speechSynthesis' in window)) {
      alert('Tu navegador no soporta síntesis de voz Web Speech.');
      return;
    }

    if (window.speechSynthesis.speaking) {
      window.speechSynthesis.cancel();
      this.updatePlayButton(false);
      return;
    }

    const theoryText = document.getElementById('acad-pane-theory')?.innerText || '';
    if (!theoryText) return;

    this.webSpeechUtterance = new SpeechSynthesisUtterance(theoryText);
    this.webSpeechUtterance.lang = 'en-US';
    this.webSpeechUtterance.rate = 1.0;

    this.webSpeechUtterance.onstart = () => this.updatePlayButton(true);
    this.webSpeechUtterance.onend = () => this.updatePlayButton(false);
    this.webSpeechUtterance.onerror = () => this.updatePlayButton(false);

    window.speechSynthesis.speak(this.webSpeechUtterance);
  },

  escape(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoAudioPlayer };
}
