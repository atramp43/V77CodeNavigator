using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using V77Navigator.Core;
using V77Navigator.Diagnostics;

namespace V77Navigator.Tests;

internal static class Program
{
    private static int passed, failed;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint ex, string cls, string text, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [STAThread]
    private static int Main()
    {
        Test("Russian procedure/function + Latin names", () =>
        {
            var d = ModuleParser.Parse("Процедура Тест(А) Экспорт\r\nКонецПроцедуры\r\nФункция Calc_2()\nКонецФункции");
            Equal(2, d.Count); Equal("Тест", d[0].Name); Equal(3, d[1].Line);
        });
        Test("Case insensitive lookup, duplicates preserved", () =>
        {
            var d = ModuleParser.Parse("пРоЦеДуРа Test()\nProcedure TEST()\nFunction Имя()");
            Equal(2, ModuleParser.Find(d, "tEsT").Count); Equal(0, ModuleParser.Find(d, "Missing").Count);
        });
        Test("Comments, quoted keywords and escaped quotes ignored", () =>
        {
            var d = ModuleParser.Parse("// Процедура Fake()\nX = \"\"\"Функция Fake2()\"\"\";\n\tФункция Real() // Процедура Bad()\n");
            Equal(1, d.Count); Equal("Real", d[0].Name);
        });
        Test("Multiline pipe string ignored", () =>
        {
            var d = ModuleParser.Parse("X = \"text\r\n| Процедура Fake()\r\n| Функция Fake2()\";\r\nПроцедура Real()\r\n");
            Equal(1, d.Count); Equal(4, d[0].Line);
        });
        Test("Multiline declaration, tabs and comments", () =>
        {
            var d = ModuleParser.Parse("\uFEFF\tПроцедура // comment\r\n Имя\r\n (\r\n Знач А, Б)\r\n");
            Equal(1, d.Count); Equal("Имя", d[0].Name); Equal(1, d[0].Line); Equal(2, d[0].Offset);
        });
        Test("Requires keyword boundary, line start, name and opening paren", () =>
        {
            Equal(0, ModuleParser.Parse("МояПроцедура Wrong()\nX.Процедура Wrong()\nПроцедура Nope;\nПроцедура 123()\n").Count);
        });
        Test("Newline variants and unterminated string", () =>
        {
            var d = ModuleParser.Parse("// test\rПроцедура A()\nФункция B()\r\nX = \"\nПроцедура Hidden()");
            Equal(2, d.Count); Equal(2, d[0].Line); Equal(3, d[1].Line);
        });
        foreach (int lines in new[] { 10_000, 100_000 })
            Test($"Performance/offsets: {lines:N0} lines", () =>
            {
                var prefix = string.Concat(Enumerable.Repeat("X = \"Процедура Fake()\"; // ignored\r\n", lines - 1));
                var timer = Stopwatch.StartNew();
                var d = ModuleParser.Parse(prefix + "Функция БольшойМодуль()");
                Equal(1, d.Count); Equal(lines, d[0].Line); Equal(prefix.Length, d[0].Offset);
                if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Exceeded 15 seconds");
                Console.WriteLine($"  elapsed {timer.ElapsedMilliseconds} ms");
            });
        Test("History LIFO across modules, duplicate and bounded capacity", () =>
        {
            var h = new NavigationHistory(2);
            var a = new NavigationPosition(1, 2, "A", 3);
            var b = new NavigationPosition(1, 4, "B", 5);
            var c = new NavigationPosition(2, 6, "C", 7);
            h.Push(a); h.Push(b); h.Push(b); Equal(2, h.Count); h.Push(c);
            Equal(c, h.Pop()); Equal(b, h.Pop()); Equal<NavigationPosition?>(null, h.Pop());
        });
        Test("Configurator guard excludes Enterprise and other apps", () =>
        {
            Equal(true, SafetyPolicy.IsConfigurator("1CV7", "Конфигуратор - база"));
            Equal(true, SafetyPolicy.IsConfigurator("1cv7", "1C: Configurator"));
            Equal(false, SafetyPolicy.IsConfigurator("1cv7", "1С:Предприятие"));
            Equal(false, SafetyPolicy.IsConfigurator("Other", "Конфигуратор"));
        });
        Test("Only exact EDIT classes allowed", () =>
        {
            Equal(true, SafetyPolicy.IsStandardEdit("RichEdit20A"));
            Equal(false, SafetyPolicy.IsStandardEdit("Afx:Edit:custom"));
            Equal(false, SafetyPolicy.IsStandardEdit("Scintilla"));
            Equal(false, SafetyPolicy.IsStandardEdit(""));
        });

        // Our own WinForms fixture only. No real 1C application is addressed.
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "V77Navigator test fixture (not 1C)", ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
        string initial = "Привет\r\nProcedure Test()\r\nКонец";
        var fh = form.Handle;
        // Raw standard classes, not WinForms' subclassed WindowsForms10.* windows.
        var eh = CreateWindowEx(0, "Edit", initial, 0x50000004, 0, 0, 200, 100, fh, 0, 0, 0);
        LoadLibrary("msftedit.dll");
        var rh = CreateWindowEx(0, "RICHEDIT50W", new string('А', 80_000), 0x50000004, 0, 110, 200, 100, fh, 0, 0, 0);
        var ph = CreateWindowEx(0, "Edit", "secret", 0x50000020, 210, 0, 100, 30, fh, 0, 0, 0);
        if (eh == 0 || rh == 0 || ph == 0) throw new Exception("Fixture CreateWindowEx failed");
        // Set up selection on our own controls only.
        V77Navigator.WinApi.Native.Message(eh, 0xB1, 10, 14, out _);
        V77Navigator.WinApi.Native.Message(rh, 0xB1, 70_000, 70_003, out _);
        form.Shown += async (_, _) =>
        {
            await Task.Run(() =>
            {
                Test("Real WinAPI read on own EDIT: privacy and selection", () =>
                {
                    var p = Collector.ProbeStandardEdit(eh, false);
                    Equal(10, (int)p["SelectionStart"]!); Equal(14, (int)p["SelectionEnd"]!);
                    Equal(false, p.ContainsKey("Text"));
                    if (JsonSerializer.Serialize(p).Contains("Привет", StringComparison.Ordinal)) throw new Exception("Privacy leak");
                    Equal(initial, (string)Collector.ProbeStandardEdit(eh, true)["Text"]!);
                });
                Test("RichEdit selection above 65535", () =>
                {
                    var p = Collector.ProbeStandardEdit(rh, false);
                    Equal(70_000, (int)p["SelectionStart"]!); Equal(70_003, (int)p["SelectionEnd"]!);
                });
                Test("Unknown HWND/class and password skipped", () =>
                {
                    Equal(1, Collector.ProbeStandardEdit(0, true).Count);
                    Equal(1, Collector.ProbeStandardEdit(fh, true).Count);
                    Equal(1, Collector.ProbeStandardEdit(ph, true).Count);
                });
                Test("UIA/MSAA on own EDIT privacy", () =>
                {
                    var a = new AccessibilityReport();
                    AccessibilityProbe.Read(eh, (uint)Environment.ProcessId, a, false);
                    var json = JsonSerializer.Serialize(a);
                    if (json.Contains("Привет", StringComparison.Ordinal) || json.Contains("DocumentText", StringComparison.Ordinal) || json.Contains("MSAAValue", StringComparison.Ordinal)) throw new Exception("Privacy leak");
                    if (!a.Properties.ContainsKey("UIA")) throw new Exception("No UIA results");
                });
            });
            Test("Read probes preserve text and selection", () =>
            {
                var ep = Collector.ProbeStandardEdit(eh, true);
                var rp = Collector.ProbeStandardEdit(rh, false);
                Equal(initial, (string)ep["Text"]!); Equal(10, (int)ep["SelectionStart"]!); Equal(14, (int)ep["SelectionEnd"]!);
                Equal(70_000, (int)rp["SelectionStart"]!); Equal(70_003, (int)rp["SelectionEnd"]!);
            });
            form.Close();
        };
        Application.Run(form);
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed. WinForms fixture is NOT 1C integration.");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
}
