// BrunoAvatar: Stickman 2D minimalista en SVG vectorial escalable con estados reactivos.
// Identidad: Cabeza blanca redonda calva, camiseta negra de cuello redondo y gafas circulares neón cian.
'use strict';

const BrunoAvatar = {
  render(state = 'IDLE', size = 120) {
    const states = {
      IDLE: {
        mouth: '<line x1="44" y1="50" x2="56" y2="50" stroke="#1e293b" stroke-width="2" stroke-linecap="round" />',
        leftArm: '<line x1="32" y1="72" x2="18" y2="92" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />',
        rightArm: '<line x1="68" y1="72" x2="82" y2="92" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />',
        glassesGlow: '#06b6d4',
        extra: ''
      },
      THINKING: {
        mouth: '<path d="M 46 51 Q 50 48 54 51" fill="none" stroke="#1e293b" stroke-width="2" stroke-linecap="round" />',
        leftArm: '<line x1="32" y1="72" x2="22" y2="95" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />',
        rightArm: '<path d="M 68 72 L 78 58 L 56 46" fill="none" stroke="#0f172a" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" />',
        glassesGlow: '#22d3ee',
        extra: '<circle cx="82" cy="22" r="3" fill="#06b6d4" opacity="0.6"/><circle cx="90" cy="14" r="5" fill="#06b6d4" opacity="0.8"/>'
      },
      EXPLAINING: {
        mouth: '<ellipse cx="50" cy="51" rx="4" ry="2.5" fill="#1e293b" />',
        leftArm: '<line x1="32" y1="72" x2="16" y2="88" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />',
        rightArm: '<line x1="68" y1="72" x2="94" y2="52" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />',
        glassesGlow: '#06b6d4',
        extra: '<circle cx="95" cy="51" r="3" fill="#22d3ee"/>'
      },
      ALERT: {
        mouth: '<ellipse cx="50" cy="53" rx="5" ry="5" fill="#b91c1c" />',
        leftArm: '<path d="M 32 72 L 18 50 L 34 38" fill="none" stroke="#0f172a" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" />',
        rightArm: '<path d="M 68 72 L 82 50 L 66 38" fill="none" stroke="#0f172a" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" />',
        glassesGlow: '#ef4444',
        extra: '<path d="M 50 10 L 50 20 M 50 24 L 50 26" stroke="#ef4444" stroke-width="3" stroke-linecap="round"/>'
      },
      SUCCESS: {
        mouth: '<path d="M 43 48 Q 50 56 57 48" fill="none" stroke="#059669" stroke-width="2.5" stroke-linecap="round" />',
        leftArm: '<line x1="32" y1="72" x2="18" y2="92" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />',
        rightArm: '<path d="M 68 72 L 85 62 L 85 45" fill="none" stroke="#0f172a" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" />',
        glassesGlow: '#10b981',
        extra: '<polygon points="85,42 81,48 89,48" fill="#10b981" />'
      }
    };

    const normalizedState = states[state] ? state : 'IDLE';
    const s = states[normalizedState];

    return `
      <svg viewBox="0 0 100 120" width="${size}" height="${size * 1.2}" class="bruno-avatar bruno-${normalizedState.toLowerCase()}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Avatar de Bruno el SDET en estado ${normalizedState}">
        <!-- Piernas -->
        <line x1="42" y1="95" x2="36" y2="116" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />
        <line x1="58" y1="95" x2="64" y2="116" stroke="#0f172a" stroke-width="4" stroke-linecap="round" />
        
        <!-- Torso con Camiseta Negra cuello redondo -->
        <path d="M 32 65 Q 50 62 68 65 L 66 96 L 34 96 Z" fill="#0f172a" />
        <path d="M 43 65 Q 50 70 57 65" fill="none" stroke="#ffffff" stroke-width="1.5" />
        
        <!-- Brazos -->
        ${s.leftArm}
        ${s.rightArm}

        <!-- Cabeza Blanca Redonda Calva -->
        <circle cx="50" cy="40" r="18" fill="#ffffff" stroke="#0f172a" stroke-width="2.5" />

        <!-- Gafas Circulares Neón Cian -->
        <!-- Ojo/Cristal Izquierdo -->
        <circle cx="43" cy="38" r="6.5" fill="none" stroke="${s.glassesGlow}" stroke-width="2.2" style="filter: drop-shadow(0 0 3px ${s.glassesGlow});" />
        <circle cx="43" cy="38" r="2.2" fill="#0f172a" />
        <!-- Ojo/Cristal Derecho -->
        <circle cx="57" cy="38" r="6.5" fill="none" stroke="${s.glassesGlow}" stroke-width="2.2" style="filter: drop-shadow(0 0 3px ${s.glassesGlow});" />
        <circle cx="57" cy="38" r="2.2" fill="#0f172a" />
        <!-- Puente de las gafas -->
        <line x1="49.5" y1="38" x2="50.5" y2="38" stroke="${s.glassesGlow}" stroke-width="2.2" />

        <!-- Boca -->
        ${s.mouth}

        <!-- Efectos de estado -->
        ${s.extra}
      </svg>
    `;
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoAvatar };
}
