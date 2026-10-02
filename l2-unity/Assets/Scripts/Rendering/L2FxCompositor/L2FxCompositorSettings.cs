using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime settings for the L2 D3D9-compatible effect compositor.
/// Evidence: L2 Colour Pass FB = B8G8R8A8_UNORM + PTDS blends;
/// Unity DrawTransparentObjects = R8G8B8A8_SRGB.
/// </summary>
[Serializable]
public sealed class L2FxCompositorSettings
{
    [Tooltip("When enabled, L2 FX draw into a raw UNORM buffer with encode/decode around the scene.")]
    public bool enableD3D9Compositor = true;

    [Tooltip("Draw sky before the world and skills directly into cameraColor. Use when the project camera target is already raw UNORM.")]
    public bool directCameraColor = true;

    [Tooltip("Injection relative to URP transparent/post. Match L2: after world, before UI/post.")]
    public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

    [Tooltip("Unity layer used for generated L2 effects (TagManager: SkillEffect).")]
    public LayerMask effectLayerMask = 1 << 18;

    [Tooltip("Sun/moon/star billboards (TagManager: L2Sky). Drawn in the UNORM blit after the world copy.")]
    public LayerMask celestialLayerMask = 1 << 20;

    [Tooltip("Horizon WhiteRing (TagManager: L2Haze). Drawn in UNORM after the scene copy, so skill post fxGain + bloom apply (same as sun/moon/skills).")]
    public LayerMask hazeLayerMask = 1 << 21;

    [Tooltip("Day cloud sheets (TagManager: L2Clouds). Drawn in UNORM after encode, before haze. Not in skill bloom.")]
    public LayerMask cloudLayerMask = 1 << 22;

    [Tooltip("Legacy diagnostic switch. Sky display correction now comes from L2SkyPostLut, outside this compositor.")]
    public bool includeSkyInBoost = false;

    [Tooltip("Peak RGB below this is night sky — no boost. Night LUT #283146 ≈ 0.27.")]
    [Range(0f, 1f)]
    public float skyBoostStart = 0.30f;

    [Tooltip("Peak RGB above this gets full fxGain (day blue). Sun/moon always use fxGain.")]
    [Range(0f, 1f)]
    public float skyBoostFull = 0.50f;

    [Tooltip("Encode Unity linear camera color into display-referred UNORM before FX. Off when Color Space is Gamma.")]
    public bool encodeLinearToSrgb = false;

    [Tooltip("Decode UNORM back to linear when writing into the Unity camera target. Off when Color Space is Gamma.")]
    public bool decodeSrgbToLinear = false;

    [Tooltip("Bind camera depth so FX still occlude against the world.")]
    public bool bindCameraDepth = true;

    [Tooltip("Skip SceneView / preview / reflection cameras.")]
    public bool gameCameraOnly = true;

    [Tooltip("0 = off, 1 = show L2 UNORM buffer, 2 = encode only, 3 = skill FX only, 4 = bloom only.")]
    public int debugMode;

    [Header("Skill post")]
    [Tooltip("Legacy skill-only bloom/boost fallback. Keep off for direct cameraColor and the replacement postprocessor.")]
    public bool enableSkillPost = false;

    [Tooltip("Luma cutoff before downsample. Skills below this do not feed bloom.")]
    [Range(0f, 1f)]
    public float bloomThreshold = 0.2f;

    [Tooltip("Bloom add after Kawase. 0 disables bloom.")]
    public float bloomIntensity = 0.95f;

    [Tooltip("Legacy post RGB Boost. Direct cameraColor always uses 1.")]
    [Range(0f, 4f)]
    public float fxGain = 1f;

    [Header("Yebis (global, after scene + skills)")]
    [Tooltip("High Elf RenderPostYebisEffect on the whole camera. Same pass for sky, ground and skills.")]
    public bool enableYebisPost = true;

    [Tooltip("SIM-HDR amount. 1 = full Env.int gain. 0 = scene as-is.")]
    [Range(0f, 1f)]
    public float yebisContrastMix = 1f;

    [Tooltip("0 = contrast+bloom, 1 = contrast only, 2 = bloom only.")]
    public int yebisDebugMode;

    [Tooltip("Env.int [Yebis] fExposure.")]
    public float yebisExposure = 0.5f;

    [Tooltip("Env.int fMappingFactor0. Contrast uses this (18). 10/4 are glare layers, not a day/night switch.")]
    public float yebisMappingFactor0 = 18f;

    [Tooltip("Env.int fMappingFactor1 (glare layer, not contrast).")]
    public float yebisMappingFactor1 = 10f;

    [Tooltip("Env.int fMappingFactor2 (glare layer, not contrast).")]
    public float yebisMappingFactor2 = 4f;

