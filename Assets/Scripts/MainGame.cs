using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// ことばを紡ぐ恋愛シミュレーション（プロトタイプ2）Unity移植版。
// Godot版 main.gd の移植。全画面ノベルUI: 立ち絵(中央・全高) + 全幅セリフ枠。
// 選択フェーズではセリフ枠が上に広がって選択欄に変形し、単語が散布される。
public class MainGame : MonoBehaviour
{
    private Dictionary<string, object> dataWords = new Dictionary<string, object>();
    private Dictionary<string, Dictionary<string, object>> wordsByText = new Dictionary<string, Dictionary<string, object>>();
    private List<Dictionary<string, object>> characters = new List<Dictionary<string, object>>();
    private List<Dictionary<string, object>> stages = new List<Dictionary<string, object>>();
    private Dictionary<string, object> stories = new Dictionary<string, object>();
    private Dictionary<string, object> evalCfg = new Dictionary<string, object>();
    private List<object> affectionLevels = new List<object>();
    private float timeLimit = 60f;

    private Dictionary<string, object> currentChar = new Dictionary<string, object>();
    private Dictionary<string, object> currentStage = new Dictionary<string, object>();
    private Dictionary<string, int> affection = new Dictionary<string, int>();
    private Dictionary<string, int> fun = new Dictionary<string, int>();
    private Dictionary<string, int> trust = new Dictionary<string, int>();
    private readonly HashSet<string> favoriteWords = new HashSet<string>();
    private List<Dictionary<string, object>> selected = new List<Dictionary<string, object>>();
    private int selectedChip = -1;
    private int turnCount = 0;
    private List<object> beats = new List<object>();
    private int storyPos = 0;
    private string phase = "story"; // story | ask | reaction
    private Dictionary<string, object> activeQuestion = new Dictionary<string, object>();
    private float timeLeft = 0f;
    private List<string[]> backlog = new List<string[]>();
    private bool typing = false;
    private float typePos = 0f;
    private const float TypeSpeed = 30f;
    private string fullText = "";
    private bool pendingTurn = false;
    private string screen = "title"; // title | map | game
    private System.Random rng = new System.Random();

    private Font jpFont;
    private RectTransform root;
    // シーンに生成済みのUIを使う間は、手動編集を尊重してレイアウトを上書きしない
    private bool preserveLayout;
    private Image bgSky;
    private Image bgGround;
    private RawImage bgArt;
    private RectTransform portraitBox;
    private RawImage portraitImg;
    // 瞬きアニメーション（characters.json の blink にフレームがあるキャラ・表情だけ動く）
    private System.Collections.Generic.List<Texture2D> blinkFrames;
    private float blinkTimer;
    private float blinkWait = 2f;
    private int blinkIndex = -1; // -1 = 目を開けて待機中
    private Text nameLabel;
    private Text dialogueLabel;
    private Text personalityLabel;
    private Button nextButton;
    private Text affectionLabel;
    private Text funLabel;
    private RectTransform affFillRt;
    private RectTransform funFillRt;
    private Text evalAffLabel;
    private Text evalFunLabel;
    private Text evalTrustLabel;
    private RawImage evalMini;
    private string searchText = "";
    private RectTransform msgPanel;
    private GameObject askGroup;
    private Text timerLabel;
    private Image timerBg;
    private Image timerStripImg;
    private RectTransform timerFillRt;
    private float timerBarW = 944f;
    private Image timerFill;
    private Text sentenceLabel;
    private Image bubblePanel;
    private Image sentenceBgImg;
    private Image sayPanelImg;
    private RectTransform chipsGroup;
    private RectTransform tabsArea;
    private RectTransform paletteArea;
    private int palettePage = 0;
    private Text palettePageLabel;
    private string activeCategory = "";
    private readonly Dictionary<string, List<Dictionary<string, object>>> paletteGroups =
        new Dictionary<string, List<Dictionary<string, object>>>();
    private readonly List<KeyValuePair<string, Button>> tabButtons = new List<KeyValuePair<string, Button>>();
    private readonly List<KeyValuePair<string, Button>> paletteButtons = new List<KeyValuePair<string, Button>>();
    private readonly List<string> paletteCatOrder = new List<string>();
    private Text resultLabel;
    private Button speakButton;
    private GameObject backlogPanel;
    private RectTransform backlogContent;
    private GameObject titleScreen;
    private GameObject mapScreen;
    private Text mapInfoLabel;
    private Coroutine portraitAnim;
    private Sprite roundedSprite;
    private Sprite buttonSprite;
    private Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

    private static readonly Dictionary<string, float[]> ZonePos = new Dictionary<string, float[]>
    {
        { "forest", new float[] { -42f, 26f } },
        { "mine", new float[] { 42f, 26f } },
        { "mystic_forest", new float[] { -42f, -24f } },
        { "city", new float[] { 42f, -24f } },
        { "savanna", new float[] { 0f, -32f } },
    };
    private const float MapOrtho = 118f;
    private const float PortraitY = 60f; // v25: 上部帯(60px)分だけ立ち絵を下げる。シーンUIでは再生成時に反映

    private string SavePath { get { return Path.Combine(Application.persistentDataPath, "affection.json"); } }

    // ---------- 起動・データ ----------
    private void Awake()
    {
        EnsureEventSystem();
        root = FindSceneRoot();
        // シーンに生成済みのUIがあれば再利用し、その間はレイアウトを上書きしない
        preserveLayout = root != null;
        if (root == null) root = CreateCanvasRoot();
        BuildAll();
        preserveLayout = false;
    }

    // エディタの「RenaiProto2 > UIをシーンに生成」から呼ばれる。UI一式をシーン上に実体化する
    public void BuildUiForEditor()
    {
        EnsureEventSystem();
        root = FindSceneRoot();
        if (root == null) root = CreateCanvasRoot();
        preserveLayout = false;
        BuildAll();
    }

    // エディタ生成時に混ざった動的部品（単語ボタン等）をシーンから取り除く
    public void ClearDynamicChildrenForEditor()
    {
        ClearChildrenImmediate(paletteArea);
        ClearChildrenImmediate(tabsArea);
        ClearChildrenImmediate(chipsGroup);
        ClearChildrenImmediate(backlogContent);
    }

    private void ClearChildrenImmediate(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--)
            DestroyImmediate(t.GetChild(i).gameObject);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private RectTransform FindSceneRoot()
    {
        var c = GameObject.Find("Canvas");
        if (c == null) return null;
        var r = c.transform.Find("Root");
        return r != null ? r.GetComponent<RectTransform>() : null;
    }

