# ──────────────────────────────────────────────────────────────────────
# L11 · E02 · Leer una cabecera de la respuesta
#
# Guarda la cabecera de versión del recurso (la de la caché) en la variable etag.
#
# Rellena cada ___ y comprueba con:  academia corregir L11
# Pista: La cabecera de la caché tiene 4 letras y se escribe con dos mayúsculas.
# ──────────────────────────────────────────────────────────────────────
@L11 @E02
Feature: L11 E02 · Leer una cabecera de la respuesta

  Scenario: El ETag del último precio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['___'][0]
    * match etag contains 'SE0000000001'
