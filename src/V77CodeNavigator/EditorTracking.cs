using System.Text;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    internal const string ActivePrefix="[Навигатор: активен] ";
    private System.Windows.Forms.Timer? trackingTimer;
    private nint trackedFrame;
    private uint trackedPid;
    private nint trackedEditor;

    private void StartTracking(Action<bool> report)
    {
        trackingTimer=new System.Windows.Forms.Timer{Interval=350};
        trackingTimer.Tick+=(_,_)=>{
            if(busy) return;
            try
            {
                var root=Native.GetForegroundWindow();
                bool active=Target(root,out var editor,out uint pid);
                if(trackedFrame!=0 && (!active || root!=trackedFrame)) ClearTrackingTitle();
                if(active)
                {
                    if(trackedFrame!=root)
                    {
                        trackedFrame=root;
                        trackedPid=pid;
                        bindings.NativeShortcuts=ReadNativeShortcuts(GetMenu(root));
                    }
                    bool editorChanged=trackedEditor!=editor;
                    trackedEditor=editor;
                    string caption=Native.Caption(root);
                    if(!caption.StartsWith(ActivePrefix,StringComparison.Ordinal))
                        SetConfiguratorCaption(root,ActivePrefix+BaseCaption(caption)+(configuratorTitles.TryGetValue(root,out var info)?info.Suffix:""));
                    if(editorChanged) RepaintEditorToolbar(root);
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
        trackedEditor=0;
    }

    private static bool RepaintEditorToolbar(nint root)
    {
        const int editorToolbarId=59404;
        const uint invalidate=1, erase=4, allChildren=0x80, frame=0x400;
        var toolbar=Native.Children(root).FirstOrDefault(window=>
            GetDlgCtrlID(window)==editorToolbarId && Native.IsWindowVisible(window));
        return toolbar!=0 && RedrawWindow(toolbar,0,0,invalidate|erase|allChildren|frame);
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