    private RectTransform CreateCanvasRoot()
    {
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var rt = new GameObject("Root").AddComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private void BuildAll()
    {
        jpFont = Resources.Load<Font>("fonts/MPLUSRounded1c-Bold");
        LoadData();
        LoadAffection();
        BuildUi();
        if (characters.Count > 0)
            SelectStage(J.GetStr(characters[0], "stage_id"));
        ShowTitle();
    }

    private Dictionary<string, object> LoadJson(string resPath)
    {
        var ta = Resources.Load<TextAsset>(resPath);
        if (ta == null)
        {
            Debug.LogError("Cannot load " + resPath);
            return new Dictionary<string, object>();
        }
        return J.Dict(JsonParser.Parse(ta.text));
    }

    private void LoadData()
    {
        dataWords = LoadJson("data/words");
        var chars = LoadJson("data/characters");
        var stgs = LoadJson("data/stages");
        var eval = LoadJson("data/evaluation");
        var strs = LoadJson("data/stories");
        foreach (var c in J.GetList(chars, "characters")) characters.Add(J.Dict(c));
        foreach (var s in J.GetList(stgs, "stages")) stages.Add(J.Dict(s));
        stories = J.GetDict(strs, "stories");
        evalCfg = J.GetDict(eval, "scores");
        affectionLevels = J.GetList(eval, "affection_levels");
        var tl = J.Get(eval, "time_limit_seconds");
        if (tl is double) timeLimit = (float)(double)tl;
        foreach (var w in J.GetList(dataWords, "words"))
        {
            var d = J.Dict(w);
            wordsByText[J.GetStr(d, "text")] = d;
        }
    }

    private void LoadAffection()
    {
        foreach (var c in characters)
        {
            string id = J.GetStr(c, "id");
            affection[id] = 0;
            fun[id] = 0;
            trust[id] = 0;
        }
        if (File.Exists(SavePath))
        {
            try
            {
                var root = J.Dict(JsonParser.Parse(File.ReadAllText(SavePath)));
                var aff = root;
                if (root.ContainsKey("affection"))
                {
                    // v13形式: 好感度・面白さ・お気に入り単語をまとめて保存
                    aff = J.GetDict(root, "affection");
                    var fd = J.GetDict(root, "fun");
                    foreach (var kv in fd) fun[kv.Key] = J.GetInt(fd, kv.Key);
                    var td = J.GetDict(root, "trust");
                    foreach (var kv in td) trust[kv.Key] = J.GetInt(td, kv.Key);
                    foreach (var fo in J.GetList(root, "favorites")) favoriteWords.Add(J.Str(fo));
                }
                // 旧形式（好感度だけのフラットなJSON）もそのまま読み込める
                foreach (var kv in aff) affection[kv.Key] = J.GetInt(aff, kv.Key);
            }
            catch (Exception) { /*壊れたセーブは初期値で続行*/ }
        }
    }

    private void SaveAffection()
    {
        try
        {
            var ap = new List<string>();
            foreach (var kv in affection) ap.Add("\"" + kv.Key + "\":" + kv.Value);
            var fp = new List<string>();
            foreach (var kv in fun) fp.Add("\"" + kv.Key + "\":" + kv.Value);
            var tp = new List<string>();
            foreach (var kv in trust) tp.Add("\"" + kv.Key + "\":" + kv.Value);
            var fv = new List<string>();
            foreach (var t in favoriteWords) fv.Add("\"" + t + "\"");
            File.WriteAllText(SavePath,
                "{\"affection\":{" + string.Join(",", ap)
                + "},\"fun\":{" + string.Join(",", fp)
                + "},\"trust\":{" + string.Join(",", tp)
                + "},\"favorites\":[" + string.Join(",", fv) + "]}");
        }
        catch (Exception) { /*保存に失敗してもゲームは続行*/ }
    }

    private int AffectionOf(string cid)
    {
        int v;
        return affection.TryGetValue(cid, out v) ? v : 0;
    }

    private int FunOf(string cid)
    {
        int v;
        return fun.TryGetValue(cid, out v) ? v : 0;
    }

    private int TrustOf(string cid)
    {
        int v;
        return trust.TryGetValue(cid, out v) ? v : 0;
    }

    // ---------- UI部品ヘルパー ----------
    private Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        int s = 64, r = 26;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float qx = Mathf.Abs(x - (s - 1) / 2f) - (s / 2f - r);
                float qy = Mathf.Abs(y - (s - 1) / 2f) - (s / 2f - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                float a = Mathf.Clamp01(1f - d);
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return roundedSprite;
    }

    // v25: 小さいボタン・ピル・帯用の角丸スプライト。角半径10px・ボーダー10px。
    // RoundedSprite は角半径26px・ボーダー26pxで、上下合計52pxあるため、
    // 高さ29〜54pxのボタンでは Sliced 描画が潰れて1pxの線にしかならず、
    // ボタンの背景が消えて白文字がクリーム色のパネルに溶けていた（スクショで確認）。
    // ボーダー合計20pxなら高さ28px以上の部品はすべて正しく描画される。
    private Sprite ButtonSprite()
    {
        if (buttonSprite != null) return buttonSprite;
        int s = 64, r = 10;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float cx = (s - 1) / 2f;
                float ax = Mathf.Abs(x - cx);
                float ay = Mathf.Abs(y - cx);
                float qx = ax - (s / 2f - r);
                float qy = ay - (s / 2f - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                float a = Mathf.Clamp01(1f - d);
                // v26: 2pxの枠線を入れる。白いボタンがクリーム色のパネル上で
                // 溶けて見えなくならないようにする。内側2pxより外は灰色。
                bool inner = ax <= (s / 2f - 2f) && ay <= (s / 2f - 2f);
                byte alpha = (byte)(a * 255f);
                px[y * s + x] = (a > 0f && !inner)
                    ? new Color32(110, 110, 120, alpha)
                    : new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        buttonSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return buttonSprite;
    }

    private RectTransform SetRect(RectTransform rt, float x, float y, float w, float h)
    {
        if (preserveLayout) return rt;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    // v27: SetRect と同じ配置を、preserveLayout に関わらず必ず適用する。
    // 吹き出しのレイアウトは、シーンの値や preserveLayout の状態に左右されては
    // ならない。LayoutBubble() はこの関数だけを使って配置する。
    private RectTransform SetRectForce(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }


    // アンカー基準の矩形設定。(ax, ay) は基準の隅: ax 0=左/1=右、ay 1=上/0=下。
    // x は基準の端からの内向き距離、y は上基準なら上から・下基準なら下からの距離。
    // 画面サイズが変わっても、要素が基準の端に張り付いたまま画面外へ出ないための仕組み。
    private RectTransform SetRectA(RectTransform rt, float ax, float ay, float x, float y, float w, float h)
    {
        if (preserveLayout) return rt;
        rt.anchorMin = new Vector2(ax, ay);
        rt.anchorMax = new Vector2(ax, ay);
        rt.pivot = new Vector2(ax, ay);
        rt.anchoredPosition = new Vector2(ax == 0f ? x : -x, ay == 1f ? -y : y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    // 親いっぱいに広げる矩形設定（余白は 左, 上, 右, 下）
    private RectTransform SetRectStretch(RectTransform rt, float left, float top, float right, float bottom)
    {
        if (preserveLayout) return rt;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    // 設計座標(1280×720)からアンカー端までの距離への変換
    private float AnchorX(float ax, float x, float w) { return ax == 1f ? 1280f - (x + w) : x; }
    private float AnchorY(float ay, float y, float h) { return ay == 0f ? 720f - (y + h) : y; }

    private GameObject NewObj(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return go;
    }

    // シーンに生成済みの同名オブジェクトがあれば再利用する（なければ新規作成）
    private GameObject FindOrNew(Transform parent, string name)
    {
        var t = parent.Find(name);
        return t != null ? t.gameObject : NewObj(parent, name);
    }

    private Transform FindNamed(Transform parent, string name)
    {
        return name == null ? null : parent.Find(name);
    }

    private T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    private Text MkLabel(Transform parent, string text, int size, Color color, float x, float y, float w, float h, TextAnchor anchor = TextAnchor.UpperLeft, string objName = null)
    {
        var found = FindNamed(parent, objName);
        if (found != null)
        {
            // シーン生成済みのラベルを再利用する（手動編集した見た目を尊重）
            var et = found.GetComponent<Text>();
            if (et == null) et = found.gameObject.AddComponent<Text>();
            if (et.font == null) et.font = jpFont;
            et.horizontalOverflow = HorizontalWrapMode.Wrap;
            et.verticalOverflow = VerticalWrapMode.Overflow; // v29: シーンの Truncate を上書きし、切れを防ぐ
            et.raycastTarget = false;
            return et;
        }
        var go = NewObj(parent, objName ?? "Label");
        var t = go.AddComponent<Text>();
        t.font = jpFont;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow; // v29: 高さ不足で文字を切らない（Truncate が最下行を切っていた）
        t.alignByGeometry = true; // 文字の表示をくっきりさせる
        t.raycastTarget = false;
        SetRect(t.rectTransform, x, y, w, h);
        return t;
    }

    // 長い文章でも枠からはみ出さないよう、フォントサイズを自動調整する
    private void FitText(Text t, int minSize, int maxSize)
    {
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = minSize;
        t.resizeTextMaxSize = maxSize;
    }
    // ---------- v23: 文字量に応じた自動フィット（BestFitに頼らない） ----------
    // スクリーンショットで確認された問題: Unityの BestFit は日本語の太字を
    // 1行に詰めようとしてフォントを小さくし、枠幅より広い1行を作って枠外にはみ出す。
    // そのため文字幅を自分で見積もり、フォントサイズと行数を先に決めてから代入する。
    // 日本語の全角1文字はフォントサイズの約1.08倍、半角は約0.58倍として見積もる
    // （M PLUS Rounded 1c Bold の実測に合わせた保守的な値。少し余裕を持たせてある）。
    private float TextWidthPx(string text, int fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        foreach (char c in text)
        {
            if (c == '\n') continue;
            w += (c < 128) ? fontSize * 0.58f : fontSize * 1.08f;
        }
        return w;
    }

    private int LinesFor(string text, int fontSize, float boxW)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        if (boxW <= 0f) return 1;
        int lines = 0;
        foreach (string part in text.Split('\n'))
        {
            float w = TextWidthPx(part, fontSize);
            lines += Mathf.Max(1, Mathf.CeilToInt(w / boxW));
        }
        return Mathf.Max(1, lines);
    }

    // boxW x boxH に収まる最大のフォントサイズを返す（baseSize から minSize まで1pxずつ下げる）
    private int FitFontForBox(string text, int baseSize, int minSize, float boxW, float boxH)
    {
        for (int fs = baseSize; fs >= minSize; fs--)
        {
            float lineH = fs * 1.50f; // v29: 実際の行高に合わせ、余裕を持たせる（1.32 では最下行が切れた）
            if (LinesFor(text, fs, boxW) * lineH <= boxH) return fs;
        }
        return minSize;
    }

    // ラベルに文字を入れる唯一の入口（吹き出し・発言プレビュー・評価カード用）。
    // BestFitは切る（BestFitを残すと Unity が勝手に縮小して1行化するため）。
    // 収まらない場合は末尾を「…」で切り、枠外には絶対に出さない。
    private int SetFittedText(Text t, string text, int baseSize, int minSize)
    {
        if (t == null) return baseSize;
        t.resizeTextForBestFit = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow; // v29: 切れ防止（文字数自体は下の … 処理で調整）
        var rt = t.rectTransform;
        float boxW = rt.sizeDelta.x;
        float boxH = rt.sizeDelta.y;
        int fs = FitFontForBox(text, baseSize, minSize, boxW, boxH);
        string shown = text ?? "";
        if (LinesFor(shown, fs, boxW) * fs * 1.50f > boxH)
        {
            // 最小フォントでも収まらない: 入る分だけ残して「…」を付ける
            int maxChars = Mathf.Max(1, (int)(boxW / (fs * 1.08f)) * Mathf.Max(1, (int)(boxH / (fs * 1.50f))));
            if (shown.Length > maxChars) shown = shown.Substring(0, Mathf.Max(0, maxChars - 1)) + "…";
        }
        t.fontSize = fs;
        t.text = shown;
        if (LinesFor(shown, fs, boxW) * fs * 1.50f > boxH + 1f)
            Debug.LogWarning("[UI] text still does not fit: " + shown + " in " + boxW + "x" + boxH + " @" + fs + "px");
        return fs;
    }


    // v28: テキストを自分で改行する。Unity の Text は、シーンの値や BestFit の
    // 残りで折り返さないことがある（ユーザーのスクショで確認：RectTransform は
    // 正しいのに1行ではみ出した）。そこで、文字幅を測って適切な位置に "\n" を
    // 入れ、Text の折り返し設定に依存せず、必ず複数行になるようにする。
    // 既存の "\n" で先に分割するので、何度呼んでも同じ結果になる（冪等）。
    // 人名（リーリア等）が行またぎで割れないよう、名前の途中で切らない。
    // 行頭に「。」 「、」 「」」 「）」が来ないよう、禁則も避ける。
    private string WrapText(string text, int fontSize, float boxW)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string[] names = new string[] { "リーリア", "グリム", "レオ", "ユイ", "ピピ" };
        var sb = new System.Text.StringBuilder();
        foreach (string part in text.Split('\n'))
        {
            if (sb.Length > 0) sb.Append('\n');
            int i = 0;
            while (i < part.Length)
            {
                float w = 0f;
                int j = i;
                while (j < part.Length)
                {
                    char c = part[j];
                    float cw = (c < 128) ? fontSize * 0.58f : fontSize * 1.08f;
                    if (w + cw > boxW && j > i) break;
                    w += cw;
                    j++;
                }
                // 人名の途中で切れそうなら、名前の直前で切る（名前全体を次行へ）。
                foreach (string nm in names)
                {
                    int ni = part.IndexOf(nm, i);
                    if (ni >= i && ni < j && ni + nm.Length > j)
                    {
                        if (ni > i) j = ni;
                        break;
                    }
                }
                // 禁則：次行の先頭が「。」 「、」 「」」 「）」なら、その文字も今行に入れる。
                if (j < part.Length)
                {
                    char nc = part[j];
                    if (nc == '。' || nc == '、' || nc == '」' || nc == '）' || nc == '』' || nc == '…')
                    {
                        float cw = (nc < 128) ? fontSize * 0.58f : fontSize * 1.08f;
                        if (w + cw <= boxW * 1.06f) j++;
                    }
                }
                sb.Append(part.Substring(i, j - i));
                if (j < part.Length) sb.Append('\n');
                i = j;
            }
        }
        return sb.ToString();
    }

    // v25: 人名が1行の途中で割れるかどうかを調べる（行またぎ防止用）。
    private bool NameSplitsAt(string text, int fs, float boxW)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int perLine = Mathf.Max(1, (int)(boxW / (fs * 1.08f)));
        string[] names = { "リーリア", "グリム", "レオ", "ユイ", "ピピ" };
        foreach (var n in names)
        {
            int i = text.IndexOf(n);
            while (i >= 0)
            {
                int startCol = i % perLine;
                if (startCol + n.Length > perLine) return true;
                i = text.IndexOf(n, i + 1);
            }
        }
        return false;
    }

    // 吹き出しの高さを中身に合わせて組み直す（v22対応版）。
    // v22ではラベルは吹き出しの子ではなく FramePanel(pt) の子で、名前はシーン再利用のため
    // NameLabel / PersonalityLabel / DialogueLabel / NextButton のままにする。
    // シーン生成時に親を変えると、既にある同名ラベルが重複して残るため、生成時は親は変えない。
    // ただし v26 から、実行時（LayoutBubble）に既存オブジェクトを吹き出しの子へ移動させる
    // だけなら重複は起きないので、文字を吹き出し内に収めるために付け替えを行う。
    // LayoutBubble() は Awake 完了後（preserveLayout=false）にだけ呼ばれるので、SetRect は有効。
    // 吹き出しのレイアウトを、文字量に合わせて組み直す（最大4行）。
    // v26: 名前・性格・セリフ・次へボタンを吹き出し本体の子にして、吹き出し内座標で配置する。
    // シーン生成時の FramePanel 直下の位置が残っていると、文字が吹き出しの端に寄って
    // はみ出すことがあった。子にすれば、吹き出しの位置・大きさが変わっても文字は
    // 必ず吹き出しの中（左右16px・上10pxの余白つき）に収まる。ランタイムだけの
    // 付け替えで、シーン自体は変えない（次にシーン生成しても、また付け替え直す）。
    private void LayoutBubble()
    {
        if (bubblePanel == null || nameLabel == null || personalityLabel == null || dialogueLabel == null) return;
        if (nameLabel.transform.parent != bubblePanel.transform) nameLabel.transform.SetParent(bubblePanel.transform, false);
        if (personalityLabel.transform.parent != bubblePanel.transform) personalityLabel.transform.SetParent(bubblePanel.transform, false);
        if (dialogueLabel.transform.parent != bubblePanel.transform) dialogueLabel.transform.SetParent(bubblePanel.transform, false);
        if (nextButton != null && nextButton.transform.parent != bubblePanel.transform) nextButton.transform.SetParent(bubblePanel.transform, false);
        bubblePanel.transform.SetAsFirstSibling();
        nameLabel.transform.SetAsLastSibling();
        personalityLabel.transform.SetAsLastSibling();
        dialogueLabel.transform.SetAsLastSibling();
        if (nextButton != null) nextButton.transform.SetAsLastSibling();
        // 吹き出し本体の位置もここで確定する（シーンの値が違っていても、文字とずれないように）。
        // v27: SetRectForce で、preserveLayout に関わらず必ず適用する。
        SetRectForce(bubblePanel.rectTransform, 16, 92, 388, bubblePanel.rectTransform.sizeDelta.y);
        float boxW = 356f;
        float padX = 16f, padTop = 12f; // v29: 上余白も広げる
        float y = padTop; // 吹き出し内座標（上端からの距離）
        nameLabel.resizeTextForBestFit = false;
        nameLabel.fontSize = 24;
        nameLabel.color = new Color(0.25f, 0.45f, 0.25f);
        nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        nameLabel.verticalOverflow = VerticalWrapMode.Overflow; // v29
        SetRectForce(nameLabel.rectTransform, padX, y, boxW, 40); // v29: 24pxの名前は28pxでは足りない（31.7px必要）→40px
        y += 40f + 4f;
        float persH = 0f;
        if (personalityLabel.gameObject.activeSelf)
        {
            string pers = personalityLabel.text ?? "";
            int persFs = FitFontForBox(pers, 13, 10, boxW, 3 * 13 * 1.50f);
            int persLines = Mathf.Min(3, LinesFor(pers, persFs, boxW));
            persH = Mathf.Max(30f, persLines * persFs * 1.50f + 12f); // v29: 行高1.50＋余白12px
            personalityLabel.resizeTextForBestFit = false;
            personalityLabel.fontSize = persFs;
            personalityLabel.color = new Color(0.32f, 0.28f, 0.22f); // v25: シーン再利用時も濃い色に
            personalityLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            personalityLabel.verticalOverflow = VerticalWrapMode.Overflow; // v29
            // v28: 性格文も自分で改行する（折り返し設定に依存しない）。
            personalityLabel.text = WrapText(pers, persFs, boxW);
            SetRectForce(personalityLabel.rectTransform, padX, y, boxW, persH);
            y += persH + 8f;
        }
        string dlg = string.IsNullOrEmpty(fullText) ? (dialogueLabel.text ?? "") : fullText;
        // v27: セリフの最大行数を 4 -> 5 にする。最長のエンディング（57文字）は
        // 4行でぎりぎりなので、文字幅の見積もり誤差で切れないよう余裕を持たせる。
        // 吹き出しは最大で y92〜432（高さ340）まで伸び、パレット上端448より上に収まる。
        int dlgFs = FitFontForBox(dlg, 22, 15, boxW, 5 * 22 * 1.50f);
        // v25: 人名（リーリア等）が行またぎで「リーリ／ア。」と割れないよう、
        // 割れる場合は1pxずつ縮小して、名前が1行に収まるフォントサイズにする。
        int guard = 0;
        while (dlgFs > 15 && NameSplitsAt(dlg, dlgFs, boxW) && guard < 7) { dlgFs--; guard++; }
        int dlgLines = Mathf.Min(5, LinesFor(dlg, dlgFs, boxW));
        float dlgH = Mathf.Max(44f, dlgLines * dlgFs * 1.50f + 16f); // v29: 行高1.50＋余白16px
        // v29: 吹き出しがパレット（上端448）に届かないよう、高さ上限340pxで抑える。
        // 340pxを超えそうな長いセリフだけフォントを1pxずつ小さくし、文字を切らずに収める。
        while (dlgFs > 15 && y + dlgH + 8f + 36f + 20f > 340f)
        {
            dlgFs--;
            dlgLines = Mathf.Min(5, LinesFor(dlg, dlgFs, boxW));
            dlgH = Mathf.Max(44f, dlgLines * dlgFs * 1.50f + 16f);
        }
        // v28: セリフも自分で改行して fullText に入れる。タイプライターは
        // fullText を1文字ずつ出すので、改行も正しいタイミングで現れる。
        // これで、Text の折り返し設定がどうなっていても、必ず複数行になる。
        fullText = WrapText(dlg, dlgFs, boxW);
        dialogueLabel.resizeTextForBestFit = false;
        dialogueLabel.fontSize = dlgFs;
        dialogueLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        dialogueLabel.verticalOverflow = VerticalWrapMode.Overflow; // v29
        SetRectForce(dialogueLabel.rectTransform, padX, y, boxW, dlgH);
        y += dlgH + 8f;
        float bubbleH = y + 36f + 20f; // v29: 下余白12→20px
        bubblePanel.rectTransform.sizeDelta = new Vector2(388f, bubbleH);
        if (nextButton != null)
        {
            SetRectForce(nextButton.GetComponent<RectTransform>(), 388f - 16f - 128f, bubbleH - 12f - 36f, 128f, 36f);
            // v26: 次へボタンも白背景＋黒文字にする（クリーム色の吹き出しの上で、枠線つきで見える）
            var nimg = nextButton.GetComponent<Image>();
            if (nimg != null) nimg.color = new Color(1f, 1f, 1f, 1f);
            var nlab = nextButton.GetComponentInChildren<Text>();
            if (nlab != null) nlab.color = new Color(0.22f, 0.18f, 0.14f, 1f);
        }
    }

    // 発言プレビューの高さを文章量に合わせて組み直す（最大3行）。発言ボタンはその下にずらす。
    private void LayoutSentence()
    {
        if (sentenceLabel == null || speakButton == null || sentenceBgImg == null || sayPanelImg == null) return;
        float boxW = 424f;
        string s = sentenceLabel.text ?? "";
        int fs = FitFontForBox(s, 22, 13, boxW, 3 * 22 * 1.50f);
        int lines = Mathf.Min(3, LinesFor(s, fs, boxW));
        float h = Mathf.Max(46f, lines * fs * 1.50f + 12f); // v29
        sentenceLabel.resizeTextForBestFit = false;
        sentenceLabel.verticalOverflow = VerticalWrapMode.Overflow; // v29
        sentenceLabel.fontSize = fs;
        var srt = sentenceLabel.rectTransform;
        srt.sizeDelta = new Vector2(boxW, h);
        sentenceBgImg.rectTransform.sizeDelta = new Vector2(432f, h + 12f); // v29
        var brt = speakButton.GetComponent<RectTransform>();
        float speakTop = 248f + (h + 12f) + 8f;
        brt.anchoredPosition = new Vector2(brt.anchoredPosition.x, -speakTop);
        brt.sizeDelta = new Vector2(424f, 54f);
    }

    // 文字の縁取り（背景が写真・イラストでも読みやすくする）
    private void AddOutline(Text t, Color c, float d)
    {
        var o = GetOrAdd<Outline>(t.gameObject);
        o.effectColor = c;
        o.effectDistance = new Vector2(d, -d);
    }

    // 文字の影（立体感と視認性の補助）
    private void AddShadowFx(Text t, Color c, float d)
    {
        var sh = GetOrAdd<Shadow>(t.gameObject);
        sh.effectColor = c;
        sh.effectDistance = new Vector2(d, -d);
    }

    private Image MkPanel(Transform parent, Color color, float x, float y, float w, float h, string objName = null)
    {
        var found = FindNamed(parent, objName);
        if (found != null)
        {
            var ei = found.GetComponent<Image>();
            if (ei == null) ei = found.gameObject.AddComponent<Image>();
            // v25: シーン再利用時もスプライトだけは直す。シーンの RoundedSprite
            // （ボーダー26px）は高さ80px未満のパネルで潰れるため、ButtonSprite に替える。
            // 位置・大きさ・色はシーンの手動編集を尊重して変えない。
            ei.sprite = (h < 80f) ? ButtonSprite() : RoundedSprite();
            ei.type = Image.Type.Sliced;
            return ei;
        }
        var go = NewObj(parent, objName ?? "Panel");
        var img = go.AddComponent<Image>();
        // v25: 高さ80px未満のパネル（上部帯・ピル等）は角半径の小さいスプライトを使う。
        // RoundedSprite（ボーダー26px）だと高さが潰れて背景が1pxの線になる。
        img.sprite = (h < 80f) ? ButtonSprite() : RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
        SetRect(img.rectTransform, x, y, w, h);
        return img;
    }

    private Image MkRect(Transform parent, Color color, float x, float y, float w, float h, string objName = null)
    {
        var found = FindNamed(parent, objName);
        if (found != null)
        {
            var ei = found.GetComponent<Image>();
            return ei != null ? ei : found.gameObject.AddComponent<Image>();
        }
        var go = NewObj(parent, objName ?? "Rect");
        var img = go.AddComponent<Image>();
        img.color = color;
        SetRect(img.rectTransform, x, y, w, h);
        return img;
    }


    // アンカー指定版（座標は通常版と同じ設計座標で渡す）
    private Text MkLabelA(Transform parent, string text, int size, Color color, float x, float y, float w, float h, float ax, float ay, TextAnchor anchor = TextAnchor.UpperLeft, string objName = null)
    {
        var t = MkLabel(parent, text, size, color, x, y, w, h, anchor, objName);
        SetRectA(t.rectTransform, ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return t;
    }

    private Image MkPanelA(Transform parent, Color color, float x, float y, float w, float h, float ax, float ay, string objName = null)
    {
        var img = MkPanel(parent, color, x, y, w, h, objName);
        SetRectA(img.rectTransform, ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return img;
    }

    private Image MkRectA(Transform parent, Color color, float x, float y, float w, float h, float ax, float ay, string objName = null)
    {
        var img = MkRect(parent, color, x, y, w, h, objName);
        SetRectA(img.rectTransform, ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return img;
    }

    // 全面に広がるパネル/矩形
    private Image MkPanelStretch(Transform parent, Color color, string objName = null)
    {
        var img = MkPanel(parent, color, 0, 0, 1280, 720, objName);
        SetRectStretch(img.rectTransform, 0, 0, 0, 0);
        return img;
    }

    private Image MkRectStretch(Transform parent, Color color, string objName = null)
    {
        var img = MkRect(parent, color, 0, 0, 1280, 720, objName);
        SetRectStretch(img.rectTransform, 0, 0, 0, 0);
        return img;
    }

    // 上端に張り付いて横いっぱいに広がる帯
    private Image MkPanelTopBand(Transform parent, Color color, float height, string objName = null)
    {
        var img = MkPanel(parent, color, 0, 0, 1280, height, objName);
        if (preserveLayout) return img;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(0, -height);
        rt.offsetMax = Vector2.zero;
        return img;
    }

    // 下端に張り付いて横いっぱいに広がる帯
    private Image MkPanelBottomBand(Transform parent, Color color, float height, string objName = null)
    {
        var img = MkPanel(parent, color, 0, 720 - height, 1280, height, objName);
        if (preserveLayout) return img;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0, height);
        return img;
    }

    // 下端に張り付いて横いっぱいに広がる矩形
    private Image MkRectBottomBand(Transform parent, Color color, float height, string objName = null)
    {
        var img = MkRect(parent, color, 0, 720 - height, 1280, height, objName);
        if (preserveLayout) return img;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0, height);
        return img;
    }

    // 横いっぱいに広がるラベル（中央寄せタイトルなど）
    private Text MkLabelWide(Transform parent, string text, int size, Color color, float y, float h, TextAnchor anchor = TextAnchor.MiddleCenter, string objName = null)
    {
        var t = MkLabel(parent, text, size, color, 0, y, 1280, h, anchor, objName);
        if (preserveLayout) return t;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(0, -(y + h));
        rt.offsetMax = new Vector2(0, -y);
        return t;
    }

    private void Deselect()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private Button MkButton(Transform parent, string text, int fontSize, float x, float y, float w, float h, Action onClick, string objName = null)
    {
        var found = FindNamed(parent, objName);
        if (found != null)
        {
            // シーン生成済みのボタンを再利用する（クリック処理だけ付け直す）
            var eb = found.GetComponent<Button>();
            if (eb == null) eb = found.gameObject.AddComponent<Button>();
            if (eb.targetGraphic == null) eb.targetGraphic = found.GetComponent<Image>();
            // v25: シーン再利用時もスプライトと遷移だけは直す。シーンの
            // RoundedSprite（ボーダー26px）は小さいボタンで潰れ、ColorTint は
            // 白でボタンの色を上書きしてしまう。位置・大きさ・色は変えない。
            var eimg = found.GetComponent<Image>();
            if (eimg != null) { eimg.sprite = ButtonSprite(); eimg.type = Image.Type.Sliced; }
            eb.transition = Selectable.Transition.None;
            if (onClick != null) eb.onClick.AddListener(() => { Deselect(); onClick(); });
            return eb;
        }
        var go = NewObj(parent, objName ?? "Button");
        var img = go.AddComponent<Image>();
        img.sprite = ButtonSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.23f, 0.24f, 0.35f, 1f);
        SetRect(img.rectTransform, x, y, w, h);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        // v25: Button の ColorTint は normalColor（白）で Image の色を上書きして
        // しまうため、設定した紺色のボタンが白くなり、白文字が見えなくなっていた。
        // 色は img.color で直接決め、選択状態も直接変えるので、遷移は使わない。
        btn.transition = Selectable.Transition.None;
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
        var label = MkLabel(go.transform, text, fontSize, Color.white, 0, 0, w, h, TextAnchor.MiddleCenter, "Caption");
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        if (onClick != null) btn.onClick.AddListener(() => { Deselect(); onClick(); });
        return btn;
    }


    // v26: 小さい操作ボタン（チップの移動・削除、ページ送り）は白背景＋黒文字にする。
    // 紺背景＋白文字だと、暗い発言パネルやクリーム色のパレットの上で沈んで見えたため。
    private void LightButton(Button b)
    {
        if (b == null) return;
        var img = b.GetComponent<Image>();
        if (img != null) img.color = new Color(1f, 1f, 1f, 1f);
        var lab = b.GetComponentInChildren<Text>();
        if (lab != null) lab.color = new Color(0.22f, 0.18f, 0.14f, 1f);
    }

    private Button MkButtonA(Transform parent, string text, int fontSize, float x, float y, float w, float h, Action onClick, float ax, float ay, string objName = null)
    {
        var btn = MkButton(parent, text, fontSize, x, y, w, h, onClick, objName);
        SetRectA(btn.GetComponent<RectTransform>(), ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return btn;
    }

    private void AddPointerDown(GameObject go, Action a)
    {
        var et = GetOrAdd<EventTrigger>(go);
        et.triggers.Clear();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entry.callback.AddListener(delegate { a(); });
        et.triggers.Add(entry);
    }

    private void AddPointerEnter(GameObject go, Action a)
    {
        var et = GetOrAdd<EventTrigger>(go);
        et.triggers.Clear();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(delegate { a(); });
        et.triggers.Add(entry);
    }

    private Color HexColor(string hex, Color def)
    {
        Color c;
        if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c)) return c;
        return def;
    }

    private Texture2D LoadTex(string godotPath)
    {
        // "res://assets/lilia.png" -> Resources "assets/lilia"
        string p = godotPath ?? "";
        if (p.StartsWith("res://")) p = p.Substring(6);
        int dot = p.LastIndexOf('.');
        if (dot > 0) p = p.Substring(0, dot);
        Texture2D tex;
        if (texCache.TryGetValue(p, out tex)) return tex;
        tex = Resources.Load<Texture2D>(p);
        texCache[p] = tex;
        return tex;
    }

    // ---------- 画面構築 ----------
    private void BuildUi()
    {
        bgSky = MkRectStretch(root, Color.white, "BgSky");
        bgSky.raycastTarget = false;
        bgGround = MkRectBottomBand(root, Color.white, 160, "BgGround");
        bgGround.raycastTarget = false;
        var bgArtGo = FindOrNew(root, "BgArt");
        bgArt = GetOrAdd<RawImage>(bgArtGo);
        bgArt.raycastTarget = false;
        bgArtGo.SetActive(false);

        portraitBox = SetRect(FindOrNew(root, "PortraitBox").GetComponent<RectTransform>(), 450, PortraitY, 380, 652);
        // 立ち絵は画面上端の中央にアンカーする（横幅が変わっても中央を保つ）
        if (!preserveLayout)
        {
            portraitBox.anchorMin = new Vector2(0.5f, 1);
            portraitBox.anchorMax = new Vector2(0.5f, 1);
            portraitBox.pivot = new Vector2(0.5f, 1);
            portraitBox.anchoredPosition = new Vector2(0, -PortraitY);
        }
        var pgo = FindOrNew(portraitBox, "Portrait");
        portraitImg = GetOrAdd<RawImage>(pgo);
        portraitImg.raycastTarget = false;
        var prt = portraitImg.rectTransform;
        if (!preserveLayout)
        {
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
        }

        // 全面クリックで進む（ボタン類より下の層に置く）
        var catcher = MkRectStretch(root, new Color(0, 0, 0, 0), "ClickCatcher");
        AddPointerDown(catcher.gameObject, OnClickAdvance);

        // 上部: 浮いたピル型ステータス表示（参考画像風）
        var top = SetRect(FindOrNew(root, "TopBar").GetComponent<RectTransform>(), 0, 0, 1280, 720).transform;
        // v23: 上部に濃い帯を追加（タイトル/ピルが背景・立ち絵に浮かないように）。
        // 新規要素なので preserveLayout に関わらず帯の配置をここで確定する。
        var topBand = MkPanelTopBand(top, new Color(0.05f, 0.06f, 0.12f, 0.95f), 60, "TopBand");
        topBand.raycastTarget = false;
        var tbRt = topBand.rectTransform;
        tbRt.anchorMin = new Vector2(0, 1); tbRt.anchorMax = new Vector2(1, 1);
        tbRt.offsetMin = new Vector2(0, -60f); tbRt.offsetMax = Vector2.zero;
        MkButton(top, "🗺 マップ", 16, 16, 8, 120, 40, ShowMap, "MapButton");
        var titleLbl = MkLabel(top, "ことばを紡ぐ恋愛シミュレーション", 18, new Color(1, 1, 1, 1f), 148, 14, 420, 26, TextAnchor.UpperLeft, "GameTitleLabel");
        titleLbl.raycastTarget = false;
        AddOutline(titleLbl, new Color(0, 0, 0, 0.55f), 1.5f);
        var affPill = MkPanelA(top, new Color(0.08f, 0.08f, 0.14f, 0.88f), 588, 8, 240, 44, 1, 1, "AffectionPill");
        affPill.raycastTarget = false;
        affectionLabel = MkLabel(affPill.transform, "", 15, new Color(1f, 1f, 1f), 10, 6, 220, 20, TextAnchor.UpperLeft, "AffectionLabel");
        affectionLabel.raycastTarget = false;
        AddOutline(affectionLabel, new Color(0, 0, 0, 0.45f), 1f);
        var affBarBg = MkRect(affPill.transform, new Color(0, 0, 0, 0.45f), 10, 30, 220, 6, "AffectionBarBg");
        affBarBg.raycastTarget = false;
        var affFill = MkRect(affPill.transform, new Color(0.45f, 0.9f, 0.55f), 10, 30, 220, 6, "AffectionBarFill");
        affFill.raycastTarget = false;
        affFillRt = affFill.rectTransform;
        var funPill = MkPanelA(top, new Color(0.08f, 0.08f, 0.14f, 0.88f), 836, 8, 240, 44, 1, 1, "FunPill");
        funPill.raycastTarget = false;
        funLabel = MkLabel(funPill.transform, "", 15, new Color(1f, 1f, 1f), 10, 6, 220, 20, TextAnchor.UpperLeft, "FunLabel");
        funLabel.raycastTarget = false;
        AddOutline(funLabel, new Color(0, 0, 0, 0.45f), 1f);
        var funBarBg = MkRect(funPill.transform, new Color(0, 0, 0, 0.45f), 10, 30, 220, 6, "FunBarBg");
        funBarBg.raycastTarget = false;
        var funFill = MkRect(funPill.transform, new Color(1f, 0.75f, 0.3f), 10, 30, 220, 6, "FunBarFill");
        funFill.raycastTarget = false;
        funFillRt = funFill.rectTransform;
        MkButtonA(top, "📖 思い出", 15, 1084, 8, 96, 40, ToggleBacklog, 1, 1, "BacklogButton");

        BuildTimerBar();
        BuildFrame();
        BuildEvalCard();
        BuildTitleScreen();
        BuildMapScreen();
        BuildBacklog();
    }

    private void BuildTimerBar()
    {
        // v25: タイマー文字とバーは、立ち絵や背景写真の上に直接置くと読めないため、
        // 濃い帯の上にまとめて置く（帯は MkRect＝スプライト無しなので潰れない）。
        timerStripImg = MkRectA(root, new Color(0.05f, 0.06f, 0.12f, 0.85f), 12, 402, 956, 48, 0, 0, "TimerStrip");
        timerStripImg.raycastTarget = false;
        // 制限時間バーは単語パレットの真上に置く
        timerLabel = MkLabelA(root, "", 17, new Color(1f, 0.95f, 0.85f), 16, 408, 300, 30, 0, 0, TextAnchor.UpperLeft, "TimerLabel"); // v29: 17pxは22pxでは足りない→30
        AddOutline(timerLabel, new Color(0, 0, 0, 0.55f), 1.5f);
        timerBg = MkRectA(root, new Color(0.05f, 0.05f, 0.1f, 0.75f), 16, 436, 944, 12, 0, 0, "TimerBarBg");
        timerBg.raycastTarget = false;
        var fill = MkRectA(root, new Color(0.45f, 0.9f, 0.5f), 16, 436, 944, 12, 0, 0, "TimerBarFill");
        fill.raycastTarget = false;
        timerFill = fill;
        timerFillRt = fill.rectTransform;
        SetTimerVisible(false);
    }

    private void BuildEvalCard()
    {
        // 右下の「今回の評価」カード（参考画像風）
        var card = MkPanelA(root, new Color(0.98f, 0.96f, 0.90f, 0.97f), 976, 448, 288, 256, 1, 0, "EvalCard");
        card.raycastTarget = false;
        var ct = card.transform;
        var title = MkLabel(ct, "✨ 今回の評価 ✨", 17, new Color(0.35f, 0.28f, 0.15f), 0, 10, 288, 24, TextAnchor.MiddleCenter, "EvalTitle");
        title.raycastTarget = false;
        evalAffLabel = MkLabel(ct, "♥ 好感度　―", 18, new Color(0.30f, 0.22f, 0.18f), 16, 42, 256, 24, TextAnchor.UpperLeft, "EvalAffLabel");
        evalFunLabel = MkLabel(ct, "😊 面白さ　―", 18, new Color(0.30f, 0.22f, 0.18f), 16, 70, 256, 24, TextAnchor.UpperLeft, "EvalFunLabel");
        evalTrustLabel = MkLabel(ct, "🤝 信頼度　―", 18, new Color(0.30f, 0.22f, 0.18f), 16, 98, 256, 24, TextAnchor.UpperLeft, "EvalTrustLabel");
        var miniGo = FindOrNew(ct, "EvalMini");
        SetRect(miniGo.GetComponent<RectTransform>(), 16, 130, 76, 112);
        evalMini = GetOrAdd<RawImage>(miniGo);
        evalMini.raycastTarget = false;
        resultLabel = MkLabel(ct, "", 13, new Color(0.25f, 0.22f, 0.18f), 104, 130, 168, 120, TextAnchor.UpperLeft, "EvalResultLabel"); // v29: 112→120（カード内 y130+120=250<256）
        resultLabel.resizeTextForBestFit = false; // v23: SetFittedText() が文字量で決める
    }

    private void SetTimerVisible(bool v)
    {
        if (timerStripImg != null) timerStripImg.gameObject.SetActive(v);
        timerLabel.gameObject.SetActive(v);
        timerBg.gameObject.SetActive(v);
        timerFill.gameObject.SetActive(v);
    }

    private void BuildFrame()
    {
        // 画面全体を覆う透明な土台（中身は吹き出し・発言パネル・単語パレットに分離）
        var panel = MkPanelStretch(root, new Color(0, 0, 0, 0), "FramePanel");
        panel.raycastTarget = false;
        msgPanel = panel.rectTransform;
        var pt = panel.transform;

        // 左上の吹き出し
        // v23: 初期サイズは2行想定。実行時は LayoutBubble() が文字量に合わせて組み直す。
        var bubble = MkPanel(pt, new Color(0.98f, 0.96f, 0.90f, 0.97f), 16, 92, 388, 280, "SpeechBubble"); // v29: 初期高さ212→280
        bubble.raycastTarget = false;
        bubblePanel = bubble;
        nameLabel = MkLabel(pt, "", 24, new Color(0.25f, 0.45f, 0.25f), 32, 102, 356, 40, TextAnchor.UpperLeft, "NameLabel"); // v29: 28→40
        AddOutline(nameLabel, new Color(1, 1, 1, 0.55f), 1f);
        personalityLabel = MkLabel(pt, "", 13, new Color(0.32f, 0.28f, 0.22f), 32, 132, 356, 48, TextAnchor.UpperLeft, "PersonalityLabel"); // v29: 32→48
        personalityLabel.resizeTextForBestFit = false;
        dialogueLabel = MkLabel(pt, "", 22, new Color(0.22f, 0.18f, 0.14f), 32, 168, 356, 150, TextAnchor.UpperLeft, "DialogueLabel"); // v29: 92→150
        dialogueLabel.resizeTextForBestFit = false;
        AddShadowFx(dialogueLabel, new Color(0, 0, 0, 0.15f), 1f);
        nextButton = MkButton(pt, "つぎへ ▼", 16, 216, 264, 128, 36, OnNext, "NextButton");

        // 選択欄: 右の発言パネル＋下の単語パレット（参考画像の構成）
        askGroup = FindOrNew(pt, "AskGroup");
        SetRectStretch(askGroup.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var at = askGroup.transform;
        var sayPanel = MkPanelA(at, new Color(0.10f, 0.11f, 0.18f, 0.96f), 800, 92, 464, 340, 1, 1, "SayPanel");
        sayPanelImg = sayPanel;
        sayPanel.raycastTarget = false;
        var sayTitle = MkLabelA(at, "✏ あなたの発言", 16, new Color(0.95f, 0.96f, 1f), 820, 106, 300, 28, 1, 1, TextAnchor.UpperLeft, "SayTitleLabel"); // v29: 20→28
        sayTitle.color = new Color(0.95f, 0.96f, 1f); // v25: シーン再利用時も色を直す
        var chipsBg = MkRectA(at, new Color(0, 0, 0, 0.20f), 816, 130, 432, 82, 1, 1, "ChipsBg");
        chipsBg.raycastTarget = false;
        chipsGroup = SetRectA(FindOrNew(at, "Chips").GetComponent<RectTransform>(), 1, 1, AnchorX(1, 820, 424), AnchorY(1, 134, 74), 424, 74);
        LightButton(MkButtonA(at, "◀", 15, 820, 216, 40, 30, () => MoveChip(-1), 1, 1, "ChipPrevButton"));
        LightButton(MkButtonA(at, "▶", 15, 864, 216, 40, 30, () => MoveChip(1), 1, 1, "ChipNextButton"));
        LightButton(MkButtonA(at, "削除", 15, 908, 216, 64, 30, DeleteChip, 1, 1, "ChipDeleteButton"));
        LightButton(MkButtonA(at, "↩ 一つ戻す", 14, 976, 216, 118, 30, UndoLast, 1, 1, "ChipUndoButton"));
        LightButton(MkButtonA(at, "全解除", 15, 1098, 216, 146, 30, ClearSelected, 1, 1, "ChipClearButton"));
        var sentenceBg = MkRectA(at, new Color(1, 1, 1, 0.08f), 816, 248, 432, 96, 1, 1, "SentenceBg"); // v29: 64→96
        sentenceBgImg = sentenceBg;
        sentenceBg.raycastTarget = false;
        sentenceLabel = MkLabelA(at, "（まだ何も選んでいません）", 22, new Color(0.9f, 0.97f, 1f), 820, 252, 424, 84, 1, 1, TextAnchor.UpperLeft, "SentenceLabel"); // v29: 56→84
        sentenceLabel.resizeTextForBestFit = false; // v23: LayoutSentence() が文字量で決める
        AddOutline(sentenceLabel, new Color(0, 0, 0, 0.5f), 1f);
        speakButton = MkButtonA(at, "✨ 発言する ▶", 24, 820, 320, 424, 54, () => DoSpeak(false), 1, 1, "SpeakButton");
        speakButton.GetComponent<Image>().color = new Color(0.20f, 0.42f, 0.24f); // v25: 色だけはシーンでも上書きする（白文字のコントラストのため。位置・大きさは変えない）
        var palPanel = MkPanelA(at, new Color(0.98f, 0.96f, 0.90f, 0.97f), 16, 448, 944, 256, 0, 0, "PalettePanel");
        palPanel.raycastTarget = false;
        tabsArea = SetRectA(FindOrNew(at, "Tabs").GetComponent<RectTransform>(), 0, 0, 32, AnchorY(0, 456, 60), 690, 60);
        BuildSearchField(at, 734, 462, 210, 30);
        paletteArea = SetRectA(FindOrNew(at, "Palette").GetComponent<RectTransform>(), 0, 0, 32, AnchorY(0, 524, 140), 912, 140);
        var hint = MkLabelA(at, "ⓘ 単語を選んで、自由に文章を作りましょう！", 14, new Color(0.32f, 0.28f, 0.22f), 32, 668, 700, 28, 0, 0, TextAnchor.UpperLeft, "PaletteHintLabel"); // v29: 20→28
        hint.color = new Color(0.32f, 0.28f, 0.22f); // v25: シーン再利用時も色を直す
        hint.raycastTarget = false;
        // パレットのページ送り（「すべて」103語を1画面に収めるためのもの。ヒント文と重ならない位置）
        LightButton(MkButtonA(at, "◀", 14, 760, 664, 36, 24, () => ChangePalettePage(-1), 0, 0, "PalettePrevButton"));
        palettePageLabel = MkLabelA(at, "1 / 1", 14, new Color(0.32f, 0.28f, 0.22f), 800, 664, 60, 28, 0, 0, TextAnchor.MiddleCenter, "PalettePageLabel"); // v29: 24→28
        palettePageLabel.color = new Color(0.32f, 0.28f, 0.22f); // v25: シーン再利用時も色を直す
        palettePageLabel.raycastTarget = false;
        LightButton(MkButtonA(at, "▶", 14, 864, 664, 36, 24, () => ChangePalettePage(1), 0, 0, "PaletteNextButton"));
        askGroup.SetActive(false);
    }

    private void BuildSearchField(Transform parent, float x, float y, float w, float h)
    {
        var go = FindOrNew(parent, "SearchField");
        SetRectA(go.GetComponent<RectTransform>(), 0, 0, x, AnchorY(0, y, h), w, h);
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        // v25: シーン再利用時もスプライトを ButtonSprite に直す（RoundedSprite は高さ30pxで潰れる）。
        img.sprite = ButtonSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.95f);
        var field = GetOrAdd<InputField>(go);
        var textComp = MkLabel(go.transform, "", 15, new Color(0.25f, 0.22f, 0.18f), 10, 0, w - 16, h, TextAnchor.MiddleLeft, "SearchText");
        textComp.supportRichText = false;
        var ph = MkLabel(go.transform, "🔍 キーワードで検索", 14, new Color(0.35f, 0.32f, 0.28f), 10, 0, w - 16, h, TextAnchor.MiddleLeft, "SearchPlaceholder");
        ph.color = new Color(0.35f, 0.32f, 0.28f); // v25: シーン再利用時も色を直す
        field.textComponent = textComp;
        field.placeholder = ph;
        field.onValueChanged.AddListener((string s) => { searchText = s; palettePage = 0; RenderPaletteGrid(); });
    }

    private void LayoutFrame(bool ask, bool animate)
    {
        // 吹き出し・発言パネル・単語パレットは固定配置。質問時はタイマー表示だけ切り替える
        SetTimerVisible(ask);
    }

    private void BuildBacklog()
    {
        var panel = MkPanelStretch(root, new Color(0.03f, 0.03f, 0.08f, 0.96f), "BacklogPanel");
        backlogPanel = panel.gameObject;
        var pt = panel.transform;
        MkLabel(pt, "バックログ", 22, new Color(1f, 0.95f, 0.8f), 20, 14, 400, 30, TextAnchor.UpperLeft, "BacklogTitleLabel");
        var scrollGo = FindOrNew(pt, "Scroll");
        SetRectStretch(scrollGo.GetComponent<RectTransform>(), 16, 52, 16, 72);
        var scroll = GetOrAdd<ScrollRect>(scrollGo);
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = FindOrNew(scrollGo.transform, "Viewport");
        var vrt = viewport.GetComponent<RectTransform>();
        if (!preserveLayout)
        {
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
        }
        var vimg = GetOrAdd<Image>(viewport);
        if (!preserveLayout) vimg.color = new Color(1, 1, 1, 0.02f);
        GetOrAdd<RectMask2D>(viewport);
        var content = FindOrNew(viewport.transform, "Content");
        backlogContent = content.GetComponent<RectTransform>();
        if (!preserveLayout)
        {
            backlogContent.anchorMin = new Vector2(0, 1);
            backlogContent.anchorMax = new Vector2(1, 1);
            backlogContent.pivot = new Vector2(0.5f, 1);
            backlogContent.anchoredPosition = Vector2.zero;
            backlogContent.sizeDelta = new Vector2(0, 0);
        }
        var vlg = GetOrAdd<VerticalLayoutGroup>(content);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4;
        var fitter = GetOrAdd<ContentSizeFitter>(content);
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vrt;
        scroll.content = backlogContent;
        MkButtonA(pt, "閉じる", 15, 16, 664, 120, 40, ToggleBacklog, 0, 0, "BacklogCloseButton");
        backlogPanel.SetActive(false);
    }

    private void ToggleBacklog()
    {
        backlogPanel.SetActive(!backlogPanel.activeSelf);
    }

    private void LogLine(string speaker, string text)
    {
        backlog.Add(new string[] { speaker, text });
        string shown = (speaker != "" ? speaker + ": " : "") + text;
        var l = MkLabel(backlogContent, shown, 16, new Color(0.9f, 0.92f, 1f), 0, 0, 1220, 26);
        var fitter = l.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var le = l.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 24;
        while (backlogContent.childCount > 200)
            Destroy(backlogContent.GetChild(0).gameObject);
    }

    // ---------- タイトル / マップ ----------
    private void SetCover(RawImage img, Texture2D tex)
    {
        img.texture = tex;
        if (tex == null) return;
        float scale = Mathf.Max(1280f / tex.width, 720f / tex.height);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
    }

    private void BuildTitleScreen()
    {
        var panel = MkPanelStretch(root, new Color(0.05f, 0.08f, 0.16f, 1f), "TitlePanel");
        titleScreen = panel.gameObject;
        var pt = panel.transform;
        var bgGo = FindOrNew(pt, "TitleBg");
        var bg = GetOrAdd<RawImage>(bgGo);
        bg.raycastTarget = false;
        if (!preserveLayout) bg.color = new Color(0.55f, 0.55f, 0.6f, 1f);
        SetCover(bg, LoadTex("res://assets/world_map_top.png"));
        var dim = MkRectStretch(pt, new Color(0.03f, 0.04f, 0.1f, 0.45f), "TitleDim");
        dim.raycastTarget = false;
        MkLabelWide(pt, "ことばを紡ぐ恋愛シミュレーション", 52, new Color(1f, 0.97f, 0.9f), 218, 72, TextAnchor.MiddleCenter, "TitleMainLabel");
        MkLabelWide(pt, "〜 プロトタイプ2 〜　5つの地域で、ことばを組み合わせて物語を紡ごう", 20, new Color(0.9f, 0.93f, 1f), 300, 30, TextAnchor.MiddleCenter, "TitleSubLabel");
        var startBtn = MkButton(pt, "▶ ゲームスタート", 26, 480, 356, 320, 64, ShowMap, "TitleStartButton");
        if (!preserveLayout)
        {
            var srt = startBtn.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 1); srt.anchorMax = new Vector2(0.5f, 1);
            srt.pivot = new Vector2(0.5f, 1); srt.anchoredPosition = new Vector2(0, -356);
        }
        var quitBtn = MkButton(pt, "おわる", 16, 560, 436, 160, 40, () => Application.Quit(), "TitleQuitButton");
        if (!preserveLayout)
        {
            var qrt = quitBtn.GetComponent<RectTransform>();
            qrt.anchorMin = new Vector2(0.5f, 1); qrt.anchorMax = new Vector2(0.5f, 1);
            qrt.pivot = new Vector2(0.5f, 1); qrt.anchoredPosition = new Vector2(0, -436);
        }
        // v27: 版数ラベルは、シーン再利用時に MkLabel がテキストを設定しないため、
        // シーン生成時（v22）の「Prototype 2 v22 (Unity版)」のまま残ってしまう。
        // 戻り値の Text に直接テキストを入れて、実行中の版数を必ず表示する。
        // これで、タイトル画面右下の版数を見れば、どの版のコードで動いているか分かる。
        var versionLabel = MkLabelA(pt, "Prototype 2 v30 (Unity版)", 13, new Color(1, 1, 1, 0.6f), 1080, 688, 184, 20, 1, 0, TextAnchor.UpperRight, "VersionLabel");
        versionLabel.text = "Prototype 2 v30 (Unity版)";
        versionLabel.fontSize = 13;
    }

    private Dictionary<string, object> CharForStage(string stageId)
    {
        foreach (var c in characters)
            if (J.GetStr(c, "stage_id") == stageId) return c;
        return new Dictionary<string, object>();
    }

    private Vector2 RegionCenter(string stageId)
    {
        float[] bp;
        if (!ZonePos.TryGetValue(stageId, out bp)) bp = new float[] { 0f, 0f };
        float hCov = MapOrtho * 1280f / 720f;
        return new Vector2((0.5f + bp[0] / hCov) * 1280f, (0.5f - bp[1] / MapOrtho) * 720f);
    }

    private void BuildMapScreen()
    {
        var panel = MkPanelStretch(root, new Color(0.4f, 0.55f, 0.75f, 1f), "MapPanel");
        mapScreen = panel.gameObject;
        var pt = panel.transform;
        var bgGo = FindOrNew(pt, "MapBg");
        var bg = GetOrAdd<RawImage>(bgGo);
        bg.raycastTarget = false;
        SetCover(bg, LoadTex("res://assets/world_map_top.png"));
        var header = MkPanelTopBand(pt, new Color(0.05f, 0.06f, 0.12f, 0.82f), 56, "MapHeader");
        header.raycastTarget = false;
        MkLabel(header.transform, "🗺 行き先を選んでください", 22, Color.white, 20, 14, 800, 28, TextAnchor.UpperLeft, "MapHeaderLabel");
        MkButtonA(header.transform, "好感度リセット", 13, 996, 10, 132, 36, ResetAffection, 1, 1, "MapResetButton");
        MkButtonA(header.transform, "タイトルへ", 15, 1140, 10, 120, 36, ShowTitle, 1, 1, "MapToTitleButton");
        foreach (var s in stages)
        {
            string sid = J.GetStr(s, "id");
            var ch = CharForStage(sid);
            string btnText = J.GetStr(s, "icon") + " " + J.GetStr(s, "name") + "\n" + J.GetStr(ch, "name");
            Vector2 ctr = RegionCenter(sid);
            var b = MkButton(pt, btnText, 17, ctr.x - 104, ctr.y - 31, 208, 62, () => EnterStage(sid), "Zone_" + sid);
            // 地域ボタンは画面中央基準に置く（中央基準で表示される背景マップと常に一致させる）
            if (!preserveLayout)
            {
                var brt = b.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.5f, 0.5f);
                brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = new Vector2(ctr.x - 640f, 360f - ctr.y);
            }
            string desc = J.GetStr(s, "name") + "：" + J.GetStr(s, "description") + "\n登場キャラ: " + J.GetStr(ch, "name") + "（" + J.GetStr(ch, "personality") + "）";
            AddPointerEnter(b.gameObject, () => { mapInfoLabel.text = desc; });
        }
        var info = MkPanelBottomBand(pt, new Color(0.05f, 0.06f, 0.12f, 0.82f), 72, "MapInfoBar");
        info.raycastTarget = false;
        mapInfoLabel = MkLabel(info.transform, "地域にカーソルを合わせると説明が出ます。クリックでその地域へ出発します。", 16, new Color(0.95f, 0.96f, 1f), 20, 10, 1240, 56, TextAnchor.UpperLeft, "MapInfoLabel");
        SetRectStretch(mapInfoLabel.rectTransform, 20, 10, 20, 6);
        FitText(mapInfoLabel, 11, 16);
        mapScreen.SetActive(false);
    }

