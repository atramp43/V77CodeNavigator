using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
        string exe=process.MainModule?.FileName??"Путь недоступен.";
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
            var labels=controls.Select(Native.Caption);
            var baseName=Native.Caption(controls.FirstOrDefault(h=>GetDlgCtrlID(h)==4159)).Trim();
            var basePath=Native.Caption(controls.FirstOrDefault(h=>GetDlgCtrlID(h)==4156)).Trim();
            if(basePath.Length!=0) TrackConfiguratorTitle(root,pid,basePath);
            var paths=labels.Where(label=>Regex.IsMatch(label,@"(?i)(?:[a-z]:[\\/]|\\\\[^\\]+\\)")).ToArray();
            string database=paths.Length==0
                ? "Путь к базе см. в открытом окне «О программе»."
                : string.Join(Environment.NewLine,paths);
            return $"Конфигуратор: {exe}{Environment.NewLine}База: {baseName}{Environment.NewLine}{database}";
        }
        return $"Конфигуратор: {exe}{Environment.NewLine}Не удалось прочитать окно «О программе».";
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
