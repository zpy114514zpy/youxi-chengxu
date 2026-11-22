using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>临时诊断：把场景里所有可开面板的几何导出到 diag.txt（编辑模式，不需要 Play）。</summary>
[InitializeOnLoad]
internal static class __Diag
{
    const string Dir = "C:/Users/zhaop/WorkBuddy/spaces/2026_11_20_23-56-45";
    static string Marker => Dir + "/.diag";

    static __Diag()
    {
        EditorApplication.delayCall += Run;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlaying) return;
        if (File.Exists(Marker)) Run();
    }

    static string S(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

    static void Run()
    {
        if (!File.Exists(Marker)) return;
        File.Delete(Marker);
        if (EditorApplication.isPlaying) return;

        var sb = new StringBuilder();
        sb.AppendLine("=== 可开面板几何诊断 ===");

        var groups = new Dictionary<string, List<MeshRenderer>>();
        foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!mr) continue;
            var mf = mr.GetComponent<MeshFilter>();
            string mesh = mf && mf.sharedMesh ? mf.sharedMesh.name : "";
            if (!IsCandidate(mesh)) continue;
            var bb = mr.bounds.size;
            if (Mathf.Max(bb.x, Mathf.Max(bb.y, bb.z)) > 2.3f) continue;   // 排除整扇房门
            if (Mathf.Max(bb.x, Mathf.Max(bb.y, bb.z)) < .05f) continue;
            string key = mesh;
            if (!groups.ContainsKey(key)) groups[key] = new List<MeshRenderer>();
            groups[key].Add(mr);
        }

        foreach (var kv in groups)
        {
            sb.AppendLine();
            sb.AppendLine("######## " + kv.Key + "  共 " + kv.Value.Count + " 个");
            foreach (var mr in kv.Value)
            {
                var b = mr.bounds;
                sb.AppendLine($"  [{mr.name}] size={S(b.size)} center={S(b.center)}");
                // 找到家具根（最近的 BP_ 祖先），合并其下所有渲染器
                Transform root = mr.transform.parent;
                while (root && root.parent && !root.name.StartsWith("BP_")) root = root.parent;
                if (root)
                {
                    var rs = root.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var fb = rs[0].bounds;
                        for (int i = 1; i < rs.Length; i++) fb.Encapsulate(rs[i].bounds);
                        sb.AppendLine($"      家具根={root.name} 框={S(fb.size)} 中心={S(fb.center)} 渲染器={rs.Length}");
                        sb.AppendLine($"      面板-家具中心=({b.center.x - fb.center.x:F2}, {b.center.z - fb.center.z:F2})");
                    }
                }
                var chain = new List<string>();
                for (var t = mr.transform.parent; t && chain.Count < 7; t = t.parent)
                {
                    var r = t.GetComponent<Renderer>();
                    chain.Add(r ? $"{t.name}(R {S(r.bounds.size)} @{S(r.bounds.center)})" : $"{t.name}(- )");
                }
                sb.AppendLine("      父链: " + string.Join(" < ", chain.ToArray()));
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== 信封碎片 / 相册 相关 ===");
        foreach (var t in Object.FindObjectsOfType<Transform>())
        {
            if (!t) continue;
            string n = t.name;
            bool hit = n.Contains("对象005") || n.Contains("Envelope") || n.Contains("信封")
                       || n.Contains("对象002") || n.Contains("letter") || n.Contains("Photo")
                       || n.Contains("相册");
            if (!hit) continue;
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            sb.AppendLine($"  {n}  size={S(b.size)} center={S(b.center)}  renderers={rs.Length} "
                + $"mesh={(rs[0] is MeshRenderer m && m.GetComponent<MeshFilter>() && m.GetComponent<MeshFilter>().sharedMesh ? m.GetComponent<MeshFilter>().sharedMesh.name : "?")}");
        }

        File.WriteAllText(Dir + "/diag.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[Diag] 写出 diag.txt，面板种类 " + groups.Count);
    }

    static bool IsCandidate(string mesh)
    {
        if (string.IsNullOrEmpty(mesh)) return false;
        if (mesh.StartsWith("SM_Drawer_A1_Cab")) return true;
        if (mesh.StartsWith("SM_WorkingTable_A1_Cab")) return true;
        if (mesh.StartsWith("SM_WorkingTable_A2_Cab")) return true;
        if (mesh.StartsWith("SM_Shelf_TV_A1_Door")) return true;
        if (mesh.StartsWith("Draw ") && mesh.Contains("_low")) return true;
        bool furniture = mesh.Contains("Door") || mesh.Contains("Drawer") || mesh.Contains("_Cab")
                         || mesh.Contains("Wardrobe") || mesh.Contains("Cabinet")
                         || mesh.Contains("Closet") || mesh.Contains("Cupboard");
        return furniture;
    }
}
