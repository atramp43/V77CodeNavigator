using System.Runtime.InteropServices;
using System.Text;

namespace V77CodeNavigator;

internal sealed partial class GlobalModuleJump
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint handle);

    private static string ReadConfiguratorClipboard()
    {
        // Russian 1C 7.7 supplies ANSI text. Windows may synthesize Unicode
        // using a different system locale, producing mojibake in literals.
        if(!OpenClipboard(0)) throw new InvalidOperationException("Буфер обмена занят.");
        try
        {
            var handle=GetClipboardData(1);
            if(handle==0) throw new InvalidOperationException("Редактор не передал текст ANSI.");
            int size=checked((int)GlobalSize(handle));
            if(size<=0 || size>64*1024*1024) throw new InvalidOperationException("Размер текста модуля недоступен.");
            var pointer=GlobalLock(handle);
            if(pointer==0) throw new InvalidOperationException("Текст модуля недоступен.");
            try
            {
                var bytes=new byte[size];
                Marshal.Copy(pointer,bytes,0,size);
                int length=Array.IndexOf(bytes,(byte)0);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251).GetString(bytes,0,length<0?size:length);
            }
            finally{GlobalUnlock(handle);}
        }
        finally{CloseClipboard();}
    }
}
