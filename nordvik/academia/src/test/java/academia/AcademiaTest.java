package academia;

import com.intuit.karate.Results;
import com.intuit.karate.Runner;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.time.LocalDateTime;
import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * El corrector de la Academia Nordvik.
 *
 * Modos (-Dmodo=...):
 *   ejercicios  corrige tus ejercicios y guarda tu progreso (por defecto; nunca rompe el build)
 *   ejemplo     ejecuta el ejemplo resuelto de la lección
 *   practica    ejecuta tu campo de práctica
 *   soluciones  comprueba que todas las soluciones oficiales pasan (lo usa el equipo)
 *   huecos      comprueba que ningún ejercicio sin rellenar pasa por casualidad (lo usa el equipo)
 *   revision    ejemplos + prácticas + soluciones (lo usa el equipo antes de cada entrega)
 */
public class AcademiaTest {

    private static final PrintStream OUT = new PrintStream(System.out, true, StandardCharsets.UTF_8);
    private static final Path LECCIONES = Paths.get("lecciones");
    private static final Path SOLUCIONES = Paths.get("soluciones");
    private static final Path PROGRESO = Paths.get("progreso.json");
    private static final String LINEA = "═".repeat(66);
    private static final String FINA = "─".repeat(66);

    record Outcome(String lesson, String exercise, String title, boolean ok, String explanation, String hint, String report) { }

    @Test
    void academia() throws IOException {
        String modo = System.getProperty("modo", "ejercicios");
        String leccion = System.getProperty("leccion", "todas");
        List<Path> lessons = findLessons(leccion);
        if (lessons.isEmpty()) {
            OUT.println("No encuentro la lección '" + leccion + "'. Las lecciones se llaman L00, L01, L02...");
            return;
        }
        switch (modo) {
            case "ejemplo" -> assertTrue(runSingle(lessons, "ejemplo.feature", "Ejemplo resuelto"), "Algún ejemplo ha fallado");
            case "practica" -> runSingle(lessons, "practica.feature", "Campo de práctica");
            case "soluciones" -> assertTrue(correct(lessons, true, false), "Alguna solución oficial no pasa");
            case "huecos" -> assertTrue(checkBlanks(lessons), "Algún ejercicio pasa con los huecos sin rellenar");
            case "revision" -> {
                boolean a = runSingle(lessons, "ejemplo.feature", "Ejemplo resuelto");
                boolean b = runSingle(lessons, "practica.feature", "Campo de práctica");
                boolean c = correct(lessons, true, false);
                assertTrue(a && b && c, "La revisión ha encontrado fallos");
            }
            default -> correct(lessons, false, true);
        }
    }

    // ---------- descubrimiento ----------

    private static List<Path> findLessons(String filter) throws IOException {
        try (Stream<Path> s = Files.list(LECCIONES)) {
            return s.filter(Files::isDirectory)
                    .filter(p -> filter.equals("todas") || p.getFileName().toString().toUpperCase().startsWith(filter.toUpperCase()))
                    .sorted().collect(Collectors.toList());
        }
    }

    private static List<Path> exercises(Path dir) throws IOException {
        if (!Files.isDirectory(dir)) return List.of();
        try (Stream<Path> s = Files.list(dir)) {
            return s.filter(p -> p.getFileName().toString().matches("E\\d+.*\\.feature")).sorted().collect(Collectors.toList());
        }
    }

    private static String code(Path lesson) { return lesson.getFileName().toString().substring(0, 3); }

    private static String lessonTitle(Path lesson) {
        try {
            String first = Files.readAllLines(lesson.resolve("README.md"), StandardCharsets.UTF_8).get(0);
            return first.replaceFirst("^#\\s*", "");
        } catch (Exception e) {
            return lesson.getFileName().toString();
        }
    }

    // ---------- ejecución ----------

    private static Outcome run(String lesson, String exercise, Path file, String classpathRoot) throws IOException {
        String text = Files.readString(file, StandardCharsets.UTF_8);
        String title = firstMatch(text, "(?m)^Feature:\\s*(.+)$", exercise);
        String hint = firstMatch(text, "(?m)^#\\s*Pista:\\s*(.+)$", null);
        String rel = classpathRoot + "/" + file.subpath(1, file.getNameCount()).toString().replace('\\', '/');
        String reportDir = "target/academia/" + lesson + "/" + exercise.replace(".feature", "");
        Results r;
        PrintStream original = System.out;
        System.setOut(new PrintStream(java.io.OutputStream.nullOutputStream()));
        try {
            r = Runner.path("classpath:" + rel)
                    .outputCucumberJson(false).outputJunitXml(false).outputHtmlReport(true)
                    .reportDir(reportDir).parallel(1);
        } finally {
            System.setOut(original);
        }
        boolean ok = r.getFailCount() == 0 && r.getScenariosTotal() > 0;
        String explanation = ok ? null : Errores.explicar(r.getErrorMessages(), r.getScenariosTotal());
        return new Outcome(lesson, exercise.replace(".feature", ""), title, ok, explanation, hint, reportDir + "/karate-summary.html");
    }

