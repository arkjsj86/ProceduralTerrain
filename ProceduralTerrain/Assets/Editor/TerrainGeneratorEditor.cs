using UnityEditor;
using UnityEngine;

public static class TerrainGeneratorEditor
{
    [MenuItem("Tools/ProceduralTerrain/Create Terrain")]
    private static void CreateTerrain()
    {
        // ── 1. 지형 오브젝트 ──────────────────────────────────────────
        GameObject go = new GameObject("ProceduralTerrain");

        go.AddComponent<MeshFilter>();

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetOrCreateDefaultMaterial();

        go.AddComponent<TerrainGenerator>();

        // TerrainDeformer + Compute Shader 자동 할당
        TerrainDeformer deformer = go.AddComponent<TerrainDeformer>();
        ComputeShader deformShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/Shaders/TerrainDeformCompute.compute");
        if (deformShader != null)
        {
            SerializedObject deformerSo = new SerializedObject(deformer);
            deformerSo.FindProperty("deformShader").objectReferenceValue = deformShader;
            deformerSo.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[ProceduralTerrain] TerrainDeformCompute.compute를 찾을 수 없습니다. deformShader를 직접 할당해 주세요.");
        }

        Undo.RegisterCreatedObjectUndo(go, "Create ProceduralTerrain");

        // ── 2. TerrainCursor 오브젝트 (씬에 없을 때만 생성) ──────────
        if (Object.FindFirstObjectByType<TerrainCursor>() == null)
        {
            GameObject cursorGo = new GameObject("TerrainCursor");
            TerrainCursor cursor = cursorGo.AddComponent<TerrainCursor>();

            SerializedObject cursorSo = new SerializedObject(cursor);
            cursorSo.FindProperty("deformer").objectReferenceValue = deformer;
            cursorSo.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(cursorGo, "Create TerrainCursor");
        }

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

        TerrainGenerator gen = (TerrainGenerator)target;
        MeshFilter mf = gen.GetComponent<MeshFilter>();
        bool hasMesh = mf != null && mf.sharedMesh != null;

        GUI.enabled = hasMesh;
        if (GUILayout.Button("Save Mesh", GUILayout.Height(30)))
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Terrain Mesh",
                "TerrainMesh",
                "asset",
                "메시를 저장할 위치를 선택하세요.");

            if (!string.IsNullOrEmpty(path))
            {
                Mesh meshToSave = Instantiate(mf.sharedMesh);
                AssetDatabase.CreateAsset(meshToSave, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = meshToSave;
                EditorGUIUtility.PingObject(meshToSave);
            }
        }
        GUI.enabled = true;
    }
}
