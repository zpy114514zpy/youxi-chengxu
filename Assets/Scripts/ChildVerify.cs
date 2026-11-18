using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class ChildVerify:MonoBehaviour {
 System.Text.StringBuilder result=new System.Text.StringBuilder();
 void Log(string s){result.AppendLine(s);File.WriteAllText("Temp/ChildVerify.txt",result.ToString());}
 IEnumerator Start(){yield return new WaitForSeconds(1);
 var p=FindObjectOfType<PlayerController>();var rb=p.GetComponent<Rigidbody>();var cap=p.GetComponentInChildren<CapsuleCollider>();var cam=Camera.main;
 Log("eye above feet="+(cam.transform.position.y-cap.bounds.min.y));
 p.enabled=false;var origin=p.transform.position;var height=cap.height;
 yield return new WaitForEndOfFrame();Dump(p,"before");p.SetCrouching(true);yield return new WaitForSeconds(.4f);Log("crouch="+p.IsCrouched+" height="+cap.height+" eye="+(cam.transform.position.y-cap.bounds.min.y));yield return new WaitForEndOfFrame();Capture("crouch");CaptureSide(p);Dump(p,"after");
 var block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.name="Temporary head clearance test";block.transform.position=new Vector3(cap.bounds.center.x,cap.bounds.min.y+height*Mathf.Abs(cap.transform.lossyScale.y)-.05f,cap.bounds.center.z);block.transform.localScale=new Vector3(.6f,.1f,.6f);Physics.SyncTransforms();p.SetCrouching(false);Log("blocked standing remains crouched="+p.IsCrouched);Destroy(block);yield return null;
 p.SetCrouching(false);yield return new WaitForSeconds(.4f);Log("stand="+(!p.IsCrouched)+" height="+cap.height);Capture("standing");
 Log("DONE");p.enabled=true;
 }
 void Dump(PlayerController p,string label){foreach(var t in p.GetComponentInChildren<Animator>().GetComponentsInChildren<Transform>())if(t.name=="mixamorig:Hips"||t.name.EndsWith("UpLeg")||t.name.EndsWith("Leg")||t.name.EndsWith("Foot"))Log(label+" "+t.name+" "+t.position);}
 void CaptureSide(PlayerController player){
 var skin=player.GetComponentsInChildren<SkinnedMeshRenderer>();
 var full=System.Array.Find(skin,r=>r.name=="Full character shadow");var visible=System.Array.Find(skin,r=>r!=full);
 if(!full||!visible)return;
 visible.enabled=false;full.enabled=true;full.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
 var obj=new GameObject("temporary pose camera");var c=obj.AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=false;
 var center=player.GetComponentInChildren<CapsuleCollider>().bounds.center;c.transform.position=center+new Vector3(1.1f,.35f,.6f);c.transform.LookAt(center);c.fieldOfView=70;
 var rt=new RenderTexture(700,700,24);c.targetTexture=rt;c.Render();var active=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(700,700,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,700,700),0,0);t.Apply();File.WriteAllBytes("Temp/Child-crouch-pose.png",t.EncodeToPNG());RenderTexture.active=active;Destroy(t);Destroy(rt);Destroy(obj);
 visible.enabled=true;full.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
 }
 void Capture(string name){var c=Camera.main;var rt=new RenderTexture(960,540,24);var old=c.targetTexture;c.targetTexture=rt;c.Render();var active=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(960,540,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,960,540),0,0);t.Apply();File.WriteAllBytes("Temp/Child-"+name+".png",t.EncodeToPNG());c.targetTexture=old;RenderTexture.active=active;Destroy(t);Destroy(rt);}
}
