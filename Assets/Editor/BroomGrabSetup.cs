#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Тестовая сцена «взять метлу руками» под XR Interaction Simulator:
///   * XR Origin снимается с метлы и ставится на землю перед ней (иначе, взяв метлу, игрок тащил бы сам себя);
///   * полёт выключен (BroomController отключён), метла просто парит перед игроком;
///   * на метлу добавляются коллайдер по форме модели и XR Grab Interactable:
///     её можно взять одной рукой или двумя сразу (вторая рука задаёт наклон древка),
///     хватается в том месте, где рука коснулась, после отпускания остаётся висеть в воздухе;
///   * XR Interaction Simulator ставится прямо в сцену, автоматическое создание симулятора выключается.
///
/// Срабатывает один раз автоматически. Повторить — меню Broom → Setup Grab Test.
/// </summary>
[InitializeOnLoad]
public static class BroomGrabSetup
{
    const string RigName = "XR Origin (XR Rig)";
    const string SimulatorSettingsPath = "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";

    // Где стоит игрок относительно метлы и на какой высоте висит метла
    const float PlayerDistance = 0.6f;
    const float BroomHeight = 1.0f;

    static string DoneKey => "BroomGrabSetupDone_" + Application.dataPath;

    const string SimulatorPrefabPath =
        "Assets/Samples/XR Interaction Toolkit/3.6.1/XR Interaction Simulator/XR Interaction Simulator.prefab";
    static string SimFixKey => "BroomSimulatorFix_v1_" + Application.dataPath;

    static BroomGrabSetup()
    {
        EditorApplication.delayCall += TryAuto;
        EditorApplication.delayCall += TryFixSimulator;
    }

    // Первая версия по ошибке поставила в сцену только UI симулятора (без самого симулятора) — чиним один раз
    static void TryFixSimulator()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(SimFixKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (!EditorPrefs.GetBool(DoneKey, false)) return;      // полный Setup и так поставит правильный
        PlaceSimulator();
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(SimFixKey, true);
        Debug.Log("[Broom Grab] XR Interaction Simulator исправлен.");
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (FindRig() == null) return;     // сначала должен отработать BroomVRSetup
        Setup();
    }

    [MenuItem("Broom/Setup Grab Test")]
    public static void Setup()
    {
        var broom = GameObject.Find("Broom");
        var rig = FindRig();
        if (broom == null || rig == null)
        {
            Debug.LogWarning("[Broom Grab] Не найден Broom или " + RigName + " — сначала Broom → Setup VR Rig.");
            return;
        }

        // 1. Полёт пока выключаем
        var flight = broom.GetComponent<BroomController>();
        if (flight != null)
        {
            Undo.RecordObject(flight, "Disable flight");
            flight.enabled = false;
        }

        // 2. Риг — отдельно, на земле
        Undo.SetTransformParent(rig.transform, null, "Unparent XR Origin");
        PrefabUtility.RecordPrefabInstancePropertyModifications(rig.transform);

        // 3. Метла: поворачиваем древком поперёк взгляда, вешаем перед игроком
        Bounds local = LocalBounds(broom);
        int axis = LongestAxis(local.size);
        Vector3 axisVector = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        Vector3 broomCenter = broom.transform.TransformPoint(local.center);
        Vector3 ground = new Vector3(broomCenter.x, GroundHeight(broomCenter), broomCenter.z);

        Undo.RecordObject(broom.transform, "Place broom");
        broom.transform.rotation = Quaternion.FromToRotation(axisVector, Vector3.right);
        Vector3 targetCenter = ground + Vector3.up * BroomHeight;
        broom.transform.position += targetCenter - broom.transform.TransformPoint(local.center);

        rig.transform.SetPositionAndRotation(
            new Vector3(targetCenter.x, ground.y, targetCenter.z - PlayerDistance),
            Quaternion.identity);                                          // смотрит на +Z, на метлу
        PrefabUtility.RecordPrefabInstancePropertyModifications(rig.transform);

        // 4. Коллайдер по форме модели
        var capsule = broom.GetComponent<CapsuleCollider>();
        if (capsule == null) capsule = Undo.AddComponent<CapsuleCollider>(broom);
        capsule.direction = axis;
        capsule.center = local.center;
        capsule.height = local.size[axis];
        float thickness = Mathf.Min(local.size[(axis + 1) % 3], local.size[(axis + 2) % 3]);
        capsule.radius = Mathf.Clamp(thickness * 0.5f, 0.03f, 0.12f);

        // 5. Rigidbody: висит в воздухе, физика не роняет
        var body = broom.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // 6. Взаимодействие: одной или двумя руками
        var grab = broom.GetComponent<XRGrabInteractable>();
        if (grab == null) grab = Undo.AddComponent<XRGrabInteractable>(broom);
        grab.selectMode = InteractableSelectMode.Multiple;      // две руки одновременно
        grab.useDynamicAttach = true;                           // берётся там, где коснулась рука
        grab.matchAttachPosition = true;
        grab.matchAttachRotation = true;
        grab.snapToColliderVolume = true;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = false;                             // отпустил — метла осталась висеть
        grab.trackScale = false;
        grab.addDefaultGrabTransformers = true;                 // XR General Grab Transformer: 1–2 руки
        EditorUtility.SetDirty(grab);

        // 7. Симулятор прямо в сцене
        PlaceSimulator();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log($"[Broom Grab] Метлу можно брать. Размер модели: {local.size}, коллайдер по оси {"XYZ"[axis]}.");
    }

    static GameObject FindRig()
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name == RigName && t.gameObject.scene.IsValid())
            .Select(t => t.gameObject)
            .FirstOrDefault();
    }

    static void PlaceSimulator()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[Broom Grab] Не найден " + SimulatorPrefabPath);
            return;
        }

        // Убираем ошибочно поставленный отдельный UI симулятора
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "XR Interaction Simulator UI")
                Undo.DestroyObjectImmediate(root);
        }

        if (GameObject.Find(prefab.name) == null)
        {
            var sim = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(sim, "Add XR Interaction Simulator");
        }

        // Раз симулятор лежит в сцене, автоматическое создание второго не нужно
        var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SimulatorSettingsPath);
        if (settings != null)
        {
            var so = new SerializedObject(settings);
            so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = false;
            so.FindProperty("m_SimulatorPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
        }
    }

    static Bounds LocalBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var r in renderers)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = wb.center + Vector3.Scale(wb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.transform.InverseTransformPoint(corner);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
        }
        if (!has) b = new Bounds(Vector3.zero, new Vector3(0.1f, 0.1f, 1.5f));
        return b;
    }

    static int LongestAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z) return 0;
        return size.y >= size.z ? 1 : 2;
    }

    static float GroundHeight(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        return 0f;
    }
}
#endif
