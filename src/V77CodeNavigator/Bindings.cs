using System.Text.Json;

namespace V77CodeNavigator;

internal enum NavAction { Definition, Variable, Occurrences, Methods, Line, Bookmark, NextBookmark, Back, Forward, FilterMethods, MethodStart, SelectMethod, CopyLine, SelectLine, InspectMethod, RefreshWindow, SelectAll, MethodEnd, SignatureHelp, CompleteName }

internal sealed class Bindings
{
    internal Dictionary<NavAction,Keys> Map { get; private set; } = new()
    {
        [NavAction.Definition]=Keys.F12, [NavAction.Variable]=Keys.Shift|Keys.F12,
        [NavAction.Occurrences]=Keys.Control|Keys.Shift|Keys.F7, [NavAction.Methods]=Keys.Control|Keys.Shift|Keys.O,
        [NavAction.Line]=Keys.Control|Keys.L, [NavAction.Bookmark]=Keys.Alt|Keys.F2, [NavAction.NextBookmark]=Keys.F2,
        [NavAction.Back]=Keys.Alt|Keys.Left, [NavAction.Forward]=Keys.Alt|Keys.Right, [NavAction.FilterMethods]=Keys.F8,
        [NavAction.MethodStart]=Keys.Control|Keys.Alt|Keys.M,
        [NavAction.SelectMethod]=Keys.Control|Keys.Alt|Keys.S,
        [NavAction.CopyLine]=Keys.Control|Keys.Alt|Keys.C,
        [NavAction.SelectLine]=Keys.Control|Keys.Alt|Keys.L,
        [NavAction.InspectMethod]=Keys.Control|Keys.Alt|Keys.I,
        [NavAction.RefreshWindow]=Keys.Control|Keys.Alt|Keys.F5,
        [NavAction.SelectAll]=Keys.Control|Keys.Shift|Keys.A,
        [NavAction.MethodEnd]=Keys.Control|Keys.Alt|Keys.Shift|Keys.M,
        [NavAction.SignatureHelp]=Keys.Control|Keys.Shift|Keys.P,
        [NavAction.CompleteName]=Keys.Control|Keys.Space
    };
    private static readonly string[] Labels={"Определение процедуры / функции","Объявление переменной","Строки с вхождениями слова","Список процедур и функций","Перейти к строке","Поставить / снять закладку","Следующая закладка","Назад по переходам","Вперёд по переходам","Методы с фильтром","В начало текущего метода","Выделить текущий метод","Копировать текущую строку","Выделить текущую строку","Сведения о текущем методе","Перерисовать окно Конфигуратора","Выделить весь текст модуля","В конец текущего метода","Параметры функции или процедуры","Дополнить имя функции или процедуры"};
    private static string FileName => Path.Combine(Program.ProjectRoot(),"hotkeys.json");
    internal Bindings(bool useSaved=true)
    {
        if(!useSaved || !File.Exists(FileName)) return;
        var loaded=JsonSerializer.Deserialize<Dictionary<NavAction,Keys>>(File.ReadAllText(FileName));
        if(loaded==null || !loaded.Keys.All(Map.ContainsKey) || loaded.Values.Where(k=>k!=Keys.None).Distinct().Count()!=loaded.Values.Count(k=>k!=Keys.None)) throw new InvalidDataException("Некорректный файл hotkeys.json");
        foreach(var action in Map.Keys.Except(loaded.Keys).ToArray())
            loaded[action]=loaded.ContainsValue(Map[action])?Keys.None:Map[action];
        Map=loaded;
    }
    internal void Apply(Dictionary<NavAction,Keys> values)
    {
        if(values.Keys.Any(k=>!Map.ContainsKey(k)) || values.Values.Any(k=>k!=Keys.None && !Supported(k)) || values.Values.Where(k=>k!=Keys.None).GroupBy(k=>k).Any(g=>g.Count()>1))
            throw new InvalidDataException("Некорректные или повторяющиеся сочетания клавиш.");
        Map=new(values);
        foreach(var (action,key) in new Bindings(useSaved:false).Map)
            Map.TryAdd(action,Map.ContainsValue(key)?Keys.None:key);
    }
    private static bool Supported(Keys k)
    {
        Keys code=k & Keys.KeyCode;
        return (k & ~(Keys.KeyCode|Keys.Control|Keys.Alt|Keys.Shift))==0 && (code is >=Keys.F1 and <=Keys.F24 || ((k & (Keys.Control|Keys.Alt))!=0 && code is >=Keys.A and <=Keys.Z or Keys.Left or Keys.Right or Keys.Space));
    }
    internal void Edit(Form owner)
    {
        using var form=new Form{Text="Горячие клавиши",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
        var grid=new TableLayoutPanel{AutoSize=true,Padding=new Padding(12),ColumnCount=2};form.Controls.Add(grid);
        grid.Controls.Add(new Label{Text="Нажмите сочетание в поле. Backspace отключает команду.",AutoSize=true},0,0);grid.SetColumnSpan(grid.Controls[0],2);
        var next=new Dictionary<NavAction,Keys>(Map);
        foreach(var action in Map.Keys)
        {
            int row=(int)action+1;
            grid.Controls.Add(new Label{Text=Labels[(int)action],AutoSize=true},0,row);
            var box=new TextBox{ReadOnly=true,Width=180,Text=Format(next[action])};
            box.KeyDown+=(_,e)=>{
                e.SuppressKeyPress=true;
                if(e.KeyCode==Keys.Back) next[action]=Keys.None;
                else if(e.KeyCode is >=Keys.F1 and <=Keys.F24 || ((e.Control||e.Alt) && (e.KeyCode is >=Keys.A and <=Keys.Z or Keys.Left or Keys.Right or Keys.Space))) next[action]=e.KeyData;
                box.Text=Format(next[action]);
            };
            grid.Controls.Add(box,1,row);
        }
        var save=new Button{Text="Сохранить",AutoSize=true};UiArt.Button(save,"check2");grid.Controls.Add(save,1,Map.Count+1);
        save.Click+=(_,_)=>{
            if(next.Values.Where(k=>k!=Keys.None).GroupBy(k=>k).Any(g=>g.Count()>1)){MessageBox.Show(form,"Одно сочетание назначено нескольким командам.");return;}
            try{File.WriteAllText(FileName+".tmp",JsonSerializer.Serialize(next));File.Move(FileName+".tmp",FileName,true);Map=next;form.Close();}
            catch(IOException ex){MessageBox.Show(form,ex.Message);}
        };
        form.ShowDialog(owner);
    }
    private static string Format(Keys k)=>k==Keys.None?"Отключено":new KeysConverter().ConvertToString(k)??"";
}



