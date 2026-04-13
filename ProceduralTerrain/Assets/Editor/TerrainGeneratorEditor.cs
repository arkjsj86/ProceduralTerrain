using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainGeneratorEditor
{
    [MenuItem("Tools/ProceduralTerrain/Create Terrain")]
    private static void CreateTerrain()
    {
        GameObject go = new GameObject("ProceduralTerrain");

        go.AddComponent<MeshFilter>();

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetDefaultMaterial();

        go.AddComponent<TerrainGenerator>();

        // Undo 등록: Ctrl+Z로 생성 취소 가능
        Undo.RegisterCreatedObjectUndo(go, "Create ProceduralTerrain");

        Selection.activeGameObject = go;
        SceneView.FrameLastActiveSceneView();
    }

    [MenuItem("Tools/ProceduralTerrain/Create Terrain", validate = true)]
    private static bool ValidateCreateTerrain()
    {
        // 씬에 이미 ProceduralTerrain이 존재하면 메뉴 비활성화
        return Object.FindFirstObjectByType<TerrainGenerator>() == null;
    }

    private static Material GetDefaultMaterial()
    {
        // URP 기본 Lit 머티리얼을 찾아 반환, 없으면 null(마젠타 표시)
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.shader.name.Contains("Lit"))
                return mat;
        }

        return null;
    }
}
