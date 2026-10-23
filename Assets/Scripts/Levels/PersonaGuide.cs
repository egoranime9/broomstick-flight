using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Указатель цели в стиле Persona 5: рубленая чёрная стрелка с белой обводкой и зелёной «тенью»,
/// дёргается и подпрыгивает, под ней подпись «NEXT 2/6  45 M».
///
/// Цель в поле зрения — стрелка висит над ней и смотрит вниз (размер держится постоянным на любом расстоянии).
/// Цель вне поля зрения — стрелка прилипает к краю взгляда и показывает, куда повернуть.
/// Создаётся из кода (LevelManager), руками на сцену ставить не нужно.
/// </summary>
public class PersonaGuide : MonoBehaviour
{
    const float CanvasSize = 400f;
    const float OnScreenAngle = 22f;        // ближе этого угла к центру взгляда — цель «в кадре»
    const float MarkerAngularSize = 0.09f;  // размер стрелки над целью относительно расстояния до неё
    const float EdgeSize = 0.16f;           // размер стрелки у края взгляда, м

    static readonly Color Black = new Color32(8, 8, 8, 255);
    static readonly Color White = new Color32(246, 244, 228, 255);
    static readonly Color Green = new Color32(55, 126, 33, 255);
    static readonly string[] Fonts = { "Copperplate Gothic Bold", "Copperplate", "Arial" };

    Transform _head;
    CanvasGroup _group;
    Canvas _canvas;
    RectTransform _arrowRoot;
    RectTransform _shadow, _outline, _fill;
    Text _label, _labelEcho;
    RectTransform _labelRoot;

    Vector3? _target;
    float _markerLift;
    string _text = "";
    float _show;
    float _punch;
    float _jitterTimer;
    float _arrowAngle;
    float _wobble;
    bool _poseReady;
    bool _lastInView;
    float _switchTime = -10f;
    Vector3 _localPosition;
    Quaternion _localRotation = Quaternion.identity;
    float _size;

    public static PersonaGuide Create(Transform parent, Transform head)
    {
        var go = new GameObject("Persona Guide", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var guide = go.AddComponent<PersonaGuide>();
        guide._head = head;
        guide.Build();
        return guide;
    }

    /// <param name="target">Куда указывать; null — спрятать стрелку.</param>
    /// <param name="label">Подпись, например «NEXT 2/6».</param>
    /// <param name="markerLift">На сколько метров над целью висит стрелка, когда цель видно.</param>
    public void SetTarget(Vector3? target, string label, float markerLift)
    {
        bool changed = target.HasValue != _target.HasValue
                       || (target.HasValue && _target.HasValue && (target.Value - _target.Value).sqrMagnitude > 1f)
                       || label != _text;
        if (changed && target.HasValue) _punch = 1f;
        _target = target;
        _text = label ?? "";
        _markerLift = markerLift;
    }

    void Build()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(CanvasSize, CanvasSize);

        _arrowRoot = new GameObject("Arrow", typeof(RectTransform)).GetComponent<RectTransform>();
        _arrowRoot.SetParent(rt, false);
        _arrowRoot.sizeDelta = new Vector2(220f, 240f);
        _arrowRoot.anchoredPosition = new Vector2(0f, 40f);

        _shadow = CreateArrow("Shadow", Green, 14f);
        _outline = CreateArrow("Outline", White, 14f);
        _fill = CreateArrow("Fill", Black, 0f);

        _labelRoot = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        _labelRoot.SetParent(rt, false);
        _labelRoot.sizeDelta = new Vector2(700f, 90f);
        _labelRoot.anchoredPosition = new Vector2(0f, -140f);
        _labelRoot.localRotation = Quaternion.Euler(0f, 0f, 6f);       // лёгкий наклон, как у надписей в Persona

        Font font = LoadFont();
        _labelEcho = CreateText("Echo", font, Green);
        _label = CreateText("Text", font, White);
        var outline = _label.gameObject.AddComponent<Outline>();
        outline.effectColor = Black;
        outline.effectDistance = new Vector2(4f, -4f);
    }

