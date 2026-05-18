Shader "UI/AnimatedBorderGlow"
{
    Properties
    {
        // Обязательные свойства для UI.Image
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        // Маска из Figma
        _MaskTex ("Mask Texture (Figma)", 2D) = "white" {}
        
        // Три цвета для плавного перетекания
        _ColorA ("Color A", Color) = (0.0, 0.5, 1.0, 1)   // Синий
        _ColorB ("Color B", Color) = (0.0, 1.0, 0.5, 1)   // Бирюзовый
        _ColorC ("Color C", Color) = (0.6, 0.0, 1.0, 1)   // Фиолетовый
        
        _AnimationSpeed  ("Animation Speed",   Float)        = 1.0
        _FlowScale       ("Flow Scale",        Float)        = 2.5   // Частота волн вдоль края
        _GlowIntensity   ("Glow Intensity",    Float)        = 1.5   // Яркость (>1 = пересвет)
        _PulseSpeed      ("Pulse Speed",       Float)        = 2.0   // Скорость пульсации альфы
        _PulseStrength   ("Pulse Strength",    Range(0,1))   = 0.25  // Глубина пульсации
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
        
        // Стандартные настройки блендинга для UI (прозрачность)
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct Varyings
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float4    _MainTex_ST;

            fixed4 _Color;
            fixed4 _ColorA;
            fixed4 _ColorB;
            fixed4 _ColorC;

            float _AnimationSpeed;
            float _FlowScale;
            float _GlowIntensity;
            float _PulseSpeed;
            float _PulseStrength;

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.pos   = UnityObjectToClipPos(i.vertex);
                o.uv    = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color * _Color;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float  t  = _Time.y * _AnimationSpeed;

                // 1. ТЕКУЩАЯ ПОЗИЦИЯ ВДОЛЬ ГРАНИЦЫ
                // Параметр "flow" едет вдоль периметра прямоугольника по времени.
                float flow = uv.x + uv.y + t * 0.4;

                // 2. ПРОЦЕДУРНЫЙ ШУМ (без внешней текстуры)
                // Два слоя sin/cos дают органичное, не повторяющееся искажение.
                float noiseX = sin(uv.y * _FlowScale * 4.3 + t * 1.7)
                             + sin(uv.x * _FlowScale * 2.1 - t * 0.9) * 0.5;
                float noiseY = cos(uv.x * _FlowScale * 3.7 - t * 1.3)
                             + cos(uv.y * _FlowScale * 1.9 + t * 2.1) * 0.5;
                float2 distort = float2(noiseX, noiseY) * 0.04;

                float2 distortedUV = uv + distort;

                // 3. АНИМИРОВАННЫЙ ГРАДИЕНТ
                // blend1 и blend2 — две независимые волны со сдвигом по фазе.
                float blend1 = sin(flow * 3.14159 + t * 0.5) * 0.5 + 0.5;
                float blend2 = sin(flow * 3.14159 * 0.6 - t * 0.3 + 1.57) * 0.5 + 0.5;

                float4 ab    = lerp(_ColorA, _ColorB, blend1);
                float4 color = lerp(ab, _ColorC, blend2 * 0.6);
                color.rgb *= _GlowIntensity;

                // 4. ПУЛЬСАЦИЯ
                float pulse = 1.0 - _PulseStrength * 0.5
                            + _PulseStrength * 0.5 * sin(_Time.y * _PulseSpeed);
                color.rgb *= pulse;

                // 5. МАСКА ФОРМЫ (ИЗ FIGMA)
                // Считываем маску из Figma по искаженным UV
                float4 mask = tex2D(_MaskTex, distortedUV);
                
                // Используем красный канал (r) или альфу (a) маски. 
                // В исходнике использовался mask.r
                color.a = mask.r * i.color.a * pulse;

                return color;
            }
            ENDCG
        }
    }
}