    private void ShowTitle()
    {
        screen = "title";
        titleScreen.SetActive(true);
        mapScreen.SetActive(false);
    }

    private void ShowMap()
    {
        screen = "map";
        titleScreen.SetActive(false);
        mapScreen.SetActive(true);
    }

    private void EnterStage(string stageId)
    {
        screen = "game";
        titleScreen.SetActive(false);
        mapScreen.SetActive(false);
        SelectStage(stageId);
    }

    // ---------- ステージ・物語 ----------
    private void SelectStage(string stageId)
    {
        foreach (var s in stages)
            if (J.GetStr(s, "id") == stageId) currentStage = s;
        foreach (var c in characters)
            if (J.GetStr(c, "stage_id") == stageId) currentChar = c;
        selected.Clear();
        bgSky.color = HexColor(J.GetStr(currentStage, "bg"), Color.white);
        bgGround.color = HexColor(J.GetStr(currentStage, "ground"), Color.white);
        string bgi = J.GetStr(currentStage, "bg_image");
        var bgTex = bgi != "" ? LoadTex(bgi) : null;
        if (bgTex != null)
        {
            SetCover(bgArt, bgTex);
            bgArt.gameObject.SetActive(true);
        }
        else bgArt.gameObject.SetActive(false);
        string p = J.GetStr(currentChar, "portrait");
        if (p != "") SetPortraitTex(LoadTex(p));
        nameLabel.color = Color.Lerp(HexColor(J.GetStr(currentChar, "accent"), Color.white), new Color(0.12f, 0.16f, 0.10f), 0.65f);
        if (evalAffLabel != null)
        {
            evalAffLabel.text = "♥ 好感度　―";
            evalFunLabel.text = "😊 面白さ　―";
            evalTrustLabel.text = "🤝 信頼度　―";
            resultLabel.text = "";
        }
        RefreshCharPanel();
        StartStory();
    }

