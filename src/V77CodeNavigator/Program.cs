using System.Diagnostics;
using System.IO;
using System.Text;
using V77Navigator.Diagnostics;

namespace V77CodeNavigator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Internal worker: no keyboard commands and no foreground activation.
        if (args.Length >= 2 && args[0] == "--capture")
        {
            var path = Path.GetFullPath(args[1]);
            // The worker may write reports only into this project's logs directory.
            var logs = Path.Combine(ProjectRoot(), "logs") + Path.DirectorySeparatorChar;
            if (!path.StartsWith(logs, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(path) != logs.TrimEnd(Path.DirectorySeparatorChar)) return 4;
            var r = Collector.Capture(path, args.Contains("--include-content"));
            return r.Status.StartsWith("Complete", StringComparison.Ordinal) ? 0 : 2;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new DiagnosticForm());
        return 0;
    }

    internal static string ProjectRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "V77CodeNavigator")) && Directory.Exists(Path.Combine(d.FullName, "logs"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
}

internal sealed class DiagnosticForm : Form
{
    private readonly Button capture = new() { Text = "Собрать отчёт через 7 секунд", AutoSize = true };
    private readonly CheckBox content = new() { Text = "Включить текст модулей, значения и заголовки окон в отчёт", AutoSize = true };
    private readonly TextBox output = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly Button open = new() { Text = "Открыть папку отчётов", AutoSize = true };

    public DiagnosticForm()
    {
        string windowTitle=$"V77CodeNavigator v{Application.ProductVersion.Split('+', 2)[0]}";
        Text=windowTitle;
        AutoScaleMode=AutoScaleMode.Dpi;
        MinimumSize=new Size(520,440);Size=new Size(760,540);StartPosition=FormStartPosition.CenterScreen;
        var appIcon=UiArt.AppIcon();Icon=appIcon;
        var bindings=new Bindings();
        var pilot=new CheckBox{Text="Горячие клавиши в редакторе 1С",AutoSize=true};
        var trayOption=new CheckBox{Text="При сворачивании скрывать окно в трей",AutoSize=true,Checked=true};
        content.Text="Включать исходный текст и заголовки в отчёт";
        var keys=new Button{Text="Горячие клавиши и справка…",AutoSize=true};
        UiArt.Button(keys,"keyboard");UiArt.Button(capture,"file-earmark-text");UiArt.Button(open,"folder2-open");
        capture.Text="Собрать через 7 секунд";
        var autoTrack=new CheckBox{Text="Автоматически отслеживать Конфигуратор и модули",AutoSize=true};
        GlobalModuleJump? jump=null;
        void UpdateTracking()
        {
            if(pilot.Checked || autoTrack.Checked)
            {
                if(jump!=null) return;
                try{jump=new(this,bindings,m=>output.Text=m,active=>Text=(active?GlobalModuleJump.ActivePrefix:"")+windowTitle);}
                catch(Exception ex){output.Text=ex.Message;}
            }
            else
            {
                jump?.Dispose();
                jump=null;
                Text=windowTitle;
            }
        }
        pilot.CheckedChanged+=(_,_)=>UpdateTracking();
        autoTrack.CheckedChanged+=(_,_)=>UpdateTracking();
        string defaults=Path.Combine(Program.ProjectRoot(),"navigator-settings.json");
        Preferences Snapshot()
        {
            var size=WindowState==FormWindowState.Normal?Size:RestoreBounds.Size;
            return new(1,pilot.Checked,content.Checked,trayOption.Checked,size.Width,size.Height,new(bindings.Map),
                autoTrack.Checked,bindings.DialogSize.Width,bindings.DialogSize.Height);
        }
        void Apply(Preferences p)
        {
            bindings.Apply(p.Bindings);
            bindings.DialogSize=p.HotkeysWidth>0 && p.HotkeysHeight>0 ? new Size(p.HotkeysWidth,p.HotkeysHeight) : Size.Empty;
            var screen=Screen.FromControl(this).WorkingArea;
            Size=new Size(Math.Clamp(p.Width,MinimumSize.Width,Math.Max(MinimumSize.Width,screen.Width)),Math.Clamp(p.Height,MinimumSize.Height,Math.Max(MinimumSize.Height,screen.Height)));
            trayOption.Checked=p.MinimizeToTray;content.Checked=p.IncludeContent;autoTrack.Checked=p.AutoTrack;pilot.Checked=p.Shortcuts;
        }
        void Transfer(bool load)
        {
            using FileDialog picker=load?new OpenFileDialog():new SaveFileDialog();
            picker.Filter="Настройки V77CodeNavigator (*.json)|*.json";picker.DefaultExt="json";
            picker.InitialDirectory=Program.ProjectRoot();picker.FileName="navigator-settings.json";
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{if(load)Apply(Preferences.Load(picker.FileName));else Snapshot().Save(picker.FileName);
                output.Text=(load?"Настройки загружены из ":"Настройки сохранены в ")+picker.FileName;}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Настройки",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        var menu=new MenuStrip{Dock=DockStyle.Top};
        ToolStripMenuItem Item(string title,string icon,Action action)=>new(title,UiArt.Image(icon),(_,_)=>action());
        var config=new ToolStripMenuItem("Настройки",UiArt.Image("gear"));
        void EditKeys()
        {
            bindings.Edit(this);
            try{Snapshot().Save(defaults);}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Не удалось сохранить настройки");}
        }
        keys.Click+=(_,_)=>EditKeys();
        config.DropDownItems.Add(Item("Горячие клавиши и справка…","keyboard",EditKeys));
        config.DropDownItems.Add(Item("Загрузить из файла…","upload",()=>Transfer(true)));
        config.DropDownItems.Add(Item("Сохранить в файл…","download",()=>Transfer(false)));
        var trayMenu=new ContextMenuStrip();
        var tray=new NotifyIcon{Icon=appIcon,Text="V77CodeNavigator",ContextMenuStrip=trayMenu};
        void Restore(){Show();WindowState=FormWindowState.Normal;Activate();tray.Visible=false;}
        void HideInTray(){tray.Visible=true;Hide();}
        trayMenu.Items.Add(Item("Открыть навигатор","window",Restore));
        trayMenu.Items.Add(Item("Выход","x-lg",Close));
        tray.DoubleClick+=(_,_)=>Restore();
        var window=new ToolStripMenuItem("Окно",UiArt.Image("window"));
        window.DropDownItems.Add(Item("Скрыть в трей","box-arrow-in-down",HideInTray));
        window.DropDownItems.Add(Item("Выход","x-lg",Close));
        menu.Items.AddRange(new ToolStripItem[]{config,window});MainMenuStrip=menu;
        Resize+=(_,_)=>{if(WindowState==FormWindowState.Minimized && trayOption.Checked)HideInTray();};
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(12),AutoScroll=true};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<7;i++)body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};
        actions.Controls.AddRange(new Control[]{capture,open,keys});
        var hint=new Label{Text="После запуска сбора переключитесь в модуль 1С за 7 секунд.",AutoSize=true,Dock=DockStyle.Fill};
        var brand=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Margin=new Padding(3,0,3,8)};
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        brand.Controls.Add(new PictureBox{Image=UiArt.Image("navigator-logo",48),SizeMode=PictureBoxSizeMode.Zoom,
            Size=new Size(44,44),Margin=new Padding(0,0,10,0),TabStop=false},0,0);
        var brandFont=new Font(Font.FontFamily,16,FontStyle.Bold);
        brand.Controls.Add(new Label{Text="V77CodeNavigator",Font=brandFont,AutoSize=true,Anchor=AnchorStyles.Left},1,0);
        FormClosed+=(_,_)=>brandFont.Dispose();
        body.Controls.Add(brand,0,0);
        body.Controls.Add(actions,0,1);body.Controls.Add(hint,0,2);body.Controls.Add(pilot,0,3);body.Controls.Add(content,0,4);body.Controls.Add(trayOption,0,5);body.Controls.Add(autoTrack,0,6);body.Controls.Add(output,0,7);
        Controls.Add(body);Controls.Add(menu);
        Shown+=(_,_)=>{
            try{if(File.Exists(defaults)) Apply(Preferences.Load(defaults));}
            catch(Exception ex){output.Text="Не удалось загрузить настройки: "+ex.Message;}
            if(Environment.GetCommandLineArgs().Any(arg=>arg is "--f10" or "--f12"))pilot.Checked=true;
        };
        FormClosed+=(_,_)=>{
            try{Snapshot().Save(defaults);}catch(Exception ex){MessageBox.Show(ex.Message,"Не удалось сохранить настройки");}
            jump?.Dispose();tray.Dispose();trayMenu.Dispose();appIcon.Dispose();
        };        capture.Click += async (_, _) => await CaptureAsync();
        FormClosing += (_, e) => { if (!capture.Enabled) e.Cancel = true; };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", Directory.CreateDirectory(Path.Combine(Program.ProjectRoot(), "logs")).FullName) { UseShellExecute = true }); }
            catch (Exception ex) { output.Text = ex.Message; }
        };
        output.Text = "Готово. По умолчанию содержимое модулей, UIA Name/Value и заголовки окон исключены.\r\nДля пробного перехода включите F10 и поставьте курсор внутри имени.";
    }

    private async Task CaptureAsync()
    {
        capture.Enabled = false; content.Enabled = false; open.Enabled = false;
        bool include = content.Checked;
        try
        {
            for (int left = 7; left > 0; left--)
            {
                output.Text = $"Переключитесь в редактор модуля 1С. До снимка: {left} сек.";
                await Task.Delay(1000);
                if (IsDisposed) return;
            }
            var logs = Path.Combine(Program.ProjectRoot(), "logs");
            Directory.CreateDirectory(logs);
            var path = Path.Combine(logs, $"diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            output.Text = "Сбор технических сведений… Ожидание ограничено 30 сек.\r\n" + path;
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Program.ProjectRoot() };
            start.ArgumentList.Add("--capture"); start.ArgumentList.Add(path);
            if (include) start.ArgumentList.Add("--include-content");
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            bool expired = false;
            try { await worker.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { expired = true; worker.Kill(true); await worker.WaitForExitAsync(); }
            if (!File.Exists(path)) throw new IOException("Рабочий процесс не успел создать отчёт.");
            var report = Reports.Load(path);
            if (expired)
            {
                report.Status = "Partial: worker exceeded 30 seconds; last checkpoint retained. An InProgress provider may be hung.";
                Reports.Save(path, report);
            }
            // Sidecar contains no captured text; JSON remains the authoritative technical report.
            var summary = $"V77CodeNavigator diagnostic\r\n{report.CapturedAt:O}\r\nStatus: {report.Status}\r\nPID: {report.ProcessId}\r\nRoot: {report.Root}\r\nFocus: {report.Focus}\r\nActive MDI: {report.ActiveMdiChild ?? "unknown"}\r\nWindows: {report.Windows.Count}\r\nContent included: {report.IncludeContent}\r\n{report.EditorIdentification}\r\n{report.CursorMovement}\r\nError: {report.Error}\r\nJSON: {Path.GetFileName(path)}\r\n";
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), summary, Encoding.UTF8);
            if (!IsDisposed) output.Text = summary + "\r\nОтчёт сохранён:\r\n" + path + "\r\nПередайте JSON для анализа. Курсор программно не перемещался.";
        }
        catch (Exception ex) { if (!IsDisposed) output.Text = "Ошибка: " + ex.Message; }
        finally { if (!IsDisposed) { capture.Enabled = true; content.Enabled = true; open.Enabled = true; } }
    }
}




