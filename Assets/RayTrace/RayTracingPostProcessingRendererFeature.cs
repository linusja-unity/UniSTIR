using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using GraphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat;

public class RayTracingPostProcessingRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Cubemap skybox;
    [SerializeField] private RayTracingShader rayTracingShader;

    [SerializeField] private Material defaultRayTracingMaterial;

    private RayTracingPostProcessingRenderPass rayTracingRenderPass;

    public override void Create()
    {
        if (rayTracingShader == null || defaultRayTracingMaterial == null)
        {
            return;
        }
        rayTracingRenderPass = new RayTracingPostProcessingRenderPass(rayTracingShader,
                                                                      defaultRayTracingMaterial,
                                                                      skybox);

        rayTracingRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (rayTracingRenderPass == null)
        {
            return;
        }

        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            rayTracingRenderPass.ConfigureInput(ScriptableRenderPassInput.Color |
                                                ScriptableRenderPassInput.Normal |
                                                ScriptableRenderPassInput.Depth |
                                                ScriptableRenderPassInput.Motion);
            renderer.EnqueuePass(rayTracingRenderPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
    }
}

public class RayTracingPostProcessingRenderPass : ScriptableRenderPass
{
    private Cubemap skybox;
    private RayTracingShader rayTracingShader;

    private RayTracingAccelerationStructure accelerationStructure;
    private RayTracingAccelerationStructure.Settings rtasSettings;

    private Renderer[] renderers;

    private Vector2Int currDims = new Vector2Int(0, 0);

    private RTHandleSystem renderTexHandleSystem = new RTHandleSystem();
    private RTHandle renderTexHandle;

    public RayTracingPostProcessingRenderPass(RayTracingShader rayTracingShader,
        Material defaultRayTracingMaterial, Cubemap skybox)
    {
        this.skybox = skybox;
        this.rayTracingShader = rayTracingShader;

        rtasSettings = new RayTracingAccelerationStructure.Settings();
        rtasSettings.managementMode = RayTracingAccelerationStructure.ManagementMode.Manual;
        rtasSettings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;

        accelerationStructure = new RayTracingAccelerationStructure(rtasSettings);

        RayTracingSubMeshFlags[] subMeshFlags = { RayTracingSubMeshFlags.Enabled };

        renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID);
        foreach (var r in renderers)
        {
            accelerationStructure.AddInstance(r, subMeshFlags);
            if (r.sharedMaterial == null || r.sharedMaterial.shader != defaultRayTracingMaterial.shader)
                r.sharedMaterial = defaultRayTracingMaterial;
        }
        accelerationStructure.Build();

        renderTexHandleSystem.Initialize(1, 1);

        // This is set so the shader knows what shader code to use using the subpass
        rayTracingShader.SetShaderPass("RayTracingPass");
    }

    private class PassData
    {
        // Inputs
        internal Camera camera;
        internal RayTracingAccelerationStructure accelerationStructure;
        internal RayTracingShader rayTracingShader;
        internal TextureHandle albedoTexture;
        internal TextureHandle normalSmoothnessTexture;
        internal TextureHandle depthsTexture;
        internal TextureHandle motionTexture;

        // Output
        internal TextureHandle outputBuffer;
    }

    static void ComputePass(PassData data, ComputeGraphContext context)
    {
        Camera c = data.camera;
        RayTracingShader rayTracingShader = data.rayTracingShader;
        ComputeCommandBuffer cmd = context.cmd;

        rayTracingShader.SetShaderPass("RayTracingPass");
        cmd.BuildRayTracingAccelerationStructure(data.accelerationStructure);

        cmd.SetRayTracingAccelerationStructure(rayTracingShader, "g_SceneAccelStruct", data.accelerationStructure);
        cmd.SetRayTracingTextureParam(rayTracingShader, "_Albedo", data.albedoTexture);
        cmd.SetRayTracingTextureParam(rayTracingShader, "_NormalSmoothness", data.normalSmoothnessTexture);
        cmd.SetRayTracingTextureParam(rayTracingShader, "_Depths", data.depthsTexture);
        cmd.SetRayTracingTextureParam(rayTracingShader, "_Motion", data.motionTexture);
        cmd.SetRayTracingFloatParam(rayTracingShader, "g_Zoom", Mathf.Tan(Mathf.Deg2Rad * c.fieldOfView * 0.5f));
        cmd.SetRayTracingIntParam(rayTracingShader, "g_ConvergenceStep", 0);
        cmd.SetRayTracingIntParam(rayTracingShader, "g_FrameIndex", Time.frameCount);

        cmd.SetRayTracingTextureParam(rayTracingShader, "g_Output", data.outputBuffer);

        cmd.DispatchRays(rayTracingShader, "MainRayGenShader", (uint)c.pixelWidth, (uint)c.pixelHeight, 1, c);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        // The following line ensures that the render pass doesn't blit
        // from the back buffer.
        if (resourceData.isActiveTargetBackBuffer)
            return;

        Camera c = cameraData.camera;
        Vector2Int trgtDims = new Vector2Int(c.pixelHeight, c.pixelWidth);
        if (currDims != trgtDims)
        {

            renderTexHandleSystem.SetReferenceSize(c.pixelWidth, c.pixelHeight, true);
            renderTexHandle = renderTexHandleSystem.Alloc(c.pixelWidth, c.pixelHeight,
                colorFormat: GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite: true, name: "RTRT_Output");
            currDims = trgtDims;
        }

        if (renderTexHandle == null)
            return;

        foreach (var r in renderers)
        {
            accelerationStructure.UpdateInstanceTransform(r);
        }
        accelerationStructure.Build();

        RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
        desc.msaaSamples = 1;
        desc.depthBufferBits = 0;

        TextureHandle output = renderGraph.ImportTexture(renderTexHandle);
        using (var builder = renderGraph.AddComputePass(passName, out PassData data))
        {
            data.camera = c;
            data.rayTracingShader = rayTracingShader;
            data.accelerationStructure = accelerationStructure;
            data.albedoTexture = resourceData.gBuffer[0];
            data.normalSmoothnessTexture = resourceData.cameraNormalsTexture;
            data.depthsTexture = resourceData.cameraDepthTexture;
            data.motionTexture = resourceData.motionVectorColor;
            builder.UseTexture(data.albedoTexture);
            builder.UseTexture(data.normalSmoothnessTexture);
            builder.UseTexture(data.depthsTexture);
            builder.UseTexture(data.motionTexture);

            data.outputBuffer = output;
            builder.UseTexture(data.outputBuffer, AccessFlags.ReadWrite);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData _data, ComputeGraphContext context) => ComputePass(_data, context));
        }

        renderGraph.AddCopyPass(output, resourceData.activeColorTexture);
    }
}
