#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Строит травяной Terrain для сцены полёта на метле.
///
/// Один раз срабатывает автоматически, когда Unity откроет SampleScene и ассета террейна ещё нет.
/// Перестроить вручную: меню Broom → Build Grass Terrain (старый террейн и его ассеты заменятся).
///
/// Что получается:
///   * поле 1×1 км с плоской площадкой в центре (там стартует метла), холмами и кольцом гор по краю;
///   * три слоя травы из «Stylized Grass Texture» с РАЗНЫМ размером тайла и оттенком —
///     они смешиваются по крупному шуму и крутизне склонов, поэтому повтор текстуры не бросается в глаза;
///   * маска (AO, высота, гладкость) и смешивание слоёв по высоте рельефа текстуры.
/// </summary>
[InitializeOnLoad]
public static class BroomTerrainBuilder
{
    const string Folder = "Assets/Terrain";
    const string DataPath = Folder + "/GrassTerrainData.asset";
    const string MaterialPath = Folder + "/GrassTerrain.mat";
    const string TerrainName = "GrassTerrain";
    const string TexFolder = "Assets/Models/LowlyPoly/Stylized Grass Texture/Textures/";
    const string UrpTerrainMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";

    // Размеры мира
    const float Size = 1000f;        // ширина и длина, м
    const float MaxHeight = 220f;    // максимальная высота рельефа, м
    const int HeightRes = 513;
    const int SplatRes = 512;

    static BroomTerrainBuilder()
    {
        EditorApplication.delayCall += TryAutoBuild;
    }

