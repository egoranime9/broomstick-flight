#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Делает отдельную сцену второго уровня — Assets/Scenes/Level2.unity.
/// Сцена — копия основной (та же карта, метла, риг, небо, меню-шляпа), но вместо колец обучения
/// стоят два набора высоких колец на шестах, по три, друг напротив друга (как ворота для квиддича).
/// Среднее кольцо в наборе выше и выдвинуто вперёд, к центру поля, два других — по краям.
/// Пролететь нужно через все шесть, по очереди, челноком между наборами; куда лететь — показывает
/// указатель в стиле Persona.
///
/// Срабатывает один раз автоматически (если сцены ещё нет). Перестроить кольца — меню Broom → Build Level 2 Scene.
/// </summary>
[InitializeOnLoad]
public static class Level2SceneBuilder
{
    const string SourceScene = "Assets/Scenes/SampleScene.unity";
    const string ScenePath = "Assets/Scenes/Level2.unity";
    const string LevelName = "Level 2 · Кольца на шестах";

    const float NearSetDistance = 25f;   // от игрока до ближнего набора колец, м
    const float FieldLength = 80f;       // между наборами, м
    const float HoopSpacing = 9f;        // между кольцами в наборе, м
    const float CenterForward = 5f;      // насколько среднее кольцо выдвинуто к центру поля, м
    const float SideHeight = 11f;        // высота центра крайних колец над землёй, м
    const float CenterHeight = 14f;      // высота центра среднего кольца, м
    const float HoopRadius = 2.2f;

    // Порядок пролёта: челноком между наборами (набор, место: -1 левое, 0 среднее, 1 правое)
    static readonly (int set, int slot)[] Order = { (0, 0), (1, -1), (0, 1), (1, 0), (0, -1), (1, 1) };
    static readonly string[] Hints =
    {
        "Пролети через кольцо, на которое показывает стрелка",
        "Развернись: следующее кольцо на другой стороне поля",
        "",
        "Правый стик вбок — быстрый разворот",
        "",
        "Последнее кольцо!",
    };

    static LevelManager.GuideStyle PersonaStyle => LevelManager.GuideStyle.Persona;

