using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Accessibility;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    private sealed record Spot(uint Pid,nint Root,nint Mdi,nint Document,nint Editor,string Title,int Line,int Column);
    private readonly List<Spot> back=new(), forward=new();
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint h,uint id,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IAccessible? view);
    private static (int Line,int Column)? Coordinates(nint root)
    {
        var bar=Native.Children(root).FirstOrDefault(h=>Native.ClassName(h)=="msctls_statusbar32" && Native.IsWindowVisible(h));
        if(bar==0) return null;
        var iid=typeof(IAccessible).GUID;
        if(AccessibleObjectFromWindow(bar,0xfffffffc,ref iid,out var view)!=0 || view==null) return null;
        try
        {
            for(int i=1;i<=view.accChildCount;i++)
            {
                var match=Regex.Match(view.get_accName(i)??"",@"^\s*(\d+)\s*,\s*(\d+)\s*$");
                if(match.Success && int.TryParse(match.Groups[1].Value,out int line) && int.TryParse(match.Groups[2].Value,out int col) && line>0 && col>0) return (line,col);
            }
            return null;
        }
        finally{if(Marshal.IsComObject(view)) Marshal.ReleaseComObject(view);}
    }
    private static Spot? CaptureSpot(nint root,nint editor,uint pid)
    {
        try
        {
            var pos=Coordinates(root);
            if(pos==null) return null;
            for(var h=editor;h!=0 && h!=root;h=Native.GetParent(h))
            {
                var parent=Native.GetParent(h);
                if(Native.ClassName(parent)=="MDIClient") return new(pid,root,parent,h,editor,Native.Caption(h),pos.Value.Line,pos.Value.Column);
            }
        }
        catch(COMException){}
        return null;
    }
    private void Remember(Spot spot)
    {
        if(back.LastOrDefault()!=spot) back.Add(spot);
        if(back.Count>100) back.RemoveAt(0);
        forward.Clear();
    }
    private async Task<string> NavigateHistory(nint root,nint editor,uint pid,bool reverse)
    {
        var from=reverse?back:forward;
        var to=reverse?forward:back;
        int index=from.FindLastIndex(s=>s.Root==root && s.Pid==pid);
        if(index<0) return "История переходов пуста.";
        var spot=from[index];
        var current=CaptureSpot(root,editor,pid);
        if(current==null) return "Координаты каретки недоступны.";
        if(!Native.IsWindow(spot.Editor) || Native.GetAncestor(spot.Editor,2)!=root || Native.GetParent(spot.Document)!=spot.Mdi || Native.Caption(spot.Document)!=spot.Title)
        {from.RemoveAt(index);return "Сохранённое окно закрыто или изменилось.";}
        Read(spot.Mdi,0x222,spot.Document,0);
        await Task.Delay(120);
        await GoToLine(root,spot.Editor,pid,spot.Line);
        for(int i=0;i<4096;i++)
        {
            if(Native.GetForegroundWindow()!=root || !Target(root,out var focus,out _) || focus!=spot.Editor) break;
            var pos=Coordinates(root);
            if(pos==null || pos.Value.Line!=spot.Line) break;
            if(pos.Value.Column==spot.Column)
            {
                from.RemoveAt(index);to.Add(current);
                return $"Переход: {spot.Title}, строка {spot.Line}, столбец {spot.Column}";
            }
            int direction=pos.Value.Column<spot.Column?39:37;
            Read(focus,256,direction,1);Read(focus,257,direction,unchecked((int)0xc0000001));
            for(int wait=0;wait<12 && Coordinates(root)==pos;wait++) await Task.Delay(25);
            if(Coordinates(root)==pos) break;
            if(i%32==31) await Task.Yield();
        }
        return $"Ожидалось {spot.Line}, {spot.Column}. Позиция редактора: {Coordinates(root)}.";
    }

    private static async Task GoToLine(nint root,nint editor,uint pid,int line)
    {
        nint d=await Open(root,editor,pid,"Переход к строке",33283);
        try
        {
            var field=Native.Children(d).Single(h=>Native.ClassName(h)=="Edit" && GetDlgCtrlID(h)==5892);
            var button=Native.Children(d).Single(h=>Native.ClassName(h)=="Button" && GetDlgCtrlID(h)==1);
            string number=line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var buffer=Marshal.StringToHGlobalUni(number);
            try
            {
                if(Read(field,12,0,buffer)==0)
                    throw new InvalidOperationException("Не удалось ввести номер строки.");
                int count=(int)Read(field,13,number.Length+1,buffer);
                if(Marshal.PtrToStringUni(buffer,count)!=number || Read(field,14,0,0)!=number.Length)
                    throw new InvalidOperationException("Номер строки в диалоге не совпал с ожидаемым.");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            if(!Owned(d,root,pid,"Переход к строке") || Native.GetForegroundWindow()!=d)
                throw new InvalidOperationException("Диалог перехода потерял фокус.");
            if(!PostMessageW(button,245,0,0))
                throw new InvalidOperationException("Не удалось нажать «Перейти».");
            for(int i=0;i<60 && Native.IsWindowVisible(d);i++) await Task.Delay(50);
        }
        finally{if(Owned(d,root,pid,"Переход к строке") && Native.IsWindowVisible(d)) PostMessageW(d,16,0,0);}
        await Task.Delay(150);
    }

    private sealed class DialogOwner(nint handle) : IWin32Window { public nint Handle=>handle; }
    private async Task<string> FilterMethods(nint root,nint editor,uint pid)
    {
        var origin=CaptureSpot(root,editor,pid);
        nint dialog=await Open(root,editor,pid,"Процедуры и функции модуля",33298);
        bool clicked=false;
        try
        {
            var controls=Native.Children(dialog);
            var list=controls.Single(h=>Native.ClassName(h)=="ListBox");
            var go=controls.Single(h=>Native.ClassName(h)=="Button" && Native.Caption(h)=="Перейти");
            int count=(int)Read(list,395,0,0);
            var rows=new List<Hit>();
            for(int i=0;i<count;i++) rows.Add(new(i,ListText(list,i)));
            using var form=new Form{Text="Методы модуля",Size=new Size(820,760),StartPosition=FormStartPosition.CenterParent};
            var query=new TextBox{Dock=DockStyle.Top,PlaceholderText="Часть имени процедуры или функции"};
            var matches=new ListBox{Dock=DockStyle.Fill};
            var choose=new Button{Text="Перейти",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};
            UiArt.Button(choose,"check2");form.Controls.Add(matches);form.Controls.Add(query);form.Controls.Add(choose);
            form.AcceptButton=choose;form.ActiveControl=query;
            void RefreshList()
            {
                matches.BeginUpdate();matches.Items.Clear();
                matches.Items.AddRange(rows.Where(r=>r.Row.Contains(query.Text,StringComparison.OrdinalIgnoreCase)).ToArray());
                if(matches.Items.Count>0) matches.SelectedIndex=0;
                choose.Enabled=matches.Items.Count>0;matches.EndUpdate();
            }
            query.TextChanged+=(_,_)=>RefreshList();
            query.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Down && matches.Items.Count>0){matches.Focus();e.SuppressKeyPress=true;}};
            matches.DoubleClick+=(_,_)=>{if(matches.SelectedItem!=null) form.DialogResult=DialogResult.OK;};
            form.KeyPreview=true;form.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape) form.DialogResult=DialogResult.Cancel;};
            RefreshList();
            if(DialogSizes.Show(form,"ModuleMethods",new DialogOwner(dialog))!=DialogResult.OK ||
                matches.SelectedItem is not Hit hit) return "Выбор отменён.";
            if(!Owned(dialog,root,pid,"Процедуры и функции модуля") || ListText(list,hit.Index)!=hit.Row || Read(list,390,hit.Index,0)!=hit.Index) return "Список изменился.";
            Read(dialog,273,65536+GetDlgCtrlID(list),list);
            clicked=IsWindowEnabled(go) && PostMessageW(go,245,0,0);
            for(int i=0;i<60 && Native.IsWindowVisible(dialog);i++) await Task.Delay(50);
            if(!clicked || Native.IsWindowVisible(dialog)) return "Переход не подтверждён.";
            if(origin!=null) Remember(origin);
            return "Переход: "+hit.Row;
        }
        finally{if(!clicked && Owned(dialog,root,pid,"Процедуры и функции модуля")) PostMessageW(dialog,16,0,0);}
    }
}


