# ──────────────────────────────────────────────────────────────────────
# L13 · E01 · Llama al ayudante
#
# Llama a la feature ayudante, que está en la misma carpeta, con el prefijo X.
#
# Rellena cada ___ y comprueba con:  academia corregir L13
# Pista: read() recibe el nombre del fichero entre comillas. El ayudante empieza por guion bajo.
# ──────────────────────────────────────────────────────────────────────
@L13 @E01
Feature: L13 E01 · Llama al ayudante

  Scenario: Crear con el ayudante
    * def resultado = call read(___) { prefix: 'X' }
    * match resultado.response.prefix == 'X'
