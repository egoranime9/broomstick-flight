#if UNITY_EDITOR
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Включает полёт на метле:
///   * BroomController на метле включается и получает XR Origin и голову;
///   * на XR Origin есть CharacterController — им двигается игрок, он же не пускает сквозь землю;
///   * у террейна с травой есть включённый TerrainCollider;
///   * у метлы есть коллайдер (его ставит Broom → Setup Grab Test).
/// Срабатывает один раз автоматически. Повторить — меню Broom → Setup Flight.
/// </summary>
[InitializeOnLoad]
public static class BroomFlightSetup
{
    static string DoneKey => "BroomFlightSetupDone_" + Application.dataPath;

    static BroomFlightSetup()
    {
        EditorApplication.delayCall += TryAuto;
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (GameObject.Find("Broom") == null) return;
        Setup();
    }

    [MenuItem("Broom/Setup Flight")]
    public static void Setup()
    {
        var broom = GameObject.Find("Broom");
        if (broom == null)
        {
            Debug.LogWarning("[Broom Flight] На сцене нет объекта Broom.");
            return;
        }

        // 1. Метла: коллайдер и контроллер полёта
        if (broom.GetComponent<Collider>() == null)
            Debug.LogWarning("[Broom Flight] У метлы нет коллайдера — сначала Broom → Setup Grab Test.");

        var controller = broom.GetComponent<BroomController>();
        if (controller == null) controller = Undo.AddComponent<BroomController>(broom);
        Undo.RecordObject(controller, "Enable broom flight");
        controller.enabled = true;

        var origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        var so = new SerializedObject(controller);
        so.FindProperty("rig").objectReferenceValue = origin;
        if (origin != null && origin.Camera != null)
            so.FindProperty("head").objectReferenceValue = origin.Camera.transform;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);

        // 2. Тело игрока
        if (origin != null && origin.GetComponent<CharacterController>() == null)
            Undo.AddComponent<CharacterController>(origin.gameObject);

        // 3. Земля: коллайдер у каждого террейна
        int terrains = 0;
        foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            var tc = terrain.GetComponent<TerrainCollider>();
            if (tc == null) tc = Undo.AddComponent<TerrainCollider>(terrain.gameObject);
            Undo.RecordObject(tc, "Setup terrain collider");
            tc.enabled = true;
            tc.terrainData = terrain.terrainData;
            EditorUtility.SetDirty(tc);
            terrains++;
        }

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log($"[Broom Flight] Полёт включён. Террейнов с коллайдером: {terrains}. " +
                  "Возьми метлу обеими руками, чтобы сесть на неё.");
    }
}
#endif
