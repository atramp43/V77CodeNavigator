"""External direct-call index for saved 1C 7.7 configuration modules."""
import re
import zlib
from method_descriptions import parse_methods
from collections import defaultdict

META = re.compile(r'"(?:[^"]|"")*"|[{}]')
LEX = re.compile(r'//[^\n]*|"(?:[^"]|"")*(?:"|$)|[\w]+|[^\s]')
START = {"function": "Функция", "функция": "Функция", "procedure": "Процедура", "процедура": "Процедура"}
END = {"endfunction", "endprocedure", "конецфункции", "конецпроцедуры"}

def metadata(md):
    text = md.openstream("Metadata/Main MetaData Stream").read()[7:].decode("cp1251")
    stack, root = [], []
    for m in META.finditer(text):
        t = m[0]
        if t == "{":
            node = []
            (stack[-1] if stack else root).append(node)
            stack.append(node)
        elif t == "}":
            stack.pop()
        else:
            stack[-1].append(t[1:-1].replace('""','"'))
    result = {}
    def visit(node, parents):
        if len(node)>1 and isinstance(node[0],str) and node[0].isdigit() and isinstance(node[1],str):
            parents = parents + [node[1]]
            result[node[0]] = parents
        for sub in node:
            if isinstance(sub,list):
                visit(sub,parents)
    visit(root,[])
    return result

def module_name(path, names):
    family = path[0]
    ident = path[1].rsplit("Number",1)[-1]
    parts = names.get(ident)
    if not parts:
        return "/".join(path[:-1])
    label = {"SubList":"Справочник", "SubFolder":"Справочник", "Subconto":"Справочник",
             "Journal":"Журнал", "Document":"Документ", "Report":"Отчёт", "CalcVar":"Обработка",
             "TypedText":"Документ"}.get(family,family)
    role = {"SubList":"ФормаСписка","SubFolder":"ФормаГруппы","Subconto":"ФормаЭлемента",
            "TypedText":"МодульПроведения"}.get(family,"")
    if family=="SubList" and len(parts)>1:
        parts=parts[:-1]+[role,parts[-1]]
    elif role:
        parts=parts+[role]
    return ".".join([label]+parts+["Модуль"])

def scan(text, names):
    lines=text.replace("\r\r\n","\n").replace("\r\n","\n").replace("\r","\n").split("\n")
    text="\n".join(lines)
    tokens=[]
    at,end=1,0
    for m in LEX.finditer(text):
        at+=text.count("\n",end,m.start())
        v=m[0]
        if not v.startswith("//"):
            tokens.append((v,at))
        at+=v.count("\n")
        end=m.end()
    local=set()
    for i,(word,_) in enumerate(tokens[:-2]):
        if word.casefold() in START and tokens[i+2][0]=="(":
            local.add(tokens[i+1][0].casefold())
    context="Код модуля"; i=0
    while i<len(tokens)-1:
        value,line=tokens[i]; key=value.casefold()
        if key in START and i+2<len(tokens) and tokens[i+2][0]=="(":
            context=START[key]+" "+tokens[i+1][0]
            i+=2; depth=1;i+=1
            while depth and i<len(tokens):
                depth+=(tokens[i][0]=="(")-(tokens[i][0]==")")
                i+=1
            if i<len(tokens) and tokens[i][0].casefold() in ("export","экспорт"):
                i+=1
            if i<len(tokens) and tokens[i][0].casefold() in ("forward","далее"):
                context="Код модуля";i+=1
            continue
        if key in END:
            context="Код модуля"
        if key in names and key not in local and tokens[i+1][0]=="(" and (i==0 or tokens[i-1][0]!="."):
            yield key,context,line,lines
        i+=1

def index(md, rows):
    names=metadata(md)
    exported=defaultdict(list)
    for r in rows:
        if re.search(r'\b(export|экспорт)\b',r["signature"].split("//")[0],re.I):
            exported[r["name"].casefold()].append(r)
    targets={k:v[0] for k,v in exported.items() if len(v)==1}
    modules=[]
    for path in md.listdir():
        if path[-1]!="MD Programm text" or path[1].startswith(("ModuleText_","UserHelp_")):
            continue
        raw=md.openstream(path).read()
        if not raw:
            continue
        text=zlib.decompress(raw,-15).decode("cp1251",errors="replace")
        module={"name":module_name(path,names),"sites":{}}
        mid=len(modules)
        modules.append(module)
        contexts=set()
        for key,ctx,line,lines in scan(text,targets):
            contexts.add(ctx)
            first=max(1,line-3)
            if line not in module["sites"]:
                module["sites"][line]=[first,lines[first-1:line+3]]
            targets[key].setdefault("external",[]).append([mid,ctx,line])
        module["methods"] = {r["kind"]+" "+r["name"]:r for r in parse_methods(text)
                             if r["kind"]+" "+r["name"] in contexts} if contexts else {}
    return modules
