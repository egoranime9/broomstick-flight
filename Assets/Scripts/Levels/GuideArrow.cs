using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Золотая стрелка чуть ниже взгляда: показывает на следующее кольцо (или на метлу, если ты пешком).
/// Появляется, только когда цель вне поля зрения. Создаётся из кода (LevelManager).
/// </summary>
public class GuideArrow : MonoBehaviour
{
    [ColorUsage(false, true)] [SerializeField] private Color color = new Color(3f, 2.1f, 0.6f);
    [Tooltip("Цель ближе этого угла от взгляда — стрелка прячется, градусов")]
    [SerializeField] private float hideAngle = 25f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    Transform _head;
    Transform _visual;
    Material _material;
    Vector3? _target;
    Quaternion _rotation;
    bool _rotationReady;
    float _show;

    public static GuideArrow Create(Transform parent, Transform head)
    {
        var go = new GameObject("Guide Arrow");
        go.transform.SetParent(parent, false);
        var arrow = go.AddComponent<GuideArrow>();
        arrow._head = head;
        arrow.Build();
        return arrow;
    }

    public void SetTarget(Vector3? target) => _target = target;

    void Build()
    {
        var visual = new GameObject("Arrow Visual", typeof(MeshFilter), typeof(MeshRenderer));
        _visual = visual.transform;
        _visual.SetParent(transform, false);
        visual.GetComponent<MeshFilter>().sharedMesh = BuildArrow();
        _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "GuideArrow" };
        _material.SetFloat("_Cull", 0f);
        var renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        visual.SetActive(false);
    }

    void LateUpdate()
    {
        if (_head == null) return;
        float dt = Time.unscaledDeltaTime;

        Vector3 forward = _head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = _head.up;
        forward.Normalize();
        Vector3 basePosition = _head.position + forward * 0.9f + Vector3.down * 0.38f;

        bool want = _target.HasValue
                    && Vector3.Angle(_head.forward, _target.Value - _head.position) > hideAngle;
        _show = Mathf.MoveTowards(_show, want ? 1f : 0f, dt * 4f);
        _visual.gameObject.SetActive(_show > 0.01f);

        transform.position = basePosition;
        if (_target.HasValue)
        {
            Vector3 direction = _target.Value - basePosition;
            if (direction.sqrMagnitude > 1e-4f)
            {
                Quaternion goal = Quaternion.LookRotation(direction, Vector3.up);
                _rotation = _rotationReady ? Quaternion.Slerp(_rotation, goal, 1f - Mathf.Exp(-8f * dt)) : goal;
                _rotationReady = true;
            }
        }
        transform.rotation = _rotation;
        transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, _show);

        float pulse = 0.8f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
        _material.SetColor(BaseColorId, color * pulse);
    }

    /// <summary>Стрелка вдоль +Z: короткое древко и наконечник-конус.</summary>
    static Mesh BuildArrow()
    {
        const int sides = 16;
        const float shaftRadius = 0.012f, shaftBack = -0.09f, shaftFront = 0.0f;
        const float headRadius = 0.045f, headBack = 0.0f, tip = 0.1f;

        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();

        // Древко — трубка
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * shaftRadius;
            vertices.Add(new Vector3(c.x, c.y, shaftBack));
            vertices.Add(new Vector3(c.x, c.y, shaftFront));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2, b = ((i + 1) % sides) * 2;
            triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }

        // Наконечник — конус с донышком
        int ring = vertices.Count;
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            vertices.Add(new Vector3(Mathf.Cos(a) * headRadius, Mathf.Sin(a) * headRadius, headBack));
        }
        int apex = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, tip));
        int center = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, headBack));
        for (int i = 0; i < sides; i++)
        {
            int a = ring + i, b = ring + (i + 1) % sides;
            triangles.AddRange(new[] { a, b, apex });
            triangles.AddRange(new[] { b, a, center });
        }

        var mesh = new Mesh { name = "GuideArrow" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
