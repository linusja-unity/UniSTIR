using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class RayTracingPostProcessingRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private RayTracingShader rayTracingShader;

    [SerializeField] private RenderTexture output;

    private RayTracingPostProcessingRenderPass rayTracingRenderPass;

    public override void Create()
    {
        if (rayTracingShader == null)
        {
            return;
        }
        rayTracingRenderPass = new RayTracingPostProcessingRenderPass(rayTracingShader);

        rayTracingRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
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
            renderer.EnqueuePass(rayTracingRenderPass);
            output = rayTracingRenderPass.output;
        }
    }

    protected override void Dispose(bool disposing)
    {
    }
}

public class RayTracingPostProcessingRenderPass : ScriptableRenderPass
{
    private RayTracingShader rayTracingShader;

    private RayTracingAccelerationStructure accelerationStructure;
    private RayTracingAccelerationStructure.Settings rtasSettings;

    private Renderer[] renderers;

    public RenderTexture output;

    public RayTracingPostProcessingRenderPass(RayTracingShader rayTracingShader)
    {
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
        }
        accelerationStructure.Build();

        rayTracingShader.SetAccelerationStructure("g_SceneAccelStruct", accelerationStructure);
        rayTracingShader.SetShaderPass("RayTracingPass");
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        Camera c = cameraData.camera;
        if (!output) {
            output = new RenderTexture(c.pixelWidth, c.pixelHeight, 0);
            output.enableRandomWrite = true;
            output.Create();

            rayTracingShader.SetFloat("g_Zoom", Mathf.Tan(Mathf.Deg2Rad * c.fieldOfView * 0.5f));
            rayTracingShader.SetTexture("g_Output", output);
        }

        // The following line ensures that the render pass doesn't blit
        // from the back buffer.
        if (resourceData.isActiveTargetBackBuffer)
            return;

        foreach (var r in renderers)
        {
            accelerationStructure.UpdateInstanceTransform(r);
        }
        accelerationStructure.Build();

        if (c.isActiveAndEnabled)
            rayTracingShader.Dispatch("MainRayGenShader", c.pixelWidth, c.pixelHeight, 1, c);
    }
}