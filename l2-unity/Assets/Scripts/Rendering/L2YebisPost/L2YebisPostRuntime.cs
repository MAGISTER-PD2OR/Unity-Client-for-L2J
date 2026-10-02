using UnityEngine;

/// <summary>
/// Handshake so nameplates flush after global Yebis, not inside the skill compositor.
/// </summary>
public static class L2YebisPostRuntime
{
    public static bool WillFlushNameplates { get; set; }
}
