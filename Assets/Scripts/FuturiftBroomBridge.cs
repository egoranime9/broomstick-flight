using UnityEngine;

// Этот скрипт зависит от пакета Futurift (FutuRiftController, UdpPortSender, UdpOptions),
// который берётся из презентации курса и пока не импортирован в проект.
// Пока пакета нет, класс скомпилирован "выключенным" под #if, чтобы не ломать
// компиляцию всего проекта. Как только импортируешь пакет Futurift, добавь символ
// FUTURIFT_INSTALLED в Project Settings -> Player -> Scripting Define Symbols,
// и скрипт заработает как обычно.
#if FUTURIFT_INSTALLED

/// <summary>
/// Мост между полётом метлы и капсулой Futurift v2.
/// Вешается на любой объект сцены, в поле Broom кидается метла.
///
/// Логика по слайду «От чего должно зависеть движение капсулы»:
///   линейное ускорение -> тангаж (разгон откидывает назад, торможение — вперёд)
///   угловая скорость   -> крен в сторону виража
///   положение в пространстве -> тангаж при пикировании и наборе высоты
///   турбулентность     -> мелкое дрожание
///   столкновения       -> резкий короткий толчок (вызывается методом Jolt)
///
/// Пределы капсулы: тангаж от -15 до 21 градуса, крен ±18.
/// Если плагин лежит в неймспейсе, добавь сюда соответствующий using.
/// </summary>
public class FuturiftBroomBridge : MonoBehaviour
{
    [Header("Подключение")]
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private int port = 6065;
    [SerializeField] private BroomController broom;

    [Header("Пределы капсулы, градусы")]
    [SerializeField] private float pitchMin = -15f;
    [SerializeField] private float pitchMax = 21f;
    [SerializeField] private float rollLimit = 18f;

    [Header("Коэффициенты")]
    [Tooltip("Сколько градусов тангажа даёт 1 м/с² продольного ускорения")]
    [SerializeField] private float accelerationToPitch = 1.2f;
    [Tooltip("Доля реального угла пикирования, передаваемая в капсулу")]
    [SerializeField] private float diveScale = 0.25f;
    [Tooltip("Сколько градусов крена даёт 1 град/с виража")]
    [SerializeField] private float yawRateToRoll = 0.25f;
    [Tooltip("Скорость возврата капсулы к нейтрали, чтобы не упираться в предел")]
    [SerializeField] private float washout = 0.6f;
    [Tooltip("Сглаживание — без него капсула будет дёргаться")]
    [SerializeField] private float smoothing = 0.15f;

    [Header("Турбулентность")]
    [SerializeField] private float turbulenceAmplitude = 1.5f;
    [SerializeField] private float turbulenceFrequency = 3f;

    [Header("Толчок при столкновении")]
    [SerializeField] private float joltDuration = 0.25f;

    private FutuRiftController _controller;

    private float _previousSpeed;
    private float _pitch, _roll;
    private float _pitchVelocity, _rollVelocity;
    private float _joltPitch, _joltRoll, _joltTimeLeft;
    private float _noiseSeed;

    private void Awake()
    {
        var udpOptions = new UdpOptions
        {
            ip = ipAddress,
            port = port
        };

        _controller = new FutuRiftController(new UdpPortSender(udpOptions));
        _noiseSeed = Random.value * 100f;

        if (broom == null)
            broom = FindObjectOfType<BroomController>();
    }

    private void OnEnable() => _controller?.Start();
    private void OnDisable() => _controller?.Stop();

    private void Update()
    {
        if (broom == null || _controller == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // 1. Линейное ускорение вдоль полёта.
        float speed = broom.CurrentSpeed;
        float longitudinalAcceleration = (speed - _previousSpeed) / dt;
        _previousSpeed = speed;
        float targetPitch = longitudinalAcceleration * accelerationToPitch;

        // 2. Положение в пространстве: часть реального угла пикирования.
        // Нос вниз в игре = наклон капсулы вперёд, поэтому знак минус.
        targetPitch += -broom.PitchAngle * diveScale;

        // 3. Угловая скорость виража.
        float targetRoll = -broom.YawRateNow * yawRateToRoll;

        // 4. Турбулентность — тем сильнее, чем быстрее летим.
        float speedFactor = Mathf.Clamp01(speed / 25f);
        float t = Time.time * turbulenceFrequency;
        targetPitch += (Mathf.PerlinNoise(_noiseSeed, t) - 0.5f) * 2f * turbulenceAmplitude * speedFactor;
        targetRoll += (Mathf.PerlinNoise(t, _noiseSeed) - 0.5f) * 2f * turbulenceAmplitude * speedFactor;

        // Washout: капсула постоянно тянется к нейтрали, иначе на затяжном
        // пикировании она упрётся в предел и перестанет что-либо передавать.
        targetPitch *= Mathf.Exp(-washout * dt * 5f);
        targetRoll *= Mathf.Exp(-washout * dt * 5f);

        // 5. Толчок при столкновении поверх всего остального.
        if (_joltTimeLeft > 0f)
        {
            float k = _joltTimeLeft / joltDuration;
            targetPitch += _joltPitch * k;
            targetRoll += _joltRoll * k;
            _joltTimeLeft -= dt;
        }

        _pitch = Mathf.SmoothDamp(_pitch, targetPitch, ref _pitchVelocity, smoothing);
        _roll = Mathf.SmoothDamp(_roll, targetRoll, ref _rollVelocity, smoothing);

        _controller.Pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
        _controller.Roll = Mathf.Clamp(_roll, -rollLimit, rollLimit);
    }

    /// <summary>
    /// Вызывается при столкновении. direction — направление удара в мировых координатах,
    /// strength — сила от 0 до 1.
    /// </summary>
    public void Jolt(Vector3 direction, float strength = 1f)
    {
        Vector3 local = broom != null
            ? broom.transform.InverseTransformDirection(direction.normalized)
            : direction.normalized;

        _joltPitch = -local.z * 12f * strength;
        _joltRoll = -local.x * 12f * strength;
        _joltTimeLeft = joltDuration;
    }
}

#endif // FUTURIFT_INSTALLED
