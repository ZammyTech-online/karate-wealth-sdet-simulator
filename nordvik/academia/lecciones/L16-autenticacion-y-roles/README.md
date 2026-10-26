# L16 · Autenticación y roles: tokens OAuth2 en Karate

**Tramo 2.** Desde aquí trabajas con el Nordvik de verdad: clientes, asesores, contratos y dinero.
Arranca el perfil grande antes de empezar:

```
nordvik up -Profile nivel-6
```

## La idea

Casi todo en Nordvik exige un **token**. Lo pides a `iam-service` (puerto 6200) con el flujo *password* de OAuth2,
que se envía como formulario (`application/x-www-form-urlencoded`), no como JSON:

```gherkin
Given url iamUrl
And path '/api/v1/oauth/token'
And form field grant_type = 'password'
And form field username = 'asesor.erik'
And form field password = 'Erik2026!'
When method post
Then status 200
* def token = response.access_token
```

Luego lo mandas en cada petición con la cabecera `Authorization: Bearer <token>`:

```gherkin
And header Authorization = 'Bearer ' + token
```

## Los usuarios de prueba

| Usuario | Rol | Puede... |
|---|---|---|
| cliente.ana / cliente.lars | Cliente | ver lo suyo y nada más |
| asesor.erik / asesora.maja | Asesor | gestionar a *sus* clientes asignados |
| operaciones.olof | Operaciones | pagos, ingresos, procesos batch |
| compliance.karin | Cumplimiento | ver datos completos para auditar |
| admin | Administración | todo, incluido gestionar usuarios |

Todos están en `config/karate-config.js` como `usuarios.ana`, `usuarios.erik`... y hay un ayudante
`_login.feature` que hace el login por ti:

```gherkin
* def erik = call read('_login.feature') usuarios.erik
And headers erik.auth
```

## La norma

1. **401** significa "no sé quién eres" (falta el token o no vale). **403** significa "sé quién eres y no puedes".
2. Cada respuesta de error es un *problem* RFC 9457: mira siempre `code`, no el texto de `detail`.
3. Pide el token **una vez** por feature (`callonce`): pedirlo en cada escenario hace la suite lenta.

## El fundamento

Probar seguridad no es solo probar que entras: es probar que **cada rol ve exactamente lo que debe y ni un byte más**.
El caso feliz es el 20 % de tu trabajo; el otro 80 % son los 401 y 403.

## Tus ejercicios

`academia corregir L16`.
