using UnityEditor;
using UnityEngine;

public static class TerrainGeneratorEditor
{
    [MenuItem("Tools/ProceduralTerrain/Create Terrain")]
    private static void CreateTerrain()
    {
        GameObject go = new GameObject("ProceduralTerrain");

        go.AddComponent<MeshFilter>();

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetOrCreateDefaultMaterial();

        go.AddComponent<TerrainGenerator>();

        Undo.RegisterCreatedObjectUndo(go, "Create ProceduralTerrain");

        Selection.activeGameObject = go;
        SceneView.FrameLastActiveSceneView();
    }

    [MenuItem("Tools/ProceduralTerrain/Create Terrain", validate = true)]
    private static bool ValidateCreateTerrain()
    {
        return Object.FindFirstObjectByType<TerrainGenerator>() == null;
    }

    // Assets/Materials/TerrainDefault.mat 를 찾거나 없으면 새로 생성
    private static Material GetOrCreateDefaultMaterial()
    {
        const string matPath = "Assets/Materials/TerrainDefault.mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[ProceduralTerrain] URP Lit 셰이더를 찾을 수 없습니다. 머티리얼을 직접 할당해 주세요.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        Material mat = new Material(shader) { name = "TerrainDefault" };
        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.SaveAssets();

        Debug.Log("[ProceduralTerrain] TerrainDefault.mat 생성 완료: " + matPath);
        return mat;
    }
}

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Preview", GUILayout.Height(30)))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            generator.GenerateTerrain();
            EditorUtility.SetDirty(generator);
            SceneView.RepaintAll();
        }
    }
}
