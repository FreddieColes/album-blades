// Draws nothing and casts no shadow. Used for the human body under Paper Mac and the banjo hitbox.
Shader "OlMac/Invisible"
{
    Properties
    {
        _BaseColor ("Unused", Color) = (1,1,1,1)
        _BaseMap ("Unused", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Nothing"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_OUTPUT(Varyings, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = float4(0, 0, -10, 1); // pushed off screen
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                clip(-1);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
