#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ставит на сцену систему уровней и прокладывает маршрут первого уровня (обучение) — 10 колец над травой.
/// Маршрут строится по рельефу: выбирается самое ровное направление от места старта,
/// кольца держатся на заданной высоте над землёй и не врезаются в холмы между собой.
///
/// Срабатывает один раз автоматически. Повторить или перестроить маршрут — меню Broom → Setup Levels.
/// Кольца потом можно двигать руками: порядок пролёта = порядок в иерархии.
/// </summary>
[InitializeOnLoad]
public static class LevelSetup
{
    const string RootName = "Levels";
    const string TutorialName = "Level 1 · Обучение";

    // Маршрут: поворот курса перед кольцом (град), высота кольца над землёй (м), подсказка
    static readonly float[] Turns = { 0f, 0f, 18f, 22f, 20f, 0f, -25f, -30f, -28f, -10f };
    static readonly float[] Heights = { 4f, 5f, 7f, 11f, 15f, 18f, 14f, 10f, 7f, 5f };
    static readonly string[] Hints =
    {
        "Правый курок — газ. Лети к светящемуся кольцу",
        "Метла летит туда, куда ты смотришь",
        "Чтобы повернуть, просто поверни голову",
        "Кольца поднимаются: посмотри вверх или толкни правый стик вверх",
        "Правый стик вверх и вниз поднимает и опускает метлу, не меняя курса",
        "Правый стик влево или вправо — быстрый поворот на 45°",
        "Теперь вниз: посмотри вниз или опусти правый стик",
        "Левый курок — тормоз. Попробуй зависнуть перед кольцом",
        "Отпусти газ — метла сама плавно сбавит ход",
        "Последнее кольцо!",
    };
    const float FirstDistance = 20f;
    const float Step = 38f;
    const float EdgeMargin = 40f;

    static string DoneKey => "LevelSetupDone_" + Application.dataPath;

    static LevelSetup()
    {
        EditorApplication.delayCall += TryAuto;
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (GameObject.Find("Broom") == null || Terrain.activeTerrain == null) return;

        // Маршрут уже есть в сцене — не перестраиваем
        var existing = GameObject.Find(RootName);
        if (existing != null && existing.GetComponentInChildren<FlightRing>(true) != null)
        {
            EditorPrefs.SetBool(DoneKey, true);
            return;
        }
        Setup();
    }

    [MenuItem("Broom/Setup Levels")]
    public static void Setup()
    {
        var terrain = Terrain.activeTerrain;
        var broom = GameObject.Find("Broom");
        var origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (terrain == null || broom == null)
        {
            Debug.LogWarning("[Levels] Нужны террейн и метла (Broom) на сцене.");
            return;
        }

        // 1. Корень с менеджером уровней
        var root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Levels");
        }
        var manager = root.GetComponent<LevelManager>();
        if (manager == null) manager = Undo.AddComponent<LevelManager>(root);
        var so = new SerializedObject(manager);
        so.FindProperty("broom").objectReferenceValue = broom.GetComponent<BroomController>();
        if (origin != null && origin.Camera != null)
            so.FindProperty("head").objectReferenceValue = origin.Camera.transform;
        so.ApplyModifiedProperties();

        // 2. Первый уровень
        var tutorial = root.transform.Find(TutorialName);
        if (tutorial == null)
        {
            var go = new GameObject(TutorialName);
            Undo.RegisterCreatedObjectUndo(go, "Create tutorial level");
            go.transform.SetParent(root.transform, false);
            tutorial = go.transform;
        }
        var level = tutorial.GetComponent<Level>();
        if (level == null) level = Undo.AddComponent<Level>(tutorial.gameObject);
        tutorial.SetSiblingIndex(0);

        // 3. Маршрут колец
        for (int i = tutorial.childCount - 1; i >= 0; i--)
        {
            var child = tutorial.GetChild(i);
            if (child.GetComponent<FlightRing>() != null) Undo.DestroyObjectImmediate(child.gameObject);
        }

