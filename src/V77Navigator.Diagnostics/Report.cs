using System.Text.Json;
using System.IO;
using System.Text.Encodings.Web;

namespace V77Navigator.Diagnostics;

public sealed class DiagnosticReport
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string OS { get; set; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    public string Architecture { get; set; } = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
    public string Runtime { get; set; } = Environment.Version.ToString();
    public bool IncludeContent { get; set; }
    public string Status { get; set; } = "Starting";
    public string? Error { get; set; }
    public string Foreground { get; set; } = "0x0";
    public string Root { get; set; } = "0x0";
    public int ProcessId { get; set; }
    public string? FileVersion { get; set; }
    public string? GuiError { get; set; }
    public string Focus { get; set; } = "0x0";
    public string Active { get; set; } = "0x0";
    public string CaretWindow { get; set; } = "0x0";
    public int[]? CaretRectangle { get; set; }
    public string? ActiveMdiChild { get; set; }
    public string EditorIdentification { get; set; } = "Unknown: focused HWND and MDI child are candidates, not a confirmed module editor.";
    public string CursorMovement { get; set; } = "Not tested: this diagnostic never moves the caret or selection.";
    public bool WindowLimitReached { get; set; }
    public List<WindowReport> Windows { get; set; } = new();
    public List<AccessibilityReport> Accessibility { get; set; } = new();
}

public sealed class WindowReport
{
    public string Hwnd { get; set; } = "";
    public string Parent { get; set; } = "";
    public uint Pid { get; set; }
    public uint Tid { get; set; }
    public string Class { get; set; } = "";
    public string? Caption { get; set; }
    public bool Visible { get; set; }
    public bool Unicode { get; set; }
    public string Style { get; set; } = "";
    public Dictionary<string, object?> ReadProbe { get; set; } = new();
}

public sealed class AccessibilityReport
{
    public string Target { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public static class Reports
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static void Save(string path, DiagnosticReport report)
    {
        // Checkpoints survive a hung accessibility provider killed by the controller.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonOptions));
        File.Move(temporary, path, true);
    }
    public static DiagnosticReport Load(string path) => JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(path), JsonOptions)!;
    public static string SafeError(Exception ex) => $"{ex.GetType().Name} (0x{ex.HResult:X8})";
}
