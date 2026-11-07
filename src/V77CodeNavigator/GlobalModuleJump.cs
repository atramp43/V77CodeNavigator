using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using V77Navigator.Core;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

// Pilot for the observed Russian global-module window on platform build 7.70.027.
internal sealed partial class GlobalModuleJump : IDisposable
{
    private delegate nint Keyboard(int code, nint message, nint data);
    [DllImport("user32", SetLastError=true)] private static extern nint SetWindowsHookExW(int id, Keyboard callback, nint module, uint thread);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] private static extern nint GetWindow(nint h,int relation);
    [DllImport("user32")] private static extern bool IsWindowEnabled(nint h);
    [DllImport("user32")] private static extern int GetDlgCtrlID(nint h);
    [DllImport("user32")] private static extern bool PostMessageW(nint h,int msg,nint w,nint l);
    private readonly Control ui;
    private readonly Action<string> show;
    private readonly Keyboard callback;
    private nint hook;
    private readonly Bindings bindings;
    private int down;
    private bool busy;
    public GlobalModuleJump(Control ui, Bindings bindings, Action<string> show)
    {
        this.bindings=bindings; this.ui=ui; this.show=show; callback=OnKey;
        hook=SetWindowsHookExW(13,callback,GetModuleHandle(null),0);
        if(hook==0) throw new System.ComponentModel.Win32Exception();
    }
    public void Dispose(){if(hook!=0) UnhookWindowsHookEx(hook); hook=0;}

    private static bool Target(nint root,out nint focus,out uint pid)
    {
        uint thread=Native.GetWindowThreadProcessId(root,out pid);
        var gui=new Native.GuiInfo{Size=(uint)Marshal.SizeOf<Native.GuiInfo>()};
        focus=0;
        if(thread==0 || !Native.GetGUIThreadInfo(thread,ref gui)) return false;
        focus=gui.Focus;
        if(Native.ClassName(focus)!="Afx:400000:8" || Native.GetAncestor(focus,2)!=root) return false;
        if(!HasCommand(root,33298)) return false;
        using var process=Process.GetProcessById((int)pid);
        return SafetyPolicy.IsConfigurator(process.ProcessName,Native.Caption(root));
    }
    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(nint window,nint rectangle,nint region,uint flags);

    private static bool IsConfiguratorWindow(nint window)
    {
        if (Native.GetWindowThreadProcessId(window,out uint pid)==0) return false;
        using var process=Process.GetProcessById((int)pid);
        return SafetyPolicy.IsConfigurator(process.ProcessName,Native.Caption(window));
    }

    private nint OnKey(int code,nint msg,nint data)
    {
        try
        {
            if(code>=0)
            {
                int vk=Marshal.ReadInt32(data);
                if((msg==0x101 || msg==0x105) && vk==down){down=0;return 1;}
                if(msg==0x100 || msg==0x104)
                {
                    if(vk==down) return 1;
                    Keys chord=(Keys)vk;
                    if(GetAsyncKeyState(16)<0) chord|=Keys.Shift;
                    if(GetAsyncKeyState(17)<0) chord|=Keys.Control;
                    if(GetAsyncKeyState(18)<0) chord|=Keys.Alt;
                    if(!busy && bindings.Map.ContainsValue(chord))
                    {
                        var root=Native.GetForegroundWindow();
                        var action=bindings.Map.First(pair=>pair.Value==chord).Key;
                        if(action==NavAction.RefreshWindow && IsConfiguratorWindow(root))
                        {
                            down=vk;
                            ui.BeginInvoke(new Action(()=>{
                                if(Native.GetForegroundWindow()!=root || !IsConfiguratorWindow(root)) return;
                                const uint invalidate=1, erase=4, allChildren=0x80, frame=0x400;
                                show(RedrawWindow(root,0,0,invalidate|erase|allChildren|frame)
                                    ? "Запрошена перерисовка окна Конфигуратора."
                                    : "Не удалось запросить перерисовку.");
                            }));
                            return 1;
                        }
                        if(action!=NavAction.RefreshWindow && Target(root,out var editor,out var pid))
                        {
                            busy=true;down=vk;
                            ui.BeginInvoke(new Action(async ()=>{try{show(await Jump(root,editor,pid,action));}catch(Exception e){show(e.Message);}finally{busy=false;}}));
                            return 1;
                        }
                    }
                }
            }
        }
        catch(ArgumentException){} // Process exited during validation.
        return CallNextHookEx(0,code,msg,data);
    }
    private static bool Owned(nint h,nint root,uint pid,string title)
    {
        Native.GetWindowThreadProcessId(h,out uint actual);
        return actual==pid && GetWindow(h,4)==root && Native.ClassName(h)=="#32770" && Native.Caption(h)==title;
    }
    private static async Task<nint> Open(nint root,nint editor,uint pid,string title,int command)
    {
        if(Native.GetForegroundWindow()!=root || !Target(root,out var current,out var actual) || current!=editor || actual!=pid)
            throw new InvalidOperationException("Фокус редактора изменился перед открытием: "+title+"");
        if(!PostMessageW(root,273,command,0)) throw new InvalidOperationException("Команда меню недоступна.");
        for(int i=0;i<80;i++){await Task.Delay(50);var d=Native.GetForegroundWindow();if(Owned(d,root,pid,title) && Native.IsWindowVisible(d) && Native.Children(d).Count>1) {await Task.Delay(120); if(Owned(d,root,pid,title)) return d;}}
        throw new InvalidOperationException("Ожидание диалога «"+title+"» истекло.");
    }
    private static nint Read(nint h,uint msg,nint w,nint l)
    {
        if(!Native.Message(h,msg,w,l,out var result)) throw new InvalidOperationException("Окно перестало отвечать.");
        return result;
    }
    private async Task<string> Jump(nint root,nint editor,uint pid,NavAction action)
    {
        if(action==NavAction.Back || action==NavAction.Forward) return await NavigateHistory(root,editor,pid,action==NavAction.Back);
        if(action==NavAction.FilterMethods) return await FilterMethods(root,editor,pid);
        if(action is NavAction.MethodStart or NavAction.MethodEnd or NavAction.SelectMethod or NavAction.CopyLine or NavAction.SelectLine or NavAction.InspectMethod or NavAction.SelectAll)
            return await EditCommand(root,editor,pid,action);
        var origin=action==NavAction.Definition ? CaptureSpot(root,editor,pid) : null;
        int direct=action switch { NavAction.Methods=>33298, NavAction.Line=>33283, NavAction.Bookmark=>33290, NavAction.NextBookmark=>33291, _=>0 };
        if(direct!=0){if(Native.GetForegroundWindow()!=root || !Target(root,out var f,out _) || f!=editor || !HasCommand(root,direct) || !PostMessageW(root,273,direct,0)) throw new InvalidOperationException("Команда недоступна.");return "Команда передана Конфигуратору.";}
        nint d=await Open(root,editor,pid,"Поиск",57636);
        string word;
        try
        {
            var field=Native.Children(d).Single(h=>Native.ClassName(h)=="Edit");
            var buf=Marshal.AllocHGlobal(1024);
            try{int n=(int)Read(field,13,512,buf);word=Marshal.PtrToStringUni(buf,n)??"";}
            finally{Marshal.FreeHGlobal(buf);}
        }
        finally{if(Owned(d,root,pid,"Поиск")) PostMessageW(d,16,0,0);}
        if(!Regex.IsMatch(word,@"\A[\p{L}_][\p{L}\p{Nd}_]{0,509}\z")) return "Поместите курсор внутрь имени процедуры или функции.";
        for(int i=0;i<60 && Native.IsWindowVisible(d);i++) await Task.Delay(50);
        await Task.Delay(200);
        if(action is NavAction.Variable or NavAction.Occurrences) return await Search(root,editor,pid,word,action);
        var result=await Definition(root,editor,pid,word,true);
        if(result.StartsWith("Переход:") && origin!=null) Remember(origin);
        return result;
    }
    private static async Task<string> Definition(nint root,nint editor,uint pid,string word,bool fallback)
    {
        nint d=await Open(root,editor,pid,"Процедуры и функции модуля",33298);
        bool clicked=false;
        try
        {
            var controls=Native.Children(d);
            var list=controls.Single(h=>Native.ClassName(h)=="ListBox");
            var button=controls.Single(h=>Native.ClassName(h)=="Button" && Native.Caption(h)=="Перейти");
            var ptr=Marshal.StringToHGlobalUni(word);
            try
            {
                var index=Read(list,418,-1,ptr);
                if(index<0 && fallback && !IsGlobal(editor,root))
                {
                    PostMessageW(d,16,0,0);
                    for(int i=0;i<60 && Native.IsWindowVisible(d);i++) await Task.Delay(50);
                    await Task.Delay(200);
                    var global=await ActivateGlobal(root,editor,pid);
                    return await Definition(root,global,pid,word,false);
                }
                if(index<0) return "Объявление не найдено: "+word;
                if(Read(list,418,index,ptr)!=index) return "Несколько объявлений: "+word;
                if(Read(list,390,index,0)!=index) throw new InvalidOperationException("Ошибка выбора строки.");
                Read(d,273,65536+GetDlgCtrlID(list),list);
                if(Native.GetForegroundWindow()!=d || !Owned(d,root,pid,"Процедуры и функции модуля") || !IsWindowEnabled(button) || Read(list,392,0,0)!=index)
                    throw new InvalidOperationException("Переход недоступен.");
                clicked=PostMessageW(button,245,0,0);
            }
            finally{Marshal.FreeHGlobal(ptr);}
            for(int i=0;i<60 && Native.IsWindowVisible(d);i++) await Task.Delay(50);
            return clicked && !Native.IsWindowVisible(d) ? "Переход: "+word : "Диалог не подтвердил переход.";
        }
        finally{if(!clicked && Owned(d,root,pid,"Процедуры и функции модуля")) PostMessageW(d,16,0,0);}
    }
    [DllImport("user32")] private static extern nint GetMenu(nint hwnd);
    [DllImport("user32")] private static extern uint GetMenuState(nint menu,uint id,uint flags);
    private static bool HasCommand(nint root,uint command)=>GetMenuState(GetMenu(root),command,0)!=uint.MaxValue;
    private static bool HasCommand(nint root,int command)=>HasCommand(root,(uint)command);
    private static bool IsGlobal(nint editor,nint root)
    {
        for(var p=Native.GetParent(editor);p!=0 && p!=root;p=Native.GetParent(p))
            if(Native.Caption(p)=="Глобальный модуль") return true;
        return false;
    }
    private static async Task<nint> ActivateGlobal(nint root,nint editor,uint pid)
    {
        if(Native.GetForegroundWindow()!=root || !Target(root,out var current,out var owner) || current!=editor || owner!=pid)
            throw new InvalidOperationException("Редактор изменился.");
        var mdi=Native.Children(root).Single(h=>Native.ClassName(h)=="MDIClient");
        var windows=Native.Children(mdi).Where(h=>Native.GetParent(h)==mdi).ToArray();
        var global=windows.SingleOrDefault(h=>Native.Caption(h)=="Глобальный модуль");
        if(global!=0) Read(mdi,0x222,global,0);
        else
        {
            var config=windows.SingleOrDefault(h=>Native.Caption(h)=="Конфигурация");
            if(config!=0) Read(mdi,0x222,config,0);
            else if(HasCommand(root,33188)) PostMessageW(root,273,33188,0);
            for(int i=0;i<60 && !HasCommand(root,33272);i++) await Task.Delay(50);
            if(Native.GetForegroundWindow()!=root || !HasCommand(root,33272)) throw new InvalidOperationException("Команда открытия глобального модуля недоступна.");
            PostMessageW(root,273,33272,0);
        }
        for(int i=0;i<80;i++)
        {
            await Task.Delay(50);
            if(Native.GetForegroundWindow()==root && Target(root,out var focus,out owner) && owner==pid && IsGlobal(focus,root)) return focus;
        }
        throw new InvalidOperationException("Глобальный модуль не открылся.");
    }
    private static async Task<string> Search(nint root,nint editor,uint pid,string word,NavAction action)
    {
        var d=await Open(root,editor,pid,"Поиск во всех текстах",33207);
        var controls=Native.Children(d);
        var field=controls.First(h=>Native.ClassName(h)=="Edit" && Native.IsWindowVisible(h));
        var value=Marshal.StringToHGlobalUni(word);
        try{Read(field,12,0,value);}finally{Marshal.FreeHGlobal(value);}
        foreach(var h in controls.Where(h=>Native.ClassName(h)=="Button"))
        {
            string label=Native.Caption(h).Replace("&","");
            bool? wanted=label switch {"Модулях" or "Искать целые слова"=>true,"Учитывать регистр" or "Формулах таблиц" or "Формулах диалогов" or "Описаниях" or "Текстах таблиц"=>false,_=>null};
            if(wanted.HasValue && (Read(h,240,0,0)!=0)!=wanted.Value) Read(h,245,0,0);
        }
        var go=controls.Single(h=>Native.ClassName(h)=="Button" && Native.Caption(h).Replace("&","")=="Искать...");
        if(!Owned(d,root,pid,"Поиск во всех текстах") || Native.GetForegroundWindow()!=d || !IsWindowEnabled(go)) throw new InvalidOperationException("Проверьте параметры штатного поиска.");
        PostMessageW(go,245,0,0);
        if(action==NavAction.Occurrences) return "Дважды щёлкните строку в результатах поиска: "+word;
        return await PickVariable(root,pid,word);
    }

    private static string ListText(nint list,int index)
    {
        int len=(int)Read(list,394,index,0);
        if(len<0 || len>65535) return "";
        var ptr=Marshal.AllocHGlobal((len+1)*2);
        try{return Marshal.PtrToStringUni(ptr,(int)Read(list,393,index,ptr))??"";}
        finally{Marshal.FreeHGlobal(ptr);}
    }

    private sealed record Hit(int Index,string Row)
    {
        public override string ToString()=>Row;
    }
    private static async Task<string> PickVariable(nint root,uint pid,string word)
    {
        nint list=0;
        int count=0;
        for(int attempt=0;attempt<200;attempt++)
        {
            await Task.Delay(100);
            foreach(var h in Native.Children(root).Where(h=>Native.ClassName(h)=="ListBox" && Native.IsWindowVisible(h)))
            {
                int n=(int)Read(h,395,0,0);
                if(n>=2 && ListText(h,0).Contains("\""+word+"\"",StringComparison.OrdinalIgnoreCase) &&
                   ListText(h,n-1).StartsWith("Всего найдено",StringComparison.OrdinalIgnoreCase))
                { list=h;count=n;break; }
            }
            if(list!=0) break;
        }
        if(list==0) return "Поиск ещё не завершён или список результатов недоступен.";
        var hits=new List<Hit>();
        for(int i=1;i<count-1;i++)
        {
            string row=ListText(list,i);
            int start=row.IndexOf(") :",StringComparison.Ordinal);
            if(start<0) continue;
            if(VariableDeclarations.Matches(row[(start+3)..],word)) hits.Add(new(i,row));
        }
        if(hits.Count==0) return "Явное объявление или параметр не найдено: "+word;
        using var form=new Form{Text="Объявления: "+word,Size=new Size(900,370),StartPosition=FormStartPosition.CenterScreen};
        var items=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        items.Items.AddRange(hits.ToArray());items.SelectedIndex=0;
        var choose=new Button{Text="Перейти",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};
        UiArt.Button(choose,"check2");form.Controls.Add(items);form.Controls.Add(choose);form.AcceptButton=choose;
        items.DoubleClick+=(_,_)=>form.DialogResult=DialogResult.OK;
        if(form.ShowDialog()!=DialogResult.OK || items.SelectedItem is not Hit selected) return "Выбор отменён.";
        Native.GetWindowThreadProcessId(list,out uint owner);
        if(owner!=pid || Native.GetAncestor(list,2)!=root || ListText(list,selected.Index)!=selected.Row)
            return "Результаты поиска изменились. Повторите поиск.";
        if(Read(list,390,selected.Index,0)!=selected.Index) return "Строка недоступна.";
        var parent=Native.GetParent(list);
        Read(parent,273,(1<<16)+GetDlgCtrlID(list),list);
        PostMessageW(parent,273,(2<<16)+GetDlgCtrlID(list),list);
        return "Выбрано объявление: "+word;
    }
}


