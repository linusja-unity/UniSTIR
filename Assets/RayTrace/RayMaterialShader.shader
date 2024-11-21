Shader "Unlit/RayMaterialShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    SubShader
    {
        Pass
        {
            // RayTracingShader.SetShaderPass must use this name in order to execute the ray tracing shaders from this Pass.
            Name "Test"

            // Add tags to identify the shaders to use for ray tracing.
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

            #pragma multi_compile_local RAY_TRACING_PROCEDURAL_GEOMETRY

            // Specify this shader is a raytracing shader.
            #pragma raytracing test

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct RayPayload
            {
                float4 color;
            };

    #if RAY_TRACING_PROCEDURAL_GEOMETRY
            [shader("intersection")]
            void IntersectionMain()
            {
                AttributeData attr;
                attr.barycentrics = float2(0, 0);
                ReportHit(0, 0, attr);
            }
    #endif

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                float hitT = RayTCurrent();
                float3 rayDirW = WorldRayDirection();
                float3 rayOriginW = WorldRayOrigin();
                float3 barycentrics = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);

                payload.color = float4(barycentrics, 1);
            }

            ENDHLSL
        }
    }
}
