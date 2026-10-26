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
| `bank-account.rejected` | bank-account-verification | audit-service |
| `bank-account.verified` | bank-account-verification | audit-service |
| `beneficiaries.updated` | beneficiary-service | audit-service |
| `case.opened` | case-management | audit-service |
| `case.resolved` | case-management | audit-service |
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
| `deposit.received` | payments-gateway | audit-service, notification-service, transaction-monitoring |
| `document.destroyed` | document-vault | audit-service |
| `document.stored` | document-vault | audit-service |
| `employer-file.processed` | employer-file-ingest | audit-service |
| `employment.changed` | employment-service | audit-service |
| `fees.billed` | fee-billing | audit-service |
| `household.updated` | household-service | audit-service |
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

## bank-account.rejected

Publica: bank-account-verification · Consumen: audit-service

## bank-account.verified

Publica: bank-account-verification · Consumen: audit-service

## beneficiaries.updated

Publica: beneficiary-service · Consumen: audit-service

## case.opened

Publica: case-management · Consumen: audit-service

## case.resolved

Publica: case-management · Consumen: audit-service

## consent.granted

Publica: consent-ledger · Consumen: audit-service

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

## corporate-action.announced

Publica: corporate-actions · Consumen: audit-service

## corporate-action.applied

Publica: corporate-actions · Consumen: audit-service, position-keeping

## custody.execution.received

Publica: custody-adapter · Consumen: audit-service, execution-service

## custody.order.rejected

Publica: custody-adapter · Consumen: audit-service, execution-service

## death-claim.opened

Publica: death-claims · Consumen: audit-service

## death-claim.settled

Publica: death-claims · Consumen: audit-service

## death-claim.verified

Publica: death-claims · Consumen: audit-service

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

## employer-file.processed

Publica: employer-file-ingest · Consumen: audit-service

## employment.changed

Publica: employment-service · Consumen: audit-service

## fees.billed

Publica: fee-billing · Consumen: audit-service

## household.updated

Publica: household-service · Consumen: audit-service

## key.created

Publica: key-management · Consumen: audit-service

## key.rotated

Publica: key-management · Consumen: audit-service

## key.shredded

Publica: key-management · Consumen: audit-service, pii-vault

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

## order.rejected

Publica: order-management · Consumen: audit-service, rebalancing-engine

## order.sent

Publica: order-management · Consumen: audit-service

## passport.issued

Publica: data-passport · Consumen: audit-service, lineage-service

## passport.read

Publica: data-passport · Consumen: audit-service, lineage-service

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

## pension-plan.frozen

Publica: pension-plan-service · Consumen: audit-service

## pension-plan.status-changed

Publica: pension-plan-service · Consumen: audit-service

## pep.identified

Publica: pep-service · Consumen: audit-service

## pii.detokenized

Publica: pii-vault · Consumen: audit-service, lineage-service

## pii.tokenized

Publica: pii-vault · Consumen: audit-service, lineage-service

## population.deceased-detected

Publica: population-adapter · Consumen: audit-service

## position.changed

Publica: position-keeping · Consumen: audit-service

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

## retention.expired

Publica: retention-scheduler · Consumen: audit-service

## risk.scored

Publica: risk-scoring · Consumen: audit-service

## salary-exchange.created

Publica: salary-exchange-service · Consumen: audit-service

## screening.completed

Publica: sanctions-adapter · Consumen: audit-service

## screening.decided

Publica: sanctions-adapter · Consumen: audit-service

## secret.read

Publica: secrets-service · Consumen: audit-service

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

## tax-residency.changed

Publica: tax-residency-service · Consumen: audit-service

## token.exchanged

Publica: token-exchange · Consumen: audit-service

## trade.executed

Publica: execution-service · Consumen: audit-service, order-management, position-keeping

## trade.rejected

Publica: execution-service · Consumen: audit-service, order-management

## transfer-valuation.ready

Publica: transfer-valuation · Consumen: audit-service

## transfer.completed

Publica: transfer-service · Consumen: audit-service

## transfer.requested

Publica: transfer-service · Consumen: audit-service

## transfer.status-changed

Publica: transfer-service · Consumen: audit-service

## voice.call.ended

Publica: voice-service · Consumen: audit-service, transcription-service

## voice.call.shredded

Publica: voice-service · Consumen: audit-service, transcription-service, insight-extraction

## voice.call.started

Publica: voice-service · Consumen: audit-service

## voice.call.summarised

Publica: call-summary · Consumen: audit-service, voice-service

## voice.insights.extracted

Publica: insight-extraction · Consumen: audit-service, voice-service

## voice.recording.revoked

Publica: voice-service · Consumen: audit-service

## voice.review.completed

Publica: voice-service · Consumen: audit-service, advisor-desk, call-summary

## voice.transcript.redacted

Publica: transcription-service · Consumen: audit-service, voice-service, insight-extraction

## withdrawal.requested

Publica: payments-gateway · Consumen: audit-service
