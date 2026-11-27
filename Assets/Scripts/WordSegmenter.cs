using System.Collections.Generic;
using System.Text;

// v36: 打った文字を語彙の単語列に変換する（案1「打って変換」）。
// 区切りなしの日本語入力を、登録単語との最長一致で左から分割する。
// 変換できなかった文字は unmatched に返し、入力欄に残して直せるようにする。
public static class WordSegmenter
{
    public static List<Dictionary<string, object>> Segment(string input, List<object> words, out string unmatched)
    {
        var byText = new Dictionary<string, Dictionary<string, object>>();
        int maxLen = 0;
        foreach (var o in words)
        {
            var w = J.Dict(o);
            string t = J.GetStr(w, "text");
            if (t.Length > 0 && !byText.ContainsKey(t))
            {
                byText[t] = w;
                if (t.Length > maxLen) maxLen = t.Length;
            }
        }
        // 空白と句読点は区切りとして読み飛ばす
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (c == ' ' || c == '　' || c == '、' || c == '。' || c == '・' || c == '\n' || c == '\r' || c == '\t') continue;
            sb.Append(c);
        }
        string s = sb.ToString();
        var result = new List<Dictionary<string, object>>();
        var bad = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            Dictionary<string, object> hit = null;
            int hitLen = 0;
            for (int len = System.Math.Min(maxLen, s.Length - i); len >= 1; len--)
            {
                Dictionary<string, object> w;
                if (byText.TryGetValue(s.Substring(i, len), out w)) { hit = w; hitLen = len; break; }
            }
            if (hit != null) { result.Add(hit); i += hitLen; }
            else { bad.Append(s[i]); i++; }
        }
        unmatched = bad.ToString();
        return result;
    }
}
