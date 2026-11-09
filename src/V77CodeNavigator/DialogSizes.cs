using System.Text.Json;

namespace V77CodeNavigator;

internal static class DialogSizes
{
    private static string FileName=>Path.Combine(Program.ProjectRoot(),"window-sizes.json");
    private static readonly Dictionary<string,Size> Sizes=Load();

    private static Dictionary<string,Size> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string,Size>>(File.ReadAllText(FileName)) ?? new();
        }
        catch(IOException){return new();}
        catch(UnauthorizedAccessException){return new();}
        catch(JsonException){return new();}
    }

    internal static DialogResult Show(Form form,string key,IWin32Window owner)
    {
        var preferred=Sizes.TryGetValue(key,out var size) && size.Width>0 && size.Height>0
            ? size : form.Size;
        Fit(form,preferred,Screen.FromHandle(owner.Handle).WorkingArea.Size);

        var result=form.ShowDialog(owner);
        var actual=form.WindowState==FormWindowState.Normal ? form.Size : form.RestoreBounds.Size;
        Sizes[key]=actual;
        string temp=FileName+".tmp";
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(Sizes));
            File.Move(temp,FileName,true);
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        return result;
    }

    internal static void Fit(Form form,Size preferred,Size area)
    {
        form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,area.Width),
            Math.Min(form.MinimumSize.Height,area.Height));
        form.Size=new Size(
            Math.Clamp(preferred.Width,Math.Max(1,form.MinimumSize.Width),area.Width),
            Math.Clamp(preferred.Height,Math.Max(1,form.MinimumSize.Height),area.Height));
    }
}
