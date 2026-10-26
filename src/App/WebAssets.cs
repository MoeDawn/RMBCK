using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

static class WebAssets
{
    // 资源名 -> 相对路径（build.bat 中 /resource:web/x,资源名 必须与此表一致）
    public static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "RMBCK.web.index.html",   "index.html" },
        { "RMBCK.web.app.css",      "app.css" },
        { "RMBCK.web.app.js",       "app.js" },
        { "RMBCK.web.overlay.html", "overlay.html" },
        { "RMBCK.web.overlay.css",  "overlay.css" },
        { "RMBCK.web.overlay.js",   "overlay.js" },
        { "RMBCK.web.assets.icon.png",    "assets/icon.png" },
        { "RMBCK.web.assets.iconf1.png",  "assets/iconf1.png" },
        { "RMBCK.web.assets.iconf2.png",  "assets/iconf2.png" },
        { "RMBCK.web.assets.iconf3.png",  "assets/iconf3.png" },
        { "RMBCK.web.assets.iconf4.png",  "assets/iconf4.png" },
        { "RMBCK.web.assets.err.jpg",     "assets/err.jpg" },
        { "RMBCK.web.assets.window.ico",  "assets/window.ico" }
    };

    public static string Extract(string version)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RMBCK", "web", version);
        string marker = Path.Combine(root, ".ready");
        if (File.Exists(marker)) return root;

        Directory.CreateDirectory(root);
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (var kv in Map)
        {
            string target = Path.Combine(root, kv.Value.Replace('/', Path.DirectorySeparatorChar));
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (Stream s = asm.GetManifestResourceStream(kv.Key))
            {
                if (s == null) continue;
                using (FileStream fs = File.Create(target)) s.CopyTo(fs);
            }
        }
        File.WriteAllText(marker, version);
        return root;
    }

    public static string ReadText(string relativePath)
    {
        foreach (var kv in Map)
        {
            if (!string.Equals(kv.Value, relativePath, StringComparison.OrdinalIgnoreCase)) continue;
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(kv.Key))
            {
                if (s == null) return null;
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }
        return null;
    }
}
