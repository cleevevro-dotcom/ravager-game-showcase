using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform _target;
    [SerializeField] float _smoothSpeed = 8f;
    [SerializeField] Vector3 _offset = new Vector3(0f, 0f, -10f);

    float _shakeAmplitude;
    float _shakeDuration;
    float _shakeTimer;

    void LateUpdate()
    {
        if (_target == null) return;
        Vector3 desired = _target.position + _offset;
        Vector3 lerped  = Vector3.Lerp(transform.position, desired, _smoothSpeed * Time.deltaTime);

        // Apply decaying shake offset on top of the lerp. Decays linearly over _shakeDuration.
        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            float t = Mathf.Max(0f, _shakeTimer / Mathf.Max(0.0001f, _shakeDuration));
            float strength = _shakeAmplitude * t;
            float ox = (Random.value - 0.5f) * strength * 2f;
            float oy = (Random.value - 0.5f) * strength * 2f;
            lerped.x += ox;
            lerped.y += oy;
        }

        transform.position = lerped;
    }

    /// <summary>Immediately snap to the target's position. Used when the player is relocated
    /// at boot (wilderness spawn) so the camera doesn't slowly lerp across the world.</summary>
    public void Snap()
    {
        if (_target == null) return;
        transform.position = _target.position + _offset;
        _shakeTimer = 0f; // cancel any active shake on snap
    }

    /// <summary>Trigger a decaying shake. If a stronger shake is already running, keeps it;
    /// otherwise replaces the current shake. Caller (PlayerHitFeedback) handles cooldown
    /// throttling so 200 hits per frame don't stack into a seizure.</summary>
    public void Shake(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f) return;
        // If the new shake is stronger, take it. Otherwise refresh the existing one's timer
        // so simultaneous hits don't end the shake prematurely.
        if (amplitude >= _shakeAmplitude)
        {
            _shakeAmplitude = amplitude;
            _shakeDuration  = duration;
            _shakeTimer     = duration;
        }
        else if (_shakeTimer < duration)
        {
            _shakeTimer = duration;
        }
    }
}
