using System.Collections.Generic;
using UnityEngine;
[DefaultExecutionOrder(-20)]
public sealed class PlayerInventory : MonoBehaviour
{
    public bool IsOpen {get;private set;}
    public float reach=1.7f;
    readonly List<PickupItem> items=new List<PickupItem>();
    int selected;
    Vector2 scroll;
    public int Count => items.Count;
    Component target;
    string prompt="",notice="";
    float noticeUntil;
    Font font;
    void Awake(){font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},24);}
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.F))SetOpen(!IsOpen);
        if(IsOpen&&Input.GetKeyDown(KeyCode.Escape))SetOpen(false);
        if(Input.GetKeyDown(KeyCode.G))Drop();
        target=null;prompt="";
        if(IsOpen||Cursor.lockState!=CursorLockMode.Locked)return;
        var cam=Camera.main;if(!cam)return;
        float nearest=reach;
        foreach(var hit in Physics.RaycastAll(cam.transform.position,cam.transform.forward,reach,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.transform.IsChildOf(transform)||hit.distance>=nearest)continue;
            nearest=hit.distance;target=null;prompt="";
            var door=hit.collider.GetComponentInParent<InteractiveDoor>();
            var item=hit.collider.GetComponentInParent<PickupItem>();
            var push=hit.collider.GetComponentInParent<PushProp>();
            if(door){target=door;prompt="E 开门 / 关门";}
            else if(item){target=item;prompt="E 拾取 "+item.displayName;}
            else if(push)prompt="按住 Q + WASD 推动";
        }
        if(Input.GetKeyDown(KeyCode.E))
        {
            if(target is InteractiveDoor door)door.Toggle(transform.position);
            else if(target is PickupItem item)Take(item);
        }
    }
    public void Take(PickupItem item)
    {
        if (!item || items.Contains(item)) return;
        items.Add(item);item.gameObject.SetActive(false);selected=items.Count-1;
        GetComponent<PlayerController>().PlayPickup();
    }
    void SetOpen(bool value){IsOpen=value;Cursor.lockState=value?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=value;}
    public void Drop()
    {
        if(items.Count==0)return;
        selected=Mathf.Clamp(selected,0,items.Count-1);var item=items[selected];var cam=Camera.main;if(!item||!cam)return;
        var renderer=item.GetComponentInChildren<Renderer>(true);float radius=renderer?Mathf.Clamp(renderer.bounds.extents.magnitude,.08f,.3f):.12f;
        Vector3 direction=Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up).normalized;
        Vector3 origin=cam.transform.position;float distance=.65f;
        foreach(var hit in Physics.SphereCastAll(origin,radius,direction,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.collider.transform.IsChildOf(transform))distance=Mathf.Min(distance,hit.distance-.03f);
        if(distance<.3f){notice="前方空间不足，无法丢弃";noticeUntil=Time.unscaledTime+2;return;}
        item.transform.position=origin+direction*distance;item.gameObject.SetActive(true);
        var rb=item.GetComponent<Rigidbody>();if(!rb)rb=item.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic=false;rb.useGravity=true;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.velocity=Vector3.zero;
        items.RemoveAt(selected);selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
    }
    void OnGUI()
    {
        var style=new GUIStyle(GUI.skin.box){font=font,fontSize=21,alignment=TextAnchor.MiddleCenter};
        if(IsOpen)
        {
            float width=Mathf.Min(440,Screen.width-32),height=Mathf.Min(420,Screen.height-32);
            float x=(Screen.width-width)*.5f,y=(Screen.height-height)*.5f;
            Color previous=GUI.color;GUI.color=new Color(.07f,.08f,.1f,.96f);
            GUI.DrawTexture(new Rect(x,y,width,height),Texture2D.whiteTexture);GUI.color=previous;
            GUI.Label(new Rect(x+12,y+12,width-24,38),"背包   F 关闭   G 丢弃",style);
            scroll=GUI.BeginScrollView(new Rect(x+20,y+65,width-40,height-85),scroll,new Rect(0,0,width-65,Mathf.Max(height-85,items.Count*38)));
            if(items.Count==0)GUI.Label(new Rect(0,20,width-65,40),"背包为空",style);
            for(int i=0;i<items.Count;i++)
                if(GUI.Button(new Rect(0,i*38,width-65,34),(i==selected?"▶ ":"")+items[i].displayName,style))selected=i;
            GUI.EndScrollView();
        }
        else if(prompt!="")GUI.Box(new Rect(Screen.width*.5f-170,Screen.height*.65f,340,42),prompt,style);
        if(Time.unscaledTime<noticeUntil)GUI.Box(new Rect(Screen.width*.5f-180,Screen.height*.5f,360,42),notice,style);
    }
    void OnDestroy(){if(font)Destroy(font);}
}
