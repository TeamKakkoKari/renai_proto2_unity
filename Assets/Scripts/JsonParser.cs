using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Minimal JSON parser producing Dictionary<string,object> / List<object> / string / double / bool / null.
// Self-contained so the project needs no external packages.
public static class JsonParser
{
    public static object Parse(string text)
    {
        var p = new Parser(text ?? "");
        p.SkipWs();
        return p.ParseValue();
    }

    private sealed class Parser
    {
        private readonly string s;
        private int i;

        public Parser(string s) { this.s = s; }

        public void SkipWs()
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private char Peek() { return i < s.Length ? s[i] : '\0'; }

        public object ParseValue()
        {
            SkipWs();
            char c = Peek();
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return ParseString();
            if (c == 't') { Expect("true"); return true; }
            if (c == 'f') { Expect("false"); return false; }
            if (c == 'n') { Expect("null"); return null; }
            return ParseNumber();
        }

        private void Expect(string lit)
        {
            if (string.Compare(s, i, lit, 0, lit.Length, StringComparison.Ordinal) != 0)
                throw new FormatException("Invalid JSON literal at " + i);
            i += lit.Length;
        }

        private Dictionary<string, object> ParseObject()
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs();
            if (Peek() == '}') { i++; return d; }
            while (true)
            {
                SkipWs();
                string key = ParseString();
                SkipWs();
                if (Peek() == ':') i++;
                d[key] = ParseValue();
                SkipWs();
                if (Peek() == ',') { i++; continue; }
                if (Peek() == '}') { i++; break; }
                break;
            }
            return d;
        }

        private List<object> ParseArray()
        {
            var list = new List<object>();
            i++; // [
            SkipWs();
            if (Peek() == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue());
                SkipWs();
                if (Peek() == ',') { i++; continue; }
                if (Peek() == ']') { i++; break; }
                break;
            }
            return list;
        }

        private string ParseString()
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private double ParseNumber()
        {
            int start = i;
            while (i < s.Length && "-+0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double v;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return 0.0;
            return v;
        }
    }
}

// Access helpers mirroring GDScript Dictionary access patterns (data.get("key", default)).
public static class J
{
    public static Dictionary<string, object> Dict(object o)
    {
        return o as Dictionary<string, object> ?? new Dictionary<string, object>();
    }

    public static List<object> List(object o)
    {
        return o as List<object> ?? new List<object>();
    }

    public static object Get(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) ? v : null;
    }

    public static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
    {
        return Dict(Get(d, key));
    }

    public static List<object> GetList(Dictionary<string, object> d, string key)
    {
        return List(Get(d, key));
    }

    public static string GetStr(Dictionary<string, object> d, string key, string def = "")
    {
        var v = Get(d, key);
        return v is string ? (string)v : def;
    }

    public static int GetInt(Dictionary<string, object> d, string key, int def = 0)
    {
        var v = Get(d, key);
        if (v is int) return (int)v;
        if (v is long) return (int)(long)v;
        if (v is double) return (int)(double)v;
        if (v is float) return (int)(float)v;
        if (v is string)
        {
            int n;
            if (int.TryParse((string)v, out n)) return n;
        }
        return def;
    }

    public static bool GetBool(Dictionary<string, object> d, string key, bool def = false)
    {
        var v = Get(d, key);
        return v is bool ? (bool)v : def;
    }

    public static string Str(object v, string def = "")
    {
        return v is string ? (string)v : def;
    }

    public static List<string> StrList(object o)
    {
        var r = new List<string>();
        foreach (var x in List(o)) r.Add(Str(x));
        return r;
    }
}
