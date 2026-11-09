using System.Text.RegularExpressions;
using V77Navigator.Core;

namespace V77CodeNavigator;

internal static class VariableDeclarations
{
    internal static int FindInScope(string source,string word,int? caretLine)
    {
        var lines=source.ReplaceLineEndings("\n").Split('\n');
        var method=caretLine.HasValue ? ModuleParser.ContainingMethod(source,caretLine.Value) : null;
        if(method is { } scope)
        {
            for(int line=scope.Declaration.Line;line<=scope.LastLine;line++)
                if(Matches(lines[line-1],word)) return line;
        }
        for(int line=1;line<=lines.Length;line++)
        {
            if(!IsVariableDeclaration(lines[line-1]) || !Matches(lines[line-1],word)) continue;
            if(ModuleParser.ContainingMethod(source,line)==null) return line;
        }
        return 0;
    }

    private static bool IsVariableDeclaration(string source)=>
        Regex.IsMatch(source,@"^\s*(?:Перем|Var)\s+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

    // One source line per native search hit. Match declarations and formal arguments,
    // excluding comments, strings, array bounds and parameter default expressions.
    internal static bool Matches(string source,string word)
    {
        string code=Regex.Replace(source,@"""(?:""""|[^""])*(?:""|$)|//.*"," ");
        var options=RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;
        var variables=Regex.Match(code,@"^\s*(?:Перем|Var)\s+([^;]+)",options);
        var parameters=Regex.Match(code,@"^\s*(?:Функция|Процедура|Function|Procedure)\s+[\w]+\s*\(([^)]*)",options);
        var declaration=variables.Success?variables:parameters;
        if(!declaration.Success) return false;
        foreach(string part in declaration.Groups[1].Value.Split(','))
        {
            var name=Regex.Match(part,@"^\s*(?:(?:Знач|Val)\s+)?([\p{L}_][\w]*)",options);
            if(name.Success && string.Equals(name.Groups[1].Value,word,StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
