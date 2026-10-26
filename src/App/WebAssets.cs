using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
        string parent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RMBCK", "web");
        string cacheName = (version == null ? "0" : version) + "-" + ComputeHash8();
        string root = Path.Combine(parent, cacheName);
        string marker = Path.Combine(root, ".ready");
        if (File.Exists(marker)) return root;

        Directory.CreateDirectory(root);
        Assembly asm = Assembly.GetExecutingAssembly();
        bool complete = true;
        foreach (var kv in Map)
        {
            string target = Path.Combine(root, kv.Value.Replace('/', Path.DirectorySeparatorChar));
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (Stream s = asm.GetManifestResourceStream(kv.Key))
            {
                // 映射与嵌入资源不一致时绝不写 .ready，下次启动会重试
                if (s == null) { complete = false; break; }
                using (FileStream fs = File.Create(target)) s.CopyTo(fs);
            }
        }
        if (!complete) return root;

        File.WriteAllText(marker, cacheName);
        CleanupSiblings(parent, root);
        return root;
    }

    // 前端资源内容哈希（按资源名排序，并加入名称/长度框架，保证键稳定且无歧义）
    static string ComputeHash8()
    {
        var names = new List<string>(Map.Keys);
        names.Sort(StringComparer.Ordinal);
        Assembly asm = Assembly.GetExecutingAssembly();
        using (var all = new MemoryStream())
        {
            for (int i = 0; i < names.Count; i++)
            {
                using (Stream s = asm.GetManifestResourceStream(names[i]))
                {
                    if (s == null) continue;
                    using (var bytes = new MemoryStream())
                    {
                        s.CopyTo(bytes);
                        byte[] data = bytes.ToArray();
                        byte[] frame = Encoding.UTF8.GetBytes(names[i] + "\n" + data.Length + "\n");
                        all.Write(frame, 0, frame.Length);
                        all.Write(data, 0, data.Length);
                    }
                }
            }
            all.Position = 0;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(all);
                var sb = new StringBuilder(8);
                for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    // 只清理 7 天以上未更新的同级旧缓存；当前目录与近期缓存永不删除，所有异常忽略
    static void CleanupSiblings(string parent, string keep)
    {
        try
        {
            string keepFull = Path.GetFullPath(keep);
            DateTime cutoff = DateTime.UtcNow.AddDays(-7);
            string[] dirs = Directory.GetDirectories(parent);
            int attempts = 0;
            for (int i = 0; i < dirs.Length && attempts < 6; i++)
            {
                try
                {
                    string candidate = Path.GetFullPath(dirs[i]);
                    if (string.Equals(candidate, keepFull, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.GetLastWriteTimeUtc(candidate) >= cutoff) continue;
                    attempts++;
                    Directory.Delete(candidate, true);
                }
                catch { }
            }
        }
        catch { }
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
