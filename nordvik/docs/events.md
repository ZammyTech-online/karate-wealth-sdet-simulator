# Catálogo de eventos

Cada servicio guarda sus eventos en su **outbox**; `outbox-relay` los publica en `event-bus` (puerto 7400) y los consumidores los leen con `GET /api/v1/events?topics=...&after=<cursor>`. Puedes suscribir un mock de Karate con `POST /api/v1/subscriptions`.

Sobre (envelope) de todos los eventos: `eventId`, `seq`, `topic`, `key`, `producer`, `occurredAt`, `correlationId`, `schemaVersion`, `payload`.

| Tema | Publica | Consumen |
| --- | --- | --- |
| `bank-account.rejected` | bank-account-verification | audit-service |
| `bank-account.verified` | bank-account-verification | audit-service |
| `contract.activated` | contract-service | audit-service, onboarding-orchestrator |
| `contract.created` | contract-service | audit-service |
| `contract.expired` | contract-service | audit-service, onboarding-orchestrator |
| `contract.invalidated` | contract-service | audit-service |
| `contract.sent` | contract-service | audit-service, notification-service |
| `contract.signed` | contract-service | audit-service |
| `contract.superseded` | contract-service | audit-service |
| `contract.terminated` | contract-service | audit-service |
| `contract.withdrawn` | contract-service | audit-service |
| `corporate-action.announced` | corporate-actions | audit-service |
| `corporate-action.applied` | corporate-actions | audit-service, position-keeping |
| `custody.execution.received` | custody-adapter | audit-service, execution-service |
| `custody.order.rejected` | custody-adapter | audit-service, execution-service |
| `deposit.received` | payments-gateway | audit-service, notification-service |
| `document.destroyed` | document-vault | audit-service |
| `document.stored` | document-vault | audit-service |
| `fees.billed` | fee-billing | audit-service |
| `ledger.entry.posted` | ledger-service | audit-service |
| `notification.sent` | notification-service | audit-service |
| `onboarding.completed` | onboarding-orchestrator | audit-service |
| `onboarding.failed` | onboarding-orchestrator | audit-service |
| `order.cancelled` | order-management | audit-service |
| `order.created` | order-management | audit-service |
| `order.executed` | order-management | audit-service, notification-service, rebalancing-engine |
| `order.rejected` | order-management | audit-service, rebalancing-engine |
| `order.sent` | order-management | audit-service |
| `payment.bank-status.changed` | nordpay-adapter | audit-service, payment-instructions |
| `payment.credit.received` | nordpay-adapter | audit-service, payments-gateway |
| `payment.instruction.rejected` | payment-instructions | audit-service, payments-gateway, refunds-returns |
| `payment.instruction.settled` | payment-instructions | audit-service, payments-gateway |
| `payment.rejected` | payments-gateway | audit-service, notification-service |
| `payment.settled` | payments-gateway | audit-service, notification-service |
| `position.changed` | position-keeping | audit-service |
| `price.nav.published` | price-feed-adapter | audit-service |
| `rebalance.completed` | rebalancing-engine | audit-service |
| `rebalance.failed` | rebalancing-engine | audit-service |
| `rebalance.started` | rebalancing-engine | audit-service |
| `reconciliation.completed` | reconciliation-service | audit-service |
| `refund.issued` | refunds-returns | audit-service |
| `signing.completed` | signing-service | audit-service |
| `signing.failed` | signing-service | audit-service |
| `trade.executed` | execution-service | audit-service, order-management, position-keeping |
| `trade.rejected` | execution-service | audit-service, order-management |
| `withdrawal.requested` | payments-gateway | audit-service |

## bank-account.rejected

Publica: bank-account-verification · Consumen: audit-service

## bank-account.verified

Publica: bank-account-verification · Consumen: audit-service

## contract.activated

Publica: contract-service · Consumen: audit-service, onboarding-orchestrator

## contract.created

Publica: contract-service · Consumen: audit-service

## contract.expired

Publica: contract-service · Consumen: audit-service, onboarding-orchestrator

## contract.invalidated

Publica: contract-service · Consumen: audit-service

## contract.sent

Publica: contract-service · Consumen: audit-service, notification-service

## contract.signed

Publica: contract-service · Consumen: audit-service

## contract.superseded

Publica: contract-service · Consumen: audit-service

## contract.terminated

Publica: contract-service · Consumen: audit-service

## contract.withdrawn

Publica: contract-service · Consumen: audit-service

## corporate-action.announced

Publica: corporate-actions · Consumen: audit-service

## corporate-action.applied

Publica: corporate-actions · Consumen: audit-service, position-keeping

## custody.execution.received

Publica: custody-adapter · Consumen: audit-service, execution-service

## custody.order.rejected

Publica: custody-adapter · Consumen: audit-service, execution-service

## deposit.received

Publica: payments-gateway · Consumen: audit-service, notification-service

## document.destroyed

Publica: document-vault · Consumen: audit-service

## document.stored

Publica: document-vault · Consumen: audit-service

## fees.billed

Publica: fee-billing · Consumen: audit-service

## ledger.entry.posted

Publica: ledger-service · Consumen: audit-service

## notification.sent

Publica: notification-service · Consumen: audit-service

## onboarding.completed

Publica: onboarding-orchestrator · Consumen: audit-service

## onboarding.failed

Publica: onboarding-orchestrator · Consumen: audit-service

## order.cancelled

Publica: order-management · Consumen: audit-service

## order.created

Publica: order-management · Consumen: audit-service

## order.executed

Publica: order-management · Consumen: audit-service, notification-service, rebalancing-engine

## order.rejected

Publica: order-management · Consumen: audit-service, rebalancing-engine

## order.sent

Publica: order-management · Consumen: audit-service

## payment.bank-status.changed

Publica: nordpay-adapter · Consumen: audit-service, payment-instructions

## payment.credit.received

Publica: nordpay-adapter · Consumen: audit-service, payments-gateway

## payment.instruction.rejected

Publica: payment-instructions · Consumen: audit-service, payments-gateway, refunds-returns

## payment.instruction.settled

Publica: payment-instructions · Consumen: audit-service, payments-gateway

## payment.rejected

Publica: payments-gateway · Consumen: audit-service, notification-service

## payment.settled

Publica: payments-gateway · Consumen: audit-service, notification-service

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

## reconciliation.completed

Publica: reconciliation-service · Consumen: audit-service

## refund.issued

Publica: refunds-returns · Consumen: audit-service

## signing.completed

Publica: signing-service · Consumen: audit-service

## signing.failed

Publica: signing-service · Consumen: audit-service

## trade.executed

Publica: execution-service · Consumen: audit-service, order-management, position-keeping

## trade.rejected

Publica: execution-service · Consumen: audit-service, order-management

## withdrawal.requested

Publica: payments-gateway · Consumen: audit-service
