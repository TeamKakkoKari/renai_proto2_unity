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
    private Image bgSky;
    private Image bgGround;
    private RawImage bgArt;
    private RectTransform portraitBox;
    private RawImage portraitImg;
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
    private RectTransform timerFillRt;
    private float timerBarW = 944f;
    private Image timerFill;
    private Text sentenceLabel;
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
    private const float PortraitY = 8f;

    private string SavePath { get { return Path.Combine(Application.persistentDataPath, "affection.json"); } }

    // ---------- 起動・データ ----------
    private void Awake()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        root = new GameObject("Root").AddComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

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

    private RectTransform SetRect(RectTransform rt, float x, float y, float w, float h)
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

    private Text MkLabel(Transform parent, string text, int size, Color color, float x, float y, float w, float h, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        var go = NewObj(parent, "Label");
        var t = go.AddComponent<Text>();
        t.font = jpFont;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
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

    // 文字の縁取り（背景が写真・イラストでも読みやすくする）
    private void AddOutline(Text t, Color c, float d)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = c;
        o.effectDistance = new Vector2(d, -d);
    }

    // 文字の影（立体感と視認性の補助）
    private void AddShadowFx(Text t, Color c, float d)
    {
        var s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = c;
        s.effectDistance = new Vector2(d, -d);
    }

    private Image MkPanel(Transform parent, Color color, float x, float y, float w, float h)
    {
        var go = NewObj(parent, "Panel");
        var img = go.AddComponent<Image>();
        img.sprite = RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
        SetRect(img.rectTransform, x, y, w, h);
        return img;
    }

    private Image MkRect(Transform parent, Color color, float x, float y, float w, float h)
    {
        var go = NewObj(parent, "Rect");
        var img = go.AddComponent<Image>();
        img.color = color;
        SetRect(img.rectTransform, x, y, w, h);
        return img;
    }


    // アンカー指定版（座標は通常版と同じ設計座標で渡す）
    private Text MkLabelA(Transform parent, string text, int size, Color color, float x, float y, float w, float h, float ax, float ay, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        var t = MkLabel(parent, text, size, color, x, y, w, h, anchor);
        SetRectA(t.rectTransform, ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return t;
    }

    private Image MkPanelA(Transform parent, Color color, float x, float y, float w, float h, float ax, float ay)
    {
        var img = MkPanel(parent, color, x, y, w, h);
        SetRectA(img.rectTransform, ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return img;
    }

    private Image MkRectA(Transform parent, Color color, float x, float y, float w, float h, float ax, float ay)
    {
        var img = MkRect(parent, color, x, y, w, h);
        SetRectA(img.rectTransform, ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return img;
    }

    // 全面に広がるパネル/矩形
    private Image MkPanelStretch(Transform parent, Color color)
    {
        var img = MkPanel(parent, color, 0, 0, 1280, 720);
        SetRectStretch(img.rectTransform, 0, 0, 0, 0);
        return img;
    }

    private Image MkRectStretch(Transform parent, Color color)
    {
        var img = MkRect(parent, color, 0, 0, 1280, 720);
        SetRectStretch(img.rectTransform, 0, 0, 0, 0);
        return img;
    }

    // 上端に張り付いて横いっぱいに広がる帯
    private Image MkPanelTopBand(Transform parent, Color color, float height)
    {
        var img = MkPanel(parent, color, 0, 0, 1280, height);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(0, -height);
        rt.offsetMax = Vector2.zero;
        return img;
    }

    // 下端に張り付いて横いっぱいに広がる帯
    private Image MkPanelBottomBand(Transform parent, Color color, float height)
    {
        var img = MkPanel(parent, color, 0, 720 - height, 1280, height);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0, height);
        return img;
    }

    // 下端に張り付いて横いっぱいに広がる矩形
    private Image MkRectBottomBand(Transform parent, Color color, float height)
    {
        var img = MkRect(parent, color, 0, 720 - height, 1280, height);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0, height);
        return img;
    }

    // 横いっぱいに広がるラベル（中央寄せタイトルなど）
    private Text MkLabelWide(Transform parent, string text, int size, Color color, float y, float h, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var t = MkLabel(parent, text, size, color, 0, y, 1280, h, anchor);
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

    private Button MkButton(Transform parent, string text, int fontSize, float x, float y, float w, float h, Action onClick)
    {
        var go = NewObj(parent, "Button");
        var img = go.AddComponent<Image>();
        img.sprite = RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.23f, 0.24f, 0.35f, 1f);
        SetRect(img.rectTransform, x, y, w, h);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.88f, 0.92f, 1f, 1f);
        cb.pressedColor = new Color(0.7f, 0.74f, 0.88f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.45f, 0.45f, 0.5f, 0.55f);
        btn.colors = cb;
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
        var label = MkLabel(go.transform, text, fontSize, Color.white, 0, 0, w, h, TextAnchor.MiddleCenter);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        if (onClick != null) btn.onClick.AddListener(() => { Deselect(); onClick(); });
        return btn;
    }


    private Button MkButtonA(Transform parent, string text, int fontSize, float x, float y, float w, float h, Action onClick, float ax, float ay)
    {
        var btn = MkButton(parent, text, fontSize, x, y, w, h, onClick);
        SetRectA(btn.GetComponent<RectTransform>(), ax, ay, AnchorX(ax, x, w), AnchorY(ay, y, h), w, h);
        return btn;
    }

    private void AddPointerDown(GameObject go, Action a)
    {
        var et = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entry.callback.AddListener(delegate { a(); });
        et.triggers.Add(entry);
    }

    private void AddPointerEnter(GameObject go, Action a)
    {
        var et = go.AddComponent<EventTrigger>();
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
        bgSky = MkRectStretch(root, Color.white);
        bgSky.raycastTarget = false;
        bgGround = MkRectBottomBand(root, Color.white, 160);
        bgGround.raycastTarget = false;
        var bgArtGo = NewObj(root, "BgArt");
        bgArt = bgArtGo.AddComponent<RawImage>();
        bgArt.raycastTarget = false;
        bgArtGo.SetActive(false);

        portraitBox = SetRect(NewObj(root, "PortraitBox").GetComponent<RectTransform>(), 450, PortraitY, 380, 712);
        // 立ち絵は画面上端の中央にアンカーする（横幅が変わっても中央を保つ）
        portraitBox.anchorMin = new Vector2(0.5f, 1);
        portraitBox.anchorMax = new Vector2(0.5f, 1);
        portraitBox.pivot = new Vector2(0.5f, 1);
        portraitBox.anchoredPosition = new Vector2(0, -PortraitY);
        var pgo = NewObj(portraitBox, "Portrait");
        portraitImg = pgo.AddComponent<RawImage>();
        portraitImg.raycastTarget = false;
        var prt = portraitImg.rectTransform;
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;

        // 全面クリックで進む（ボタン類より下の層に置く）
        var catcher = MkRectStretch(root, new Color(0, 0, 0, 0));
        AddPointerDown(catcher.gameObject, OnClickAdvance);

        // 上部: 浮いたピル型ステータス表示（参考画像風）
        var top = SetRect(NewObj(root, "TopBar").GetComponent<RectTransform>(), 0, 0, 1280, 720).transform;
        MkButton(top, "🗺 マップ", 16, 16, 8, 120, 40, ShowMap);
        var titleLbl = MkLabel(top, "ことばを紡ぐ恋愛シミュレーション", 16, new Color(1, 1, 1, 0.85f), 148, 15, 420, 22);
        titleLbl.raycastTarget = false;
        AddOutline(titleLbl, new Color(0, 0, 0, 0.55f), 1.5f);
        var affPill = MkPanelA(top, new Color(0.08f, 0.08f, 0.14f, 0.78f), 588, 8, 240, 40, 1, 1);
        affPill.raycastTarget = false;
        affectionLabel = MkLabel(affPill.transform, "", 16, new Color(1f, 0.88f, 0.92f), 10, 4, 220, 20);
        affectionLabel.raycastTarget = false;
        AddOutline(affectionLabel, new Color(0, 0, 0, 0.45f), 1f);
        var affBarBg = MkRect(affPill.transform, new Color(0, 0, 0, 0.45f), 10, 26, 220, 6);
        affBarBg.raycastTarget = false;
        var affFill = MkRect(affPill.transform, new Color(0.45f, 0.9f, 0.55f), 10, 26, 220, 6);
        affFill.raycastTarget = false;
        affFillRt = affFill.rectTransform;
        var funPill = MkPanelA(top, new Color(0.08f, 0.08f, 0.14f, 0.78f), 836, 8, 240, 40, 1, 1);
        funPill.raycastTarget = false;
        funLabel = MkLabel(funPill.transform, "", 16, new Color(1f, 0.92f, 0.65f), 10, 4, 220, 20);
        funLabel.raycastTarget = false;
        AddOutline(funLabel, new Color(0, 0, 0, 0.45f), 1f);
        var funBarBg = MkRect(funPill.transform, new Color(0, 0, 0, 0.45f), 10, 26, 220, 6);
        funBarBg.raycastTarget = false;
        var funFill = MkRect(funPill.transform, new Color(1f, 0.75f, 0.3f), 10, 26, 220, 6);
        funFill.raycastTarget = false;
        funFillRt = funFill.rectTransform;
        MkButtonA(top, "📖 思い出", 15, 1084, 8, 96, 40, ToggleBacklog, 1, 1);

        BuildTimerBar();
        BuildFrame();
        BuildEvalCard();
        BuildTitleScreen();
        BuildMapScreen();
        BuildBacklog();
    }

    private void BuildTimerBar()
    {
        // 制限時間バーは単語パレットの真上に置く
        timerLabel = MkLabelA(root, "", 16, new Color(1f, 0.95f, 0.85f), 16, 408, 300, 22, 0, 0, TextAnchor.UpperLeft);
        AddOutline(timerLabel, new Color(0, 0, 0, 0.55f), 1.5f);
        timerBg = MkRectA(root, new Color(0.05f, 0.05f, 0.1f, 0.75f), 16, 434, 944, 10, 0, 0);
        timerBg.raycastTarget = false;
        var fill = MkRectA(root, new Color(0.45f, 0.9f, 0.5f), 16, 434, 944, 10, 0, 0);
        fill.raycastTarget = false;
        timerFill = fill;
        timerFillRt = fill.rectTransform;
        SetTimerVisible(false);
    }

    private void BuildEvalCard()
    {
        // 右下の「今回の評価」カード（参考画像風）
        var card = MkPanelA(root, new Color(0.98f, 0.96f, 0.90f, 0.97f), 976, 448, 288, 256, 1, 0);
        card.raycastTarget = false;
        var ct = card.transform;
        var title = MkLabel(ct, "✨ 今回の評価 ✨", 17, new Color(0.35f, 0.28f, 0.15f), 0, 10, 288, 24, TextAnchor.MiddleCenter);
        title.raycastTarget = false;
        evalAffLabel = MkLabel(ct, "♥ 好感度　―", 18, new Color(0.30f, 0.22f, 0.18f), 16, 42, 256, 24);
        evalFunLabel = MkLabel(ct, "😊 面白さ　―", 18, new Color(0.30f, 0.22f, 0.18f), 16, 70, 256, 24);
        evalTrustLabel = MkLabel(ct, "🤝 信頼度　―", 18, new Color(0.30f, 0.22f, 0.18f), 16, 98, 256, 24);
        var miniGo = NewObj(ct, "EvalMini");
        SetRect(miniGo.GetComponent<RectTransform>(), 16, 130, 76, 112);
        evalMini = miniGo.AddComponent<RawImage>();
        evalMini.raycastTarget = false;
        resultLabel = MkLabel(ct, "", 13, new Color(0.32f, 0.28f, 0.22f), 104, 130, 168, 112);
        FitText(resultLabel, 10, 13);
    }

    private void SetTimerVisible(bool v)
    {
        timerLabel.gameObject.SetActive(v);
        timerBg.gameObject.SetActive(v);
        timerFill.gameObject.SetActive(v);
    }

    private void BuildFrame()
    {
        // 画面全体を覆う透明な土台（中身は吹き出し・発言パネル・単語パレットに分離）
        var panel = MkPanelStretch(root, new Color(0, 0, 0, 0));
        panel.raycastTarget = false;
        msgPanel = panel.rectTransform;
        var pt = panel.transform;

        // 左上の吹き出し
        var bubble = MkPanel(pt, new Color(0.98f, 0.96f, 0.90f, 0.97f), 16, 92, 344, 196);
        bubble.raycastTarget = false;
        nameLabel = MkLabel(pt, "", 23, new Color(0.25f, 0.45f, 0.25f), 32, 102, 312, 28);
        personalityLabel = MkLabel(pt, "", 13, new Color(0.5f, 0.45f, 0.38f), 32, 132, 312, 18);
        FitText(personalityLabel, 10, 13);
        dialogueLabel = MkLabel(pt, "", 22, new Color(0.22f, 0.18f, 0.14f), 32, 154, 312, 92);
        FitText(dialogueLabel, 13, 22);
        AddShadowFx(dialogueLabel, new Color(0, 0, 0, 0.15f), 1f);
        nextButton = MkButton(pt, "つぎへ ▼", 16, 216, 240, 128, 36, OnNext);

        // 選択欄: 右の発言パネル＋下の単語パレット（参考画像の構成）
        askGroup = NewObj(pt, "AskGroup");
        SetRectStretch(askGroup.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var at = askGroup.transform;
        var sayPanel = MkPanelA(at, new Color(0.10f, 0.11f, 0.18f, 0.96f), 800, 92, 464, 340, 1, 1);
        sayPanel.raycastTarget = false;
        MkLabelA(at, "✏ あなたの発言", 15, new Color(0.75f, 0.8f, 0.95f), 820, 106, 300, 20, 1, 1);
        var chipsBg = MkRectA(at, new Color(0, 0, 0, 0.20f), 816, 130, 432, 82, 1, 1);
        chipsBg.raycastTarget = false;
        chipsGroup = SetRectA(NewObj(at, "Chips").GetComponent<RectTransform>(), 1, 1, AnchorX(1, 820, 424), AnchorY(1, 134, 74), 424, 74);
        MkButtonA(at, "◀", 15, 820, 216, 40, 30, () => MoveChip(-1), 1, 1);
        MkButtonA(at, "▶", 15, 864, 216, 40, 30, () => MoveChip(1), 1, 1);
        MkButtonA(at, "削除", 15, 908, 216, 64, 30, DeleteChip, 1, 1);
        MkButtonA(at, "↩ 一つ戻す", 14, 976, 216, 118, 30, UndoLast, 1, 1);
        MkButtonA(at, "全解除", 15, 1098, 216, 146, 30, ClearSelected, 1, 1);
        var sentenceBg = MkRectA(at, new Color(1, 1, 1, 0.08f), 816, 248, 432, 42, 1, 1);
        sentenceBg.raycastTarget = false;
        sentenceLabel = MkLabelA(at, "（まだ何も選んでいません）", 22, new Color(0.9f, 0.97f, 1f), 820, 252, 424, 34, 1, 1);
        FitText(sentenceLabel, 13, 22);
        AddOutline(sentenceLabel, new Color(0, 0, 0, 0.5f), 1f);
        speakButton = MkButtonA(at, "✨ 発言する ▶", 24, 820, 296, 424, 54, () => DoSpeak(false), 1, 1);
        speakButton.GetComponent<Image>().color = new Color(0.32f, 0.58f, 0.34f);
        var palPanel = MkPanelA(at, new Color(0.98f, 0.96f, 0.90f, 0.97f), 16, 448, 944, 256, 0, 0);
        palPanel.raycastTarget = false;
        tabsArea = SetRectA(NewObj(at, "Tabs").GetComponent<RectTransform>(), 0, 0, 32, AnchorY(0, 456, 60), 690, 60);
        BuildSearchField(at, 734, 462, 210, 30);
        paletteArea = SetRectA(NewObj(at, "Palette").GetComponent<RectTransform>(), 0, 0, 32, AnchorY(0, 524, 140), 912, 140);
        var hint = MkLabelA(at, "ⓘ 単語を選んで、自由に文章を作りましょう！", 13, new Color(0.5f, 0.46f, 0.4f), 32, 672, 700, 20, 0, 0);
        hint.raycastTarget = false;
        // パレットのページ送り（「すべて」103語を1画面に収めるためのもの。ヒント文と重ならない位置）
        MkButtonA(at, "◀", 14, 760, 668, 36, 24, () => ChangePalettePage(-1), 0, 0);
        palettePageLabel = MkLabelA(at, "1 / 1", 14, new Color(0.5f, 0.46f, 0.4f), 800, 668, 60, 24, 0, 0, TextAnchor.MiddleCenter);
        palettePageLabel.raycastTarget = false;
        MkButtonA(at, "▶", 14, 864, 668, 36, 24, () => ChangePalettePage(1), 0, 0);
        askGroup.SetActive(false);
    }

    private void BuildSearchField(Transform parent, float x, float y, float w, float h)
    {
        var go = NewObj(parent, "SearchField");
        SetRectA(go.GetComponent<RectTransform>(), 0, 0, x, AnchorY(0, y, h), w, h);
        var img = go.AddComponent<Image>();
        img.sprite = RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.95f);
        var field = go.AddComponent<InputField>();
        var textComp = MkLabel(go.transform, "", 15, new Color(0.25f, 0.22f, 0.18f), 10, 0, w - 16, h, TextAnchor.MiddleLeft);
        textComp.supportRichText = false;
        var ph = MkLabel(go.transform, "🔍 キーワードで検索", 14, new Color(0.55f, 0.52f, 0.48f), 10, 0, w - 16, h, TextAnchor.MiddleLeft);
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
        var panel = MkPanelStretch(root, new Color(0.03f, 0.03f, 0.08f, 0.96f));
        backlogPanel = panel.gameObject;
        var pt = panel.transform;
        MkLabel(pt, "バックログ", 22, new Color(1f, 0.95f, 0.8f), 20, 14, 400, 30);
        var scrollGo = NewObj(pt, "Scroll");
        SetRectStretch(scrollGo.GetComponent<RectTransform>(), 16, 52, 16, 72);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = NewObj(scrollGo.transform, "Viewport");
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        var vimg = viewport.AddComponent<Image>();
        vimg.color = new Color(1, 1, 1, 0.02f);
        viewport.AddComponent<RectMask2D>();
        var content = NewObj(viewport.transform, "Content");
        backlogContent = content.GetComponent<RectTransform>();
        backlogContent.anchorMin = new Vector2(0, 1);
        backlogContent.anchorMax = new Vector2(1, 1);
        backlogContent.pivot = new Vector2(0.5f, 1);
        backlogContent.anchoredPosition = Vector2.zero;
        backlogContent.sizeDelta = new Vector2(0, 0);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vrt;
        scroll.content = backlogContent;
        MkButtonA(pt, "閉じる", 15, 16, 664, 120, 40, ToggleBacklog, 0, 0);
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
        var panel = MkPanelStretch(root, new Color(0.05f, 0.08f, 0.16f, 1f));
        titleScreen = panel.gameObject;
        var pt = panel.transform;
        var bgGo = NewObj(pt, "TitleBg");
        var bg = bgGo.AddComponent<RawImage>();
        bg.raycastTarget = false;
        bg.color = new Color(0.55f, 0.55f, 0.6f, 1f);
        SetCover(bg, LoadTex("res://assets/world_map_top.png"));
        var dim = MkRectStretch(pt, new Color(0.03f, 0.04f, 0.1f, 0.45f));
        dim.raycastTarget = false;
        MkLabelWide(pt, "ことばを紡ぐ恋愛シミュレーション", 52, new Color(1f, 0.97f, 0.9f), 218, 72);
        MkLabelWide(pt, "〜 プロトタイプ2 〜　5つの地域で、ことばを組み合わせて物語を紡ごう", 20, new Color(0.9f, 0.93f, 1f), 300, 30);
        var startBtn = MkButton(pt, "▶ ゲームスタート", 26, 480, 356, 320, 64, ShowMap);
        var srt = startBtn.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 1); srt.anchorMax = new Vector2(0.5f, 1);
        srt.pivot = new Vector2(0.5f, 1); srt.anchoredPosition = new Vector2(0, -356);
        var quitBtn = MkButton(pt, "おわる", 16, 560, 436, 160, 40, () => Application.Quit());
        var qrt = quitBtn.GetComponent<RectTransform>();
        qrt.anchorMin = new Vector2(0.5f, 1); qrt.anchorMax = new Vector2(0.5f, 1);
        qrt.pivot = new Vector2(0.5f, 1); qrt.anchoredPosition = new Vector2(0, -436);
        MkLabelA(pt, "Prototype 2 v21 (Unity版)", 13, new Color(1, 1, 1, 0.6f), 1080, 688, 184, 20, 1, 0, TextAnchor.UpperRight);
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
        var panel = MkPanelStretch(root, new Color(0.4f, 0.55f, 0.75f, 1f));
        mapScreen = panel.gameObject;
        var pt = panel.transform;
        var bgGo = NewObj(pt, "MapBg");
        var bg = bgGo.AddComponent<RawImage>();
        bg.raycastTarget = false;
        SetCover(bg, LoadTex("res://assets/world_map_top.png"));
        var header = MkPanelTopBand(pt, new Color(0.05f, 0.06f, 0.12f, 0.82f), 56);
        header.raycastTarget = false;
        MkLabel(header.transform, "🗺 行き先を選んでください", 22, Color.white, 20, 14, 800, 28);
        MkButtonA(header.transform, "好感度リセット", 13, 996, 10, 132, 36, ResetAffection, 1, 1);
        MkButtonA(header.transform, "タイトルへ", 15, 1140, 10, 120, 36, ShowTitle, 1, 1);
        foreach (var s in stages)
        {
            string sid = J.GetStr(s, "id");
            var ch = CharForStage(sid);
            string btnText = J.GetStr(s, "icon") + " " + J.GetStr(s, "name") + "\n" + J.GetStr(ch, "name");
            Vector2 ctr = RegionCenter(sid);
            var b = MkButton(pt, btnText, 17, ctr.x - 104, ctr.y - 31, 208, 62, () => EnterStage(sid));
            // 地域ボタンは画面中央基準に置く（中央基準で表示される背景マップと常に一致させる）
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(ctr.x - 640f, 360f - ctr.y);
            string desc = J.GetStr(s, "name") + "：" + J.GetStr(s, "description") + "\n登場キャラ: " + J.GetStr(ch, "name") + "（" + J.GetStr(ch, "personality") + "）";
            AddPointerEnter(b.gameObject, () => { mapInfoLabel.text = desc; });
        }
        var info = MkPanelBottomBand(pt, new Color(0.05f, 0.06f, 0.12f, 0.82f), 72);
        info.raycastTarget = false;
        mapInfoLabel = MkLabel(info.transform, "地域にカーソルを合わせると説明が出ます。クリックでその地域へ出発します。", 16, new Color(0.95f, 0.96f, 1f), 20, 10, 1240, 56);
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
        nameLabel.color = Color.Lerp(HexColor(J.GetStr(currentChar, "accent"), Color.white), new Color(0.12f, 0.16f, 0.10f), 0.55f);
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
        float scale = Mathf.Min(380f / tex.width, 712f / tex.height);
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
        affectionLabel.text = "♥ 好感度 " + aff + " / 100";
        if (affFillRt != null) affFillRt.sizeDelta = new Vector2(220f * Mathf.Clamp01(aff / 100f), 6);
        if (funLabel != null) funLabel.text = "😊 面白さ " + FunOf(cid) + " / 100";
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
        LogLine(speaker, text);
    }

    private void FinishTyping()
    {
        typing = false;
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
        if (typing)
        {
            typePos += TypeSpeed * Time.deltaTime;
            int total = fullText.Length;
            int n = Math.Min(total, (int)typePos);
            if (n > 0 && n < total && char.IsHighSurrogate(fullText[n - 1])) n++;
            dialogueLabel.text = fullText.Substring(0, n);
            if ((int)typePos >= total) typing = false;
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
        timerFillRt.sizeDelta = new Vector2(timerBarW * ratio, 10);
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
        resultLabel.text = "単語を選んで文章を作りましょう。時間切れは無言 (-10) です。";
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
            img.color = active
                ? new Color(0.30f, 0.55f, 0.33f, 1f)
                : new Color(0.16f, 0.17f, 0.26f, 1f);
            var lab = kv.Value.GetComponentInChildren<Text>();
            if (lab != null) lab.color = active ? Color.white : new Color(0.72f, 0.74f, 0.84f, 1f);
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
            img.color = selCount > 0 ? new Color(0.30f, 0.55f, 0.33f, 1f) : new Color(0.16f, 0.17f, 0.26f, 1f);
            // ホバー時は明るく、押下時は少し暗くして、ボタンらしく反応させる
            var bc = b.colors;
            bc.highlightedColor = new Color(1.18f, 1.18f, 1.22f, 1f);
            bc.pressedColor = new Color(0.72f, 0.72f, 0.8f, 1f);
            bc.selectedColor = new Color(1.18f, 1.18f, 1.22f, 1f);
            b.colors = bc;
            var label = b.GetComponentInChildren<Text>();
            label.color = new Color(0.97f, 0.97f, 1f, 1f);
            if (selCount >= 2) label.text = text + " ×" + selCount;
            FitText(label, 14, wordFont);
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
            if (sl != null) sl.color = fav ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.62f, 0.7f);
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
                img.color = c > 0 ? new Color(0.30f, 0.55f, 0.33f, 1f) : new Color(0.16f, 0.17f, 0.26f, 1f);
            var lab = kv.Value.GetComponentInChildren<Text>();
            if (lab != null) lab.text = c >= 2 ? kv.Key + " ×" + c : kv.Key;
        }
    }

    private void ToggleFavorite(string text)
    {
        if (!favoriteWords.Add(text)) favoriteWords.Remove(text);
        SaveAffection();
        RenderPaletteGrid();
    }

    private void AddWord(Dictionary<string, object> token)
    {
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
            return;
        }
        var parts = new List<string>();
        foreach (var w in selected) parts.Add(J.GetStr(w, "text"));
        sentenceLabel.text = "「" + string.Join("", parts) + "」";
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
            float cw = Mathf.Max(label.preferredWidth + pad, text.Length * (font - 2) + pad);
            var rt = chip.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(cw, chipH);
            label.rectTransform.sizeDelta = new Vector2(cw, chipH);
            if (i == selectedChip)
            {
                var img = chip.GetComponent<Image>();
                if (img != null) img.color = new Color(0.30f, 0.55f, 0.33f, 1f);
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
            if (dl != null) dl.color = new Color(0.85f, 0.87f, 0.95f);
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
        var rparts = new List<string>();
        foreach (var r in reasons) rparts.Add("・" + r);
        string head = "好感度 " + (delta >= 0 ? "+" + delta : delta.ToString()) + " → " + AffectionOf(cid)
            + " ｜ 面白さ " + (funDelta >= 0 ? "+" + funDelta : funDelta.ToString()) + " → " + FunOf(cid);
        if (isTimeout) head = "⏱ 時間切れで無言になってしまった… " + head;
        if (J.GetBool(res, "scene_change"))
        {
            pendingTurn = true;
            head += " / 転換の言葉で次の話題が変わりそうだ";
        }
        personalityLabel.text = head;
        if (evalAffLabel != null)
        {
            evalAffLabel.text = "♥ 好感度 " + (delta >= 0 ? "+" + delta : delta.ToString()) + " → " + AffectionOf(cid);
            evalFunLabel.text = "😊 面白さ " + (funDelta >= 0 ? "+" + funDelta : funDelta.ToString()) + " → " + FunOf(cid);
            evalTrustLabel.text = "🤝 信頼度 " + (trustDelta >= 0 ? "+" + trustDelta : trustDelta.ToString()) + " → " + TrustOf(cid);
            resultLabel.text = string.Join(" ", rparts);
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
        if (exprs.Count == 0) return;
        string path = "";
        if (expr == "default") path = J.GetStr(currentChar, "portrait");
        else path = J.GetStr(exprs, expr);
        if (path != "")
        {
            var tex = LoadTex(path);
            if (tex != null) SetPortraitTex(tex);
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
