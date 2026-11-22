using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>临时：看到 final_done.txt 就退出 Play。</summary>
[InitializeOnLoad]
internal static class __FinalRun
{
    const string Dir = "C:/Users/zhaop/WorkBuddy/spaces/2026_11_20_23-56-45";
    static string Done => Dir + "/final_done.txt";

    static __FinalRun() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.isCompiling) return;
        if (EditorApplication.isPlaying && File.Exists(Done))
        {
            EditorApplication.ExitPlaymode();
            File.Delete(Done);
        }
    }
}
