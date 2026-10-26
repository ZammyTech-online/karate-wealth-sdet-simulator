# Catálogo de eventos

Cada servicio guarda sus eventos en su **outbox**; `outbox-relay` los publica en `event-bus` (puerto 7400) y los consumidores los leen con `GET /api/v1/events?topics=...&after=<cursor>`. Puedes suscribir un mock de Karate con `POST /api/v1/subscriptions`.

Sobre (envelope) de todos los eventos: `eventId`, `seq`, `topic`, `key`, `producer`, `occurredAt`, `correlationId`, `schemaVersion`, `payload`.

| Tema | Publica | Consumen |
| --- | --- | --- |
| `appropriateness.assessed` | appropriateness-service | audit-service |
| `appropriateness.warning-acknowledged` | appropriateness-service | audit-service |
| `approval.granted` | approval-service | audit-service |
| `approval.rejected` | approval-service | audit-service |
| `approval.requested` | approval-service | audit-service |
| `archive.record-added` | archive-service | audit-service |
| `bank-account.rejected` | bank-account-verification | audit-service |
| `bank-account.verified` | bank-account-verification | audit-service |
| `beneficiaries.updated` | beneficiary-service | audit-service |
| `breach.authority-notified` | breach-service | audit-service |
| `breach.closed` | breach-service | audit-service |
| `breach.reported` | breach-service | audit-service |
| `case.opened` | case-management | audit-service |
| `case.resolved` | case-management | audit-service |
| `complaint.received` | complaints-service | audit-service |
| `complaint.resolved` | complaints-service | audit-service |
| `consent.granted` | consent-ledger | audit-service |
| `consent.withdrawn` | consent-ledger | audit-service |
| `contract.activated` | contract-service | audit-service, onboarding-orchestrator |
| `contract.created` | contract-service | audit-service |
| `contract.expired` | contract-service | audit-service, onboarding-orchestrator |
| `contract.invalidated` | contract-service | audit-service |
| `contract.sent` | contract-service | audit-service, notification-service |
| `contract.signed` | contract-service | audit-service |
| `contract.superseded` | contract-service | audit-service |
| `contract.terminated` | contract-service | audit-service |
| `contract.withdrawn` | contract-service | audit-service |
| `contribution.received` | contribution-service | audit-service |
| `corporate-action.announced` | corporate-actions | audit-service |
| `corporate-action.applied` | corporate-actions | audit-service, position-keeping |
| `custody.execution.received` | custody-adapter | audit-service, execution-service |
| `custody.order.rejected` | custody-adapter | audit-service, execution-service |
| `death-claim.opened` | death-claims | audit-service |
| `death-claim.settled` | death-claims | audit-service |
| `death-claim.verified` | death-claims | audit-service |
| `delegation.created` | delegation-service | audit-service |
| `delegation.revoked` | delegation-service | audit-service |
| `delegation.verified` | delegation-service | audit-service |
| `deposit.received` | payments-gateway | audit-service, notification-service, transaction-monitoring |
| `device.revoked` | device-trust | audit-service |
| `device.trusted` | device-trust | audit-service |
| `document.destroyed` | document-vault | audit-service |
| `document.stored` | document-vault | audit-service |
| `dsar.completed` | dsar-service | audit-service |
| `employer-file.processed` | employer-file-ingest | audit-service |
| `employment.changed` | employment-service | audit-service |
| `erasure.blocked` | erasure-orchestrator | audit-service |
| `erasure.completed` | erasure-orchestrator | audit-service |
| `fees.billed` | fee-billing | audit-service |
| `health-declaration.completed` | health-declaration | audit-service |
| `household.updated` | household-service | audit-service |
| `insurance-claim.paid` | insurance-claims | audit-service |
| `insurance-claim.rejected` | insurance-claims | audit-service |
| `insurance-claim.submitted` | insurance-claims | audit-service |
| `insurance-policy.issued` | insurance-policy | audit-service |
| `insurance-policy.status-changed` | insurance-policy | audit-service |
| `key.created` | key-management | audit-service |
| `key.rotated` | key-management | audit-service |
| `key.shredded` | key-management | audit-service, pii-vault |
| `kyc.approved` | kyc-aml-service | audit-service |
| `kyc.rejected` | kyc-aml-service | audit-service |
| `kyc.review-required` | kyc-aml-service | audit-service, case-management |
| `ledger.entry.posted` | ledger-service | audit-service |
| `meeting.booked` | meeting-scheduler | audit-service |
| `meeting.cancelled` | meeting-scheduler | audit-service |
| `message.destroyed` | secure-messaging | audit-service |
| `message.sent` | secure-messaging | audit-service |
| `monitoring.alert.closed` | transaction-monitoring | audit-service |
| `monitoring.alert.raised` | transaction-monitoring | audit-service, case-management |
| `notification.sent` | notification-service | audit-service |
| `onboarding.completed` | onboarding-orchestrator | audit-service |
| `onboarding.failed` | onboarding-orchestrator | audit-service |
| `order.cancelled` | order-management | audit-service |
| `order.created` | order-management | audit-service |
| `order.executed` | order-management | audit-service, notification-service, rebalancing-engine |
| `order.rejected` | order-management | audit-service, rebalancing-engine |
| `order.sent` | order-management | audit-service |
| `passport.issued` | data-passport | audit-service, lineage-service |
| `passport.read` | data-passport | audit-service, lineage-service |
| `passport.revoked` | data-passport | audit-service, lineage-service |
| `payment.bank-status.changed` | nordpay-adapter | audit-service, payment-instructions |
| `payment.credit.received` | nordpay-adapter | audit-service, payments-gateway |
| `payment.instruction.rejected` | payment-instructions | audit-service, payments-gateway, refunds-returns |
| `payment.instruction.settled` | payment-instructions | audit-service, payments-gateway |
| `payment.rejected` | payments-gateway | audit-service, notification-service |
| `payment.settled` | payments-gateway | audit-service, notification-service, transaction-monitoring |
| `payout-plan.created` | payout-planner | audit-service |
| `payout-plan.status-changed` | payout-planner | audit-service |
| `payout-run.completed` | payout-execution | audit-service |
| `payout.paid` | payout-execution | audit-service |
| `pension-plan.created` | pension-plan-service | audit-service |
| `pension-plan.frozen` | pension-plan-service | audit-service |
| `pension-plan.status-changed` | pension-plan-service | audit-service |
| `pep.identified` | pep-service | audit-service |
| `pii.detokenized` | pii-vault | audit-service, lineage-service |
| `pii.tokenized` | pii-vault | audit-service, lineage-service |
| `population.deceased-detected` | population-adapter | audit-service |
| `position.changed` | position-keeping | audit-service |
| `price.nav.published` | price-feed-adapter | audit-service |
| `rebalance.completed` | rebalancing-engine | audit-service |
| `rebalance.failed` | rebalancing-engine | audit-service |
| `rebalance.started` | rebalancing-engine | audit-service |
| `recommendation.accepted` | advisor-desk | audit-service |
| `recommendation.declined` | advisor-desk | audit-service |
| `recommendation.proposed` | advisor-desk | audit-service |
| `reconciliation.completed` | reconciliation-service | audit-service |
| `refund.issued` | refunds-returns | audit-service |
| `regulatory-report.submitted` | regulatory-reporting | audit-service |
| `retention.expired` | retention-scheduler | audit-service |
| `risk.scored` | risk-scoring | audit-service |
| `salary-exchange.created` | salary-exchange-service | audit-service |
| `screening.completed` | sanctions-adapter | audit-service |
| `screening.decided` | sanctions-adapter | audit-service |
| `secret.read` | secrets-service | audit-service |
| `secret.rotated` | secrets-service | audit-service |
| `signing.completed` | signing-service | audit-service |
| `signing.failed` | signing-service | audit-service |
| `suitability.assessed` | suitability-service | audit-service |
| `suitability.checked` | suitability-service | audit-service |
| `task.completed` | task-queue | audit-service |
| `task.created` | task-queue | audit-service |
| `tax-report.filed` | tax-reporting | audit-service |
| `tax-residency.changed` | tax-residency-service | audit-service |
| `token.exchanged` | token-exchange | audit-service |
| `trade.executed` | execution-service | audit-service, order-management, position-keeping |
| `trade.rejected` | execution-service | audit-service, order-management |
| `transfer-valuation.ready` | transfer-valuation | audit-service |
| `transfer.completed` | transfer-service | audit-service |
| `transfer.requested` | transfer-service | audit-service |
| `transfer.status-changed` | transfer-service | audit-service |
| `voice.call.ended` | voice-service | audit-service, transcription-service |
| `voice.call.shredded` | voice-service | audit-service, transcription-service, insight-extraction |
| `voice.call.started` | voice-service | audit-service |
| `voice.call.summarised` | call-summary | audit-service, voice-service |
| `voice.insights.extracted` | insight-extraction | audit-service, voice-service |
| `voice.recording.revoked` | voice-service | audit-service |
| `voice.review.completed` | voice-service | audit-service, advisor-desk, call-summary |
| `voice.transcript.redacted` | transcription-service | audit-service, voice-service, insight-extraction |
| `withdrawal.requested` | payments-gateway | audit-service |

