// Configuración global de la Academia: una URL por microservicio y los usuarios de prueba.
// Karate ejecuta esta función antes de cada escenario y todas sus claves quedan como variables.
// Qué servicios necesita cada lección lo dice la escalera (torre/escalera.json): nivel 0 = L00-L15, nivel 1 = L16-L17,
// nivel 2 = L18-L21, nivel 3 = L22-L27 y nivel 4 = L28-L33. academia.ps1 se lo pide a la Torre de control (puerto 3000).
function fn() {
  var env = karate.env || 'local';
  var host = 'http://localhost';
  var config = {
    env: env,
    // Oleada 0 · utilidades y mercado
    uuidUrl: host + ':6001',
    sequenceUrl: host + ':6002',
    clockUrl: host + ':6003',
    randomUrl: host + ':6004',
    personUrl: host + ':6005',
    ibanUrl: host + ':6007',
    catalogUrl: host + ':6801',
    marketUrl: host + ':6802',
    fxUrl: host + ':6803',
    calendarUrl: host + ':6804',
    // Oleadas 1 y 2 · identidad, clientes, contratos, inversión y pagos
    iamUrl: host + ':6200',
    customerUrl: host + ':6300',
    notificationUrl: host + ':6600',
    contractsUrl: host + ':6700',
    vaultUrl: host + ':6704',
    compositionUrl: host + ':6107',
    portfolioUrl: host + ':6900',
    ordersUrl: host + ':6902',
    paymentsUrl: host + ':7000',
    cashUrl: host + ':7004',
    nordpaySimUrl: host + ':7706',
    // Oleada 3 · privacidad, calidad, cumplimiento, voz y pensiones
    piiVaultUrl: host + ':6500',
    consentsUrl: host + ':6502',
    passportsUrl: host + ':6503',
    chaosUrl: host + ':7600',
    mockControlUrl: host + ':7601',
    eventSpyUrl: host + ':7602',
    sanctionsUrl: host + ':6401',
    sanctionSimUrl: host + ':7703',
    voiceUrl: host + ':6606',
    pensionPlansUrl: host + ':7100',
    employerPortalUrl: host + ':7102',
    employerFilesUrl: host + ':7103',
    transfersUrl: host + ':7108',
    clearingAdapterUrl: host + ':7505',
    clearingSimUrl: host + ':7709',
    providerSimUrl: host + ':7710',
    // Oleada 4 · sistemas heredados, aseguradora, contratos, calidad y RGPD
    soapUrl: host + ':7510',
    mainframeUrl: host + ':7509',
    insurerSimUrl: host + ':7711',
    insurerAdapterUrl: host + ':7508',
    schemaRegistryUrl: host + ':6111',
    contractCheckUrl: host + ':7603',
    testInsightsUrl: host + ':7604',
    erasureUrl: host + ':6507',
    // Ids de los datos semilla (siempre los mismos)
    ids: {
      ana: 'c0000000-0000-4000-8000-000000000001',
      lars: 'c0000000-0000-4000-8000-000000000002',
      sofia: 'c0000000-0000-4000-8000-000000000003',
      carteraAna: 'p0000000-0000-4000-8000-000000000001'
    },
    // Usuarios de prueba: call read('_login.feature') usuarios.erik
    usuarios: {
      ana: { usuario: 'cliente.ana', clave: 'Ana2026!' },
      lars: { usuario: 'cliente.lars', clave: 'Lars2026!' },
      erik: { usuario: 'asesor.erik', clave: 'Erik2026!' },
      maja: { usuario: 'asesora.maja', clave: 'Maja2026!' },
      olof: { usuario: 'operaciones.olof', clave: 'Olof2026!' },
      karin: { usuario: 'compliance.karin', clave: 'Karin2026!' },
      admin: { usuario: 'admin', clave: 'Admin2026!' }
    }
  };
  karate.configure('connectTimeout', 5000);
  karate.configure('readTimeout', 15000);
  karate.configure('logPrettyResponse', false);
  return config;
}
