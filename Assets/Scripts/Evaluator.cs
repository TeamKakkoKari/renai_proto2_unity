using System;
using System.Collections.Generic;

// 発言評価ロジック（UIから独立）。Godot版 scripts/evaluator.gd の移植。
// 数値は evaluation.json の scores から読む。
// v8: 好感度は「単語そのもの」より「組み合わせ方（語順・文法・助詞・接続）」で決まる。
// v9: 会話の話題から逸脱した単語と、文体だけの中身のない発言（一言返事）もチェックする。
// tokens: List of Dictionary {text, category, tags(List)}
// returns: {score, reasons(List<string>), polarity, scene_change, confused, violations, offtopic, minimal}
public static class Evaluator
{
    // v10: 助詞を基本セット（は/が/を/に/へ/で/と/も/の/から/まで/や）に拡張
    private static readonly List<string> Particles = new List<string>
    {
        "は", "が", "を", "に", "へ", "で", "と", "も", "の", "から", "まで", "や"
    };

    // どの話題でも自然に使える汎用タグ（話題逸脱の判定から除外する）
    private static readonly List<string> TopicGeneric = new List<string>
    {
        "positive", "negative", "like", "dislike", "you", "me", "want", "see", "move"
    };

    private static bool HasTag(Dictionary<string, object> t, string tag)
    {
        return J.List(J.Get(t, "tags")).Contains(tag);
    }

    private static bool HasTagStr(Dictionary<string, object> t, string tag)
    {
        foreach (var x in J.List(J.Get(t, "tags")))
            if (J.Str(x) == tag) return true;
        return false;
    }

    // 文中での標準的な位置（小さいほど前）。-1は語順チェック対象外（接続詞など柔軟な言葉）
    private static int Rank(Dictionary<string, object> t)
    {
        if (HasTagStr(t, "turn")) return 0;
        string cat = J.GetStr(t, "category");
        switch (cat)
        {
            case "affirm":
            case "negate":
                return 1;
            case "target":
                return 2;
            case "connect":
                if (Particles.Contains(J.GetStr(t, "text"))) return 3;
                return -1;
            case "degree":
                return 4;
            case "emotion":
            case "action":
                return 6;
            case "ending":
                return 7;
        }
        return -1;
    }

    private static string PD(int v)
    {
        // GDScript の "%+d" 相当
        return v >= 0 ? "+" + v : v.ToString();
    }

    private static Dictionary<string, object> Result(int score, List<string> reasons, string polarity,
        bool sceneChange, bool confused, int violations, bool offtopic, bool minimal, bool brief = false, string unknown = "")
    {
        return new Dictionary<string, object>
        {
            { "score", score },
            { "reasons", reasons },
            { "polarity", polarity },
            { "scene_change", sceneChange },
            { "confused", confused },
            { "violations", violations },
            { "offtopic", offtopic },
            { "minimal", minimal },
            { "brief", brief },
            { "unknown", unknown },
        };
    }

