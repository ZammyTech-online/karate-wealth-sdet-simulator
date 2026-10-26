# ADR-013 · Los sistemas externos se simulan como servicios propios

- **Estado:** aceptada (oleada 2, 2026-10-26)
- **Ámbito:** `externals/` (dominio X-external, puertos 7701-7712)
- **Sustituye a:** nada. Las ADR-001 a ADR-012 están en el documento "Plan del sistema y plan de Karate", pestaña 1.

## Contexto

Nordvik depende de sistemas que no son suyos: el eID de firma (NordID, tipo BankID), el custodio que ejecuta las órdenes,
el proveedor de precios, el banco (NordPay), y en la oleada 3 la Agencia de Pensiones, la agencia tributaria, el registro
de población, el proveedor de voz y el empleador.

Para que alguien aprenda a probar una plataforma así necesita **los mismos problemas que en la vida real**: latencia, caídas,
webhooks que llegan tarde o dos veces, firmas que tardan, ficheros con errores. Y tiene que poder **provocarlos a voluntad**.

Opciones consideradas:

| Opción | Pros | Contras |
|---|---|---|
| A · Mocks de Karate (`karate.start`) dentro de cada test | rápido, aislado | el sistema no funciona sin tests; cada alumno reinventa el mock; no hay flujo end-to-end |
| B · WireMock con stubs estáticos | conocido en la industria | no tiene estado (una firma no "termina"), webhooks pobres, otro runtime en 24 GB |
| **C · Simuladores .NET con estado dentro del mismo host** | se comportan como el real, tienen estado, mandan webhooks, viven en el mismo proceso (≈2 MB cada uno) | hay que escribirlos y mantenerlos |

## Decisión

**Opción C.** Cada sistema externo es un servicio de `externals/` que:

1. Implementa **la API del proveedor, no la nuestra**: nombres en inglés, sus códigos de error (`FORMAT_ERROR`, `alreadyInProgress`),
   sus formatos (camt.054 para avisos de abono). El adaptador de Nordvik (`P-integrations`) traduce.
2. Tiene **estado y tiempo**: una firma NordID pasa `outstandingTransaction → userSign → complete` según el reloj virtual;
   un custodio ejecuta a las 18:05; un abono entra en el extracto del día.
3. Expone **`/__admin/*`** para que los tests manipulen el mundo: `incoming-credits` (un cliente nos transfiere),
   `behaviour` (normal · down · error · slow · failNext) para inyectar fallos, y consultas de lo recibido.
4. **No cuenta** en los 156 microservicios: es "el mundo exterior". Sale en `nordvik status` y en la documentación con su
   propio dominio X-external.

## Consecuencias

- ✅ Los flujos E2E (ingreso, orden, firma) funcionan sin un solo test arrancado: puedes jugar desde Swagger o Bruno.
- ✅ Las pruebas de resiliencia son reales: `behaviour=down` en `custodian-sim` y compruebas que `order-router` reintenta y alerta.
- ✅ La academia enseña `retry until` contra algo que de verdad tarda (L18, L20), no contra un `sleep` disfrazado.
- ⚠️ Los simuladores **también llevan tests** (`ExternalIntegrationTests`, `AdapterTests`): si el simulador miente, la suite miente.
- ⚠️ Karate sigue teniendo su lección de mocks propios (`karate.start`) en el tramo 3, para cuando el alumno quiera aislar
  un servicio sin depender del simulador.

## Simuladores

| Puerto | Simulador | Simula a | Oleada |
|---|---|---|---|
| 7701 | nordid-sim | E1 · eID de autenticación y firma (tipo BankID) | 2 |
| 7704 | custodian-sim | E4 · custodio que ejecuta órdenes en lote | 2 |
| 7705 | pricefeed-sim | E5 · proveedor de NAV y precios | 2 |
| 7706 | nordpay-sim | E6 · banco: abonos entrantes, pagos salientes, extractos | 2 |
| 7702, 7703, 7707, 7709, 7710, 7712 | registro de población, agencia tributaria, pensiones, voz, empleador, sanciones | E2, E3, E7, E9, E10, E12 | 3 |
