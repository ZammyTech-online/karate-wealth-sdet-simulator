# L34 · Servidores Mock Dinámicos con karate.start

Cuando un sistema bancario depende de una pasarela externa (como un regulador fiscal o una agencia de crédito que cobra por consulta o se cae en sandbox), un Senior SDET no bloquea la suite: levanta un **Mock Server dinámico** con Karate dentro de la propia ejecución.

## La norma
- `karate.start('mock-server.feature')` arranca un servidor HTTP embebido en un puerto libre o asignado.
- El objeto retornado expone `mock.port`, permitiendo configurar la URL del test hacia `http://localhost:' + mock.port`.
- Al finalizar el test o feature, `mock.stop()` libera el socket.

## El fundamento
Permite simular fallos de red, códigos HTTP 500/503 y retrasos extremos de forma determinista y sin depender de la nube.
