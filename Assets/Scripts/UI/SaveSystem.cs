using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一份存档里装的东西：
///   · 在哪个场景、玩家站在哪、朝哪看
///   · 第二关三个谜题各自的进度（相片补了几块 / 玩偶洗了几个摆了哪几个 / 镜子到哪一步）
///   · 哪些门是开着的
///   · 剧情线索与信封碎片（第一关和第二关共用）
/// 第一关的机关状态没有全部写进来 —— 第一关的谜题脚本是另一套，这里不去动它。
/// </summary>
[Serializable]
public class SaveData
{
    public int version = 1;
    public string scene = "Level 2";
    public float px, py, pz;
    public float yaw, pitch;
    public string stamp = "";

    public int photoPlaced;
    public string toys = "";
    public int mirrorStage;
    public string doors = "";

    public string clues = "";
    public string fragments = "";
}

/// <summary>四格存档：第 1~4 格依次写，第 5 次回到第 1 格覆盖。</summary>
public static class SaveSystem
{
    public const int Slots = 4;
    const string NextKey = "save_next_slot";

    public static int NextSlot
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(NextKey, 1), 1, Slots); }
        set { PlayerPrefs.SetInt(NextKey, Mathf.Clamp(value, 1, Slots)); PlayerPrefs.Save(); }
    }

    public static string PathOf(int slot)
        => Path.Combine(Application.persistentDataPath, "save_" + slot + ".json");

    public static bool Has(int slot) => File.Exists(PathOf(slot));

    public static SaveData Read(int slot)
    {
        if (!Has(slot)) return null;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathOf(slot)));
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[存档] 读不出来 " + slot + "：" + e.Message);
            return null;
        }
    }

    public static void Write(int slot, SaveData data)
    {
        try
        {
            File.WriteAllText(PathOf(slot), JsonUtility.ToJson(data, true));
            Debug.Log($"[存档] 已写入第 {slot} 格：{PathOf(slot)}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[存档] 写入失败 " + slot + "：" + e.Message);
        }
    }

    /// <summary>把当前这一刻的游戏状态抓成一份存档数据。</summary>
    public static SaveData Capture()
    {
        var d = new SaveData
        {
            scene = SceneManager.GetActiveScene().name,
            stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };

        var player = GameObject.Find("Player");
        if (player)
        {
            d.px = player.transform.position.x;
            d.py = player.transform.position.y;
            d.pz = player.transform.position.z;
            var fpv = player.GetComponent<FirstPersonView>();
            d.yaw = fpv ? fpv.Yaw : player.transform.eulerAngles.y;
            d.pitch = fpv ? fpv.Pitch : 0f;
        }

        var story = StoryProgress.Instance;
        if (story)
        {
            d.clues = string.Join("\n", story.clues);
            d.fragments = string.Join("\n", story.fragments);
        }

        var dir = Level2Director.Instance;
        if (dir) dir.FillSave(d);

        return d;
    }

    /// <summary>存到“下一格”，返回实际写入的格号。</summary>
    public static int SaveToNextSlot(out SaveData saved)
    {
        int slot = NextSlot;
        saved = Capture();
        Write(slot, saved);
        NextSlot = slot >= Slots ? 1 : slot + 1;      // 第五次保存自动回到第一格
        return slot;
    }

    public static void SaveTo(int slot)
    {
        Write(slot, Capture());
    }

    /// <summary>读取某一格：切场景 → 玩家归位 → 恢复第二关进度。</summary>
    public static void Load(int slot)
    {
        var data = Read(slot);
        if (data == null)
        {
            Debug.LogWarning("[存档] 第 " + slot + " 格是空的");
            return;
        }
        SaveLoader.Get().Begin(data);
    }

    /// <summary>剧情列表在第一关是靠 PlayerPrefs 落盘的，读档时一并写回去。</summary>
    public static void RestoreStory(SaveData d)
    {
        if (!string.IsNullOrEmpty(d.clues)) PlayerPrefs.SetString("child_story_clues", d.clues);
        if (!string.IsNullOrEmpty(d.fragments)) PlayerPrefs.SetString("child_story_fragments", d.fragments);
        PlayerPrefs.Save();

        var story = StoryProgress.Instance;
        if (!story) return;
        story.clues.Clear();
        story.fragments.Clear();
        foreach (var s in d.clues.Split('\n')) if (s.Length > 0) story.clues.Add(s);
        foreach (var s in d.fragments.Split('\n')) if (s.Length > 0) story.fragments.Add(s);
    }
}

/// <summary>跨场景读档：挂在 DontDestroyOnLoad 上，切完场景再落地。</summary>
public sealed class SaveLoader : MonoBehaviour
{
    static SaveLoader instance;
    SaveData pending;

    public static SaveLoader Get()
    {
        if (instance) return instance;
        var go = new GameObject("读档器");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SaveLoader>();
        return instance;
    }

    public void Begin(SaveData data)
    {
        pending = data;

        // 不管当前是不是同一个场景，都重新加载一遍。
        // 否则「读一个更早的存档」只会把进度往上叠（补好的相片不会退回去、摆好的玩偶不会回到背包），
        // 玩家看到的就是“读了档但还在原来的进度上”。
        Level2Director.PendingRestore = data.scene == "Level 2" ? data : null;

        SceneManager.sceneLoaded += OnLoaded;
        SceneManager.LoadScene(data.scene);
    }

    void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnLoaded;
        StartCoroutine(ApplyNextFrame());
    }

    IEnumerator ApplyNextFrame()
    {
        yield return null;
        yield return null;
        var data = pending;
        pending = null;
        if (data == null) yield break;

        SaveSystem.RestoreStory(data);
        PlacePlayer(data);

        // 第二关的谜题进度已经在 Level2Director.Start() 里用 PendingRestore 恢复过了，
        // 这里只负责把玩家摆回存档里的位置；第一关的机关状态本来就不进存档。
        Debug.Log($"[存档] 已读取 {data.scene} @ {L2.Fmt(new Vector3(data.px, data.py, data.pz))}");
    }

    static void PlacePlayer(SaveData data)
    {
        var player = GameObject.Find("Player");
        if (!player) return;

        Vector3 pos = new Vector3(data.px, data.py, data.pz);
        var rot = Quaternion.Euler(0f, data.yaw, 0f);
        var rb = player.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.position = pos;
            rb.rotation = rot;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        player.transform.SetPositionAndRotation(pos, rot);
        var fpv = player.GetComponent<FirstPersonView>();
        if (fpv) fpv.SetLook(data.yaw, data.pitch);
        Physics.SyncTransforms();
    }
}
