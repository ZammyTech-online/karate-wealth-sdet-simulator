# ──────────────────────────────────────────────────────────────────────
# L16 · E05 · 403: sé quién eres y no puedes
#
# Erik es asesor y no puede listar los usuarios del sistema. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L16
# Pista: Ahora sí hay token válido; lo que falta es el rol. El código habla de rol.
# ──────────────────────────────────────────────────────────────────────
@L16 @E05
Feature: L16 E05 · 403: sé quién eres y no puedes

  Scenario: Erik no administra usuarios
    * def erik = call read('_login.feature') usuarios.erik
    Given url iamUrl
    And path '/api/v1/users'
    And headers erik.auth
    When method get
    Then status ___
    And match response.code == ___
