package academia;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Traduce los mensajes de error de Karate a explicaciones para alguien que está aprendiendo. */
final class Errores {

    private Errores() { }

    private static final Map<Pattern, String> REGLAS = new LinkedHashMap<>();

    static {
        regla("___ is not defined|\\b___\\b", "Todavía quedan huecos ___ sin rellenar en este ejercicio.");
        regla("status code was: (\\d+), expected: (\\d+)",
                "El servidor respondió $1, pero tu test esperaba $2. Si la petición es correcta, revisa tu predicción; si no, revisa url, path, param o el cuerpo.");
        regla("Connection refused|connect timed out|ConnectException",
                "No hay ningún servicio escuchando en esa URL. ¿Has arrancado Nordvik? Ejecuta: nordvik up");
        regla("match failed: EQUALS", "El valor que llegó no es igual al que esperabas. Compara 'actual' (lo que llegó) con 'expected' (lo que escribiste) en el detalle.");
        regla("match failed: CONTAINS_ONLY", "La lista no contiene exactamente esos elementos: o falta alguno o sobra alguno.");
        regla("match failed: CONTAINS", "Lo que llegó no contiene lo que esperabas. Revisa mayúsculas, tipos (texto o número) y nombres de campos.");
        regla("match failed: EACH", "Algún elemento de la lista no cumple la condición de 'match each'.");
        regla("not a number|is not a number", "Estás comparando un número con un texto. En JSON, 5 y '5' no son lo mismo.");
        regla("(\\w+) is not defined", "Usas la variable '$1', que no existe. ¿Está bien escrita? ¿La definiste con '* def' antes de usarla?");
        regla("SyntaxError|<eval>", "Hay un error de sintaxis en una expresión JavaScript: revisa comillas, paréntesis y llaves.");
        regla("assert evaluated to false", "La condición del 'assert' ha dado falso.");
        regla("cannot find feature file|file not found|could not find or read file", "El fichero que intentas leer no existe en esa ruta. Revisa el nombre y que esté en la misma carpeta.");
        regla("no step-definition method match found", "Karate no entiende una línea: revisa que empiece por Given/When/Then/And/* y que la palabra clave esté bien escrita.");
        regla("Invalid URL|unknown protocol|URISyntaxException|Illegal character", "La URL no es válida. Debe empezar por http:// y no tener espacios.");
    }

    private static void regla(String regex, String texto) {
        REGLAS.put(Pattern.compile(regex, Pattern.CASE_INSENSITIVE), texto);
    }

    static String explicar(String errores, int escenarios) {
        if (escenarios == 0) return "El fichero no tiene ningún escenario que ejecutar (¿falta 'Scenario:' o hay un error de formato?).";
        String raw = errores == null ? "" : errores;
        for (Map.Entry<Pattern, String> e : REGLAS.entrySet()) {
            Matcher m = e.getKey().matcher(raw);
            if (m.find()) {
                String out = e.getValue();
                for (int i = 1; i <= m.groupCount(); i++) out = out.replace("$" + i, m.group(i) == null ? "" : m.group(i));
                return out + detalle(raw);
            }
        }
        return "Ha fallado: " + primeraLinea(raw);
    }

    private static String detalle(String raw) {
        Matcher m = Pattern.compile("actual: (.{1,80}?), expected: (.{1,80}?),").matcher(raw);
        return m.find() ? "  [llegó: " + m.group(1) + " · esperabas: " + m.group(2) + "]" : "";
    }

    private static String primeraLinea(String raw) {
        String line = raw.strip().split("\\R", 2)[0];
        return line.length() > 160 ? line.substring(0, 160) + "..." : line;
    }
}
