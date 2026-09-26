// ============================================================================
// ShapeWipe Mask Shader
// Two-phase alpha-mask screen transition with up to 200 shape instances.
// Compatible with Unity 2018.4+.
// WARNING: MAX_SHAPES = 200 will fail to compile on mobile GLES 2.0.
// ============================================================================
Shader "ShapeWipe/Mask"
{
    Properties
    {
        [Header(Mask)]
        _MainTex ("Shape Mask", 2D) = "white" {}
        _MaskColorStart ("Mask Color Start", Color) = (0, 0, 0, 1)
        _MaskColorEnd   ("Mask Color End",   Color) = (0, 0, 0, 1)

        [Header(Animation)]
        _Progress ("Progress", Range(0, 1)) = 0
        _Softness ("Edge Softness", Range(0, 0.5)) = 0.05
        _Mode     ("Mode (0=Black, 1=Erase)", Range(0, 1)) = 0

        [Header(Screen)]
        _ScreenAspect ("Screen Aspect", Float) = 1.777
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_SHAPES 200

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            fixed4    _MaskColorStart;
            fixed4    _MaskColorEnd;
            float     _Progress;
            float     _Softness;
            float     _Mode;
            float     _ScreenAspect;

            // Number of active shape slots. Only the first N slots are read.
            float _ActiveCount;

            // xy = center (UV), z = startCos, w = startScale.x
            float4 _ShapeData[MAX_SHAPES];
            // x = startScale.y, y = endScale.x, z = endScale.y, w = delay (0~1)
            float4 _ShapeTarget[MAX_SHAPES];
            // xy = wipe offset, z = expand duration (0~1), w = endCos
            float4 _ShapeMotion[MAX_SHAPES];
            // xy = startSin, endSin (unused zw for alignment)
            float2 _ShapeRotation[MAX_SHAPES];

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float SampleShape(float2 uv, float2 center, float cosR, float sinR,
                              float scaleX, float scaleY)
            {
                if (scaleX <= 0 || scaleY <= 0) return 0;

                float2 localUV = uv - center;
                localUV.x *= _ScreenAspect;

                // Rotation is already precomputed as (cos, sin).
                localUV = float2(localUV.x * cosR + localUV.y * sinR,
                                -localUV.x * sinR + localUV.y * cosR);

                localUV.x /= scaleX;
                localUV.y /= scaleY;
                localUV += 0.5;

                if (localUV.x < 0 || localUV.x > 1 || localUV.y < 0 || localUV.y > 1)
                    return 0;

                return tex2D(_MainTex, localUV).a;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float shapeMask = 0;
                int activeCount = (int)_ActiveCount;

                for (int idx = 0; idx < MAX_SHAPES; idx++)
                {
                    if (idx >= activeCount) break;

                    float4 data   = _ShapeData[idx];
                    float4 target = _ShapeTarget[idx];
                    float4 motion = _ShapeMotion[idx];
                    float2 rot2   = _ShapeRotation[idx];

                    float delay  = target.w;
                    // expand is guaranteed > 0 by the C# side.
                    float expand = motion.z;

                    float localProgress = 0;
                    if (_Progress > delay)
                        localProgress = saturate((_Progress - delay) / expand);

                    float sx = lerp(data.w,   target.y, localProgress);
                    float sy = lerp(target.x, target.z, localProgress);

                    // Lerp precomputed cos/sin instead of computing sin/cos per pixel.
                    float cosR = lerp(data.z,   motion.w, localProgress);
                    float sinR = lerp(rot2.x,   rot2.y,   localProgress);

                    float2 pos = data.xy + motion.xy * localProgress;

                    float s = SampleShape(i.uv, pos, cosR, sinR, sx, sy);
                    shapeMask = max(shapeMask, s);
                }

                float edge = smoothstep(0.0, _Softness, shapeMask);
                float alpha = (1.0 - _Mode) * edge + _Mode * (1.0 - edge);

                float3 col = _MaskColorStart.rgb * (1.0 - _Progress)
                           + _MaskColorEnd.rgb   * _Progress;
                float maskAlpha = _MaskColorStart.a * (1.0 - _Progress)
                                + _MaskColorEnd.a   * _Progress;

                return fixed4(col, alpha * maskAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}