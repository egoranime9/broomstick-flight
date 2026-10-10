using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Меню-шляпа.
///   * Снять шляпу: поднеси левую руку к макушке и нажми грип, затем опусти руку перед лицом.
///     Чем дальше шляпа от головы, тем сильнее мир уходит в зелёный монохром; время останавливается сразу.
///   * Шляпа остаётся в левой руке, в правой появляется карточка пункта в стиле Persona.
///   * Следующий пункт: резко тряхнуть шляпой в сторону правой руки (или левый стик вправо).
///   * Выбрать пункт: курок правой руки. Пока выбор ничего не открывает — только событие On Submit.
///   * Вернуться в игру: поднести шляпу обратно к голове — мир плавно возвращается в цвет, время разгоняется.
/// Для проверки без шлема: P — снять/надеть шляпу, N — следующий пункт, Enter — выбрать.
/// </summary>
public class HatMenu : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string label = "Item";
        public UnityEvent onSubmit = new UnityEvent();
    }

    [Header("Пункты")]
    [SerializeField] private Entry[] entries =
    {
        new Entry { label = "Item" },
        new Entry { label = "Brooms" },
        new Entry { label = "Settings" },
    };

    [Header("Ссылки (пустые найдутся сами)")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private InputActionProperty leftGrip;        // XRI Left Interaction/Select
    [SerializeField] private InputActionProperty rightTrigger;    // XRI Right Interaction/Activate
    [SerializeField] private InputActionProperty leftThumbstick;  // XRI Left/Thumbstick

    [Header("Жест снятия шляпы")]
    [Tooltip("Где сидит шляпа относительно камеры (макушка)")]
    [SerializeField] private Vector3 hatOnHead = new Vector3(0f, 0.11f, -0.02f);
    [Tooltip("Насколько близко к макушке нужно поднести руку, чтобы схватить или надеть шляпу")]
    [SerializeField] private float grabRadius = 0.2f;
    [Tooltip("На сколько нужно отвести шляпу от головы, чтобы меню полностью открылось")]
    [SerializeField] private float removeDistance = 0.35f;

    [Header("Шляпа в руке (так же, как метла)")]
    [Tooltip("Точка на шляпе, за которую её держит рука (в осях шляпы: Y — от отверстия к верхушке). По умолчанию — середина тульи.")]
    [SerializeField] private Vector3 hatGripPoint = new Vector3(0f, 0.08f, 0f);
    [Tooltip("Дополнительный сдвиг точки хвата относительно руки")]
    [SerializeField] private Vector3 hatHoldOffset = Vector3.zero;
    [Tooltip("Поворот шляпы относительно руки. (0, 180, 0): верхушка вверх, отверстие вниз, залом верхушки — от игрока")]
    [SerializeField] private Vector3 hatHoldEuler = new Vector3(0f, 180f, 0f);

    [Header("Тряска шляпой")]
    [SerializeField] private float shakeSpeed = 1.3f;
    [SerializeField] private float shakeCooldown = 0.45f;

    [Header("Мир в меню")]
    [Tooltip("Во что превращается яркость: каждый канал = яркость пикселя × этот цвет. " +
             "Тёмное остаётся тёмным, а чем светлее предмет, тем светлее и сочнее его зелёный.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color greenTone = new Color(0.32f, 1.6f, 0.12f);
    [SerializeField, Range(-100f, 100f)] private float greenContrast = 35f;
    [Tooltip("Подсветка мира в меню (ночью без неё зелёный выходит слишком тёмным)")]
    [SerializeField] private float greenExposure = 0.6f;
    [SerializeField] private float resumeDuration = 1f;

    [Header("Карточка")]
    [SerializeField] private Font font;
    [SerializeField] private string[] systemFontNames = { "Copperplate Gothic Bold", "Copperplate Gothic", "Copperplate" };
    [SerializeField] private Vector3 cardOffset = new Vector3(0f, 0.1f, 0.05f);
    [SerializeField] private float cardWidth = 0.3f;
    [SerializeField] private Color cardColor = new Color32(10, 10, 10, 240);
    [SerializeField] private Color accentColor = new Color32(55, 126, 33, 255);
    [SerializeField] private Color labelColor = new Color32(246, 244, 228, 255);

    [Header("Отладка без шлема")]
    [SerializeField] private bool keyboardShortcuts = true;

    public event Action<int, string> ItemSubmitted;
    public bool IsOpen => _state != State.Worn;

    // ---------------------------------------------------------------------------------------

    enum State { Worn, Removing, Open, Returning }

    class Card
    {
        public GameObject root;
        public CanvasGroup group;
        public RectTransform labelRect, echoRect;
        public Vector3 from;
        public bool fromHat;
        public float bornAt, diedAt = -1f, punch;
        public Vector2 jitterA, jitterB;
    }

    const float CardCanvasWidth = 640f, CardCanvasHeight = 220f;
    const float CardSpawnDuration = 0.38f;

    // Размеры шляпы (м). Отверстие тульи — в начале координат шляпы, верхушка — по её оси +Y.
    const float BrimOuter = 0.13f, CrownRadius = 0.065f, CrownHeight = 0.2f, CrownBend = 0.06f;

    State _state = State.Worn;
    float _stateTime;
    bool _byKeyboard;
    int _index;
    float _savedTimeScale = 1f;
    float _tint;

    Transform _hat;
    Transform _leftAttach;
    Renderer[] _hatRenderers;
    MeshRenderer _hatGlow;
    Pose _returnFrom;

    Volume _volume;
    Font _font;
    FontStyle _fontStyle;

    Card _card;
    readonly List<Card> _dying = new List<Card>();
    float _jitterTimer;

    Vector3 _lastLeftPos;
    Vector3 _leftVelocity;
    float _shakeTimer;
    bool _stickWasPushed;

    // ---------------------------------------------------------------------------------------

    void Awake()
    {
        _font = ResolveFont(out _fontStyle);
        BuildHat();
        BuildTintVolume();
    }

    void Start()
    {
        FindMissingReferences();
        if (leftHand != null)
        {
            var interactor = leftHand.GetComponentInChildren<NearFarInteractor>(true);
            _leftAttach = interactor != null && interactor.attachTransform != null ? interactor.attachTransform : leftHand;
        }
        if (leftHand != null) _lastLeftPos = leftHand.position;
    }

    void OnDisable()
    {
        if (_state != State.Worn) Time.timeScale = _savedTimeScale;
        if (_volume != null) _volume.weight = 0f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _stateTime += dt;
        TrackLeftHand(dt);

        switch (_state)
        {
            case State.Worn: UpdateWorn(); break;
            case State.Removing: UpdateRemoving(); break;
            case State.Open: UpdateOpen(dt); break;
            case State.Returning: UpdateReturning(); break;
        }

        UpdateCards(dt);
        if (_volume != null) _volume.weight = _tint;
        if (_hatGlow != null) _hatGlow.enabled = _state == State.Open;
    }

    // ---------------------------------------------------------------------------------------
    // Состояния

    void UpdateWorn()
    {
        _tint = Mathf.MoveTowards(_tint, 0f, Time.unscaledDeltaTime * 4f);

        bool handGrab = leftHand != null && head != null && Pressed(leftGrip)
                        && Vector3.Distance(leftHand.position, HatHeadPosition) < grabRadius;
        bool keyGrab = KeyPressed(Key.P);
        if (!handGrab && !keyGrab) return;

        _byKeyboard = !handGrab;
        _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;                         // время останавливается сразу, как только взялся за шляпу
        SetHatVisible(true);
        Enter(State.Removing);
    }

    void UpdateRemoving()
    {
        float progress;
        if (_byKeyboard)
        {
            progress = Mathf.Clamp01(_stateTime / 0.5f);
            PlaceHat(LerpPose(HeadHatPose, HandHatPose, EaseInOut(progress)));
        }
        else
        {
            // Отпустил грип, не сняв шляпу до конца, — шляпа возвращается на голову
            if (!Held(leftGrip))
            {
                Time.timeScale = _savedTimeScale;
                SetHatVisible(false);
                Enter(State.Worn);
                return;
            }
            PlaceHat(HandHatPose);
            progress = Mathf.Clamp01(Vector3.Distance(leftHand.position, HatHeadPosition) / removeDistance);
        }

        _tint = Mathf.Max(_tint, EaseInOut(progress));   // зелёный «стекает» по миру вслед за шляпой
        if (progress >= 1f)
        {
            _tint = 1f;
            Enter(State.Open);
            SpawnCard(fromHat: true);
        }
    }

    void UpdateOpen(float dt)
    {
        PlaceHat(_byKeyboard && leftHand == null ? HeadHatPose : HandHatPose);
        _tint = 1f;

        // Следующий пункт: тряска шляпой к правой руке, стик вправо или N
        _shakeTimer -= dt;
        bool next = KeyPressed(Key.N);
        if (leftHand != null && rightHand != null && _shakeTimer <= 0f)
        {
            Vector3 toRight = (rightHand.position - leftHand.position).normalized;
            if (Vector3.Dot(_leftVelocity, toRight) > shakeSpeed) next = true;
        }
        var stick = leftThumbstick.action;
        if (stick != null)
        {
            bool pushed = stick.ReadValue<Vector2>().x > 0.7f;
            if (pushed && !_stickWasPushed) next = true;
            _stickWasPushed = pushed;
        }
        if (next && _shakeTimer <= 0f)
        {
            _shakeTimer = shakeCooldown;
            _index = (_index + 1) % entries.Length;
            KillCard();
            SpawnCard(fromHat: true);
        }

        // Выбор пункта
        if (Pressed(rightTrigger) || KeyPressed(Key.Enter) || KeyPressed(Key.NumpadEnter))
            Submit();

        // Надеть шляпу обратно
        bool handReturn = !_byKeyboard && leftHand != null
                          && Vector3.Distance(leftHand.position, HatHeadPosition) < grabRadius;
        if (handReturn || KeyPressed(Key.P))
        {
            _returnFrom = new Pose(_hat.position, _hat.rotation);
            KillCard();
            Enter(State.Returning);
        }
    }

    void UpdateReturning()
    {
        float hatT = Mathf.Clamp01(_stateTime / 0.25f);
        PlaceHat(LerpPose(_returnFrom, HeadHatPose, EaseInOut(hatT)));
        if (hatT >= 1f) SetHatVisible(false);

        // Мир «оттаивает»: цвет возвращается, время плавно разгоняется
        float t = Mathf.Clamp01(_stateTime / resumeDuration);
        _tint = 1f - EaseInOut(t);
        Time.timeScale = Mathf.Lerp(0f, _savedTimeScale, t * t);

        if (t >= 1f)
        {
            Time.timeScale = _savedTimeScale;
            _tint = 0f;
            Enter(State.Worn);
        }
    }

    void Enter(State s)
    {
        _state = s;
        _stateTime = 0f;
    }

    void Submit()
    {
        if (_card == null) return;
        _card.punch = 0.3f;
        Debug.Log("[Hat Menu] " + entries[_index].label);
        entries[_index].onSubmit?.Invoke();
        ItemSubmitted?.Invoke(_index, entries[_index].label);
    }

    // ---------------------------------------------------------------------------------------
    // Шляпа

    Vector3 HatHeadPosition => head != null ? head.TransformPoint(hatOnHead) : transform.position;
    Pose HeadHatPose => head != null ? new Pose(HatHeadPosition, Quaternion.Euler(0f, head.eulerAngles.y, 0f)) : new Pose(transform.position, transform.rotation);

    Pose HandHatPose
    {
        get
        {
            if (leftHand == null) return HeadHatPose;
            // Ровно как метла: точка хвата шляпы совмещается с точкой захвата руки (attach transform
            // Near-Far Interactor'а — к нему же XR Grab Interactable крепит метлу). Ось «вверх» руки
            // идёт сквозь кулак — вдоль неё у метлы черенок, у шляпы верхушка; отверстие смотрит вниз.
            Transform frame = _leftAttach != null ? _leftAttach : leftHand;
            Quaternion rot = frame.rotation * Quaternion.Euler(hatHoldEuler);
            Vector3 pos = frame.TransformPoint(hatHoldOffset) - rot * hatGripPoint;
            return new Pose(pos, rot);
        }
    }

    void PlaceHat(Pose pose) => _hat.SetPositionAndRotation(pose.position, pose.rotation);

    void SetHatVisible(bool visible)
    {
        foreach (var r in _hatRenderers) r.enabled = visible;
    }

    void BuildHat()
    {
        _hat = new GameObject("WizardHat").transform;
        _hat.SetParent(transform, false);

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { name = "HatMaterial" };
        mat.SetColor("_BaseColor", new Color(0.12f, 0.08f, 0.18f));
        mat.SetFloat("_Smoothness", 0.25f);
        var band = new Material(mat) { name = "HatBand" };
        band.SetColor("_BaseColor", accentColor);

        mat.SetFloat("_Cull", 0f);                      // видно и снаружи, и изнутри тульи
        band.SetFloat("_Cull", 0f);

        // Поля — кольцо, чтобы отверстие шляпы было открыто
        AddHatPart("Brim", BuildRing(CrownRadius, BrimOuter, 40), mat, Vector3.zero);
        // Лента у основания тульи
        AddHatPart("Band", BuildCone(CrownRadius + 0.002f, 0.025f, CrownRadius - 0.004f, 0f, 32), band, new Vector3(0f, 0.002f, 0f));
        // Тулья с заломленной верхушкой (без донышка — изнутри шляпа пустая)
        AddHatPart("Crown", BuildCone(CrownRadius, CrownHeight, 0.004f, CrownBend, 32), mat, Vector3.zero);

        // Зелёное свечение в глубине шляпы — отсюда «вылетают» карточки, как у фокусника
        var glowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "HatGlow" };
        glowMat.SetColor("_BaseColor", new Color(0.5f, 2.2f, 0.35f));
        glowMat.SetFloat("_Cull", 0f);
        _hatGlow = AddHatPart("Glow", BuildRing(0f, CrownRadius * 0.85f, 24), glowMat, new Vector3(0f, 0.05f, 0f))
            .GetComponent<MeshRenderer>();

        foreach (var r in _hat.GetComponentsInChildren<MeshRenderer>())
            r.shadowCastingMode = ShadowCastingMode.Off;
        _hatRenderers = _hat.GetComponentsInChildren<Renderer>();
        SetHatVisible(false);
    }

    GameObject AddHatPart(string name, Mesh mesh, Material material, Vector3 localPosition)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(_hat, false);
        go.transform.localPosition = localPosition;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>Плоское кольцо (или диск при inner = 0) в плоскости XZ.</summary>
    static Mesh BuildRing(float inner, float outer, int segments)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int s = 0; s <= segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(dir * inner);
            verts.Add(dir * outer);
        }
        for (int s = 0; s < segments; s++)
        {
            int i = s * 2;
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 3);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }
        var mesh = new Mesh { name = "HatRing" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Усечённый конус по оси Y; bend сдвигает верхушку вбок — получается «ведьмин» залом.</summary>
    static Mesh BuildCone(float bottomRadius, float height, float topRadius, float bend, int segments)
    {
        const int rings = 8;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            float radius = Mathf.Lerp(bottomRadius, topRadius, t);
            Vector3 offset = new Vector3(0f, height * t, -bend * t * t);
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                verts.Add(offset + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
        }
        int row = segments + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int i = r * row + s;
                tris.Add(i); tris.Add(i + row); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(i + row); tris.Add(i + row + 1);
            }
        var mesh = new Mesh { name = "HatPart" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------------------------------
    // Зелёный мир

    void BuildTintVolume()
    {
        var go = new GameObject("HatMenuTint");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 100f;
        _volume.weight = 0f;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // Channel Mixer превращает картинку в «зелёный монохром»: каждый выходной канал считается
        // из яркости пикселя (веса Rec.709), умноженной на свой коэффициент greenTone.
        // Чёрное → чёрное, середина → насыщенный зелёный, светлое → светло-салатовый.
        var mixer = profile.Add<ChannelMixer>(true);
        Vector3 luma = new Vector3(0.2126f, 0.7152f, 0.0722f) * 100f;   // микшер работает в процентах
        mixer.redOutRedIn.Override(luma.x * greenTone.r);
        mixer.redOutGreenIn.Override(luma.y * greenTone.r);
        mixer.redOutBlueIn.Override(luma.z * greenTone.r);
        mixer.greenOutRedIn.Override(luma.x * greenTone.g);
        mixer.greenOutGreenIn.Override(luma.y * greenTone.g);
        mixer.greenOutBlueIn.Override(luma.z * greenTone.g);
        mixer.blueOutRedIn.Override(luma.x * greenTone.b);
        mixer.blueOutGreenIn.Override(luma.y * greenTone.b);
        mixer.blueOutBlueIn.Override(luma.z * greenTone.b);

        var color = profile.Add<ColorAdjustments>(true);
        color.contrast.Override(greenContrast);
        color.postExposure.Override(greenExposure);
        _volume.sharedProfile = profile;
    }

    // ---------------------------------------------------------------------------------------
    // Карточки пунктов

    void SpawnCard(bool fromHat)
    {
        var card = new Card { bornAt = Time.unscaledTime, fromHat = fromHat, from = fromHat ? _hat.position : CardTargetPosition };

        card.root = new GameObject("MenuCard_" + entries[_index].label, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        card.root.transform.SetParent(transform, false);
        var canvas = card.root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)card.root.transform;
        rt.sizeDelta = new Vector2(CardCanvasWidth, CardCanvasHeight);
        card.root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        card.group = card.root.GetComponent<CanvasGroup>();

        // Чёрная скошенная плашка + зелёная полоса — как в меню Persona
        var plate = CreateQuad("Plate", rt, cardColor, 60f);
        Stretch(plate.rectTransform);
        var stripe = CreateQuad("Stripe", rt, accentColor, 60f);
        var sr = stripe.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0f);
        sr.sizeDelta = new Vector2(0f, 18f);
        sr.anchoredPosition = new Vector2(0f, 9f);

        var echo = CreateText("Echo", rt, accentColor, entries[_index].label);
        var label = CreateText("Label", rt, labelColor, entries[_index].label);
        card.echoRect = echo.rectTransform;
        card.labelRect = label.rectTransform;

        _card = card;
        PositionCard(card, 0f);
    }

    void KillCard()
    {
        if (_card == null) return;
        _card.diedAt = Time.unscaledTime;
        _dying.Add(_card);
        _card = null;
    }

    Vector3 CardTargetPosition => rightHand != null ? rightHand.TransformPoint(cardOffset)
                                : head != null ? head.TransformPoint(new Vector3(0.15f, -0.1f, 0.45f)) : transform.position;

    void UpdateCards(float dt)
    {
        _jitterTimer -= dt;
        bool newJitter = _jitterTimer <= 0f;
        if (newJitter) _jitterTimer = 0.07f;

        if (_card != null)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _card.bornAt) / CardSpawnDuration);
            _card.punch = Mathf.MoveTowards(_card.punch, 0f, dt * 1.5f);
            if (newJitter)
            {
                _card.jitterA = UnityEngine.Random.insideUnitCircle * 2.5f;
                _card.jitterB = UnityEngine.Random.insideUnitCircle * 5f;
            }
            _card.labelRect.anchoredPosition = _card.jitterA;
            _card.echoRect.anchoredPosition = new Vector2(9f, -7f) + _card.jitterB;
            PositionCard(_card, t);
        }

        for (int i = _dying.Count - 1; i >= 0; i--)
        {
            var c = _dying[i];
            float t = Mathf.Clamp01((Time.unscaledTime - c.diedAt) / 0.18f);
            c.group.alpha = 1f - t;
            c.root.transform.localScale *= 1f - dt * 4f;
            c.root.transform.Rotate(0f, 0f, 140f * dt, Space.Self);
            if (t >= 1f)
            {
                Destroy(c.root);
                _dying.RemoveAt(i);
            }
        }
    }

    void PositionCard(Card card, float t)
    {
        Vector3 target = CardTargetPosition;
        Vector3 pos;
        if (card.fromHat && _hat != null)
        {
            // Как у фокусника: карточка поднимается из глубины шляпы, выходит из отверстия
            // и по дуге летит в правую руку (квадратичная кривая Безье; шляпа может двигаться — точки берём каждый кадр)
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector3 inside = _hat.TransformPoint(new Vector3(0f, 0.07f, 0f));
            Vector3 mouth = _hat.position - _hat.up * 0.16f;
            pos = (1f - e) * (1f - e) * inside + 2f * (1f - e) * e * mouth + e * e * target;
        }
        else
        {
            pos = Vector3.LerpUnclamped(card.from, target, EaseOutBack(t));
        }
        Vector3 look = head != null ? pos - head.position : Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(0f, 0f, 6f);
        float grow = card.fromHat ? Mathf.LerpUnclamped(0.08f, 1f, EaseOutBack(t)) : Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(t * 1.3f));
        float scale = cardWidth / CardCanvasWidth * grow * (1f + card.punch);
        card.root.transform.SetPositionAndRotation(pos, rot);
        card.root.transform.localScale = Vector3.one * scale;
    }

    Text CreateText(string name, RectTransform parent, Color color, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontStyle = _fontStyle;
        t.fontSize = 110;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        Stretch(t.rectTransform);
        return t;
    }

    static SlantedQuad CreateQuad(string name, RectTransform parent, Color color, float skew)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SlantedQuad));
        go.transform.SetParent(parent, false);
        var q = go.GetComponent<SlantedQuad>();
        q.color = color;
        q.Skew = skew;
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

    // ---------------------------------------------------------------------------------------
    // Ввод и вспомогательное

    void TrackLeftHand(float dt)
    {
        if (leftHand == null || dt <= 0f) return;
        Vector3 v = (leftHand.position - _lastLeftPos) / dt;
        _leftVelocity = Vector3.Lerp(_leftVelocity, v, 0.5f);
        _lastLeftPos = leftHand.position;
    }

    static bool Pressed(InputActionProperty p) => p.action != null && p.action.WasPressedThisFrame();
    static bool Held(InputActionProperty p) => p.action != null && p.action.IsPressed();

    bool KeyPressed(Key key)
    {
        if (!keyboardShortcuts) return false;
        var kb = Keyboard.current;
        return kb != null && kb[key].wasPressedThisFrame;
    }

    void FindMissingReferences()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (leftHand == null) leftHand = FindByName("Left Controller");
        if (rightHand == null) rightHand = FindByName("Right Controller");

        var manager = FindAnyObjectByType<InputActionManager>();
        if (manager == null) return;
        foreach (var asset in manager.actionAssets)
        {
            if (asset == null) continue;
            if (leftGrip.action == null) leftGrip = new InputActionProperty(asset.FindAction("XRI Left Interaction/Select"));
            if (rightTrigger.action == null) rightTrigger = new InputActionProperty(asset.FindAction("XRI Right Interaction/Activate"));
            if (leftThumbstick.action == null) leftThumbstick = new InputActionProperty(asset.FindAction("XRI Left/Thumbstick"));
        }
    }

    static Transform FindByName(string name)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == name) return t;
        return null;
    }

    Font ResolveFont(out FontStyle style)
    {
        if (font != null) { style = FontStyle.Normal; return font; }
        string[] installed = Font.GetOSInstalledFontNames();
        foreach (var wanted in systemFontNames)
            foreach (var name in installed)
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    style = name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 ? FontStyle.Normal : FontStyle.Bold;
                    return Font.CreateDynamicFontFromOSFont(name, 110);
                }
        style = FontStyle.Bold;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static Pose LerpPose(Pose a, Pose b, float t) =>
        new Pose(Vector3.LerpUnclamped(a.position, b.position, t), Quaternion.SlerpUnclamped(a.rotation, b.rotation, t));

    static float EaseInOut(float t) => t * t * (3f - 2f * t);
    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
