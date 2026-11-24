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
        // 背包开着、或者鼠标还放在界面上的时候不响应丢弃：否则会在看不见准星的情况下把道具扔在脚边。
        if(!IsOpen&&Cursor.lockState==CursorLockMode.Locked&&Input.GetKeyDown(KeyCode.G))Drop();
        if(IsOpen||Cursor.lockState!=CursorLockMode.Locked)return;
        var cam=Camera.main;if(!cam)return;
        float nearest=reach;
        // 抽屉里的东西总是先撞到柜板，所以“能捡的道具”单独记一份，最后让它优先于抽屉/柜门。
        PickupItem reachableItem=null;
        float reachableItemDistance=float.MaxValue;
        // 准星最近一次撞到的柜门/抽屉面板：面板没打开时不允许隔着它捡东西。
        FurnitureOpenable blockFurniture=null;
        float blockFurnitureDistance=float.MaxValue;
        // 用带半径的球扫而不是细射线：信封碎片、相册这类又薄又小的东西，
        // 细射线很容易从边上擦过去，玩家明明瞄准了却按不出提示。
        foreach(var hit in Physics.SphereCastAll(cam.transform.position,.085f,cam.transform.forward,reach,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.transform.IsChildOf(transform)||hit.distance>=reach)continue;
            var door=hit.collider.GetComponentInParent<InteractiveDoor>();
            var furniture=hit.collider.GetComponentInParent<FurnitureOpenable>();
            if(furniture&&hit.distance<blockFurnitureDistance){blockFurnitureDistance=hit.distance;blockFurniture=furniture;}
            var placement=hit.collider.GetComponentInParent<StoryPlacement>();
            var readable=hit.collider.GetComponentInParent<ReadableClue>();
            var item=hit.collider.GetComponentInParent<PickupItem>();
            // 场景里的装饰玩具、书、已经摆好的玩偶都带 PickupItem：不是当前谜题要用的东西，
            // 不允许拾取，连“E 拾取 玩具”的提示也不要出现。
            if(item && !IsUseful(item))item=null;
            if(item&&hit.distance<reachableItemDistance){reachableItem=item;reachableItemDistance=hit.distance;}
            if(hit.distance>=nearest)continue;
            var push=hit.collider.GetComponentInParent<PushProp>();
            var keypad=hit.collider.GetComponentInParent<PasswordBoxPuzzle>();
            var stool=hit.collider.GetComponentInParent<StoolStep>();
            var pile=hit.collider.GetComponentInParent<BlockPuzzle>();
            var spot=hit.collider.GetComponentInParent<TidySpot>();
            var clock=hit.collider.GetComponentInParent<ClockDial>();
            var deposit=hit.collider.GetComponentInParent<FragmentDeposit>();
            var chair=hit.collider.GetComponentInParent<ChairSeat>();
            // 第二关的所有 E 交互（相框、洗手台、归位点、镜子、盒子）都走这一条。
            var l2=hit.collider.GetComponentInParent<L2Interactable>();
            if(spot && spot.Prompt.Length==0)spot=null;
            if(chair && chair.Prompt.Length==0)chair=null;
            if(l2 && l2.Prompt.Length==0)l2=null;
            if(!placement && !readable && !door && !furniture && !item && !keypad && !stool && !push && !pile && !spot && !clock && !deposit && !chair && !l2)continue;
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
            else if(chair){target=chair;prompt=chair.Prompt;}
            else if(push)prompt="按住 Q + WASD 推动";
            else if(spot){target=spot;prompt=spot.Prompt;}
            else if(clock){target=clock;prompt=clock.Prompt;}
            else if(deposit){target=deposit;prompt=deposit.Prompt;}
            else if(l2){target=l2;prompt=l2.Prompt;}
        }
        // 道具优先：站在抽屉/柜子前面时，只要里面那件东西够得着，就按“拾取”而不是“开抽屉”。
        // 但门板/抽屉面板还没打开时不许越过它 —— 否则玩家能隔着关着的柜门把里面的玩偶拿走。
        if(reachableItem&&reachableItemDistance<=nearest+.75f)
        {
            bool behindClosedPanel=blockFurniture&&blockFurniture.OpenAmount<.35f
                &&blockFurnitureDistance<reachableItemDistance-.02f;
            if(!behindClosedPanel)
            {
                target=reachableItem;
                prompt="E 拾取 "+reachableItem.displayName;
            }
        }
        // 坐着的时候椅子自己的碰撞体是关着的（不然角色会被物理系统顶出椅面），准星扫不到它；
        // 这里把交互目标强行指回椅子，否则坐下之后就再也按不了 E 起身。
        var seatedChair=ChairSeat.Current;
        if(seatedChair&&seatedChair.Seated){target=seatedChair;prompt=seatedChair.Prompt;}
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
                if(RoomDoorGate.Blocks(door))RoomDoorGate.Deny(door);
                else if(boundary)boundary.Deny();
                else
                {
                    // 二号房间的门：积木谜题没解开、或者还没拿到钥匙，都不允许打开。
                    var gate=door.GetComponent<DoorLock>();
                    if(gate && !door.IsOpen && !gate.TryUnlock(this)) { }
                    else
                    {
                        Debug.Log("[门] "+door.name+" 交互："+(door.IsOpen?"关门":"开门")
                            +(gate?"（锁已解开）":"（没有门锁组件）"),door);
                        door.Toggle(transform.position);
                        var intro=door.GetComponent<DoorRoomIntro>();if(intro && door.IsOpening)intro.OnOpened();
                    }
                }
            }
            else if(target is FurnitureOpenable furniture)furniture.Toggle(transform.position);
            else if(target is PasswordBoxPuzzle keypad)keypad.Interact();
            else if(target is BlockPuzzle blockPile)blockPile.Interact(this);
            else if(target is StoolStep stool)stool.Interact();
            else if(target is ChairSeat chair)chair.Interact();
            else if(target is PickupItem item)Take(item);
            else if(target is TidySpot tidySpot)tidySpot.Place(this);
            else if(target is ClockDial clockDial)clockDial.Tick();
            else if(target is FragmentDeposit fragmentDeposit)fragmentDeposit.Place();
            else if(target is L2Interactable l2target)l2target.Interact(this);
        }
    }
    /// <summary>
    /// 当前进行到第几个解密。用“已拿到的信封碎片”推断：碎片是每关唯一、顺序固定的产物，
    /// 比另开一个状态字段更可靠（读档、回看、重进场景都不会错位）。
    /// 1=玩偶归位 2=积木 3=密码箱 4=二号房间整理 5=客厅时钟 6=碎片集齐（只剩放置）
    /// </summary>
    static int PuzzleStage
    {
        get
        {
            var story=StoryProgress.Instance;
            if(!story)return 1;
            if(story.fragments.Contains("信封碎片⑤"))return 6;
            if(story.fragments.Contains("信封碎片④"))return 5;
            if(story.fragments.Contains("信封碎片③"))return 4;
            if(story.fragments.Contains("信封碎片②"))return 3;
            if(story.fragments.Contains("信封碎片①"))return 2;
            return 1;
        }
    }

    /// <summary>
    /// 这个东西该不该允许拾取。
    /// 规则：**只放行当前这个解密任务相关的东西**，别的任务的道具先不给捡
    /// （不然玩家能提前把钥匙、积木、整理道具一股脑捞走，后面的谜题就乱了）。
    ///   · 当前关的玩偶 / 积木 / 整理道具 / 钥匙；
    ///   · 信封碎片：同一时刻场上只会有一片，随时可以捡；
    ///   · 已经全部集齐之后（只剩放置），不再限制。
    /// 装饰玩具、书这类从头到尾都不允许拾取。
    /// </summary>
    bool IsUseful(PickupItem item)
    {
        if(!item)return false;
        string id=item.storyId??"";
        if(id.Length==0)return false;
        if(id.StartsWith("l2_"))return true;              // 第二关的道具不设门槛
        if(id.StartsWith("letter"))return true;           // 信封碎片：当前关的产物

        switch(PuzzleStage)
        {
            case 1: return NeededByPlacement(id);          // 只认这一关要归位的四只玩偶
            case 2: return id.StartsWith("block_");        // 积木
            case 3: return false;                          // 密码箱：没有可拾取的道具
            case 4: return id=="room2_key"||id.StartsWith("tidy_");   // 钥匙 + 二号房间整理
            case 5: return false;                          // 客厅时钟：没有可拾取的道具
            default: return true;                          // 碎片集齐，只剩放置
        }
    }

    /// <summary>这件道具是不是某个还没摆好的归位点正等着的。</summary>
    bool NeededByPlacement(string id)
    {
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
        if(!fragment&&items.Count>=capacity){ShowNotice("背包已满");return;}

        // 有些道具是从抽屉里拿的（积木、碎片）：先把“抽屉关着就隐藏”的开关解掉，
        // 否则捡起来再按 G 丢出去，东西是 SetActive(true) 了、渲染器却还关着 —— 隐形砖头一块。
        var stash=item.GetComponentInParent<StashedBlock>();
        if(stash)stash.Release();
        // 钥匙这类“抽屉里的小物件”同理：它可能正被隐藏、还挂在抽屉面板下面。
        var drawerHint=item.GetComponentInParent<DrawerItemHint>();
        if(drawerHint)drawerHint.Release();
        // 再挂回场景根节点，否则玩家之后把抽屉一推、或者丢出来的时候，东西会跟着抽屉一起跑。
        if (item.transform.parent) item.transform.SetParent(null, true);

        var player=GetComponent<PlayerController>();
        if(fragment)
        {
            if(StoryProgress.Instance)StoryProgress.Instance.AddFragment(fragment.fragmentName,fragment.text);
            item.gameObject.SetActive(false);
            if(player)player.PlayPickup();
            GameAudio.PlayPickup();
            return;
        }
        items.Add(item);item.gameObject.SetActive(false);selected=items.Count-1;
        Debug.Log("[拾取] "+item.displayName+"（id="+item.storyId+"），背包里现有 "+items.Count+" 件。",item);
        if(player)player.PlayPickup();
        GameAudio.PlayPickup();
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

        // 视线压到最低、或者垂直往下看时，forward 的水平投影会变成零向量，
        // 归一化之后是 (0,0,0)，道具会被丢在脚底下（甚至是原点）。这里逐级兜底。
        Vector3 direction=Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up);
        if(direction.sqrMagnitude<.001f)direction=Vector3.ProjectOnPlane(cam.transform.up,Vector3.up);
        if(direction.sqrMagnitude<.001f)direction=Vector3.ProjectOnPlane(transform.forward,Vector3.up);
        if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
        direction.Normalize();

        var renderer=item.GetComponentInChildren<Renderer>(true);float radius=renderer?Mathf.Clamp(renderer.bounds.extents.magnitude,.08f,.3f):.12f;
        Vector3 origin=cam.transform.position;float distance=.65f;
        foreach(var hit in Physics.SphereCastAll(origin,radius,direction,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.transform.IsChildOf(transform))continue;
            if(hit.collider.GetComponentInParent<PickupItem>())continue;      // 地上别的道具不算挡路
            distance=Mathf.Min(distance,hit.distance-.04f);
        }
        if(distance<.18f){ShowNotice("前方空间不足，换个方向再丢");return;}

        // 先摘掉父级：否则从抽屉里拿出来的东西丢出去还挂在抽屉上，抽屉一动它又跟着跑。
        // 摘之前先把“藏在抽屉里”的状态解掉（渲染器一直是关着的），不然丢出来的是块隐形砖头。
        var stashInDrawer=item.GetComponentInParent<StashedBlock>();
        if(stashInDrawer)stashInDrawer.Release();
        var hintInDrawer=item.GetComponentInParent<DrawerItemHint>();
        if(hintInDrawer)hintInDrawer.Release();
        if(item.transform.parent)item.transform.SetParent(null,true);

        // 谜题关键道具（还没摆好的玩偶、信封碎片、积木、钥匙）丢出来时直接落到地面上，
        // 不给刚体自由下落：否则很容易滚进柜子里、或者穿过地板，玩家就再也找不到了。
        if(IsUseful(item))
        {
            Vector3 spot=origin+direction*distance;
            if(Physics.Raycast(spot+Vector3.up*.8f,Vector3.down,out var ground,3f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                item.transform.position=ground.point+Vector3.up*.03f;
                item.gameObject.SetActive(true);
                // 从抽屉里出来的积木可能还带着 StashedBlock 的“隐藏”状态，这里再确认一次是可见的。
                var stash=item.GetComponent<StashedBlock>();
                if(!stash)stash=item.GetComponentInChildren<StashedBlock>(true);
                if(stash)stash.Release();
                var hint=item.GetComponent<DrawerItemHint>();
                if(!hint)hint=item.GetComponentInChildren<DrawerItemHint>(true);
                if(hint)hint.Release();
                var body=item.GetComponent<Rigidbody>();
                if(body){body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.useGravity=false;body.isKinematic=true;}
                items.RemoveAt(selected);selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
                GameAudio.PlayPlace();
                return;
            }
        }

        item.transform.position=origin+direction*distance;item.gameObject.SetActive(true);
        var rb=item.GetComponent<Rigidbody>();if(!rb)rb=item.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic=false;rb.useGravity=true;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.velocity=Vector3.zero;
        items.RemoveAt(selected);selected=Mathf.Clamp(selected,0,Mathf.Max(0,items.Count-1));
        GameAudio.PlayPlace();
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
