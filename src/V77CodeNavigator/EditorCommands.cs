using System.Runtime.InteropServices;
using V77Navigator.Core;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static void CheckEditor(nint root, nint editor, uint pid)
    {
        if (Native.GetForegroundWindow()!=root || !Target(root,out var focus,out var actual) || focus!=editor || actual!=pid)
            throw new InvalidOperationException("Фокус редактора изменился.");
    }

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")]
    private static extern nint GetSubMenu(nint menu,int index);
    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(nint menu,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetMenuString(nint menu,uint index,System.Text.StringBuilder text,int capacity,uint flags);

    private static uint FindMenuCommand(nint menu,params string[] captions)
    {
        const uint byPosition=0x400;
        var label=new System.Text.StringBuilder(128);
        for(int i=0;i<GetMenuItemCount(menu);i++)
        {
            var child=GetSubMenu(menu,i);
            if(child!=0)
            {
                var command=FindMenuCommand(child,captions);
                if(command!=0) return command;
                continue;
            }
            label.Clear();
            GetMenuString(menu,(uint)i,label,label.Capacity,byPosition);
            var caption=label.ToString().Split('\t')[0].Replace("&","").Replace('ё','е').Trim();
            if(captions.Contains(caption,StringComparer.OrdinalIgnoreCase))
                return GetMenuItemID(menu,i);
        }
        return 0;
    }

    private static void SelectAll(Spot origin)
    {
        CheckEditor(origin.Root,origin.Editor,origin.Pid);
        var menu=GetMenu(origin.Root);
        var command=FindMenuCommand(menu,"Выделить все","Select All");
        if(command==0 || (GetMenuState(menu,command,0)&3)!=0)
            throw new InvalidOperationException("Пункт «Выделить все» недоступен.");
        if(!PostMessageW(origin.Root,273,(nint)command,0))
            throw new InvalidOperationException("Не удалось отправить команду «Выделить все».");
    }

    private static async Task<string> ReadModuleViaClipboard(Spot origin)
    {
        var saved = Clipboard.GetDataObject();
        uint before = GetClipboardSequenceNumber();
        uint copied = before;
        try
        {
            CheckEditor(origin.Root,origin.Editor,origin.Pid);
            SelectAll(origin);
            CheckEditor(origin.Root,origin.Editor,origin.Pid);
            if(!PostMessageW(origin.Root,273,57634,0))
                throw new InvalidOperationException("Не удалось отправить команду копирования.");
            for(int i=0;i<200 && GetClipboardSequenceNumber()==before;i++) await Task.Delay(25);
            copied=GetClipboardSequenceNumber();
            if(copied==before || !Clipboard.ContainsText())
                throw new InvalidOperationException("Редактор не передал текст модуля в буфер обмена.");
            return Clipboard.GetText().Replace("\r\r\n","\r\n");
        }
        finally
        {
            try
            {
                if(copied!=before && GetClipboardSequenceNumber()==copied)
                {
                    if(saved==null) Clipboard.Clear();
                    else Clipboard.SetDataObject(saved,true);
                }
            }
            finally
            {
                await PlaceCaret(origin,origin.Line);
                if(origin.Column>1) SendKeys.SendWait($"{{RIGHT {origin.Column-1}}}");
            }
        }
    }

    private static async Task PlaceCaret(Spot origin,int line)
    {
        await GoToLine(origin.Root,origin.Editor,origin.Pid,line);
        for(int i=0;i<30;i++)
        {
            if(Native.GetForegroundWindow()==origin.Root &&
                Target(origin.Root,out var focus,out var pid) && focus==origin.Editor && pid==origin.Pid &&
                Coordinates(origin.Root)?.Line==line)
                break;
            await Task.Delay(50);
        }
        CheckEditor(origin.Root,origin.Editor,origin.Pid);
        if(Coordinates(origin.Root)?.Line!=line)
            throw new InvalidOperationException($"Редактор не перешёл к строке {line}.");
        Read(origin.Editor,256,(nint)Keys.Home,1);
        Read(origin.Editor,257,(nint)Keys.Home,unchecked((int)0xc0000001));
    }

    private async Task<string> EditCommand(nint root,nint editor,uint pid,NavAction action)
    {
        // SendKeys must run after the user releases the shortcut modifiers.
        for(int i=0;i<100 && (down!=0 || GetAsyncKeyState(16)<0 || GetAsyncKeyState(17)<0 || GetAsyncKeyState(18)<0);i++)
            await Task.Delay(25);
        if(down!=0 || GetAsyncKeyState(16)<0 || GetAsyncKeyState(17)<0 || GetAsyncKeyState(18)<0)
            return "Отпустите клавиши и повторите команду.";
        CheckEditor(root,editor,pid);
        var origin=CaptureSpot(root,editor,pid);
        if(origin==null) return "Координаты каретки недоступны.";
        if(action==NavAction.SelectAll)
        {
            SelectAll(origin);
            return "Выделен весь текст модуля.";
        }
        if(action==NavAction.SelectLine)
        {
            SendKeys.SendWait("{HOME}{HOME}+{END}");
            return $"Выделена строка {origin.Line}.";
        }

        var source=await ReadModuleViaClipboard(origin);
        if(action is NavAction.SignatureHelp or NavAction.CompleteName)
            return ShowMethodHelp(origin,source,action==NavAction.CompleteName);
        if(action==NavAction.CopyLine)
        {
            var lines=source.ReplaceLineEndings("\n").Split('\n');
            if(origin.Line>lines.Length) return "Текущая строка недоступна.";
            var line=lines[origin.Line-1];
            Clipboard.SetText(line+"\r\n");
            return $"Скопирована строка {origin.Line}.";
        }

        var method=ModuleParser.ContainingMethod(source,origin.Line);
        if(method==null)
        {
            if(action is not (NavAction.MethodStart or NavAction.MethodEnd))
                return "Каретка находится вне завершённой процедуры или функции.";
            int adjacent=origin.Line+(action==NavAction.MethodStart?-1:1);
            if(adjacent<1 || adjacent>source.ReplaceLineEndings("\n").Count(c=>c=='\n')+1)
                return "Граница метода не найдена. Достигнута граница модуля.";
            await PlaceCaret(origin,adjacent);
            Remember(origin);
            return $"Граница метода не найдена. Переход на строку {adjacent}.";
        }
        var (declaration,lastLine)=method.Value;
        if(action==NavAction.InspectMethod)
            return $"{declaration.Kind} {declaration.Name}, строки {declaration.Line}–{lastLine} ({lastLine-declaration.Line+1}).";

        int targetLine=action==NavAction.MethodEnd?lastLine:declaration.Line;
        await PlaceCaret(origin,targetLine);
        if(action==NavAction.SelectMethod)
        {
            if(lastLine>declaration.Line) SendKeys.SendWait($"+{{DOWN {lastLine-declaration.Line}}}");
            SendKeys.SendWait("+{END}");
            return $"Выделен метод {declaration.Name}.";
        }

        Remember(origin);
        return $"Переход: {declaration.Name}, строка {targetLine}.";
    }
}
