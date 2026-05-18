Shader "UI/AnimatedFluidGradient"
{
    Properties
    {
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        LOD 100

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
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            float4 _Color1;
            float4 _Color2;
            float4 _Color3;
            float _Speed;
            float _Scale;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
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

            fixed4 frag(v2f i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float2 uv = screenUV * _Scale;
                float t = _Time.y * _Speed;
                
                // Add movement to uv
                uv += float2(sin(t * 0.5), cos(t * 0.3));

                // Combine noises
                float n1 = noise(uv + float2(t, 0.0)) * 0.5 + 0.5;
                float n2 = noise(uv * 1.5 - float2(0.0, t * 0.8)) * 0.5 + 0.5;
                float n3 = noise(uv * 2.0 + float2(cos(t), sin(t))) * 0.5 + 0.5;

                // Mix colors based on noise
                float3 col = lerp(_Color1.rgb, _Color2.rgb, n1);
                col = lerp(col, _Color3.rgb, n2);
                col += float3(n3, n3, n3) * 0.15; // Add subtle detail

                return float4(col, i.color.a);
            }
            ENDCG
        }
    }
}
