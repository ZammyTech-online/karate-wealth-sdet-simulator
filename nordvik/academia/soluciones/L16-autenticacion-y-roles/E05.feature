# ──────────────────────────────────────────────────────────────────────
# L16 · E05 · 403: sé quién eres y no puedes
#
# Erik es asesor y no puede listar los usuarios del sistema. Completa estado y código.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L16 @E05 @solucion
Feature: L16 E05 · 403: sé quién eres y no puedes

  Scenario: Erik no administra usuarios
    * def erik = call read('_login.feature') usuarios.erik
    Given url iamUrl
    And path '/api/v1/users'
    And headers erik.auth
    When method get
    Then status 403
    And match response.code == 'ROLE_NOT_ALLOWED'
