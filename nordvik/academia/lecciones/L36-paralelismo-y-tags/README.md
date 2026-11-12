# L36 · Ejecución Paralela, Tags y Runner JUnit 5

En un entorno empresarial bancario, una suite completa puede tener miles de assertions. Ejecutarlas secuencialmente tardaría horas. Karate soporta **ejecución paralela nativa** controlada por etiquetas (`@tags`) y el runner de JUnit 5.

## La norma
- `Runner.path("classpath:lecciones").tags("@smoke", "~@ignore").parallel(5)`
- La tilde `~` niega una etiqueta (excluye tests inestables o lentos).
- Los escenarios paralelos deben ser **herméticos** y no compartir datos mutables en disco o BD.
