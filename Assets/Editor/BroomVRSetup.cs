#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Переносит VR-настройку из прошлого проекта (kursach) на метлу:
///   * XR Origin (XR Rig) из XRI Starter Assets становится дочерним объектом Broom вместо обычной камеры;
///   * вся локомоция рига выключена (телепорт, ходьба, поворот стиком, прыжок, гравитация) — двигает метла;
///   * на контроллеры ставятся руки «Oculus Hands» вместо стандартной модели контроллера,
///     пальцы анимируются от курка и грипа (как было в kursach);
///   * BroomController получает голову и руки, газ в режиме VRHands — правый курок;
///   * меню паузы переключается в режим «перед глазами» (World Space);
///   * в редакторе включается XR Interaction Simulator, чтобы проверять без шлема (на Mac).
///
/// Один раз срабатывает автоматически, когда всё нужное импортировано. Повторно — меню Broom → Setup VR Rig.
/// </summary>
[InitializeOnLoad]
public static class BroomVRSetup
{
    const string SampleRoot = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/";
    const string RigPath = SampleRoot + "Prefabs/XR Origin (XR Rig).prefab";
    const string ActionsPath = SampleRoot + "XRI Default Input Actions.inputactions";
    const string LeftHandPath = "Assets/Oculus Hands/Prefabs/Left Hand Model.prefab";
    const string RightHandPath = "Assets/Oculus Hands/Prefabs/Right Hand Model.prefab";
    const string SimulatorSettingsPath = "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";
    const string XRGeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
    const string OpenXRSettingsPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";
    const string RigName = "XR Origin (XR Rig)";

    // Где сидит игрок относительно древка: ноль рига (пол капсулы) чуть ниже и позади метлы
    static readonly Vector3 RigLocalPosition = new Vector3(0f, -0.8f, -0.25f);

    static string DoneKey => "BroomVRSetupDone_" + Application.dataPath;