## appropriateness.assessed

Publica: appropriateness-service · Consumen: audit-service

## appropriateness.warning-acknowledged

Publica: appropriateness-service · Consumen: audit-service

## approval.granted

Publica: approval-service · Consumen: audit-service

## approval.rejected

Publica: approval-service · Consumen: audit-service

## approval.requested

Publica: approval-service · Consumen: audit-service

## archive.record-added

Publica: archive-service · Consumen: audit-service

## bank-account.rejected

Publica: bank-account-verification · Consumen: audit-service

## bank-account.verified

Publica: bank-account-verification · Consumen: audit-service

## beneficiaries.updated

Publica: beneficiary-service · Consumen: audit-service

## breach.authority-notified

Publica: breach-service · Consumen: audit-service

## breach.closed

Publica: breach-service · Consumen: audit-service

## breach.reported

Publica: breach-service · Consumen: audit-service

## case.opened

Publica: case-management · Consumen: audit-service

## case.resolved

Publica: case-management · Consumen: audit-service

## complaint.received

Publica: complaints-service · Consumen: audit-service

## complaint.resolved

Publica: complaints-service · Consumen: audit-service

## consent.granted

Publica: consent-ledger · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_ee9e39df1c46460bb5104ddb694cbf3b",
  "seq": 322,
  "topic": "consent.granted",
  "key": "48eedc09-915b-431d-8dea-1543c2d6357e",
  "producer": "consent-ledger",
  "occurredAt": "2026-10-26T06:44:48.585+01:00",
  "correlationId": "corr-9709f84d5da04ad5",
  "schemaVersion": 1,
  "payload": {
    "customerId": "48eedc09-915b-431d-8dea-1543c2d6357e",
    "purpose": "VOICE_RECORDING",
    "channel": "ADVISOR",
    "entryId": "cns_d3ca36c24e0d4242"
  }
}
```

## consent.withdrawn

Publica: consent-ledger · Consumen: audit-service

## contract.activated

Publica: contract-service · Consumen: audit-service, onboarding-orchestrator

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_da5a611e6eac4a4daa7ca709a253e615",
  "seq": 42,
  "topic": "contract.activated",
  "key": "9b50b049-b0d8-4108-a45a-872db3d7999e",
  "producer": "contract-service",
  "occurredAt": "2026-10-26T04:30:08.686+01:00",
  "correlationId": "corr-41f20af97d3249d4",
  "schemaVersion": 1,
  "payload": {
    "contractId": "9b50b049-b0d8-4108-a45a-872db3d7999e",
    "customerId": "c0000000-0000-4000-8000-000000000001",
    "type": "MASTER_AGREEMENT",
    "amends": null
  }
}
```

