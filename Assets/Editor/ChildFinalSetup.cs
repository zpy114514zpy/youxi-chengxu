using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
[InitializeOnLoad] static class ChildFinalSetup {
 static ChildFinalSetup(){EditorApplication.delayCall+=Apply;}
 static void Apply(){if(EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Temp/ChildFinalSetup.done"))return;
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!="Assets/Level 1.unity")return;
 if(!GameObject.Find("Bedroom Push Box")){
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SM_Box_A3.prefab");
 var box=(GameObject)PrefabUtility.InstantiatePrefab(prefab);box.name="Bedroom Push Box";box.transform.localScale=Vector3.one*1.6f;
 var render=box.GetComponentInChildren<MeshRenderer>();
 foreach(var c in box.GetComponentsInChildren<Collider>())c.enabled=false;
 var filter=render.GetComponent<MeshFilter>();var collider=render.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;collider.convex=true;
 var rb=box.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.Interpolate;box.AddComponent<PushProp>();
 box.transform.position=new Vector3(-20.5f,3.3f,-93.7f);Physics.SyncTransforms();box.transform.position+=Vector3.up*(3.21f-render.bounds.min.y);Physics.SyncTransforms();
 bool blocked=false;foreach(var c in Physics.OverlapBox(render.bounds.center,render.bounds.extents*.95f))if(!c.transform.IsChildOf(box.transform))blocked=true;
 if(blocked){Object.DestroyImmediate(box);Debug.LogWarning("Bedroom box location obstructed; kept existing six push props.");}
 }
 var player=Object.FindObjectOfType<PlayerController>();player.standingEyeHeight=.9f;EditorUtility.SetDirty(player);
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/ChildFinalSetup.done","saved");
 }
}
