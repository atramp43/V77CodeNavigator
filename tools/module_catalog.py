"""Create a standalone method catalog from exported text or a saved 7.7 MD."""
import argparse
import html
import json
import re
import zlib
from pathlib import Path

ap = argparse.ArgumentParser()
ap.add_argument("source", type=Path)
ap.add_argument("output", type=Path)
ap.add_argument("--stream", default="TypedText/ModuleText_Number1/MD Programm text")
args = ap.parse_args()
if args.source.suffix.lower() == ".md":
    import olefile
    with olefile.OleFileIO(args.source) as md:
        raw = zlib.decompress(md.openstream(args.stream).read(), -15)
    source = raw.decode("cp1251")
else:
    raw = args.source.read_bytes()
    try:
        source = raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        source = raw.decode("cp1251")
from method_descriptions import parse_methods
rows = parse_methods(source)
# Resolve only direct calls to a uniquely named implementation in this module.
from collections import defaultdict
names = defaultdict(list)
for method in rows:
    names[method["name"].casefold()].append(method)
    method["calls"] = []
    method["callers"] = []
lex = re.compile(r'//[^\n]*|"(?:[^"]|"")*(?:"|$)|[\w]+|[^\s]')
for caller in rows:
    tokens = []
    line = caller["line"]
    end = 0
    for token in lex.finditer(caller.get("source", "")):
        line += caller["source"].count("\n", end, token.start())
        value = token[0]
        if not value.startswith("//"):
            tokens.append((value, line))
        line += value.count("\n")
        end = token.end()
    # Skip the complete declaration, including multiline formal parameters.
    depth = 0
    body = False
    for i, (value, at) in enumerate(tokens):
        if not body:
            if value == "(":
                depth += 1
            elif value == ")":
                depth -= 1
                body = depth == 0
            continue
        targets = names.get(value.casefold())
        if not targets or len(targets)!=1 or tokens[i-1][0]==".":
            continue
        if i+1<len(tokens) and tokens[i+1][0]=="(":
            target = targets[0]
            caller["calls"].append([target["line"], at])
            target["callers"].append([caller["line"], at])

external_modules = []
if args.source.suffix.lower()==".md" and args.stream=="TypedText/ModuleText_Number1/MD Programm text":
    from external_calls import index
    with olefile.OleFileIO(args.source) as md:
        external_modules=index(md,rows)
template = Path(__file__).with_suffix(".html").read_text(encoding="utf-8-sig")
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(template.replace("/*DATA*/", json.dumps(rows, ensure_ascii=False).replace("<","\\u003c"))
                      .replace("/*MODULES*/",json.dumps(external_modules,ensure_ascii=False).replace("<","\\u003c"))
                      .replace("<!--SOURCE-->",html.escape(args.source.name)),encoding="utf-8")
print(len(rows), "methods;", sum(r["kind"]=="Функция" for r in rows),"functions;", sum(bool(r["inputs"]) for r in rows),"with IN")



