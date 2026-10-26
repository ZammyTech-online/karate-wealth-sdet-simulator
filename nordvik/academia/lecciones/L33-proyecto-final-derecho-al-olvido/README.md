# L33 · Proyecto final: el derecho al olvido de punta a punta

## La idea

Juntas todo lo aprendido en una saga real que cruza cinco servicios. Un cliente pide que le borren (RGPD art. 17):

1. `erasure-orchestrator` (6507) comprueba lo que lo impide: planes de pensión vivos, carteras con dinero, seguros.
   Si hay algo → `BLOCKED` con `blockers` y **no toca nada**.
2. Si no, tritura las claves del cliente en `key-management`: `pii-vault` recibe el evento `key.shredded` y deja sus
   tokens **ERASED** (consistencia eventual: espera con `retry until`).
3. Retira sus consentimientos (`consent-ledger`) y cierra su ficha (`customer-registry`).
4. Devuelve `COMPLETED`, un certificado SHA-256 y lo que la ley obliga a guardar (`retained`).

Lo pide Cumplimiento (compliance.karin) o el propio cliente.

## La norma

1. Una saga se prueba por **sus efectos en cada servicio**, no solo por la respuesta del orquestador.
2. Prueba el camino bloqueado: el orquestador no debe haber cambiado nada.
3. Repetir la operación es parte del contrato: el segundo borrado responde 409.

## Tu entrega

Los seis ejercicios de esta lección forman la suite de regresión del derecho al olvido. Cuando pasen todos, ejecútalos
diez veces seguidas (`academia corregir L33` varias veces) y manda los resultados a `test-insights` como en L32:
si alguno alterna, tienes un test inestable que arreglar.