## contract.created

Publica: contract-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_845e948c829f40a4918bc6aad7f94898",
  "seq": 1,
  "topic": "contract.created",
  "key": "a003f84c-7ebb-43c2-870f-c99cd8627131",
  "producer": "contract-service",
  "occurredAt": "2026-10-26T04:20:40.976+01:00",
  "correlationId": "corr-3d44818123e7474c",
  "schemaVersion": 1,
  "payload": {
    "contractId": "a003f84c-7ebb-43c2-870f-c99cd8627131",
    "customerId": "c0000000-0000-4000-8000-000000000001",
    "type": "MASTER_AGREEMENT",
    "templateVersion": 3
  }
}
```

## contract.expired

Publica: contract-service · Consumen: audit-service, onboarding-orchestrator

## contract.invalidated

Publica: contract-service · Consumen: audit-service

## contract.sent

Publica: contract-service · Consumen: audit-service, notification-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_2bb041132ff44e97a90350584f683d57",
  "seq": 3,
  "topic": "contract.sent",
  "key": "2c7afda2-32ed-4b8b-a10b-1e0b55ab06ab",
  "producer": "contract-service",
  "occurredAt": "2026-10-26T04:21:00.244+01:00",
  "correlationId": "corr-a74cf4f62491430b",
  "schemaVersion": 1,
  "payload": {
    "contractId": "2c7afda2-32ed-4b8b-a10b-1e0b55ab06ab",
    "customerId": "c0000000-0000-4000-8000-000000000001",
    "type": "MASTER_AGREEMENT",
    "expiresAt": "2026-11-09T04:21:00.222+01:00",
    "notificationVars": {
      "firstName": "Ana",
      "contractType": "Contrato marco de cliente",
      "deadline": "2026-11-09"
    }
  }
}
```

## contract.signed

Publica: contract-service · Consumen: audit-service

## contract.superseded

Publica: contract-service · Consumen: audit-service

## contract.terminated

Publica: contract-service · Consumen: audit-service

## contract.withdrawn

Publica: contract-service · Consumen: audit-service

## contribution.received

Publica: contribution-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_283fc410d85f41149d58313a179f7745",
  "seq": 373,
  "topic": "contribution.received",
  "key": "ctb_ed7433597a5046",
  "producer": "contribution-service",
  "occurredAt": "2026-10-26T06:45:24.027+01:00",
  "correlationId": "corr-fe9b32ae3d294b0d",
  "schemaVersion": 1,
  "payload": {
    "contributionId": "ctb_ed7433597a5046",
    "planId": "pp_8487d8a8482445",
    "customerId": "ec5ae40d-342e-4e83-82f7-607629f8dbda",
    "amount": "2500.00",
    "source": "EMPLOYER",
    "period": "2026-10"
  }
}
```

## corporate-action.announced

Publica: corporate-actions · Consumen: audit-service

## corporate-action.applied

Publica: corporate-actions · Consumen: audit-service, position-keeping

## custody.execution.received

Publica: custody-adapter · Consumen: audit-service, execution-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_40a12fe609484ab68924f41e6ca11427",
  "seq": 407,
  "topic": "custody.execution.received",
  "key": "ord_4f9999d07b984101",
  "producer": "custody-adapter",
  "occurredAt": "2026-10-26T06:45:51.582+01:00",
  "correlationId": "corr-bc8f15d93d184a81",
  "schemaVersion": 1,
  "payload": {
    "batchId": "BATCH-20261020-0001-FFD9",
    "execution": {
      "executionId": "EX-2DE472C8DE3C",
      "clientOrderId": "ord_4f9999d07b984101",
      "accountId": "098fa739-a386-4f4a-8b56-88b977d0eda6",
      "isin": "SE0000000203",
      "side": "BUY",
      "units": "2832.6739",
      "nav": "21.1814",
      "fundCurrency": "SEK",
      "fxRate": "1.000000",
      "grossAmountSek": "60000.00",
      "feeSek": "0.00",
      "netAmountSek": "60000.00",
      "tradeDate": "2026-10-20",
      "settlementDate": "2026-10-22"
    }
  }
}
```

## custody.order.rejected

Publica: custody-adapter · Consumen: audit-service, execution-service

## death-claim.opened

Publica: death-claims · Consumen: audit-service

## death-claim.settled

Publica: death-claims · Consumen: audit-service

## death-claim.verified

Publica: death-claims · Consumen: audit-service

## delegation.created

Publica: delegation-service · Consumen: audit-service

## delegation.revoked

Publica: delegation-service · Consumen: audit-service

## delegation.verified

Publica: delegation-service · Consumen: audit-service

## deposit.received

