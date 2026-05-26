using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// ============================================================
// MeshCombinerTool — Ferramenta de Mesh Combine Inteligente
// Combina meshes que compartilham material em um único objeto
// Reduz draw calls e melhora performance em cenas complexas
// Acessível via: Tools → Mesh Combiner → Combine Selected Objects
// Atalho: Ctrl+Shift+M
// ============================================================
public class MeshCombinerTool : EditorWindow
{
    // ── CONFIGURAÇÕES DA JANELA ──────────────────────────────
    private bool combineByMaterial = true; // Agrupa por material (padrão) ou combina tudo
    private bool keepOriginals = true; // Mantém originais organizados ou remove após combinação

    // ── MENU ─────────────────────────────────────────────────
    [MenuItem("Tools/Mesh Combiner/Combine Selected Objects %#m")]
    public static void ShowWindow()
    {
        MeshCombinerTool window = GetWindow<MeshCombinerTool>("Mesh Combiner");
        window.minSize = new Vector2(300, 200);
        window.Show();
    }

    // ── INTERFACE DA JANELA ──────────────────────────────────
    private void OnGUI()
    {
        GUILayout.Label("Mesh Combiner", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Contador de objetos selecionados — atualizado em tempo real via OnSelectionChange
        int selectedCount = Selection.gameObjects.Length;
        EditorGUILayout.HelpBox(
            selectedCount == 0
                ? "Nenhum objeto selecionado."
                : $"{selectedCount} objeto(s) selecionado(s) na cena.",
            selectedCount == 0 ? MessageType.Warning : MessageType.Info
        );

        EditorGUILayout.Space();

        // Opções configuráveis
        GUILayout.Label("Configurações", EditorStyles.boldLabel);
        combineByMaterial = EditorGUILayout.Toggle("Combine by Material", combineByMaterial);
        keepOriginals = EditorGUILayout.Toggle("Keep Originals", keepOriginals);

        EditorGUILayout.Space();

        // Botão desabilitado automaticamente com menos de 2 objetos selecionados
        GUI.enabled = selectedCount >= 2;
        if (GUILayout.Button("Combine Selected Objects", GUILayout.Height(40)))
            CombineSelected(combineByMaterial, keepOriginals);
        GUI.enabled = true;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Atalho: Ctrl+Shift+M", MessageType.None);
    }

    // Atualiza o contador automaticamente quando a seleção muda na cena
    private void OnSelectionChange() { Repaint(); }

    // ── LÓGICA DE COMBINAÇÃO ─────────────────────────────────
    public static void CombineSelected(bool byMaterial = true, bool keepOriginals = true)
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length < 2)
        {
            EditorUtility.DisplayDialog("Mesh Combiner",
                "Selecione pelo menos 2 objetos para combinar.", "OK");
            return;
        }

        // Agrupa objetos por material ou tudo junto
        // Usa string como chave para evitar null key quando byMaterial = false
        // byMaterial = true  → chave = nome + instanceID do material (único por material)
        // byMaterial = false → chave fixa "ALL" (todos no mesmo grupo)
        Dictionary<string, List<MeshFilter>> groups = new Dictionary<string, List<MeshFilter>>();
        Dictionary<string, Material> groupMaterials = new Dictionary<string, Material>();

        foreach (GameObject obj in selectedObjects)
        {
            MeshFilter mf = obj.GetComponent<MeshFilter>();
            MeshRenderer mr = obj.GetComponent<MeshRenderer>();

            if (mf == null || mr == null || mf.sharedMesh == null) continue;

            string key = byMaterial
                ? mr.sharedMaterial.name + mr.sharedMaterial.GetInstanceID()
                : "ALL";

            if (!groups.ContainsKey(key))
            {
                groups[key] = new List<MeshFilter>();
                groupMaterials[key] = mr.sharedMaterial;
            }

            groups[key].Add(mf);
        }

        if (groups.Count == 0)
        {
            EditorUtility.DisplayDialog("Mesh Combiner",
                "Nenhum objeto válido encontrado. Verifique se os objetos têm MeshFilter e MeshRenderer.", "OK");
            return;
        }

        // Registra estado inicial para suporte a Undo (Ctrl+Z)
        Undo.RegisterCompleteObjectUndo(selectedObjects, "Combine Meshes");

        int totalBefore = 0;
        int totalAfter = 0;

