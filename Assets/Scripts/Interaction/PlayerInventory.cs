using System.Collections.Generic;
using UnityEngine;
[DefaultExecutionOrder(-20)]
public sealed class PlayerInventory : MonoBehaviour
{
    public bool IsOpen {get;private set;}
    public float reach=1.7f;
    readonly List<PickupItem> items=new List<PickupItem>();
    int selected;
    int tab;
    Vector2 scroll;
    public int Count => items.Count;
    public PickupItem Selected => items.Count==0 ? null : items[Mathf.Clamp(selected,0,items.Count-1)];
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
        if(IsOpen||Cursor.lockState!=CursorLockMode.Locked||StoryProgress.Instance && StoryProgress.Instance.IsReading)return;
        var cam=Camera.main;if(!cam)return;
        float nearest=reach;
        foreach(var hit in Physics.RaycastAll(cam.transform.position,cam.transform.forward,reach,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.transform.IsChildOf(transform)||hit.distance>=nearest)continue;
            nearest=hit.distance;target=null;prompt="";
            var door=hit.collider.GetComponentInParent<InteractiveDoor>();
            var placement=hit.collider.GetComponentInParent<StoryPlacement>();
            var readable=hit.collider.GetComponentInParent<ReadableClue>();
            var item=hit.collider.GetComponentInParent<PickupItem>();
            var push=hit.collider.GetComponentInParent<PushProp>();
            if(placement){target=placement;prompt=placement.filled?"已归位":("E 摆放 "+placement.DisplayName);}
            else if(readable){target=readable;prompt="E 阅读 "+readable.title;}
            else if(door){target=door;prompt="E 开门 / 关门";}
            else if(item){target=item;prompt="E 拾取 "+item.displayName;}
            else if(push)prompt="按住 Q + WASD 推动";
        }
        if(Input.GetKeyDown(KeyCode.E))
        {
            if(target is StoryPlacement placement)
            {
                if(!placement.Place(this))ShowNotice(placement.filled?"这里已经摆好了":"请按绘本顺序选择对应玩偶，再按 E 摆放");
            }
            else if(target is ReadableClue readable)readable.Read();
            else if(target is InteractiveDoor door)
            {
                door.Toggle(transform.position);
                var intro=door.GetComponent<DoorRoomIntro>();if(intro)intro.OnOpened();
            }
            else if(target is PickupItem item)Take(item);
        }
    }
    public void Take(PickupItem item)
    {
        if (!item || items.Contains(item)) return;
        var fragment=item.GetComponent<EnvelopeFragment>();
        if(fragment)
        {
            if(StoryProgress.Instance)StoryProgress.Instance.AddFragment(fragment.fragmentName,fragment.text);
            item.gameObject.SetActive(false);
            GetComponent<PlayerController>().PlayPickup();
            return;
        }
        items.Add(item);item.gameObject.SetActive(false);selected=items.Count-1;
        GetComponent<PlayerController>().PlayPickup();
    }
    public PickupItem ConsumeSelected(string id)
    {
        var item=Selected;
        if(!item||item.storyId!=id)return null;
        items.Remove(item);selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
        return item;
    }
    void ShowNotice(string value){notice=value;noticeUntil=Time.unscaledTime+2.5f;}
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
            float width=Mathf.Min(680,Screen.width-32),height=Mathf.Min(520,Screen.height-32);
            float x=(Screen.width-width)*.5f,y=(Screen.height-height)*.5f;
            Color previous=GUI.color;GUI.color=new Color(.07f,.08f,.1f,.96f);
            GUI.DrawTexture(new Rect(x,y,width,height),Texture2D.whiteTexture);GUI.color=previous;
            GUI.Label(new Rect(x+12,y+12,width-24,38),"背包   F 关闭   G 丢弃所选道具",style);
            string[] tabs={"道具","信封碎片","线索记录"};
            for(int i=0;i<3;i++)if(GUI.Button(new Rect(x+20+i*(width-40)/3,y+58,(width-46)/3,38),tabs[i],style))tab=i;
            if(tab==0)
            {
                scroll=GUI.BeginScrollView(new Rect(x+20,y+110,width-40,height-132),scroll,new Rect(0,0,width-65,Mathf.Max(height-132,items.Count*48+80)));
                if(items.Count==0)GUI.Label(new Rect(0,20,width-65,40),"背包为空",style);
                for(int i=0;i<items.Count;i++)if(GUI.Button(new Rect(0,i*48,width-65,42),(i==selected?"▶ ":"")+items[i].displayName,style))selected=i;
                if(Selected)GUI.Label(new Rect(0,items.Count*48+10,width-65,80),Selected.description,style);
                GUI.EndScrollView();
            }
            else if(tab==1)
            {
                var story=StoryProgress.Instance;
                string text="信件拼接预览\n\n";
                for(int i=1;i<=3;i++)
                {
                    string name="信封碎片"+new[]{"","①","②","③"}[i];
                    text+=(story && story.fragments.Contains(name))?(i==1?"① 长大以后我要一直开心\n":"已收集 "+name+"\n"):("□ "+name+" 尚未找到\n");
                }
                GUI.Label(new Rect(x+32,y+123,width-64,height-150),text,new GUIStyle(style){alignment=TextAnchor.UpperLeft,wordWrap=true});
            }
            else
            {
                var clues=StoryProgress.Instance?StoryProgress.Instance.clues:null;
                int count=clues==null?0:clues.Count;
                scroll=GUI.BeginScrollView(new Rect(x+20,y+110,width-40,height-132),scroll,new Rect(0,0,width-65,Mathf.Max(height-132,count*90)));
                if(count==0)GUI.Label(new Rect(0,20,width-65,40),"尚未阅读线索",style);
                for(int i=0;i<count;i++)GUI.Label(new Rect(0,i*90,width-65,84),clues[i],new GUIStyle(style){alignment=TextAnchor.MiddleLeft,wordWrap=true});
                GUI.EndScrollView();
            }
        }
        else if(prompt!="")GUI.Box(new Rect(Screen.width*.5f-170,Screen.height*.65f,340,42),prompt,style);
        if(Time.unscaledTime<noticeUntil)GUI.Box(new Rect(Screen.width*.5f-180,Screen.height*.5f,360,42),notice,style);
    }
    void OnDestroy(){if(font)Destroy(font);}
}
