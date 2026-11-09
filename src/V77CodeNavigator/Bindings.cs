using System.Text.Json;

namespace V77CodeNavigator;

internal enum NavAction { Definition, Variable, Occurrences, Methods, Line, Bookmark, NextBookmark, Back, Forward, FilterMethods, MethodStart, SelectMethod, CopyLine, SelectLine, InspectMethod, RefreshWindow, SelectAll, MethodEnd, SignatureHelp, CompleteName, ConfiguratorInfo, DuplicateLine, SelectDocumentedMethod }

internal sealed class Bindings
{
    internal Dictionary<NavAction,Keys> Map { get; private set; } = new()
    {
        [NavAction.Definition]=Keys.F10, [NavAction.Variable]=Keys.Shift|Keys.F10,
        [NavAction.Occurrences]=Keys.Control|Keys.Shift|Keys.F7, [NavAction.Methods]=Keys.Control|Keys.Shift|Keys.O,
        [NavAction.Line]=Keys.Control|Keys.Alt|Keys.G, [NavAction.Bookmark]=Keys.Control|Keys.Alt|Keys.B, [NavAction.NextBookmark]=Keys.Control|Keys.Alt|Keys.N,
        [NavAction.Back]=Keys.Alt|Keys.Left, [NavAction.Forward]=Keys.Alt|Keys.Right, [NavAction.FilterMethods]=Keys.Control|Keys.Alt|Keys.O,
        [NavAction.MethodStart]=Keys.Control|Keys.Alt|Keys.M,
        [NavAction.SelectMethod]=Keys.Control|Keys.Alt|Keys.S,
        [NavAction.CopyLine]=Keys.Control|Keys.Alt|Keys.C,
        [NavAction.SelectLine]=Keys.Control|Keys.Alt|Keys.L,
        [NavAction.InspectMethod]=Keys.Control|Keys.Alt|Keys.I,
        [NavAction.RefreshWindow]=Keys.Control|Keys.Alt|Keys.F5,
        [NavAction.SelectAll]=Keys.Control|Keys.Shift|Keys.A,
        [NavAction.MethodEnd]=Keys.Control|Keys.Alt|Keys.Shift|Keys.M,
        [NavAction.SignatureHelp]=Keys.Control|Keys.Shift|Keys.P,
        [NavAction.CompleteName]=Keys.Control|Keys.Space,
        [NavAction.ConfiguratorInfo]=Keys.Control|Keys.Alt|Keys.F6,
        [NavAction.DuplicateLine]=Keys.Control|Keys.Alt|Keys.D,
        [NavAction.SelectDocumentedMethod]=Keys.Control|Keys.Alt|Keys.Shift|Keys.S
    };
    private static readonly string[] Labels={"Определение процедуры / функции","Объявление переменной","Строки с вхождениями слова","Список процедур и функций","Перейти к строке","Поставить / снять закладку","Следующая закладка","Назад по переходам","Вперёд по переходам","Методы с фильтром","В начало текущего метода","Выделить текущий метод","Копировать текущую строку","Выделить текущую строку","Сведения о текущем методе","Перерисовать окно Конфигуратора","Выделить весь текст модуля","В конец текущего метода","Параметры функции или процедуры","Дополнить имя функции или процедуры","Пути Конфигуратора и текущей базы","Дублировать текущую строку ниже","Выделить текущий метод с описанием"};
    private static string FileName => Path.Combine(Program.ProjectRoot(),"hotkeys.json");
    internal Bindings(bool useSaved=true)
    {
        if(!useSaved || !File.Exists(FileName)) return;
        var loaded=JsonSerializer.Deserialize<Dictionary<NavAction,Keys>>(File.ReadAllText(FileName));
        if(loaded!=null) Apply(loaded);
    }

    internal Dictionary<Keys,string> NativeShortcuts { get; set; } = new();
    internal static readonly Dictionary<Keys,string> Reserved = new()
    {
        [Keys.F1]="Помощь", [Keys.F11]="Запустить 1С:Предприятие", [Keys.F12]="Отладчик",
        [Keys.Control|Keys.A]="Вернуть", [Keys.Control|Keys.Z]="Отменить",
        [Keys.Control|Keys.C]="Копировать", [Keys.Control|Keys.X]="Вырезать",
        [Keys.Control|Keys.V]="Вставить", [Keys.Control|Keys.S]="Сохранить",
        [Keys.Control|Keys.F]="Поиск", [Keys.Control|Keys.N]="Создать",
        [Keys.Control|Keys.O]="Открыть", [Keys.Control|Keys.P]="Печать",
        [Keys.F3]="Найти далее", [Keys.Shift|Keys.F3]="Найти ранее",
        [Keys.Control|Keys.Shift|Keys.F3]="Заменить", [Keys.Alt|Keys.F4]="Закрыть окно",
        [Keys.Control|Keys.F6]="Следующее окно", [Keys.Alt|Keys.Enter]="Свойства"
    };

