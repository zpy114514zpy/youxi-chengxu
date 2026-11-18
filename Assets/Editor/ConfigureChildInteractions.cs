using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;
using System.IO;
[InitializeOnLoad]
static class ConfigureChildInteractions
{
 static ConfigureChildInteractions(){EditorApplication.delayCall+=Apply;}
 static void Apply(){if(EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Temp/ChildInteractions.done"))return;
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!="Assets/Level 1.unity")return;
 int doors=0,items=0,pushes=0;
 foreach(var root in scene.GetRootGameObjects()){
  var r=root.GetComponentInChildren<MeshRenderer>();if(!r)continue;var p=r.bounds.center;
  if(p.x<-26||p.x>-15||p.z<-109||p.z>-89)continue;
  if(root.name.StartsWith("BP_Door_Inside")||root.name.StartsWith("BP_Door_Outside")){
   if(root.GetComponent<InteractiveDoor>())continue;var door=root.AddComponent<InteractiveDoor>();
   foreach(var c in root.GetComponentsInChildren<MeshCollider>())if(c.enabled)c.convex=true;
   var rb=root.GetComponent<Rigidbody>();if(!rb)rb=root.AddComponent<Rigidbody>();rb.isKinematic=true;
   var bounds=r.bounds;
   Vector3 a=bounds.center,b=bounds.center;if(bounds.size.x>bounds.size.z){a.x=bounds.min.x;b.x=bounds.max.x;}else{a.z=bounds.min.z;b.z=bounds.max.z;}
   door.hingeLocal=root.transform.InverseTransformPoint(Vector3.Distance(a,root.transform.position)<Vector3.Distance(b,root.transform.position)?a:b);doors++;
  }
  if((root.name.StartsWith("SM_Book")&&!root.name.Contains("Set")&&!root.name.Contains("Shelf"))||root.name.StartsWith("SM_Toy")){
   if(root.GetComponent<PickupItem>())continue;var item=root.AddComponent<PickupItem>();item.displayName=root.name.StartsWith("SM_Book")?"书籍":"玩具";
   foreach(var c in root.GetComponentsInChildren<MeshCollider>())if(c.enabled)c.convex=true;items++;
  }
  if(root.name.StartsWith("SM_Box") && r.bounds.size.y>.15f && r.bounds.size.y<.65f && p.y>3.2f&&p.y<3.8f){
   if(root.GetComponent<PushProp>())continue;
   foreach(var c in root.GetComponentsInChildren<MeshCollider>())if(c.enabled)c.convex=true;
   var rb=root.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.Interpolate;root.AddComponent<PushProp>();pushes++;
  }
 }
 var player=Object.FindObjectOfType<PlayerController>();if(player){player.pushKey=KeyCode.Q;player.pickupKey=KeyCode.None;EditorUtility.SetDirty(player);}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/ChildInteractions.done",$"doors={doors} items={items} pushProps={pushes}");Debug.Log("CHILD interactions configured");
 }
}