    RectTransform CreateArrow(string name, Color color, float inflate)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(PersonaArrowGraphic));
        go.transform.SetParent(_arrowRoot, false);
        var g = go.GetComponent<PersonaArrowGraphic>();
        g.color = color;
        g.Inflate = inflate;
        g.raycastTarget = false;
        var r = g.rectTransform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
        return r;
    }

    Text CreateText(string name, Font font, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(_labelRoot, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontStyle = FontStyle.Bold;
        t.fontSize = 64;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.raycastTarget = false;
        var r = t.rectTransform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
        return t;
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        bool want = _target.HasValue && _head != null;
        _show = Mathf.MoveTowards(_show, want ? 1f : 0f, dt * 5f);
        _group.alpha = _show;
        _canvas.enabled = _show > 0.001f;
        if (!_canvas.enabled || _head == null)
        {
            _poseReady = false;
            return;
        }
        if (!_target.HasValue) return;     // гаснет на последней позе

        Vector3 target = _target.Value;
        Vector3 toTarget = target - _head.position;
        float distance = toTarget.magnitude;
        Vector3 local = _head.InverseTransformDirection(toTarget / Mathf.Max(distance, 0.001f));
        bool inView = local.z > 0f && Vector3.Angle(Vector3.forward, local) < OnScreenAngle;

        Vector3 position;
        Quaternion rotation;
        float size;
        float arrowAngle;
        if (inView)
        {
            // Над целью, остриём вниз
            position = target + Vector3.up * _markerLift;
            Vector3 view = position - _head.position;
            rotation = Quaternion.LookRotation(view, Vector3.up);
            size = Mathf.Max(0.12f, view.magnitude * MarkerAngularSize);
            arrowAngle = 180f;
        }
        else
        {
            // У края взгляда, остриём в сторону цели
            Vector2 dir = new Vector2(local.x, local.y);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.right;      // цель ровно за спиной
            dir.Normalize();
            position = _head.TransformPoint(new Vector3(dir.x * 0.32f, dir.y * 0.22f - 0.05f, 1.1f));
            rotation = _head.rotation;
            size = EdgeSize;
            arrowAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        }

        // Позу храним относительно головы: так стрелка не отстаёт, когда метла летит на полной скорости.
        // Плавно (как прыжок в меню Persona) она движется только при переходе «над целью» ↔ «у края».
        if (inView != _lastInView)
        {
            _lastInView = inView;
            _switchTime = Time.unscaledTime;
        }
        bool transitioning = Time.unscaledTime - _switchTime < 0.3f;
        float k = _poseReady && transitioning ? 1f - Mathf.Exp(-14f * dt) : 1f;
        _poseReady = true;

        Vector3 localPosition = _head.InverseTransformPoint(position);
        Quaternion localRotation = Quaternion.Inverse(_head.rotation) * rotation;
        _localPosition = Vector3.Lerp(_localPosition, localPosition, k);
        _localRotation = Quaternion.Slerp(_localRotation, localRotation, k);
        _size = Mathf.Lerp(_size, size, k);
        transform.SetPositionAndRotation(_head.TransformPoint(_localPosition), _head.rotation * _localRotation);
        transform.localScale = Vector3.one * (_size / CanvasSize);
        _punch = Mathf.MoveTowards(_punch, 0f, dt * 2.5f);

        _arrowAngle = Mathf.LerpAngle(_arrowAngle, arrowAngle, k);
        float bounce = inView ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) * 22f : 0f;   // подпрыгивает над целью
        _arrowRoot.localRotation = Quaternion.Euler(0f, 0f, _arrowAngle + _wobble);
        _arrowRoot.anchoredPosition = new Vector2(0f, 40f + bounce);
        _arrowRoot.localScale = Vector3.one * (1f + 0.35f * _punch);

        _label.text = _text + "   " + Mathf.RoundToInt(distance) + " M";
        _labelEcho.text = _label.text;
        _labelRoot.localScale = Vector3.one * (1f + 0.25f * _punch);

        // Дрожание слоёв
        _jitterTimer -= dt;
        if (_jitterTimer <= 0f)
        {
            _jitterTimer = 0.07f;
            float amount = 1f + 4f * _punch;
            _shadow.anchoredPosition = new Vector2(14f, -12f) + UnityEngine.Random.insideUnitCircle * amount * 2f;
            _outline.anchoredPosition = UnityEngine.Random.insideUnitCircle * amount;
            _fill.anchoredPosition = UnityEngine.Random.insideUnitCircle * amount * 0.6f;
            _wobble = UnityEngine.Random.Range(-2.5f, 2.5f) * (1f + 2f * _punch);
            _label.rectTransform.anchoredPosition = UnityEngine.Random.insideUnitCircle * amount;
            _labelEcho.rectTransform.anchoredPosition = new Vector2(8f, -7f) + UnityEngine.Random.insideUnitCircle * amount * 2f;
        }
    }

    static Font LoadFont()
    {
        var found = new List<string>();
        try
        {
            string[] installed = Font.GetOSInstalledFontNames();
            foreach (var name in Fonts)
                foreach (var os in installed)
                    if (string.Equals(os, name, StringComparison.OrdinalIgnoreCase)) { found.Add(os); break; }
        }
        catch (Exception) { }
        return found.Count > 0
            ? Font.CreateDynamicFontFromOSFont(found.ToArray(), 64)
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
