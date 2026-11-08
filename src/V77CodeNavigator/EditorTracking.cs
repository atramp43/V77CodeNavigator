using System.Text;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    internal const string ActivePrefix="[Навигатор: активен] ";
    private System.Windows.Forms.Timer? trackingTimer;
    private nint trackedFrame;
    private uint trackedPid;

    private void StartTracking(Action<bool> report)
    {
        trackingTimer=new System.Windows.Forms.Timer{Interval=350};
        trackingTimer.Tick+=(_,_)=>{
            if(busy) return;
            try
            {
                var root=Native.GetForegroundWindow();
                bool active=Target(root,out _,out uint pid);
                if(trackedFrame!=0 && (!active || root!=trackedFrame)) ClearTrackingTitle();
                if(active)
                {
                    if(trackedFrame!=root)
                    {
                        trackedFrame=root;
                        trackedPid=pid;
                        bindings.NativeShortcuts=ReadNativeShortcuts(GetMenu(root));
                    }
                    string caption=Native.Caption(root);
                    if(!caption.StartsWith(ActivePrefix,StringComparison.Ordinal))
                        SetConfiguratorCaption(root,ActivePrefix+BaseCaption(caption)+(configuratorTitles.TryGetValue(root,out var info)?info.Suffix:""));
                }
                report(active);
            }
            catch(ArgumentException){ClearTrackingTitle();report(false);}
        };
        trackingTimer.Start();
    }

    private void StopTracking()
    {
        trackingTimer?.Dispose();
        trackingTimer=null;
        ClearTrackingTitle();
    }

    private void ClearTrackingTitle()
    {
        if(trackedFrame==0) return;
        Native.GetWindowThreadProcessId(trackedFrame,out uint pid);
        if(pid==trackedPid)
        {
            string caption=Native.Caption(trackedFrame);
            if(caption.StartsWith(ActivePrefix,StringComparison.Ordinal))
                SetConfiguratorCaption(trackedFrame,BaseCaption(caption[ActivePrefix.Length..])+
                    (configuratorTitles.TryGetValue(trackedFrame,out var info)?info.Suffix:""));
        }
        trackedFrame=0;
    }

    private bool HasNativeShortcut(nint root,Keys chord)
    {
        // F10 is explicitly assigned to navigation inside the module editor.
        if((chord&Keys.KeyCode)==Keys.F10) return false;
        var shortcuts=ReadNativeShortcuts(GetMenu(root));
        if(shortcuts.Count>0) bindings.NativeShortcuts=shortcuts;
        return shortcuts.ContainsKey(chord);
    }

    private static Dictionary<Keys,string> ReadNativeShortcuts(nint menu)
    {
        var shortcuts=new Dictionary<Keys,string>();
        var converter=new KeysConverter();
        Collect(menu);
        return shortcuts;

        void Collect(nint current)
        {
            var caption=new StringBuilder(256);
            for(int i=0;i<GetMenuItemCount(current);i++)
            {
                var child=GetSubMenu(current,i);
                if(child!=0){Collect(child);continue;}
                caption.Clear();
                GetMenuString(current,(uint)i,caption,caption.Capacity,0x400);
                var parts=caption.ToString().Split('\t',2);
                if(parts.Length!=2) continue;
                try
                {
                    if(converter.ConvertFromInvariantString(parts[1].Trim().Replace('-','+')) is Keys key)
                        shortcuts.TryAdd(key,parts[0].Replace("&",""));
                }
                catch(ArgumentException){}
                catch(FormatException){}
                catch(NotSupportedException){}
            }
        }
    }
}
