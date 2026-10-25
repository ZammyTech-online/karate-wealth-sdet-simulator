function fn() {
  var env = karate.env || 'local';
  karate.log('karate.env active system property:', env);

  var config = {
    env: env,
    baseUrl: 'http://localhost:5125',
    advisorUser: 'advisor',
    advisorPass: 'NordicPass2026!',
    adminUser: 'admin',
    adminPass: 'RootAdmin2026!',
    customerUser: 'customer',
    customerPass: 'Customer123!'
  };

  if (env === 'dev') {
    config.baseUrl = 'https://dev-wealth.soderberg.internal';
  } else if (env === 'staging') {
    config.baseUrl = 'https://staging-wealth.soderberg.internal';
  }

  // Enterprise timeout settings (10s connect, 15s read)
  karate.configure('connectTimeout', 10000);
  karate.configure('readTimeout', 15000);
  karate.configure('logPrettyRequest', true);
  karate.configure('logPrettyResponse', true);

  return config;
}
