namespace V77Navigator.Core;

public sealed record Declaration(string Name, string Kind, int Offset, int Line);

// Small lexical index, not a full 1C compiler. Offsets are UTF-16, lines are one-based.
public static class ModuleParser
{
    private sealed record Token(string Value, int Offset, int Line, bool First);

    public static IReadOnlyList<Declaration> Parse(string source)
    {
        var tokens = Scan(source).ToArray();
        var result = new List<Declaration>();
        for (int i = 0; i + 2 < tokens.Length; i++)
        {
            var t = tokens[i];
            if (!t.First || !IsKeyword(t.Value) || !IsStart(tokens[i + 1].Value[0]) || tokens[i + 2].Value != "(") continue;
            result.Add(new(tokens[i + 1].Value, t.Value, t.Offset, t.Line));
        }
        return result;
    }

    public static string Signature(string source, Declaration declaration)
    {
        var tokens = Scan(source[declaration.Offset..]).GetEnumerator();
        using (tokens)
        {
            int depth = 0;
            while (tokens.MoveNext())
            {
                var token = tokens.Current;
                if (token.Value == "(") depth++;
                if (token.Value == ")" && --depth == 0)
                    return source.Substring(declaration.Offset, token.Offset + 1);
            }
        }
        return declaration.Kind + " " + declaration.Name;
    }

    public static IReadOnlyList<Declaration> Find(IReadOnlyList<Declaration> declarations, string name) =>
        declarations.Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();

    public static (Declaration Declaration, int LastLine)? ContainingMethod(string source, int line)
    {
        var tokens = Scan(source).ToArray();
        Declaration? method = null;
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!token.First) continue;
            if (IsKeyword(token.Value))
            {
                method = token.Line <= line && i + 2 < tokens.Length &&
                    IsStart(tokens[i + 1].Value[0]) && tokens[i + 2].Value == "("
                    ? new(tokens[i + 1].Value, token.Value, token.Offset, token.Line)
                    : null;
            }
            else if (method != null && IsMethodEnd(token.Value, method.Kind))
            {
                if (token.Line >= line) return (method, token.Line);
                method = null;
            }
        }
        return null;
    }

    private static bool IsMethodEnd(string value, string kind) =>
        kind.Equals("Function", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("Функция", StringComparison.OrdinalIgnoreCase)
            ? value.Equals("EndFunction", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("КонецФункции", StringComparison.OrdinalIgnoreCase)
            : value.Equals("EndProcedure", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("КонецПроцедуры", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeyword(string s) => s.Equals("Процедура", StringComparison.OrdinalIgnoreCase)
        || s.Equals("Функция", StringComparison.OrdinalIgnoreCase)
        || s.Equals("Procedure", StringComparison.OrdinalIgnoreCase) || s.Equals("Function", StringComparison.OrdinalIgnoreCase);
    private static bool IsStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsPart(char c) => IsStart(c) || char.IsDigit(c);

    private static IEnumerable<Token> Scan(string s)
    {
        int i = 0, line = 1;
        bool first = true;
        while (i < s.Length)
        {
            char c = s[i];
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                i++; line++; first = true; continue;
            }
            if (char.IsWhiteSpace(c) || c == '\uFEFF') { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            { while (i < s.Length && s[i] is not ('\r' or '\n')) i++; continue; }
            int start = i, startLine = line;
            bool wasFirst = first;
            first = false;
            if (c == '"')
            {
                i++;
                while (i < s.Length)
                {
                    if (s[i] == '"')
                    {
                        i++;
                        if (i < s.Length && s[i] == '"') { i++; continue; }
                        break;
                    }
                    if (s[i] == '\n' || (s[i] == '\r' && (i + 1 == s.Length || s[i + 1] != '\n'))) line++;
                    i++;
                }
                yield return new("#string", start, startLine, wasFirst);
                continue;
            }
            i++;
            if (IsStart(c)) while (i < s.Length && IsPart(s[i])) i++;
            yield return new(s[start..i], start, startLine, wasFirst);
        }
    }
}
