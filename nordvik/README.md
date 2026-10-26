# Nordvik Wealth & Pension

Simulador de una gestora nórdica de inversiones y pensiones para aprender a probar APIs con **Karate DSL**, de cero a senior.
156 microservicios .NET 8 en 17 dominios y 12 sistemas externos simulados, todo dentro de un solo proceso.

## Empieza aquí

```powershell
cd D:\WORKSPACE_KARATE\nordvik
.\nordvik.cmd up -Nivel 0     # nivel 0: 8 servicios
.\nordvik.cmd torre           # Torre de control: http://localhost:3000
cd academia; .\academia.cmd leer L00
```

| Qué | Dónde |
|---|---|
| Guía paso a paso con capturas | [docs/guia](docs/guia/README.md) |
| Torre de control y escalera de niveles 0-4 | [torre](torre/README.md) · [ADR-014](docs/adr/ADR-014-torre-de-control-y-escalera.md) |
| Academia de Karate (34 lecciones, 187 ejercicios) | [academia](academia/README.md) |
| Portal de desarrolladores (servicios, eventos, OpenAPI) | [docs](docs/README.md) |
| Flujos de negocio F-01 a F-12 | [docs/flows](docs/flows/README.md) |
| Decisiones de arquitectura | [docs/adr](docs/adr/README.md) |
| Informe de la noche | [docs/informes/2026-10-26-noche.md](docs/informes/2026-10-26-noche.md) |

## Comandos

| Comando | Qué hace |
|---|---|
| `nordvik up -Nivel N` | Arranca un nivel de la escalera (0 a 4) |
| `nordvik niveles` | La escalera: servicios, lecciones y ruta de cada nivel |
| `nordvik torre` | Abre la Torre de control |
| `nordvik status` · `nordvik smoke` | Qué está encendido · prueba de humo |
| `nordvik down` · `nordvik reset` | Parar · parar y volver a los datos semilla |
| `nordvik test` | Pruebas unitarias y de API del backend |
