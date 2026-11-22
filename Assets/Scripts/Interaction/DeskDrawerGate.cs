using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第一个玩偶归位谜题没完成之前，书桌【中间】那个抽屉拉不开，左右两侧的抽屉照常能开。
/// 谜题一解开、剧情把中间抽屉弹开时，由 DrawerFragmentReveal 解除这把锁。
/// 运行时装配，不改场景文件。
/// </summary>
[DisallowMultipleComponent]
public sealed class DeskDrawerGate : MonoBehaviour
{
    [TextArea] public string lockedHint = "中间这个抽屉锁着。先把四个玩偶都放回床上吧。";

    BedroomToyPuzzle puzzle;
    FurnitureOpenable middle;
    bool lockedOnce;
    float solvedAt = -1f;

    void Awake()
    {
        puzzle = GetComponent<BedroomToyPuzzle>();
    }

    IEnumerator Start()
    {
        // DrawerSetup 是在 AfterSceneLoad 里才给抽屉挂 FurnitureOpenable 的，可能比 Start 晚。
        // 这里先找抽屉本体，再等它身上出现可交互组件，最多等 12 秒。
        List<Transform> drawers = null;
        float nextScan = 0f;
        float timeout = 12f;
        while (timeout > 0f)
        {
            if (puzzle && puzzle.Solved) yield break;          // 已经解开了就不用锁

            // 第一次找，以及一直没找到时每秒重找一次（可能模型刚被激活/刚导入完）。
            if (drawers == null || (drawers.Count == 0 && Time.time >= nextScan))
            {
                drawers = FindDeskDrawers();
                nextScan = Time.time + 1f;
            }
            if (drawers.Count > 0)
            {
                // 从左到右排好序：3 个抽屉时中间那个就是正中间的；2 个时取靠右的一只。
                Transform target = drawers[drawers.Count / 2];
                var openable = target.GetComponent<FurnitureOpenable>();
                if (!openable) openable = target.GetComponentInParent<FurnitureOpenable>();
                if (openable)
                {
                    middle = openable;
                    middle.LockByStory(lockedHint);
                    lockedOnce = true;
                    Debug.Log("[抽屉] 书桌中间抽屉已上锁：" + target.name, this);
                    yield break;
                }
            }

            timeout -= .25f;
            yield return new WaitForSeconds(.25f);
        }

        if (!lockedOnce)
            Debug.LogWarning("[抽屉] 没找到书桌中间的抽屉，无法上锁（左右两侧抽屉不受影响）。", this);
    }

    void Update()
    {
        if (!puzzle || !puzzle.Solved)
        {
            solvedAt = -1f;
            return;
        }
        if (solvedAt < 0f) solvedAt = Time.time;

        // 兜底：万一剧情那边没能把抽屉弹开，20 秒后也放开，别让玩家永远打不开中间的抽屉。
        if (lockedOnce && middle && middle.Locked && Time.time - solvedAt > 20f)
        {
            middle.UnlockByStory();
            Debug.Log("[抽屉] 谜题已完成，兜底解除中间抽屉的锁。", this);
        }
    }

    List<Transform> FindDeskDrawers()
    {
        Vector3 near = puzzle && puzzle.fragmentObject
            ? puzzle.fragmentObject.transform.position
            : transform.position;
        return DrawerFragmentReveal.FindDeskDrawersNear(near);
    }
}