Publica: payments-gateway · Consumen: audit-service, notification-service, transaction-monitoring

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_c60f7ca439804ab8a238d951f32125a4",
  "seq": 7,
  "topic": "deposit.received",
  "key": "dep_f3ab9f1c1bcd4d26",
  "producer": "payments-gateway",
  "occurredAt": "2026-10-26T04:29:00.181+01:00",
  "correlationId": "corr-edbd666ff5a6468e",
  "schemaVersion": 1,
  "payload": {
    "paymentId": "dep_f3ab9f1c1bcd4d26",
    "customerId": "c0000000-0000-4000-8000-000000000001",
    "amount": "250.00",
    "amountMismatch": false,
    "notificationVars": {
      "amount": "250.00"
    }
  }
}
```

## device.revoked

Publica: device-trust · Consumen: audit-service

## device.trusted

Publica: device-trust · Consumen: audit-service

## document.destroyed

Publica: document-vault · Consumen: audit-service

## document.stored

Publica: document-vault · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_89ba3c3f84764c2bb51c042baba4e184",
  "seq": 57,
  "topic": "document.stored",
  "key": "doc_aafb271c03604116",
  "producer": "document-vault",
  "occurredAt": "2026-10-26T04:30:44.753+01:00",
  "correlationId": "corr-1cbd40c99750425f",
  "schemaVersion": 1,
  "payload": {
    "documentId": "doc_aafb271c03604116",
    "kind": "FEE_SCHEDULE",
    "contractId": "9df12df2-823a-40d6-8af8-cfea50af7fb7",
    "expiresAt": null
  }
}
```

## dsar.completed

Publica: dsar-service · Consumen: audit-service

## employer-file.processed

Publica: employer-file-ingest · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_d0f132ab98bf45bfa66cf3f7a1f43990",
  "seq": 375,
  "topic": "employer-file.processed",
  "key": "ef_3c2ec4eb34e4",
  "producer": "employer-file-ingest",
  "occurredAt": "2026-10-26T06:45:24.161+01:00",
  "correlationId": "corr-fe9b32ae3d294b0d",
  "schemaVersion": 1,
  "payload": {
    "ingestId": "ef_3c2ec4eb34e4",
    "fileId": "cf_3c866fa04770",
    "employerOrgNumber": "554470-4306",
    "period": "2026-10",
    "accepted": 1,
    "rejected": 1
  }
}
```

## employment.changed

Publica: employment-service · Consumen: audit-service

## erasure.blocked

Publica: erasure-orchestrator · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_0df05813a8e7401f961c64217107a420",
  "seq": 946,
  "topic": "erasure.blocked",
  "key": "era_f199f9394938",
  "producer": "erasure-orchestrator",
  "occurredAt": "2026-10-26T08:05:15.854+01:00",
  "correlationId": "corr-8cb30331fd6b4ac4",
  "schemaVersion": 1,
  "payload": {
    "requestId": "era_f199f9394938",
    "blockers": 1
  }
}
```

## erasure.completed

Publica: erasure-orchestrator · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_6e043a8de304463589ab506e07cd67df",
  "seq": 937,
  "topic": "erasure.completed",
  "key": "era_4b6c47d6c71b",
  "producer": "erasure-orchestrator",
  "occurredAt": "2026-10-26T08:05:07.652+01:00",
  "correlationId": "corr-03ee0ec370ac4c1d",
  "schemaVersion": 1,
  "payload": {
    "requestId": "era_4b6c47d6c71b",
    "blockers": 0
  }
}
```

## fees.billed

Publica: fee-billing · Consumen: audit-service

## health-declaration.completed

Publica: health-declaration · Consumen: audit-service

## household.updated

Publica: household-service · Consumen: audit-service

## insurance-claim.paid

Publica: insurance-claims · Consumen: audit-service

## insurance-claim.rejected

Publica: insurance-claims · Consumen: audit-service

## insurance-claim.submitted

Publica: insurance-claims · Consumen: audit-service

## insurance-policy.issued

Publica: insurance-policy · Consumen: audit-service

## insurance-policy.status-changed

Publica: insurance-policy · Consumen: audit-service

## key.created

Publica: key-management · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_d95687d9e28d4b22a8d88170320b141e",
  "seq": 300,
  "topic": "key.created",
  "key": "key_f82c35022220405d",
  "producer": "key-management",
  "occurredAt": "2026-10-26T06:43:33.070+01:00",
  "correlationId": "corr-a3f0105ea5f1457a",
  "schemaVersion": 1,
  "payload": {
    "keyId": "key_f82c35022220405d",
    "subject": "customer:0965a9fe-fee8-4fad-8016-1032e7243d0a",
    "purpose": "PII"
  }
}
```

## key.rotated

Publica: key-management · Consumen: audit-service

## key.shredded

Publica: key-management · Consumen: audit-service, pii-vault

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_24a4d47e21434ef29c6db6beab198791",
  "seq": 336,
  "topic": "key.shredded",
  "key": "key_cd372a8079534e95",
  "producer": "key-management",
  "occurredAt": "2026-10-26T06:44:51.060+01:00",
  "correlationId": "corr-1d70d10ca9a2411b",
  "schemaVersion": 1,
  "payload": {
    "keyId": "key_cd372a8079534e95",
    "subject": "call:call_992db05026de",
    "reason": "ciclo de vida completo: resumen guardado"
  }
}
```

## kyc.approved

Publica: kyc-aml-service · Consumen: audit-service

## kyc.rejected

Publica: kyc-aml-service · Consumen: audit-service

## kyc.review-required

Publica: kyc-aml-service · Consumen: audit-service, case-management

## ledger.entry.posted

Publica: ledger-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_11a643a8429a4191b0ca8ae3e21522bc",
  "seq": 6,
  "topic": "ledger.entry.posted",
  "key": "je_2c2441b248914754",
  "producer": "ledger-service",
  "occurredAt": "2026-10-26T04:29:00.094+01:00",
  "correlationId": "corr-2408b650fefd49ff",
  "schemaVersion": 1,
  "payload": {
    "entryId": "je_2c2441b248914754",
    "reference": "DEP-dep_f3ab9f1c1bcd4d26",
    "valueDate": "2026-10-26",
    "accounts": [
      "bank:client-money",
      "customer:c0000000-0000-4000-8000-000000000001:cash"
    ],
    "reversalOf": null
  }
}
```