    [Tooltip("Env.int fMiddleGray.")]
    public float yebisMiddleGray = 0.18f;

    [Tooltip("Env.int fAdaptationSensitivity. Eye speed into the 1x1 luma.")]
    [Range(0f, 1f)]
    public float yebisAdaptationSensitivity = 0.5f;

    [Tooltip("Env.int fAdaptationScale.")]
    [Range(0f, 1f)]
    public float yebisAdaptationScale = 0.85f;

    [Tooltip("Unity pre-Yebis vs L2 luma. 1 = off. Do not use to hide a dead adapt loop.")]
    public float yebisPrePostLumaScale = 1f;

    [Tooltip("If > 0, Grade uses this adapted luma (0.37 = L2 dark-room capture). 0 = live 8x8.")]
    public float yebisAdaptedOverride = 0f;

    [Tooltip("Env.int fColorGammaValue. 1.6 on L2 combine (f[12]=0.625). Option.ini 0.8 is display after Yebis, not this.")]
    public float yebisColorGamma = 1.6f;

    [Tooltip("Env.int fColorSaturation. Combine cbuffer 0.5 washed fire; 0.95 is the look.")]
    [Range(0f, 2f)]
    public float yebisColorSaturation = 0.95f;

    [Tooltip("EID 4524 c.f[14].x filmic constant.")]
    public float yebisFilmicA = 2.6581414f;

    [Tooltip("EID 4524 c.f[14].y filmic constant.")]
    public float yebisFilmicB = 0.66499311f;

    [Tooltip("EID 4524 matrix w bias.")]
    public float yebisMatrixBias = 0.005f;

    [Tooltip("Numerical floor only. Do not pin to 1 — that was the 2.1 hall hack.")]
    public float yebisGainMin = 0.01f;

    [Tooltip("Clamp for the unused absolute gain. The exposure test uses a ratio and does not apply this cap.")]
    public float yebisGainMax = 8f;

    [Tooltip("EID 3898 before combine. 1 = village 12:00 path. 0 = raw scene into 4.76 (dark-room dump).")]
    public float yebisPre3898 = 1f;

    [Tooltip("Previous look, used only when FirstMatrix is 0. One gain on R, G and B: 4.7551184.")]
    public float yebisCombineGain = 4.7551184f;

    // Test 2026-09-27. Live client log (room and street) uploaded the same 3x3 as RenderDoc EID 4833.
    // Before this test: 0. Grade did sat * CombineGain 4.7551184 on every channel.
    // This test: 1. Grade uses L2Yebis_FirstMatrix (R 4.7551184, G 4.8795118, B 4.7203693 plus off-diagonal terms).
    // Put 0 back to restore the old scalar path. URP_Renderer.asset must match this value; the asset overrides the field default.
    [Tooltip("Test. 1 = full EID 4833 matrix (R 4.755, G 4.880, B 4.720). 0 = old path, CombineGain 4.755 on R, G and B.")]
    public float yebisFirstMatrix = 1f;

    [Tooltip("EID 4833 f[7..9] after filmic (~0.95). 1 = on. Softens mids without sat 0.5 wash.")]
    public float yebisSecondMatrix = 1f;

    [Tooltip("Option.ini Gamma after Yebis. Off on Unity (already Gamma). 0.8 crushed mids.")]
    public float yebisDisplayGamma = 1f;

    [Tooltip("Luma cutoff on the graded look. 0.66 = 0.6 + 10%.")]
    [Range(0f, 1f)]
    public float yebisBloomThreshold = 0.66f;

    [Tooltip("Soft UNORM stand-in for fGlareRemapFactor=32 (32 clips 8-bit).")]
    public float yebisBloomRemap = 1.25f;

    [Tooltip("Add after grade. 0 = off.")]
    public float yebisBloomIntensity = 0.18f;

    [Tooltip("Cut the sky dome, clouds and sun out of bloom extract. Haze and moon stay.")]
    public bool yebisBloomExcludeSkyCloudsSun = true;

    // Test 2026-09-27. Live log: the color matrix stayed fixed while a scalar exposure moved
    // about 1.18..7.17 between the room and the street.
    // Before this test: 0. The grade ignored adapted luma. Day and night kept the raw sun difference.
    // This test: 1. Frames darker than middle gray 0.18 are lifted, then the same matrix.
    // Bright sky is not allowed to darken the ground (that was the look-up / tree-shadow dip).
    // Put 0 back to restore the old fixed grade. URP_Renderer.asset must match this value.
    [Tooltip("Test. 1 = lift frames darker than middle gray 0.18. Bright sky does not darken the ground. 0 = old fixed grade, adapted luma ignored.")]
    public float yebisUseExposure = 1f;
}