    private void SetPortraitTex(Texture2D tex)
    {
        portraitImg.texture = tex;
        if (evalMini != null) evalMini.texture = tex;
        if (tex == null) return;
        // v23: 幅上限320 (was 380) で発言パネルの下に潜らないように。高さ上限は652 (PortraitY 60分)
        float scale = Mathf.Min(320f / tex.width, 652f / tex.height);
        portraitImg.rectTransform.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
        portraitImg.rectTransform.anchoredPosition = Vector2.zero;
        portraitImg.rectTransform.localScale = Vector3.one;
    }

    private void RefreshCharPanel()
    {
        if (currentChar.Count == 0) return;
        string cid = J.GetStr(currentChar, "id");
        int aff = AffectionOf(cid);
        var lv = Evaluator.AffectionLevel(aff, affectionLevels);
        string hearts = "";
        int hcount = Mathf.Clamp(aff / 10, 0, 10);
        for (int i = 0; i < hcount; i++) hearts += "♥";
        affectionLabel.color = Color.white; // v25: シーン再利用時も白文字に
        funLabel.color = Color.white;
        affectionLabel.text = "♥ 好感度 " + aff + "/100";
        if (affFillRt != null) affFillRt.sizeDelta = new Vector2(220f * Mathf.Clamp01(aff / 100f), 6);
        if (funLabel != null) funLabel.text = "😊 面白さ " + FunOf(cid) + "/100";
        if (funFillRt != null) funFillRt.sizeDelta = new Vector2(220f * Mathf.Clamp01(FunOf(cid) / 100f), 6);
    }

