using UnityEngine;

/// <summary>
/// Passes flashlight properties to the FloorFog shader each frame
/// so the fog can compute spotlight illumination manually,
/// bypassing URP's per-pixel additional light system.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class FogFlashlightReceiver : MonoBehaviour
{
    private const string FLASHLIGHT_TAG = "Player";
    private const float UPDATE_INTERVAL = 0f;

    [Tooltip("Path to the flashlight Light component in the scene hierarchy.")]
    [SerializeField] private string _flashlightPath = "/Flashlight";

    private Light _flashlightLight;
    private Material _material;
    private float _timer;

    // Shader property IDs
    private static readonly int ID_POS = Shader.PropertyToID("_FlashlightPos");
    private static readonly int ID_DIR = Shader.PropertyToID("_FlashlightDir");
    private static readonly int ID_COLOR = Shader.PropertyToID("_FlashlightColor");
    private static readonly int ID_INTENSITY = Shader.PropertyToID("_FlashlightIntensity");
    private static readonly int ID_RANGE = Shader.PropertyToID("_FlashlightRange");
    private static readonly int ID_SPOT_ANGLE = Shader.PropertyToID("_FlashlightSpotAngle");
    private static readonly int ID_INNER_ANGLE = Shader.PropertyToID("_FlashlightInnerAngle");
    private static readonly int ID_ENABLED = Shader.PropertyToID("_FlashlightEnabled");

    private void Start()
    {
        _material = GetComponent<MeshRenderer>().material;

        GameObject flashlightObj = GameObject.Find(_flashlightPath);
        if (flashlightObj != null)
        {
            _flashlightLight = flashlightObj.GetComponent<Light>();
        }

        if (_flashlightLight == null)
        {
            Debug.LogWarning("[FogFlashlightReceiver] Flashlight Light not found at path: " + _flashlightPath, this);
        }
    }

    private void LateUpdate()
    {
        if (_flashlightLight == null || _material == null)
            return;

        bool isOn = _flashlightLight.enabled && _flashlightLight.intensity > 0.01f;

        _material.SetVector(ID_POS, _flashlightLight.transform.position);
        _material.SetVector(ID_DIR, _flashlightLight.transform.forward);
        _material.SetVector(ID_COLOR, _flashlightLight.color.linear);
        _material.SetFloat(ID_INTENSITY, _flashlightLight.intensity);
        _material.SetFloat(ID_RANGE, _flashlightLight.range);
        _material.SetFloat(ID_SPOT_ANGLE, _flashlightLight.spotAngle);
        _material.SetFloat(ID_INNER_ANGLE, _flashlightLight.innerSpotAngle);
        _material.SetFloat(ID_ENABLED, isOn ? 1f : 0f);
    }
}
