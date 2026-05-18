Shader "UI/NegativeBWText"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Color1 ("Color 1", Color) = (1, 0, 0, 1)
        _Color2 ("Color 2", Color) = (0, 0, 1, 1)
        _Color3 ("Color 3", Color) = (0, 1, 0, 1)
        _Speed ("Animation Speed", Float) = 1.0
        _Scale ("Noise Scale", Float) = 5.0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
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

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            
            float4 _Color1;
            float4 _Color2;
            float4 _Color3;
            float _Speed;
            float _Scale;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            float2 hash(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float noise(float2 p)
            {
                const float K1 = 0.366025404; // (sqrt(3)-1)/2;
                const float K2 = 0.211324865; // (3-sqrt(3))/6;

                float2 i = floor(p + (p.x + p.y) * K1);
                float2 a = p - i + (i.x + i.y) * K2;
                float2 o = (a.x > a.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
                float2 b = a - o + K2;
                float2 c = a - 1.0 + 2.0 * K2;

                float3 h = max(0.5 - float3(dot(a, a), dot(b, b), dot(c, c)), 0.0);
                float3 n = h * h * h * h * float3(dot(a, hash(i + 0.0)), dot(b, hash(i + o)), dot(c, hash(i + 1.0)));

                return dot(n, float3(70.0, 70.0, 70.0));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Вычисляем экранные координаты
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 uv = screenUV * _Scale;
                float t = _Time.y * _Speed;
                
                // Смещаем UV для анимации (так же как в фоне)
                uv += float2(sin(t * 0.5), cos(t * 0.3));

                // Рассчитываем шум
                float n1 = noise(uv + float2(t, 0.0)) * 0.5 + 0.5;
                float n2 = noise(uv * 1.5 - float2(0.0, t * 0.8)) * 0.5 + 0.5;
                float n3 = noise(uv * 2.0 + float2(cos(t), sin(t))) * 0.5 + 0.5;

                // Получаем фоновый цвет в этой же пиксельной точке
                float3 bgColor = lerp(_Color1.rgb, _Color2.rgb, n1);
                bgColor = lerp(bgColor, _Color3.rgb, n2);
                bgColor += float3(n3, n3, n3) * 0.15;

                // Делаем негатив
                float3 negativeColor = 1.0 - bgColor;

                // Переводим в черно-белый
                float gray = dot(negativeColor.rgb, float3(0.299, 0.587, 0.114));
                float3 finalColor = float3(gray, gray, gray);

                // Альфа берется из текстуры шрифта
                fixed4 texColor = tex2D(_MainTex, IN.texcoord);
                float alpha = texColor.a * IN.color.a;

                return float4(finalColor, alpha);
            }
            ENDCG
        }
    }
}
