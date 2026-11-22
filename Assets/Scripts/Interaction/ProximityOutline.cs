using System.Collections.Generic;
using UnityEngine;

// Adds a second, expanded mesh pass only while the player is close enough to see the toy.
public sealed class ProximityOutline : MonoBehaviour
{
    [Min(.25f)] public float revealDistance = 1.9f;
    [Range(.003f,.04f)] public float outlineWidth = .018f;
    public Color outlineColor = new Color(.25f,.9f,1f,.9f);

    readonly Dictionary<Renderer,Material[]> originals = new Dictionary<Renderer,Material[]>();
    Renderer[] renderers;
    Material outline;
    PlayerController player;
    bool visible;

    /// <summary>
    /// 改颜色 / 粗细 / 可见距离，并且立刻作用到已经建好的描边材质上。
    ///
    /// 注意：以前调用方都是 AddComponent 之后再去改这两个 public 字段，
    /// 可描边材质是在 OnEnable 里按默认值建好的 —— 字段改了材质没改，
    /// 结果所有“黄色描边”实际都还是默认的青色。要改颜色必须走这里。
    /// </summary>
    public void Apply(Color color, float width, float reveal)
    {
        outlineColor=color;
        outlineWidth=Mathf.Clamp(width,.003f,.04f);
        revealDistance=Mathf.Max(.05f,reveal);
        // 有些碎片模型上本来就挂着描边（比如密码箱开箱时会把箱子子物体的描边全关掉），
        // 那就得先打开，否则后面改材质也看不到。
        if(!enabled)enabled=true;
        if(outline)
        {
            outline.SetColor("_OutlineColor",outlineColor);
            outline.SetFloat("_OutlineWidth",outlineWidth);
        }
        else
        {
            // 材质还没建出来（组件是这一帧刚加的）：重走一次 OnEnable 让它按新参数生成。
            enabled=false;enabled=true;
        }
    }

    void OnEnable()
    {
        renderers=GetComponentsInChildren<Renderer>(true);
        var shader=Shader.Find("Childhood/ProximityOutline");
        if(!shader){Debug.LogWarning("Proximity outline shader is missing",this);return;}
        outline=new Material(shader){hideFlags=HideFlags.DontSave};
        outline.SetFloat("_OutlineWidth",outlineWidth);
        outline.SetColor("_OutlineColor",outlineColor);
        foreach(var renderer in renderers)
            if(renderer && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                originals[renderer]=renderer.sharedMaterials;
        player=FindObjectOfType<PlayerController>();
    }
    void Update()
    {
        if(!player)player=FindObjectOfType<PlayerController>();
        if(!player||!outline)return;
        float closest=float.PositiveInfinity;
        foreach(var renderer in originals.Keys)
            if(renderer)closest=Mathf.Min(closest,Vector3.Distance(player.transform.position,renderer.bounds.ClosestPoint(player.transform.position)));
        bool shouldShow=closest<=revealDistance;
        if(shouldShow==visible)return;
        visible=shouldShow;
        // 材质是 OnEnable 时建的，之后再改 outlineColor/outlineWidth 不会自动生效，
        // 这里每次切换都同步一次，运行时改颜色才管用。
        if(visible && outline)
        {
            outline.SetFloat("_OutlineWidth",outlineWidth);
            outline.SetColor("_OutlineColor",outlineColor);
        }
        foreach(var pair in originals)
        {
            if(!pair.Key)continue;
            if(!visible){pair.Key.sharedMaterials=pair.Value;continue;}
            var materials=new Material[pair.Value.Length+1];
            pair.Value.CopyTo(materials,0);materials[materials.Length-1]=outline;
            pair.Key.sharedMaterials=materials;
        }
    }
    void OnDisable()
    {
        foreach(var pair in originals)if(pair.Key)pair.Key.sharedMaterials=pair.Value;
        originals.Clear();visible=false;
        if(outline)Destroy(outline);
    }
}
