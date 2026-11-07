using System.Diagnostics;
using System.Runtime.InteropServices;
using V77Navigator.Core;
using V77Navigator.WinApi;

namespace V77Navigator.Diagnostics;

public static class Collector
{
    public static DiagnosticReport Capture(string path, bool includeContent)
    {
        var report = new DiagnosticReport { IncludeContent = includeContent };
        Reports.Save(path, report);
        try
        {
            var foreground = Native.GetForegroundWindow();
            var root = Native.GetAncestor(foreground, 2);
            report.Foreground = Native.Hex(foreground);
            report.Root = Native.Hex(root);
            if (foreground == 0 || root == 0)
            {
                report.Status = "Rejected: no foreground window available. Run from the same interactive desktop as Configurator (not a restricted/background desktop). No editor probes sent.";
                Reports.Save(path, report);
                return report;
            }
            uint tid = Native.GetWindowThreadProcessId(root, out uint pid);
            // Do not report metadata/text from unrelated applications.
            using var process = Process.GetProcessById((int)pid);
            if (!SafetyPolicy.IsConfigurator(process.ProcessName, Native.Caption(root)))
            {
                report.Status = "Rejected: foreground is not an identified 1cv7 Configurator. No editor probes sent.";
                Reports.Save(path, report);
                return report;
            }
            report.ProcessId = (int)pid;
            try { report.FileVersion = process.MainModule?.FileVersionInfo.FileVersion; }
            catch (Exception e) { report.Error = "Version: " + Reports.SafeError(e); }
            var gui = new Native.GuiInfo { Size = (uint)Marshal.SizeOf<Native.GuiInfo>() };
            if (Native.GetGUIThreadInfo(tid, ref gui))
            {
                report.Focus = Native.Hex(gui.Focus); report.Active = Native.Hex(gui.Active);
                report.CaretWindow = Native.Hex(gui.Caret);
                report.CaretRectangle = new[] { gui.CaretRect.Left, gui.CaretRect.Top, gui.CaretRect.Right, gui.CaretRect.Bottom };
            }
            else report.GuiError = $"GetGUIThreadInfo Win32={Marshal.GetLastWin32Error()}";
            var children = Native.Children(root);
            report.WindowLimitReached = children.Count >= 512;
            foreach (var h in new[] { root }.Concat(children))
            {
                uint wt = Native.GetWindowThreadProcessId(h, out uint wp);
                if (wp != pid || !Native.IsWindow(h)) continue;
                var w = new WindowReport
                {
                    Hwnd = Native.Hex(h), Parent = Native.Hex(Native.GetParent(h)), Pid = wp, Tid = wt,
                    Class = Native.ClassName(h), Caption = includeContent ? Native.Caption(h) : null,
                    Visible = Native.IsWindowVisible(h), Unicode = Native.IsWindowUnicode(h),
                    Style = $"0x{Native.GetWindowLong(h, -16):X8}"
                };
                report.Windows.Add(w);
                if (w.Class == "MDIClient" && Native.Message(h, 0x229, 0, 0, out var mdi)) report.ActiveMdiChild = Native.Hex(mdi);
                // Inspect standard controls only; never assume an arbitrary Afx/custom window is EDIT.
                // Probe only the focused control or its standard-edit ancestors, avoiding unrelated dialogs.
                if (h == gui.Focus && SafetyPolicy.IsStandardEdit(w.Class)) w.ReadProbe = ProbeStandardEdit(h, includeContent);
                else w.ReadProbe["Status"] = "Not probed: not a focused, exact known EDIT/RichEdit class.";
            }
            report.Status = "NativeComplete; AccessibilityPending";
            Reports.Save(path, report);
            foreach (var h in new[] { gui.Focus, ParseHandle(report.ActiveMdiChild), root }.Where(h => h != 0).Distinct())
            {
                Native.GetWindowThreadProcessId(h, out uint currentPid);
                if (!Native.IsWindow(h) || currentPid != pid) continue;
                var a = new AccessibilityReport { Target = Native.Hex(h), Status = "InProgress" };
                report.Accessibility.Add(a);
                Reports.Save(path, report);
                AccessibilityProbe.Read(h, pid, a, includeContent);
                Reports.Save(path, report);
            }
            report.Status = "Complete (read-only; editor compatibility and navigation unconfirmed)";
        }
        catch (Exception ex) { report.Status = "Failed"; report.Error = Reports.SafeError(ex); }
        Reports.Save(path, report);
        return report;
    }

    private static nint ParseHandle(string? value) => value is null ? 0 : (nint)Convert.ToInt64(value[2..], 16);

    public static Dictionary<string, object?> ProbeStandardEdit(nint h, bool content)
    {
        var p = new Dictionary<string, object?>();
        if (!Native.IsWindow(h) || !SafetyPolicy.IsStandardEdit(Native.ClassName(h)))
        { p["Status"] = "Skipped unknown or destroyed window"; return p; }
        if ((Native.GetWindowLong(h, -16) & 0x20) != 0) { p["Status"] = "Skipped password style"; return p; }
        p["Status"] = "Read-only standard control probe; no movement attempted";
        if (Native.Message(h, 0xE, 0, 0, out var length))
        {
            p["WM_GETTEXTLENGTH"] = (long)length;
            int count = (int)Math.Clamp((long)length + 1, 1, 1_000_001);
            var buffer = Marshal.AllocHGlobal(count * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                if (Native.Message(h, 0xD, count, buffer, out var copied))
                {
                    int size = (int)Math.Clamp((long)copied, 0, count - 1);
                    p["WM_GETTEXTChars"] = size;
                    p["TextTruncated"] = (long)length >= count;
                    if (content) p["Text"] = Marshal.PtrToStringUni(buffer, size);
                }
                else p["WM_GETTEXTError"] = Marshal.GetLastWin32Error();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        else p["WM_GETTEXTLENGTHError"] = Marshal.GetLastWin32Error();
        var start = Marshal.AllocHGlobal(4); var end = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(start, -1); Marshal.WriteInt32(end, -1);
            if (Native.Message(h, 0xB0, start, end, out _))
            {
                int s = Marshal.ReadInt32(start), e = Marshal.ReadInt32(end);
                p["SelectionStart"] = s; p["SelectionEnd"] = e;
                p["CaretOffset"] = s == e && s >= 0 ? s : null;
                // Selection start does not reveal the caret end for nonempty selections.
                if (s >= 0 && Native.Message(h, 0xC9, s, 0, out var line)) p["SelectionStartDisplayLineZeroBased"] = (long)line;
            }
            else p["EM_GETSELError"] = Marshal.GetLastWin32Error();
        }
        finally { Marshal.FreeHGlobal(start); Marshal.FreeHGlobal(end); }
        return p;
    }
}
