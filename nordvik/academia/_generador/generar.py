"""
Generador de la Academia Nordvik.
Cada lección se define una sola vez como datos. Los huecos se marcan con ⟦ ⟧ en la solución:
el ejercicio los sustituye por ___ y la solución los conserva, así nunca se desincronizan.
Uso: python generar.py   (desde nordvik/academia/_generador)
"""
import os, re, shutil, importlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LECCIONES = os.path.join(ROOT, "lecciones")
SOLUCIONES = os.path.join(ROOT, "soluciones")
HUECO = re.compile(r"⟦(.*?)⟧", re.S)

def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text.strip("\n") + "\n")

def header(code, ex, kind):
    lines = [
        "# " + "─" * 70,
        f"# {code} · {ex['id']} · {ex['title']}",
        "#",
    ]
    for l in ex["enunciado"].strip().split("\n"):
        lines.append("# " + l)
    lines.append("#")
    if kind == "ejercicio":
        lines.append(f"# Rellena cada ___ y comprueba con:  academia corregir {code}")
        lines.append(f"# Pista: {ex['pista1']}")
    else:
        lines.append("# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.")
        if ex.get("explicacion"):
            for l in ex["explicacion"].strip().split("\n"):
                lines.append("# " + l)
    lines.append("# " + "─" * 70)
    return "\n".join(lines)

def feature(code, ex, kind):
    body = ex["feature"].strip("\n")
    body = HUECO.sub("___" if kind == "ejercicio" else (lambda m: m.group(1)), body)
    tags = f"@{code} @{ex['id']}" + (" @solucion" if kind == "solucion" else "")
    return f"{header(code, ex, kind)}\n{tags}\nFeature: {code} {ex['id']} · {ex['title']}\n\n{body}\n"

def practica(code, url):
    return f"""
# Campo de práctica de {code}: prueba aquí lo que quieras. No cuenta para tu progreso y no se corrige.
# Ejecuta con:  academia practica {code}
@practica
Feature: {code} · Campo de práctica

  Background:
    * url {url}

  Scenario: Mi prueba libre
    * print 'Campo de práctica de {code} listo. Escribe tus pasos debajo de esta línea.'
"""

def pistas(lesson):
    out = [f"# Pistas de {lesson['code']}", "", "Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion {0} E0X`.".format(lesson["code"]), ""]
    for ex in lesson["ejercicios"]:
        out += [f"## {ex['id']} · {ex['title']}", "", ex["pista2"].strip(), ""]
    return "\n".join(out)

def build(lesson):
    folder = f"{lesson['code']}-{lesson['slug']}"
    ldir = os.path.join(LECCIONES, folder)
    sdir = os.path.join(SOLUCIONES, folder)
    write(os.path.join(ldir, "README.md"), lesson["readme"])
    write(os.path.join(ldir, "ejemplo.feature"), lesson["ejemplo"])
    write(os.path.join(ldir, "practica.feature"), practica(lesson["code"], lesson["url"]))
    write(os.path.join(ldir, "pistas.md"), pistas(lesson))
    for ex in lesson["ejercicios"]:
        name = f"{ex['id']}.feature"
        write(os.path.join(ldir, "ejercicios", name), feature(lesson["code"], ex, "ejercicio"))
        write(os.path.join(sdir, name), feature(lesson["code"], ex, "solucion"))
    for fname, content in lesson.get("apoyo", {}).items():
        write(os.path.join(ldir, "ejercicios", fname), content)
        write(os.path.join(sdir, fname), content)
    return folder, len(lesson["ejercicios"])

if __name__ == "__main__":
    # Los ficheros se sobrescriben; no se borra nada (si se renombra una lección, borra a mano la carpeta antigua).
    total = 0
    for modname in ["lecciones_a", "lecciones_b", "lecciones_c"]:
        try:
            mod = importlib.import_module(modname)
        except ModuleNotFoundError:
            continue
        for lesson in mod.LECCIONES:
            folder, n = build(lesson)
            total += n
            print(f"{folder}: {n} ejercicios")
    print(f"Total: {total} ejercicios")
