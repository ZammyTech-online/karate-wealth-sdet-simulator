# Pistas de L36

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L36 E0X`.

## E01 · Etiquetado semántico con @smoke

El escenario debe marcar @smoke

## E02 · Filtrado condicional por tag en runtime

Valida lógica simple con match

## E03 · Exclusión de escenarios con tilde

def tagFilter = '~@flaky'

## E04 · Configurar número de hilos paralelos

match hilos == 4

## E05 · Validar aserción independiente de tiempo

match item.completed == true
