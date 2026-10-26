# L27 · Sagas largas y reloj virtual: el traspaso de una pensión

## La idea

Traspasar una pensión desde otra gestora tarda **días**: la gestora (simulador E10, 7710) contesta la valoración
en 1-3 días. Un test no puede esperar días… pero Nordvik tiene **reloj virtual** (`clock-service`, 6003):

```gherkin
Given url clockUrl
And path '/api/v1/clock/freeze'
And request { at: '2026-10-22T10:30:00' }
When method post
```

La saga de `transfer-service` (7108) es una **máquina de estados**:

```
VALUATION_PENDING → VALUED → (cliente: /confirm) AWAITING_APPROVAL → (Operaciones: /approve)
   → COMPLETED            si lo recibido está a ±2 % de la valoración
   → MANUAL_REVIEW        si no  → /accept-amount (COMPLETED) o /reject (REJECTED)
```

Cualquier paso fuera de orden es **409 `INVALID_STATE_TRANSITION`**, y cada paso queda en `history`.

El ayudante `_traspaso-listo.feature` congela el reloj en el martes 2026-10-20 a las 10:00, crea un cliente con un plan
de pensión y le inventa una póliza en la gestora con el valor, los días de demora y la desviación que digas.

## La norma

1. **Devuelve el reloj** a la hora real al acabar: aquí lo hace `afterScenario` en el `Background`.
   Y ojo: los tokens caducan también en tiempo virtual. Si adelantas días, vuelve a hacer login.
2. Prueba cada transición prohibida, no solo el camino feliz.
3. Valida el historial completo con `match response.history[*].to == [...]`.

## El fundamento

En una saga el bug típico no está en un paso: está en el **orden** de los pasos. El reloj virtual convierte días
en milisegundos y te deja probar ese orden de forma determinista.
