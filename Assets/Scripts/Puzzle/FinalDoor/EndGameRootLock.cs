using UnityEngine;

/// <summary>
/// Temporary LateUpdate hook for the doctor's Turn→Sad handoff: keeps the
/// figure's rendered direction locked on a fixed world yaw while the
/// animator's cross-fade absorbs the turn's hips offset. Runs in LateUpdate
/// — after the animator has applied the frame's pose — so the correction is
/// frame-exact: root yaw + hips offset always equals the locked yaw, with no
/// lag wobble. The lock releases itself after the duration; the root then
/// holds whatever yaw keeps the last frame's direction.
/// </summary>
public class EndGameRootLock : MonoBehaviour
{
    private Transform _figure;
    private Transform _hips;
    private float _lockedYaw;
    private float _timer;
    private float _duration;

    /// <summary>
    /// Locks the figure's rendered direction to the hips' current world yaw
    /// (the portal direction at the handoff frame) for the given duration.
    /// </summary>
    public static EndGameRootLock Begin(Transform figure, Transform hips, float duration)
    {
        var host = new GameObject("EndGameRootLock");
        var comp = host.AddComponent<EndGameRootLock>();
        comp._figure = figure;
        comp._hips = hips;
        comp._lockedYaw = hips.eulerAngles.y;
        comp._duration = duration;
        return comp;
    }

    private void LateUpdate()
    {
        if (_figure == null || _hips == null)
        {
            Destroy(gameObject);
            return;
        }

        _timer += Time.deltaTime;

        // Frame-exact: the animator has already posed the hips for this frame;
        // rotate the root so root yaw + hips offset == the locked yaw.
        float residual = Mathf.DeltaAngle(_figure.eulerAngles.y, _hips.eulerAngles.y);
        _figure.rotation = Quaternion.Euler(0f, _lockedYaw - residual, 0f);

        if (_timer >= _duration)
            Destroy(gameObject);
    }
}
