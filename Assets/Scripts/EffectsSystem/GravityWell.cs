using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Pulls any Rigidbody tagged "Pickup" within the configured radius toward this transform.
    /// Spawned and destroyed by SuperdenseBlackness as a child of the player.
    /// Tag pickup prefabs with the "Pickup" Unity tag to participate.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class GravityWell : MonoBehaviour
    {
        private float _pullForce;
        private SphereCollider _trigger;

        public void Initialize(float radius, float pullForce)
        {
            _pullForce = pullForce;
            _trigger = GetComponent<SphereCollider>();
            _trigger.isTrigger = true;
            _trigger.radius    = radius;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!other.CompareTag("Pickup")) return;
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null) return;

            Vector3 dir   = (transform.position - other.transform.position).normalized;
            float   dist  = Vector3.Distance(transform.position, other.transform.position);
            float   force = _pullForce * (1f - dist / _trigger.radius); // stronger near center
            rb.AddForce(dir * force, ForceMode.Acceleration);
        }
    }
}
