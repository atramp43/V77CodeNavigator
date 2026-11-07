using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace V77CodeNavigator;

internal static class UiArt
{
    private static readonly Dictionary<string,Bitmap> Cache=new();
    internal static Bitmap Image(string name)
    {
        if(Cache.TryGetValue(name,out var cached)) return cached;
        using var stream=typeof(UiArt).Assembly.GetManifestResourceStream($"V77CodeNavigator.Icons.{name}.png") ?? throw new InvalidOperationException(name);
        using var source=new Bitmap(stream);
        return Cache[name]=new Bitmap(source,new Size(20,20));
    }
    internal static void Button(Button button,string name)
    {
        button.Image=Image(name);button.TextImageRelation=TextImageRelation.ImageBeforeText;
        button.Padding=new Padding(5,2,5,2);
    }
    [DllImport("user32")] private static extern bool DestroyIcon(nint icon);
    internal static Icon AppIcon()
    {
        var h=Image("signpost-2").GetHicon();
        try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}
    }
}

internal sealed record Preferences(int Version,bool Shortcuts,bool IncludeContent,bool MinimizeToTray,int Width,int Height,Dictionary<NavAction,Keys> Bindings)
{
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    internal static Preferences Load(string path)
    {
        var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path),Options);
        if(p==null || p.Version!=1 || p.Bindings==null || p.Width<=0 || p.Height<=0)
            throw new InvalidDataException("Файл настроек повреждён или имеет неизвестную версию.");
        return p;
    }
    internal void Save(string path)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(this,Options));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
