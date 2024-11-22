using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class dxr_script : MonoBehaviour
{
    RayTracingAccelerationStructure accelerationStructure;
    RayTracingAccelerationStructure.Settings settings;

    public Renderer[] targets;

    public RayTracingShader rayTracingShader;

    Camera c;

    public RenderTexture output;

    public RawImage image;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        c = Camera.main;

        output = new RenderTexture(c.pixelWidth, c.pixelHeight, 0);
        output.enableRandomWrite = true;
        output.Create();

        settings = new RayTracingAccelerationStructure.Settings();
        settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Manual;
        settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;

        accelerationStructure = new RayTracingAccelerationStructure(settings);
        foreach (var r in targets)
        {
            RayTracingSubMeshFlags[] subMeshFlags = { RayTracingSubMeshFlags.Enabled };
            accelerationStructure.AddInstance(r, subMeshFlags);
        }
        accelerationStructure.Build();

        rayTracingShader.SetAccelerationStructure("g_SceneAccelStruct", accelerationStructure);
        rayTracingShader.SetFloat("g_Zoom", Mathf.Tan(Mathf.Deg2Rad * c.fieldOfView * 0.5f));
        rayTracingShader.SetTexture("g_Output", output);
        rayTracingShader.SetShaderPass("Test");

        image.texture = output;
    }

    // Update is called once per frame
    void Update()
    {
        foreach (var r in targets)
        {
            accelerationStructure.UpdateInstanceTransform(r);
        }

        accelerationStructure.Build();

        rayTracingShader.Dispatch("MainRayGenShader", c.pixelWidth, c.pixelHeight, 1, c);
    }
}