## meeting.booked

Publica: meeting-scheduler · Consumen: audit-service

## meeting.cancelled

Publica: meeting-scheduler · Consumen: audit-service

## message.destroyed

Publica: secure-messaging · Consumen: audit-service

## message.sent

Publica: secure-messaging · Consumen: audit-service

## monitoring.alert.closed

Publica: transaction-monitoring · Consumen: audit-service

## monitoring.alert.raised

Publica: transaction-monitoring · Consumen: audit-service, case-management

## notification.sent

Publica: notification-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_b06cde884d69434fbd99a2c1cc1dd564",
  "seq": 4,
  "topic": "notification.sent",
  "key": "ntf_8969fa936aa14b59",
  "producer": "notification-service",
  "occurredAt": "2026-10-26T04:21:00.837+01:00",
  "correlationId": "corr-43d4f37aed934611",
  "schemaVersion": 1,
  "payload": {
    "notificationId": "ntf_8969fa936aa14b59",
    "customerId": "c0000000-0000-4000-8000-000000000001",
    "channel": "EMAIL",
    "templateKey": "contract.ready-to-sign"
  }
}
```

## onboarding.completed

Publica: onboarding-orchestrator · Consumen: audit-service

## onboarding.failed

Publica: onboarding-orchestrator · Consumen: audit-service

## order.cancelled

Publica: order-management · Consumen: audit-service

## order.created

Publica: order-management · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_59b54525f6a442bd92fcbf77f12d1518",
  "seq": 29,
  "topic": "order.created",
  "key": "ord_8ec9f5a3659c42f2",
  "producer": "order-management",
  "occurredAt": "2026-10-26T04:29:37.858+01:00",
  "correlationId": "corr-5301202cb3064e84",
  "schemaVersion": 1,
  "payload": {
    "orderId": "ord_8ec9f5a3659c42f2",
    "portfolioId": "427cd713-f663-4571-bfe6-ccd8d6a3a6c4",
    "customerId": "d27177a8-fb88-49ee-8fdc-6f5b327ad74b",
    "isin": "SE0000000001",
    "side": "BUY",
    "amount": "300.00",
    "units": null,
    "tradeDate": "2026-10-26"
  }
}
```

## order.executed

Publica: order-management · Consumen: audit-service, notification-service, rebalancing-engine

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_1dd01abebdbb4d1c8cdcb8be4c2610c1",
  "seq": 417,
  "topic": "order.executed",
  "key": "ord_4f9999d07b984101",
  "producer": "order-management",
  "occurredAt": "2026-10-26T06:45:52.593+01:00",
  "correlationId": "corr-bc8f15d93d184a81",
  "schemaVersion": 1,
  "payload": {
    "orderId": "ord_4f9999d07b984101",
    "customerId": "7b4c9bc7-7539-48e2-8f7e-f432e88db5ae",
    "portfolioId": "098fa739-a386-4f4a-8b56-88b977d0eda6",
    "isin": "SE0000000203",
    "side": "BUY",
    "units": "2832.6739",
    "nav": "21.1814",
    "grossAmountSek": "60000.00",
    "netAmountSek": "60000.00",
    "tradeDate": "2026-10-20",
    "notificationVars": {
      "firstName": "Ebba",
      "orderId": "ord_4f9999d07b984101",
      "side": "compra",
      "fundName": "Nordvik Generation 2050",
      "units": "2832.6739",
      "nav": "21.1814",
      "settlementDate": "2026-10-22"
    }
  }
}
```

## order.rejected

Publica: order-management · Consumen: audit-service, rebalancing-engine

## order.sent

Publica: order-management · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_c4b2c0b8a8a04526ba6c7fede7138fb3",
  "seq": 406,
  "topic": "order.sent",
  "key": "ord_4f9999d07b984101",
  "producer": "order-management",
  "occurredAt": "2026-10-26T06:45:51.309+01:00",
  "correlationId": "corr-0c8540509bf64175",
  "schemaVersion": 1,
  "payload": {
    "orderId": "ord_4f9999d07b984101",
    "batchId": "BATCH-20261020-0001-FFD9"
  }
}
```

## passport.issued

Publica: data-passport · Consumen: audit-service, lineage-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_401af7746c2b44ce8fd1e4a35a3d3c90",
  "seq": 310,
  "topic": "passport.issued",
  "key": "pas_67e20a9882ab4411",
  "producer": "data-passport",
  "occurredAt": "2026-10-26T06:43:39.444+01:00",
  "correlationId": "corr-db576ed7ca39425d",
  "schemaVersion": 1,
  "payload": {
    "passportId": "pas_67e20a9882ab4411",
    "token": "tok_mail_f33e9d60da2e",
    "purpose": "PAYMENT_EXECUTION",
    "audience": "payments-gateway",
    "maxReads": 1
  }
}
```

## passport.read

Publica: data-passport · Consumen: audit-service, lineage-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_95d1d4dfe63a45c588472f92b3af1d2e",
  "seq": 313,
  "topic": "passport.read",
  "key": "pas_67e20a9882ab4411",
  "producer": "data-passport",
  "occurredAt": "2026-10-26T06:43:39.622+01:00",
  "correlationId": "corr-18d2adecca1241d8",
  "schemaVersion": 1,
  "payload": {
    "passportId": "pas_67e20a9882ab4411",
    "token": "tok_mail_f33e9d60da2e",
    "audience": "payments-gateway",
    "purpose": "PAYMENT_EXECUTION",
    "readNo": 1
  }
}
```

