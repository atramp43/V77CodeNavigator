using System.Text.RegularExpressions;
using V77Navigator.Core;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    private static string ShowMethodHelp(Spot origin,string source,bool complete)
    {
        var lines=source.ReplaceLineEndings("\n").Split('\n');
        if(origin.Line>lines.Length) return "Текущая строка недоступна.";
        var line=lines[origin.Line-1];
        int column=origin.Column-1;
        if(column>line.Length) return "Поставьте курсор внутри имени или сразу после него.";
        var word=Regex.Matches(line,@"[\p{L}_][\p{L}\p{Nd}_]*").Cast<Match>()
            .FirstOrDefault(m=>m.Index<=column && column<=m.Index+m.Length);
        if(word==null) return "Поставьте курсор внутри имени или сразу после него.";
        if(complete && column!=word.Index+word.Length)
            return "Для дополнения поставьте курсор сразу после набранной части имени.";

        var methods=ModuleParser.Parse(source)
            .Where(m=>complete ? m.Name.StartsWith(word.Value,StringComparison.OrdinalIgnoreCase)
                               : m.Name.Equals(word.Value,StringComparison.OrdinalIgnoreCase))
            .OrderBy(m=>m.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        if(methods.Length==0) return "В текущем модуле подходящих объявлений нет.";

        using var form=new Form{Text=complete?"Дополнение имени":"Параметры метода",
            StartPosition=FormStartPosition.CenterParent,Width=660,Height=320,ShowInTaskbar=false,
            MinimizeBox=false,MaximizeBox=false,KeyPreview=true};
        var list=new ListBox{Dock=DockStyle.Top,Height=100};
        foreach(var method in methods) list.Items.Add($"{method.Name} · строка {method.Line}");
        var signature=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        var button=new Button{Text=complete?"Дополнить":"Закрыть",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};
        list.SelectedIndexChanged+=(_,_)=>{
            var method=methods[list.SelectedIndex];
            signature.Text=ModuleParser.Signature(source,method).ReplaceLineEndings("\r\n");
            button.Enabled=!complete || method.Name.Length>word.Length;
        };
        form.Controls.Add(signature);
        form.Controls.Add(list);
        form.Controls.Add(button);
        form.AcceptButton=button;
        form.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;form.DialogResult=DialogResult.Cancel;}};
        list.SelectedIndex=0;
        var result=form.ShowDialog(new DialogOwner(origin.Root));
        if(!complete || result!=DialogResult.OK) return "Подсказка закрыта.";

        CheckEditor(origin.Root,origin.Editor,origin.Pid);
        if(Coordinates(origin.Root)!=(origin.Line,origin.Column))
            return "Позиция каретки изменилась. Повторите дополнение.";

        var suffix=methods[list.SelectedIndex].Name[word.Length..];
        if(suffix.Length==0) return "Имя уже введено полностью.";
        var saved=Clipboard.GetDataObject();
        Clipboard.SetText(suffix);
        uint staged=GetClipboardSequenceNumber();
        try{Read(origin.Root,273,57637,0);}
        finally
        {
            if(GetClipboardSequenceNumber()==staged)
            {
                if(saved==null) Clipboard.Clear();
                else Clipboard.SetDataObject(saved,true);
            }
        }
        return $"Дополнено имя {methods[list.SelectedIndex].Name}.";
    }
}