    internal static bool IsReserved(Keys key) =>
        (key&Keys.KeyCode) is Keys.F11 or Keys.F12 || Reserved.ContainsKey(key);

    internal void Apply(Dictionary<NavAction,Keys> values)
    {
        var defaults=new Bindings(useSaved:false).Map;
        if(values.Keys.Any(k=>!defaults.ContainsKey(k)) || values.Values.Any(k=>k!=Keys.None && !Supported(k)))
            throw new InvalidDataException("Некорректные сочетания клавиш.");
        var updated=new Dictionary<NavAction,Keys>();
        foreach(var (action,key) in values)
        {
            Keys chosen=key;
            if((key&Keys.KeyCode)==Keys.F12) chosen=(key&Keys.Modifiers)|Keys.F10;
            if((action==NavAction.Line && key==(Keys.Control|Keys.L)) ||
                (action==NavAction.Bookmark && key==(Keys.Alt|Keys.F2)) ||
                (action==NavAction.NextBookmark && key==Keys.F2) ||
                (action==NavAction.FilterMethods && key==Keys.F8))
                chosen=defaults[action];
            updated[action]=IsReserved(chosen) || (chosen!=Keys.None && updated.ContainsValue(chosen))
                ? Keys.None : chosen;
        }
        foreach(var (action,key) in defaults)
            updated.TryAdd(action,updated.ContainsValue(key)?Keys.None:key);
        Map=updated;
    }
    private static bool Supported(Keys k)
    {
        Keys code=k & Keys.KeyCode;
        return (k & ~(Keys.KeyCode|Keys.Control|Keys.Alt|Keys.Shift))==0 && (code is >=Keys.F1 and <=Keys.F24 || ((k & (Keys.Control|Keys.Alt))!=0 && code is >=Keys.A and <=Keys.Z or Keys.Left or Keys.Right or Keys.Space));
    }
    internal Size DialogSize { get; set; }

    internal void Edit(Form owner)
    {
        using var form=new Form{Text="Справка и настройка горячих клавиш",ClientSize=new Size(680,580),
            MinimumSize=new Size(500,360),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false};
        var tabs=new TabControl{Dock=DockStyle.Fill};
        var navigator=new TabPage("Навигатор");
        var native=new TabPage("Конфигуратор 1С 7.7");
        tabs.TabPages.AddRange(new[]{navigator,native});
        form.Controls.Add(tabs);
        var grid=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(12),ColumnCount=2};
        var commands=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        commands.Controls.Add(grid);
        navigator.Controls.Add(commands);
        var nativeList=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        foreach(var item in Reserved.Concat(NativeShortcuts).GroupBy(p=>p.Key).Select(g=>g.Last()).OrderBy(p=>Format(p.Key)))
            nativeList.Items.Add($"{Format(item.Key)}   {item.Value}");
        native.Controls.Add(nativeList);
        native.Controls.Add(new Label{Dock=DockStyle.Top,Height=48,
            Text="Известные сочетания и команды последнего активного меню 1С. F10 в редакторе назначена Навигатору."});
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
                else if(Supported(e.KeyData))
                {
                    if(IsReserved(e.KeyData) || ((e.KeyData&Keys.KeyCode)!=Keys.F10 && NativeShortcuts.ContainsKey(e.KeyData)))
                    {
                        MessageBox.Show(form,"Это сочетание используется Конфигуратором.");
                        return;
                    }
                    next[action]=e.KeyData;
                }
                box.Text=Format(next[action]);
            };
            grid.Controls.Add(box,1,row);
        }
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,
            FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
        var save=new Button{Text="Сохранить",AutoSize=true};
        UiArt.Button(save,"check2");footer.Controls.Add(save);
        navigator.Controls.Add(footer);
        commands.BringToFront();
        save.Click+=(_,_)=>{
            if(next.Values.Where(k=>k!=Keys.None).GroupBy(k=>k).Any(g=>g.Count()>1)){MessageBox.Show(form,"Одно сочетание назначено нескольким командам.");return;}
            try{File.WriteAllText(FileName+".tmp",JsonSerializer.Serialize(next));File.Move(FileName+".tmp",FileName,true);Map=next;form.Close();}
            catch(IOException ex){MessageBox.Show(form,ex.Message);}
        };
        form.PerformLayout();
        var preferred=new Size(820,grid.GetPreferredSize(Size.Empty).Height+
            footer.GetPreferredSize(Size.Empty).Height+100);
        DialogSizes.Fit(form,DialogSize.IsEmpty?preferred:DialogSize,Screen.FromControl(owner).WorkingArea.Size);
        form.ShowDialog(owner);
        DialogSize=form.WindowState==FormWindowState.Normal ? form.Size : form.RestoreBounds.Size;
    }
    private static string Format(Keys k)=>k==Keys.None?"Отключено":new KeysConverter().ConvertToString(k)??"";
}



