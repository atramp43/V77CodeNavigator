import re


def parse_methods(source):
    lines = source.replace("\r\r\n", "\n").replace("\r\n", "\n").replace("\r", "\n").split("\n")
    header = re.compile(r"^\s*(function|procedure|функция|процедура)\s+(\w+)\s*\(", re.I)
    rows = []
    comments = []
    current = None
    quoted = False
    for num, line in enumerate(lines, 1):
        # Preserve positions while hiding strings/comments from keyword detection.
        code = list(line)
        i = 0
        while i < len(line):
            if not quoted and line.startswith("//", i):
                code[i:] = " " * (len(line) - i)
                break
            if line[i] == '"':
                code[i] = " "
                if quoted and line[i+1:i+2] == '"':
                    code[i+1] = " "
                    i += 2
                    continue
                quoted = not quoted
            elif quoted:
                code[i] = " "
            i += 1
        clean = "".join(code)
        if not quoted and line.lstrip().startswith("//"):
            comments.append(line.lstrip()[2:].strip())
            continue
        m = header.match(clean)
        if m:
            docs = {}
            field = None
            for c in comments:
                tag = re.match(r"^(NAME|IN|OUT|RET(?:URN)?|RESULT|NOTE)\s*:\s*(.*)", c, re.I)
                if tag:
                    field = tag[1].upper()
                    docs[field] = tag[2].strip()
                elif c and field and not re.fullmatch(r"[-=\s]+", c):
                    docs[field] += "\n" + c
                else:
                    field = None
            sig = line.strip()
            j = num
            while ")" not in sig and j < len(lines) and j < num+100:
                sig += " " + lines[j].strip()
                j += 1
            if re.search(r"\b(forward|далее)\b", sig.split("//")[0], re.I):
                current = None
            else:
                name = m[2]
                kind = "Функция" if m[1].lower() in ("function","функция") else "Процедура"
                prefix = re.match(r"^(?:gl)?(?:[A-ZА-ЯЁ][a-zа-яё]+|[a-zа-яё]+)", name)
                current = dict(name=name, kind=kind, group=prefix[0] if prefix else name[:1], line=num,
                               signature=sig, description=docs.get("NAME","").strip(),
                               inputs=docs.get("IN","").strip(), outputs=docs.get("OUT","").strip(),
                               result=next((docs[k] for k in ("RETURN","RESULT","RET") if docs.get(k)),""),
                               returns=[])
                rows.append(current)
        elif current and current["kind"] == "Функция":
            r = re.match(r"^\s*(?:return|возврат)\b", clean, re.I)
            if r:
                end = clean.find(";", r.end())
                if end >= 0:
                    expr = line[r.end():end].strip()
                    if expr and expr not in current["returns"]:
                        current["returns"].append(expr)
        if re.match(r"^\s*(?:endfunction|endprocedure|конецфункции|конецпроцедуры)\b", clean, re.I):
            if current is not None:
                current["source"] = "\n".join(lines[current["line"]-1:num])
            current = None
        if clean.strip() or line.strip():
            comments.clear()
    return rows
