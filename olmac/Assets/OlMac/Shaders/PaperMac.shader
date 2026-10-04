// Paper Mac: a flat drawing that always turns to face the player (round the vertical axis).
// Walks (banjo frames) when you're far away, flails (arms-out frames) when you're close.
// No C# at all, so it runs on Nomad without the scripting beta.
//
// Atlas: top row = walk frames, bottom row = attack frames (made by OlMacBuilder).
// The object's origin is where Mac is carried (his hip holster). _FeetDrop moves the
// drawing down from there to the floor.

Shader "OlMac/PaperMac"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        _Columns ("Atlas Columns", Float) = 6
        _Rows ("Atlas Rows", Float) = 2
        _WalkFrames ("Walk Frames", Float) = 5
        _AttackFrames ("Attack Frames", Float) = 6
        _FPS ("Frames Per Second", Float) = 8

        _AttackDistance ("Flail When Closer Than (m)", Float) = 2.2

        _Size ("Size (m)", Float) = 2.0
        _FeetDrop ("Feet Below Origin (m)", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "DisableBatching" = "True"
            "IgnoreProjector" = "True"
        }
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Cutoff;
                float _Columns;
                float _Rows;
                float _WalkFrames;
                float _AttackFrames;
                float _FPS;
                float _AttackDistance;
                float _Size;
                float _FeetDrop;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_OUTPUT(Varyings, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Feet = holster position dropped straight down
                float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0)) - float3(0, _FeetDrop, 0);

                // Face the camera, stay upright
                float3 toCam = _WorldSpaceCameraPos - pivotWS;
                toCam.y = 0;
                float dist = length(toCam);
                toCam = (dist > 0.0001) ? toCam / dist : float3(0, 0, 1);
                float3 right = normalize(cross(float3(0, 1, 0), toCam));

                // UVs (0..1) pick the corner, so the mesh shape doesn't matter
                float2 corner = IN.uv - float2(0.5, 0.0);
                float3 posWS = pivotWS + right * corner.x * _Size + float3(0, 1, 0) * corner.y * _Size;
                OUT.positionCS = TransformWorldToHClip(posWS);

                // Close = attack frames (bottom row), far = walk frames (top row)
                bool attacking = dist < _AttackDistance;
                float frames = attacking ? _AttackFrames : _WalkFrames;
                float row = attacking ? 1 : 0;
                float frame = floor(fmod(_Time.y * _FPS, frames));

                float2 cell = float2(1.0 / _Columns, 1.0 / _Rows);
                float2 uv = IN.uv * cell;
                uv.x += frame * cell.x;
                uv.y += (_Rows - 1 - row) * cell.y; // texture V starts at the bottom
                OUT.uv = uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;
                clip(col.a - _Cutoff);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
