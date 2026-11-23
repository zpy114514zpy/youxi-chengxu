using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 洗干净之后，把玩偶身上那套“脏”材质换成第一关里干净玩偶的材质。
///
/// 两边的模型是同一份 FBX（连字节都一样），差别只在材质/贴图：
/// ATOY* 那套是压暗过的脏贴图，toy1/toy3/toy4/toy5 那套是干净的。
/// 所以这里不复制几何，只把材质换成第一关用的那几份（放在 Resources 里，
/// 由它们自己去引用第一关的贴图 —— 第一关的场景和脚本一个字都没动）。
/// </summary>
public static class Level2CleanToys
{
    // 脏材质名 → 干净材质资源路径。
    // ATOY2（小老虎）有 7 个部件，材质名就是 1~7，干净版按 toy_mat / toy_mat2… 一一对应。
    static readonly Dictionary<string, string> Map = new Dictionary<string, string>
    {
        { "MODEL_MAT",  "Level2/Toys/clean_toy1" },     // ATOY1 大眼仔  → 第一关 粉色独眼玩偶
        { "1",          "Level2/Toys/clean_toy3_1" },   // ATOY2 老虎    → 第一关 小熊玩偶
        { "2",          "Level2/Toys/clean_toy3_2" },
        { "3",          "Level2/Toys/clean_toy3_3" },
        { "4",          "Level2/Toys/clean_toy3_4" },
        { "5",          "Level2/Toys/clean_toy3_5" },
        { "6",          "Level2/Toys/clean_toy3_6" },
        { "7",          "Level2/Toys/clean_toy3_7" },
        { "MODEL3_MAT", "Level2/Toys/clean_toy4" },     // ATOY3 鱼      → 第一关 小猫玩偶
        { "MODEL4_MAT", "Level2/Toys/clean_toy5" },     // ATOY4 猪猪厨师 → 第一关 小猪玩偶
    };

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    static Material Load(string path)
    {
        if (cache.TryGetValue(path, out var m) && m) return m;
        m = Resources.Load<Material>(path);
        if (!m) Debug.LogWarning("[谜题二] 缺干净材质 " + path);
        cache[path] = m;
        return m;
    }

    /// <summary>把一件玩偶身上的脏材质整体换成干净的那套（就地替换，不动网格）。</summary>
    public static int Cleanse(Transform toy)
    {
        if (!toy) return 0;
        int replaced = 0;
        foreach (var r in toy.GetComponentsInChildren<Renderer>(true))
        {
            if (!r) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (!mat) continue;
                if (!Map.TryGetValue(mat.name, out var path)) continue;
                var clean = Load(path);
                if (!clean || clean == mat) continue;
                mats[i] = clean;
                changed = true;
                replaced++;
            }
            if (changed) r.sharedMaterials = mats;
        }

        if (replaced == 0)
            Debug.LogWarning("[谜题二] 这件玩偶没认出来是哪一套材质，保持原样：" + toy.name);
        else
            Debug.Log($"[谜题二] {toy.name} 换上干净材质 {replaced} 处");
        return replaced;
    }
}
