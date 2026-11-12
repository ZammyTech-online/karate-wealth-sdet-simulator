# L37 · Interceptores y Java RuntimeHook

Cuando la regulación bancaria exige registrar cada petición HTTP, firmar auditorías criptográficas o enmascarar tokens antes de que toquen el reporte HTML, un SDET implementa un **ExecutionHook / RuntimeHook** en Java.

## La norma
- Implementa `com.intuit.karate.RuntimeHook`.
- Métodos clave: `beforeScenario()`, `afterScenario()`, `beforeStep()`, `afterStep()`.
- Permite inyectar lógica pura en el ciclo de vida de Karate sin ensuciar los archivos `.feature`.
