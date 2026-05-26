// ============================================================
// XRay Highlight Shader — Custom/XRayHighlight
// Renderiza o objeto onde está ATRÁS de outros objetos (X-Ray)
// Aplicado como segundo material no Mesh Renderer
// Compatível com URP (Universal Render Pipeline)
// ============================================================
Shader "Custom/XRayHighlight"
{
    Properties
    {
        _XRayColor ("XRay Color", Color) = (0, 1, 1, 0.2)
        _ClipPlaneHeight ("Clip Plane Height", Range(-5, 5)) = 0.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent"
            "Queue" = "Transparent+1"       // Renderiza após o highlight principal
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            // ZTest Greater — renderiza APENAS onde o objeto está atrás de geometria
            // Inverso do comportamento padrão (LEqual)
            ZTest Greater
            ZWrite Off                      // Não escreve no depth buffer — objeto transparente
            Blend SrcAlpha OneMinusSrcAlpha // Transparência padrão
            Cull Back                       // Descarta faces traseiras

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Biblioteca principal do URP — funções de transformação de espaço
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // CBUFFER — agrupa variáveis para compatibilidade com SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _XRayColor;
                float _ClipPlaneHeight;
            CBUFFER_END

            // Dados de entrada do mesh (por vértice)
            struct Attributes
            {
                float4 positionOS : POSITION; // Posição em Object Space
            };

            // Dados passados do vertex shader para o fragment shader
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // Posição em Clip Space (tela)
                float3 positionWS  : TEXCOORD0;   // Posição em World Space (para clipping)
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // Converte posição: Object Space → World Space → Clip Space
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Clipping plane — descarta pixels abaixo da altura definida
                // Usando World Space para altura absoluta na cena
                clip(IN.positionWS.y - _ClipPlaneHeight);

                // Retorna cor X-Ray flat — sem cálculo de iluminação (Unlit)
                return _XRayColor;
            }
            ENDHLSL
        }
    }
}