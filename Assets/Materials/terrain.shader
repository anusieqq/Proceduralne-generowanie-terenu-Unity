Shader "Custom/Terrain_Realistic_Fixed_V3_DetailNormals"
{
    Properties
    {
        [MainTexture] _BaseMap("Region Texture", 2D) = "white" {}
        
        [Header(Detail Normal Map)]
        _NormalMap("Detail Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 5)) = 1.0
        _NormalTiling("Normal Tiling", Float) = 50.0
        
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // Wsparcie dla cieni (Wa?ne!)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                // Deklaracja dla shadowCoord
                float4 shadowCoord : TEXCOORD4;
                float fogFactor : TEXCOORD5;
            };

            sampler2D _BaseMap;
            sampler2D _NormalMap;
            float _NormalStrength;
            float _NormalTiling;
            float _Smoothness;

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = vertexInput.positionCS;
                o.positionWS = vertexInput.positionWS;
                o.uv = v.uv;

                VertexNormalInputs normalInput = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.normalWS = normalInput.normalWS;
                o.tangentWS = float4(normalInput.tangentWS, v.tangentOS.w);

                // --- KLUCZOWA ZMIANA: Poprawne pobieranie wspó?rz?dnych cieni ---
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    o.shadowCoord = GetShadowCoord(vertexInput);
                #else
                    o.shadowCoord = float4(0, 0, 0, 0);
                #endif

                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Albedo (Twoje blendowanie)
                float2 uv = i.uv;
                float2 offset = float2(0.002, 0.002);
                float3 albedo = (tex2D(_BaseMap, uv).rgb + 
                                 tex2D(_BaseMap, uv + float2(offset.x, 0)).rgb + 
                                 tex2D(_BaseMap, uv + float2(0, offset.y)).rgb + 
                                 tex2D(_BaseMap, uv - offset).rgb) * 0.25;

                // Normal Map
                float2 detailUV = i.uv * _NormalTiling;
                float3 nTex = UnpackNormal(tex2D(_NormalMap, detailUV));

                // TBN
                float3 bitangent = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float3x3 TBN = float3x3(i.tangentWS.xyz, bitangent, i.normalWS);
                float3 normalWS = normalize(mul(nTex * float3(_NormalStrength, _NormalStrength, 1.0), TBN));

                // --- O?WIETLENIE I CIENIE ---
                // Przeliczenie shadowCoord dla fragmentu
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                
                // shadowAttenuation to warto?? od 0 do 1 (gdzie 0 to pe?ny cie?)
                float shadow = light.shadowAttenuation;
                
                float NdotL = saturate(dot(normalWS, light.direction));
                float3 diffuse = albedo * light.color * (NdotL * shadow);

                // Ambient
                float3 ambient = SampleSH(normalWS) * albedo;

                // Specular
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float3 H = normalize(light.direction + V);
                float spec = pow(saturate(dot(normalWS, H)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness * shadow;

                float3 finalColor = diffuse + ambient + (spec * light.color);

                // Mg?a
                finalColor = MixFog(finalColor, i.fogFactor);

                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
        
        // Pass dla rzucania w?asnego cienia (je?li mesh mia?by rzuca? cie? na inne obiekty)
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}