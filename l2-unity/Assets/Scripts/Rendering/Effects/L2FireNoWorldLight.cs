using UnityEngine;

/// <summary>
/// de_fire used to force its Point Light off so URP would not light the floor.
/// The light stays on: characters take it in the actor vertex color, and the
/// Light intensity is the client-scale knob.
/// </summary>
[DisallowMultipleComponent]
public sealed class L2FireNoWorldLight : MonoBehaviour
{
}