    private static boolean runSingle(List<Path> lessons, String fileName, String label) throws IOException {
        boolean all = true;
        for (Path lesson : lessons) {
            Path f = lesson.resolve(fileName);
            if (!Files.exists(f)) continue;
            Outcome o = run(code(lesson), fileName, f, "lecciones");
            OUT.println((o.ok ? " [OK] " : " [XX] ") + code(lesson) + " · " + label + " · " + o.title);
            if (!o.ok) {
                OUT.println("        Qué ha pasado: " + o.explanation);
                OUT.println("        Detalle: " + o.report);
                all = false;
            }
        }
        return all;
    }

    private static boolean correct(List<Path> lessons, boolean solutions, boolean saveProgress) throws IOException {
        Map<String, Map<String, String>> progress = saveProgress ? Progreso.load(PROGRESO) : new TreeMap<>();
        int totalOk = 0, total = 0;
        for (Path lesson : lessons) {
            String c = code(lesson);
            Path dir = solutions ? SOLUCIONES.resolve(lesson.getFileName()) : lesson.resolve("ejercicios");
            List<Path> files = exercises(dir);
            if (files.isEmpty()) continue;
            OUT.println();
            OUT.println(LINEA);
            OUT.println(" " + lessonTitle(lesson) + (solutions ? "   (soluciones oficiales)" : ""));
            OUT.println(LINEA);
            int ok = 0;
            for (Path f : files) {
                String ex = f.getFileName().toString();
                Outcome o = run(c, ex, f, solutions ? "soluciones" : "lecciones");
                total++;
                if (o.ok) { ok++; totalOk++; }
                OUT.println((o.ok ? " [OK] " : " [  ] ") + o.exercise + "  " + o.title.replaceFirst("^L\\d+\\s+E\\d+\\s*·\\s*", ""));
                if (!o.ok) {
                    OUT.println("        Qué ha pasado: " + o.explanation);
                    if (o.hint != null) OUT.println("        Pista: " + o.hint);
                    OUT.println("        Más pistas: academia pista " + c + " " + o.exercise + "   ·   Detalle: " + o.report);
                }
                if (saveProgress) progress.computeIfAbsent(c, k -> new TreeMap<>()).put(o.exercise, o.ok ? "OK" : "PENDIENTE");
            }
            OUT.println(FINA);
            OUT.println(" Resultado " + c + ": " + ok + "/" + files.size() + "  " + bar(ok, files.size()));
            if (!solutions) OUT.println(" " + (ok == files.size()
                    ? "¡Lección superada! Siguiente: academia leer " + next(c)
                    : "Rellena los huecos ___ que faltan y vuelve a ejecutar: academia corregir " + c));
        }
        if (saveProgress) {
            Progreso.save(PROGRESO, progress);
            OUT.println();
            OUT.println(" Progreso guardado en progreso.json · " + LocalDateTime.now().withNano(0));
        }
        return totalOk == total;
    }

    private static boolean checkBlanks(List<Path> lessons) throws IOException {
        boolean allFail = true;
        for (Path lesson : lessons) {
            for (Path f : exercises(lesson.resolve("ejercicios"))) {
                Outcome o = run(code(lesson), f.getFileName().toString(), f, "lecciones");
                OUT.println((o.ok ? " [!!] PASA SIN RESOLVER " : " [ok] falla como debe ") + code(lesson) + " " + o.exercise);
                if (o.ok) allFail = false;
            }
        }
        return allFail;
    }

    // ---------- utilidades ----------

    private static String bar(int ok, int total) {
        int width = 20, filled = total == 0 ? 0 : Math.round(width * ok / (float) total);
        return "█".repeat(filled) + "░".repeat(width - filled) + " " + (total == 0 ? 0 : Math.round(100f * ok / total)) + " %";
    }

    private static String next(String code) {
        int n = Integer.parseInt(code.substring(1)) + 1;
        return String.format("L%02d", n);
    }

    private static String firstMatch(String text, String regex, String fallback) {
        Matcher m = Pattern.compile(regex).matcher(text);
        return m.find() ? m.group(1).trim() : fallback;
    }
}