## passport.revoked

Publica: data-passport · Consumen: audit-service, lineage-service

## payment.bank-status.changed

Publica: nordpay-adapter · Consumen: audit-service, payment-instructions

## payment.credit.received

Publica: nordpay-adapter · Consumen: audit-service, payments-gateway

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_b9e0e5e3e6434e3080c0494604137fa4",
  "seq": 5,
  "topic": "payment.credit.received",
  "key": "7875768942",
  "producer": "nordpay-adapter",
  "occurredAt": "2026-10-26T04:28:59.906+01:00",
  "correlationId": "corr-edbd666ff5a6468e",
  "schemaVersion": 1,
  "payload": {
    "eventType": "CREDIT_NOTIFICATION",
    "creditId": "CR21C5DF3BC17F4B",
    "creditorIban": "SE4550000000058398257466",
    "debtorIban": "SE3130032506893039433184",
    "debtorName": "Ana",
    "amount": "250.00",
    "ccy": "SEK",
    "reference": "7875768942",
    "bookingDate": "2026-10-26"
  }
}
```

## payment.instruction.rejected

Publica: payment-instructions · Consumen: audit-service, payments-gateway, refunds-returns

## payment.instruction.settled

Publica: payment-instructions · Consumen: audit-service, payments-gateway

## payment.rejected

Publica: payments-gateway · Consumen: audit-service, notification-service

## payment.settled

Publica: payments-gateway · Consumen: audit-service, notification-service, transaction-monitoring

## payout-plan.created

Publica: payout-planner · Consumen: audit-service

## payout-plan.status-changed

Publica: payout-planner · Consumen: audit-service

## payout-run.completed

Publica: payout-execution · Consumen: audit-service

## payout.paid

Publica: payout-execution · Consumen: audit-service

## pension-plan.created

Publica: pension-plan-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_c752b0a1894948dfbec9b9db6ef3541b",
  "seq": 370,
  "topic": "pension-plan.created",
  "key": "pp_8487d8a8482445",
  "producer": "pension-plan-service",
  "occurredAt": "2026-10-26T06:45:23.168+01:00",
  "correlationId": "corr-c522645669244ff6",
  "schemaVersion": 1,
  "payload": {
    "planId": "pp_8487d8a8482445",
    "customerId": "ec5ae40d-342e-4e83-82f7-607629f8dbda",
    "type": "OCCUPATIONAL",
    "employerOrgNumber": "554470-4306"
  }
}
```

## pension-plan.frozen

Publica: pension-plan-service · Consumen: audit-service

## pension-plan.status-changed

Publica: pension-plan-service · Consumen: audit-service

## pep.identified

Publica: pep-service · Consumen: audit-service

## pii.detokenized

Publica: pii-vault · Consumen: audit-service, lineage-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_c1eb1d2a5e5c40b49fe5b72267b4a359",
  "seq": 312,
  "topic": "pii.detokenized",
  "key": "tok_mail_f33e9d60da2e",
  "producer": "pii-vault",
  "occurredAt": "2026-10-26T06:43:39.599+01:00",
  "correlationId": "corr-18d2adecca1241d8",
  "schemaVersion": 1,
  "payload": {
    "token": "tok_mail_f33e9d60da2e",
    "purpose": "PAYMENT_EXECUTION",
    "by": "payments-gateway",
    "customerId": "c0000000-0000-4000-8000-000000000001"
  }
}
```

## pii.tokenized

Publica: pii-vault · Consumen: audit-service, lineage-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_dbdc5e54f55346a8a51ecd47522ea6ec",
  "seq": 301,
  "topic": "pii.tokenized",
  "key": "tok_nid_243c4d82dcbd",
  "producer": "pii-vault",
  "occurredAt": "2026-10-26T06:43:33.135+01:00",
  "correlationId": "corr-a3f0105ea5f1457a",
  "schemaVersion": 1,
  "payload": {
    "token": "tok_nid_243c4d82dcbd",
    "type": "NATIONAL_ID",
    "customerId": "0965a9fe-fee8-4fad-8016-1032e7243d0a"
  }
}
```

## population.deceased-detected

Publica: population-adapter · Consumen: audit-service

## position.changed