    public static Dictionary<string, object> Evaluate(
        List<Dictionary<string, object>> tokens,
        Dictionary<string, object> character,
        Dictionary<string, object> question,
        Dictionary<string, object> cfg)
    {
        var reasons = new List<string>();
        if (tokens.Count == 0)
        {
            return Result(J.GetInt(cfg, "empty_penalty", -10),
                new List<string> { "（無言）……何か言ってあげよう" },
                "none", false, false, 0, false, false);
        }

        var texts = new List<string>();
        var cats = new List<string>();
        foreach (var t in tokens)
        {
            texts.Add(J.GetStr(t, "text"));
            cats.Add(J.GetStr(t, "category"));
        }

        // 特別ルール: 「あなた＋嫌い」= 直接の拒絶（文法以前の問題）
        if (texts.Contains("あなた") && texts.Contains("嫌い"))
        {
            return Result(J.GetInt(cfg, "direct_dislike_you", -15),
                new List<string> { "「あなたは嫌い」は相手を深く傷つける (-15)" },
                "negative", false, false, 0, false, false);
        }

        // v38: 彼女がまだ知らない言葉（free）が含まれているか。
        // 核になる既知の言葉が無ければ、意味不明としてではなく「初めて聞く言葉」として返す
        var freeTexts = new List<string>();
        foreach (var t in tokens) if (J.GetStr(t, "category") == "free") freeTexts.Add(J.GetStr(t, "text"));
        string unknownJoined = string.Join("、", freeTexts);
        if (freeTexts.Count > 0)
        {
            bool knownCore = false;
            foreach (var t in tokens)
            {
                string c = J.GetStr(t, "category");
                if (c == "affirm" || c == "negate" || c == "emotion" || c == "action") { knownCore = true; break; }
            }
            if (!knownCore)
            {
                return Result(0,
                    new List<string> { "初めて聞く言葉だった（意味を教えてあげよう）" },
                    "none", false, false, 0, false, false, false, unknownJoined);
            }
        }

        // 意味をなさない発言: 肯定/否定/感情/動作のどれも無い（名詞だけの羅列など）
        bool hasCore = cats.Contains("affirm") || cats.Contains("negate") || cats.Contains("emotion") || cats.Contains("action");
        if (!hasCore)
        {
            return Result(J.GetInt(cfg, "nonsense_penalty", -10),
                new List<string> { "文章として意味をなしていない (-10)" },
                "none", false, true, 99, false, false);
        }

        // 中身のない発言チェック: 対象・感情・動作がなく、肯定/否定（＋文体）だけの発言は厳しく見る
        bool hasContent = cats.Contains("target") || cats.Contains("emotion") || cats.Contains("action");
        if (!hasContent && (cats.Contains("affirm") || cats.Contains("negate")))
        {
            if (cats.Contains("affirm") && cats.Contains("negate"))
            {
                int hp = J.GetInt(cfg, "hollow_penalty", -6);
                return Result(hp,
                    new List<string> { "肯定と否定だけが並んでいて意味が伝わらない (" + hp + ")" },
                    "none", false, true, 0, false, false);
            }
            string mpol = cats.Contains("affirm") ? "positive" : "negative";
            string mexp = J.GetStr(question, "expected_polarity", "any");
            int mscore = J.GetInt(cfg, "minimal_any", 0);
            if (mexp == "positive" || mexp == "negative")
            {
                mscore = mpol == mexp ? J.GetInt(cfg, "minimal_match", 2) : J.GetInt(cfg, "minimal_mismatch", -4);
            }
            // 短答が質問への立派な答えになる場合（Yes/Noで答えられる質問への肯定）は
            // 落胆ではなく穏やかな受け止め（brief）にする。点数の小ささはそのまま
            bool briefOk = J.GetBool(question, "short_ok") && cats.Contains("affirm") && mexp != "negative";
            if (briefOk)
            {
                return Result(mscore,
                    new List<string> { "短いけれど、質問への立派な答え (" + PD(mscore) + ")" },
                    mpol, false, false, 0, false, false, true);
            }
            return Result(mscore,
                new List<string> { "一言だけの返事で、気持ちはあまり伝わらなかった (" + PD(mscore) + ")" },
                mpol, false, false, 0, false, true);
        }

        // ===== 文法・組み合わせの分析 =====
        var violations = new List<string>();

        // 語順: 標準順に対する逆転を数える
        // v10: 名詞に付いた助詞の直後の名詞は新しい句の始まりとして扱い、逆転に数えない
        //      （「あなたにリンゴをあげる」のような複数句の文を許容する。
        //        文頭など名詞に付いていない助詞の直後は対象外）
        var particleAttached = new bool[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            particleAttached[i] = cats[i] == "connect" && Particles.Contains(texts[i])
                && i > 0 && cats[i - 1] == "target";
        }
        int prevRank = -1;
        string prevCat = "";
        int prevIdx = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            int r = Rank(tokens[i]);
            if (r < 0) continue;
            if (prevRank >= 0 && r < prevRank)
            {
                // 感情→対象 の隣接は「綺麗な森」のような形容として許容する
                bool adjectival = prevCat == "emotion" && cats[i] == "target";
                // 助詞→名詞 は句の切り替わり（「〜にリンゴ」「〜の森」など）
                bool phraseReset = cats[i] == "target" && prevIdx >= 0 && particleAttached[prevIdx];
                if (!adjectival && !phraseReset)
                    violations.Add("「" + texts[i] + "」の位置が不自然");
            }
            prevRank = r;
            prevCat = cats[i];
            prevIdx = i;
        }

