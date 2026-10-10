#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ставит меню-шляпу в сцену и выключает старое меню паузы на Tab (два меню паузы спорили бы за Time.timeScale).
/// Срабатывает один раз автоматически. Повторить — меню Broom → Setup Hat Menu.
/// </summary>
[InitializeOnLoad]
public static class HatMenuSetup
{
    static string DoneKey => "HatMenuSetupDone_" + Application.dataPath;

    static HatMenuSetup()
    {
        EditorApplication.delayCall += TryAuto;
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (GameObject.Find("XR Origin (XR Rig)") == null) return;
        Setup();
    }

    [MenuItem("Broom/Setup Hat Menu")]
    public static void Setup()
    {
        var rig = GameObject.Find("XR Origin (XR Rig)");

        var menuGo = GameObject.Find("HatMenu");
        if (menuGo == null)
        {
            menuGo = new GameObject("HatMenu");
            Undo.RegisterCreatedObjectUndo(menuGo, "Add Hat Menu");
        }
        var menu = menuGo.GetComponent<HatMenu>();
        if (menu == null) menu = Undo.AddComponent<HatMenu>(menuGo);

        if (rig != null)
        {
            var so = new SerializedObject(menu);
            so.FindProperty("head").objectReferenceValue = FindDeep(rig.transform, "Main Camera");
            so.FindProperty("leftHand").objectReferenceValue = FindDeep(rig.transform, "Left Controller");
            so.FindProperty("rightHand").objectReferenceValue = FindDeep(rig.transform, "Right Controller");
            so.ApplyModifiedProperties();
        }

        var oldMenu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
        if (oldMenu != null)
        {
            Undo.RecordObject(oldMenu.gameObject, "Disable old pause menu");
            oldMenu.gameObject.SetActive(false);
        }

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorPrefs.SetBool(DoneKey, true);
        Debug.Log("[Hat Menu] Меню-шляпа добавлено в сцену.");
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
