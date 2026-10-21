using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Полёт на метле под обычный VR-шлем (без платформы).
///
/// Пешком: метлу можно взять одной или двумя руками. Как только её держат ОБЕ руки —
/// метла выскальзывает из рук, подныривает под игрока и подбрасывает его в воздух: ты сидишь на метле.
///
/// В полёте:
///   * летишь туда, куда смотришь (взгляд вниз — снижаешься, вверх — набираешь высоту);
///   * правый курок — газ (чем сильнее жмёшь, тем быстрее);
///   * левый курок — тормоз, метла останавливается и зависает;
///   * правый стик вверх/вниз — подняться/опуститься прямо, не меняя курса;
///   * правый стик влево/вправо — поворот рывком на 45° (чтобы не крутиться на стуле);
///   * кнопка A — приземлиться и слезть с метлы (во время посадки газ отменяет её).
///
/// Игрока двигает CharacterController на XR Origin, поэтому сквозь землю и холмы не пролететь.
/// Пешком XR Origin сам держится на уровне земли под головой.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class BroomController : MonoBehaviour
{
    public enum FlightState { OnFoot, Mounting, Flying, Landing }

    [Header("Ссылки (если пусто — найдутся сами)")]
    [SerializeField] private XROrigin rig;
    [SerializeField] private Transform head;

    [Header("Посадка на метлу")]
    [Tooltip("Насколько ниже глаз проходит древко, м")]
    [SerializeField] private float seatBelowEyes = 0.7f;
    [Tooltip("Насколько позади середины метлы ты сидишь, м")]
    [SerializeField] private float seatBehindMiddle = 0.25f;
    [Tooltip("Сколько длится «запрыгивание» на метлу, с")]
    [SerializeField] private float mountDuration = 0.45f;
    [Tooltip("На сколько метров метла подбрасывает при посадке")]
    [SerializeField] private float mountLift = 0.6f;

    [Header("Скорость, м/с и м/с²")]
    [SerializeField] private float topSpeed = 16f;
    [SerializeField] private float accelerationRate = 7f;
    [Tooltip("Как быстро метла сбавляет ход, если отпустить газ")]
    [SerializeField] private float coastDeceleration = 2f;
    [Tooltip("Как быстро метла останавливается на тормозе")]
    [SerializeField] private float brakeDeceleration = 16f;
    [Tooltip("Скорость подъёма и спуска стиком")]
    [SerializeField] private float climbSpeed = 5f;
    [Tooltip("Скорость снижения при посадке")]
    [SerializeField] private float landingSpeed = 4f;

    [Header("Повороты")]
    [Tooltip("Как быстро курс догоняет взгляд. Меньше — плавнее, больше — резче")]
    [SerializeField] private float steerSharpness = 2.5f;
    [SerializeField] private float snapTurnDegrees = 45f;

    [Header("Ограничения")]
    [SerializeField] private float maxHeightAboveGround = 250f;
    [Tooltip("Отступ от края террейна, дальше которого не улететь, м")]
    [SerializeField] private float worldEdgeMargin = 15f;

    [Header("Вид метлы в полёте")]
    [SerializeField] private float maxVisualPitch = 25f;
    [SerializeField] private float maxVisualBank = 20f;

    [Header("Кнопки (если пусто — берутся из XRI Default Input Actions)")]
    [SerializeField] private InputActionProperty throttleInput;    // XRI Right Interaction/Activate Value
    [SerializeField] private InputActionProperty brakeInput;       // XRI Left Interaction/Activate Value
    [SerializeField] private InputActionProperty steerStick;       // XRI Right/Thumbstick
    [SerializeField] private InputActionProperty leftGripInput;    // XRI Left Interaction/Select Value
    [SerializeField] private InputActionProperty rightGripInput;   // XRI Right Interaction/Select Value

    // Состояние полёта — его читают мост к платформе и тактильные скрипты.
    public FlightState State => _state;
    public bool IsMounted => _state != FlightState.OnFoot;
    public float CurrentSpeed { get; private set; }
    public float YawRateNow { get; private set; }       // град/с, знак = сторона поворота
    public float PitchAngle { get; private set; }       // град, минус = снижение
    public Vector3 Velocity { get; private set; }
    public bool IsBoosting { get; private set; }

    private FlightState _state = FlightState.OnFoot;
    private Rigidbody _rb;
    private XRGrabInteractable _grab;
    private Collider[] _broomColliders;
    private CharacterController _body;
    private HapticImpulsePlayer[] _haptics;
    private InputAction _landAction;

    private Vector3 _handleLocal;      // направление «к черенку» в осях метлы
    private Vector3 _upLocal;
    private Vector3 _middleLocal;      // середина древка
    private Vector3 _course = Vector3.forward;
    private float _speed;
    private float _vertical;
    private float _visualPitch;
    private float _visualBank;
    private float _mountT;
    private Vector3 _mountFromPos;
    private Quaternion _mountFromRot;
    private bool _snapLatched;
    private bool _waitGripRelease;
    private RigidbodyInterpolation _savedInterpolation;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    // ───────────────────────── жизненный цикл ─────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _grab = GetComponent<XRGrabInteractable>();
        _broomColliders = GetComponentsInChildren<Collider>(true);
        ComputeBroomAxes();

        _landAction = new InputAction("Broom Land", InputActionType.Button);
        _landAction.AddBinding("<XRController>{RightHand}/primaryButton");
        _landAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
    }

    private void OnEnable() => _landAction?.Enable();
    private void OnDisable() => _landAction?.Disable();
    private void OnDestroy() => _landAction?.Dispose();

    private void Start()
    {
        if (rig == null) rig = FindAnyObjectByType<XROrigin>();
        if (head == null)
        {
            if (rig != null && rig.Camera != null) head = rig.Camera.transform;
            else if (Camera.main != null) head = Camera.main.transform;
        }

        if (rig != null)
        {
            _body = rig.GetComponent<CharacterController>();
            if (_body == null) _body = rig.gameObject.AddComponent<CharacterController>();
            _body.radius = 0.25f;
            _body.skinWidth = 0.04f;
            _body.stepOffset = 0.4f;
            _body.slopeLimit = 60f;
            _body.minMoveDistance = 0f;
            _haptics = rig.GetComponentsInChildren<HapticImpulsePlayer>(true);
        }
        else
        {
            Debug.LogWarning("[Broom] На сцене нет XR Origin — летать некому.");
        }

        SetIgnoreBody(true);
        ResolveActions();
    }

    private void Update()
    {
        if (rig == null || head == null || _body == null) return;
        float dt = Time.deltaTime;

        switch (_state)
        {
            case FlightState.OnFoot: UpdateOnFoot(dt); break;
            case FlightState.Mounting: UpdateMounting(dt); break;
            case FlightState.Flying: UpdateFlying(dt); break;
            case FlightState.Landing: UpdateLanding(dt); break;
        }
    }

    private void LateUpdate()
    {
        if (_state == FlightState.OnFoot || head == null) return;

        // Метла всегда под игроком: её ставим уже после того, как сдвинулись риг и голова
        SeatPose(Time.deltaTime, out Vector3 pos, out Quaternion rot);
        if (_state == FlightState.Mounting)
        {
            float k = Mathf.SmoothStep(0f, 1f, _mountT);
            pos = Vector3.Lerp(_mountFromPos, pos, k);
            rot = Quaternion.Slerp(_mountFromRot, rot, k);
        }
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── пешком ─────────────────────────

    private void UpdateOnFoot(float dt)
    {
        if (_waitGripRelease)
        {
            // После посадки ждём, пока игрок отпустит обе кнопки хвата, иначе метла сразу прыгнет в руки
            if (ReadFloat(leftGripInput) < 0.3f && ReadFloat(rightGripInput) < 0.3f)
            {
                _waitGripRelease = false;
                _grab.enabled = true;
            }
        }
        else if (_grab.enabled && _grab.interactorsSelecting.Count >= 2)
        {
            BeginMount();
            return;
        }

        if (dt > 0f) FollowGround(dt);
        if (!_grab.isSelected) KeepBroomAboveGround();
    }

    /// <summary>Держит пол XR Origin на уровне земли под головой — чтобы не проваливаться в холмы.</summary>
    private void FollowGround(float dt)
    {
        if (!TryGroundHeight(head.position, out float ground)) return;
        Vector3 p = rig.transform.position;
        float diff = ground - p.y;
        if (Mathf.Abs(diff) < 0.002f) return;
        p.y = Mathf.Abs(diff) > 2f ? ground : p.y + diff * (1f - Mathf.Exp(-12f * dt));
        rig.transform.position = p;
    }

    /// <summary>Отпущенная метла не уходит в траву.</summary>
    private void KeepBroomAboveGround()
    {
        float bottom = float.MaxValue;
        foreach (var c in _broomColliders)
            if (c != null && c.enabled && !c.isTrigger) bottom = Mathf.Min(bottom, c.bounds.min.y);
        if (bottom == float.MaxValue) return;
        if (!TryGroundHeight(transform.position, out float ground)) return;

        const float clearance = 0.05f;
        if (bottom < ground + clearance)
            transform.position += Vector3.up * (ground + clearance - bottom);
    }

    // ───────────────────────── посадка на метлу ─────────────────────────

    private void BeginMount()
    {
        // Отпускаем метлу из рук и выключаем хват, пока сидим на ней
        if (_grab.interactionManager != null)
            _grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_grab);
        _grab.enabled = false;

        _savedInterpolation = _rb.interpolation;
        _rb.interpolation = RigidbodyInterpolation.None;
        SetIgnoreBody(true);

        _course = Flat(head.forward);
        _speed = 0f;
        _vertical = 0f;
        _visualPitch = 0f;
        _visualBank = 0f;
        _snapLatched = false;
        _mountT = 0f;
        _mountFromPos = transform.position;
        _mountFromRot = transform.rotation;
        Physics.SyncTransforms();

        Pulse(0.6f, 0.2f);
        _state = FlightState.Mounting;
    }

    private void UpdateMounting(float dt)
    {
        if (dt <= 0f) return;
        float step = dt / Mathf.Max(0.01f, mountDuration);
        float before = _mountT;
        _mountT = Mathf.Min(1f, _mountT + step);

        UpdateBodyCapsule();
        _body.Move(Vector3.up * mountLift * (_mountT - before));

        if (_mountT >= 1f) _state = FlightState.Flying;
    }

    // ───────────────────────── полёт ─────────────────────────

    private void UpdateFlying(float dt)
    {
        if (dt <= 0f) return;    // меню-шляпа остановило время

        float throttle = ReadFloat(throttleInput);
        float brake = ReadFloat(brakeInput);
        Vector2 stick = ReadVector(steerStick);

        if (_landAction.WasPressedThisFrame())
        {
            _state = FlightState.Landing;
            Pulse(0.3f, 0.1f);
            return;
        }

        // Поворот рывком: вращаем весь XR Origin вокруг головы
        if (!_snapLatched && Mathf.Abs(stick.x) > 0.75f)
        {
            float angle = Mathf.Sign(stick.x) * snapTurnDegrees;
            rig.RotateAroundCameraUsingOriginUp(angle);
            _course = Quaternion.AngleAxis(angle, Vector3.up) * _course;
            _snapLatched = true;
            Physics.SyncTransforms();
        }
        else if (Mathf.Abs(stick.x) < 0.3f)
        {
            _snapLatched = false;
        }

        // Курс плавно догоняет взгляд
        Vector3 previousFlat = Flat(_course);
        _course = Vector3.Slerp(_course, head.forward, 1f - Mathf.Exp(-steerSharpness * dt)).normalized;

        // Скорость: газ разгоняет, без газа метла медленно сбавляет, тормоз останавливает
        if (brake > 0.1f)
        {
            _speed = Mathf.MoveTowards(_speed, 0f, brakeDeceleration * brake * dt);
        }
        else
        {
            float target = topSpeed * throttle;
            float rate = target > _speed ? accelerationRate : coastDeceleration;
            _speed = Mathf.MoveTowards(_speed, target, rate * dt);
        }

        // Подъём и спуск стиком
        float verticalTarget = Mathf.Abs(stick.y) > 0.15f ? stick.y * climbSpeed : 0f;
        _vertical = Mathf.Lerp(_vertical, verticalTarget, 1f - Mathf.Exp(-6f * dt));

        Move(_course * _speed + Vector3.up * _vertical, dt);
        UpdateTelemetry(previousFlat, throttle, dt);
    }

    private void UpdateLanding(float dt)
    {
        if (dt <= 0f) return;

        float throttle = ReadFloat(throttleInput);
        if (throttle > 0.5f)                 // передумал садиться
        {
            _state = FlightState.Flying;
            return;
        }

        Vector3 previousFlat = Flat(_course);
        _speed = Mathf.MoveTowards(_speed, 0f, brakeDeceleration * 0.5f * dt);

        // У земли снижаемся медленнее, чтобы не «шлёпнуться»
        float height = HeightAboveGround();
        float down = Mathf.Min(landingSpeed, 0.4f + height * 1.5f);
        _vertical = Mathf.MoveTowards(_vertical, -down, 10f * dt);

        Move(previousFlat * _speed + Vector3.up * _vertical, dt);
        UpdateTelemetry(previousFlat, 0f, dt);

        if (_body.isGrounded || height < 0.03f) Dismount();
    }

    private void Dismount()
    {
        _state = FlightState.OnFoot;
        _speed = 0f;
        _vertical = 0f;
        CurrentSpeed = 0f;
        YawRateNow = 0f;
        PitchAngle = 0f;
        Velocity = Vector3.zero;
        IsBoosting = false;

        _rb.interpolation = _savedInterpolation;
        _waitGripRelease = true;             // хват включится, когда обе руки отпустят кнопки
        Pulse(0.4f, 0.15f);
    }

    /// <summary>Двигает игрока CharacterController-ом: земля и холмы не пускают вниз.</summary>
    private void Move(Vector3 velocity, float dt)
    {
        // Потолок по высоте
        if (velocity.y > 0f && HeightAboveGround() > maxHeightAboveGround) velocity.y = 0f;

        // Край мира: дальше террейна не улетаем
        var terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 min = terrain.transform.position + new Vector3(worldEdgeMargin, 0f, worldEdgeMargin);
            Vector3 max = terrain.transform.position + terrain.terrainData.size - new Vector3(worldEdgeMargin, 0f, worldEdgeMargin);
            Vector3 h = head.position;
            if ((h.x < min.x && velocity.x < 0f) || (h.x > max.x && velocity.x > 0f)) velocity.x = 0f;
            if ((h.z < min.z && velocity.z < 0f) || (h.z > max.z && velocity.z > 0f)) velocity.z = 0f;
        }

        UpdateBodyCapsule();
        _body.Move(velocity * dt);
        Velocity = _body.velocity;
    }

    /// <summary>Капсула игрока: от пола XR Origin до головы, по месту головы.</summary>
    private void UpdateBodyCapsule()
    {
        Vector3 local = rig.transform.InverseTransformPoint(head.position);
        float height = Mathf.Clamp(local.y, 0.6f, 2.2f);
        _body.height = height;
        _body.center = new Vector3(local.x, height * 0.5f + _body.skinWidth, local.z);
    }

    private void UpdateTelemetry(Vector3 previousFlat, float throttle, float dt)
    {
        CurrentSpeed = _speed;
        YawRateNow = Vector3.SignedAngle(previousFlat, Flat(_course), Vector3.up) / dt;
        Vector3 v = Velocity;
        float flat = new Vector2(v.x, v.z).magnitude;
        PitchAngle = v.sqrMagnitude > 0.01f ? Mathf.Atan2(v.y, flat) * Mathf.Rad2Deg : 0f;
        IsBoosting = throttle > 0.9f;
    }

    // ───────────────────────── поза метлы ─────────────────────────

    /// <summary>Где должна быть метла, чтобы игрок на ней сидел.</summary>
    private void SeatPose(float dt, out Vector3 position, out Quaternion rotation)
    {
        Vector3 forward = Flat(_course);

        // Нос вверх при наборе высоты, вниз при снижении
        Vector3 motion = _course * _speed + Vector3.up * _vertical;
        float targetPitch = 0f;
        if (motion.sqrMagnitude > 0.04f)
        {
            float elevation = Mathf.Atan2(motion.y, new Vector2(motion.x, motion.z).magnitude) * Mathf.Rad2Deg;
            targetPitch = Mathf.Clamp(elevation, -maxVisualPitch, maxVisualPitch) * Mathf.Clamp01(motion.magnitude / 3f);
        }
        float targetBank = Mathf.Clamp(-YawRateNow * 0.4f, -maxVisualBank, maxVisualBank);
        if (dt > 0f)
        {
            float k = 1f - Mathf.Exp(-4f * dt);
            _visualPitch = Mathf.Lerp(_visualPitch, targetPitch, k);
            _visualBank = Mathf.Lerp(_visualBank, targetBank, k);
        }

        Quaternion world = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(-_visualPitch, 0f, _visualBank);
        rotation = world * Quaternion.Inverse(Quaternion.LookRotation(_handleLocal, _upLocal));

        Vector3 seatWorld = head.position - Vector3.up * seatBelowEyes - forward * 0.1f;
        Vector3 seatLocal = _middleLocal - _handleLocal * seatBehindMiddle;
        position = seatWorld - rotation * Vector3.Scale(transform.lossyScale, seatLocal);
    }

    /// <summary>Оси модели: куда смотрит черенок и где середина древка.</summary>
    private void ComputeBroomAxes()
    {
        var capsule = GetComponent<CapsuleCollider>();
        var grip = transform.Find("GripPoint");
        var secondary = transform.Find("SecondaryGripPoint");

        _middleLocal = capsule != null ? capsule.center : Vector3.zero;
        if (grip != null && secondary != null && (secondary.localPosition - grip.localPosition).sqrMagnitude > 1e-4f)
        {
            _handleLocal = (secondary.localPosition - grip.localPosition).normalized;
            _middleLocal = grip.localPosition;
        }
        else if (capsule != null)
        {
            _handleLocal = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
        }
        else
        {
            _handleLocal = Vector3.forward;
        }

        Vector3 up = Mathf.Abs(Vector3.Dot(_handleLocal, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward;
        _upLocal = Vector3.ProjectOnPlane(up, _handleLocal).normalized;
    }

    // ───────────────────────── помощники ─────────────────────────

    private float HeightAboveGround()
    {
        return TryGroundHeight(head.position, out float ground) ? rig.transform.position.y - ground : float.MaxValue;
    }

    /// <summary>Высота поверхности под точкой (земля, террейн, любые коллайдеры, кроме метлы и самого игрока).</summary>
    private bool TryGroundHeight(Vector3 point, out float height)
    {
        height = float.NegativeInfinity;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 30f, Vector3.down, _hits, 3000f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = _hits[i].collider;
            if (c == null || IsOurs(c)) continue;
            height = Mathf.Max(height, _hits[i].point.y);
        }
        return !float.IsNegativeInfinity(height);
    }

    private bool IsOurs(Collider c)
    {
        Transform t = c.transform;
        return t.IsChildOf(transform) || (rig != null && t.IsChildOf(rig.transform));
    }

    private void SetIgnoreBody(bool ignore)
    {
        if (_body == null) return;
        foreach (var c in _broomColliders)
            if (c != null) Physics.IgnoreCollision(_body, c, ignore);
    }

    private Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 1e-4f) v = rig != null ? rig.transform.forward : Vector3.forward;
        v.y = 0f;
        return v.normalized;
    }

    /// <summary>Короткая вибрация обоих контроллеров.</summary>
    public void Pulse(float amplitude, float duration)
    {
        if (_haptics == null) return;
        foreach (var h in _haptics)
            if (h != null) h.SendHapticImpulse(amplitude, duration);
    }

    private void ResolveActions()
    {
        var manager = FindAnyObjectByType<InputActionManager>();
        if (manager == null || manager.actionAssets == null) return;
        foreach (var asset in manager.actionAssets)
        {
            if (asset == null) continue;
            Fill(ref throttleInput, asset, "XRI Right Interaction/Activate Value");
            Fill(ref brakeInput, asset, "XRI Left Interaction/Activate Value");
            Fill(ref steerStick, asset, "XRI Right/Thumbstick");
            Fill(ref leftGripInput, asset, "XRI Left Interaction/Select Value");
            Fill(ref rightGripInput, asset, "XRI Right Interaction/Select Value");
        }
    }

    private static void Fill(ref InputActionProperty property, InputActionAsset asset, string path)
    {
        var current = property.action;
        if (current != null && current.bindings.Count > 0) return;
        var found = asset.FindAction(path);
        if (found != null) property = new InputActionProperty(found);
    }

    private static float ReadFloat(InputActionProperty property)
    {
        var action = property.action;
        if (action == null) return 0f;
        if (!action.enabled) action.Enable();
        return action.ReadValue<float>();
    }

    private static Vector2 ReadVector(InputActionProperty property)
    {
        var action = property.action;
        if (action == null) return Vector2.zero;
        if (!action.enabled) action.Enable();
        return action.ReadValue<Vector2>();
    }
}
