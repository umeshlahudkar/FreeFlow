Shader "Custom/UI/PermissionBorder"
{
    // Rounded-rect border for a permission cell (BlockType.ForbiddenForPair /
    // AllowedForPairs). The ring is split into up to 4 angular slices, one per named pair
    // colour; each slice is solid when that colour may pass and dashed when it may not, so
    // a two-colour cell reads as two mitred halves instead of a ring plus a hand-cut arc.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SegmentCount ("Segment Count", Range(1,4)) = 1
        _Color0 ("Segment Color 0", Color) = (1,1,1,1)
        _Color1 ("Segment Color 1", Color) = (1,1,1,1)
        _Color2 ("Segment Color 2", Color) = (1,1,1,1)
        _Color3 ("Segment Color 3", Color) = (1,1,1,1)
        // x=seg0..w=seg3, 1 = solid (allowed), 0 = dashed (refused)
        _Allowed ("Allowed Flags", Vector) = (1,1,1,1)

        _CornerRadius ("Corner Radius (UV)", Range(0, 0.5)) = 0
        _BorderThickness ("Border Thickness (UV)", Range(0.01, 0.3)) = 0.09
        _DashCount ("Dashes Per Segment", Range(1, 12)) = 5
        _DashDuty ("Dash Fill Fraction", Range(0.05, 0.95)) = 0.55
        // In turns (0-1). Default lands 4-segment seams on the diagonals, so each side of
        // the resulting frame centres on an edge (up/right/down/left) rather than a corner.
        _AngleOffset ("Angle Offset (turns)", Range(0,1)) = 0.125

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
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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
                float4 vertex       : SV_POSITION;
                fixed4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                float4 worldPosition: TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;

            float _SegmentCount;
            fixed4 _Color0;
            fixed4 _Color1;
            fixed4 _Color2;
            fixed4 _Color3;
            float4 _Allowed;

            float _CornerRadius;
            float _BorderThickness;
            float _DashCount;
            float _DashDuty;
            float _AngleOffset;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.uv = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Signed distance from a rounded rectangle boundary. p is centered at the rect's
            // middle, b is the rect's half-size, r is the corner radius. Negative inside.
            float RoundedBoxSD(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.uv - 0.5;
                float d = RoundedBoxSD(p, float2(0.5, 0.5), _CornerRadius);
                float aa = max(fwidth(d), 1e-5);

                // 1 just inside the outer edge (d < 0), 0 just outside.
                float outerMask = 1.0 - smoothstep(-aa, aa, d);
                // 1 outside the inner hole (d > -thickness), 0 once we're past it.
                float innerMask = smoothstep(-aa, aa, d + _BorderThickness);
                float ringMask = outerMask * innerMask;

                if (ringMask <= 0.0) { discard; }

                // Angle around the cell centre, 0..1 turns, rotated so a 4-way split mitres
                // on the diagonals instead of the axes.
                float angle01 = frac(atan2(p.y, p.x) / (2.0 * UNITY_PI) + 0.5 + _AngleOffset);

                int segCount = clamp((int)(_SegmentCount + 0.5), 1, 4);
                float segF = angle01 * segCount;
                int seg = clamp((int)segF, 0, 3);
                float segLocal = frac(segF);

                fixed4 segColor = _Color0;
                float allowed = _Allowed.x;
                if (seg == 1) { segColor = _Color1; allowed = _Allowed.y; }
                else if (seg == 2) { segColor = _Color2; allowed = _Allowed.z; }
                else if (seg == 3) { segColor = _Color3; allowed = _Allowed.w; }

                // Dash pattern along the segment's own arc; collapses to fully solid when
                // that colour is allowed through this cell.
                float dashPhase = frac(segLocal * _DashCount);
                float dashMask = lerp(step(dashPhase, _DashDuty), 1.0, allowed);

                fixed4 color = IN.color;
                color.rgb *= segColor.rgb;
                color.a *= segColor.a * ringMask * dashMask;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
