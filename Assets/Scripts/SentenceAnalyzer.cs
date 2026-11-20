using System;
using System.Collections.Generic;

// v10: 助詞・接続語による日本語文章解析システム。
// Evaluator（好感度スコア）とは独立に、単語同士の関係・解釈候補・理解度を判定する。
//
// 設計方針:
// - 助詞によって単語同士の関係（話題・主語・対象・共同・所有など）を構築する
// - 同じ単語でも助詞が違えば解釈が変わる（あなたは好き / あなたが好き / あなたも好き …）
// - 複数の解釈が可能な文章は候補を複数持ち、好感度とキャラクターの文脈で採用を決める
// - 理解度（Lv1〜4）と好感度は直接結びつけない。不自然でも面白い文、自然でも傷つける文がある
// - 解析の補正（modifier）は解釈の「内容」に対してのみ小さく付く
public static class SentenceAnalyzer
{
    public class Analysis
    {
        public int understanding = 1; // 1 自然 / 2 多少不自然 / 3 複数解釈が可能 / 4 理解が困難
        public List<string> relations = new List<string>();
        public List<string> interpretations = new List<string>();
        public string chosenKey = "";
        public string chosenText = "";
        public int modifier = 0;
        public bool absurd = false;       // 現実には難しい関係（笑い・困惑寄りの反応）
        public bool questionForm = false; // 質問の形（か/ですか）
        public bool requestForm = false;  // 依頼の形（ください）
        public bool clarify = false;      // 聞き返しが自然（未確定の曖昧さ・理解困難）
        public List<string> notes = new List<string>();

        public string Summary()
        {
            var parts = new List<string>();
            if (relations.Count > 0) parts.Add("関係: " + string.Join("、", relations));
            if (interpretations.Count > 0) parts.Add("解釈: " + string.Join(" / ", interpretations));
            if (chosenText != "") parts.Add("採用: " + chosenText);
            parts.Add("理解度Lv" + understanding);
            if (absurd) parts.Add("現実には難しい関係");
            if (questionForm) parts.Add("質問の形");
            if (requestForm) parts.Add("依頼の形");
            foreach (var n in notes) parts.Add(n);
            if (modifier != 0) parts.Add("解析補正 " + (modifier >= 0 ? "+" + modifier : modifier.ToString()));
            return string.Join(" ｜ ", parts);
        }
    }

    private class Phrase
    {
        public string noun = "";
        public string particle = ""; // 直後の助詞（なければ ""）
        public int index = 0;
    }

    private class Interp
    {
        public string key = "";
        public string text = "";
        public bool dominant = false;
        public Interp(string k, string t, bool d) { key = k; text = t; dominant = d; }
    }

    private static readonly Dictionary<string, string> RoleNames = new Dictionary<string, string>
    {
        { "は", "話題" }, { "が", "主語" }, { "を", "動作の対象" }, { "に", "受け手・方向" },
        { "と", "共同する相手" }, { "も", "〜も（追加）" }, { "の", "所有・関係" },
        { "で", "場所・手段" }, { "へ", "目的地・方向" }, { "から", "起点" }, { "まで", "終点" }, { "や", "並列" },
    };

    private static bool IsParticle(string text)
    {
        return RoleNames.ContainsKey(text);
    }

    private static bool IsPerson(string noun)
    {
        return noun == "あなた" || noun == "私";
    }

