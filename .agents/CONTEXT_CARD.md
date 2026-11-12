# Nordvik Karate & SDET Context Card

## Stack & Arquitectura
- **Plataforma Nordvik**: .NET 8 (168 microservicios C# autónomos orquestados en Nordvik.Host).
- **Core Bancario API**: .NET 8 (`soderberg-wealth-api`) en C# con Swagger y endpoints financieros en `http://localhost:5125`.
- **Academia y Torre de Control**: Web interactiva (`torre/web`) servida por `Nordvik.Host` en `http://localhost:3000`.
- **Karate DSL Suite**: Java 21 (Eclipse Temurin), Apache Maven 3.9.x, Karate 1.4.1.

## Puertos y Servicios
- `http://localhost:3000`: Torre de Control & Academia Interactiva Karate Nordvik.
- `http://localhost:5125`: Soderberg Wealth Core API (.NET 8).
- `6001-7712`: Rango de 168 microservicios Nordvik (catalog, iam, order-management, pii-vault, etc.).

## Comandos Esenciales
- Typecheck / Build Platform: `dotnet build D:\WORKSPACE_KARATE\nordvik\Nordvik.sln`
- Levantar plataforma completa: `powershell -ExecutionPolicy Bypass -File D:\WORKSPACE_KARATE\nordvik\tools\nordvik.ps1 up -Profile completo`
- Test Karate Suite: `mvn test -f D:\WORKSPACE_KARATE\karate-test-suite\pom.xml`
- Academia Corregir: `powershell -ExecutionPolicy Bypass -File D:\WORKSPACE_KARATE\nordvik\academia\academia.ps1 corregir <Lxx>`
- Academia Verificar Soluciones: `powershell -ExecutionPolicy Bypass -File D:\WORKSPACE_KARATE\nordvik\academia\academia.ps1 verificar`
