// gym.js: Alias de compatibilidad hacia bruno-gym.js
'use strict';
if (typeof window !== 'undefined' && typeof BrunoGym === 'undefined') {
  // Asegura disponibilidad global si se carga directamente
  console.info('BrunoGym: cargado vía alias gym.js');
}
