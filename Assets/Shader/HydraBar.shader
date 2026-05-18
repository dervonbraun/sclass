Shader "UI/HydraBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _Count("Effect Count", Int) = 0
        _NoiseScale("Noise Scale", Float) = 15.0
        _NoiseSpeed("Noise Speed", Float) = 0.5
        _NoiseIntensity("Noise Intensity", Float) = 0.03
        _BlendSoftness("Blend Softness", Float) = 0.05
        
        // Stencil properties for UI masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            
            int _Count;
            float _NoiseScale;
            float _NoiseSpeed;
            float _NoiseIntensity;
            float _BlendSoftness;
            
            // До 16 цветов (оптимально для константных массивов)
            float4 _EffectColors[16];
            float _EffectBorders[16]; 

            // Простой 2D псевдо-рандом
            float random (float2 uv) {
                return frac(sin(dot(uv,float2(12.9898,78.233)))*43758.5453123);
            }

            // Простой шум Value Noise
            float noise (float2 uv) {
                float2 i = floor(uv);
                float2 f = frac(uv);

                float a = random(i);
                float b = random(i + float2(1.0, 0.0));
                float c = random(i + float2(0.0, 1.0));
                float d = random(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(a, b, u.x) +
                        (c - a)* u.y * (1.0 - u.x) +
                        (d - b) * u.x * u.y;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Базовый цвет спрайта UI (для маски)
                half4 color = tex2D(_MainTex, IN.texcoord) * IN.color;

                if (_Count <= 0) return fixed4(0,0,0,0);

                // Генерация шума для эффекта переливания "жидкости"
                float2 noiseUV = IN.texcoord * _NoiseScale + float2(_Time.y * _NoiseSpeed, _Time.x * _NoiseSpeed);
                float n = noise(noiseUV) * 2.0 - 1.0; 
                
                // Искажаем X-координату с помощью шума
                float xPos = IN.texcoord.x + n * _NoiseIntensity;
                xPos = clamp(xPos, 0.0, 1.0); 

                float4 finalColor = _EffectColors[0];
                
                // Плавное смешивание цветов на границах
                for (int i = 0; i < 15; i++)
                {
                    if (i >= _Count - 1) break; 
                    
                    float border = _EffectBorders[i];
                    
                    // Smoothstep делает границу мягкой
                    float blend = smoothstep(border - _BlendSoftness, border + _BlendSoftness, xPos);
                    
                    finalColor = lerp(finalColor, _EffectColors[i + 1], blend);
                }

                // Затемнение внизу для придания "объема"
                finalColor.rgb *= lerp(0.5, 1.0, IN.texcoord.y);

                color.rgb *= finalColor.rgb;
                color.a *= finalColor.a;

                // Для UI масок, чтобы прозрачность работала корректно
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
