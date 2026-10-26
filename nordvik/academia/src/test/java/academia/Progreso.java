package academia;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import java.util.TreeMap;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Guarda y lee progreso.json sin dependencias externas: { "L01": { "E01": "OK", ... }, ... }. */
final class Progreso {

    private Progreso() { }

    static Map<String, Map<String, String>> load(Path file) throws IOException {
        Map<String, Map<String, String>> out = new TreeMap<>();
        if (!Files.exists(file)) return out;
        String json = Files.readString(file, StandardCharsets.UTF_8);
        Matcher lesson = Pattern.compile("\"(L\\d+)\"\\s*:\\s*\\{([^}]*)\\}").matcher(json);
        while (lesson.find()) {
            Map<String, String> ex = new TreeMap<>();
            Matcher e = Pattern.compile("\"(E\\d+)\"\\s*:\\s*\"(\\w+)\"").matcher(lesson.group(2));
            while (e.find()) ex.put(e.group(1), e.group(2));
            out.put(lesson.group(1), ex);
        }
        return out;
    }

    static void save(Path file, Map<String, Map<String, String>> data) throws IOException {
        StringBuilder sb = new StringBuilder("{\n");
        int i = 0;
        for (var l : data.entrySet()) {
            sb.append("  \"").append(l.getKey()).append("\": { ");
            int j = 0;
            for (var e : l.getValue().entrySet()) {
                sb.append("\"").append(e.getKey()).append("\": \"").append(e.getValue()).append("\"");
                if (++j < l.getValue().size()) sb.append(", ");
            }
            sb.append(" }");
            if (++i < data.size()) sb.append(",");
            sb.append("\n");
        }
        sb.append("}\n");
        Files.writeString(file, sb.toString(), StandardCharsets.UTF_8);
    }
}
