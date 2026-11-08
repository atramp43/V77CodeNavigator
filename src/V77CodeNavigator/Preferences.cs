using System.Text.Json;
using System.Text.Json.Serialization;

namespace V77CodeNavigator;

internal static class UiArt
{
    private static readonly Dictionary<string,Bitmap> Cache=new();
    internal static Bitmap Image(string name,int size=20)
    {
        string key=$"{name}:{size}";
        if(Cache.TryGetValue(key,out var cached)) return cached;
        using var stream=typeof(UiArt).Assembly.GetManifestResourceStream($"V77CodeNavigator.Icons.{name}.png") ?? throw new InvalidOperationException(name);
        using var source=new Bitmap(stream);
        return Cache[key]=new Bitmap(source,new Size(size,size));
    }
    internal static void Button(Button button,string name)
    {
        button.Image=Image(name);button.TextImageRelation=TextImageRelation.ImageBeforeText;
        button.Padding=new Padding(5,2,5,2);
    }
    internal static Icon AppIcon()
    {
        using var stream=typeof(UiArt).Assembly.GetManifestResourceStream("V77CodeNavigator.Icons.navigator.ico")
            ?? throw new InvalidOperationException("navigator.ico");
        using var icon=new Icon(stream,32,32);
        return (Icon)icon.Clone();
    }
}

internal sealed record Preferences(int Version,bool Shortcuts,bool IncludeContent,bool MinimizeToTray,int Width,int Height,Dictionary<NavAction,Keys> Bindings,bool AutoTrack=false)
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
