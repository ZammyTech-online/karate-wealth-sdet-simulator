// Ayudante: HMAC-SHA256 en hexadecimal (minúsculas), como lo calculan el clearing y Nordvik.
// Uso:  * def hmac = read('_hmac.js')
//       * def firma = hmac('secreto', texto)
function(secret, text) {
  var Mac = Java.type('javax.crypto.Mac');
  var Spec = Java.type('javax.crypto.spec.SecretKeySpec');
  var JString = Java.type('java.lang.String');
  var mac = Mac.getInstance('HmacSHA256');
  mac.init(new Spec(new JString(secret).getBytes('UTF-8'), 'HmacSHA256'));
  var bytes = mac.doFinal(new JString(text).getBytes('UTF-8'));
  var hex = '';
  for (var i = 0; i < bytes.length; i++) {
    var h = (bytes[i] & 0xff).toString(16);
    hex += (h.length == 1 ? '0' : '') + h;
  }
  return hex;
}
