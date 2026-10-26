using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 触发日志：纯内存环形缓冲，仅本次运行保留（不落盘、关窗不清空也不写出）
class HistoryEntry
{
    public DateTime At;
    public string Keys;
    public string Proc;
    public string Mode;
}

class HistoryRing
{
    const int Capacity = 1000;
    readonly List<HistoryEntry> _items = new List<HistoryEntry>();

    // 记录顺序为时间正序；超过容量时从最旧一条开始丢弃
    public void Add(string keys, string proc, string mode, DateTime at)
    {
        _items.Add(new HistoryEntry { Keys = keys, Proc = proc, Mode = mode, At = at });
        if (_items.Count > Capacity) _items.RemoveRange(0, _items.Count - Capacity);
    }

    public void Clear() { _items.Clear(); }

    // 最新在前，供前端直接渲染
    public List<Dictionary<string, object>> Snapshot()
    {
        var list = new List<Dictionary<string, object>>();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var e = _items[i];
            list.Add(new Dictionary<string, object>
            {
                { "time", e.At.ToString("HH:mm:ss") },
                { "date", e.At.ToString("MM-dd") },
                { "keys", e.Keys },
                { "proc", e.Proc },
                { "mode", e.Mode }
            });
        }
        return list;
    }

    // UTF-8 BOM 让 Excel 正确识别中文；字段按 RFC 4180 加引号与双写引号
    public int ExportCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("时间,键位,进程,模式");
        for (int i = 0; i < _items.Count; i++)
        {
            var e = _items[i];
            sb.AppendLine(e.At.ToString("yyyy-MM-dd HH:mm:ss") + "," +
                Csv(e.Keys) + "," + Csv(e.Proc) + "," + Csv(e.Mode));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return _items.Count;
    }

    static string Csv(string v)
    {
        if (v == null) return "";
        if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 ||
            v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
