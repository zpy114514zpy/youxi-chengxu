using System.Collections.Generic;
using UnityEngine;
[DefaultExecutionOrder(-20)]
public sealed class PlayerInventory : MonoBehaviour
{
    public bool IsOpen {get;private set;}
    public float reach=1.7f;
    [Range(1,20)] public int capacity=20;
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
    Texture2D frame;
    StoryPlacement[] placements;
    float placementRefresh;
    void Awake(){font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},32);frame=Resources.Load<Texture2D>("StoryUI/inventory-frame");}
    void Update()
    {
        target=null;prompt="";
        if(UiInputBlocker.Active)return;
        if(StoryProgress.Instance && StoryProgress.Instance.IsReading)return;
        if(Input.GetKeyDown(KeyCode.B))SetOpen(!IsOpen);
        if(IsOpen&&Input.GetKeyDown(KeyCode.Escape))SetOpen(false);
        if(Input.GetKeyDown(KeyCode.G))Drop();
        if(IsOpen||Cursor.lockState!=CursorLockMode.Locked)return;
        var cam=Camera.main;if(!cam)return;
        float nearest=reach;
        // 用带半径的球扫而不是细射线：信封碎片、相册这类又薄又小的东西，
        // 细射线很容易从边上擦过去，玩家明明瞄准了却按不出提示。
        foreach(var hit in Physics.SphereCastAll(cam.transform.position,.085f,cam.transform.forward,reach,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.transform.IsChildOf(transform)||hit.distance>=nearest)continue;
            var door=hit.collider.GetComponentInParent<InteractiveDoor>();
            var furniture=hit.collider.GetComponentInParent<FurnitureOpenable>();
            var placement=hit.collider.GetComponentInParent<StoryPlacement>();
            var readable=hit.collider.GetComponentInParent<ReadableClue>();
            var item=hit.collider.GetComponentInParent<PickupItem>();
            // 场景里的装饰玩具、书、已经摆好的玩偶都带 PickupItem：不是当前谜题要用的东西，
            // 不允许拾取，连“E 拾取 玩具”的提示也不要出现。
            if(item && !IsUseful(item))item=null;
            var push=hit.collider.GetComponentInParent<PushProp>();
            var keypad=hit.collider.GetComponentInParent<PasswordBoxPuzzle>();
            var stool=hit.collider.GetComponentInParent<StoolStep>();
            var pile=hit.collider.GetComponentInParent<BlockPuzzle>();
            var spot=hit.collider.GetComponentInParent<TidySpot>();
            var clock=hit.collider.GetComponentInParent<ClockDial>();
            var deposit=hit.collider.GetComponentInParent<FragmentDeposit>();
            if(spot && spot.Prompt.Length==0)spot=null;
            if(!placement && !readable && !door && !furniture && !item && !keypad && !stool && !push && !pile && !spot && !clock && !deposit)continue;
            // 箱子打开后箱体还会挡住射线：它不再是交互目标，直接跳过，让箱口里的碎片能被瞄准。
            if(keypad && keypad.Unlocked && !item)continue;
            nearest=hit.distance;target=null;prompt="";
            if(placement){target=placement;prompt=placement.filled?"已归位":("E 摆放 "+placement.DisplayName);}
            else if(readable){target=readable;prompt="E 阅读 "+readable.title;}
            else if(door)
            {
                target=door;
                var gate=door.GetComponent<DoorLock>();
                bool exitDoor=door.GetComponentInParent<ExitBoundary>();
                // 第一关没做完之前，除了大门以外的房门都不给开，只能“查看”。
                if(RoomDoorGate.Blocks(door))prompt=RoomDoorGate.Prompt;
                else prompt=exitDoor?"E 查看门外白雾":(gate&&gate.LockedReason(this)!=""?gate.LockedPrompt:"E 开门 / 关门");
            }
            else if(furniture){target=furniture;prompt=furniture.Prompt;}
            else if(item){target=item;prompt="E 拾取 "+item.displayName;}
            else if(pile){target=pile;prompt=pile.Prompt;}
            else if(keypad){target=keypad;prompt=keypad.Prompt;}
            else if(stool){target=stool;prompt=stool.Prompt;}
            else if(push)prompt="按住 Q + WASD 推动";
            else if(spot){target=spot;prompt=spot.Prompt;}
            else if(clock){target=clock;prompt=clock.Prompt;}
            else if(deposit){target=deposit;prompt=deposit.Prompt;}
        }
        if(Input.GetKeyDown(KeyCode.E))
        {
            if(target is StoryPlacement placement)
            {
                if(!placement.Place(this))ShowNotice(placement.filled?"这里已经摆好了":"请选中对应的玩偶，再按 E 摆放");
            }
            else if(target is ReadableClue readable)readable.Read();
            else if(target is InteractiveDoor door)
            {
                var boundary=door.GetComponentInParent<ExitBoundary>();
                if(RoomDoorGate.Blocks(door))RoomDoorGate.Deny();
                else if(boundary)boundary.Deny();
                else
                {
                    // 二号房间的门：积木谜题没解开、或者还没拿到钥匙，都不允许打开。
                    var gate=door.GetComponent<DoorLock>();
                    if(gate && !door.IsOpen && !gate.TryUnlock(this)) { }
                    else
                    {
                        door.Toggle(transform.position);
                        var intro=door.GetComponent<DoorRoomIntro>();if(intro && door.IsOpening)intro.OnOpened();
                    }
                }
            }
            else if(target is FurnitureOpenable furniture)furniture.Toggle(transform.position);
            else if(target is PasswordBoxPuzzle keypad)keypad.Interact();
            else if(target is BlockPuzzle blockPile)blockPile.Interact(this);
            else if(target is StoolStep stool)stool.Interact();
            else if(target is PickupItem item)Take(item);
            else if(target is TidySpot tidySpot)tidySpot.Place(this);
            else if(target is ClockDial clockDial)clockDial.Tick();
            else if(target is FragmentDeposit fragmentDeposit)fragmentDeposit.Place();
        }
    }
    /// <summary>
    /// 这个东西该不该允许拾取：
    /// 只放行当前剧情要用的——信封碎片、积木、二号房间钥匙，以及“还没摆好”的归位谜题要的道具。
    /// 其余（装饰玩具、书、已经摆到床上的玩偶）都不再提示拾取。
    /// </summary>
    bool IsUseful(PickupItem item)
    {
        if(!item)return false;
        string id=item.storyId??"";
        if(id.StartsWith("letter_fragment")||id.StartsWith("block_")||id=="room2_key")return true;
        if(id.StartsWith("tidy_"))return true;
        if(id.Length==0)return false;
        if(placements==null||Time.unscaledTime>placementRefresh)
        {
            placements=FindObjectsOfType<StoryPlacement>();
            placementRefresh=Time.unscaledTime+1.5f;
        }
        foreach(var placement in placements)
            if(placement && !placement.filled && placement.requiredItemId==id)return true;
        return false;
    }

    public bool HasItem(string id)
    {
        if(string.IsNullOrEmpty(id))return false;
        foreach(var item in items)if(item && item.storyId==id)return true;
        return false;
    }

    /// <summary>取出一件道具（把对象本身交出去，归位摆放时要用）。</summary>
    public PickupItem TakeItem(string id)
    {
        if(string.IsNullOrEmpty(id))return null;
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i];
            if(!item||item.storyId!=id)continue;
            items.RemoveAt(i);
            selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
            return item;
        }
        return null;
    }

    public bool ConsumeItem(string id)
    {
        if(string.IsNullOrEmpty(id))return false;
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i];
            if(!item||item.storyId!=id)continue;
            items.RemoveAt(i);
            selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
            return true;
        }
        return false;
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
        if(items.Count>=capacity){ShowNotice("背包已满");return;}
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
    /// <summary>背包里有没有积木块（积木谜题用）。</summary>
    public bool HasBlock
    {
        get
        {
            foreach(var item in items)if(item&&item.storyId!=null&&item.storyId.StartsWith("block_"))return true;
            return false;
        }
    }
    /// <summary>取出一块积木（积木谜题放置时用），取不到返回 null。</summary>
    public PickupItem TakeBlock()
    {
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i];
            if(!item||item.storyId==null||!item.storyId.StartsWith("block_"))continue;
            items.RemoveAt(i);
            selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
            return item;
        }
        return null;
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

        // 谜题关键道具（还没摆好的玩偶、信封碎片、积木、钥匙）丢出来时直接落到地面上，
        // 不给刚体自由下落：否则很容易滚进柜子里、或者穿过地板，玩家就再也找不到了。
        if(IsUseful(item))
        {
            Vector3 spot=origin+direction*distance;
            if(Physics.Raycast(spot+Vector3.up*.8f,Vector3.down,out var ground,3f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                item.transform.position=ground.point+Vector3.up*.03f;
                var body=item.GetComponent<Rigidbody>();
                if(body){body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.useGravity=false;body.isKinematic=true;}
                item.gameObject.SetActive(true);
                items.RemoveAt(selected);selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
                return;
            }
        }

        item.transform.position=origin+direction*distance;item.gameObject.SetActive(true);
        var rb=item.GetComponent<Rigidbody>();if(!rb)rb=item.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic=false;rb.useGravity=true;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.velocity=Vector3.zero;
        items.RemoveAt(selected);selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
    }
    void OnGUI()
    {
        var style=new GUIStyle(GUI.skin.box){font=font,fontSize=17,alignment=TextAnchor.MiddleCenter,wordWrap=true};
        if(IsOpen)DrawInventory();
        else if(prompt!="")
        {
            float width=Mathf.Min(520,Screen.width*.8f);
            GUI.Box(new Rect((Screen.width-width)*.5f,Screen.height*.86f,width,50),prompt,style);
        }
        if(Time.unscaledTime<noticeUntil)
        {
            float width=Mathf.Min(520,Screen.width*.8f);
            GUI.Box(new Rect((Screen.width-width)*.5f,Screen.height*.5f,width,50),notice,style);
        }
    }
    GUIStyle TextStyle(int size,TextAnchor alignment,Color color)
    {
        var result=new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=alignment,wordWrap=true};
        result.normal.textColor=color;return result;
    }
    void Tint(Rect rect,Color color)
    {
        var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;
    }
    void DrawInventory()
    {
        const float w=1536f,h=1024f;
        float scale=Mathf.Min(Screen.width*.94f/w,Screen.height*.94f/h);
        var original=GUI.matrix;
        GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-w*scale)*.5f,(Screen.height-h*scale)*.5f,0),Quaternion.identity,new Vector3(scale,scale,1));
        if(frame)GUI.DrawTexture(new Rect(0,0,w,h),frame,ScaleMode.StretchToFill,true);
        else {Tint(new Rect(0,0,w,h),new Color(.025f,.07f,.11f,.97f));}
        var cyan=new Color(.35f,.95f,1f);
        GUI.Label(new Rect(480,55,580,76),"背  包",TextStyle(52,TextAnchor.MiddleCenter,cyan));
        var tabs=new[]{"道具","信封碎片","线索记录"};
        for(int i=0;i<tabs.Length;i++)
        {
            var rect=new Rect(130+i*267,196,240,58);
            Tint(rect,tab==i?new Color(.03f,.48f,.60f,.78f):new Color(.02f,.13f,.19f,.83f));
            if(GUI.Button(rect,tabs[i],TextStyle(31,TextAnchor.MiddleCenter,Color.white))){tab=i;scroll=Vector2.zero;}
        }
        if(tab==0)DrawSlots();else if(tab==1)DrawFragments();else DrawClues();
        DrawDetail();
        GUI.Label(new Rect(128,890,820,52),"容量  "+items.Count+" / "+capacity+"     B 关闭    G 丢弃所选道具",TextStyle(28,TextAnchor.MiddleLeft,cyan));
        if(GUI.Button(new Rect(1407,70,64,55),"×",TextStyle(48,TextAnchor.MiddleCenter,cyan)))SetOpen(false);
        GUI.matrix=original;
    }
    void DrawSlots()
    {
        var cyan=new Color(.27f,.82f,.91f);
        for(int i=0;i<capacity;i++)
        {
            int col=i%4,row=i/4;
            var rect=new Rect(144+col*194,282+row*118,165,104);
            Tint(rect,new Color(.01f,.03f,.09f,i==selected&&i<items.Count?.93f:.72f));
            var edge=i==selected&&i<items.Count?new Color(.48f,1f,1f):cyan;
            Tint(new Rect(rect.x,rect.y,rect.width,3),edge);Tint(new Rect(rect.x,rect.yMax-3,rect.width,3),edge);
            Tint(new Rect(rect.x,rect.y,3,rect.height),edge);Tint(new Rect(rect.xMax-3,rect.y,3,rect.height),edge);
            if(i>=items.Count)continue;
            var item=items[i];if(!item)continue;
            if(item.icon)GUI.DrawTexture(new Rect(rect.x+53,rect.y+10,60,54),item.icon.texture,ScaleMode.ScaleToFit,true);
            else GUI.Label(new Rect(rect.x+8,rect.y+7,rect.width-16,53),item.displayName.Length>0?item.displayName.Substring(0,1):"?",TextStyle(42,TextAnchor.MiddleCenter,cyan));
            GUI.Label(new Rect(rect.x+7,rect.y+67,rect.width-14,31),item.displayName,TextStyle(21,TextAnchor.MiddleCenter,Color.white));
            if(GUI.Button(rect,GUIContent.none,GUIStyle.none))selected=i;
        }
    }
    void DrawFragments()
    {
        var story=StoryProgress.Instance;
        GUI.Label(new Rect(145,290,770,75),"信件拼接预览",TextStyle(39,TextAnchor.MiddleCenter,Color.white));
        var names=new[]{"信封碎片①","信封碎片②","信封碎片③"};
        for(int i=0;i<names.Length;i++)
        {
            bool found=story&&story.fragments.Contains(names[i]);
            var r=new Rect(153+i*250,410,222,260);
            Tint(r,new Color(.02f,.12f,.18f,.88f));
            GUI.Label(new Rect(r.x+12,r.y+22,r.width-24,45),names[i],TextStyle(28,TextAnchor.MiddleCenter,new Color(.3f,.9f,1f)));
            GUI.Label(new Rect(r.x+15,r.y+90,r.width-30,145),found?(i==0?"以后我一定会好好爱护我的玩偶的！":"已找到，等待拼接"):"尚未找到",TextStyle(26,TextAnchor.MiddleCenter,found?Color.white:new Color(.52f,.64f,.7f)));
        }
    }
    void DrawClues()
    {
        var clues=StoryProgress.Instance?StoryProgress.Instance.clues:null;
        int count=clues==null?0:clues.Count;
        GUI.Label(new Rect(146,290,770,68),count==0?"尚未阅读线索":"已记录的线索 · "+count,TextStyle(36,TextAnchor.MiddleCenter,Color.white));
        scroll=GUI.BeginScrollView(new Rect(146,380,770,460),scroll,new Rect(0,0,740,Mathf.Max(460,count*105)));
        for(int i=0;i<count;i++)
        {
            var rect=new Rect(0,i*105,722,94);Tint(rect,new Color(.02f,.12f,.18f,.88f));
            GUI.Label(new Rect(20,i*105+6,682,82),clues[i],TextStyle(25,TextAnchor.MiddleLeft,Color.white));
        }
        GUI.EndScrollView();
    }
    void DrawDetail()
    {
        var cyan=new Color(.34f,.93f,1f);
        PickupItem item=tab==0?Selected:null;
        GUI.Label(new Rect(1030,195,390,56),"道具详情",TextStyle(38,TextAnchor.MiddleCenter,cyan));
        Tint(new Rect(1112,277,225,225),new Color(.01f,.05f,.12f,.85f));
        if(item)
        {
            if(item.icon)GUI.DrawTexture(new Rect(1142,307,165,165),item.icon.texture,ScaleMode.ScaleToFit,true);
            else GUI.Label(new Rect(1120,310,210,160),item.displayName.Length>0?item.displayName.Substring(0,1):"?",TextStyle(104,TextAnchor.MiddleCenter,cyan));
        }
        else GUI.Label(new Rect(1120,310,210,160),"?",TextStyle(94,TextAnchor.MiddleCenter,new Color(.25f,.4f,.48f)));
        GUI.Label(new Rect(1035,530,365,46),"名称",TextStyle(25,TextAnchor.MiddleLeft,cyan));
        GUI.Label(new Rect(1035,572,375,58),item?item.displayName:"未选择道具",TextStyle(34,TextAnchor.MiddleLeft,Color.white));
        GUI.Label(new Rect(1035,642,365,40),"种类",TextStyle(25,TextAnchor.MiddleLeft,cyan));
        GUI.Label(new Rect(1035,680,365,42),item?item.itemType:"—",TextStyle(28,TextAnchor.MiddleLeft,Color.white));
        GUI.Label(new Rect(1035,732,365,40),"简介",TextStyle(25,TextAnchor.MiddleLeft,cyan));
        GUI.Label(new Rect(1035,776,365,108),item?item.description:"选择左侧道具查看详细介绍。",TextStyle(23,TextAnchor.UpperLeft,Color.white));
        GUI.Label(new Rect(1035,893,365,38),"持有数量  "+(item?item.quantity:0),TextStyle(25,TextAnchor.MiddleLeft,cyan));
    }
    void OnDestroy(){if(font)Destroy(font);}
}
