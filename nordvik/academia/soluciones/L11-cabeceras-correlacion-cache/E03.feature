# ──────────────────────────────────────────────────────────────────────
# L11 · E03 · Dámelo solo si cambió
#
# Envía el ETag en la cabecera de caché condicional. Como el precio no ha cambiado, ¿qué código llega?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L11 @E03 @solucion
Feature: L11 E03 · Dámelo solo si cambió

  Scenario: 304 Not Modified
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['ETag'][0]
    Given path '/api/v1/prices', 'SE0000000002', 'latest'
    And header If-None-Match = etag
    When method get
    Then status 304
