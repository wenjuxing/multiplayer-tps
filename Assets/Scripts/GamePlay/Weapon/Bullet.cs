using Mirror;
using UnityEngine;

/// <summary>Plays the server-confirmed shot path on each client.</summary>
public class Bullet : NetworkBehaviour {
    private const float EndpointHold = 0.15f;

    [Tooltip("飞行速度（米/秒）")]
    [SerializeField, Min(0.01f)] private float speed = 20f;
    [SyncVar] private Vector3 origin;
    [SyncVar] private Vector3 endpoint;
    [SyncVar] private double launchedAt;
    [SyncVar] private float flightTime;

    [Server]
    public void InitializePath(Vector3 start, Vector3 end) {
        origin = start;
        endpoint = end;
        flightTime = Vector3.Distance(start, end) / speed;
        launchedAt = NetworkTime.time;
    }

    public override void OnStartClient() {
        base.OnStartClient();
        UpdatePosition();

        GameObject trail = WeaponShotEffects.Client?.Trail;
        if (trail != null)
            Instantiate(trail, transform, false);
    }

    private void Update() {
        double elapsed = NetworkTime.time - launchedAt;
        UpdatePosition();
        // Leave the endpoint visible while observing clients catch up to the server timeline.
        if (isServer && elapsed >= flightTime + EndpointHold)
            NetworkServer.Destroy(gameObject);
    }

    private void UpdatePosition() {
        float elapsed = (float)(NetworkTime.time - launchedAt);
        float progress = flightTime > 0f ? elapsed / flightTime : 1f;
        transform.position = Vector3.Lerp(origin, endpoint, progress);
    }
}
