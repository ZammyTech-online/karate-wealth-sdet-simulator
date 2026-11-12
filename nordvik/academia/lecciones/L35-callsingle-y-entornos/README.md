# L35 · karate.callSingle y Cache de Autenticación Enterprise

En suites grandes (con más de 500 tests), hacer login en cada Scenario destruye el servidor de IAM y ralentiza la regresión. `karate.callSingle()` ejecuta una rutina **una sola vez para toda la suite** y cachea el resultado en memoria.

## La norma
- Se usa típicamente en `karate-config.js`: `karate.callSingle('classpath:auth.feature', config)`.
- El objeto retornado es inmutable y compartido entre todos los hilos paralelos.

## El fundamento
Ahorra hasta un 90% del tiempo de ejecución en CI/CD y protege los límites de tasa (rate limiting) de IAM.
