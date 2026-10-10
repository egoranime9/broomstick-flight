#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Хват метлы:
///   * при взятии метла сразу «прыгает» в руку и ложится в ладонь серединой древка (точка GripPoint);
///   * вторая рука берёт древко чуть впереди (SecondaryGripPoint), метла поворачивается по линии между руками;
///   * взять можно только рукой вплотную (компонент NearGrabOnly), дальний луч метлу не берёт.
/// Срабатывает один раз автоматически. Повторить — меню Broom → Setup Grip Points.
/// </summary>
[InitializeOnLoad]
public static class BroomGripSetup
{
    const float SecondHandOffset = 0.3f;   // на сколько метров впереди первой руки берёт вторая

    static string DoneKey => "BroomGripSetupDone_v3_" + Application.dataPath;

    static BroomGripSetup()
    {
        EditorApplication.delayCall += TryAuto;
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        var broom = GameObject.Find("Broom");
        if (broom == null || broom.GetComponent<XRGrabInteractable>() == null) return;   // сначала Setup Grab Test
        Setup();
    }

    [MenuItem("Broom/Setup Grip Points")]
    public static void Setup()
    {
        var broom = GameObject.Find("Broom");
        var grab = broom != null ? broom.GetComponent<XRGrabInteractable>() : null;
        var capsule = broom != null ? broom.GetComponent<CapsuleCollider>() : null;
        if (grab == null || capsule == null)
        {
            Debug.LogWarning("[Broom Grip] Нет XR Grab Interactable или коллайдера на Broom — сначала Broom → Setup Grab Test.");
            return;
        }

        // Ось древка берём из коллайдера, который построил Setup Grab Test
        int axis = capsule.direction;
        Vector3 axisVector = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        float handleSign = HandleDirection(broom, axis, capsule.center);
        Vector3 toHandle = axisVector * handleSign;
        // Метла стоит в кулаке вертикально, перпендикулярно руке: древко идёт вдоль оси «вверх» контроллера,
        // черенок смотрит вверх, прутья — вниз. «Вперёд» точки хвата — любая ось, перпендикулярная древку.
        Vector3 side = axis == 1 ? Vector3.forward : Vector3.up;
        Quaternion gripRotation = Quaternion.LookRotation(side, toHandle);

        var grip = GetOrCreateChild(broom.transform, "GripPoint");
        grip.localPosition = capsule.center;
        grip.localRotation = gripRotation;

        var secondary = GetOrCreateChild(broom.transform, "SecondaryGripPoint");
        secondary.localPosition = capsule.center + toHandle * SecondHandOffset;
        secondary.localRotation = gripRotation;

        Undo.RecordObject(grab, "Setup grip");
        grab.useDynamicAttach = false;                     // всегда за середину, а не где коснулся
        grab.attachTransform = grip;
        grab.secondaryAttachTransform = secondary;
        grab.attachEaseInTime = 0f;                        // мгновенно «телепортируется» в руку
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.farAttachMode = InteractableFarAttachMode.Near;
        EditorUtility.SetDirty(grab);

        if (broom.GetComponent<NearGrabOnly>() == null)
            Undo.AddComponent<NearGrabOnly>(broom);

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log($"[Broom Grip] Хват настроен. Черенок смотрит в сторону {(handleSign > 0 ? "+" : "-")}{"XYZ"[axis]} модели.");
    }

    static Transform GetOrCreateChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>
    /// В какую сторону по оси древка находится черенок (+1 или -1).
    /// Смотрим толщину модели у обоих концов: толстый конец — это прутья, значит черенок с другой стороны.
    /// </summary>
    static float HandleDirection(GameObject broom, int axis, Vector3 center)
    {
        int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
        float minAlong = float.MaxValue, maxAlong = float.MinValue;
        var points = new System.Collections.Generic.List<Vector3>();

        foreach (var filter in broom.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Vector3[] verts;
            try { verts = mesh.vertices; }
            catch { continue; }
            for (int i = 0; i < verts.Length; i += 3)   // каждой третьей вершины достаточно
            {
                Vector3 p = broom.transform.InverseTransformPoint(filter.transform.TransformPoint(verts[i]));
                points.Add(p);
                minAlong = Mathf.Min(minAlong, p[axis]);
                maxAlong = Mathf.Max(maxAlong, p[axis]);
            }
        }
        if (points.Count == 0) return 1f;

        float length = maxAlong - minAlong;
        float spreadLow = 0f, spreadHigh = 0f;
        foreach (var p in points)
        {
            float spread = Mathf.Max(Mathf.Abs(p[a1] - center[a1]), Mathf.Abs(p[a2] - center[a2]));
            if (p[axis] < minAlong + length * 0.2f) spreadLow = Mathf.Max(spreadLow, spread);
            else if (p[axis] > maxAlong - length * 0.2f) spreadHigh = Mathf.Max(spreadHigh, spread);
        }
        // прутья на «толстом» конце → черенок на противоположном
        return spreadHigh > spreadLow ? -1f : 1f;
    }
}
#endif
