using System.Runtime.InteropServices;
using System.Text;

namespace V77Navigator.WinApi;

public static class Native
{
    public delegate bool EnumProc(nint hwnd, nint state);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct GuiInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowUnicode(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetGUIThreadInfo(uint tid, ref GuiInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, nint state);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint hwnd, EnumProc callback, nint state);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hwnd, uint message, nint wp, nint lp, uint flags, uint timeout, out nint result);

    public static string ClassName(nint h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
    public static string Caption(nint h) { var b = new StringBuilder(4096); GetWindowText(h, b, b.Capacity); return b.ToString(); }
    public static string Hex(nint h) => $"0x{h:X}";
    public static bool Message(nint h, uint msg, nint wp, nint lp, out nint result) =>
        SendMessageTimeout(h, msg, wp, lp, 0x23, 300, out result) != 0;
    public static List<nint> Children(nint root, int maximum = 512)
    {
        var result = new List<nint>();
        EnumChildWindows(root, (h, _) => { result.Add(h); return result.Count < maximum; }, 0);
        return result;
    }
}