        foreach (var group in groups)
        {
            string key = group.Key;
            List<MeshFilter> mfs = group.Value;
            Material mat = groupMaterials[key];

            // Ignora grupos com menos de 2 objetos — não há o que combinar
            if (mfs.Count < 2) continue;

            totalBefore += mfs.Count;

            // Monta array de CombineInstance — estrutura nativa do Unity para combinação
            CombineInstance[] combine = new CombineInstance[mfs.Count];
            List<string> originalNames = new List<string>();

            for (int i = 0; i < mfs.Count; i++)
            {
                combine[i].mesh = mfs[i].sharedMesh;
                // localToWorldMatrix preserva posição, rotação e escala absolutas de cada objeto
                // Sem isso todos os meshes iriam para a origem (0,0,0)
                combine[i].transform = mfs[i].transform.localToWorldMatrix;
                originalNames.Add(mfs[i].gameObject.name);
            }

            // Nome do objeto combinado inclui nomes dos originais para rastreabilidade
            string namesList = string.Join(", ", originalNames);
            string combinedName = byMaterial
                ? $"Combined_{mat.name} [{namesList}]"
                : $"Combined_All [{namesList}]";

            // Cria o objeto combinado na cena
            GameObject combined = new GameObject(combinedName);
            MeshFilter combinedMF = combined.AddComponent<MeshFilter>();
            MeshRenderer combinedMR = combined.AddComponent<MeshRenderer>();

            // Cria e configura o novo mesh
            Mesh newMesh = new Mesh();
            newMesh.name = byMaterial ? $"Mesh_{mat.name}" : "Mesh_Combined";

            // IndexFormat.UInt32 — suporte a meshes com mais de 65.535 vértices
            // Padrão UInt16 limitaria cenas complexas
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // true = usa matrizes do mundo (posições absolutas)
            // true = um único sub-mesh (mesmo material)
            newMesh.CombineMeshes(combine, true, true);

            // Recalcula bounds — garante Frustum Culling e Clipping corretos
            // Sem isso o mesh pode sumir da tela incorretamente
            newMesh.RecalculateBounds();

            combinedMF.sharedMesh = newMesh;
            combinedMR.sharedMaterial = mat;

            Undo.RegisterCreatedObjectUndo(combined, "Combine Meshes");

            if (keepOriginals)
            {
                // Coleta pais únicos ANTES de mover os objetos
                // Necessário para detectar pais que ficarem vazios após a reorganização
                HashSet<Transform> parents = new HashSet<Transform>();
                foreach (MeshFilter mf in mfs)
                    if (mf.transform.parent != null)
                        parents.Add(mf.transform.parent);

                // Cria Empty para organizar os originais — desativado mas preservado
                GameObject originalsGroup = new GameObject($"MeshCombiner_Originals_{(byMaterial ? mat.name : "All")}");
                Undo.RegisterCreatedObjectUndo(originalsGroup, "Combine Meshes");

                // Move originais para dentro do grupo — mantém ativos internamente
                foreach (MeshFilter mf in mfs)
                {
                    Undo.RecordObject(mf.gameObject, "Combine Meshes");
                    mf.gameObject.SetActive(true);
                    Undo.SetTransformParent(mf.transform, originalsGroup.transform, "Combine Meshes");
                }

                // Remove pais que ficaram vazios após mover os filhos
                foreach (Transform parent in parents)
                    if (parent != null && parent.childCount == 0)
                        Undo.DestroyObjectImmediate(parent.gameObject);

                // Desativa o grupo inteiro — originais preservados mas ocultos
                Undo.RecordObject(originalsGroup, "Combine Meshes");
                originalsGroup.SetActive(false);
            }
            else
            {
                // Coleta pais únicos ANTES de deletar os objetos
                HashSet<Transform> parents = new HashSet<Transform>();
                foreach (MeshFilter mf in mfs)
                    if (mf.transform.parent != null)
                        parents.Add(mf.transform.parent);

                // Deleta os objetos originais
                foreach (MeshFilter mf in mfs)
                    Undo.DestroyObjectImmediate(mf.gameObject);

                // Remove pais que ficaram vazios após deletar os filhos
                foreach (Transform parent in parents)
                    if (parent != null && parent.childCount == 0)
                        Undo.DestroyObjectImmediate(parent.gameObject);
            }

            totalAfter++;
            Debug.Log($"[MeshCombiner] '{(byMaterial ? mat.name : "All")}': {mfs.Count} objetos → 1 combinado");
        }

        // Relatório final — draw calls reduzidos
        Debug.Log($"[MeshCombiner] Concluído! {totalBefore} → {totalAfter} objetos. " +
                  $"Draw calls reduzidos: ~{totalBefore - totalAfter}.");

        EditorUtility.DisplayDialog("Mesh Combiner — Concluído!",
            $"Combinação finalizada!\n\n" +
            $"Objetos antes: {totalBefore}\n" +
            $"Objetos depois: {totalAfter}\n" +
            $"Draw calls reduzidos: ~{totalBefore - totalAfter}\n\n" +
            $"{(keepOriginals ? "Originais organizados em grupo desativado." : "Originais removidos.")}\n" +
            $"Use Ctrl+Z para desfazer.", "OK");
    }
}