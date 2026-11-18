using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
[InitializeOnLoad] static class ChildInteractionAudit {
 static ChildInteractionAudit(){EditorApplication.delayCall+=Run;}
 static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode)return;var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!="Assets/Level 1.unity")return;
 var lines=new System.Collections.Generic.List<string>();
 foreach(var go in scene.GetRootGameObjects())if(go.name.Contains("Door")||go.name.StartsWith("SM_Box")||go.name.StartsWith("SM_Book")){
 var r=go.GetComponentInChildren<Renderer>();if(!r)continue;var p=r.bounds.center;if(p.x<-26||p.x>-15||p.z<-109||p.z>-89)continue;
 lines.Add(go.name+" root="+go.transform.position+" bounds="+r.bounds+"\n"+string.Join("\n",go.GetComponentsInChildren<Renderer>().Select(x=>"  "+x.name+" bounds="+x.bounds)));
 }
 File.WriteAllLines("Temp/ChildInteractionAudit.txt",lines);
 }
}
