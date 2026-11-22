using System;
using UnityEngine;

internal sealed class ShotCollision {
    private const float MuzzleClearance = 0.01f;
    private const string VirtualEnemyTag = "Enemy";
    private readonly int playerHitboxLayer = LayerMask.NameToLayer("PlayerHitbox");
    private RaycastHit[] rayHits = new RaycastHit[8];
    private Collider[] muzzleOverlaps = new Collider[4];

    public readonly struct Contact {
        public Vector3 Point { get; }
        public float Distance { get; }
        public PlayerHealth Target { get; }
        public bool IsVirtualEnemy { get; }
        public bool IsTarget => Target != null || IsVirtualEnemy;

        public Contact(Vector3 point, float distance, PlayerHealth target, bool isVirtualEnemy = false) {
            Point = point;
            Distance = distance;
            Target = target;
            IsVirtualEnemy = isVirtualEnemy;
        }
    }

    public bool TryGetClosest(Ray ray, float range, Transform shooter, out Contact contact) {
        int count = Physics.RaycastNonAlloc(ray, rayHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        while (count == rayHits.Length) {
            Array.Resize(ref rayHits, rayHits.Length * 2);
            count = Physics.RaycastNonAlloc(ray, rayHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        }

        contact = default;
        bool found = false;
        for (int i = 0; i < count; i++) {
            RaycastHit hit = rayHits[i];
            if (!TryClassify(hit.collider, shooter, hit.point, hit.distance, out Contact candidate)) continue;
            if (found && (candidate.Distance > contact.Distance ||
                          (candidate.Distance == contact.Distance && (candidate.IsTarget || !contact.IsTarget)))) continue;

            contact = candidate;
            found = true;
        }

        return found;
    }

    public bool TryGetMuzzleContact(Vector3 muzzle, Transform shooter, out Contact contact) {
        // Raycasts skip colliders that already contain the origin.
        int count = Physics.OverlapSphereNonAlloc(muzzle, MuzzleClearance, muzzleOverlaps,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        while (count == muzzleOverlaps.Length) {
            Array.Resize(ref muzzleOverlaps, muzzleOverlaps.Length * 2);
            count = Physics.OverlapSphereNonAlloc(muzzle, MuzzleClearance, muzzleOverlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        }

        contact = default;
        for (int i = 0; i < count; i++) {
            Collider collider = muzzleOverlaps[i];
            if (!TryClassify(collider, shooter, muzzle, 0f, out Contact candidate)) continue;

            if (!candidate.IsTarget) {
                contact = candidate;
                return true;
            }

            if (collider.ClosestPoint(muzzle) == muzzle)
                contact = candidate;
        }

        return contact.IsTarget;
    }

    private bool TryClassify(Collider collider, Transform shooter, Vector3 point, float distance, out Contact contact) {
        contact = default;
        if (collider == null || collider.transform.IsChildOf(shooter)) return false;
        if (collider.gameObject.layer == playerHitboxLayer) {
            PlayerHealth target = collider.GetComponentInParent<PlayerHealth>();
            if (target == null) return false;
            contact = new Contact(point, distance, target);
            return true;
        }

        if (collider.isTrigger) return false;
        contact = new Contact(point, distance, null, collider.CompareTag(VirtualEnemyTag));
        return true;
    }
}
