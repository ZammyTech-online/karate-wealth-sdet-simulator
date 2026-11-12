# L39 · Integración Continua con GitHub Actions y Reportes HTML

Un framework de pruebas no aporta valor si no se ejecuta automáticamente en cada Pull Request. Un SDET Senior diseña el pipeline CI/CD, publica los reportes interactivos de Karate (`target/karate-reports`) y bloquea el merge si la regresión falla.

## La norma
- Workflow en `.github/workflows/karate-tests.yml`.
- Cache de dependencias de Maven (`~/.m2/repository`).
- Ejecución `mvn test -Dkarate.options="--tags @smoke"`.
- Publicación de artefactos HTML con `actions/upload-artifact`.
