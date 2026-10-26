# L00 · ¿Qué es una API?

**En esta lección** entiendes qué pasa cuando un programa le pide algo a otro, y ejecutas tu primera prueba sin escribir casi nada.

## La idea

Una **API** es una puerta por la que un programa le hace peticiones a otro. En Nordvik, cada microservicio es una de esas puertas. Tú (o Karate) eres el **cliente**; el microservicio es el **servidor**.

Cada conversación tiene dos mitades:

| La petición (lo que tú envías) | La respuesta (lo que te devuelven) |
| --- | --- |
| **Método**: qué quieres hacer (`GET` leer, `POST` crear, `PUT`/`PATCH` cambiar, `DELETE` borrar) | **Código de estado**: un número que resume cómo ha ido |
| **URL**: a quién y qué recurso (`http://localhost:6801/api/v1/funds`) | **Cabeceras**: datos sobre la respuesta |
| **Cabeceras** (opcional): datos extra | **Cuerpo**: los datos, casi siempre en **JSON** |
| **Cuerpo** (opcional): los datos que envías | |

## La norma

1. Los códigos `2xx` significan éxito: `200` OK, `201` creado, `204` hecho y sin contenido.
2. Los códigos `4xx` significan que **el cliente** pidió algo mal: `400` petición inválida, `404` no existe, `409` conflicto, `422` regla de negocio incumplida.
3. Los códigos `5xx` significan que **el servidor** falló.
4. Una URL se lee así: `http://localhost:6801` (el servicio) + `/api/v1/funds` (el recurso) + `?category=EQUITY` (los parámetros).

## El fundamento

Probar una API es comprobar que, ante cada petición, el servidor contesta **lo que dice su contrato**: el código correcto y el JSON correcto. Un buen tester predice la respuesta antes de enviar la petición. Si la predicción falla, o el servidor tiene un bug, o tu modelo mental está mal: en los dos casos aprendes algo.

## Así se escribe en Karate

```gherkin
Scenario: Leer el catálogo
  Given url catalogUrl            # el servicio (la URL está en karate-config.js)
  And path '/api/v1/funds'        # el recurso
  When method get                 # la acción
  Then status 200                 # tu predicción del código
```

## Antes de los ejercicios

1. Arranca Nordvik: `nordvik up` (desde la carpeta `nordvik`).
2. Abre `http://localhost:6801/swagger` en el navegador y prueba `GET /api/v1/funds` con **Try it out**. Mira el código y el JSON.
3. Ejecuta el ejemplo: `academia ejemplo L00`.

## Errores típicos

- Confundir `404` (no existe) con `400` (lo pediste mal).
- Olvidar que Nordvik tiene que estar arrancado: si no, verás "No hay ningún servicio escuchando".

## Tus ejercicios

Cinco predicciones: rellena cada `___` y ejecuta `academia corregir L00`.
