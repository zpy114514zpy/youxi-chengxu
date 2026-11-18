using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
[InitializeOnLoad] static class ChildVerifyLaunch {
 static ChildVerifyLaunch(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&File.Exists("Temp/ChildVerify.request")){File.Delete("Temp/ChildVerify.request");new GameObject("Temporary interaction verifier").AddComponent<ChildVerify>();}};}
}
