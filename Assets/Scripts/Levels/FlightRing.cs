using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Светящееся кольцо, сквозь которое надо пролететь.
/// Кольцо лежит в плоскости XY объекта, синяя ось (forward) — направление пролёта.
/// Сама модель строится при запуске игры; в окне Scene кольцо видно как жёлтый гизмо-круг.
///
/// Состояния: Waiting — тусклое, ждёт очереди; Next — следующее, ярко пульсирует и светит;
/// Passed — пройдено: вспыхивает зелёным, разлетается и гаснет.
/// </summary>
[DisallowMultipleComponent]
public class FlightRing : MonoBehaviour
{
    public enum RingState { Waiting, Next, Passed }

    [Tooltip("Радиус кольца, м")]
    [SerializeField] private float radius = 3f;
    [Tooltip("Толщина обода, м")]
    [SerializeField] private float thickness = 0.2f;
    [Tooltip("Подсказка, пока это кольцо — следующее. Пусто — общая подсказка уровня")]
    [TextArea(1, 3)] [SerializeField] private string hint;

    [Header("Цвета (HDR: ярче 1 — светится через Bloom)")]
    [ColorUsage(false, true)] [SerializeField] private Color nextColor = new Color(3.2f, 2.2f, 0.6f);
    [ColorUsage(false, true)] [SerializeField] private Color waitingColor = new Color(0.45f, 0.36f, 0.14f);
    [ColorUsage(false, true)] [SerializeField] private Color passedColor = new Color(1.2f, 4f, 0.8f);

    public string Hint => hint;
    public RingState State { get; private set; } = RingState.Waiting;
    public float WorldRadius => radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static AudioClip s_chime;
    static readonly int[] Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };   // пентатоника: каждое кольцо звенит выше

    Transform _visual;
    Material _material;
    Light _light;
    AudioSource _audio;
    float _stateTime;
    bool _built;

    void Awake() => Build();

    public void SetState(RingState state)
    {
        Build();
        State = state;
        _stateTime = 0f;
        _visual.gameObject.SetActive(true);
        _visual.localScale = Vector3.one;
        _light.enabled = state == RingState.Next;
        Apply();
    }

    /// <summary>Пересёк ли отрезок from→to плоскость кольца внутри обода.</summary>
    public bool Crossed(Vector3 from, Vector3 to)
    {
        Vector3 n = transform.forward;
        Vector3 c = transform.position;
        float a = Vector3.Dot(from - c, n);
        float b = Vector3.Dot(to - c, n);
        if ((a > 0f) == (b > 0f)) return false;

        Vector3 hit = from + (to - from) * (a / (a - b));
        float limit = WorldRadius + 0.25f;
        return (hit - c).sqrMagnitude <= limit * limit;
    }

    /// <summary>Звон при пролёте. order — номер кольца, чем дальше, тем выше нота.</summary>
    public void PlayChime(int order)
    {
        Build();
        _audio.pitch = Mathf.Pow(2f, Scale[Mathf.Clamp(order, 0, Scale.Length - 1)] / 12f);
        _audio.PlayOneShot(Chime(), 0.8f);
    }

    void Update()
    {
        if (!_built) return;
        _stateTime += Time.deltaTime;
        Apply();
    }

    void Apply()
    {
        Color color;
        switch (State)
        {
            case RingState.Next:
            {
                float pulse = 0.8f + 0.3f * Mathf.Sin(Time.time * 4f);
                color = nextColor * pulse;
                _visual.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.time * 4f));
                _light.intensity = 4f * pulse;
                break;
            }
            case RingState.Passed:
            {
                float t = Mathf.Clamp01(_stateTime / 0.6f);
                color = Color.Lerp(passedColor, Color.black, t * t);
                _visual.localScale = Vector3.one * (1f + 0.7f * t);
                if (t >= 1f) _visual.gameObject.SetActive(false);
                break;
            }
            default:
                color = waitingColor;
                break;
        }
        _material.SetColor(BaseColorId, color);
    }

    void Build()
    {
        if (_built) return;
        _built = true;

        var go = new GameObject("Ring Visual", typeof(MeshFilter), typeof(MeshRenderer));
        _visual = go.transform;
        _visual.SetParent(transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = BuildTorus(radius, thickness, 72, 12);
        _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "RingGlow" };
        _material.SetFloat("_Cull", 0f);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var lightGo = new GameObject("Ring Light");
        lightGo.transform.SetParent(transform, false);
        _light = lightGo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.range = radius * 5f;
        _light.color = new Color(1f, 0.78f, 0.4f);
        _light.shadows = LightShadows.None;
        _light.enabled = false;

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0.6f;
        _audio.minDistance = 6f;
        _audio.maxDistance = 300f;

        Apply();
    }

    static Mesh BuildTorus(float majorRadius, float minorRadius, int segments, int sides)
    {
        var vertices = new Vector3[(segments + 1) * (sides + 1)];
        var triangles = new int[segments * sides * 6];
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
            for (int j = 0; j <= sides; j++)
            {
                float v = j / (float)sides * Mathf.PI * 2f;
                Vector3 normal = radial * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                vertices[i * (sides + 1) + j] = radial * majorRadius + normal * minorRadius;
            }
        }
        int k = 0;
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = i * (sides + 1) + j;
            int b = (i + 1) * (sides + 1) + j;
            triangles[k++] = a; triangles[k++] = b; triangles[k++] = a + 1;
            triangles[k++] = a + 1; triangles[k++] = b; triangles[k++] = b + 1;
        }
        var mesh = new Mesh { name = "Ring" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Колокольчик, синтезированный в коде, — отдельный аудиофайл не нужен.</summary>
    static AudioClip Chime()
    {
        if (s_chime != null) return s_chime;
        const int rate = 44100;
        int count = (int)(rate * 1.4f);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float envelope = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-3.5f * t);
            float s = 0.55f * Mathf.Sin(2f * Mathf.PI * 880f * t)
                    + 0.25f * Mathf.Sin(2f * Mathf.PI * 1763f * t)
                    + 0.12f * Mathf.Sin(2f * Mathf.PI * 2640f * t)
                    + 0.08f * Mathf.Sin(2f * Mathf.PI * 3520f * t) * Mathf.Exp(-6f * t);
            data[i] = envelope * s * 0.8f;
        }
        s_chime = AudioClip.Create("RingChime", count, 1, rate, false);
        s_chime.SetData(data, 0);
        return s_chime;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f);
        Vector3 previous = transform.TransformPoint(new Vector3(radius, 0f, 0f));
        for (int i = 1; i <= 48; i++)
        {
            float a = i / 48f * Mathf.PI * 2f;
            Vector3 p = transform.TransformPoint(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            Gizmos.DrawLine(previous, p);
            previous = p;
        }
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * radius);
    }
}
