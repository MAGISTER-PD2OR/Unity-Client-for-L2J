using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Reconstructs the original L2 colour-pass sky.
/// Upper sky / sun / clouds stay behind world depth. Hard lower haze
/// matches EID 1475 and fills the whole below-horizon buffer.
/// </summary>
public sealed class L2SkyRenderPass : ScriptableRenderPass
{
    static readonly int D3D9ActiveId = Shader.PropertyToID("_L2FxD3D9CompositorActive");
    static readonly int FxGainId = Shader.PropertyToID("_FxGain");
    static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
    static readonly int LowerHazeColorId = Shader.PropertyToID("_LowerHazeColor");
    static readonly ShaderTagId ForwardOnlyTag = new ShaderTagId("UniversalForwardOnly");
    static readonly ProfilingSampler Sampler = new ProfilingSampler("L2 Original Sky Layers");

    readonly L2FxCompositorSettings _settings;
    Material _backdropMaterial;

    public L2SkyRenderPass(L2FxCompositorSettings settings)
    {
        _settings = settings;
        // Deferred opaque depth/color is ready here. Sky shaders force far-plane
        // depth, so every world surface remains in front of the backdrop.
        renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!ShouldRun(ref renderingData))
            return;

        EnsureBackdropMaterial();
        RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
        RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
        if (cameraColor == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("L2 Original Sky Layers");
        using (new ProfilingScope(cmd, Sampler))
        {
            if (cameraDepth != null)
                CoreUtils.SetRenderTarget(cmd, cameraColor, cameraDepth, ClearFlag.None, Color.clear);
            else
                CoreUtils.SetRenderTarget(cmd, cameraColor, ClearFlag.None, Color.clear);
            cmd.SetGlobalFloat(D3D9ActiveId, 1f);
            cmd.SetGlobalFloat(FxGainId, 1f);
            // Globals only — no blit. Floor already used them in the opaque pass.

            if (_backdropMaterial != null)
            {
                _backdropMaterial.SetVector(SkyColorId, ResolveSkyColor(renderingData.cameraData.camera));
                _backdropMaterial.SetVector(LowerHazeColorId, L2HazeRing.CurrentTint);
                cmd.DrawProcedural(
                    Matrix4x4.identity,
                    _backdropMaterial,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1);
            }
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // Original: black lower sky (camera clear) -> upper blue -> sun ->
        // hard mauve (slight overlap) -> stars/clouds -> soft WhiteRing.
        DrawLayer(
            context,
            ref renderingData,
            _settings.celestialLayerMask,
            QueueRange(3000, 5000));

        if (_backdropMaterial != null)
        {
            // Colour only: original 1475 fills every below-horizon pixel,
            // then later world drawing covers it. Binding deferred depth
            // here left the lower clear black except a horizon strip.
            CoreUtils.SetRenderTarget(cmd, cameraColor, ClearFlag.None, Color.clear);
            cmd.DrawProcedural(
                Matrix4x4.identity,
                _backdropMaterial,
                1,
                MeshTopology.Triangles,
                3,
                1);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            if (cameraDepth != null)
                CoreUtils.SetRenderTarget(cmd, cameraColor, cameraDepth, ClearFlag.None, Color.clear);
        }

        DrawLayer(
            context,
            ref renderingData,
            _settings.celestialLayerMask,
            QueueRange(2501, 2999));
        DrawLayer(context, ref renderingData, _settings.cloudLayerMask);
        DrawLayer(context, ref renderingData, _settings.hazeLayerMask);

        cmd.SetGlobalFloat(D3D9ActiveId, 0f);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    void DrawLayer(
        ScriptableRenderContext context,
        ref RenderingData renderingData,
        LayerMask layerMask,
        RenderQueueRange? queueRange = null)
    {
        if (layerMask.value == 0)
            return;

        var filtering = new FilteringSettings(
            queueRange ?? RenderQueueRange.transparent,
            layerMask);
        DrawingSettings drawing = CreateDrawingSettings(
            ForwardOnlyTag,
            ref renderingData,
            SortingCriteria.CommonTransparent);
        drawing.perObjectData = PerObjectData.None;
        context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
    }

    static RenderQueueRange QueueRange(int lower, int upper)
    {
        return new RenderQueueRange
        {
            lowerBound = lower,
            upperBound = upper
        };
    }

    static Color ResolveSkyColor(Camera camera)
    {
        Material skybox = RenderSettings.skybox;
        if (skybox != null && skybox.HasProperty("_GradientColor1"))
            return skybox.GetColor("_GradientColor1");

        return camera != null ? camera.backgroundColor : Color.black;
    }

    public void Dispose()
    {
        CoreUtils.Destroy(_backdropMaterial);
        _backdropMaterial = null;
    }

    void EnsureBackdropMaterial()
    {
        if (_backdropMaterial != null)
            return;

        Shader backdropShader = Shader.Find("Hidden/L2/SkyBackdrop");
        if (backdropShader != null)
            _backdropMaterial = CoreUtils.CreateEngineMaterial(backdropShader);
    }

    bool ShouldRun(ref RenderingData renderingData)
    {
        if (_settings == null ||
            !_settings.enableD3D9Compositor ||
            !_settings.directCameraColor)
        {
            return false;
        }

        CameraType type = renderingData.cameraData.camera.cameraType;
        if (_settings.gameCameraOnly &&
            type != CameraType.Game &&
            type != CameraType.VR)
        {
            return false;
        }

        Camera renderingCamera = renderingData.cameraData.camera;
        CameraController controller = CameraController.Instance;
        if (controller == null || !controller.isActiveAndEnabled)
            return false;

        Camera worldCamera = controller.GetComponent<Camera>();
        if (worldCamera == null)
            worldCamera = controller.GetComponentInChildren<Camera>();

        return worldCamera != null &&
               worldCamera.enabled &&
               worldCamera.gameObject.activeInHierarchy &&
               renderingCamera == worldCamera;
    }
}