        Vector3 start = origin != null ? origin.transform.position : broom.transform.position;
        Vector3 facing = broom.transform.position - start;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
        facing.Normalize();

        Vector3 heading = BestHeading(terrain, start, facing);
        var points = BuildRoute(terrain, start, heading);

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 previous = i == 0 ? start + Vector3.up * 2f : points[i - 1];
            Vector3 next = i == points.Count - 1 ? points[i] + (points[i] - previous) : points[i + 1];
            Vector3 tangent = (next - previous).normalized;

            var ringGo = new GameObject($"Ring {i + 1:00}");
            Undo.RegisterCreatedObjectUndo(ringGo, "Create ring");
            ringGo.transform.SetParent(tutorial, false);
            ringGo.transform.SetPositionAndRotation(points[i], Quaternion.LookRotation(tangent, Vector3.up));

            var ring = ringGo.AddComponent<FlightRing>();
            var rso = new SerializedObject(ring);
            rso.FindProperty("radius").floatValue = i < 2 ? 3.6f : 3f;
            rso.FindProperty("hint").stringValue = i < Hints.Length ? Hints[i] : "";
            rso.ApplyModifiedPropertiesWithoutUndo();
        }

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log($"[Levels] Уровень «Обучение»: {points.Count} колец, маршрут ≈{FirstDistance + Step * (points.Count - 1):0} м.");
    }

    /// <summary>Из 8 направлений берём то, где рельеф ровнее и маршрут не выходит за край карты.
    /// Направление «лицом к метле» получает фору, чтобы первое кольцо было перед игроком.</summary>
    static Vector3 BestHeading(Terrain terrain, Vector3 start, Vector3 facing)
    {
        Vector3 best = facing;
        float bestScore = float.MaxValue;
        for (int k = 0; k < 8; k++)
        {
            Vector3 heading = Quaternion.AngleAxis(k * 45f, Vector3.up) * facing;
            float score = Mathf.Min(k, 8 - k) * 15f;
            Vector3 pos = start;
            Vector3 dir = heading;
            float lastGround = Ground(terrain, start);
            for (int i = 0; i < Turns.Length; i++)
            {
                dir = Quaternion.AngleAxis(Turns[i], Vector3.up) * dir;
                pos += dir * (i == 0 ? FirstDistance : Step);
                if (!Inside(terrain, pos)) score += 10000f;
                float ground = Ground(terrain, pos);
                score += Mathf.Abs(ground - lastGround);
                lastGround = ground;
            }
            if (score < bestScore)
            {
                bestScore = score;
                best = heading;
            }
        }
        return best;
    }

    static List<Vector3> BuildRoute(Terrain terrain, Vector3 start, Vector3 heading)
    {
        var points = new List<Vector3>();
        Vector3 pos = start;
        Vector3 dir = heading;
        Vector3 previous = start + Vector3.up * 2f;
        for (int i = 0; i < Turns.Length; i++)
        {
            dir = Quaternion.AngleAxis(Turns[i], Vector3.up) * dir;
            pos += dir * (i == 0 ? FirstDistance : Step);

            // Не ниже заданной высоты над землёй и на 4 м выше любого холма по пути от предыдущего кольца
            float y = Ground(terrain, pos) + Heights[i];
            for (int s = 1; s <= 12; s++)
            {
                Vector3 p = Vector3.Lerp(previous, pos, s / 12f);
                y = Mathf.Max(y, Ground(terrain, p) + 4f);
            }
            var point = new Vector3(pos.x, y, pos.z);
            points.Add(point);
            previous = point;
        }
        return points;
    }

    static float Ground(Terrain terrain, Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;

    static bool Inside(Terrain terrain, Vector3 p)
    {
        Vector3 min = terrain.transform.position;
        Vector3 max = min + terrain.terrainData.size;
        return p.x > min.x + EdgeMargin && p.x < max.x - EdgeMargin && p.z > min.z + EdgeMargin && p.z < max.z - EdgeMargin;
    }
}
#endif
