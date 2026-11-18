using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Uses the full character mesh for shadows while the first-person mesh hides the head.
[DefaultExecutionOrder(200)]
public sealed class FullBodyShadow : MonoBehaviour
{
    SkinnedMeshRenderer source;
    SkinnedMeshRenderer shadow;
    Transform originalNeck;
    Transform shadowNeck;
    Vector3 neckScale;
    ShadowCastingMode originalCastingMode;
    readonly Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();

    public void Initialize(SkinnedMeshRenderer character, Mesh fullMesh, Transform neck)
    {
        source = character;
        originalNeck = neck;
        neckScale = neck.localScale;
        originalCastingMode = source.shadowCastingMode;
        shadowNeck = CopyBone(neck, neck.parent);

        var meshObject = new GameObject("Full character shadow");
        meshObject.layer = source.gameObject.layer;
        meshObject.transform.SetParent(source.transform, false);
        shadow = meshObject.AddComponent<SkinnedMeshRenderer>();
        shadow.sharedMesh = fullMesh;
        shadow.sharedMaterials = source.sharedMaterials;
        var bones = source.bones;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] && copies.TryGetValue(bones[i], out var replacement))
                bones[i] = replacement;
        shadow.bones = bones;
        shadow.rootBone = source.rootBone;
        shadow.localBounds = source.localBounds;
        shadow.quality = source.quality;
        shadow.updateWhenOffscreen = true;
        shadow.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        shadow.receiveShadows = false;
        source.shadowCastingMode = ShadowCastingMode.Off;
        LateUpdate();
    }

    Transform CopyBone(Transform original, Transform parent)
    {
        var copy = new GameObject(original.name + " shadow").transform;
        copy.SetParent(parent, false);
        copy.localPosition = original.localPosition;
        copy.localRotation = original.localRotation;
        copy.localScale = original.localScale;
        copies.Add(original, copy);
        foreach (Transform child in original) CopyBone(child, copy);
        return copy;
    }

    void LateUpdate()
    {
        if (!source || !shadow) return;
        foreach (var pair in copies)
        {
            if (!pair.Key || !pair.Value) continue;
            pair.Value.localPosition = pair.Key.localPosition;
            pair.Value.localRotation = pair.Key.localRotation;
            pair.Value.localScale = pair.Key == originalNeck ? neckScale : pair.Key.localScale;
        }
        for (int i = 0; i < shadow.sharedMesh.blendShapeCount; i++)
            shadow.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
        shadow.enabled = source.enabled;
        shadow.renderingLayerMask = source.renderingLayerMask;
    }

    void OnDisable()
    {
        if (shadow) shadow.enabled = false;
        if (source) source.shadowCastingMode = originalCastingMode;
    }

    void OnEnable()
    {
        if (source) source.shadowCastingMode = ShadowCastingMode.Off;
    }

    void OnDestroy()
    {
        if (source) source.shadowCastingMode = originalCastingMode;
        if (shadow) Destroy(shadow.gameObject);
        if (shadowNeck) Destroy(shadowNeck.gameObject);
    }
}
