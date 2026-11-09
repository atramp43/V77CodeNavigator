using System.Text.RegularExpressions;
using V77Navigator.Core;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    private string ShowMethodHelp(Spot origin,string source,bool complete)
    {
        IndexSignatures(origin,source);
        var lines=source.ReplaceLineEndings("\n").Split('\n');
        if(origin.Line>lines.Length) return "Текущая строка недоступна.";
        var line=lines[origin.Line-1];
        int column=origin.Column-1;
        if(column>line.Length) return "Поставьте курсор внутри имени или сразу после него.";
        if(!complete && column>0 && line[column-1]=='(') column--;
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
        if(!complete)
        {
            ShowSignature(origin,ModuleParser.Signature(source,methods[0]));
            return $"Параметры: {methods[0].Name}.";
        }

        using var form=new Form{Text="Дополнение имени",
            StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(640,300),MinimumSize=new Size(420,250),ShowInTaskbar=true,
            MinimizeBox=false,MaximizeBox=false,KeyPreview=true};
        var list=new ListBox{Dock=DockStyle.Fill};
        foreach(var method in methods) list.Items.Add($"{method.Name} · строка {method.Line}");
        var signature=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        var button=new Button{Text="Дополнить",AutoSize=true,Anchor=AnchorStyles.Right,Padding=new Padding(12,4,12,4),DialogResult=DialogResult.OK};
        list.SelectedIndexChanged+=(_,_)=>{
            var method=methods[list.SelectedIndex];
            signature.Text=ModuleParser.Signature(source,method).ReplaceLineEndings("\r\n");
            button.Enabled=!method.Name.Equals(word.Value,StringComparison.Ordinal);
        };
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=3};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,58));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(list,0,0);
        layout.Controls.Add(signature,0,1);
        layout.Controls.Add(button,0,2);
        form.Controls.Add(layout);
        form.AcceptButton=button;
        form.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;form.DialogResult=DialogResult.Cancel;}};
        list.SelectedIndex=0;
        var result=DialogSizes.Show(form,"Completion",new DialogOwner(origin.Root));
        if(result!=DialogResult.OK) return "Дополнение отменено.";

        CheckEditor(origin.Root,origin.Editor,origin.Pid);
        if(Coordinates(origin.Root)!=(origin.Line,origin.Column))
            return "Позиция каретки изменилась. Повторите дополнение.";

        var name=methods[list.SelectedIndex].Name;
        if(name.Equals(word.Value,StringComparison.Ordinal)) return "Имя уже введено полностью.";
        SendKeys.SendWait($"+{{LEFT {word.Length}}}");
        if(Coordinates(origin.Root)!=(origin.Line,origin.Column-word.Length))
            return "Не удалось выделить имя для замены.";
        PasteEditorText(origin,name);
        typedName.Clear();
        typedName.Append(name);
        return $"Дополнено имя {name}.";
    }
    private static void PasteEditorText(Spot origin,string text)
    {
        CheckEditor(origin.Root,origin.Editor,origin.Pid);
        var saved=Clipboard.GetDataObject();
        Clipboard.SetText(text);
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
    }
}
