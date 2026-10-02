using UnityEngine;

/// <summary>
/// Client point light on an already placed torch. Position is this Transform.
/// Color is the Unity Light color times intensity. Attenuation is the client
/// c232 triple, with distance converted to meters (1 m = 52.5 L2 units).
/// </summary>
[DisallowMultipleComponent]
public sealed class L2ActorLocalLight : MonoBehaviour
{
    [Tooltip("c232.x. Not scaled.")]
    public float constantAttenuation = 0.431085169f;

    [Tooltip("c232.y * 52.5. Distance in the shader is meters.")]
    public float linearAttenuation = 0.021897854f;

    [Tooltip("c232.z / 52.5. 1525 L2 units is 29.05 m.")]
    public float radius = 29.04762f;

    Light _light;

    void Awake()
    {
        _light = GetComponent<Light>();
    }

    public bool IsActive
    {
        get
        {
            if (!isActiveAndEnabled || radius <= 0f)
                return false;
            if (_light == null)
                _light = GetComponent<Light>();
            return _light != null && _light.enabled && _light.intensity > 0f;
        }
    }

    public Vector3 WorldPosition => transform.position;

    public float Intensity
    {
        get
        {
            Light light = LightOrNull;
            return light != null ? light.intensity : 0f;
        }
    }

    public float UnityRange
    {
        get
        {
            Light light = LightOrNull;
            return light != null ? light.range : 0f;
        }
    }

    public Color SourceColor
    {
        get
        {
            Light light = LightOrNull;
            return light != null ? light.color : Color.black;
        }
    }

    Light LightOrNull
    {
        get
        {
            if (_light == null)
                _light = GetComponent<Light>();
            return _light;
        }
    }

    public Color ReadyColor
    {
        get
        {
            Light light = LightOrNull;
            if (light == null)
                return Color.black;
            return light.color * light.intensity;
        }
    }
}
