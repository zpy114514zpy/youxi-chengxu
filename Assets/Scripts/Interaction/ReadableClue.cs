using UnityEngine;
public sealed class ReadableClue : MonoBehaviour
{
    [TextArea] public string title="线索";
    [TextArea(3,8)] public string description="";
    public float glowRange=1.8f;
    public Vector3 labelOffset=Vector3.up*.25f;
    [TextArea] public string guideAfterRead;
    bool guideShown;
    Renderer[] meshes;MaterialPropertyBlock block;Camera view;Transform player;
    void Start()
    {
        meshes=GetComponentsInChildren<Renderer>();block=new MaterialPropertyBlock();view=Camera.main;
        var p=FindObjectOfType<PlayerController>();if(p)player=p.transform;
        foreach(var r in meshes)if(r)foreach(var material in r.materials)if(material && material.HasProperty("_EmissionColor"))material.EnableKeyword("_EMISSION");
    }
    void LateUpdate()
    {
        if(!player||meshes==null)return;
        bool near=(player.position-transform.position).sqrMagnitude<glowRange*glowRange;
        foreach(var r in meshes)if(r)
        {r.GetPropertyBlock(block);block.SetColor("_EmissionColor",near?Color.white*1.8f:Color.black);r.SetPropertyBlock(block);}
    }
    public void Read()
    {
        StoryProgress.Instance?.Read(title,description);
        if (!guideShown && !string.IsNullOrEmpty(guideAfterRead))
        { guideShown=true; StoryProgress.Instance?.Enqueue(guideAfterRead); }
    }
    void OnGUI()
    {
        if(!player||!view||StoryProgress.Instance && StoryProgress.Instance.IsReading ||
           (player.position-transform.position).sqrMagnitude>glowRange*glowRange)return;
        ReadableClue focused=null;float best=float.MaxValue;
        foreach(var hit in Physics.RaycastAll(view.transform.position,view.transform.forward,glowRange,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            var clue=hit.collider.GetComponentInParent<ReadableClue>();
            if(clue && hit.distance<best){focused=clue;best=hit.distance;}
        }
        if(focused!=this)return;
        Vector3 top=transform.position+labelOffset;
        var r=GetComponentInChildren<Renderer>();if(r)top=new Vector3(r.bounds.center.x,r.bounds.max.y+.12f,r.bounds.center.z);
        var screen=view.WorldToScreenPoint(top);if(screen.z<=0)return;
        var style=new GUIStyle(GUI.skin.box){fontSize=15,alignment=TextAnchor.MiddleCenter,wordWrap=true};
        GUI.Box(new Rect(screen.x-150,Screen.height-screen.y-48,300,38),"E 阅读 · "+title,style);
    }
    void OnDisable(){if(meshes!=null)foreach(var r in meshes)if(r)r.SetPropertyBlock(null);}
}