    static void TryAutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(DataPath)) return;                                   // уже строили — ничего не трогаем
        if (SceneManager.GetActiveScene().name != "SampleScene") return;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "Vol_42_1_Base_Color.png") == null) return;
        Build();
    }

    [MenuItem("Broom/Build Grass Terrain")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Terrain");

        var old = GameObject.Find(TerrainName);
        if (old != null) Object.DestroyImmediate(old);

        // ---------- Слои травы ----------
        var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "Vol_42_1_Base_Color.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "Vol_42_1_Normal.png");
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFolder + "Vol_42_1_MaskMap.png");

        // Разные размеры тайла (не кратные друг другу) + разные оттенки = не видно сетки повторов
        var grass = CreateLayer("Grass", baseColor, normal, mask, new Vector2(5f, 5f), Vector2.zero,
                                new Vector4(1f, 1f, 1f, 1f));
        var lush = CreateLayer("GrassLush", baseColor, normal, mask, new Vector2(8.3f, 8.3f), new Vector2(2.1f, 4.7f),
                               new Vector4(0.72f, 0.88f, 0.70f, 1f));
        var dry = CreateLayer("GrassDry", baseColor, normal, mask, new Vector2(6.7f, 6.7f), new Vector2(3.3f, 1.2f),
                              new Vector4(1.0f, 0.86f, 0.58f, 1f));

        // ---------- Данные террейна ----------
        AssetDatabase.DeleteAsset(DataPath);
        var data = new TerrainData();
        data.heightmapResolution = HeightRes;               // сначала разрешение, потом размер
        data.size = new Vector3(Size, MaxHeight, Size);
        data.alphamapResolution = SplatRes;
        data.baseMapResolution = 1024;
        data.terrainLayers = new[] { grass, lush, dry };

        data.SetHeights(0, 0, BuildHeights());
        data.SetAlphamaps(0, 0, BuildSplat(data));
        AssetDatabase.CreateAsset(data, DataPath);

        // ---------- Материал ----------
        var material = BuildMaterial();

        // ---------- Объект ----------
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = TerrainName;
        go.transform.position = new Vector3(-Size * 0.5f, 0f, -Size * 0.5f);
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = material;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 4f;
        terrain.basemapDistance = 350f;
        terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        Undo.RegisterCreatedObjectUndo(go, "Build Grass Terrain");

        // Старая плоскость-земля больше не нужна (не удаляем, просто выключаем)
        var plane = GameObject.Find("Plane");
        if (plane != null && plane.GetComponent<MeshFilter>() != null)
            plane.SetActive(false);

        AssetDatabase.SaveAssets();
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Broom] Grass terrain built: " + DataPath);
    }

    static TerrainLayer CreateLayer(string name, Texture2D diffuse, Texture2D normal, Texture2D mask,
                                    Vector2 tile, Vector2 offset, Vector4 tint)
    {
        string path = Folder + "/" + name + ".terrainlayer";
        AssetDatabase.DeleteAsset(path);
        var layer = new TerrainLayer
        {
            diffuseTexture = diffuse,
            normalMapTexture = normal,
            maskMapTexture = mask,
            normalScale = 1f,
            tileSize = tile,
            tileOffset = offset,
            diffuseRemapMin = Vector4.zero,
            diffuseRemapMax = tint,
            maskMapRemapMin = Vector4.zero,
            maskMapRemapMax = Vector4.one,
            metallic = 0f,
            smoothness = 0.15f
        };
        AssetDatabase.CreateAsset(layer, path);
        return layer;
    }

    static Material BuildMaterial()
    {
        AssetDatabase.DeleteAsset(MaterialPath);
        var source = AssetDatabase.LoadAssetAtPath<Material>(UrpTerrainMaterial);
        Material mat = source != null
            ? new Material(source)
            : new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
        mat.name = "GrassTerrain";

        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_MASKMAP");
        mat.SetFloat("_EnableHeightBlend", 1f);
        mat.SetFloat("_HeightTransition", 0.35f);
        mat.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
        mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
        mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");

        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    // ---------- Рельеф ----------
    static float[,] BuildHeights()
    {
        var h = new float[HeightRes, HeightRes];
        float half = Size * 0.5f;
        for (int z = 0; z < HeightRes; z++)
        {
            for (int x = 0; x < HeightRes; x++)
            {
                float wx = (x / (float)(HeightRes - 1)) * Size - half;
                float wz = (z / (float)(HeightRes - 1)) * Size - half;
                float r = Mathf.Sqrt(wx * wx + wz * wz);

                // мягкие холмы, в центре — ровная площадка для старта
                float rolling = Fbm(wx * 0.004f + 137.1f, wz * 0.004f + 59.3f, 4) * 22f
                              + Fbm(wx * 0.018f + 11.7f, wz * 0.018f + 83.9f, 3) * 4f;
                rolling *= SmoothStep(35f, 150f, r);

                // кольцо гор по краю карты, чтобы мир не обрывался
                float ring = SmoothStep(300f, 480f, r);
                float ridge = 1f - Mathf.Abs(2f * Fbm(wx * 0.006f + 412.5f, wz * 0.006f + 77.7f, 4) - 1f);
                float mountains = ring * (60f + 140f * ridge * ridge);

                h[z, x] = Mathf.Clamp01((rolling + mountains) / MaxHeight);
            }
        }
        return h;
    }

    // ---------- Раскраска слоями ----------
    static float[,,] BuildSplat(TerrainData data)
    {
        var a = new float[SplatRes, SplatRes, 3];
        float half = Size * 0.5f;
        for (int z = 0; z < SplatRes; z++)
        {
            for (int x = 0; x < SplatRes; x++)
            {
                float nx = x / (float)(SplatRes - 1);
                float nz = z / (float)(SplatRes - 1);
                float wx = nx * Size - half;
                float wz = nz * Size - half;

                float height = data.GetInterpolatedHeight(nx, nz);
                float slope = data.GetSteepness(nx, nz);

                float macro = Fbm(wx * 0.007f + 901.3f, wz * 0.007f + 33.1f, 4);
                float patches = Fbm(wx * 0.03f + 55.5f, wz * 0.03f + 291.4f, 3);

                float lushW = SmoothStep(0.42f, 0.66f, macro);
                float dryW = Mathf.Max(SmoothStep(18f, 34f, slope), SmoothStep(40f, 110f, height));
                dryW = Mathf.Max(dryW, SmoothStep(0.62f, 0.78f, patches) * 0.7f);

                float rest = 1f - dryW;
                a[z, x, 0] = rest * (1f - lushW);
                a[z, x, 1] = rest * lushW;
                a[z, x, 2] = dryW;
            }
        }
        return a;
    }

    static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(x, y);
            norm += amp;
            x = x * 2.03f + 17.3f;
            y = y * 2.03f + 9.1f;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    static float SmoothStep(float a, float b, float v)
    {
        float t = Mathf.Clamp01((v - a) / (b - a));
        return t * t * (3f - 2f * t);
    }
}
#endif