    static Level2SceneBuilder()
    {
        EditorApplication.delayCall += TryAuto;
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(ScenePath)) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null) return;
        Build();
    }

    [MenuItem("Broom/Build Level 2 Scene")]
    public static void Build()
    {
        if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(SourceScene, ScenePath))
        {
            Debug.LogWarning("[Level 2] Не получилось скопировать " + SourceScene);
            return;
        }

        // Открываем рядом с текущей сценой, правим и закрываем — текущая сцена не трогается
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }

        try
        {
            if (Populate(scene)) EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }

        AddToBuildSettings();
    }

    static bool Populate(Scene scene)
    {
        var broom = FindInScene(scene, "Broom");
        var origin = FindComponent<XROrigin>(scene);
        var terrain = FindComponent<Terrain>(scene);
        if (broom == null || terrain == null)
        {
            Debug.LogWarning("[Level 2] В сцене нет метлы или террейна.");
            return false;
        }

        // 1. Менеджер уровней: старые уровни убираем, указатель — в стиле Persona
        var root = FindInScene(scene, "Levels");
        if (root == null)
        {
            root = new GameObject("Levels");
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        var manager = root.GetComponent<LevelManager>();
        if (manager == null) manager = root.AddComponent<LevelManager>();
        foreach (var old in root.GetComponentsInChildren<Level>(true))
            Object.DestroyImmediate(old.gameObject);

        var mso = new SerializedObject(manager);
        mso.FindProperty("guide").enumValueIndex = (int)PersonaStyle;
        mso.FindProperty("startLevel").intValue = 0;
        mso.FindProperty("broom").objectReferenceValue = broom.GetComponent<BroomController>();
        if (origin != null && origin.Camera != null)
            mso.FindProperty("head").objectReferenceValue = origin.Camera.transform;
        mso.ApplyModifiedPropertiesWithoutUndo();

        var levelGo = new GameObject(LevelName);
        levelGo.transform.SetParent(root.transform, false);
        var level = levelGo.AddComponent<Level>();

        // 2. Поле: ось от игрока вперёд, в самом ровном направлении
        Vector3 start = origin != null ? origin.transform.position : broom.transform.position;
        Vector3 facing = broom.transform.position - start;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
        facing.Normalize();
        Vector3 forward = BestAxis(terrain, start, facing);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        var hoops = new FlightRing[2, 3];
        string[] setNames = { "Set A (near)", "Set B (far)" };
        string[] slotNames = { "Left", "Center", "Right" };
        for (int set = 0; set < 2; set++)
        {
            var setGo = new GameObject(setNames[set]);
            setGo.transform.SetParent(levelGo.transform, false);
            Vector3 setCenter = start + forward * (NearSetDistance + set * FieldLength);
            Vector3 towardField = set == 0 ? forward : -forward;
            setGo.transform.position = new Vector3(setCenter.x, Ground(terrain, setCenter), setCenter.z);

            for (int slot = -1; slot <= 1; slot++)
            {
                Vector3 p = slot == 0 ? setCenter + towardField * CenterForward : setCenter + right * (slot * HoopSpacing);
                float ground = Ground(terrain, p);
                float height = slot == 0 ? CenterHeight : SideHeight;
                p.y = ground + height;

                var hoopGo = new GameObject($"Hoop {(set == 0 ? "A" : "B")} {slotNames[slot + 1]}");
                hoopGo.transform.SetParent(setGo.transform, false);
                hoopGo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(forward, Vector3.up));
                var ring = hoopGo.AddComponent<FlightRing>();
                var rso = new SerializedObject(ring);
                rso.FindProperty("radius").floatValue = HoopRadius;
                rso.FindProperty("thickness").floatValue = 0.22f;
                rso.FindProperty("poleLength").floatValue = Mathf.Max(0.5f, height - HoopRadius);
                rso.ApplyModifiedPropertiesWithoutUndo();
                hoops[set, slot + 1] = ring;
            }
        }

        // 3. Порядок пролёта и подсказки
        var lso = new SerializedObject(level);
        lso.FindProperty("title").stringValue = "Кольца на шестах";
        lso.FindProperty("defaultHint").stringValue = "Лети в кольцо, на которое показывает стрелка";
        var order = lso.FindProperty("order");
        order.arraySize = Order.Length;
        for (int i = 0; i < Order.Length; i++)
        {
            var ring = hoops[Order[i].set, Order[i].slot + 1];
            order.GetArrayElementAtIndex(i).objectReferenceValue = ring;
            var rso = new SerializedObject(ring);
            rso.FindProperty("hint").stringValue = Hints[i];
            rso.ApplyModifiedPropertiesWithoutUndo();
        }
        lso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Level 2] Сцена Level2: 6 колец на шестах, два набора по 3 в " + FieldLength + " м друг от друга.");
        return true;
    }

    /// <summary>Ось поля: из нескольких направлений около «лицом к метле» — где земля под шестами ровнее.</summary>
    static Vector3 BestAxis(Terrain terrain, Vector3 start, Vector3 facing)
    {
        Vector3 best = facing;
        float bestScore = float.MaxValue;
        for (int k = -3; k <= 3; k++)
        {
            Vector3 forward = Quaternion.AngleAxis(k * 20f, Vector3.up) * facing;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            var heights = new List<float>();
            float score = Mathf.Abs(k) * 4f;
            for (int set = 0; set < 2; set++)
            for (int slot = -1; slot <= 1; slot++)
            {
                Vector3 p = start + forward * (NearSetDistance + set * FieldLength) + right * (slot * HoopSpacing);
                if (!Inside(terrain, p)) score += 10000f;
                heights.Add(Ground(terrain, p));
            }
            float mean = 0f;
            foreach (var h in heights) mean += h;
            mean /= heights.Count;
            foreach (var h in heights) score += Mathf.Abs(h - mean);
            if (score < bestScore)
            {
                bestScore = score;
                best = forward;
            }
        }
        return best;
    }

    static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        }
        return null;
    }

    static T FindComponent<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    static float Ground(Terrain terrain, Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;

    static bool Inside(Terrain terrain, Vector3 p)
    {
        Vector3 min = terrain.transform.position;
        Vector3 max = min + terrain.terrainData.size;
        const float margin = 30f;
        return p.x > min.x + margin && p.x < max.x - margin && p.z > min.z + margin && p.z < max.z - margin;
    }
}
#endif
