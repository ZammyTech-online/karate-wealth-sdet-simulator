# Pistas de L37

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L37 E0X`.

## E01 · Configurar afterScenario en línea

karate.log('done')

## E02 · Intercepción de errores y limpieza

function() { karate.log('reset') }

## E03 · Lectura de metadatos del escenario

match name == '#present'

## E04 · Medición de tiempo transcurrido con karate.info

match karate.info.featureDir == '#present'

## E05 · Java Interop en Hook de Validación

System.currentTimeMillis() devuelve un timestamp