Publica: position-keeping · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_387eb46789024e82aa248e2133433f2e",
  "seq": 418,
  "topic": "position.changed",
  "key": "098fa739-a386-4f4a-8b56-88b977d0eda6",
  "producer": "position-keeping",
  "occurredAt": "2026-10-20T10:00:00.000+02:00",
  "correlationId": "corr-bc8f15d93d184a81",
  "schemaVersion": 1,
  "payload": {
    "portfolioId": "098fa739-a386-4f4a-8b56-88b977d0eda6",
    "customerId": "7b4c9bc7-7539-48e2-8f7e-f432e88db5ae",
    "isin": "SE0000000203",
    "type": "BUY",
    "deltaUnits": "2832.6739",
    "units": "2832.6739",
    "date": "2026-10-20",
    "reference": "EX-2DE472C8DE3C"
  }
}
```

## price.nav.published

Publica: price-feed-adapter · Consumen: audit-service

## rebalance.completed

Publica: rebalancing-engine · Consumen: audit-service

## rebalance.failed

Publica: rebalancing-engine · Consumen: audit-service

## rebalance.started

Publica: rebalancing-engine · Consumen: audit-service

## recommendation.accepted

Publica: advisor-desk · Consumen: audit-service

## recommendation.declined

Publica: advisor-desk · Consumen: audit-service

## recommendation.proposed

Publica: advisor-desk · Consumen: audit-service

## reconciliation.completed

Publica: reconciliation-service · Consumen: audit-service

## refund.issued

Publica: refunds-returns · Consumen: audit-service

## regulatory-report.submitted

Publica: regulatory-reporting · Consumen: audit-service

## retention.expired

Publica: retention-scheduler · Consumen: audit-service

## risk.scored

Publica: risk-scoring · Consumen: audit-service

## salary-exchange.created

Publica: salary-exchange-service · Consumen: audit-service

## screening.completed

Publica: sanctions-adapter · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_b29092f35e4c45d59375e2eefa3e932c",
  "seq": 315,
  "topic": "screening.completed",
  "key": "scr_c61074851a3844f8",
  "producer": "sanctions-adapter",
  "occurredAt": "2026-10-26T06:44:21.923+01:00",
  "correlationId": "corr-703a58510ec44d41",
  "schemaVersion": 1,
  "payload": {
    "screeningId": "scr_c61074851a3844f8",
    "customerId": null,
    "status": "CLEAR",
    "maxScore": 0
  }
}
```

## screening.decided

Publica: sanctions-adapter · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_fe4d5ecbe5d94c60a9984656743a0845",
  "seq": 320,
  "topic": "screening.decided",
  "key": "scr_6c0ad87c5c1c4e56",
  "producer": "sanctions-adapter",
  "occurredAt": "2026-10-26T06:44:26.259+01:00",
  "correlationId": "corr-5351ecdc66ce46c4",
  "schemaVersion": 1,
  "payload": {
    "screeningId": "scr_6c0ad87c5c1c4e56",
    "customerId": null,
    "status": "CONFIRMED_MATCH",
    "decidedBy": "compliance.karin"
  }
}
```

## secret.read

Publica: secrets-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_011f7bb0fd354f89ae3d708d27043e8d",
  "seq": 314,
  "topic": "secret.read",
  "key": "sanctionscreen-api-key",
  "producer": "secrets-service",
  "occurredAt": "2026-10-26T06:44:03.305+01:00",
  "correlationId": "corr-c024c5d0b2d04903",
  "schemaVersion": 1,
  "payload": {
    "name": "sanctionscreen-api-key",
    "version": 1,
    "by": "nordvik-internal"
  }
}
```

## secret.rotated

Publica: secrets-service · Consumen: audit-service

## signing.completed

Publica: signing-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_51eb32e1c3f44d6299165995517fd4fc",
  "seq": 43,
  "topic": "signing.completed",
  "key": "9b50b049-b0d8-4108-a45a-872db3d7999e",
  "producer": "signing-service",
  "occurredAt": "2026-10-26T04:30:08.721+01:00",
  "correlationId": "corr-41f20af97d3249d4",
  "schemaVersion": 1,
  "payload": {
    "sessionId": "sig_3ab5aba007354b0e",
    "contractId": "9b50b049-b0d8-4108-a45a-872db3d7999e",
    "party": "CUSTOMER",
    "contractStatus": "ACTIVE"
  }
}
```

## signing.failed

Publica: signing-service · Consumen: audit-service

## suitability.assessed

Publica: suitability-service · Consumen: audit-service

## suitability.checked

Publica: suitability-service · Consumen: audit-service

## task.completed

Publica: task-queue · Consumen: audit-service

## task.created

Publica: task-queue · Consumen: audit-service

## tax-report.filed

Publica: tax-reporting · Consumen: audit-service

## tax-residency.changed

Publica: tax-residency-service · Consumen: audit-service

## token.exchanged

Publica: token-exchange · Consumen: audit-service

## trade.executed

Publica: execution-service · Consumen: audit-service, order-management, position-keeping

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_ed81d34b508f492782e1907ec100fca8",
  "seq": 408,
  "topic": "trade.executed",
  "key": "ord_4f9999d07b984101",
  "producer": "execution-service",
  "occurredAt": "2026-10-20T10:00:00.000+02:00",
  "correlationId": "corr-bc8f15d93d184a81",
  "schemaVersion": 1,
  "payload": {
    "executionId": "EX-2DE472C8DE3C",
    "orderId": "ord_4f9999d07b984101",
    "portfolioId": "098fa739-a386-4f4a-8b56-88b977d0eda6",
    "customerId": "7b4c9bc7-7539-48e2-8f7e-f432e88db5ae",
    "isin": "SE0000000203",
    "side": "BUY",
    "units": "2832.6739",
    "nav": "21.1814",
    "fundCurrency": "SEK",
    "fxRate": "1.000000",
    "grossAmountSek": "60000.00",
    "feeSek": "0.00",
    "netAmountSek": "60000.00",
    "tradeDate": "2026-10-20",
    "settlementDate": "2026-10-22"
  }
}
```

## trade.rejected

Publica: execution-service · Consumen: audit-service, order-management

## transfer-valuation.ready

Publica: transfer-valuation · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_feb1c1ba446c4c88b7bcc9ceaa69661f",
  "seq": 399,
  "topic": "transfer-valuation.ready",
  "key": "tv_ffcef7eca49b",
  "producer": "transfer-valuation",
  "occurredAt": "2026-10-20T10:00:00.000+02:00",
  "correlationId": "corr-59cc193264574619",
  "schemaVersion": 1,
  "payload": {
    "valuationId": "tv_ffcef7eca49b",
    "value": "60000.00"
  }
}
```

## transfer.completed

Publica: transfer-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_db797e3c92084fc98f1e66e64434f7a6",
  "seq": 405,
  "topic": "transfer.completed",
  "key": "tr_93b7b923d9b5",
  "producer": "transfer-service",
  "occurredAt": "2026-10-20T10:00:00.000+02:00",
  "correlationId": "corr-fb59a0c15e304463",
  "schemaVersion": 1,
  "payload": {
    "transferId": "tr_93b7b923d9b5",
    "from": "AWAITING_APPROVAL",
    "to": "COMPLETED",
    "extra": {
      "amount": "60000.00"
    }
  }
}
```

