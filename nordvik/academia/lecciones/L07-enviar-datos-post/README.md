# L07 · Enviar datos: POST, request y cuerpos JSON

**En esta lección** creas recursos con `POST`, los usas, y los borras.

## La idea

Hasta ahora solo leías. Para crear algo hay que **enviar un cuerpo** JSON con `request` y usar `method post`. Si sale bien, el servidor devuelve `201 Created` y, en la cabecera `Location`, dónde vive lo que creaste.

Usamos `sequence-service`, que crea contadores con nombre: `CLI-0001`, `CLI-0002`...

| Acción | Método y ruta | Cuerpo | Código si va bien |
| --- | --- | --- | --- |
| Crear | `POST /api/v1/sequences` | `{ name, prefix, padding }` | `201` |
| Siguiente valor | `POST /api/v1/sequences/{name}/next?count=N` | — | `200` |
| Ver | `GET /api/v1/sequences/{name}` | — | `200` |
| Borrar | `DELETE /api/v1/sequences/{name}` | — | `204` |

## La norma

1. Lo que creas en un test debe tener un **nombre único** para que tu test no choque con otro: usa un UUID.
2. `#(variable)` dentro de un JSON inserta el valor de una variable (**expresión embebida**).
3. Comprueba siempre el código de creación (`201`) y lo que devuelve.
4. Si puedes, borra lo que creaste al terminar.

## El fundamento

Un test que crea `'clientes'` funciona la primera vez y falla la segunda con `409 Conflict`. Los datos únicos por ejecución son lo que separa un test frágil de uno que puedes lanzar mil veces y en paralelo.

## Así se escribe

```gherkin
* def nombre = 'demo-' + java.util.UUID.randomUUID().toString().substring(0, 8)
Given url sequenceUrl
And path '/api/v1/sequences'
And request { name: '#(nombre)', prefix: 'CLI', padding: 4 }
When method post
Then status 201
And match response.current == 0
```

## Errores típicos

- Usar siempre el mismo nombre: el segundo intento da `409`.
- Olvidar `request`: el servidor recibe un cuerpo vacío y responde `400`.

## Tus ejercicios

`academia corregir L07`.