    static BroomVRSetup()
    {
        EditorApplication.delayCall += TryAuto;
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RigPath) == null) return;      // сэмпл ещё не импортирован
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LeftHandPath) == null) return;
        Setup();
    }

    [MenuItem("Broom/Setup VR Rig")]
    public static void Setup()
    {
        RegisterXRSettings();

        var broom = GameObject.Find("Broom");
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        if (broom == null || rigPrefab == null)
        {
            Debug.LogWarning("[Broom VR] Не найден объект Broom или " + RigPath);
            return;
        }

        // 1. Риг на метлу
        var oldRig = broom.transform.Find(RigName);
        if (oldRig != null) Object.DestroyImmediate(oldRig.gameObject);

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, broom.transform);
        rig.name = RigName;
        rig.transform.localPosition = RigLocalPosition;
        rig.transform.localRotation = Quaternion.identity;
        Record(rig.transform);
        Undo.RegisterCreatedObjectUndo(rig, "Setup VR Rig");

        // 2. Локомоция не нужна — летаем метлой
        var locomotion = FindDeep(rig.transform, "Locomotion");
        if (locomotion != null)
        {
            locomotion.gameObject.SetActive(false);
            Record(locomotion.gameObject);
        }

        // 3. Старая камера метлы выключается, главной становится камера рига
        var oldCamera = broom.transform.Find("Main Camera");
        if (oldCamera != null)
        {
            oldCamera.gameObject.tag = "Untagged";
            oldCamera.gameObject.SetActive(false);
        }

        var rigCameraTransform = FindDeep(rig.transform, "Main Camera");
        var rigCamera = rigCameraTransform != null ? rigCameraTransform.GetComponent<Camera>() : null;
        if (rigCamera != null)
        {
            rigCamera.farClipPlane = 2000f;                                   // террейн 1 км + горы
            var cameraData = rigCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;                           // Bloom для луны и звёзд
            Record(rigCamera);
            Record(cameraData);
        }

        // 4. Руки вместо модели контроллера
        var refs = AssetDatabase.LoadAllAssetsAtPath(ActionsPath).OfType<InputActionReference>().ToArray();
        InputActionReference Ref(string path) => refs.FirstOrDefault(r => r.name == path);

        SetupHand(rig.transform, "Left Controller Visual", LeftHandPath,
            new Vector3(-0.033f, -0.012f, 0.007f), new Quaternion(-0.5f, -0.8660254f, 0f, 0f), Vector3.one,
            Ref("XRI Left Interaction/Activate Value"), Ref("XRI Left Interaction/Select Value"));
        SetupHand(rig.transform, "Right Controller Visual", RightHandPath,
            new Vector3(-0.031f, -0.008f, 0.007f), new Quaternion(0.5075384f, 0.8616291f, 0f, 0f), new Vector3(-1f, 1f, 1f),
            Ref("XRI Right Interaction/Activate Value"), Ref("XRI Right Interaction/Select Value"));

        // 5. BroomController: голова, руки, газ
        var controller = broom.GetComponent<BroomController>();
        if (controller != null)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("head").objectReferenceValue = rigCameraTransform;
            so.FindProperty("leftHand").objectReferenceValue = FindDeep(rig.transform, "Left Controller");
            so.FindProperty("rightHand").objectReferenceValue = FindDeep(rig.transform, "Right Controller");
            so.FindProperty("throttleAction.m_UseReference").boolValue = true;
            so.FindProperty("throttleAction.m_Reference").objectReferenceValue = Ref("XRI Right Interaction/Activate Value");
            so.ApplyModifiedProperties();
        }

        // 6. Меню паузы — в мир перед глазами, иначе в шлеме его не видно
        var menu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
        if (menu != null)
        {
            var so = new SerializedObject(menu);
            so.FindProperty("worldSpace").boolValue = true;
            so.ApplyModifiedProperties();
        }

        // 7. Симулятор шлема в редакторе
        EnableSimulator();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("[Broom VR] XR Origin установлен на метлу.");
    }

    static void SetupHand(Transform rig, string visualName, string prefabPath, Vector3 pos, Quaternion rot, Vector3 scale,
                          InputActionReference trigger, InputActionReference grip)
    {
        var visual = FindDeep(rig, visualName);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (visual == null || prefab == null)
        {
            Debug.LogWarning("[Broom VR] Не удалось поставить руку на " + visualName);
            return;
        }

        var controllerModel = FindDeep(visual, "UniversalController");
        if (controllerModel != null)
        {
            controllerModel.gameObject.SetActive(false);
            Record(controllerModel.gameObject);
        }

        var hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, visual);
        hand.transform.localPosition = pos;
        hand.transform.localRotation = rot;
        hand.transform.localScale = scale;
        Record(hand.transform);

        var animator = hand.GetComponentInChildren<Animator>(true);
        var handAnimator = hand.AddComponent<HandAnimator>();
        handAnimator.Configure(trigger, grip, animator);
        EditorUtility.SetDirty(handAnimator);
    }

    static void RegisterXRSettings()
    {
        var general = AssetDatabase.LoadAssetAtPath<ScriptableObject>(XRGeneralSettingsPath);
        if (general != null) EditorBuildSettings.AddConfigObject("com.unity.xr.management.loader_settings", general, true);
        var openxr = AssetDatabase.LoadAssetAtPath<ScriptableObject>(OpenXRSettingsPath);
        if (openxr != null) EditorBuildSettings.AddConfigObject("com.unity.xr.openxr.settings4", openxr, true);
    }

    static void EnableSimulator()
    {
        var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SimulatorSettingsPath);
        var simulator = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Samples/XR Interaction Toolkit/3.6.1/XR Interaction Simulator/XR Interaction Simulator.prefab");
        if (settings == null || simulator == null) return;
        var so = new SerializedObject(settings);
        so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = true;
        so.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue = true;
        so.FindProperty("m_UseClassic").boolValue = false;
        so.FindProperty("m_SimulatorPrefab").objectReferenceValue = simulator;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
    }

    // Изменения в экземпляре префаба из кода нужно явно записать как override, иначе они потеряются
    static void Record(Object target)
    {
        if (target != null && PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
