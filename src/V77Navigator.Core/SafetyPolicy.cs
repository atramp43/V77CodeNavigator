namespace V77Navigator.Core;

public static class SafetyPolicy
{
    public static bool IsConfigurator(string processName, string title) =>
        processName.Equals("1cv7", StringComparison.OrdinalIgnoreCase) &&
        (title.Contains("Конфигуратор", StringComparison.OrdinalIgnoreCase) || title.Contains("Configurator", StringComparison.OrdinalIgnoreCase));

    // Exact classes only: a custom editor with a similar name must not receive EM_*.
    public static bool IsStandardEdit(string className) =>
        new[] { "Edit", "RichEdit", "RichEdit20A", "RichEdit20W", "RICHEDIT50W" }.Contains(className, StringComparer.OrdinalIgnoreCase);
}