    public static Analysis Analyze(
        List<Dictionary<string, object>> tokens,
        Dictionary<string, object> character,
        Dictionary<string, object> question,
        Dictionary<string, object> cfg,
        int affection,
        Dictionary<string, object> evalRes)
    {
        var a = new Analysis();
        if (tokens.Count == 0)
        {
            a.understanding = 4;
            return a;
        }
        var texts = new List<string>();
        var cats = new List<string>();
        foreach (var t in tokens)
        {
            texts.Add(J.GetStr(t, "text"));
            cats.Add(J.GetStr(t, "category"));
        }

        // 「あなた＋嫌い」はEvaluatorの特別ルールで処理済み（二重に罰しない）
        if (texts.Contains("あなた") && texts.Contains("嫌い"))
        {
            a.notes.Add("直接の拒絶として受け取った");
            return a;
        }

        int evalViolations = J.GetInt(evalRes, "violations");
        bool evalConfused = J.GetBool(evalRes, "confused");
        bool evalOfftopic = J.GetBool(evalRes, "offtopic");

        // ===== 第1・2段階: 助詞の役割と単語同士の関係 =====
        var phrases = new List<Phrase>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (cats[i] != "target") continue;
            var ph = new Phrase { noun = texts[i], index = i };
            if (i + 1 < tokens.Count && cats[i + 1] == "connect" && IsParticle(texts[i + 1]))
                ph.particle = texts[i + 1];
            phrases.Add(ph);
        }
        foreach (var ph in phrases)
        {
            if (ph.particle != "")
                a.relations.Add(ph.noun + "（" + RoleNames[ph.particle] + "）");
        }
        // 所有の連なり（AのB）
        for (int i = 0; i + 1 < phrases.Count; i++)
        {
            if (phrases[i].particle == "の")
                a.notes.Add("所有: " + phrases[i].noun + "の" + phrases[i + 1].noun);
        }