    private void StartStory()
    {
        string cid = J.GetStr(currentChar, "id");
        // 好感度対策: 物語の始まり（周回・入場時）に、冷めた気持ちは時間経過で少し回復する
        int aff = AffectionOf(cid);
        if (aff < 0)
        {
            affection[cid] = Math.Min(0, aff + 10);
            SaveAffection();
            LogLine("システム", "（時間が経ち、" + J.GetStr(currentChar, "name") + "の気持ちが少し落ち着いたようだ）");
            RefreshCharPanel();
        }
        var story = J.GetDict(stories, cid);
        beats = J.GetList(story, "beats");
        storyPos = 0;
        ShowBeat();
    }

    private void ResetAffection()
    {
        var keys = new List<string>(affection.Keys);
        foreach (var k in keys) affection[k] = 0;
        var fkeys = new List<string>(fun.Keys);
        foreach (var k in fkeys) fun[k] = 0;
        var tkeys = new List<string>(trust.Keys);
        foreach (var k in tkeys) trust[k] = 0;
        SaveAffection();
        RefreshCharPanel();
        mapInfoLabel.text = "好感度・面白さ・信頼度をリセットしました（全キャラクター 0）。地域をクリックでその地域へ出発します。";
    }

    private void SetSpeaker(bool isLine)
    {
        if (isLine)
        {
            nameLabel.text = J.GetStr(currentChar, "name") + " ( " + J.GetStr(currentChar, "race") + " )";
            portraitImg.color = Color.white;
        }
        else
        {
            nameLabel.text = "";
            portraitImg.color = new Color(0.82f, 0.82f, 0.88f);
        }
        personalityLabel.text = J.GetStr(currentChar, "personality") + " / " + J.GetStr(currentStage, "description");
        LayoutBubble();
    }

