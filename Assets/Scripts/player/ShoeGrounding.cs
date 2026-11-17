using UnityEngine;

// Mark the heel, midsole and toe once, then follow their bones after animation.
internal sealed class ShoeGrounding
{
    struct SolePoint
    {
        public Transform bone;
        public Vector3 local;
    }

    readonly SolePoint[] points = new SolePoint[6];
    readonly Transform hips;
    readonly CapsuleCollider capsule;
    readonly int mask;
    bool calibrated;

    public ShoeGrounding(Animator animator, CapsuleCollider capsule, int mask)
    {
        this.capsule = capsule;
        this.mask = mask;
        hips = animator.transform.Find("mixamorig:Hips");
        if (!hips || capsule.direction != 1) return;
        var bones = hips.GetComponentsInChildren<Transform>();
        var feet = new Transform[2];
        var toes = new Transform[2];
        for (int side = 0; side < 2; side++)
        {
            string prefix = side == 0 ? "mixamorig:Left" : "mixamorig:Right";
            feet[side] = System.Array.Find(bones, t => t.name == prefix + "Foot");
            toes[side] = System.Array.Find(bones, t => t.name == prefix + "ToeBase");
            if (!feet[side] || !toes[side]) return;
        }

        float[] lowest = new float[points.Length];
        for (int i = 0; i < lowest.Length; i++) lowest[i] = float.PositiveInfinity;
        var mesh = new Mesh();
        foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.BakeMesh(mesh);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 p = skin.transform.TransformPoint(vertex);
                int side = (p - feet[0].position).sqrMagnitude < (p - feet[1].position).sqrMagnitude ? 0 : 1;
                var foot = feet[side];
                Vector3 axis = Vector3.ProjectOnPlane(toes[side].position - foot.position, Vector3.up);
                float along = Vector3.Dot(p - foot.position, axis) / Mathf.Max(0.000001f, axis.sqrMagnitude);
                int zone = along < 0.25f ? 0 : along < 0.95f ? 1 : 2;
                int i = 3 * side + zone;
                if (p.y >= lowest[i]) continue;
                lowest[i] = p.y;
                var bone = zone == 2 ? toes[side] : foot;
                points[i] = new SolePoint { bone = bone, local = bone.InverseTransformPoint(p) };
            }
        }
        Object.Destroy(mesh);
        if (System.Array.Exists(points, p => !p.bone)) return;
        float bottom = Mathf.Min(lowest);
        // Fit the bottom of the standing capsule to the shoes, keeping its top in place.
        float delta = (bottom - capsule.bounds.min.y) / Mathf.Abs(capsule.transform.lossyScale.y);
        float height = Mathf.Max(2f * capsule.radius, capsule.height - delta);
        capsule.center += Vector3.up * ((capsule.height - height) * 0.5f);
        capsule.height = height;
        calibrated = true;
    }

    public void Plant()
    {
        if (!calibrated) return;
        float bottom = capsule.bounds.min.y;
        float lift = float.NegativeInfinity;
        foreach (var point in points)
        {
            Vector3 sole = point.bone.TransformPoint(point.local);
            Vector3 origin = new Vector3(sole.x, bottom + 0.12f, sole.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 0.21f, mask, QueryTriggerInteraction.Ignore)
                && hit.rigidbody != capsule.attachedRigidbody && hit.normal.y > 0.55f)
                lift = Mathf.Max(lift, hit.point.y - sole.y);
        }
        // All grounded clips key the hips position, so each frame starts with a fresh pose.
        if (!float.IsNegativeInfinity(lift))
            hips.position += Vector3.up * Mathf.Clamp(lift, -0.15f, 0.15f);
    }
}
