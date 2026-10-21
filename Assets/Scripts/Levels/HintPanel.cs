using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Подсказка перед глазами в стиле меню-шляпы: чёрная скошенная плашка, зелёная полоса, «дрожащий» текст.
/// Висит чуть ниже линии взгляда и лениво догоняет поворот головы, чтобы не мешать смотреть по сторонам.
/// Создаётся из кода (LevelManager), руками на сцену ставить не нужно.
/// </summary>
public class HintPanel : MonoBehaviour
{
    const float CanvasWidth = 1100f, CanvasHeight = 300f, WorldWidth = 0.75f;
    static readonly Vector3 Offset = new Vector3(0f, -0.3f, 1.4f);     // относительно головы: ниже и впереди

    static readonly Color PlateColor = new Color32(10, 10, 10, 235);
    static readonly Color AccentColor = new Color32(55, 126, 33, 255);
    static readonly Color LabelColor = new Color32(246, 244, 228, 255);

    static readonly string[] TitleFonts = { "Copperplate Gothic Bold", "Copperplate", "Arial" };
    static readonly string[] BodyFonts = { "Segoe UI", "Helvetica Neue", "Arial" };

    Transform _head;
    Canvas _canvas;
    CanvasGroup _group;
    Text _title;
    Text _body;
    Text _echo;
    string _shownTitle;
    string _shownBody;
    bool _visible;
    float _alpha;
    float _punch;
    float _yaw;
    bool _yawReady;
    bool _following;
    float _jitterTimer;
    Vector2 _bodyBase;
    Vector2 _echoBase;

    public static HintPanel Create(Transform parent, Transform head)
    {
        var go = new GameObject("Hint Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var panel = go.AddComponent<HintPanel>();
        panel._head = head;
        panel.Build();
        return panel;
    }

    public void Show(string title, string body, bool visible)
    {
        _visible = visible;
        if (title != _shownTitle)
        {
            _shownTitle = title;
            _title.text = title;
        }
        if (body != _shownBody)
        {
            _shownBody = body;
            _body.text = body;
            _echo.text = body;
            _punch = 1f;
        }
    }

    void Build()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

        var plate = CreateQuad("Plate", rt, PlateColor);
        Stretch(plate.rectTransform);
        var stripe = CreateQuad("Stripe", rt, AccentColor);
        var sr = stripe.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0f);
        sr.sizeDelta = new Vector2(0f, 16f);
        sr.anchoredPosition = new Vector2(0f, 8f);

        Font titleFont = LoadFont(TitleFonts);
        Font bodyFont = LoadFont(BodyFonts);

        _title = CreateText("Title", rt, titleFont, 44, AccentColor, TextAnchor.MiddleLeft);
        var tr = _title.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(-200f, 80f);
        tr.anchoredPosition = new Vector2(10f, -18f);

        _echo = CreateText("Echo", rt, bodyFont, 58, AccentColor, TextAnchor.MiddleLeft);
        _body = CreateText("Body", rt, bodyFont, 58, LabelColor, TextAnchor.MiddleLeft);
        foreach (var t in new[] { _echo, _body })
        {
            var r = t.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(90f, 36f);
            r.offsetMax = new Vector2(-90f, -96f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
        _bodyBase = _body.rectTransform.anchoredPosition;
        _echoBase = _echo.rectTransform.anchoredPosition + new Vector2(6f, -5f);
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        _alpha = Mathf.MoveTowards(_alpha, _visible ? 1f : 0f, dt * 4f);
        _group.alpha = _alpha;
        _canvas.enabled = _alpha > 0.001f;
        if (_head == null || !_canvas.enabled) { _yawReady = false; return; }

        // Ленивое следование: панель стоит на месте, пока голова не отвернулась больше чем на 20°
        float headYaw = _head.eulerAngles.y;
        if (!_yawReady) { _yaw = headYaw; _yawReady = true; }
        float delta = Mathf.DeltaAngle(_yaw, headYaw);
        if (Mathf.Abs(delta) > 20f) _following = true;
        else if (Mathf.Abs(delta) < 3f) _following = false;
        if (_following) _yaw += delta * (1f - Mathf.Exp(-4f * dt));

        Vector3 position = _head.position + Quaternion.Euler(0f, _yaw, 0f) * Offset;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - _head.position, Vector3.up));

        _punch = Mathf.MoveTowards(_punch, 0f, dt * 3f);
        transform.localScale = Vector3.one * (WorldWidth / CanvasWidth * (1f + 0.06f * _punch));

        // Дрожание текста, как у карточек меню
        _jitterTimer -= dt;
        if (_jitterTimer <= 0f)
        {
            _jitterTimer = 0.07f;
            float amount = 0.6f + 2.5f * _punch;
            _body.rectTransform.anchoredPosition = _bodyBase + UnityEngine.Random.insideUnitCircle * amount;
            _echo.rectTransform.anchoredPosition = _echoBase + UnityEngine.Random.insideUnitCircle * amount * 2f;
        }
    }

    static Font LoadFont(string[] wanted)
    {
        var found = new List<string>();
        try
        {
            string[] installed = Font.GetOSInstalledFontNames();
            foreach (var name in wanted)
                foreach (var os in installed)
                    if (string.Equals(os, name, StringComparison.OrdinalIgnoreCase)) { found.Add(os); break; }
        }
        catch (Exception) { }

        // Шрифты после первого — запасные для букв, которых нет в первом (например, кириллицы в Copperplate)
        return found.Count > 0
            ? Font.CreateDynamicFontFromOSFont(found.ToArray(), 64)
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static Text CreateText(string name, RectTransform parent, Font font, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontStyle = FontStyle.Bold;
        t.fontSize = size;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.raycastTarget = false;
        Stretch(t.rectTransform);
        return t;
    }

    static SlantedQuad CreateQuad(string name, RectTransform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SlantedQuad));
        go.transform.SetParent(parent, false);
        var q = go.GetComponent<SlantedQuad>();
        q.color = color;
        q.Skew = 60f;
        q.raycastTarget = false;
        return q;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }
}
