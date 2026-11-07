using System.Runtime.InteropServices;
using System.Windows.Automation;
using Accessibility;

namespace V77Navigator.Diagnostics;

public static class AccessibilityProbe
{
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);

    public static void Read(nint hwnd, uint expectedPid, AccessibilityReport result, bool content)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var nodes = new List<Dictionary<string, object?>>();
            result.Properties["UIA"] = nodes;
            // Include windowless providers; bounded traversal, with process-wide timeout in controller.
            var pending = new Queue<(AutomationElement Element, int Depth)>();
            pending.Enqueue((root, 0));
            while (pending.Count > 0 && nodes.Count < 64)
            {
                var (element, depth) = pending.Dequeue();
                if (element.Current.ProcessId != expectedPid) continue;
                nodes.Add(ReadElement(element, depth, content));
                if (depth >= 4) continue;
                var child = TreeWalker.RawViewWalker.GetFirstChild(element);
                int siblings = 0;
                while (child is not null && siblings++ < 64 && pending.Count + nodes.Count < 128)
                {
                    pending.Enqueue((child, depth + 1));
                    child = TreeWalker.RawViewWalker.GetNextSibling(child);
                }
            }
            result.Properties["UIATraversalBounded"] = true;
            result.Properties["UIANodeLimitReached"] = pending.Count > 0;
        }
        catch (Exception ex) { result.Properties["UIAError"] = Reports.SafeError(ex); }

        IAccessible? accessible = null;
        try
        {
            var iid = typeof(IAccessible).GUID;
            int hr = AccessibleObjectFromWindow(hwnd, unchecked((uint)-4), ref iid, out accessible);
            result.Properties["MSAAHResult"] = $"0x{hr:X8}";
            if (hr >= 0 && accessible is not null)
            {
                result.Properties["MSAARole"] = accessible.get_accRole(0);
                result.Properties["MSAAState"] = accessible.get_accState(0);
                result.Properties["MSAAChildCount"] = accessible.accChildCount;
                result.Properties["MSAAFocusType"] = accessible.accFocus?.GetType().Name;
                result.Properties["MSAASelectionType"] = accessible.accSelection?.GetType().Name;
                // Names/values can contain source code. Read only on explicit opt-in.
                if (content)
                {
                    result.Properties["MSAAName"] = accessible.get_accName(0);
                    result.Properties["MSAAValue"] = accessible.get_accValue(0);
                }
            }
        }
        catch (Exception ex) { result.Properties["MSAAError"] = Reports.SafeError(ex); }
        finally { if (accessible is not null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
        result.Status = "Complete (individual provider failures, if any, recorded in properties)";
    }

    private static Dictionary<string, object?> ReadElement(AutomationElement e, int depth, bool content)
    {
        var p = new Dictionary<string, object?>();
        try
        {
            var c = e.Current;
            p["Depth"] = depth; p["Class"] = c.ClassName; p["ControlType"] = c.ControlType.ProgrammaticName;
            p["NativeWindowHandle"] = $"0x{c.NativeWindowHandle:X}";
            p["ProcessId"] = c.ProcessId; p["FrameworkId"] = c.FrameworkId;
            p["HasKeyboardFocus"] = c.HasKeyboardFocus; p["IsPassword"] = c.IsPassword;
            var patterns = e.GetSupportedPatterns();
            p["Patterns"] = patterns.Select(x => new { x.Id, x.ProgrammaticName }).ToArray();
            p["TextPattern2Available"] = patterns.Any(x => x.Id == 10024);
            p["TextPattern2Probe"] = "Availability only; managed TextPattern read below; GetCaretRange not invoked.";
            if (c.IsPassword) { p["Content"] = "Skipped password provider"; return p; }
            if (content) p["Name"] = c.Name;
            if (e.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var t = (TextPattern)tp;
                p["SupportedTextSelection"] = t.SupportedTextSelection.ToString();
                var ranges = t.GetSelection();
                p["SelectedRangeCount"] = ranges.Length;
                p["SelectionsCollapsed"] = ranges.Select(r => r.CompareEndpoints(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start, r,
                    System.Windows.Automation.Text.TextPatternRangeEndpoint.End) == 0).ToArray();
                var text = t.DocumentRange.GetText(1_000_001);
                p["DocumentSampleChars"] = text.Length;
                p["DocumentSampleMayBeTruncated"] = text.Length >= 1_000_001;
                if (content) p["DocumentText"] = text;
                p["Movement"] = "TextPatternRange.Select API exists; compatibility not tested; no Select call made.";
            }
            if (e.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                var v = ((ValuePattern)vp).Current;
                p["ValueIsReadOnly"] = v.IsReadOnly;
                // No length-limited getter exists, so avoid fetching potentially large Value by default.
                if (content) p["Value"] = v.Value;
            }
        }
        catch (Exception ex) { p["Error"] = Reports.SafeError(ex); }
        return p;
    }
}
