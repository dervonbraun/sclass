using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Trigger zone that drains or fills one elemental scale while the player stands inside.
    /// Requires a Collider with Is Trigger enabled on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class MutationZone : MonoBehaviour
    {
        [SerializeField] private MutationType _type;
        [Tooltip("Amount added per second. Use negative values to drain.")]
        [SerializeField] private float _ratePerSecond = 20f;

        private ElementalMutationManager _target;

        private void OnTriggerEnter(Collider other)
        {
            _target = other.GetComponentInParent<ElementalMutationManager>();
        }

        private void OnTriggerStay(Collider other)
        {
            if (_target != null)
                _target.ModifyStat(_type, _ratePerSecond * Time.deltaTime);
        }

        private void OnTriggerExit(Collider other)
        {
            _target = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Color c = _type switch
            {
                MutationType.Kinesia     => ElementalMutationManager.KinesiaColor,
                MutationType.Smallion    => ElementalMutationManager.SmallionColor,
                MutationType.Transfinite => ElementalMutationManager.TransfiniteColor,
                _                        => Color.white,
            };
            c.a = 0.25f;
            Gizmos.color = c;

            if (TryGetComponent(out BoxCollider box))
                Gizmos.DrawCube(transform.position, box.size);
            else if (TryGetComponent(out SphereCollider sphere))
                Gizmos.DrawSphere(transform.position, sphere.radius);
        }
#endif
    }
}
