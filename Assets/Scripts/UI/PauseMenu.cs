using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>
/// Внутриигровое меню на Tab в духе Persona 5 Royal / Persona 3 Reload.
///
/// Весь интерфейс строится из кода при старте — достаточно повесить компонент на пустой объект.
/// Пункты — только надписи (без кнопок-табличек). Выбранный пункт становится светлым, за ним
/// появляется чёрное «эхо», и обе надписи слегка дрожат.
///
/// Управление: Tab — открыть/закрыть, W/S или стрелки — выбор, Enter/Space — подтвердить,
/// Esc — закрыть. Мышь: наведение выбирает, клик подтверждает. Геймпад: Start, крестовина, A/B.
/// Функционал пунктов вешается в инспекторе на события On Submit (или через событие ItemSubmitted).
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string label = "Item";
        public UnityEvent onSubmit = new UnityEvent();
    }

    [Header("Пункты меню")]
    [SerializeField] private Entry[] entries =
    {
        new Entry { label = "Item" },
        new Entry { label = "Brooms" },
        new Entry { label = "Settings" },
    };

    [Header("Шрифт")]
    [Tooltip("Перетащи сюда .ttf/.otf Copperplate Gothic — тогда шрифт попадёт и в билд. " +
             "Если пусто, берётся установленный в системе Copperplate Gothic / Copperplate.")]
    [SerializeField] private Font font;
    [SerializeField] private string[] systemFontNames = { "Copperplate Gothic Bold", "Copperplate Gothic", "Copperplate" };
    [SerializeField] private int itemFontSize = 112;

    [Header("Цвета")]
    [SerializeField] private Color backgroundColor = new Color32(55, 126, 33, 255);
    [SerializeField] private Color stripeColor = new Color32(40, 100, 24, 90);
    [SerializeField] private Color bandColor = new Color32(30, 82, 18, 255);
    [SerializeField] private Color bandEdgeColor = new Color32(0, 0, 0, 255);
    [SerializeField] private Color textColor = new Color32(0, 0, 0, 255);
    [SerializeField] private Color selectedTextColor = new Color32(246, 244, 228, 255);
    [SerializeField] private Color echoColor = new Color32(0, 0, 0, 255);

    [Header("Раскладка (пиксели при 1920×1080, от центра экрана)")]
    [SerializeField] private Vector2 firstItemPosition = new Vector2(-180f, -50f);
    [SerializeField] private Vector2 itemStep = new Vector2(-84f, -105f);
    [SerializeField] private float[] itemAngles = { -5f, -2.5f, -6.5f };
    [SerializeField] private float selectedShift = 36f;
    [SerializeField] private float selectedScale = 1.14f;
    [SerializeField] private Vector2 echoOffset = new Vector2(11f, -9f);

    [Header("Дрожание выбранного пункта")]
    [SerializeField] private float jitterAmplitude = 3.5f;
    [SerializeField] private float jitterRotation = 1.4f;
    [SerializeField] private float jitterInterval = 0.055f;

    [Header("Поведение")]
    [SerializeField] private bool pauseGame = true;
    [SerializeField] private float repeatDelay = 0.35f;
    [SerializeField] private float repeatRate = 0.09f;

    [Header("VR (позже)")]
    [Tooltip("Рисовать меню в мире перед камерой, а не поверх экрана — нужно для шлема.")]
    [SerializeField] private bool worldSpace = false;
    [SerializeField] private float worldDistance = 2f;
    [SerializeField] private float worldScale = 0.0013f;

    /// <summary>Вызывается при подтверждении пункта: индекс и подпись.</summary>
    public event Action<int, string> ItemSubmitted;
    /// <summary>Меню открылось (true) или закрылось (false).</summary>
    public event Action<bool> OpenChanged;

    public bool IsOpen => _state == State.Opening || _state == State.Open;

    // ---------------------------------------------------------------------------------------

    enum State { Closed, Opening, Open, Closing }

    class ItemView
    {
        public RectTransform root;
        public RectTransform labelRect;
        public RectTransform echoRect;
        public Text label;
        public Text echo;
        public Vector2 basePos;
        public float baseAngle;
        public float selectedness;
        public float punch;
        public Vector2 jitterLabel, jitterEcho;
        public float jitterLabelRot, jitterEchoRot;
    }

    const float OpenDuration = 0.62f;
    const float CloseDuration = 0.34f;

    Canvas _canvas;
    RectTransform _canvasRect;
    SlantedQuad _background;
    SlantedQuad _band;
    SlantedQuad _bandEdge;
    RawImage _stripes;
    Text _hint;
    ItemView[] _items;

    State _state = State.Closed;
    float _stateTime;
    int _selected;
    float _jitterTimer;
    float _previousTimeScale = 1f;
    bool _previousCursorVisible;
    CursorLockMode _previousCursorLock;

    int _heldDirection;
    float _repeatTimer;

    // ---------------------------------------------------------------------------------------

    void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        _canvas.enabled = false;
    }

    void OnDisable()
    {
        if (IsOpen && pauseGame) Time.timeScale = _previousTimeScale;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (TogglePressed())
        {
            if (IsOpen) Close();
            else if (_state == State.Closed) Open();
        }

        if (_state == State.Open || _state == State.Opening)
        {
            if (CancelPressed() && !SimulatorActive()) Close();
            else
            {
                HandleNavigation(dt);
                if (SubmitPressed()) Submit(_selected);
            }
        }

        if (_state != State.Closed) Animate(dt);
    }

    // ---------------------------------------------------------------------------------------
    // Открытие / закрытие

    public void Open()
    {
        if (_state == State.Open || _state == State.Opening) return;
        _state = State.Opening;
        _stateTime = 0f;
        _canvas.enabled = true;

        if (worldSpace) PlaceInFrontOfCamera();

        if (pauseGame)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        _previousCursorVisible = Cursor.visible;
        _previousCursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        foreach (var item in _items) { item.selectedness = 0f; item.punch = 0f; }
        _items[_selected].punch = 0.2f;

        OpenChanged?.Invoke(true);
    }

    public void Close()
    {
        if (_state == State.Closed || _state == State.Closing) return;
        _state = State.Closing;
        _stateTime = 0f;

        if (pauseGame) Time.timeScale = _previousTimeScale;
        Cursor.visible = _previousCursorVisible;
        Cursor.lockState = _previousCursorLock;

        OpenChanged?.Invoke(false);
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    // ---------------------------------------------------------------------------------------
    // Выбор

    void HandleNavigation(float dt)
    {
        int dir = (UpHeld() ? -1 : 0) + (DownHeld() ? 1 : 0);
        if (dir == 0)
        {
            _heldDirection = 0;
            return;
        }

        if (dir != _heldDirection)
        {
            _heldDirection = dir;
            _repeatTimer = repeatDelay;
            Move(dir);
            return;
        }

        _repeatTimer -= dt;
        if (_repeatTimer <= 0f)
        {
            _repeatTimer = repeatRate;
            Move(dir);
        }
    }

    void Move(int dir)
    {
        int count = _items.Length;
        Select((_selected + dir + count) % count);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Length || index == _selected) return;
        _selected = index;
        _items[index].punch = 0.18f;
        _jitterTimer = 0f;
    }

    public void Submit(int index)
    {
        if (!IsOpen || index < 0 || index >= _items.Length) return;
        Select(index);
        _items[index].punch = 0.35f;
        Debug.Log("[Menu] " + entries[index].label);
        entries[index].onSubmit?.Invoke();
        ItemSubmitted?.Invoke(index, entries[index].label);
    }

    // Вызываются компонентом PauseMenuItem при работе мышью
    internal void OnPointerEnterItem(int index)
    {
        if (_state == State.Open || _state == State.Opening) Select(index);
    }

    internal void OnPointerClickItem(int index)
    {
        if (_state == State.Open || _state == State.Opening) Submit(index);
    }

    // ---------------------------------------------------------------------------------------
    // Анимация

    void Animate(float dt)
    {
        _stateTime += dt;
        bool closing = _state == State.Closing;

        // Фон: шторка справа налево при открытии, уезжает вправо при закрытии
        float bg = closing
            ? 1f - EaseInCubic(Mathf.Clamp01((_stateTime - 0.06f) / 0.24f))
            : EaseOutCubic(Mathf.Clamp01(_stateTime / 0.26f));
        _background.SetReveal(1f - bg, 1f);

        float band = closing
            ? 1f - EaseInCubic(Mathf.Clamp01(_stateTime / 0.2f))
            : EaseOutCubic(Mathf.Clamp01((_stateTime - 0.1f) / 0.3f));
        _band.SetReveal(0f, band);
        _bandEdge.SetReveal(0f, band);

        // Медленно ползущие диагональные полосы на фоне
        var uv = _stripes.uvRect;
        uv.x = Mathf.Repeat(uv.x + dt * 0.05f, 1f);
        _stripes.uvRect = uv;
        _stripes.color = new Color(stripeColor.r, stripeColor.g, stripeColor.b, stripeColor.a * bg);

        _hint.color = new Color(textColor.r, textColor.g, textColor.b, 0.75f * (closing ? bg : EaseOutCubic(Mathf.Clamp01((_stateTime - 0.35f) / 0.25f))));

        // Дрожание: каждые jitterInterval секунд новое случайное смещение
        _jitterTimer -= dt;
        bool newJitter = _jitterTimer <= 0f;
        if (newJitter) _jitterTimer = jitterInterval;

        for (int i = 0; i < _items.Length; i++)
        {
            var it = _items[i];
            bool isSelected = i == _selected;

            float target = isSelected ? 1f : 0f;
            it.selectedness = Mathf.MoveTowards(it.selectedness, target, dt * 9f);
            float s = EaseOutCubic(it.selectedness);
            it.punch = Mathf.MoveTowards(it.punch, 0f, dt * 1.6f);

            // Влёт / вылет пунктов по очереди
            float slide, alpha;
            if (closing)
            {
                float t = Mathf.Clamp01((_stateTime - i * 0.03f) / 0.16f);
                slide = Mathf.Lerp(0f, 1300f, EaseInCubic(t));
                alpha = 1f - t;
            }
            else
            {
                float t = Mathf.Clamp01((_stateTime - 0.12f - i * 0.065f) / 0.34f);
                slide = Mathf.LerpUnclamped(1100f, 0f, EaseOutBack(t));
                alpha = Mathf.Clamp01(t * 2.5f);
            }

            it.root.anchoredPosition = it.basePos + new Vector2(slide + selectedShift * s, 0f);
            it.root.localRotation = Quaternion.Euler(0f, 0f, it.baseAngle + slide * -0.004f);
            it.root.localScale = Vector3.one * (Mathf.Lerp(1f, selectedScale, s) + it.punch);

            if (isSelected && newJitter)
            {
                it.jitterLabel = UnityEngine.Random.insideUnitCircle * jitterAmplitude;
                it.jitterEcho = UnityEngine.Random.insideUnitCircle * jitterAmplitude * 1.8f;
                it.jitterLabelRot = UnityEngine.Random.Range(-jitterRotation, jitterRotation);
                it.jitterEchoRot = UnityEngine.Random.Range(-jitterRotation, jitterRotation) * 1.6f;
            }
            else if (!isSelected)
            {
                it.jitterLabel = Vector2.Lerp(it.jitterLabel, Vector2.zero, dt * 20f);
                it.jitterEcho = Vector2.Lerp(it.jitterEcho, Vector2.zero, dt * 20f);
                it.jitterLabelRot = Mathf.Lerp(it.jitterLabelRot, 0f, dt * 20f);
                it.jitterEchoRot = Mathf.Lerp(it.jitterEchoRot, 0f, dt * 20f);
            }

            it.labelRect.anchoredPosition = it.jitterLabel * s;
            it.labelRect.localRotation = Quaternion.Euler(0f, 0f, it.jitterLabelRot * s);
            it.echoRect.anchoredPosition = echoOffset * s + it.jitterEcho * s;
            it.echoRect.localRotation = Quaternion.Euler(0f, 0f, -1.5f * s + it.jitterEchoRot * s);

            Color c = Color.Lerp(textColor, selectedTextColor, s);
            c.a *= alpha;
            it.label.color = c;
            it.echo.color = new Color(echoColor.r, echoColor.g, echoColor.b, echoColor.a * s * alpha);
        }

        if (closing && _stateTime >= CloseDuration)
        {
            _state = State.Closed;
            _canvas.enabled = false;
        }
        else if (_state == State.Opening && _stateTime >= OpenDuration)
        {
            _state = State.Open;
        }
    }

    static float EaseOutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
    static float EaseInCubic(float t) => t * t * t;
    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // ---------------------------------------------------------------------------------------
    // Построение интерфейса

    void BuildUI()
    {
        var canvasGo = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvasRect = (RectTransform)canvasGo.transform;
        _canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        if (worldSpace)
        {
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvasRect.sizeDelta = new Vector2(1920f, 1080f);
            _canvasRect.localScale = Vector3.one * worldScale;
            scaler.dynamicPixelsPerUnit = 3f;
        }
        else
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        Font resolvedFont = ResolveFont(out FontStyle style);

        // Фон
        _background = CreateQuad("Background", _canvasRect, backgroundColor, 220f);
        Stretch(_background.rectTransform);

        _stripes = new GameObject("Stripes", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        _stripes.transform.SetParent(_canvasRect, false);
        Stretch(_stripes.rectTransform);
        _stripes.texture = CreateStripeTexture();
        _stripes.uvRect = new Rect(0f, 0f, 1920f / 96f, 1080f / 96f);
        _stripes.raycastTarget = false;

        // Тёмная диагональная полоса за пунктами + чёрная кромка
        _band = CreateQuad("Band", _canvasRect, bandColor, 0f);
        var bandRect = _band.rectTransform;
        bandRect.anchorMin = bandRect.anchorMax = new Vector2(0.5f, 0.5f);
        bandRect.sizeDelta = new Vector2(3200f, 400f);
        bandRect.anchoredPosition = new Vector2(0f, -150f);
        bandRect.localRotation = Quaternion.Euler(0f, 0f, 9f);

        _bandEdge = CreateQuad("BandEdge", _canvasRect, bandEdgeColor, 0f);
        var edgeRect = _bandEdge.rectTransform;
        edgeRect.anchorMin = edgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        edgeRect.sizeDelta = new Vector2(3200f, 14f);
        edgeRect.anchoredPosition = new Vector2(0f, -150f + 207f);
        edgeRect.localRotation = Quaternion.Euler(0f, 0f, 9f);

        // Пункты
        _items = new ItemView[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            _items[i] = CreateItem(i, entries[i].label, resolvedFont, style);

        // Подсказка по управлению
        _hint = CreateText("Hint", _canvasRect, resolvedFont, style, 30, TextAnchor.LowerRight);
        var hr = _hint.rectTransform;
        hr.anchorMin = hr.anchorMax = new Vector2(1f, 0f);
        hr.pivot = new Vector2(1f, 0f);
        hr.sizeDelta = new Vector2(1200f, 60f);
        hr.anchoredPosition = new Vector2(-60f, 40f);
        hr.localRotation = Quaternion.Euler(0f, 0f, 2f);
        _hint.text = "W/S  Select     Enter  Confirm     Tab  Close";
        _hint.raycastTarget = false;
    }

    ItemView CreateItem(int index, string text, Font f, FontStyle style)
    {
        var view = new ItemView();

        var rootGo = new GameObject("Item_" + text, typeof(RectTransform));
        view.root = (RectTransform)rootGo.transform;
        view.root.SetParent(_canvasRect, false);
        view.root.anchorMin = view.root.anchorMax = new Vector2(0.5f, 0.5f);
        view.root.pivot = new Vector2(0f, 0.5f);
        view.root.sizeDelta = new Vector2(900f, itemFontSize * 1.15f);
        view.basePos = firstItemPosition + itemStep * index;
        view.baseAngle = itemAngles != null && itemAngles.Length > 0 ? itemAngles[index % itemAngles.Length] : 0f;

        view.echo = CreateText("Echo", view.root, f, style, itemFontSize, TextAnchor.MiddleLeft);
        view.echo.text = text;
        view.echo.raycastTarget = false;
        view.echoRect = view.echo.rectTransform;
        Stretch(view.echoRect);

        view.label = CreateText("Label", view.root, f, style, itemFontSize, TextAnchor.MiddleLeft);
        view.label.text = text;
        view.label.raycastTarget = true;           // по надписи можно кликать
        view.labelRect = view.label.rectTransform;
        Stretch(view.labelRect);

        var pointer = rootGo.AddComponent<PauseMenuItem>();
        pointer.Init(this, index);

        view.root.anchoredPosition = view.basePos;
        view.root.localRotation = Quaternion.Euler(0f, 0f, view.baseAngle);
        return view;
    }

    static Text CreateText(string name, Transform parent, Font f, FontStyle style, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = f;
        t.fontStyle = style;
        t.fontSize = size;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        return t;
    }

    static SlantedQuad CreateQuad(string name, Transform parent, Color c, float skew)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SlantedQuad));
        go.transform.SetParent(parent, false);
        var q = go.GetComponent<SlantedQuad>();
        q.color = c;
        q.Skew = skew;
        q.raycastTarget = false;
        q.SetReveal(0f, 0f);
        return q;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    static Texture2D CreateStripeTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "MenuStripes"
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // диагональная полоса шириной ~40% периода с мягким краем
                float d = Mathf.Repeat((x + y) / (float)size, 1f);
                float a = Mathf.Clamp01((0.2f - Mathf.Abs(d - 0.5f)) * 20f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    Font ResolveFont(out FontStyle style)
    {
        if (font != null)
        {
            style = FontStyle.Normal;
            return font;
        }

        string[] installed = Font.GetOSInstalledFontNames();
        string found = null;
        foreach (var wanted in systemFontNames)
        {
            foreach (var name in installed)
            {
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) { found = name; break; }
            }
            if (found != null) break;
        }
        if (found == null)
        {
            foreach (var name in installed)
            {
                if (name.IndexOf("Copperplate", StringComparison.OrdinalIgnoreCase) >= 0) { found = name; break; }
            }
        }

        if (found != null)
        {
            style = found.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 ? FontStyle.Normal : FontStyle.Bold;
            return Font.CreateDynamicFontFromOSFont(found, itemFontSize);
        }

        Debug.LogWarning("[Menu] Шрифт Copperplate не найден в системе — перетащи .ttf в поле Font у PauseMenu.");
        style = FontStyle.Bold;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void PlaceInFrontOfCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();
        _canvasRect.position = cam.transform.position + forward * worldDistance;
        _canvasRect.rotation = Quaternion.LookRotation(forward, Vector3.up);
        _canvas.worldCamera = cam;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    // ---------------------------------------------------------------------------------------
    // Ввод (новая Input System, с запасным вариантом для старой)

#if ENABLE_INPUT_SYSTEM
    static bool Pressed(ButtonControl b) => b != null && b.wasPressedThisFrame;
    static bool Held(ButtonControl b) => b != null && b.isPressed;

    bool TogglePressed()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        // В XR Interaction Simulator Tab переключает устройства, поэтому при симуляторе меню открывается на Esc
        var key = SimulatorActive() ? (kb != null ? kb.escapeKey : null) : (kb != null ? kb.tabKey : null);
        return Pressed(key) || (gp != null && Pressed(gp.startButton));
    }

    XRInteractionSimulator _simulator;
    float _nextSimulatorSearch;

    bool SimulatorActive()
    {
        if (_simulator == null && Time.unscaledTime >= _nextSimulatorSearch)
        {
            _simulator = FindAnyObjectByType<XRInteractionSimulator>();
            _nextSimulatorSearch = Time.unscaledTime + 1f;
        }
        return _simulator != null && _simulator.isActiveAndEnabled;
    }

    bool CancelPressed()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        return (kb != null && Pressed(kb.escapeKey)) || (gp != null && Pressed(gp.buttonEast));
    }

    bool SubmitPressed()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        return (kb != null && (Pressed(kb.enterKey) || Pressed(kb.numpadEnterKey) || Pressed(kb.spaceKey)))
            || (gp != null && Pressed(gp.buttonSouth));
    }

    bool UpHeld()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        return (kb != null && (Held(kb.wKey) || Held(kb.upArrowKey)))
            || (gp != null && (Held(gp.dpad.up) || gp.leftStick.ReadValue().y > 0.6f));
    }

    bool DownHeld()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        return (kb != null && (Held(kb.sKey) || Held(kb.downArrowKey)))
            || (gp != null && (Held(gp.dpad.down) || gp.leftStick.ReadValue().y < -0.6f));
    }
#else
    bool TogglePressed() => Input.GetKeyDown(KeyCode.Tab);
    bool CancelPressed() => Input.GetKeyDown(KeyCode.Escape);
    bool SubmitPressed() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
    bool UpHeld() => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
    bool DownHeld() => Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
#endif
}
