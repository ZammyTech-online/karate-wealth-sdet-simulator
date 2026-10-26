# L17 · IDOR y máscaras: que cada uno vea solo lo suyo

## La idea

Un **IDOR** (*Insecure Direct Object Reference*) es el fallo más común de una API: cambias el id de la URL
y ves los datos de otra persona. En Nordvik lo probamos así:

- Ana pide la ficha de **Lars** → **404 CUSTOMER_NOT_FOUND**. No es un 403: Nordvik ni siquiera confirma que Lars exista.
- Erik pide a **Sofía**, que no es clienta suya → **403 ACCESS_DENIED** con el motivo en `detail`. A un empleado sí se le explica.

Y cuando sí puedes ver al cliente, puede que no lo veas entero. El número nacional (`nationalId`) sale:

| Quién mira | Cómo sale |
|---|---|
| El propio cliente | completo |
| Su asesor | enmascarado: `19850312-****` → contiene `**-****` |
| Cumplimiento | completo (está auditando) |

## La norma

1. Prueba cada recurso con **al menos tres actores**: el dueño, otro del mismo rol y alguien de otro rol.
2. Un 404 por IDOR es correcto si el actor no debe saber que el recurso existe.
3. Las máscaras se comprueban con `contains` y `!contains`, nunca comparando el valor entero (cambia en cada alta).

## El fundamento

La tabla *actor × recurso × resultado* es tu mapa de seguridad. `Scenario Outline` la convierte en pruebas
de una sola pasada: cada fila es un actor.

## Tus ejercicios

`academia corregir L17`.
