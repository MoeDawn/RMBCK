using System;
using System.Diagnostics;

static class Autostart
{
    const string TaskName = "RMBCK";

    public static bool IsEnabled()
    {
        return Run("/Query /TN " + TaskName, 4000);
    }

    public static bool SetEnabled(bool on)
    {
        string exe = System.Windows.Forms.Application.ExecutablePath;
        string args = on
            ? "/Create /F /TN " + TaskName + " /RL HIGHEST /SC ONLOGON /TR \"\\\"" + exe + "\\\"\""
            : "/Delete /F /TN " + TaskName;
        return Run(args, 8000);
    }

    static bool Run(string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                return p.ExitCode == 0;
            }
        }
        catch { return false; }
    }
}
