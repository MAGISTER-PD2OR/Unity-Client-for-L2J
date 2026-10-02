using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP feature: L2 D3D9-compatible FX compositor (raw UNORM + PTDS blends).
/// </summary>
public sealed class L2FxCompositorRendererFeature : ScriptableRendererFeature
{
    public L2FxCompositorSettings settings = new L2FxCompositorSettings();

    [SerializeField]
    Shader transferShader;

    [SerializeField]
    Shader postShader;

    [SerializeField]
    Shader yebisShader;

    Material _transferMaterial;
    Material _postMaterial;
    Material _yebisMaterial;
    L2SkyRenderPass _skyPass;
    L2FxCompositorRenderPass _pass;
    L2YebisPostRenderPass _yebisPass;
    L2TerrainOverlayRenderPass _terrainOverlayPass;

    public override void Create()
    {
        if (transferShader == null)
            transferShader = Shader.Find("Hidden/L2/FxColorTransfer");
        if (postShader == null)
            postShader = Shader.Find("Hidden/L2/FxPostBloomContrast");
        if (yebisShader == null)
            yebisShader = Shader.Find("Hidden/L2/YebisPost");

        if (transferShader != null)
        {
            if (_transferMaterial == null || _transferMaterial.shader != transferShader)
                _transferMaterial = CoreUtils.CreateEngineMaterial(transferShader);
        }

        if (postShader != null)
        {
            if (_postMaterial == null || _postMaterial.shader != postShader)
                _postMaterial = CoreUtils.CreateEngineMaterial(postShader);
        }

        if (yebisShader != null)
        {
            if (_yebisMaterial == null || _yebisMaterial.shader != yebisShader)
                _yebisMaterial = CoreUtils.CreateEngineMaterial(yebisShader);
        }

        _skyPass?.Dispose();
        _pass?.Dispose();
        _yebisPass?.Dispose();
        _skyPass = new L2SkyRenderPass(settings);
        _terrainOverlayPass = new L2TerrainOverlayRenderPass();
        _pass = new L2FxCompositorRenderPass(settings, _transferMaterial, _postMaterial)
        {
            renderPassEvent = settings != null
                ? settings.renderPassEvent
                : RenderPassEvent.BeforeRenderingPostProcessing
        };
        _yebisPass = new L2YebisPostRenderPass(settings, _yebisMaterial)
        {
            renderPassEvent = settings != null
                ? settings.renderPassEvent
                : RenderPassEvent.BeforeRenderingPostProcessing
        };

        bool prefer = settings != null && settings.enableD3D9Compositor;
        L2FxCompositorRuntime.SetPreferGpuQueue(prefer && isActive);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        bool prefer = settings != null && settings.enableD3D9Compositor && isActive;
        L2FxCompositorRuntime.SetPreferGpuQueue(prefer);

        if (_terrainOverlayPass != null)
            renderer.EnqueuePass(_terrainOverlayPass);

        bool yebis = WantYebis(ref renderingData);
        L2YebisPostRuntime.WillFlushNameplates = yebis;

        if (!prefer && !yebis)
            return;

        if (prefer)
        {
            bool allowCompositor = true;
            if (settings.gameCameraOnly)
            {
                CameraType type = renderingData.cameraData.camera.cameraType;
                if (type != CameraType.Game && type != CameraType.VR)
                    allowCompositor = false;
            }

            if (allowCompositor)
            {
                if (!settings.directCameraColor && _transferMaterial == null)
                {
                    if (!yebis)
                        return;
                }
                else
                {
                    if (settings.directCameraColor)
                        renderer.EnqueuePass(_skyPass);

                    _pass.renderPassEvent = settings.renderPassEvent;
                    renderer.EnqueuePass(_pass);
                }
            }
        }

        if (yebis && _yebisPass != null && _yebisMaterial != null)
        {
            _yebisPass.renderPassEvent = settings.renderPassEvent;
            renderer.EnqueuePass(_yebisPass);
        }
    }

    bool WantYebis(ref RenderingData renderingData)
    {
        if (settings == null || !settings.enableYebisPost || !isActive || _yebisMaterial == null)
            return false;

        if (settings.gameCameraOnly)
        {
            CameraType type = renderingData.cameraData.camera.cameraType;
            if (type != CameraType.Game && type != CameraType.VR)
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

    protected override void Dispose(bool disposing)
    {
        _skyPass?.Dispose();
        _pass?.Dispose();
        _yebisPass?.Dispose();
        _pass = null;
        _skyPass = null;
        _yebisPass = null;
        _terrainOverlayPass = null;
        CoreUtils.Destroy(_transferMaterial);
        _transferMaterial = null;
        CoreUtils.Destroy(_postMaterial);
        _postMaterial = null;
        CoreUtils.Destroy(_yebisMaterial);
        _yebisMaterial = null;
        L2FxGpuDrawQueue.Clear();
        L2NameplateOverlayQueue.Clear();
        base.Dispose(disposing);
    }
}
