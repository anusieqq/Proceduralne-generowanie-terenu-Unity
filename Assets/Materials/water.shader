Shader "Custom/URP_Transparent_Water_Clean"
{
    Properties
    {
        [Header(Visuals)]
        _BaseColor("Water Color", Color) = (0.1, 0.4, 0.7, 0.5)
        _Smoothness("Smoothness", Range(0,1)) = 0.85

        [Header(Texture)]
        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 1.5)) = 0.35

        [Header(UV Movement)]
        _ScrollSpeed("Scroll Direction", Vector) = (0.1, 0.05, 0, 0)
        _ScrollMultiplier("Scroll Speed", Range(0, 0.5)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            float4 _BaseColor;
            float _Smoothness;

            sampler2D _NormalMap;
            float _NormalStrength;

            float2 _ScrollSpeed;
            float _ScrollMultiplier;

            float4 _NormalMap_ST;

            Varyings vert(Attributes v)
            {
                Varyings o;

                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);

                o.positionCS = TransformWorldToHClip(worldPos);
                o.worldPos = worldPos;

                o.uv = TRANSFORM_TEX(v.uv, _NormalMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // ? P?YNNY SCROLL - bez frac(), bez skakania
                float time = _Time.y * _ScrollMultiplier;
                float2 uv = i.uv + _ScrollSpeed * time;

                // Normal map sampling z p?ynnym przesuwaniem
                float3 n = UnpackNormal(tex2D(_NormalMap, uv));
                float3 normalWS = normalize(i.normalWS + n * _NormalStrength);

                Light light = GetMainLight();

                float3 L = normalize(light.direction);
                float3 V = normalize(GetCameraPositionWS() - i.worldPos);

                float NdotL = saturate(dot(normalWS, L));

                float3 H = normalize(L + V);

                float specPower = exp2(10.0 * _Smoothness + 1.0);
                float spec = pow(saturate(dot(normalWS, H)), specPower);

                float3 color = _BaseColor.rgb * (0.35 + NdotL * 0.65);
                color += light.color * spec * _Smoothness;

                return half4(color, _BaseColor.a);
            }

            ENDHLSL
        }
    }
}