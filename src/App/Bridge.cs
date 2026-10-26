using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

class BridgeMessage
{
    public string Action;
    public Dictionary<string, object> Args = new Dictionary<string, object>();
}

static class Bridge
{
    static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

    public static BridgeMessage Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var d = Ser.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return null;
            object a;
            if (!d.TryGetValue("action", out a)) return null;
            var m = new BridgeMessage();
            m.Action = a as string;
            m.Args = d;
            return string.IsNullOrEmpty(m.Action) ? null : m;
        }
        catch { return null; }
    }

    public static string Serialize(object o)
    {
        try { return Ser.Serialize(o); } catch { return "{}"; }
    }

    public static object Get(Dictionary<string, object> d, string key, object fallback)
    {
        object v;
        return (d != null && d.TryGetValue(key, out v) && v != null) ? v : fallback;
    }

    public static string GetStr(Dictionary<string, object> d, string key, string fallback)
    {
        object v = Get(d, key, null);
        return v == null ? fallback : Convert.ToString(v);
    }

    public static int GetInt(Dictionary<string, object> d, string key, int fallback)
    {
        object v = Get(d, key, null);
        if (v == null) return fallback;
        try { return Convert.ToInt32(v); } catch { return fallback; }
    }

    public static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
    {
        object v = Get(d, key, null);
        if (v == null) return fallback;
        if (v is bool) return (bool)v;
        if (v is int || v is long || v is double)
        {
            try { return Convert.ToInt32(v) != 0; } catch { return fallback; }
        }
        bool b;
        return bool.TryParse(Convert.ToString(v), out b) ? b : fallback;
    }
}
