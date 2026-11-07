using System.Diagnostics;
using System.Runtime.InteropServices;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    private async Task<string> ShowConfiguratorInfo(nint root)
    {
        if(Native.GetForegroundWindow()!=root || !IsConfiguratorWindow(root))
            return "Окно Конфигуратора потеряло фокус.";
        Native.GetWindowThreadProcessId(root,out uint pid);
        using var process=Process.GetProcessById((int)pid);
        string exe=process.MainModule?.FileName??"";
        var menu=GetMenu(root);
        uint command=FindMenuCommand(menu,"О программе...","О программе…","О программе","About...","About…","About");
        if(command==0 || (GetMenuState(menu,command,0)&3)!=0 || !PostMessageW(root,273,(nint)command,0))
            return $"Конфигуратор: {exe}{Environment.NewLine}Пункт «О программе» недоступен.";

        for(int i=0;i<30;i++)
        {
            await Task.Delay(50);
            var dialog=Native.GetForegroundWindow();
            if(!Owned(dialog,root,pid,"О программе") && !Owned(dialog,root,pid,"About")) continue;
            var controls=Native.Children(dialog).Where(Native.IsWindowVisible).ToArray();
            string Value(int id)=>Native.Caption(controls.FirstOrDefault(h=>GetDlgCtrlID(h)==id)).Trim();
            string baseName=Value(4159);
            string basePath=Value(4156);
            string user=Value(4120);
            if(basePath.Length!=0) TrackConfiguratorTitle(root,pid,basePath);
            if(!PostMessageW(dialog,16,0,0)) return "Не удалось закрыть окно «О программе».";
            for(int wait=0;wait<30 && Native.IsWindowVisible(dialog);wait++) await Task.Delay(50);
            if(Native.IsWindowVisible(dialog)) return "Закройте окно «О программе» и повторите команду.";
            ShowPathsDialog(root,baseName,exe,basePath,user);
            return "Окно сведений закрыто.";
        }
        return $"Конфигуратор: {exe}{Environment.NewLine}Не удалось прочитать окно «О программе».";
    }

    private static void ShowPathsDialog(nint root,string baseName,string exe,string basePath,string user)
    {
        using var dialog=new Form{Text=baseName.Length==0?"Сведения о Конфигураторе":$"Сведения о Конфигураторе — {baseName}",
            ClientSize=new Size(760,220),MinimumSize=new Size(480,240),StartPosition=FormStartPosition.CenterParent,
            MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=true,KeyPreview=true};
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=3,RowCount=5};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var status=new Label{AutoSize=true,Anchor=AnchorStyles.Left};
        var fields=new (string Label,string Value)[]{("Путь к 1cv7.exe",exe),("Путь к базе",basePath),("Пользователь 1С",user)};
        var buttons=new Button[fields.Length];
        for(int row=0;row<fields.Length;row++)
        {
            var (label,value)=fields[row];
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,row);
            grid.Controls.Add(new TextBox{Text=value,PlaceholderText="Недоступно",ReadOnly=true,
                Dock=DockStyle.Fill,AccessibleName=label},1,row);
            var copy=new Button{Text=$"Копировать (&{row+1})",AutoSize=true,Enabled=value.Length!=0,
                AccessibleName=$"Копировать: {label}"};
            copy.Click+=(_,_)=>{
                try{Clipboard.SetText(value);status.Text=$"Скопировано: {label}";}
                catch(ExternalException){status.Text="Буфер обмена занят. Повторите копирование.";}
            };
            buttons[row]=copy;
            grid.Controls.Add(copy,2,row);
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        grid.Controls.Add(status,0,3);
        grid.SetColumnSpan(status,2);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var close=new Button{Text="Закрыть",AutoSize=true,Anchor=AnchorStyles.Right,DialogResult=DialogResult.Cancel};
        grid.Controls.Add(close,2,4);
        dialog.Controls.Add(grid);
        dialog.CancelButton=close;
        dialog.KeyDown+=(_,e)=>{
            if(e.Alt && e.KeyCode is >=Keys.D1 and <=Keys.D3)
            {
                buttons[e.KeyCode-Keys.D1].PerformClick();
                e.SuppressKeyPress=true;
            }
        };
        dialog.ShowDialog(new DialogOwner(root));
    }

    private const string TitleMarker=" | База: ";
    private sealed record TitleDetails(uint Pid,string Suffix);
    private readonly Dictionary<nint,TitleDetails> configuratorTitles=new();
    private System.Windows.Forms.Timer? titleTimer;

    private void TrackConfiguratorTitle(nint window,uint pid,string basePath)
    {
        if(configuratorTitles.TryGetValue(window,out var previous) && previous.Pid==pid)
            RemoveTitleSuffix(window,previous);
        configuratorTitles[window]=new(pid,$"{TitleMarker}{basePath}");
        SetConfiguratorCaption(window,BaseCaption(Native.Caption(window))+configuratorTitles[window].Suffix);
        if(titleTimer!=null) return;
        titleTimer=new System.Windows.Forms.Timer{Interval=750};
        titleTimer.Tick+=(_,_)=>RefreshConfiguratorTitles();
        titleTimer.Start();
    }

    private void RefreshConfiguratorTitles()
    {
        foreach(var (window,details) in configuratorTitles.ToArray())
        {
            Native.GetWindowThreadProcessId(window,out uint actual);
            if(actual!=details.Pid)
            {
                configuratorTitles.Remove(window);
                continue;
            }
            string caption=Native.Caption(window);
            if(caption.Length!=0 && !caption.Contains(TitleMarker,StringComparison.Ordinal))
                SetConfiguratorCaption(window,BaseCaption(caption)+details.Suffix);
        }
    }

    private static void RemoveTitleSuffix(nint window,TitleDetails details)
    {
        Native.GetWindowThreadProcessId(window,out uint actual);
        if(actual!=details.Pid) return;
        string caption=Native.Caption(window);
        if(caption.Contains(TitleMarker,StringComparison.Ordinal))
            SetConfiguratorCaption(window,BaseCaption(caption));
    }

    private static string BaseCaption(string caption)
    {
        int marker=caption.IndexOf(TitleMarker,StringComparison.Ordinal);
        if(marker>=0) caption=caption[..marker];
        // MFC appends the active document itself when the frame caption is set.
        int document=caption.LastIndexOf(" - [",StringComparison.Ordinal);
        return document>=0 && caption.EndsWith(']')?caption[..document]:caption;
    }

    private static void SetConfiguratorCaption(nint window,string caption)
    {
        var text=Marshal.StringToHGlobalUni(caption);
        try{Native.Message(window,12,0,text,out _);}
        finally{Marshal.FreeHGlobal(text);}
    }

    private void RestoreConfiguratorTitles()
    {
        titleTimer?.Dispose();
        titleTimer=null;
        foreach(var (window,details) in configuratorTitles)
            RemoveTitleSuffix(window,details);
        configuratorTitles.Clear();
    }
}
