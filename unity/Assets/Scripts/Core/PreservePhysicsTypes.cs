using UnityEngine;
using UnityEngine.Scripting;

namespace RaceX.Core
{
    /// <summary>
    /// Garante que o linker do Unity IL2CPP no Android preserve todas as classes de física e colisores.
    /// </summary>
    [Preserve]
    public static class PreservePhysicsTypes
    {
        [Preserve]
        public static void Touch()
        {
            var t1 = typeof(CapsuleCollider);
            var t2 = typeof(SphereCollider);
            var t3 = typeof(BoxCollider);
            var t4 = typeof(MeshCollider);
            var t5 = typeof(WheelCollider);
            var t6 = typeof(Rigidbody);
            if (t1 != null && t2 != null && t3 != null && t4 != null && t5 != null && t6 != null)
            {
                // no-op
            }
        }
    }
}
