using System.Diagnostics;
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
            var labels=Native.Children(dialog).Where(Native.IsWindowVisible).Select(Native.Caption).ToArray();
            var paths=labels.Where(label=>Regex.IsMatch(label,@"(?i)(?:[a-z]:[\\/]|\\\\[^\\]+\\)")).ToArray();
            string database=paths.Length==0
                ? "Путь к базе см. в открытом окне «О программе»."
                : string.Join(Environment.NewLine,paths);
            return $"Конфигуратор: {exe}{Environment.NewLine}{database}";
        }
        return $"Конфигуратор: {exe}{Environment.NewLine}Не удалось прочитать окно «О программе».";
    }
}
