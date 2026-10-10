using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Скошенный прямоугольник (параллелограмм) для UI — фон и декоративные полосы в стиле Persona.
/// Верхняя грань сдвинута относительно нижней на <see cref="skew"/> пикселей.
/// revealStart / revealEnd (0..1) задают, какая часть ширины залита — так делается «шторка».
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class SlantedQuad : MaskableGraphic
{
    [SerializeField] private float skew = 160f;
    [SerializeField, Range(0f, 1f)] private float revealStart = 0f;
    [SerializeField, Range(0f, 1f)] private float revealEnd = 1f;

    public float Skew
    {
        get => skew;
        set { skew = value; SetVerticesDirty(); }
    }

    public void SetReveal(float start, float end)
    {
        start = Mathf.Clamp01(start);
        end = Mathf.Clamp01(end);
        if (Mathf.Approximately(start, revealStart) && Mathf.Approximately(end, revealEnd)) return;
        revealStart = start;
        revealEnd = end;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (revealEnd <= revealStart) return;

        Rect r = GetPixelAdjustedRect();
        // Диапазон, в котором ходит нижняя грань, чтобы при полном раскрытии прямоугольник был закрыт целиком
        float lo = r.xMin - Mathf.Max(skew, 0f);
        float hi = r.xMax - Mathf.Min(skew, 0f);
        float left = Mathf.Lerp(lo, hi, revealStart);
        float right = Mathf.Lerp(lo, hi, revealEnd);

        Color32 c = color;
        vh.AddVert(new Vector3(left, r.yMin), c, new Vector2(0f, 0f));
        vh.AddVert(new Vector3(left + skew, r.yMax), c, new Vector2(0f, 1f));
        vh.AddVert(new Vector3(right + skew, r.yMax), c, new Vector2(1f, 1f));
        vh.AddVert(new Vector3(right, r.yMin), c, new Vector2(1f, 0f));
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
