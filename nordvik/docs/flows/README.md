# Flujos de negocio de Nordvik (oleadas 1 a 4)

Cada flujo dice **quién llama a quién**, **qué evento sale** y **dónde lo pruebas**. Los eventos viajan siempre igual:
el productor los escribe en su *outbox* → `outbox-relay` (7401) los publica en `event-bus` (7400) → cada consumidor los
recoge con su cursor (reintentos y tabla de fallos incluidos). Por eso todo lo que va después de un evento es **eventual**:
en tus tests se espera con `retry until` (academia L18).

| Flujo | Qué pasa | Test de API | Lección |
|---|---|---|---|
| [F-01 · Ingreso de dinero](F-01-ingreso.md) | El cliente transfiere con su referencia OCR y el dinero aparece en su caja | `PaymentsTests` | L18 |
| [F-02 · Orden de compra y ejecución](F-02-orden.md) | Orden idempotente → lote al custodio → ejecución a las 18:05 → posiciones | `TradingTests` | L19 |
| [F-03 · Contrato y firma](F-03-contrato.md) | Borrador → envío → firma NordID → ACTIVE | `ContractTests` | L20 |
| [F-04 · Composición contract-bundle](F-04-composicion.md) | Contrato + anexo en un solo JSON | `CompositionTests` | L21 |
| [F-05 · Segundo factor y step-up](F-05-mfa-step-up.md) | Código MFA al buzón y token acr=high para operaciones sensibles | `ExternalIntegrationTests` | L16 |
| [F-06 · Fichero mensual de un empleador](F-06-fichero-empleador.md) | Webhook firmado del clearing → validación línea a línea → aportaciones → acuse | `EmployerPensionTests` | L26 |
| [F-07 · Traspaso entrante](F-07-traspaso.md) | Saga con valoración en días virtuales, firma mutua, aprobación y tolerancia del 2 % | `TransferTests` | L27 |
| [F-08 · Cobro y fallecimiento](F-08-cobro-y-fallecimiento.md) | Renta mensual con retención, congelación y reparto entre beneficiarios | `PayoutTests`, `DeathClaimTests` | — |
| [F-09 · Seguro ligado a la pensión](F-09-seguro-ligado-a-la-pension.md) | Cuestionario efímero, suscripción OAuth2 con la aseguradora, póliza, siniestro y pago | `InsuranceTests` | L30 |
| [F-10 · Fiscalidad](F-10-fiscalidad.md) | Retención por tabla municipal o SINK, informe anual a la Agencia, certificados y extractos | `TaxTests` | — |
| [F-11 · Derechos RGPD](F-11-derechos-rgpd.md) | Borrado con crypto-shredding, acceso y portabilidad, brechas en 72 h | `DataRightsTests`, `GovernanceTests` | L33 |
| [F-12 · Sistemas heredados](F-12-sistemas-heredados.md) | Mainframe de ancho fijo con códigos rc y pensiones antiguas en SOAP | `LegacyAndQualityTests` | L28, L29 |
