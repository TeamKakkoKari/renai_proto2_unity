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
    private RectTransform portraitBox;
    private RawImage portraitImg;
    private Text nameLabel;
    private Text dialogueLabel;
    private Text personalityLabel;
    private Button nextButton;
    private Text affectionLabel;
    private RectTransform msgPanel;
    private GameObject askGroup;
    private Text timerLabel;
    private Image timerBg;
    private RectTransform timerFillRt;
    private Image timerFill;
    private Text sentenceLabel;
    private RectTransform chipsGroup;
    private RectTransform scatterArea;
    private Text resultLabel;
    private Button speakButton;
    private GameObject backlogPanel;
    private RectTransform backlogContent;
    private GameObject titleScreen;
    private GameObject mapScreen;
    private Text mapInfoLabel;
    private Coroutine frameAnim;
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
    private static readonly Rect FrameStory = new Rect(16, 500, 1248, 204);
    private static readonly Rect FrameAsk = new Rect(16, 300, 1248, 404);

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

        jpFont = Resources.Load<Font>("fonts/MPLUSRounded1c-Regular");
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
        foreach (var c in characters) affection[J.GetStr(c, "id")] = 0;
        if (File.Exists(SavePath))
        {
            try
            {
                var parsed = J.Dict(JsonParser.Parse(File.ReadAllText(SavePath)));
                foreach (var kv in parsed) affection[kv.Key] = J.GetInt(parsed, kv.Key);
            }
            catch (Exception) { /*壊れたセーブは初期値で続行*/ }
        }
    }

    private void SaveAffection()
    {
        var parts = new List<string>();
        foreach (var kv in affection) parts.Add("\"" + kv.Key + "\":" + kv.Value);
        File.WriteAllText(SavePath, "{" + string.Join(",", parts) + "}");
    }

    private int AffectionOf(string cid)
    {
        int v;
        return affection.TryGetValue(cid, out v) ? v : 0;
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
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        SetRect(t.rectTransform, x, y, w, h);
        return t;
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
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (onClick != null) btn.onClick.AddListener(() => { Deselect(); onClick(); });
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
        bgSky = MkRect(root, Color.white, 0, 0, 1280, 720);
        bgSky.raycastTarget = false;
        bgGround = MkRect(root, Color.white, 0, 560, 1280, 160);
        bgGround.raycastTarget = false;

        portraitBox = SetRect(NewObj(root, "PortraitBox").GetComponent<RectTransform>(), 450, PortraitY, 380, 712);
        var pgo = NewObj(portraitBox, "Portrait");
        portraitImg = pgo.AddComponent<RawImage>();
        portraitImg.raycastTarget = false;
        var prt = portraitImg.rectTransform;
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;

        // 全面クリックで進む（ボタン類より下の層に置く）
        var catcher = MkRect(root, new Color(0, 0, 0, 0), 0, 0, 1280, 720);
        AddPointerDown(catcher.gameObject, OnClickAdvance);

        // 上部バー
        var top = MkPanel(root, new Color(0.06f, 0.06f, 0.12f, 0.85f), 0, 0, 1280, 52);
        MkLabel(top.transform, "ことばを紡ぐ恋愛シミュレーション", 18, Color.white, 16, 13, 430, 26);
        MkButton(top.transform, "🗺 マップ", 15, 454, 8, 120, 36, ShowMap);
        affectionLabel = MkLabel(top.transform, "", 18, new Color(1f, 0.85f, 0.9f), 586, 13, 560, 26);
        MkButton(top.transform, "ログ", 15, 1180, 8, 84, 36, ToggleBacklog);

        BuildTimerBar();
        BuildFrame();
        BuildTitleScreen();
        BuildMapScreen();
        BuildBacklog();
    }

    private void BuildTimerBar()
    {
        timerLabel = MkLabel(root, "", 15, new Color(1f, 0.95f, 0.85f), 1014, 456, 250, 22, TextAnchor.UpperRight);
        timerBg = MkRect(root, new Color(0.05f, 0.05f, 0.1f, 0.75f), 16, 484, 1248, 10);
        timerBg.raycastTarget = false;
        var fill = MkRect(root, new Color(0.45f, 0.9f, 0.5f), 16, 484, 1248, 10);
        fill.raycastTarget = false;
        timerFill = fill;
        timerFillRt = fill.rectTransform;
        SetTimerVisible(false);
    }

    private void SetTimerVisible(bool v)
    {
        timerLabel.gameObject.SetActive(v);
        timerBg.gameObject.SetActive(v);
        timerFill.gameObject.SetActive(v);
    }

    private void BuildFrame()
    {
        var panel = MkPanel(root, new Color(0.05f, 0.05f, 0.12f, 0.88f), FrameStory.x, FrameStory.y, FrameStory.width, FrameStory.height);
        msgPanel = panel.rectTransform;
        AddPointerDown(panel.gameObject, OnClickAdvance);
        var pt = panel.transform;

        nameLabel = MkLabel(pt, "", 21, new Color(0.65f, 1f, 0.75f), 16, 8, 1216, 28);
        personalityLabel = MkLabel(pt, "", 13, new Color(0.75f, 0.78f, 0.9f), 16, 38, 1216, 20);
        dialogueLabel = MkLabel(pt, "", 20, Color.white, 16, 62, 1216, 96);
        nextButton = MkButton(pt, "つぎへ ▼", 15, 16, FrameStory.height - 42, 170, 34, OnNext);

        // 選択欄（セリフ枠の中に広がる）
        askGroup = NewObj(pt, "AskGroup");
        SetRect(askGroup.GetComponent<RectTransform>(), 0, 0, 1248, 404);
        var at = askGroup.transform;
        sentenceLabel = MkLabel(at, "（まだ何も選んでいません）", 21, new Color(0.9f, 0.97f, 1f), 16, 100, 990, 36);
        speakButton = MkButton(at, "💬 発言する", 21, 1022, 98, 210, 42, () => DoSpeak(false));
        chipsGroup = SetRect(NewObj(at, "Chips").GetComponent<RectTransform>(), 16, 142, 1216, 38);
        MkButton(at, "◀ 左へ", 14, 16, 184, 84, 30, () => MoveChip(-1));
        MkButton(at, "右へ ▶", 14, 106, 184, 84, 30, () => MoveChip(1));
        MkButton(at, "削除", 14, 196, 184, 70, 30, DeleteChip);
        MkButton(at, "全解除", 14, 272, 184, 84, 30, ClearSelected);
        MkLabel(at, "↓ 散らばった単語を押して文章を作ろう (カテゴリ色つき)", 13, new Color(0.8f, 0.82f, 0.95f), 366, 188, 700, 24);
        scatterArea = SetRect(NewObj(at, "Scatter").GetComponent<RectTransform>(), 16, 220, 1216, 140);
        resultLabel = MkLabel(at, "", 13, new Color(0.85f, 0.9f, 1f), 16, 364, 1216, 36);
        askGroup.SetActive(false);
    }

    private void LayoutFrame(bool ask, bool animate)
    {
        Rect target = ask ? FrameAsk : FrameStory;
        SetTimerVisible(ask);
        float barY = target.y - 16f;
        float labelY = target.y - 44f;
        if (frameAnim != null) StopCoroutine(frameAnim);
        if (animate)
        {
            frameAnim = StartCoroutine(AnimFrame(target, barY, labelY));
        }
        else
        {
            ApplyFrame(target, barY, labelY);
        }
        // つぎへボタンは枠の下端に追随
        SetRect(nextButton.GetComponent<RectTransform>(), 16, target.height - 42, 170, 34);
        // セリフ表示位置はモードで変える
        if (ask) SetRect(dialogueLabel.rectTransform, 16, 38, 1216, 54);
        else SetRect(dialogueLabel.rectTransform, 16, 62, 1216, 96);
    }

    private void ApplyFrame(Rect target, float barY, float labelY)
    {
        SetRect(msgPanel, target.x, target.y, target.width, target.height);
        SetRect(timerBg.rectTransform, 16, barY, 1248, 10);
        float ratio = timeLimit > 0f ? Mathf.Clamp01(timeLeft / timeLimit) : 0f;
        SetRect(timerFillRt, 16, barY, 1248f * ratio, 10);
        SetRect(timerLabel.rectTransform, 1014, labelY, 250, 22);
    }

    private IEnumerator AnimFrame(Rect target, float barY, float labelY)
    {
        float t = 0f;
        Vector2 fromPos = msgPanel.anchoredPosition;
        Vector2 fromSize = msgPanel.sizeDelta;
        Vector2 toPos = new Vector2(target.x, -target.y);
        Vector2 toSize = new Vector2(target.width, target.height);
        float fromBarY = timerBg.rectTransform.anchoredPosition.y;
        float fromLabelY = timerLabel.rectTransform.anchoredPosition.y;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.28f;
            float k = Mathf.Clamp01(t);
            msgPanel.anchoredPosition = Vector2.Lerp(fromPos, toPos, k);
            msgPanel.sizeDelta = Vector2.Lerp(fromSize, toSize, k);
            float by = Mathf.Lerp(fromBarY, -barY, k);
            timerBg.rectTransform.anchoredPosition = new Vector2(16, by);
            timerFillRt.anchoredPosition = new Vector2(16, by);
            timerLabel.rectTransform.anchoredPosition = new Vector2(1014, Mathf.Lerp(fromLabelY, -labelY, k));
            yield return null;
        }
        ApplyFrame(target, barY, labelY);
        frameAnim = null;
    }

    private void BuildBacklog()
    {
        var panel = MkPanel(root, new Color(0.03f, 0.03f, 0.08f, 0.96f), 0, 0, 1280, 720);
        backlogPanel = panel.gameObject;
        var pt = panel.transform;
        MkLabel(pt, "バックログ", 22, new Color(1f, 0.95f, 0.8f), 20, 14, 400, 30);
        var scrollGo = NewObj(pt, "Scroll");
        SetRect(scrollGo.GetComponent<RectTransform>(), 16, 52, 1248, 596);
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
        MkButton(pt, "閉じる", 15, 16, 664, 120, 40, ToggleBacklog);
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
        var l = MkLabel(backlogContent, shown, 15, new Color(0.9f, 0.92f, 1f), 0, 0, 1220, 24);
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
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(640, -360);
        rt.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
    }

    private void BuildTitleScreen()
    {
        var panel = MkPanel(root, new Color(0.05f, 0.08f, 0.16f, 1f), 0, 0, 1280, 720);
        titleScreen = panel.gameObject;
        var pt = panel.transform;
        var bgGo = NewObj(pt, "TitleBg");
        var bg = bgGo.AddComponent<RawImage>();
        bg.raycastTarget = false;
        bg.color = new Color(0.55f, 0.55f, 0.6f, 1f);
        SetCover(bg, LoadTex("res://assets/world_map_top.png"));
        var dim = MkRect(pt, new Color(0.03f, 0.04f, 0.1f, 0.45f), 0, 0, 1280, 720);
        dim.raycastTarget = false;
        MkLabel(pt, "ことばを紡ぐ恋愛シミュレーション", 52, new Color(1f, 0.97f, 0.9f), 0, 218, 1280, 72, TextAnchor.MiddleCenter);
        MkLabel(pt, "〜 プロトタイプ2 〜　5つの地域で、ことばを組み合わせて物語を紡ごう", 20, new Color(0.9f, 0.93f, 1f), 0, 300, 1280, 30, TextAnchor.MiddleCenter);
        MkButton(pt, "▶ ゲームスタート", 26, 480, 356, 320, 64, ShowMap);
        MkButton(pt, "おわる", 16, 560, 436, 160, 40, () => Application.Quit());
        MkLabel(pt, "Prototype 2 v10 (Unity版)", 13, new Color(1, 1, 1, 0.6f), 1080, 688, 184, 20, TextAnchor.UpperRight);
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
        var panel = MkPanel(root, new Color(0.4f, 0.55f, 0.75f, 1f), 0, 0, 1280, 720);
        mapScreen = panel.gameObject;
        var pt = panel.transform;
        var bgGo = NewObj(pt, "MapBg");
        var bg = bgGo.AddComponent<RawImage>();
        bg.raycastTarget = false;
        SetCover(bg, LoadTex("res://assets/world_map_top.png"));
        var header = MkPanel(pt, new Color(0.05f, 0.06f, 0.12f, 0.82f), 0, 0, 1280, 56);
        header.raycastTarget = false;
        MkLabel(header.transform, "🗺 行き先を選んでください", 22, Color.white, 20, 14, 800, 28);
        MkButton(header.transform, "タイトルへ", 15, 1140, 10, 120, 36, ShowTitle);
        foreach (var s in stages)
        {
            string sid = J.GetStr(s, "id");
            var ch = CharForStage(sid);
            string btnText = J.GetStr(s, "icon") + " " + J.GetStr(s, "name") + "\n" + J.GetStr(ch, "name");
            Vector2 ctr = RegionCenter(sid);
            var b = MkButton(pt, btnText, 17, ctr.x - 104, ctr.y - 31, 208, 62, () => EnterStage(sid));
            string desc = J.GetStr(s, "name") + "：" + J.GetStr(s, "description") + "\n登場キャラ: " + J.GetStr(ch, "name") + "（" + J.GetStr(ch, "personality") + "）";
            AddPointerEnter(b.gameObject, () => { mapInfoLabel.text = desc; });
        }
        var info = MkPanel(pt, new Color(0.05f, 0.06f, 0.12f, 0.82f), 0, 648, 1280, 72);
        info.raycastTarget = false;
        mapInfoLabel = MkLabel(info.transform, "地域にカーソルを合わせると説明が出ます。クリックでその地域へ出発します。", 16, new Color(0.95f, 0.96f, 1f), 20, 10, 1240, 56);
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
        selectedChip = -1;
        bgSky.color = HexColor(J.GetStr(currentStage, "bg"), Color.white);
        bgGround.color = HexColor(J.GetStr(currentStage, "ground"), Color.white);
        string p = J.GetStr(currentChar, "portrait");
        if (p != "") SetPortraitTex(LoadTex(p));
        nameLabel.color = Color.Lerp(HexColor(J.GetStr(currentChar, "accent"), Color.white), Color.white, 0.45f);
        RefreshCharPanel();
        StartStory();
    }

    private void SetPortraitTex(Texture2D tex)
    {
        portraitImg.texture = tex;
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
        affectionLabel.text = "  " + J.GetStr(currentChar, "name") + " 好感度 " + aff + " " + hearts + " (" + J.GetStr(lv, "name") + ")";
    }

    private void StartStory()
    {
        string cid = J.GetStr(currentChar, "id");
        var story = J.GetDict(stories, cid);
        beats = J.GetList(story, "beats");
        storyPos = 0;
        ShowBeat();
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
        timerFillRt.sizeDelta = new Vector2(1248f * ratio, 10);
        timerFill.color = Color.HSVToRGB(0.33f * ratio, 0.85f, 0.95f);
        timerLabel.color = sec <= 10 ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.95f, 0.85f);
    }

    private void OnTimeout()
    {
        selected.Clear();
        selectedChip = -1;
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
        resultLabel.text = "制限時間内に、散らばった単語から文章を作って発言してください。時間切れは無言 (-10) です。";
        timeLeft = timeLimit;
        UpdateTimerUi();
        ScatterWords(OfferedWords(activeQuestion));
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

    private void ScatterWords(Dictionary<string, Dictionary<string, object>> offered)
    {
        for (int i = scatterArea.childCount - 1; i >= 0; i--)
            Destroy(scatterArea.GetChild(i).gameObject);
        var items = new List<Dictionary<string, object>>(offered.Values);
        Shuffle(items);
        float areaW = 1216f, areaH = 140f;
        int n = items.Count;
        int cols = n <= 24 ? 8 : (n <= 40 ? 10 : 12);
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));
        float cellW = areaW / cols;
        float cellH = areaH / rows;
        var catColors = new Dictionary<string, string>();
        foreach (var co in J.GetList(dataWords, "categories"))
        {
            var c = J.Dict(co);
            catColors[J.GetStr(c, "id")] = J.GetStr(c, "color", "#ffffff");
        }
        var cells = new List<int>();
        for (int i = 0; i < n; i++) cells.Add(i);
        Shuffle(cells);
        for (int idx = 0; idx < n; idx++)
        {
            var w = items[idx];
            int cell = cells[idx];
            float cx = (cell % cols) * cellW;
            float cy = (cell / cols) * cellH;
            string text = J.GetStr(w, "text");
            string hex;
            if (!catColors.TryGetValue(J.GetStr(w, "category"), out hex)) hex = "#ffffff";
            Color col = Color.Lerp(HexColor(hex, Color.white), Color.white, 0.3f);
            var token = w;
            var b = MkButton(scatterArea, text, 17, 0, 0, 60, 34, () => AddWord(token));
            var label = b.GetComponentInChildren<Text>();
            label.color = col;
            float bw = Mathf.Max(label.preferredWidth + 18f, text.Length * 15f + 18f);
            float bh = 34f;
            var rt = b.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(bw, bh);
            label.rectTransform.sizeDelta = new Vector2(bw, bh);
            float jx = (float)rng.NextDouble() * Mathf.Max(0f, cellW - bw - 4f);
            float jy = (float)rng.NextDouble() * Mathf.Max(0f, cellH - bh - 4f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx + jx + bw / 2f, -(cy + jy + bh / 2f));
            rt.localEulerAngles = new Vector3(0, 0, (float)(rng.NextDouble() * 14.0 - 7.0));
        }
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
        for (int i = chipsGroup.childCount - 1; i >= 0; i--)
            Destroy(chipsGroup.GetChild(i).gameObject);
        if (selected.Count == 0)
        {
            sentenceLabel.text = "（まだ何も選んでいません）";
            return;
        }
        var parts = new List<string>();
        float x = 0f, y = 0f;
        for (int i = 0; i < selected.Count; i++)
        {
            string text = J.GetStr(selected[i], "text");
            parts.Add(text);
            int idx = i;
            string chipText = (i == selectedChip ? "▶" : "") + text;
            var chip = MkButton(chipsGroup, chipText, 15, x, y, 60, 30, () => { selectedChip = idx; RefreshSentence(); });
            var label = chip.GetComponentInChildren<Text>();
            float cw = Mathf.Max(label.preferredWidth + 14f, chipText.Length * 14f + 14f);
            var rt = chip.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(cw, 30);
            label.rectTransform.sizeDelta = new Vector2(cw, 30);
            x += cw + 4f;
            if (x > 1216f && i + 1 < selected.Count)
            {
                x = 0f;
                y += 34f;
            }
        }
        sentenceLabel.text = "「" + string.Join("", parts) + "」";
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
        selectedChip = Math.Min(selectedChip, selected.Count - 1);
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
        var reactions = J.GetList(J.GetDict(currentChar, "reactions"), rkey);
        string reaction = reactions.Count > 0 ? J.Str(reactions[turnCount % reactions.Count], "……") : "……";
        ShowStoryBox();
        SetSpeaker(true);
        ShowDialogue("「" + reaction + "」", J.GetStr(currentChar, "name"));
        var reasons = res["reasons"] as List<string> ?? new List<string>();
        var rparts = new List<string>();
        foreach (var r in reasons) rparts.Add("・" + r);
        string head = "好感度 " + (delta >= 0 ? "+" + delta : delta.ToString()) + " → " + AffectionOf(cid);
        if (isTimeout) head = "⏱ 時間切れで無言になってしまった… " + head;
        if (J.GetBool(res, "scene_change"))
        {
            pendingTurn = true;
            head += " / 転換の言葉で次の話題が変わりそうだ";
        }
        personalityLabel.text = head;
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
