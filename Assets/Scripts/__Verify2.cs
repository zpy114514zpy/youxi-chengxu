using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本轮（钥匙亮度 / 相册拾取 / 抽屉柜门分类 / 碎片流程）的自动自检。</summary>
public sealed class __Verify2 : MonoBehaviour
{
    const string Dir = "C:/Users/zhaop/WorkBuddy/spaces/2026_11_20_23-56-45";
    static readonly StringBuilder sb = new StringBuilder();
    static void L(string s) { sb.AppendLine(s); Debug.Log("[W] " + s); }
    static string F(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

    static IEnumerable<Transform> All()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) yield break;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                yield return t;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!File.Exists(Dir + "/.finalrun")) { Debug.Log("[W] 无标记，跳过"); return; }
        new GameObject("__verify2").AddComponent<__Verify2>();
    }

    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(3f);
        Safe(ReportClassification);
        yield return StartCoroutine(ReportTableDrawers());
        yield return StartCoroutine(ReportHinges());
        Safe(ReportKey);
        Safe(ReportPickups);
        yield return StartCoroutine(TestFlow());
        yield return StartCoroutine(Shots());
        File.WriteAllText(Dir + "/report2.txt", sb.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Dir + "/final_done.txt", "ok");
        Debug.Log("[W] 自检结束");
    }

    static void Safe(Action a)
    {
        try { a(); } catch (Exception e) { L("!! 异常 " + e.GetType().Name + ": " + e.Message); }
    }

    // ---------- A. 分类 ----------
    void ReportClassification()
    {
        L("=== A. 柜门 / 抽屉分类 ===");
        var all = FindObjectsOfType<FurnitureOpenable>().Where(x => x).ToList();
        int slide = all.Count(x => x.slide);
        int hinge = all.Count(x => !x.slide);
        L($"  共 {all.Count} 个可开面板：抽屉(直拉) {slide} 个，柜门(平开) {hinge} 个");

        // 抽几个代表看看
        foreach (var o in all.Where(x => !x.slide).Take(8))
        {
            var r = o.GetComponent<Renderer>() ?? o.GetComponentInChildren<Renderer>();
            if (!r) continue;
            L($"  [门] {o.name} 面板={F(r.bounds.size)} 铰链在右={o.hingeOnRight} 角度={o.openAngle}");
        }
        foreach (var o in all.Where(x => x.slide).Take(5))
        {
            var r = o.GetComponent<Renderer>() ?? o.GetComponentInChildren<Renderer>();
            if (!r) continue;
            L($"  [屉] {o.name} 面板={F(r.bounds.size)} travel={o.travel:F2}");
        }
    }

    // ---------- B. 办公桌抽屉（图3 / 图5）----------
    IEnumerator ReportTableDrawers()
    {
        L("=== B. 办公桌 / 学生桌抽屉：必须垂直于面板朝外，不能横向滑 ===");
        var targets = FindObjectsOfType<FurnitureOpenable>()
            .Where(x => x && (x.name.StartsWith("SM_WorkingTable") || x.name.StartsWith("Draw ")))
            .ToList();

        foreach (var o in targets)
        {
            var r = o.GetComponent<Renderer>() ?? o.GetComponentInChildren<Renderer>();
            if (!r) continue;
            Vector3 before = r.bounds.center;
            Vector3 size = r.bounds.size;

            var dir = o.PickOutwardByClearance();
            o.Toggle(before + dir * 2.0f);
            yield return new WaitForSecondsRealtime(.45f);       // 等动画跑完
            Vector3 delta = r.bounds.center - before;
            o.Close();
            yield return new WaitForSecondsRealtime(.25f);

            // 面板的水平长边（宽度方向）—— 抽屉只能沿它的垂直方向出来
            Vector3 longAxis = size.x >= size.z ? Vector3.right : Vector3.forward;
            Vector3 shortAxis = size.x >= size.z ? Vector3.forward : Vector3.right;
            float alongLong = Mathf.Abs(Vector3.Dot(delta, longAxis));
            float alongShort = Mathf.Abs(Vector3.Dot(delta, shortAxis));
            bool ok = alongShort > .12f && alongLong < .05f;

            L($"  {o.name} 面板={F(size)} 位移={F(delta)} 沿宽={alongLong:F3} 沿深={alongShort:F3} "
                + (ok ? "✓ 垂直朝外" : "✗ 方向不对"));
        }
    }

    // ---------- C. 柜门铰链（图4 / 图6）----------
    IEnumerator ReportHinges()
    {
        L("=== C. 柜门：左柜左开 / 右柜右开 ===");
        var doors = FindObjectsOfType<FurnitureOpenable>().Where(x => x && !x.slide).ToList();
        int right = doors.Count(x => x.hingeOnRight);
        L($"  柜门 {doors.Count} 扇：铰链在右 {right} 扇，在左 {doors.Count - right} 扇");

        // 按 mesh 名分组，看成对的左右门是否一左一右
        var groups = doors.GroupBy(x => MeshOf(x));
        foreach (var g in groups)
        {
            var list = g.ToList();
            if (list.Count < 2) continue;
            var flags = list.Select(x => x.hingeOnRight ? "R" : "L").ToArray();
            bool mixed = flags.Distinct().Count() > 1;
            L($"  {g.Key} ×{list.Count}: [{string.Join(",", flags)}] " + (mixed ? "✓ 已分左右" : "! 全在同侧"));
        }

        // 实测一扇：打开后应绕侧边转，门板中心朝外移动
        var sample = doors.FirstOrDefault(x => x.name.Contains("Wardrobe") || x.name.Contains("Cabinet"))
                     ?? doors.FirstOrDefault();
        if (sample)
        {
            var r = sample.GetComponent<Renderer>() ?? sample.GetComponentInChildren<Renderer>();
            if (r)
            {
                Vector3 before = r.bounds.center;
                var dir = sample.PickOutwardByClearance();
                sample.Toggle(before + dir * 2.0f);
                yield return new WaitForSecondsRealtime(.45f);
                Vector3 delta = r.bounds.center - before;
                sample.Close();
                yield return new WaitForSecondsRealtime(.25f);
                float outward = Vector3.Dot(delta, dir);
                L($"  样例 {sample.name} 铰链在右={sample.hingeOnRight} 位移={F(delta)} 朝外分量={outward:F3} "
                    + (outward > .08f ? "✓ 往外开" : "✗ 没往外开"));
            }
        }
    }

    static string MeshOf(FurnitureOpenable o)
    {
        var f = o.GetComponent<MeshFilter>() ?? o.GetComponentInChildren<MeshFilter>();
        return f && f.sharedMesh ? f.sharedMesh.name : o.name;
    }

    // ---------- D. 钥匙 ----------
    void ReportKey()
    {
        L("=== D. 钥匙亮度 ===");
        var key = All().FirstOrDefault(t => t.name == "key(1)");
        if (!key) { L("  ✗ 没找到 key(1)"); return; }
        var outline = key.GetComponent<ProximityOutline>();
        var childOutline = key.GetComponentInChildren<ProximityOutline>(true);
        var lamp = key.Find("钥匙微光");
        var light = lamp ? lamp.GetComponent<Light>() : null;
        L($"  位置 {F(key.position)}  描边={((outline || childOutline) ? "有（应该没有）" : "无 ✓")}");
        L(light
            ? $"  微光强度={light.intensity:F2} 半径={light.range:F2} 颜色={light.color}"
            : "  ✗ 没有微光灯");
        L($"  PickupItem={(key.GetComponent<PickupItem>() ? "有 ✓" : "无")}");
    }

    // ---------- E. 相册 / 碎片 ----------
    void ReportPickups()
    {
        L("=== E. 相册 / 小房间碎片 可拾取性 ===");
        foreach (var pick in FindObjectsOfType<PickupItem>())
        {
            if (!pick) continue;
            string id = pick.storyId ?? "";
            if (!id.StartsWith("tidy_") && id != "letter_fragment_4" && id != "room2_key") continue;
            var col = pick.GetComponent<Collider>() ?? pick.GetComponentInChildren<Collider>();
            string size = "-";
            if (col is BoxCollider b)
            {
                var s = col.transform.lossyScale;
                size = F(new Vector3(b.size.x * Mathf.Abs(s.x), b.size.y * Mathf.Abs(s.y), b.size.z * Mathf.Abs(s.z)))
                       + (pick.GetComponent<Collider>() ? " (自身)" : " (子级)");
            }
            else if (col) size = col.GetType().Name + "(子级)";
            L($"  {pick.displayName} id={id} 位置={F(pick.transform.position)} Collider={size} active={pick.gameObject.activeInHierarchy}");
        }
    }

    // ---------- G. 拍照 ----------
    IEnumerator Shots()
    {
        L("=== G. 拍照 ===");

        var key = All().FirstOrDefault(t => t.name == "key(1)");
        if (key)
        {
            // 正上方俯视：抽屉侧板挡不住，能直接看清钥匙的形状
            Vector3 eye = key.position + new Vector3(.05f, .30f, -.04f);
            yield return Shot(eye, key.position, "shot_key", .10f);

            // A/B：相机不再补光，只靠场景光 + 钥匙微光；再关掉钥匙微光，只靠场景光。
            // 这一步是为了区分“白光团到底是钥匙自己发的，还是拍照补光”。
            Vector3 eye2 = key.position + new Vector3(.10f, .45f, -.12f);
            yield return Shot(eye2, key.position, "shot_key_scene", 0f);

            var lamp = key.Find("钥匙微光");
            if (lamp) lamp.gameObject.SetActive(false);
            yield return Shot(eye2, key.position, "shot_key_nolamp", 0f);
            if (lamp) lamp.gameObject.SetActive(true);
        }

        // 办公桌：先按真实交互把抽屉拉开，再退到桌前拍
        var desk = FindObjectsOfType<FurnitureOpenable>()
            .FirstOrDefault(x => x && x.name == "SM_WorkingTable_A1_Cab3");
        if (desk)
        {
            var r = desk.GetComponent<Renderer>() ?? desk.GetComponentInChildren<Renderer>();
            if (r)
            {
                Vector3 c = r.bounds.center;
                var dir = desk.PickOutwardByClearance();
                desk.Toggle(c + dir * 2f);
                yield return new WaitForSecondsRealtime(.6f);
                Vector3 eye = c + dir * 1.35f + Vector3.up * .35f;
                yield return Shot(eye, c, "shot_desk", .55f);
                desk.Close();
                yield return new WaitForSecondsRealtime(.3f);
            }
        }

        // 衣柜：双开百叶门，两扇一起开，看是不是一左一右
        var wardrobeDoors = FindObjectsOfType<FurnitureOpenable>()
            .Where(x => x && !x.slide && x.name.Contains("Cabinets"))
            .GroupBy(x => x.transform.parent ? x.transform.parent.name : "")
            .Select(g => g.OrderBy(x => x.transform.position.x).Take(2).ToList())
            .FirstOrDefault(g => g.Count >= 2) ?? new List<FurnitureOpenable>();
        L("  衣柜候选门=" + wardrobeDoors.Count);
        Vector3 wardrobeOut = wardrobeDoors.Count > 0 ? wardrobeDoors[0].PickOutwardByClearance() : Vector3.forward;
        Vector3 wardrobeCenter = Vector3.zero;
        foreach (var d in wardrobeDoors)
        {
            var r = d.GetComponent<Renderer>() ?? d.GetComponentInChildren<Renderer>();
            if (!r) continue;
            wardrobeCenter += r.bounds.center;
            d.Toggle(r.bounds.center + d.PickOutwardByClearance() * 2f);
        }
        if (wardrobeDoors.Count > 0)
        {
            wardrobeCenter /= wardrobeDoors.Count;
            yield return new WaitForSecondsRealtime(.9f);
            // 顺门板法线退到 2m 外平视，才能看出两扇是分开往左右扇
            yield return Shot(wardrobeCenter + wardrobeOut * 2.0f + Vector3.up * .15f,
                              wardrobeCenter, "shot_wardrobe", .6f);
            foreach (var d in wardrobeDoors) d.Close();
        }
    }

    IEnumerator Shot(Vector3 eye, Vector3 target, string name, float fill)
    {
        var go = new GameObject("shotcam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 52f;
        cam.nearClipPlane = .02f;
        cam.farClipPlane = 40f;
        go.transform.position = eye;
        go.transform.LookAt(target);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 3f;
        light.intensity = fill;
        light.color = Color.white;
        light.shadows = LightShadows.None;

        var rt = new RenderTexture(800, 450, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(800, 450, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 800, 450), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Dir + "/" + name + ".png", tex.EncodeToPNG());
        cam.targetTexture = null;
        Destroy(go);
        Destroy(rt);
        Destroy(tex);
        L("  拍了 " + name);
    }

    // ---------- F. 整理流程 ----------
    IEnumerator TestFlow()
    {
        L("=== F. 整理谜题收尾流程 ===");
        var tidy = FindObjectOfType<Room2Tidy>();
        if (!tidy) { L("  ✗ 没找到 Room2Tidy"); yield break; }

        var inv = FindObjectOfType<PlayerInventory>();
        tidy.Begin();
        yield return new WaitForSecondsRealtime(.3f);

        foreach (var spot in FindObjectsOfType<TidySpot>())
        {
            var item = FindObjectsOfType<PickupItem>().FirstOrDefault(p => p.storyId == "tidy_" + spot.itemId);
            if (item) inv.Take(item);
            bool ok = spot.Place(inv);
            L($"  {spot.label}：拾取→放置 = {(ok ? "✓" : "✗")}");
            yield return new WaitForSecondsRealtime(.12f);
        }

        var chair = All().FirstOrDefault(t => t.name == "SM_OfficeChair_A1");
        if (chair) chair.position = new Vector3(-18.3252f, 3.2000f, -105.7380f);
        yield return new WaitForSecondsRealtime(.5f);
        L("  四件归位 Solved=" + tidy.Solved);

        // 挪玩家进小房间
        var player = FindObjectOfType<PlayerController>();
        var frag0 = DrawerFragmentReveal.FindDeep("对象005");
        Vector3 target = frag0 ? frag0.position + new Vector3(0f, -0.3f, 1.2f) : new Vector3(-21.08f, 3.26f, -106.15f);
        target.y = 3.26f;
        if (player) player.transform.position = target;
        L($"  玩家挪到 {F(target)}，碎片在 {(frag0 ? F(frag0.position) : "?")}");
        // 直接改 position 会被角色控制器/物理弹回去，这里连续压 1.2 秒
        for (int i = 0; i < 80; i++)
        {
            if (player) player.transform.position = target;
            yield return null;
        }
        if (player)
        {
            var frag1 = DrawerFragmentReveal.FindDeep("对象005");
            float d = frag1 ? Vector3.Distance(player.transform.position, frag1.position) : -1f;
            L($"  锁定位置后玩家在 {F(player.transform.position)}，到碎片距离 {d:F2}");
        }

        var frag = DrawerFragmentReveal.FindDeep("对象005");
        var glow = frag ? frag.Find("碎片指引光") : null;
        L("  进入小房间后：碎片=" + (frag ? frag.name : "无") + " 指引光=" + (glow ? "已亮 ✓" : "无 ✗"));
        L("  碎片 PickupItem=" + (frag && frag.GetComponent<PickupItem>() ? "有 ✓" : "无 ✗")
            + " id=" + (frag && frag.GetComponent<PickupItem>() ? frag.GetComponent<PickupItem>().storyId : "-"));

        if (frag) frag.gameObject.SetActive(false);      // 模拟被捡走
        yield return new WaitForSecondsRealtime(.5f);
        L("  拾取后 UI 队列 busy=" + (StoryProgress.Instance ? StoryProgress.Instance.IsBusy.ToString() : "?"));
    }
}
