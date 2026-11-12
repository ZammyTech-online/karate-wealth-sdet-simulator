# Memoria de Aprendizajes y Reglas Permanentes

## Causa Raíz 1: Procesos Fantasma en Port 3000
- **Hallazgo**: Procesos externos (como dev servers de Next.js u otras apps en Node) pueden intentar usar el puerto 3000 si no se valida antes.
- **Regla**: Siempre verificar con `netstat -ano | Select-String 3000` y matar PIDs ajenos antes de arrancar `Nordvik.Host`.

## Causa Raíz 2: Variable Substitution en Cuerpos JSON de Karate
- **Hallazgo**: Escribir `{ "user": reqUser }` en Karate puede serializar `"reqUser"` como literal de texto si no se declara o referencia adecuadamente.
- **Regla**: Usar la sintaxis canónica de Karate: `#(nombreVar)` o instanciar el payload mediante bloques JSON nativos de Karate.

## Causa Raíz 3: Lecciones y Escalera de Torre de Control
- **Hallazgo**: `escalera.json` y `ControlTower.cs` son el cerebro que alimenta el visualizador web de la Torre de Control y la API `/api/v1/lecciones`.
- **Regla**: Toda lección nueva debe crearse a través del pipeline `_generador/generar.py` para sincronizar `lecciones/`, `soluciones/`, `pistas.md` y registrarse en `torre/escalera.json`.

## Causa Raíz 4: Rutas Relativas de Recursos en Mocks de Karate
- **Hallazgo**: Al usar `karate.start({ mock: 'this:_mock_demo.feature', port: 0 })`, si el archivo `_mock_demo.feature` solo se escribe dentro del subdirectorio `ejercicios/`, la ejecución del `ejemplo.feature` (en la raíz de la lección) falla con `FileNotFoundException` porque `this:` busca en el mismo directorio del feature llamador.
- **Regla**: En `generar.py`, los archivos de apoyo (`lesson["apoyo"]`) deben copiarse tanto en la raíz de la lección `ldir` como en `ldir/ejercicios` y `sdir/` para que `this:` resuelva correctamente en todos los contextos.

## Causa Raíz 5: Incompatibilidad de Sintaxis de Match para Comparaciones Numéricas
- **Hallazgo**: La sentencia `* match latency < maxMs` o `* match val <= 300` provoca error de parseo en Karate (`syntax error, expected '==' for match` o `no step-definition method match found`).
- **Regla**: Para comparaciones relativas (`<`, `<=`, `>`, `>=`), usar la palabra clave `assert` (ej. `* assert latency < maxMs` o `* assert p95Threshold <= 300`).

## Causa Raíz 6: Ephemeral Port Exhaustion en Windows (BindException)
- **Hallazgo**: La ejecución paralela rápida de múltiples hilos en Karate con sockets TCP efímeros reciclados contra `localhost:5125` causa `java.net.BindException: Address already in use: getsockopt` en Windows por el estado `TIME_WAIT`.
- **Regla**: Configurar el runner JUnit con soporte para parametrizar hilos concurrentes (`karate.threads`, por defecto 1 en suites locales intensivas de loopback) o inyectar flags de reutilización de socket.

## Causa Raíz 7: Bloqueo de Binarios .NET durante Recompilación
- **Hallazgo**: Ejecutar `dotnet build Nordvik.sln` mientras instancias previas de `Nordvik.Host` están en ejecución produce `MSB3026: Could not copy ... because it is being used by another process`.
- **Regla**: Siempre terminar limpiamente los procesos de `Nordvik.Host` antes de reconstruir la solución, y reiniciar el host como daemon tras la compilación.

## Causa Raíz 8: Invocación de `openModal` no definida en `app.js`
- **Hallazgo**: `acad-btn-correct` invocaba `openModal(...)`, pero la función base de modal se llamaba `modal(title, html, actions)`, provocando un fallo de JavaScript en consola al corregir lecciones.
- **Regla**: Proveer un alias canónico `openModal(title, html)` que invoque `modal(title, html, [{ label: 'Cerrar', kind: 'quiet' }])` asegurando robustez total en la interfaz.

## Causa Raíz 9: Fallback de Clases CSS en Componentes SVG Dinámicos
- **Hallazgo**: Al pasar un estado no registrado a `BrunoAvatar.render('UNKNOWN')`, el avatar renderizaba los trazos de `IDLE` pero conservaba la clase CSS no parametrizada `bruno-unknown`, rompiendo estilizaciones dependientes.
- **Regla**: Normalizar el estado antes de inyectar clases CSS: `const normalizedState = states[state] ? state : 'IDLE'`.