    private void ShowStoryBox()
    {
        personalityLabel.gameObject.SetActive(true);
        askGroup.SetActive(false);
        LayoutFrame(false, true);
    }

    private void ShowBeat()
    {
        if (storyPos >= beats.Count)
        {
            StartStory();
            return;
        }
        var beat = J.Dict(beats[storyPos]);
        string btype = J.GetStr(beat, "type", "narration");
        phase = "story";
        ShowStoryBox();
        SetExpression("default");
        nextButton.gameObject.SetActive(true);
        if (btype == "ask")
        {
            var q = QuestionByIndex(J.GetInt(beat, "question_index", 0));
            if (q.Count == 0)
            {
                storyPos++;
                ShowBeat();
                return;
            }
            EnterAsk(q);
        }
        else if (btype == "ending")
        {
            SetSpeaker(false);
            SetExpression(EndingExpression());
            ShowDialogue(EndingText(), "");
        }
        else if (btype == "line")
        {
            SetSpeaker(true);
            ShowDialogue("「" + J.GetStr(beat, "text") + "」", J.GetStr(currentChar, "name"));
        }
        else
        {
            SetSpeaker(false);
            ShowDialogue(J.GetStr(beat, "text"), "");
        }
    }

    private string EndingText()
    {
        string cid = J.GetStr(currentChar, "id");
        var endings = J.GetDict(J.GetDict(stories, cid), "endings");
        int aff = AffectionOf(cid);
        if (aff >= 40) return J.GetStr(endings, "high");
        if (aff < 10) return J.GetStr(endings, "low");
        return J.GetStr(endings, "normal");
    }

    private string EndingExpression()
    {
        int aff = AffectionOf(J.GetStr(currentChar, "id"));
        if (aff >= 40) return "fun";
        if (aff < 10) return "cry";
        return "default";
    }

    private Dictionary<string, object> QuestionByIndex(int idx)
    {
        var qs = J.GetList(currentChar, "questions");
        if (idx < 0 || idx >= qs.Count) return new Dictionary<string, object>();
        var q = J.Dict(qs[idx]);
        string cid = J.GetStr(currentChar, "id");
        if (AffectionOf(cid) < J.GetInt(q, "min_affection", 0)) return new Dictionary<string, object>();
        return q;
    }

    private void ShowDialogue(string text, string speaker)
    {
        fullText = text;
        dialogueLabel.text = "";
        typePos = 0f;
        typing = true;
        // v23: タイプライター表示中もフォントサイズと吹き出し高さは全文で決めて固定する
        LayoutBubble();
        LogLine(speaker, text);
    }

    private void FinishTyping()
    {
        typing = false;
        dialogueLabel.text = fullText;
        LayoutBubble();
        dialogueLabel.text = fullText;
    }

    private void OnClickAdvance()
    {
        if (screen != "game") return;
        if (backlogPanel.activeSelf) return;
        if (typing)
        {
            FinishTyping();
            return;
        }
        if (phase != "ask") OnNext();
    }

    private void OnNext()
    {
        if (phase == "ask") return;
        storyPos++;
        ShowBeat();
    }