        // 語尾の位置と重なり
        int endingCount = 0;
        foreach (var c in cats) if (c == "ending") endingCount++;
        bool hasEnding = endingCount > 0;
        bool endingIsLast = hasEnding && cats[cats.Count - 1] == "ending";
        // 文末に連続する語尾（です＋ね、だ＋よ等）は1つの語尾として扱う。
        // 「重なっている」のは、文の途中にも語尾が現れた場合だけとする
        int trailEndings = 0;
        for (int i = cats.Count - 1; i >= 0 && cats[i] == "ending"; i--) trailEndings++;
        if (endingIsLast && endingCount - trailEndings > 0) violations.Add("語尾が重なっている");
        if (hasEnding && !endingIsLast) violations.Add("語尾は文の最後に置こう");

        // 文頭にあるべき言葉の位置
        int headOk = 0;
        if (tokens.Count > 0 && HasTagStr(tokens[0], "turn")) headOk = 1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if ((cats[i] == "affirm" || cats[i] == "negate") && i > headOk)
                violations.Add("「" + texts[i] + "」は文の最初に置こう");
            if (HasTagStr(tokens[i], "turn") && i > 0)
                violations.Add("転換の言葉「" + texts[i] + "」は文の最初に置こう");
        }

        // 助詞（は/を/と）は名詞の直後に置く
        for (int i = 0; i < tokens.Count; i++)
        {
            if (cats[i] == "connect" && Particles.Contains(texts[i]))
            {
                if (i == 0 || cats[i - 1] != "target")
                    violations.Add("「" + texts[i] + "」の前には名詞が必要");
            }
        }

        // 「一緒に」は動作より前に置く
        int togetherIdx = texts.IndexOf("一緒に");
        int actionIdx = cats.IndexOf("action");
        if (togetherIdx >= 0 && actionIdx >= 0 && togetherIdx > actionIdx)
            violations.Add("「一緒に」は動作の前に置こう");

        // 同じ単語の繰り返し
        var seen = new Dictionary<string, int>();
        foreach (var tx in texts)
        {
            int n;
            seen.TryGetValue(tx, out n);
            seen[tx] = n + 1;
        }
        foreach (var kv in seen)
        {
            if (kv.Value >= 2)
            {
                for (int k = 0; k < kv.Value - 1; k++)
                    violations.Add("同じ単語「" + kv.Key + "」の繰り返し");
            }
        }

        // 同じ種類の単語の羅列（3つ以上連続）
        string runCat = "";
        int runLen = 0;
        for (int i = 0; i < cats.Count; i++)
        {
            if (cats[i] == runCat)
            {
                runLen++;
            }
            else
            {
                if (runLen >= 3) violations.Add("単語の羅列になっている");
                runCat = cats[i];
                runLen = 1;
            }
        }
        if (runLen >= 3) violations.Add("単語の羅列になっている");

        // 肯定/否定と気持ちのちぐはぐ
        bool hasPosEmotion = false;
        foreach (var t in tokens)
        {
            if (J.GetStr(t, "category") == "emotion" && HasTagStr(t, "positive"))
                hasPosEmotion = true;
        }
        bool contradiction = false;
        if (cats.Contains("affirm") && texts.Contains("嫌い")) contradiction = true;
        if (cats.Contains("negate") && hasPosEmotion) contradiction = true;
        if (texts.Contains("好き") && texts.Contains("嫌い")) contradiction = true;
        if (contradiction) violations.Add("肯定・否定と気持ちがちぐはぐ");

        int vcount = violations.Count;
        string quality = "clean";
        if (vcount == 1) quality = "flawed";
        else if (vcount >= 2) quality = "broken";

        // 極性判定（ちぐはぐな場合は伝わらない）
        string polarity = "none";
        if (contradiction) polarity = "mixed";
        else if (cats.Contains("negate") || texts.Contains("嫌い")) polarity = "negative";
        else if (cats.Contains("affirm") || texts.Contains("好き") || texts.Contains("嬉しい")) polarity = "positive";

        // ===== 話題の逸脱チェック =====
        // 会話の文脈 = 質問の話題タグ ＋ 相手の好きな話題タグ
        var contextTags = new HashSet<string>();
        foreach (var tag in J.List(J.Get(question, "topic_tags"))) contextTags.Add(J.Str(tag));
        foreach (var tag in J.List(J.Get(character, "liked_tags"))) contextTags.Add(J.Str(tag));
        int contentCount = 0;
        int topicalCount = 0;
        var offWords = new List<string>();
        foreach (var t in tokens)
        {
            string tcat = J.GetStr(t, "category");
            if (tcat != "target" && tcat != "emotion" && tcat != "action") continue;
            contentCount++;
            int nongeneric = 0;
            bool inContext = false;
            foreach (var tag in J.List(J.Get(t, "tags")))
            {
                string tg = J.Str(tag);
                if (!TopicGeneric.Contains(tg)) nongeneric++;
                if (contextTags.Contains(tg)) inContext = true;
            }
            if (nongeneric == 0) continue; // 気持ちだけを表す言葉はどの話題でも自然
            if (inContext) topicalCount++;
            else offWords.Add(J.GetStr(t, "text"));
        }
        int offCount = offWords.Count;
        // 話題に沿った中身が無い、または半分以上が逸脱していたら「話が噛み合っていない」
        bool isOfftopic = offCount > 0 && contentCount > 0 && (topicalCount == 0 || offCount * 2 > contentCount);

        // ===== ここから加点・減点 =====
        int score = 0;

        // 文法: 語尾まで言えたか
        if (endingIsLast)
        {
            int v = J.GetInt(cfg, "grammar_ending_bonus", 2);
            score += v;
            reasons.Add("語尾まで言えた (" + PD(v) + ")");
        }
        else if (!hasEnding)
        {
            int v = J.GetInt(cfg, "incomplete_penalty", -3);
            score += v;
            reasons.Add("語尾がなくてぶっきらぼう (" + v + ")");
        }

        // 構造: 対象＋感情/動作 がそろった文
        if (cats.Contains("target") && (cats.Contains("emotion") || cats.Contains("action")))
        {
            int v = J.GetInt(cfg, "grammar_structure_bonus", 3);
            score += v;
            reasons.Add("対象と気持ち/動作がそろった文章 (" + PD(v) + ")");
        }

        // 程度→感情 の正しい組み合わせ
        int degreeIdx = cats.IndexOf("degree");
        int emotionIdx = cats.IndexOf("emotion");
        if (degreeIdx >= 0 && emotionIdx >= 0 && degreeIdx < emotionIdx)
        {
            int v = J.GetInt(cfg, "degree_emotion_bonus", 1);
            score += v;
            reasons.Add("程度表現で気持ちが伝わった (" + PD(v) + ")");
        }

        // 一緒に→動作 の正しい組み合わせ
        if (togetherIdx >= 0 && actionIdx >= 0 && togetherIdx < actionIdx)
        {
            int v = J.GetInt(cfg, "together_bonus", 5);
            score += v;
            reasons.Add("「一緒に〜したい」が伝わった (" + PD(v) + ")");
        }

        // 質問への極性の適合（文が整っているほど伝わる）
        string expected = J.GetStr(question, "expected_polarity", "any");
        if (expected == "positive" || expected == "negative")
        {
            if (polarity == expected)
            {
                int pm = J.GetInt(cfg, "polarity_match", 5);
                if (quality == "flawed") pm = J.GetInt(cfg, "polarity_match_flawed", 3);
                else if (quality == "broken") pm = J.GetInt(cfg, "polarity_match_broken", 1);
                score += pm;
                reasons.Add("質問に合った答え方 (" + PD(pm) + ")");
            }
            else if (polarity == "positive" || polarity == "negative")
            {
                int v = J.GetInt(cfg, "polarity_mismatch", -5);
                score += v;
                reasons.Add("質問と答えの向きが逆 (" + v + ")");
            }
        }

        // 好きな話題（単語そのものの加点は小さく、文が整っているときにだけ効く）
        var likedTags = J.StrList(J.Get(character, "liked_tags"));
        var likedWords = J.StrList(J.Get(character, "liked_words"));
        int topicBonus = 0;
        foreach (var t in tokens)
        {
            bool hit = false;
            foreach (var tag in J.List(J.Get(t, "tags")))
                if (likedTags.Contains(J.Str(tag))) hit = true;
            if (likedWords.Contains(J.GetStr(t, "text"))) hit = true;
            if (hit) topicBonus += J.GetInt(cfg, "liked_topic_per_word", 3);
        }
        topicBonus = Math.Min(topicBonus, J.GetInt(cfg, "liked_topic_cap", 6));
        if (quality == "flawed") topicBonus = topicBonus / 2;
        else if (quality == "broken") topicBonus = 0;
        if (topicBonus > 0)
        {
            score += topicBonus;
            reasons.Add(J.GetStr(character, "name", "相手") + "の好きな話題 (" + PD(topicBonus) + ")");
        }

        // 話題から逸脱した単語
        if (offCount > 0)
        {
            int op = J.GetInt(cfg, "offtopic_per_word", -2);
            score += op * offCount;
            foreach (var ow in offWords)
                reasons.Add("話題とずれた単語「" + ow + "」(" + op + ")");
        }

        // 嫌いな言葉
        var disliked = J.StrList(J.Get(character, "disliked_words"));
        foreach (var tx in texts)
        {
            if (disliked.Contains(tx) && tx != "嫌い")
            {
                int v = J.GetInt(cfg, "disliked_topic_per_word", -5);
                score += v;
                reasons.Add("苦手な話題「" + tx + "」(" + v + ")");
            }
        }
        if (texts.Contains("嫌い") && !texts.Contains("あなた"))
        {
            int v = J.GetInt(cfg, "disliked_topic_per_word", -5);
            score += v;
            reasons.Add("「嫌い」という言葉が刺さった (" + v + ")");
        }

        // 転換の言葉は文頭で使うと場面が変わる
        bool sceneChange = false;
        if (tokens.Count > 0 && HasTagStr(tokens[0], "turn"))
        {
            sceneChange = true;
            int v = J.GetInt(cfg, "scene_change_bonus", 2);
            score += v;
            reasons.Add("転換の言葉で場の空気が変わった (" + PD(v) + ")");
        }

        // 性格: 好みの語尾（文の最後にあるときだけ効く）
        var pref = J.StrList(J.Get(character, "preferred_endings"));
        if (endingIsLast && pref.Count > 0)
        {
            // 文末の語尾列を連結して判定する（「です」＋「ね」で好みの「ですね」等）
            var tailWords = new List<string>();
            for (int i = texts.Count - 1; i >= 0 && cats[i] == "ending"; i--) tailWords.Insert(0, texts[i]);
            string joinedEnding = string.Join("", tailWords);
            if (pref.Contains(joinedEnding) || pref.Contains(texts[texts.Count - 1]))
            {
                int v = J.GetInt(cfg, "personality_ending_match", 2);
                score += v;
                reasons.Add("この子の好みの話し方 (" + PD(v) + ")");
            }
        }

        // 文法違反の減点
        if (vcount > 0)
        {
            int vp = J.GetInt(cfg, "violation_penalty", -2);
            score += vp * vcount;
            foreach (var vn in violations)
                reasons.Add(vn + " (" + vp + ")");
        }

        // 文が崩れていると、好きな単語を並べても意味が伝わらない
        bool confused = false;
        if (quality == "broken")
        {
            confused = true;
            int v = J.GetInt(cfg, "broken_penalty", -3);
            score += v;
            reasons.Add("文が崩れていて意味が伝わらなかった (" + v + ")");
            if (score > -1) score = -1;
        }

        // 話が噛み合っていない発言は、文法が正しくても好感度は上がらない
        if (isOfftopic)
        {
            reasons.Add("今の話題と関係のない話になってしまった");
            if (score > -1) score = -1;
        }

        return Result(score, reasons, polarity, sceneChange, confused, vcount, isOfftopic, false, false, unknownJoined);
    }

    public static string ReactionKey(int score)
    {
        if (score >= 10) return "great";
        if (score >= 1) return "good";
        if (score >= -9) return "bad";
        return "terrible";
    }

    public static Dictionary<string, object> AffectionLevel(int affection, List<object> levels)
    {
        var cur = levels.Count > 0 ? J.Dict(levels[0]) : new Dictionary<string, object> { { "name", "知り合い" }, { "min", 0.0 } };
        foreach (var lv in levels)
        {
            var d = J.Dict(lv);
            if (affection >= J.GetInt(d, "min", 0)) cur = d;
        }
        return cur;
    }
}
