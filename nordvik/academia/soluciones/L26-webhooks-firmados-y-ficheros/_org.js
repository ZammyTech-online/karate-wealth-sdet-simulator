// Ayudante: un número de organización sueco válido y distinto cada vez (NNNNNN-NNNN, tercer dígito >= 2, Luhn).
// Uso:  * def org = read('_org.js')
//       * def numero = org()
function() {
  var d = [5];
  for (var i = 1; i < 9; i++) d.push(Math.floor(Math.random() * 10));
  d[2] = 2 + Math.floor(Math.random() * 8);
  var sum = 0;
  for (var i = 0; i < 9; i++) { var x = d[i] * (i % 2 == 0 ? 2 : 1); sum += x > 9 ? x - 9 : x; }
  d.push((10 - sum % 10) % 10);
  var s = d.join('');
  return s.substring(0, 6) + '-' + s.substring(6);
}
