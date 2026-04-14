# 5단계: Custom Editor 도구화 설계

**날짜:** 2026-04-14  
**브랜치:** feature/jhpark

---

## 목표

다른 Unity 프로젝트로 이식할 때 절차적 지형 메시를 `.asset` 파일로 저장할 수 있게 한다.

---

## 현재 상태

`Assets/Editor/TerrainGeneratorEditor.cs`에 이미 구현됨:
- `[MenuItem]` — Tools/ProceduralTerrain/Create Terrain
- `Preview` 버튼 — Inspector에서 `GenerateTerrain()` 호출
- 파라미터 노출 — `SerializeField`로 Inspector에 자동 표시

---

## 추가할 것

### Save Mesh 버튼

`TerrainGeneratorInspector.OnInspectorGUI()`에 버튼 1개 추가.

**동작:**
1. `EditorUtility.SaveFilePanelInProject()`로 저장 경로 선택
2. 현재 MeshFilter의 sharedMesh를 `AssetDatabase.CreateAsset()`으로 `.asset` 저장
3. 저장 완료 시 Project 창에서 선택 포커스

**전제 조건:** 메시가 없으면(HeightMap 미생성) 버튼 비활성화.

---

## 수정 파일

| 상태 | 경로 |
|------|------|
| 수정 | `Assets/Editor/TerrainGeneratorEditor.cs` |

---

## 완료 기준

- Inspector에서 "Save Mesh" 버튼 표시
- 버튼 클릭 → 저장 다이얼로그 → `.asset` 파일 생성
- 메시 없을 때 버튼 비활성화
