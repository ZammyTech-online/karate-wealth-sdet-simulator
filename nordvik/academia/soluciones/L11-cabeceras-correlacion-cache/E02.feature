# ──────────────────────────────────────────────────────────────────────
# L11 · E02 · Leer una cabecera de la respuesta
#
# Guarda la cabecera de versión del recurso (la de la caché) en la variable etag.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L11 @E02 @solucion
Feature: L11 E02 · Leer una cabecera de la respuesta

  Scenario: El ETag del último precio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['ETag'][0]
    * match etag contains 'SE0000000001'
