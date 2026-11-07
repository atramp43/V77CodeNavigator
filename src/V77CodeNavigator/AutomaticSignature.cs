using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using V77Navigator.Core;
using V77Navigator.WinApi;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    private sealed record SignatureIndex(Spot Module,string[] Lines,Dictionary<string,string> Signatures);
    private SignatureIndex? signatureIndex;
    private readonly StringBuilder typedName=new();
    private SignaturePopup? signaturePopup;
    private System.Windows.Forms.Timer? signatureTimer;

    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key,uint scan,byte[] state,StringBuilder text,int size,uint flags,nint layout);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window,ref Point point);

    private void IndexSignatures(Spot origin,string source)
    {
        var signatures=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var declaration in ModuleParser.Parse(source))
            signatures.TryAdd(declaration.Name,ModuleParser.Signature(source,declaration));
        signatureIndex=new(origin,source.ReplaceLineEndings("\n").Split('\n'),signatures);
        typedName.Clear();
    }

    private void ObserveSignatureKey(int vk,Keys chord,nint data)
    {
        var index=signatureIndex!;
        var module=index.Module;
        if(Native.GetForegroundWindow()!=module.Root || (chord&(Keys.Control|Keys.Alt))!=0)
        {
            typedName.Clear();
            CloseSignature();
            return;
        }

        if(vk is (int)Keys.ShiftKey or (int)Keys.LShiftKey or (int)Keys.RShiftKey) return;
        var thread=Native.GetWindowThreadProcessId(module.Root,out _);
        var state=new byte[256];
        if((chord&Keys.Shift)!=0) state[(int)Keys.ShiftKey]=128;
        if(Control.IsKeyLocked(Keys.CapsLock)) state[(int)Keys.CapsLock]=1;
        var translated=new StringBuilder(8);
        int length=ToUnicodeEx((uint)vk,(uint)Marshal.ReadInt32(data,4),state,translated,translated.Capacity,4,GetKeyboardLayout(thread));
        char character=length==1?translated[0]:'\0';
        if(character=='(')
        {
            string name=typedName.ToString();
            typedName.Clear();
            ui.BeginInvoke(new Action(async ()=>{
                // Let the Configurator process the bracket before reading its caret.
                await Task.Delay(80);
                if(signatureIndex==index) ShowAutomaticSignature(index,name);
            }));
            return;
        }

        if(char.IsLetterOrDigit(character) || character=='_')
        {
            if(typedName.Length<256) typedName.Append(character);
        }
        else typedName.Clear();

        if(character==')' || vk is (int)Keys.Escape or (int)Keys.Return or (int)Keys.Left or
            (int)Keys.Right or (int)Keys.Up or (int)Keys.Down or (int)Keys.Home or (int)Keys.End)
            CloseSignature();
    }

    private void ShowAutomaticSignature(SignatureIndex index,string name)
    {
        CloseSignature();
        var module=index.Module;
        if(hook==0 || Native.GetForegroundWindow()!=module.Root ||
            !Target(module.Root,out var editor,out var pid) || editor!=module.Editor || pid!=module.Pid ||
            Native.GetParent(module.Document)!=module.Mdi || Native.Caption(module.Document)!=module.Title) return;

        if(!index.Signatures.TryGetValue(name,out var signature))
        {
            if(name.Length!=0) return;
            var coordinates=Coordinates(module.Root);
            if(coordinates is not { } position || position.Line>index.Lines.Length) return;
            var line=index.Lines[position.Line-1];
            int beforeBracket=position.Column-2;
            if(beforeBracket<0 || beforeBracket>line.Length) return;
            var match=Regex.Match(line[..beforeBracket],@"[\p{L}_][\p{L}\p{Nd}_]*$");
            if(!match.Success || !index.Signatures.TryGetValue(match.Value,out signature)) return;
        }

        uint thread=Native.GetWindowThreadProcessId(module.Root,out _);
        var gui=new Native.GuiInfo{Size=(uint)Marshal.SizeOf<Native.GuiInfo>()};
        if(!Native.GetGUIThreadInfo(thread,ref gui) || gui.Caret==0) return;
        var point=new Point(gui.CaretRect.Left,gui.CaretRect.Bottom+5);
        if(!ClientToScreen(gui.Caret,ref point)) return;
        var popup=new SignaturePopup(signature.ReplaceLineEndings(" "));
        var area=Screen.FromPoint(point).WorkingArea;
        popup.Location=new Point(Math.Clamp(point.X,area.Left,Math.Max(area.Left,area.Right-popup.Width)),
            Math.Clamp(point.Y,area.Top,Math.Max(area.Top,area.Bottom-popup.Height)));
        signaturePopup=popup;
        popup.Show(new DialogOwner(module.Root));
        var expires=Environment.TickCount64+10000;
        signatureTimer=new System.Windows.Forms.Timer{Interval=150};
        signatureTimer.Tick+=(_,_)=>{
            var current=new Native.GuiInfo{Size=(uint)Marshal.SizeOf<Native.GuiInfo>()};
            if(Environment.TickCount64>=expires || Native.GetForegroundWindow()!=module.Root ||
                !Native.GetGUIThreadInfo(thread,ref current) || current.Focus!=editor)
                CloseSignature();
        };
        signatureTimer.Start();
    }

    private void CloseSignature()
    {
        signatureTimer?.Dispose();
        signatureTimer=null;
        signaturePopup?.Dispose();
        signaturePopup=null;
    }

    private sealed class SignaturePopup : Form
    {
        internal SignaturePopup(string signature)
        {
            Text="Параметры вызова";
            FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.Manual;
            ShowInTaskbar=false;
            TopMost=true;
            AutoSize=true;
            AutoSizeMode=AutoSizeMode.GrowAndShrink;
            Controls.Add(new Label{Text=signature,AutoSize=true,MaximumSize=new Size(560,0),
                BackColor=SystemColors.Info,ForeColor=SystemColors.InfoText,Padding=new Padding(8)});
            Size=GetPreferredSize(Size.Empty);
        }
        protected override bool ShowWithoutActivation=>true;
        protected override CreateParams CreateParams
        {
            get { var options=base.CreateParams; options.ExStyle|=0x08000000; return options; }
        }
    }
}
