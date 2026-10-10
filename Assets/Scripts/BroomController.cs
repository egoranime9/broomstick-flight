using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управление полётом на метле.
/// Вешается на объект-метлу (у него должен быть Rigidbody с isKinematic = true).
/// Камера игрока (XR Origin) делается дочерним объектом.
///
/// Режимы ввода:
///   Keyboard — отладка в редакторе без шлема (W/S — тангаж, A/D — поворот, Shift — газ).
///   VRHands  — контроллеры: наклон рук задаёт направление, курок — газ.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BroomController : MonoBehaviour
{
    public enum InputMode { Keyboard, VRHands }

    [Header("Ввод")]
    [SerializeField] private InputMode inputMode = InputMode.Keyboard;
    [Tooltip("Трансформ левого контроллера (только для режима VRHands)")]
    [SerializeField] private Transform leftHand;
    [Tooltip("Трансформ правого контроллера (только для режима VRHands)")]
    [SerializeField] private Transform rightHand;
    [Tooltip("Трансформ камеры/головы — нужен, чтобы повороты считались относительно взгляда")]
    [SerializeField] private Transform head;
    [Tooltip("Газ в режиме VRHands (например, XRI Right Interaction/Activate Value — правый курок)")]
    [SerializeField] private InputActionProperty throttleAction;

    [Header("Скорость")]
    [Tooltip("Скорость без газа, м/с")]
    [SerializeField] private float cruiseSpeed = 12f;
    [Tooltip("Скорость с зажатым газом, м/с")]
    [SerializeField] private float boostSpeed = 28f;
    [Tooltip("Насколько быстро набирается и теряется скорость")]
    [SerializeField] private float acceleration = 6f;

    [Header("Повороты")]
    [Tooltip("Градусов в секунду по тангажу (вверх-вниз)")]
    [SerializeField] private float pitchRate = 45f;
    [Tooltip("Градусов в секунду по рысканью (влево-вправо)")]
    [SerializeField] private float yawRate = 60f;
    [Tooltip("Визуальный крен метли в вираже, градусов")]
    [SerializeField] private float visualBank = 35f;
    [Tooltip("Сглаживание ввода. Больше значение — резче реакция")]
    [SerializeField] private float inputSmoothing = 5f;

    [Header("Ограничения")]
    [Tooltip("Максимальный угол пикирования и набора высоты, градусов")]
    [SerializeField] private float maxPitch = 60f;
    [SerializeField] private float minAltitude = 1.5f;
    [SerializeField] private float maxAltitude = 300f;

    [Header("Чувствительность рук (режим VRHands)")]
    [Tooltip("Наклон рук в градусах, который считается полным отклонением")]
    [SerializeField] private float handFullDeflection = 35f;
    [Tooltip("Мёртвая зона в градусах, чтобы метла не дёргалась от дрожания рук")]
    [SerializeField] private float handDeadZone = 5f;

    // Текущее состояние полёта — их читает мост к платформе и тактильные скрипты.
    public float CurrentSpeed { get; private set; }
    public float YawRateNow { get; private set; }       // град/с, знак = сторона виража
    public float PitchAngle { get; private set; }       // град, минус = пикирование
    public Vector3 Velocity { get; private set; }
    public bool IsBoosting { get; private set; }

    private Rigidbody _rb;
    private float _pitchInput;                          // -1..1
    private float _yawInput;                            // -1..1
    private float _throttleInput;                       // 0..1
    private float _currentBank;
    private Vector3 _previousPosition;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _previousPosition = transform.position;

        if (head == null && Camera.main != null)
            head = Camera.main.transform;
    }

    private void OnEnable()
    {
        // Ссылки на ассет экшенов включает Input Action Manager на XR Origin, а «ручной» экшен включаем сами
        if (throttleAction.reference == null) throttleAction.action?.Enable();
    }

    private void Update()
    {
        ReadInput();
    }

    private void FixedUpdate()
    {
        Fly(Time.fixedDeltaTime);
    }

    private void ReadInput()
    {
        float targetPitch, targetYaw, targetThrottle;

        if (inputMode == InputMode.VRHands && leftHand != null && rightHand != null)
        {
            // Древко метлы — воображаемая линия между руками.
            // Средний поворот рук задаёт, куда направлена метла.
            Quaternion handle = Quaternion.Slerp(leftHand.rotation, rightHand.rotation, 0.5f);
            Vector3 handleForward = handle * Vector3.forward;

            // Наклон древка вниз-вверх относительно горизонта.
            float tilt = -Mathf.Asin(Mathf.Clamp(handleForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            targetPitch = Deflection(tilt);

            // Поворот древка относительно направления взгляда.
            Vector3 flatHandle = Vector3.ProjectOnPlane(handleForward, Vector3.up);
            Vector3 reference = head != null
                ? Vector3.ProjectOnPlane(head.forward, Vector3.up)
                : Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            float twist = Vector3.SignedAngle(reference, flatHandle, Vector3.up);
            targetYaw = Deflection(twist);

            // Газ — курок контроллера (экшен задаётся в инспекторе)
            var throttle = throttleAction.action;
            targetThrottle = throttle != null ? Mathf.Clamp01(throttle.ReadValue<float>()) : 0f;
        }
        else
        {
            var kb = Keyboard.current;
            bool up = kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed);
            bool down = kb != null && (kb.sKey.isPressed || kb.downArrowKey.isPressed);
            bool left = kb != null && (kb.aKey.isPressed || kb.leftArrowKey.isPressed);
            bool right = kb != null && (kb.dKey.isPressed || kb.rightArrowKey.isPressed);

            targetPitch = (down ? 1f : 0f) - (up ? 1f : 0f);    // W — нос вниз, S — вверх
            targetYaw = (right ? 1f : 0f) - (left ? 1f : 0f);
            targetThrottle = kb != null && kb.leftShiftKey.isPressed ? 1f : 0f;
        }

        // Сглаживаем ввод — резкие рывки камеры это первая причина укачивания.
        float k = 1f - Mathf.Exp(-inputSmoothing * Time.deltaTime);
        _pitchInput = Mathf.Lerp(_pitchInput, targetPitch, k);
        _yawInput = Mathf.Lerp(_yawInput, targetYaw, k);
        _throttleInput = Mathf.Lerp(_throttleInput, targetThrottle, k);
    }

    /// <summary>Переводит угол наклона руки в отклонение -1..1 с мёртвой зоной.</summary>
    private float Deflection(float angle)
    {
        float sign = Mathf.Sign(angle);
        float magnitude = Mathf.Abs(angle);
        if (magnitude < handDeadZone) return 0f;
        float normalized = (magnitude - handDeadZone) / Mathf.Max(0.01f, handFullDeflection - handDeadZone);
        return sign * Mathf.Clamp01(normalized);
    }

    private void Fly(float dt)
    {
        // Поворот
        float pitchDelta = _pitchInput * pitchRate * dt;
        float yawDelta = _yawInput * yawRate * dt;

        PitchAngle = Mathf.Clamp(PitchAngle + pitchDelta, -maxPitch, maxPitch);
        YawRateNow = yawDelta / dt;

        float newYaw = transform.eulerAngles.y + yawDelta;

        // Визуальный крен — метла заваливается в вираж. На камеру не влияет напрямую,
        // потому что крен горизонта в VR переносится тяжело, поэтому угол небольшой.
        _currentBank = Mathf.Lerp(_currentBank, -_yawInput * visualBank, 1f - Mathf.Exp(-4f * dt));
        transform.rotation = Quaternion.Euler(PitchAngle, newYaw, _currentBank);

        // Скорость
        IsBoosting = _throttleInput > 0.5f;
        float targetSpeed = Mathf.Lerp(cruiseSpeed, boostSpeed, _throttleInput);
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, acceleration * dt);

        // Движение всегда вдоль древка метлы
        Vector3 direction = Quaternion.Euler(PitchAngle, newYaw, 0f) * Vector3.forward;
        Vector3 next = transform.position + direction * CurrentSpeed * dt;
        next.y = Mathf.Clamp(next.y, minAltitude, maxAltitude);

        _rb.MovePosition(next);

        Velocity = (next - _previousPosition) / dt;
        _previousPosition = next;
    }
}