## transfer.requested

Publica: transfer-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_b799316955bb46b29bd23fb0b51a4e1e",
  "seq": 381,
  "topic": "transfer.requested",
  "key": "tr_84b692b5bd7a",
  "producer": "transfer-service",
  "occurredAt": "2026-10-20T10:00:00.000+02:00",
  "correlationId": "corr-adc3213d77ca4016",
  "schemaVersion": 1,
  "payload": {
    "transferId": "tr_84b692b5bd7a",
    "customerId": "97f28309-c16b-494b-ae5e-8de39e873c8c",
    "planId": "pp_9531d56d2d204d",
    "providerCode": "FJORDLIV"
  }
}
```

## transfer.status-changed

Publica: transfer-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_e16438b8dbf745b28ece741a518c73e1",
  "seq": 398,
  "topic": "transfer.status-changed",
  "key": "tr_93b7b923d9b5",
  "producer": "transfer-service",
  "occurredAt": "2026-10-20T10:00:00.000+02:00",
  "correlationId": "corr-59cc193264574619",
  "schemaVersion": 1,
  "payload": {
    "transferId": "tr_93b7b923d9b5",
    "from": "VALUATION_PENDING",
    "to": "VALUED",
    "extra": null
  }
}
```

## voice.call.ended

Publica: voice-service · Consumen: audit-service, transcription-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_26080215073249c789ab32e2bd99ca41",
  "seq": 325,
  "topic": "voice.call.ended",
  "key": "call_992db05026de",
  "producer": "voice-service",
  "occurredAt": "2026-10-26T06:44:49.089+01:00",
  "correlationId": "corr-587db273b0de41cd",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "customerId": "48eedc09-915b-431d-8dea-1543c2d6357e",
    "turns": 6
  }
}
```

## voice.call.shredded

Publica: voice-service · Consumen: audit-service, transcription-service, insight-extraction

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_99920f289338445c8ab7074edb77785c",
  "seq": 337,
  "topic": "voice.call.shredded",
  "key": "call_992db05026de",
  "producer": "voice-service",
  "occurredAt": "2026-10-26T06:44:51.131+01:00",
  "correlationId": "corr-db00e4066a474111",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "reason": "ciclo de vida completo: resumen guardado",
    "chunksDestroyed": 6
  }
}
```

## voice.call.started

Publica: voice-service · Consumen: audit-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_832e0820ae314cce994a9f4fdd1e90c7",
  "seq": 324,
  "topic": "voice.call.started",
  "key": "call_992db05026de",
  "producer": "voice-service",
  "occurredAt": "2026-10-26T06:44:48.798+01:00",
  "correlationId": "corr-c5d301020060431b",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "customerId": "48eedc09-915b-431d-8dea-1543c2d6357e",
    "advisor": "asesor.erik"
  }
}
```

## voice.call.summarised

Publica: call-summary · Consumen: audit-service, voice-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_fc4874ec67f0495195e4616f6fbe18d6",
  "seq": 333,
  "topic": "voice.call.summarised",
  "key": "call_992db05026de",
  "producer": "call-summary",
  "occurredAt": "2026-10-26T06:44:50.718+01:00",
  "correlationId": "corr-f0924270c3d542cd",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "customerId": "48eedc09-915b-431d-8dea-1543c2d6357e"
  }
}
```

## voice.insights.extracted

Publica: insight-extraction · Consumen: audit-service, voice-service

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_b4cb2c83cc24482c8bf75974058ee229",
  "seq": 327,
  "topic": "voice.insights.extracted",
  "key": "call_992db05026de",
  "producer": "insight-extraction",
  "occurredAt": "2026-10-26T06:44:49.898+01:00",
  "correlationId": "corr-587db273b0de41cd",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "fields": 0,
    "needsReview": 0
  }
}
```

## voice.recording.revoked

Publica: voice-service · Consumen: audit-service

## voice.review.completed

Publica: voice-service · Consumen: audit-service, advisor-desk, call-summary

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_af9b7e32486d4ed28dd2c9e65885253b",
  "seq": 328,
  "topic": "voice.review.completed",
  "key": "call_992db05026de",
  "producer": "voice-service",
  "occurredAt": "2026-10-26T06:44:50.305+01:00",
  "correlationId": "corr-f0924270c3d542cd",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "confirmedFields": 0,
    "automatic": true
  }
}
```

## voice.transcript.redacted

Publica: transcription-service · Consumen: audit-service, voice-service, insight-extraction

Ejemplo real (el primero que hay en el bus):

```json
{
  "eventId": "evt_1e00f88d7d814da58e07e8bda7f8a732",
  "seq": 326,
  "topic": "voice.transcript.redacted",
  "key": "call_992db05026de",
  "producer": "transcription-service",
  "occurredAt": "2026-10-26T06:44:49.562+01:00",
  "correlationId": "corr-587db273b0de41cd",
  "schemaVersion": 1,
  "payload": {
    "callId": "call_992db05026de",
    "turns": 6,
    "redactions": 0,
    "healthMentioned": false
  }
}
```

## withdrawal.requested

Publica: payments-gateway · Consumen: audit-service
