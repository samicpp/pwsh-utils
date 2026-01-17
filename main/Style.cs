namespace Samicpp.Pwsh;

using System.Management.Automation;
using System.Management.Automation.Host;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Humanizer;
using Microsoft.PowerShell.Commands;

public class UXStyle(PSHost host, PSVersionHashTable version)
{
    public readonly PSHost host = host;
    public readonly PSVersionHashTable hash = version;

    
    DirectoryInfo? lastdir;
    public bool couldRead = false;
    public bool couldWrite = false;

    HistoryInfo? lastcmd;


    public string Prompt(PathInfo pwd, HistoryInfo? hist)
    {
        DirectoryInfo cwd = new(pwd.Path);
        // Console.WriteLine(hist?.ToString() ?? "null");
        
        string ver = $"{hash["PSVersion"]}";

        // PS C:\Windows\system32> 
        string prefix = "PS ";
        string user = ""; // Environment.UserName;
        string dir = cwd.FullName;
        string suffix = "> ";

        // var e = new WindowsPrincipal (WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        bool admin = IsAdmin();

        string timestamp = "";
        if ((lastcmd == null || hist?.Duration != lastcmd?.Duration) && hist?.Duration is TimeSpan dur)
        {
            lastcmd = hist;

            timestamp += Color("\udb86\udee1 ", 242);
            if (dur.Days > 0) timestamp += $"{dur.Days}d ";
            if (dur.Hours > 0) timestamp += $"{dur.Hours}h ";
            if (dur.Minutes > 0) timestamp += $"{dur.Minutes}m ";
            timestamp += $"{Math.Round(dur.TotalSeconds, 3) % 60}s ";

        }

        
        bool canRead = false;
        bool canWrite = false;

        if (lastdir?.FullName == cwd.FullName)
        {
            canRead = couldRead;
            canWrite = couldWrite;
        }
        else
        {
            lastdir = cwd;

            try
            {
                Directory.GetFiles(cwd.FullName);
                couldRead = canRead = true;
            }
            catch (UnauthorizedAccessException) { }

            try
            {
                string testFile = Path.Combine(cwd.FullName, Path.GetRandomFileName());
                using (FileStream fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
                couldWrite = canWrite = true;
            }
            catch (UnauthorizedAccessException) { }

            
        }

        if (admin)
        {
            prefix = $"PS";
        }
        else
        {
            prefix = $"ps";
        }

        if (!cwd.Exists) prefix = Color(prefix, 196);
        else if (canRead && canWrite) prefix = Color(prefix, 115);
        else if (canRead || canWrite) prefix = Color(prefix, 215);
        else prefix = Color(prefix, 202);

        dir = Color(dir, 242);

        host.UI.RawUI.WindowTitle = $"pwsh {ver}";

        // return $"{prefix}{user}{dir}{suffix}";
        return $"\n{prefix} {timestamp}{user}{dir}{suffix}";
    }

    public static bool IsAdmin()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        else
        {
            return Unix.geteuid() == 0;
        }
    }

    public static string Color(string text, (byte,byte,byte)? foreground = null, (byte,byte,byte)? background = null, bool reset = true)
    {
        if (foreground != null)
        {
            var (r,g,b) = ((byte,byte,byte))foreground;
            text = $"\e[38;2;{r};{g};{b}m" + text;
        }
        if (background != null)
        {
            var (r,g,b) = ((byte,byte,byte))background;
            text = $"\e[48;2;{r};{g};{b}m" + text;
        }
        if (reset) text += "\e[0m";

        return text;
    }
    public static string Color(string text, byte? foreground = null, byte? background = null, bool reset = true)
    {
        if (foreground != null) text = $"\e[38;5;{foreground}m" + text;
        if (background != null) text = $"\e[48;5;{background}m" + text;
        if (reset) text += "\e[0m";

        return text;
    }
    public static string Color(string text, int? foreground = null, int? background = null, bool reset = true)
    {
        (byte,byte,byte)? fore = null;
        (byte,byte,byte)? back = null;

        if (foreground != null) fore = ((byte)((foreground & 0xff0000) >> 16), (byte)((foreground & 0x00ff00) >> 8), (byte)(foreground & 0x0000ff));
        if (background != null) back = ((byte)((background & 0xff0000) >> 16), (byte)((background & 0x00ff00) >> 8), (byte)(background & 0x0000ff));

        return Color(text, fore, back, reset);
    }
}

static class Unix
{
    [DllImport("libc")]
    public static extern uint geteuid();
}
