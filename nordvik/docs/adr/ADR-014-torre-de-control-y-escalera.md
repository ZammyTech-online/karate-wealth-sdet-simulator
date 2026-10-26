# ADR-014 · Torre de control y escalera de niveles: encender y apagar en caliente

- **Estado:** aceptada (2026-10-26)
- **Ámbito:** `platform/Nordvik.Host` (`Supervisor.cs`, `ControlTower.cs`, `Escalera.cs`), `torre/` (escalera y web), `tools/nordvik.ps1`, `academia/academia.ps1`

## Contexto

Nordvik tiene 156 microservicios y 12 sistemas externos. Encendidos todos ocupan unos 300-360 MB, que caben en 24 GB, pero
**no caben en la cabeza** de quien empieza: 168 Swagger, 1400 endpoints y 30 trampas el primer día no enseñan nada.

Hasta ahora había perfiles fijos (`nivel-0`, `nivel-3`, `nivel-6`, `nivel-9`, `completo`). Para cambiar de perfil había que
parar el anfitrión y volver a arrancarlo, y nada relacionaba un perfil con las lecciones de la academia ni con los flujos de negocio.

## Decisión

1. **Una escalera de 5 niveles es la fuente única** (`torre/escalera.json`): nombre, perfil, lecciones, conceptos de Karate,
   negocio, checkpoint y misiones de cada nivel. Los **servicios** de cada nivel no se escriben a mano: salen del código,
   `nivel N = servicios con oleada <= N`. Los perfiles `profiles/*.json` deben coincidir exactamente (lo comprueba un test).

   | Nivel | Nombre | Perfil | Servicios | Lecciones |
   |---|---|---|---|---|
   | 0 | Principiante · Leer el mercado | nivel-0 | 8 | L00-L15 |
   | 1 | Básico · Clientes y permisos | nivel-3 | 30 | L16-L17 |
   | 2 | Intermedio · El dinero se mueve | nivel-6 | 79 | L18-L21 |
   | 3 | Avanzado · Datos sensibles y sagas | nivel-9 | 137 | L22-L27 |
   | 4 | Senior · La empresa completa | completo | 168 | L28-L33 |

2. **El Supervisor enciende y apaga servicios sin reiniciar.** Cada servicio ya era su propia `WebApplication` dentro del
   proceso (ADR-001): encender = construirla y abrir su puerto; apagar = pararla y liberar el puerto. Los datos (SQLite) se conservan.
   - Al encender, trae antes sus dependencias (`DependsOn`), en orden.
   - Al apagar, si hay servicios encendidos que dependen de él responde 409 `HAS_RUNNING_DEPENDENTS` con la lista;
     con `?cascada=true` apaga primero a los dependientes y después a él.
   - `clock-service` es núcleo: no se apaga nunca.
   - `OptionalDependsOn` marca dependencias con plan B (por ejemplo, payout-execution usa tax-withholding si está
     encendido; si no, retiene el 30 % provisional). No se encienden solas.

3. **La Torre de control** es una web en el puerto **3000** servida por el propio anfitrión (porque es quien sabe qué está
   encendido). Pantallas: Escalera, Misiones, Mapa (con ficha de cada servicio), Laboratorio (reloj, fallos, trampas) y Cómo
   trabajar. Su API (`/api/v1/estado`, `niveles`, `servicios`, `dominios`, `misiones`, `lecciones`, `trampas`, `flujos`) y una
   pasarela `/api/v1/proxy/{servicio}/...` para que la web llame a cualquier servicio sin problemas de CORS.
   El puerto 3000 es el que el plan reservaba para la Academy (el 6000 lo bloquean los navegadores).

4. **Misiones** (M01-M23): un objetivo de negocio de punta a punta con sus servicios clave, pasos (método y ruta reales),
   lecciones, flujo y trampas. "Preparar" enciende exactamente lo que hace falta. Cada flujo F-01..F-12 tiene su misión.

5. **La academia pregunta a la torre.** `academia ejemplo L20` lee la escalera para saber el nivel de la lección y, si
   Nordvik está en marcha, pide a la torre `POST /api/v1/lecciones/L20/preparar` en lugar de reiniciar con otro perfil.

## Coherencia comprobada por tests (`TowerTests.cs`)

- Perfil de cada nivel == servicios con oleada <= nivel.
- Las 34 lecciones están en un solo nivel y solo usan servicios de su nivel (se lee qué URL usa cada `.feature` de la solución).
- Cada paso de cada misión existe en el OpenAPI vivo de su servicio y ningún servicio (ni sus dependencias) es de un nivel superior.
- Todos los servicios sobreviven a apagarse y encenderse (el test baja la empresa al nivel 0 y la vuelve a subir al 4).

## Consecuencias

- `nordvik up -Nivel N` arranca un nivel; `nordvik niveles` muestra la escalera; `nordvik torre` abre la web.
- Añadir un servicio nuevo = darle su oleada. Si su nivel no cuadra con su perfil, lecciones o misiones, los tests lo dicen.
- Encontró una incoherencia real: payout-execution (nivel 3) dependía de tax-withholding (nivel 4); ahora es una dependencia opcional.
