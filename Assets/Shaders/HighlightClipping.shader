// ============================================================
// Highlight Clipping Shader — Custom/HighlightClipping
// Shader técnico de seleção com clipping plane controlável
// Efeitos: Highlight, Fresnel, Depth Fade
// Compatível com URP (Universal Render Pipeline)
// HLSL puro — sem Shader Graph
// ============================================================
Shader "Custom/HighlightClipping"
{
    Properties
    {
        _HighlightColor     ("Highlight Color", Color)            = (1, 1, 0, 1)
        _HighlightIntensity ("Intensity", Range(0, 5))            = 1.0
        _ClipPlaneHeight    ("Clip Plane Height", Range(-5, 5))   = 0.0
        _FresnelColor       ("Fresnel Color", Color)              = (1, 1, 1, 1)
        _FresnelPower       ("Fresnel Power", Range(0.1, 10))     = 2.0
        _FresnelIntensity   ("Fresnel Intensity", Range(0, 3))    = 1.0
        _DepthFadeDistance  ("Depth Fade Distance", Range(0.1, 5)) = 1.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"        // Renderiza após objetos sólidos (Queue 3000)
            "RenderPipeline" = "UniversalPipeline"  // Identifica o shader como URP
        }

        Blend SrcAlpha OneMinusSrcAlpha // Fórmula da transparência: cor_final = cor × alpha + cor_atrás × (1 - alpha)
        ZWrite Off                      // Não escreve no depth buffer — correto para transparentes
        Cull Back                       // Descarta faces traseiras por otimização

        Pass
        {
            Name "HighlightPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Biblioteca principal do URP
            // Contém: TransformObjectToWorld, TransformWorldToHClip,
            // TransformObjectToWorldNormal, GetWorldSpaceViewDir, etc.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // CBUFFER — agrupa variáveis numa estrutura de memória na GPU
            // Obrigatório para compatibilidade com SRP Batcher do URP
            // Sem isso o SRP Batcher ignora o shader e performance é reduzida
            CBUFFER_START(UnityPerMaterial)
                float4 _HighlightColor;
                float  _HighlightIntensity;
                float  _ClipPlaneHeight;
                float4 _FresnelColor;
                float  _FresnelPower;
                float  _FresnelIntensity;
                float  _DepthFadeDistance;
            CBUFFER_END

            // Textura de profundidade da cena — fornecida automaticamente pelo URP
            // Requer Depth Texture ativado no URP Asset
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            // Dados de entrada do mesh (por vértice)
            struct Attributes
            {
                float4 positionOS : POSITION; // Posição em Object Space
                float3 normalOS   : NORMAL;   // Normal em Object Space — necessária para Fresnel
            };

            // Dados interpolados passados do vertex para o fragment shader
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // Posição em Clip Space (tela)
                float3 positionWS  : TEXCOORD0;   // Posição em World Space — usada no clipping
                float3 normalWS    : TEXCOORD1;   // Normal em World Space — usada no Fresnel
                float3 viewDirWS   : TEXCOORD2;   // Direção da câmera — usada no Fresnel
                float4 screenPos   : TEXCOORD3;   // Posição de tela — usada no Depth Fade
            };

            // =====================
            // VERTEX SHADER
            // Executado uma vez por vértice
            // Responsável pelas transformações de espaço
            // =====================
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Object Space → World Space (posição absoluta na cena)
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);

                // World Space → Clip Space (posição na tela)
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);

                // Normal: Object Space → World Space
                // Necessário para o cálculo correto do Fresnel
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // Direção da câmera até o vértice — usada no dot product do Fresnel
                OUT.viewDirWS = GetWorldSpaceViewDir(OUT.positionWS);

                // Posição de tela normalizada — usada para samplear a depth texture
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);

                return OUT;
            }

            // =====================
            // FRAGMENT SHADER
            // Executado uma vez por pixel
            // Responsável pela cor final de cada pixel
            // =====================
            half4 frag(Varyings IN) : SV_Target
            {
                // ── CLIPPING PLANE ──────────────────────────────────────────
                // clip() descarta o pixel se o valor for negativo
                // positionWS.y é a altura do pixel no mundo
                // Se altura < _ClipPlaneHeight → pixel descartado
                clip(IN.positionWS.y - _ClipPlaneHeight);

                // ── FRESNEL ─────────────────────────────────────────────────
                // Efeito de brilho nas bordas baseado no ângulo de visão
                // dot(normal, viewDir) → 1 no centro, 0 nas bordas
                // 1 - dot → invertido: 0 no centro, 1 nas bordas
                float3 normal  = normalize(IN.normalWS);
                float3 viewDir = normalize(IN.viewDirWS);

                float fresnel = 1.0 - saturate(dot(normal, viewDir));
                // pow controla dureza da borda: alto = fina, baixo = larga
                fresnel = pow(fresnel, _FresnelPower) * _FresnelIntensity;

                // Cor base do highlight + brilho Fresnel nas bordas
                float3 finalColor = _HighlightColor.rgb * _HighlightIntensity;
                finalColor += _FresnelColor.rgb * fresnel;

                // ── DEPTH FADE ──────────────────────────────────────────────
                // Dissolve o objeto onde se intersecta com outras superfícies
                // Evita bordas duras de transparência

                // Coordenadas UV normalizadas na tela (0 a 1)
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // Profundidade da cena nesse pixel (o que está "atrás")
                float sceneDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV).r;
                // Converte valor raw para distância linear em unidades do mundo
                sceneDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);

                // Profundidade do pixel atual do shader
                float pixelDepth = IN.screenPos.w;

                // Diferença entre as profundidades
                // Perto de 0 = intersectando → fade
                // Maior = distante → opaco
                float depthDiff = sceneDepth - pixelDepth;
                float fade = saturate(depthDiff / _DepthFadeDistance);

                // Aplica fade no alpha — dissolve nas bordas de intersecção
                float finalAlpha = _HighlightColor.a * fade;

                return half4(finalColor, finalAlpha);
            }

            ENDHLSL
        }
    }
}