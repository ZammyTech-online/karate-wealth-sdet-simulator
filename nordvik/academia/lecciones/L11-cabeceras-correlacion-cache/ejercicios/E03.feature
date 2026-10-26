# ──────────────────────────────────────────────────────────────────────
# L11 · E03 · Dámelo solo si cambió
#
# Envía el ETag en la cabecera de caché condicional. Como el precio no ha cambiado, ¿qué código llega?
#
# Rellena cada ___ y comprueba con:  academia corregir L11
# Pista: La cabecera empieza por If-. El código significa 'no modificado'.
# ──────────────────────────────────────────────────────────────────────
@L11 @E03
Feature: L11 E03 · Dámelo solo si cambió

  Scenario: 304 Not Modified
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['ETag'][0]
    Given path '/api/v1/prices', 'SE0000000002', 'latest'
    And header ___ = etag
    When method get
    Then status ___
