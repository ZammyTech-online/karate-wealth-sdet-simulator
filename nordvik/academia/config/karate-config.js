// Configuración global de la Academia: una URL por microservicio de la oleada 0.
// Karate ejecuta esta función antes de cada escenario y todas sus claves quedan como variables.
function fn() {
  var env = karate.env || 'local';
  var host = 'http://localhost';
  var config = {
    env: env,
    uuidUrl: host + ':6001',
    sequenceUrl: host + ':6002',
    clockUrl: host + ':6003',
    randomUrl: host + ':6004',
    catalogUrl: host + ':6801',
    marketUrl: host + ':6802',
    fxUrl: host + ':6803',
    calendarUrl: host + ':6804'
  };
  karate.configure('connectTimeout', 5000);
  karate.configure('readTimeout', 10000);
  karate.configure('logPrettyResponse', false);
  return config;
}
