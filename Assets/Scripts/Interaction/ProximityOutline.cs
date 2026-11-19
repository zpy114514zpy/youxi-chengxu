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
