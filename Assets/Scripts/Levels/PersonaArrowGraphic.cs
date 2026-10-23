using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Угловатая «рубленая» стрелка в духе Persona 5 для UI. Смотрит вверх (+Y), вписана в RectTransform.
/// Обводку и тень делают несколько копий друг под другом с разным inflate и цветом.
/// </summary>
public class PersonaArrowGraphic : MaskableGraphic
{
    [Tooltip("Насколько раздуть контур наружу, пиксели (для обводки)")]
    [SerializeField] private float inflate = 0f;

    public float Inflate
    {
        get => inflate;
        set { inflate = value; SetVerticesDirty(); }
    }

    // Контур в долях полуразмеров прямоугольника: чуть неровный, как вырезанный ножницами
    static readonly Vector2[] Shape =
    {
        new Vector2(0.04f, 1.00f),     // остриё
        new Vector2(-0.92f, 0.08f),    // левое плечо
        new Vector2(-0.38f, 0.16f),    // левая выемка
        new Vector2(-0.50f, -1.00f),   // низ древка слева
        new Vector2(0.40f, -0.86f),    // низ древка справа
        new Vector2(0.34f, 0.20f),     // правая выемка
        new Vector2(0.98f, 0.02f),     // правое плечо
    };
    static readonly Vector2 Center = new Vector2(0f, 0.12f);   // от этой точки раздуваем контур для обводки
    // Наконечник — веером от острия, древко — четырёхугольником
    static readonly int[] Triangles = { 0, 1, 2, 0, 2, 5, 0, 5, 6, 2, 3, 4, 2, 4, 5 };

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Vector2 half = r.size * 0.5f;
        Vector2 origin = r.center;
        Vector2 c = origin + Vector2.Scale(Center, half);

        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        for (int i = 0; i < Shape.Length; i++)
        {
            Vector2 p = origin + Vector2.Scale(Shape[i], half);
            Vector2 outward = (p - c).normalized;
            vertex.position = p + outward * inflate;
            vh.AddVert(vertex);
        }
        for (int i = 0; i < Triangles.Length; i += 3)
            vh.AddTriangle(Triangles[i], Triangles[i + 1], Triangles[i + 2]);
    }
}