        // 述語と文末表現
        string lastEmotion = "";
        string lastAction = "";
        List<string> lastActionTags = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (cats[i] == "emotion") lastEmotion = texts[i];
            if (cats[i] == "action")
            {
                lastAction = texts[i];
                lastActionTags = J.StrList(J.Get(tokens[i], "tags"));
            }
        }
        if (cats[cats.Count - 1] == "ending")
        {
            var endTok = tokens[tokens.Count - 1];
            var endTags = J.StrList(J.Get(endTok, "tags"));
            if (endTags.Contains("question")) a.questionForm = true;
            if (endTags.Contains("request")) a.requestForm = true;
        }

        Phrase FindPhrase(string noun, string particle)
        {
            foreach (var ph in phrases)
                if (ph.noun == noun && ph.particle == particle) return ph;
            return null;
        }
        Phrase FirstPhraseWith(string particle)
        {
            foreach (var ph in phrases)
                if (ph.particle == particle) return ph;
            return null;
        }

        var interps = new List<Interp>();
        bool incompletePartner = false;
        // あなた以外の内容名詞が好悪の対象になっているか（「あなたはリンゴが好き」の判定用）
        bool otherObjectPhrase = false;
        foreach (var ph in phrases)
        {
            if (IsPerson(ph.noun) || ph.noun == "こと") continue;
            if (ph.particle == "は" || ph.particle == "が" || ph.particle == "を" || ph.particle == "も")
                otherObjectPhrase = true;
        }

        // ===== 第3段階: 解釈候補の生成（好悪） =====
        if (lastEmotion == "好き")
        {
            var youTopic = FindPhrase("あなた", "は");
            var youSubject = FindPhrase("あなた", "が");
            var youAlso = FindPhrase("あなた", "も");
            var youPartner = FindPhrase("あなた", "と");
            var youPoss = FindPhrase("あなた", "の");
            var youObject = FindPhrase("あなた", "を");
            // 別の対象が明示されている場合、「あなたは」は聞き手の話題提示として扱う
            if (youTopic != null && !otherObjectPhrase)
                interps.Add(new Interp("like_you", "（私が）あなたのことが好き", true));
            if (youSubject != null)
            {
                interps.Add(new Interp("like_you", "（私が）あなたが好き（強調）", false));
                interps.Add(new Interp("you_like", "あなたが（誰かを）好き", false));
            }
            if (youAlso != null)
            {
                interps.Add(new Interp("like_you", "（私も）あなたのことが好き", false));
                interps.Add(new Interp("you_like", "あなたも（誰かを）好き", false));
            }
            if (youPartner != null && lastAction == "")
            {
                interps.Add(new Interp("with_you", "あなたと一緒に（何かを）したい？", false));
                incompletePartner = true;
            }
            if (youPoss != null)
            {
                bool hasKoto = false;
                foreach (var ph in phrases) if (ph.noun == "こと") hasKoto = true;
                if (hasKoto)
                    interps.Add(new Interp("like_you", "あなたのことが好き", true));
                else if (phrases[phrases.Count - 1] == youPoss)
                {
                    interps.Add(new Interp("your_favorite", "あなたの好きなもの", false));
                    interps.Add(new Interp("like_you", "あなたのことが好き", false));
                }
            }
            if (youObject != null)
                interps.Add(new Interp("like_you", "あなたを好き", true));
            // あなた以外の対象への好意（「こと」のような形式名詞は対象にしない）
            foreach (var ph in phrases)
            {
                if (IsPerson(ph.noun) || ph.noun == "こと") continue;
                if (ph.particle == "は" || ph.particle == "が" || ph.particle == "を" || ph.particle == "も")
                {
                    string t = ph.particle == "も" ? ph.noun + "も好き" : ph.noun + "が好き";
                    interps.Add(new Interp("like_object", t, true));
                    break;
                }
            }
            if (interps.Count == 0)
                interps.Add(new Interp("like_general", "何かが好き", true));
        }
        else if (lastEmotion == "嫌い")
        {
            foreach (var ph in phrases)
            {
                if (IsPerson(ph.noun)) continue;
                if (ph.particle == "は" || ph.particle == "が" || ph.particle == "を" || ph.particle == "も")
                {
                    interps.Add(new Interp("dislike_object", ph.noun + "が嫌い", true));
                    break;
                }
            }
            if (interps.Count == 0)
                interps.Add(new Interp("dislike_general", "何かが嫌い", true));
        }

        // ===== 解釈候補の生成（動作） =====
        var partner = FirstPhraseWith("と");
        var objectPh = FirstPhraseWith("を");
        var recipient = FirstPhraseWith("に");
        var destination = FirstPhraseWith("へ");
        if (destination == null) destination = FirstPhraseWith("に");
        bool moveAction = lastActionTags != null && lastActionTags.Contains("move");
        if (partner != null && partner.noun == "あなた" && moveAction)
            interps.Add(new Interp("go_together", "あなたと一緒に" + (destination != null ? destination.noun + "へ" : "") + lastAction, true));
        if (partner != null && partner.noun == "あなた" && objectPh != null && lastAction != "" && !moveAction)
            interps.Add(new Interp("do_together", "あなたと" + objectPh.noun + "を" + lastAction, true));
        if (recipient != null && objectPh != null && lastAction == "あげる")
            interps.Add(new Interp("give", recipient.noun + "に" + objectPh.noun + "をあげる", true));
        if (objectPh != null && lastAction != "" && partner == null && recipient == null)
        {
            // 所有の修飾が付いた対象（あなたのリンゴを食べたい 等）
            string objText = objectPh.noun;
            foreach (var ph in phrases)
            {
                if (ph.particle == "の" && ph.index < objectPh.index)
                    objText = ph.noun + "の" + objText;
            }
            interps.Add(new Interp("do_object", objText + "を" + lastAction, true));
        }

        // 結婚のような関係の妥当性（人でない相手との結婚は現実には難しい）
        if (texts.Contains("結婚"))
        {
            foreach (var ph in phrases)
            {
                if ((ph.particle == "と" || ph.particle == "は" || ph.particle == "が") && !IsPerson(ph.noun))
                {
                    a.absurd = true;
                    interps.Add(new Interp("marry_absurd", ph.noun + "と結婚する（現実には難しい関係）", true));
                }
                else if (ph.particle == "と" && IsPerson(ph.noun))
                {
                    interps.Add(new Interp("marry", ph.noun + "と結婚する", true));
                }
            }
        }

        // 質問の形: 好みを尋ねている場合は解釈候補に加える
        if (a.questionForm && lastEmotion == "好き")
            interps.Add(new Interp("ask_preference", "相手の好みを尋ねる質問", false));

        foreach (var it in interps) a.interpretations.Add(it.text);

        // ===== 第4段階: 複数解釈の解決（会話の文脈とキャラクターとの関係で選ぶ） =====
        Interp FindInterp(string key)
        {
            foreach (var it in interps) if (it.key == key) return it;
            return null;
        }
        bool ambiguous = false;
        var likeYou = FindInterp("like_you");
        var youLike = FindInterp("you_like");
        var likeObject = FindInterp("like_object");
        if (likeYou != null && youLike != null)
        {
            if (likeObject != null)
            {
                // 「あなたもリンゴが好き」のように対象が明示されていれば、主語解釈で確定する
                a.chosenKey = "you_like_object";
                a.chosenText = "あなたも" + likeObject.text.Replace("が好き", "") + "が好き";
                likeObject.dominant = true;
            }
            else
            {
                ambiguous = true;
                // 親密度が高い相手の言葉は、好意的に解釈されやすい（ただし常にではない）
                if (affection >= 30)
                {
                    a.chosenKey = likeYou.key;
                    a.chosenText = likeYou.text;
                    a.notes.Add("親密度が高いため、好意的な解釈を選んだ");
                }
                else
                {
                    a.notes.Add("解釈が割れるため、決めつけなかった");
                }
            }
        }
        // 質問の形で対象が明示されていれば、「相手の好みを尋ねる質問」として読む
        if (a.questionForm && a.chosenKey == "")
        {
            var ap = FindInterp("ask_preference");
            if (ap != null && FindInterp("like_object") != null)
            {
                a.chosenKey = ap.key;
                a.chosenText = ap.text;
            }
        }
        if (a.chosenKey == "")
        {
            foreach (var it in interps)
            {
                if (it.dominant)
                {
                    a.chosenKey = it.key;
                    a.chosenText = it.text;
                    break;
                }
            }
        }
        // あなたの好き（所有で終わる形）は「好きなもの」解釈と割れる
        if (FindInterp("your_favorite") != null && FindInterp("like_you") != null && !ambiguous && a.chosenKey == "")
        {
            ambiguous = true;
            if (affection >= 30)
            {
                var ly = FindInterp("like_you");
                a.chosenKey = ly.key;
                a.chosenText = ly.text;
                a.notes.Add("親密度が高いため、好意的な解釈を選んだ");
            }
        }
        else if (FindInterp("your_favorite") != null && a.chosenKey == "like_you")
        {
            ambiguous = true;
        }

        // ===== 評価の分離: 補正は解釈の「内容」にだけ小さく付ける =====
        if (a.chosenKey == "like_you")
            a.modifier += J.GetInt(cfg, "interp_like_bonus", 2);
        if (a.chosenKey == "go_together")
            a.modifier += J.GetInt(cfg, "partner_bonus", 2);
        if (a.questionForm && !evalOfftopic)
            a.modifier += J.GetInt(cfg, "question_bonus", 1);
        if (a.absurd)
            a.modifier += J.GetInt(cfg, "absurd_penalty", -1);

        // ===== 理解度（Lv1〜4）: 好感度とは独立に判定する =====
        bool anyParticle = false;
        int barePhrases = 0;
        foreach (var ph in phrases)
        {
            if (ph.particle != "") anyParticle = true;
            else barePhrases++;
        }
        bool salad = phrases.Count >= 3 && !anyParticle;
        if (evalViolations == 99 || evalViolations >= 2)
            a.understanding = 4;
        else if (salad)
            a.understanding = 4;
        else if (ambiguous)
            a.understanding = 3;
        else if (incompletePartner || evalViolations == 1)
            a.understanding = 2;
        else
            a.understanding = 1;

        // 聞き返しが自然なケース: 未確定の曖昧さ / 共同の相手だけで述語が無い / 解析側の理解困難
        a.clarify = (ambiguous && a.chosenKey == "")
            || incompletePartner
            || (a.understanding == 4 && !evalConfused);

        return a;
    }
}