    private void Update()
    {
        TickBlink();
        // v27: 吹き出しの配置がずれているのを検出したら、すぐ直す安全網。
        // シーンの値や他の処理でラベルの位置・幅・折り返しが戻ってしまっても、
        // 次のフレームで LayoutBubble() が直す。表示中だけ確認する（軽い比較のみ）。
        if (screen == "game" && bubblePanel != null && bubblePanel.gameObject.activeInHierarchy
            && dialogueLabel != null
            && (dialogueLabel.transform.parent != bubblePanel.transform
                || dialogueLabel.rectTransform.sizeDelta.x != 356f
                || dialogueLabel.horizontalOverflow != HorizontalWrapMode.Wrap
                || dialogueLabel.verticalOverflow != VerticalWrapMode.Overflow // v29
                || dialogueLabel.rectTransform.sizeDelta.y < 44f)) // v29: 高さが小さいままなら直す
        {
            LayoutBubble();
        }
        if (typing)
        {
            typePos += TypeSpeed * Time.deltaTime;
            int total = fullText.Length;
            int n = Math.Min(total, (int)typePos);
            if (n > 0 && n < total && char.IsHighSurrogate(fullText[n - 1])) n++;
            dialogueLabel.text = fullText.Substring(0, n);
            // v26: タイプライターが自然に終わったときも FinishTyping() を呼ぶ。
            // 従来は typing=false だけだったため、表示完了時の再レイアウトが
            // 走らず、吹き出しの高さが途中のまま残ることがあった。
            if ((int)typePos >= total) FinishTyping();
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
                OnClickAdvance();
        }
        if (phase != "ask" || screen != "game") return;
        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            UpdateTimerUi();
            OnTimeout();
            return;
        }
        UpdateTimerUi();
    }

    private void UpdateTimerUi()
    {
        int sec = Mathf.CeilToInt(timeLeft);
        timerLabel.text = "⏱ 制限時間: 残り " + sec + " 秒";
        float ratio = timeLimit > 0f ? Mathf.Clamp01(timeLeft / timeLimit) : 0f;
        timerFillRt.sizeDelta = new Vector2(timerBarW * ratio, 12);
        timerFill.color = Color.HSVToRGB(0.33f * ratio, 0.85f, 0.95f);
        timerLabel.color = sec <= 10 ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.95f, 0.85f);
    }

    private void OnTimeout()
    {
        selected.Clear();
        RefreshSentence();
        DoSpeak(true);
    }

    // ---------- 質問フェーズ ----------
    private void EnterAsk(Dictionary<string, object> q)
    {
        phase = "ask";
        bool turned = false;
        if (pendingTurn)
        {
            pendingTurn = false;
            var tqs = J.GetList(currentChar, "turn_questions");
            if (tqs.Count > 0)
            {
                q = J.Dict(tqs[turnCount % tqs.Count]);
                turned = true;
            }
        }
        activeQuestion = q;
        personalityLabel.gameObject.SetActive(false);
        askGroup.SetActive(true);
        LayoutFrame(true, true);
        SetSpeaker(true);
        if (turned) personalityLabel.text = "（転換の言葉で、話題がふわりと変わった）";
        ShowDialogue("「" + J.GetStr(q, "text") + "」", J.GetStr(currentChar, "name"));
        nextButton.gameObject.SetActive(false);
        speakButton.interactable = true;
        SetFittedText(resultLabel, "単語を選んで文章を作りましょう。時間切れは無言 (-10) です。", 13, 10);
        timeLeft = timeLimit;
        UpdateTimerUi();
        BuildPalette(OfferedWords(activeQuestion));
        RefreshSentence();
    }

    private Dictionary<string, Dictionary<string, object>> OfferedWords(Dictionary<string, object> q)
    {
        var offered = new Dictionary<string, Dictionary<string, object>>();
        var wantTags = new HashSet<string>();
        foreach (var tag in J.List(J.Get(currentChar, "liked_tags"))) wantTags.Add(J.Str(tag));
        foreach (var tag in J.List(J.Get(q, "topic_tags"))) wantTags.Add(J.Str(tag));
        var likedWords = J.StrList(J.Get(currentChar, "liked_words"));
        var quirky = new List<Dictionary<string, object>>();
        var basics = new List<string> { "好き", "嫌い", "あなた", "一緒に", "とても" };
        foreach (var wo in J.GetList(dataWords, "words"))
        {
            var w = J.Dict(wo);
            string cat = J.GetStr(w, "category");
            string text = J.GetStr(w, "text");
            bool hit = false;
            if (cat == "affirm" || cat == "negate" || cat == "ending") hit = true;
            if (likedWords.Contains(text)) hit = true;
            if (basics.Contains(text)) hit = true;
            foreach (var tag in J.List(J.Get(w, "tags")))
            {
                string tg = J.Str(tag);
                if (wantTags.Contains(tg) || tg == "turn") hit = true;
                if (tg == "particle") hit = true; // 助詞はいつでも使えるように提示する
            }
            if (hit) offered[text] = w;
            else quirky.Add(w);
        }
        Shuffle(quirky);
        for (int i = 0; i < Math.Min(8, quirky.Count); i++)
            offered[J.GetStr(quirky[i], "text")] = quirky[i];
        return offered;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    // v11: 単語は散らばさず、カテゴリー別タブ＋整列グリッドで提示する
    private void BuildPalette(Dictionary<string, Dictionary<string, object>> offered)
    {
        paletteGroups.Clear();
        foreach (var w in offered.Values)
        {
            string cat = J.GetStr(w, "category");
            List<Dictionary<string, object>> list;
            if (!paletteGroups.TryGetValue(cat, out list))
            {
                list = new List<Dictionary<string, object>>();
                paletteGroups[cat] = list;
            }
            list.Add(w);
        }
        // タブは words.json の categories の順に並べる
        var catOrder = new List<string>();
        var catLabels = new Dictionary<string, string>();
        foreach (var co in J.GetList(dataWords, "categories"))
        {
            var c = J.Dict(co);
            string id = J.GetStr(c, "id");
            catOrder.Add(id);
            catLabels[id] = J.GetStr(c, "label", id);
        }
        foreach (var cat in paletteGroups.Keys)
            if (!catOrder.Contains(cat)) catOrder.Add(cat);
        paletteCatOrder.Clear();
        paletteCatOrder.AddRange(catOrder);

        // タブ: すべて / お気に入り / カテゴリー別
        var tabDefs = new List<KeyValuePair<string, string>>();
        tabDefs.Add(new KeyValuePair<string, string>("__all", "すべて"));
        tabDefs.Add(new KeyValuePair<string, string>("__fav", "★ お気に入り"));
        foreach (var cat in catOrder)
        {
            List<Dictionary<string, object>> list;
            if (!paletteGroups.TryGetValue(cat, out list) || list.Count == 0) continue;
            string label;
            if (!catLabels.TryGetValue(cat, out label)) label = cat;
            tabDefs.Add(new KeyValuePair<string, string>(cat, label));
        }
        for (int i = tabsArea.childCount - 1; i >= 0; i--)
            Destroy(tabsArea.GetChild(i).gameObject);
        tabButtons.Clear();
        float x = 0f, ty = 0f;
        foreach (var def in tabDefs)
        {
            string catId = def.Key;
            float tw = def.Value.Length * 18f + 28f;
            // タブが横に収まらない場合は2行目に折り返す（検索欄と重ねない）
            if (x > 0f && x + tw > 690f) { x = 0f; ty += 30f; }
            var tab = MkButton(tabsArea, def.Value, 15, x, ty, tw, 28, () =>
            {
                activeCategory = catId;
                palettePage = 0;
                RefreshTabs();
                RenderPaletteGrid();
            });
            tabButtons.Add(new KeyValuePair<string, Button>(catId, tab));
            x += tw + 6f;
        }
        if (activeCategory == "" ||
            (activeCategory != "__all" && activeCategory != "__fav" && !paletteGroups.ContainsKey(activeCategory)))
            activeCategory = "__all";
        RefreshTabs();
        RenderPaletteGrid();
    }

    private void RefreshTabs()
    {
        foreach (var kv in tabButtons)
        {
            var img = kv.Value.GetComponent<Image>();
            if (img == null) continue;
            bool active = kv.Key == activeCategory;
            // v26: タブも白背景＋黒文字にする。選択中だけ淡い緑で示す。
            img.color = active
                ? new Color(0.78f, 0.90f, 0.80f, 1f)
                : new Color(1f, 1f, 1f, 1f);
            var lab = kv.Value.GetComponentInChildren<Text>();
            if (lab != null) lab.color = active
                ? new Color(0.16f, 0.35f, 0.20f, 1f)
                : new Color(0.25f, 0.22f, 0.18f, 1f);
        }
    }

    private void ChangePalettePage(int dir)
    {
        palettePage += dir;
        RenderPaletteGrid();
    }

    private void RenderPaletteGrid()
    {
        for (int i = paletteArea.childCount - 1; i >= 0; i--)
            Destroy(paletteArea.GetChild(i).gameObject);
        List<Dictionary<string, object>> items;
        if (!string.IsNullOrEmpty(searchText))
        {
            // 検索中は全単語から部分一致で探す
            items = new List<Dictionary<string, object>>();
            foreach (var wo in J.GetList(dataWords, "words"))
            {
                var w = J.Dict(wo);
                if (J.GetStr(w, "text").Contains(searchText)) items.Add(w);
            }
        }
        else if (activeCategory == "__all")
        {
            items = new List<Dictionary<string, object>>();
            foreach (var cat in paletteCatOrder)
            {
                List<Dictionary<string, object>> l;
                if (paletteGroups.TryGetValue(cat, out l)) items.AddRange(l);
            }
        }
        else if (activeCategory == "__fav")
        {
            // お気に入りは、今回提示されていない単語からも選べる
            items = new List<Dictionary<string, object>>();
            foreach (var wo in J.GetList(dataWords, "words"))
            {
                var w = J.Dict(wo);
                if (favoriteWords.Contains(J.GetStr(w, "text"))) items.Add(w);
            }
        }
        else if (!paletteGroups.TryGetValue(activeCategory, out items)) return;
        // 1ページは最大24語（6列×4行）。単語を詰め込まず、ボタンと文字を
        // 読みやすい大きさに保ち、超えた分はページ送りで表示する。
        const int pageSize = 24;
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)pageSize));
        palettePage = Mathf.Clamp(palettePage, 0, pageCount - 1);
        if (palettePageLabel != null)
            palettePageLabel.text = items.Count == 0 ? "—" : (palettePage + 1) + " / " + pageCount;
        items = items.GetRange(palettePage * pageSize, Mathf.Min(pageSize, items.Count - palettePage * pageSize));
        var catColors = new Dictionary<string, string>();
        foreach (var co in J.GetList(dataWords, "categories"))
        {
            var c = J.Dict(co);
            catColors[J.GetStr(c, "id")] = J.GetStr(c, "color", "#ffffff");
        }
        // 文章に選ばれている単語は、パレット側でも色を変えて示す
        var selectedCounts = new Dictionary<string, int>();
        foreach (var tok in selected)
        {
            string tt = J.GetStr(tok, "text");
            int cc;
            selectedCounts.TryGetValue(tt, out cc);
            selectedCounts[tt] = cc + 1;
        }
        paletteButtons.Clear();
        int n = items.Count;
        int cols = n <= 2 ? Math.Max(1, n) : (n <= 6 ? 3 : 6);
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));
        var paSize = ((RectTransform)paletteArea).sizeDelta;
        float cellW = paSize.x / cols;
        float cellH = Mathf.Min(44f, paSize.y / rows);
        float bh = Mathf.Max(24f, cellH - 6f);
        int wordFont = bh >= 36f ? 20 : (bh >= 28f ? 17 : 15);
        // ボタン幅は全てそろえ、間隔を均一にする
        float bw = Mathf.Min(cellW - 10f, 300f);
        for (int idx = 0; idx < n; idx++)
        {
            var w = items[idx];
            string text = J.GetStr(w, "text");
            string hex;
            if (!catColors.TryGetValue(J.GetStr(w, "category"), out hex)) hex = "#ffffff";
            Color catCol = HexColor(hex, Color.white);
            int selCount;
            selectedCounts.TryGetValue(text, out selCount);
            var token = w;
            var b = MkButton(paletteArea, text, wordFont, 0, 0, 60, 34, () => AddWord(token));
            paletteButtons.Add(new KeyValuePair<string, Button>(text, b));
            var img = b.GetComponent<Image>();
            // v26: 単語ボタンは白背景＋黒文字にする。選ばれている単語は淡い緑で示す。
            // （v25の紺背景＋白文字は「文字の背景が黒い」ように見えて重かったため）
            img.color = selCount > 0 ? new Color(0.78f, 0.90f, 0.80f, 1f) : new Color(1f, 1f, 1f, 1f);
            var label = b.GetComponentInChildren<Text>();
            label.color = new Color(0.22f, 0.18f, 0.14f, 1f);
            if (selCount >= 2) label.text = text + " ×" + selCount;
            // v23: BestFitをやめ、文字量でフォントを決める。「×2」が入らない場合は回数表示を落とす
            label.resizeTextForBestFit = false;
            string labelText = label.text;
            int pf = FitFontForBox(labelText, wordFont, 11, bw - 30f, bh);
            if (LinesFor(labelText, pf, bw - 30f) * pf * 1.50f > bh) label.text = text; // v29
            label.fontSize = FitFontForBox(label.text, wordFont, 11, bw - 30f, bh);
            var rt = b.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(bw, bh);
            // 文字の領域は、左端の色帯と右端の星を避けた範囲にする
            label.rectTransform.sizeDelta = new Vector2(bw - 34f, bh);
            label.rectTransform.anchoredPosition = new Vector2(10f, 0f);
            float cx = (idx % cols) * cellW + (cellW - bw) / 2f;
            float cy = (idx / cols) * cellH + (cellH - bh) / 2f;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx + bw / 2f, -(cy + bh / 2f));
            // 左端の色帯でカテゴリーを示す
            var stripe = MkRect(b.transform, catCol, 0, 4, 5, bh - 8);
            stripe.raycastTarget = false;
            // 右端の星でお気に入りに登録/解除
            bool fav = favoriteWords.Contains(text);
            string wordText = text;
            float ss = Mathf.Min(26f, bh - 2f);
            var star = MkButton(b.transform, fav ? "★" : "☆", ss < 26f ? 11 : 14, bw - ss - 2f, (bh - ss) / 2f, ss, ss, () => ToggleFavorite(wordText));
            star.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var sl = star.GetComponentInChildren<Text>();
            if (sl != null) sl.color = fav ? new Color(0.75f, 0.55f, 0.10f) : new Color(0.45f, 0.45f, 0.50f); // v26: 白背景の上で見える濃い色に
        }
    }

    // 文章の選択状態を、パレットの単語ボタンの色・表示に反映する（再描画はしない）
    private void RefreshPaletteSelection()
    {
        if (paletteButtons.Count == 0) return;
        var counts = new Dictionary<string, int>();
        foreach (var tok in selected)
        {
            string tt = J.GetStr(tok, "text");
            int cc;
            counts.TryGetValue(tt, out cc);
            counts[tt] = cc + 1;
        }
        foreach (var kv in paletteButtons)
        {
            if (kv.Value == null) continue;
            int c;
            counts.TryGetValue(kv.Key, out c);
            var img = kv.Value.GetComponent<Image>();
            if (img != null)
                img.color = c > 0 ? new Color(0.78f, 0.90f, 0.80f, 1f) : new Color(1f, 1f, 1f, 1f); // v26: 白背景＋黒文字
            var lab = kv.Value.GetComponentInChildren<Text>();
            if (lab != null) { lab.text = c >= 2 ? kv.Key + " ×" + c : kv.Key; lab.color = new Color(0.22f, 0.18f, 0.14f, 1f); }
        }
    }

    private void ToggleFavorite(string text)
    {
        if (!favoriteWords.Add(text)) favoriteWords.Remove(text);
        SaveAffection();
        RenderPaletteGrid();
    }

    // v27: 選べる単語は12個まで。無制限だと発言プレビューが3行（13pxで約90文字）に
    // 収まらず「…」で切れ、チップもボタン列にはみ出す。12個あれば十分な長さの
    // 文章が作れる（平均4文字×12語＝48文字）。上限に達したら、それ以上は選べない。
    private const int MaxSelectedWords = 12;
    private void AddWord(Dictionary<string, object> token)
    {
        if (selected.Count >= MaxSelectedWords)
        {
            if (resultLabel != null)
                SetFittedText(resultLabel, "単語は12個までです。いらない単語はチップの × で消せます。", 13, 10);
            return;
        }
        selected.Add(new Dictionary<string, object>(token));
        selectedChip = selected.Count - 1;
        RefreshSentence();
    }

    private void ClearSelected()
    {
        selected.Clear();
        selectedChip = -1;
        RefreshSentence();
    }

    private void RefreshSentence()
    {
        RefreshPaletteSelection();
        if (selected.Count == 0)
        {
            for (int i = chipsGroup.childCount - 1; i >= 0; i--)
                Destroy(chipsGroup.GetChild(i).gameObject);
            sentenceLabel.text = "（まだ何も選んでいません）";
            LayoutSentence();
            return;
        }
        var parts = new List<string>();
        foreach (var w in selected) parts.Add(J.GetStr(w, "text"));
        sentenceLabel.text = "「" + string.Join("", parts) + "」";
        LayoutSentence();
        BuildChips(false);
    }

    // 選択中カードの描画。2行に収まらない場合は小型カードにして3行以内に収める（下のボタン類と重ねない）
    private void BuildChips(bool compact)
    {
        for (int i = chipsGroup.childCount - 1; i >= 0; i--)
            Destroy(chipsGroup.GetChild(i).gameObject);
        float chipH = compact ? 23f : 32f;
        int font = compact ? 13 : 17;
        float pitch = compact ? 25f : 36f;
        float pad = compact ? 30f : 40f;
        float x = 0f, y = 0f;
        int row = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            string text = J.GetStr(selected[i], "text");
            int idx = i;
            // カード本体を押すと選択（◀▶で並べ替え）、右端の × で削除
            var chip = MkButton(chipsGroup, text, font, x, y, 60, chipH, () => { selectedChip = idx; RefreshSentence(); });
            var label = chip.GetComponentInChildren<Text>();
            // v26: チップも白背景＋黒文字にする（暗い発言パネルの上で、白いチップがよく見える）
            if (label != null) label.color = new Color(0.22f, 0.18f, 0.14f, 1f);
            var chipImg = chip.GetComponent<Image>();
            if (chipImg != null) chipImg.color = new Color(1f, 1f, 1f, 1f);
            float cw = Mathf.Max(label.preferredWidth + pad, text.Length * (font - 2) + pad);
            var rt = chip.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(cw, chipH);
            label.rectTransform.sizeDelta = new Vector2(cw, chipH);
            if (i == selectedChip)
            {
                if (chipImg != null) chipImg.color = new Color(0.78f, 0.90f, 0.80f, 1f);
            }
            float delSize = compact ? 19f : 26f;
            var del = MkButton(chip.transform, "×", compact ? 11 : 14, cw - delSize - 2f, (chipH - delSize) / 2f, delSize, delSize, () =>
            {
                selected.RemoveAt(idx);
                if (selectedChip >= selected.Count) selectedChip = selected.Count - 1;
                RefreshSentence();
            });
            del.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var dl = del.GetComponentInChildren<Text>();
            if (dl != null) dl.color = new Color(0.40f, 0.38f, 0.35f); // v26: 白いチップの上で見える濃い色に
            x += cw + 6f;
            if (x > 424f && i + 1 < selected.Count)
            {
                x = 0f;
                y += pitch;
                row++;
            }
        }
        if (!compact && row >= 2) BuildChips(true);
    }

    private void MoveChip(int dir)
    {
        if (selectedChip < 0) return;
        int j = selectedChip + dir;
        if (j < 0 || j >= selected.Count) return;
        var tmp = selected[selectedChip];
        selected[selectedChip] = selected[j];
        selected[j] = tmp;
        selectedChip = j;
        RefreshSentence();
    }

    private void DeleteChip()
    {
        if (selectedChip < 0 || selectedChip >= selected.Count) return;
        selected.RemoveAt(selectedChip);
        if (selectedChip >= selected.Count) selectedChip = selected.Count - 1;
        RefreshSentence();
    }

    private void UndoLast()
    {
        if (selected.Count == 0) return;
        selected.RemoveAt(selected.Count - 1);
        if (selectedChip >= selected.Count) selectedChip = selected.Count - 1;
        RefreshSentence();
    }

    private void DoSpeak(bool isTimeout)
    {
        if (phase != "ask") return;
        var res = Evaluator.Evaluate(selected, currentChar, activeQuestion, evalCfg);
        var ptexts = new List<string>();
        foreach (var tk in selected) ptexts.Add(J.GetStr(tk, "text"));
        LogLine("あなた", ptexts.Count > 0 ? "「" + string.Join("", ptexts) + "」" : "（無言）");
        string cid = J.GetStr(currentChar, "id");
        // v10: 助詞による文章解析（関係・解釈・理解度）。好感度とは独立に判定し、
        // 補正は解釈の内容に対してのみ小さく加える
        var ana = SentenceAnalyzer.Analyze(selected, currentChar, activeQuestion, evalCfg, AffectionOf(cid), res);
        int delta = J.GetInt(res, "score") + ana.modifier;
        // 好感度対策: すでにマイナスの相手は、さらに大きくは下がりにくい（掘り下げ防止）
        if (delta < 0 && AffectionOf(cid) < 0) delta = delta / 2;
        affection[cid] = Mathf.Clamp(AffectionOf(cid) + delta, -20, 100);
        SaveAffection();
        turnCount++;
        phase = "reaction";
        string key = Evaluator.ReactionKey(delta);
        bool confused = J.GetBool(res, "confused");
        bool offtopic = J.GetBool(res, "offtopic");
        bool minimal = J.GetBool(res, "minimal");
        // 反応の優先順位: 崩れた文 → 現実には難しい関係（笑い） → 聞き返し →
        // 話題逸脱（質問の形のときは通常反応） → 一言返事 → 好感度段位
        string rkey = key;
        if (confused) rkey = "confused";
        else if (ana.absurd) rkey = "absurd";
        else if (ana.clarify) rkey = "clarify";
        else if (offtopic && !ana.questionForm) rkey = "offtopic";
        else if (minimal) rkey = "minimal";
        // 表情は反応キーに連動させる
        if (rkey == "confused" || rkey == "offtopic" || rkey == "clarify") SetExpression("neutral");
        else if (rkey == "absurd") SetExpression("joy");
        else if (rkey == "minimal") SetExpression("sad");
        else
        {
            string expr = "default";
            if (key == "great") expr = "fun";
            else if (key == "good") expr = "joy";
            else if (key == "bad") expr = "sad";
            else if (key == "terrible") expr = "angry";
            SetExpression(expr);
        }
        // 面白さは好感度とは別の物差しで動く（笑える失敗は好感度が低くても面白い）
        int funDelta = 0;
        if (ana.absurd) funDelta += 8;
        if (rkey == "great") funDelta += 5;
        else if (rkey == "good") funDelta += 3;
        else if (rkey == "confused" || rkey == "clarify") funDelta += 2;
        else if (rkey == "offtopic") funDelta += 1;
        else if (rkey == "terrible") funDelta -= 3;
        fun[cid] = Mathf.Clamp(FunOf(cid) + funDelta, 0, 100);
        // 信頼度はゆっくり積み上がり、傷つける発言で下がる
        int trustDelta = 0;
        if (rkey == "great") trustDelta += 2;
        else if (rkey == "good") trustDelta += 1;
        else if (ana.absurd) trustDelta += 1;
        else if (rkey == "terrible") trustDelta -= 2;
        trust[cid] = Mathf.Clamp(TrustOf(cid) + trustDelta, 0, 100);
        SaveAffection();
        var reactions = J.GetList(J.GetDict(currentChar, "reactions"), rkey);
        string reaction = reactions.Count > 0 ? J.Str(reactions[turnCount % reactions.Count], "……") : "……";
        ShowStoryBox();
        SetSpeaker(true);
        ShowDialogue("「" + reaction + "」", J.GetStr(currentChar, "name"));
        var reasons = res["reasons"] as List<string> ?? new List<string>();
        // v27: 評価カードの結果欄は 168x112（13pxで約66文字）しかないため、
        // 理由が5〜8個並ぶと「…」で切れてしまう。表示は先頭3件までにして、
        // 全文はバックログに残す（LogLine は全理由を出す）。
        var rparts = new List<string>();
        foreach (var r in reasons.GetRange(0, Mathf.Min(3, reasons.Count))) rparts.Add("・" + r);
        string head = "好感度 " + (delta >= 0 ? "+" + delta : delta.ToString()) + " → " + AffectionOf(cid)
            + " ｜ 面白さ " + (funDelta >= 0 ? "+" + funDelta : funDelta.ToString()) + " → " + FunOf(cid);
        if (isTimeout) head = "⏱ 時間切れで無言になってしまった… " + head;
        if (J.GetBool(res, "scene_change"))
        {
            pendingTurn = true;
            head += " / 転換の言葉で次の話題が変わりそうだ";
        }
        personalityLabel.text = head;
        LayoutBubble();
        if (evalAffLabel != null)
        {
            evalAffLabel.text = "♥ 好感度 " + (delta >= 0 ? "+" + delta : delta.ToString()) + " → " + AffectionOf(cid);
            evalFunLabel.text = "😊 面白さ " + (funDelta >= 0 ? "+" + funDelta : funDelta.ToString()) + " → " + FunOf(cid);
            evalTrustLabel.text = "🤝 信頼度 " + (trustDelta >= 0 ? "+" + trustDelta : trustDelta.ToString()) + " → " + TrustOf(cid);
            SetFittedText(resultLabel, string.Join(" ", rparts), 13, 10);
        }
        LogLine("評価", head + " " + string.Join(" ", rparts) + " ｜ 解析: " + ana.Summary());
        selected.Clear();
        selectedChip = -1;
        speakButton.interactable = false;
        nextButton.gameObject.SetActive(true);
        PortraitReact(delta);
        RefreshCharPanel();
        RefreshSentence();
    }

    private void SetExpression(string expr)
    {
        var exprs = J.GetDict(currentChar, "expressions");
        if (exprs.Count != 0)
        {
            string path = "";
            if (expr == "default") path = J.GetStr(currentChar, "portrait");
            else path = J.GetStr(exprs, expr);
            if (path != "")
            {
                var tex = LoadTex(path);
                if (tex != null) SetPortraitTex(tex);
            }
        }
        SetupBlink(expr);
    }

    // 瞬きフレームが登録されている表情なら、瞬きアニメーションを開始する
    private void SetupBlink(string expr)
    {
        StopBlink();
        var blink = J.GetDict(currentChar, "blink");
        if (blink.Count == 0) return;
        var sets = J.GetList(blink, expr);
        if (sets.Count == 0) return;
        var set = sets[UnityEngine.Random.Range(0, sets.Count)] as System.Collections.Generic.List<object>;
        if (set == null) return;
        var frames = new System.Collections.Generic.List<Texture2D>();
        foreach (var o in set)
        {
            string fp = o as string;
            if (fp == null || fp == "") continue;
            var t = LoadTex(fp);
            if (t != null) frames.Add(t);
        }
        if (frames.Count < 2) return;
        blinkFrames = frames;
        blinkWait = UnityEngine.Random.Range(1.0f, 2.4f);
        SetPortraitTex(frames[0]);
    }

    private void StopBlink()
    {
        blinkFrames = null;
        blinkIndex = -1;
        blinkTimer = 0f;
    }

    // 瞬きの進行: しばらく目を開けて待ち、フレーム列を1回再生して、また待つ
    private void TickBlink()
    {
        if (blinkFrames == null || portraitImg == null) return;
        blinkTimer += Time.deltaTime;
        if (blinkIndex < 0)
        {
            if (blinkTimer >= blinkWait)
            {
                blinkTimer = 0f;
                blinkIndex = 1;
                portraitImg.texture = blinkFrames[blinkIndex];
            }
        }
        else if (blinkTimer >= 0.11f)
        {
            blinkTimer = 0f;
            blinkIndex++;
            if (blinkIndex >= blinkFrames.Count)
            {
                blinkIndex = -1;
                portraitImg.texture = blinkFrames[0];
                blinkWait = UnityEngine.Random.Range(2.2f, 4.6f);
            }
            else portraitImg.texture = blinkFrames[blinkIndex];
        }
    }

    private void PortraitReact(int delta)
    {
        if (portraitAnim != null) StopCoroutine(portraitAnim);
        portraitAnim = StartCoroutine(PortraitAnim(delta));
    }

    private IEnumerator PortraitAnim(int delta)
    {
        var rt = portraitImg.rectTransform;
        float t = 0f;
        if (delta >= 1)
        {
            rt.localScale = new Vector3(1.04f, 1.04f, 1f);
            while (t < 1f)
            {
                t += Time.deltaTime / 0.35f;
                float s = Mathf.Lerp(1.04f, 1f, Mathf.Clamp01(t));
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }
        else
        {
            rt.anchoredPosition = new Vector2(0, -6);
            while (t < 1f)
            {
                t += Time.deltaTime / 0.35f;
                rt.anchoredPosition = new Vector2(0, Mathf.Lerp(-6f, 0f, Mathf.Clamp01(t)));
                yield return null;
            }
            rt.anchoredPosition = Vector2.zero;
        }
        portraitAnim = null;
    }
}
