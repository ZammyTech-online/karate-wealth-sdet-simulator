# L20 · Contratos y firma electrónica: un flujo con estados

## La idea

Un contrato de Nordvik vive en una **máquina de estados**:

```
DRAFT ──send──▶ PENDING_SIGNATURE ──firma──▶ (PARTIALLY_SIGNED) ──▶ ACTIVE ──▶ WITHDRAWN · SUPERSEDED · TERMINATED
                                     └──▶ EXPIRED · INVALID_SIGNATURE
```

1. El asesor lo crea (`POST /api/v2/contracts`) → **DRAFT**. El contenido sale de la plantilla y del motor de cláusulas.
2. El asesor lo envía (`POST /{id}/send`) → **PENDING_SIGNATURE**. Ya no se puede tocar.
3. El cliente abre una **sesión de firma** (`POST /{id}/signing-sessions` con `party: 'CUSTOMER'`). Nordvik pide la firma a NordID
   (el BankID simulado, puerto 7701), que tarda unos segundos: la sesión empieza en `PENDING`.
4. Cuando NordID termina, la sesión pasa a `COMPLETED` y el contrato a **ACTIVE**.

## La integridad: qué se firmó

Cada contrato tiene un `contentHash` (SHA-256 de su contenido canónico). La sesión de firma guarda `signedHash`:
**el cliente firma exactamente ese hash**. Si alguien cambiara una cláusula después, el hash no coincidiría.

## Una trampa real: una firma a la vez

Como el BankID de verdad, NordID solo permite **una firma en curso por persona**. Si abres otra sesión mientras la anterior
sigue `PENDING`, recibes **409 `SIGNING_ALREADY_IN_PROGRESS`**. Por eso cada prueba que abre una sesión **espera a que termine**:
si no, rompe a la siguiente (y ese test "falla a veces" según el orden en que se ejecuten).

## La norma

1. Prueba **cada transición** y también las prohibidas (enviar dos veces, firmar un DRAFT).
2. Las transiciones asíncronas (la firma) se esperan con `retry until`, como en L18.
3. Guarda lo que necesitas de cada paso en variables con nombre de negocio: `contrato`, `sesion`.

## Tus ejercicios

`academia corregir L20`.
