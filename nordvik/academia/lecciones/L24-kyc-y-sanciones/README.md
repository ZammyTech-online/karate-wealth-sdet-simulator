# L24 · Cumplimiento: listas de sanciones y coincidencia difusa

## La idea

Antes de aceptar dinero, Nordvik comprueba que el cliente no esté en listas de sanciones. `sanctions-adapter` (6401)
llama al proveedor externo **SanctionScreen** (simulador E3, 7703) y devuelve:

| status | Qué significa |
|---|---|
| `CLEAR` | Nadie en las listas se parece lo bastante |
| `POTENTIAL_MATCH` | Alguien se parece: una persona de Cumplimiento tiene que decidir |
| `CONFIRMED_MATCH` / `CLEARED` | Ya decidido (`TRUE_MATCH` / `FALSE_POSITIVE`) |
| `INCOMPLETE` | El proveedor no miró todas las listas: **nunca** cuenta como limpio |

La coincidencia es **difusa**: "Viktor Draganow" se parece a "Viktor Draganov" (puntuación 0-100). El umbral es 85
y se cambia en `config-service` (`sanctions-adapter/matchThreshold`).

```gherkin
Given url sanctionsUrl
And path '/api/v1/screenings'
And headers karin.auth
And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
When method post
Then status 201
And match response.status == 'POTENTIAL_MATCH'
And match response.matches[0] contains { entryId: 'EU-0001', score: '#? _ >= 85' }
```

## La norma

1. Prueba los **casi**: errores de tecleo, nombre y apellido al revés, acentos.
2. Una decisión es de Cumplimiento (Karin), nunca del asesor. Y se toma **una vez** (luego 409).
3. Un resultado parcial jamás es CLEAR: búscalo a propósito con `/__admin/partial?on=true` en el simulador.

## El fundamento

En cumplimiento normativo, un falso negativo (dejar pasar a alguien sancionado) es mucho más caro que un falso positivo.
Por eso los tests de esta lección buscan los grises.
