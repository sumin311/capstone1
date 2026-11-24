// Chef24 toon shader (Built-in render pipeline).
// Two hard light bands (lit / warm shadow tint, never black), flat ambient, a soft rim,
// cast shadows banded with the light, and an inverted-hull outline with constant screen width.
Shader "Chef24/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _ShadowTint ("Shadow Tint", Color) = (0.72, 0.62, 0.66, 1)
        _Threshold ("Band Threshold", Range(0,1)) = 0.45
        _Smoothness ("Band Edge", Range(0.001, 0.2)) = 0.03
        _RimColor ("Rim Color", Color) = (1, 0.96, 0.87, 1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.18
        _OutlineColor ("Outline Color", Color) = (0.29, 0.165, 0.09, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 6)) = 2
        _AmbientStrength ("Ambient Strength", Range(0, 1.5)) = 0.55
        _PointStrength ("Lamp Strength", Range(0, 1.5)) = 0.6
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Lighting.cginc"
    #include "AutoLight.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    fixed4 _Color, _ShadowTint, _RimColor, _OutlineColor;
    float _Threshold, _Smoothness, _RimStrength, _OutlineWidth, _AmbientStrength, _PointStrength;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 worldNormal : TEXCOORD1;
        float3 worldPos : TEXCOORD2;
        SHADOW_COORDS(3)
        UNITY_FOG_COORDS(4)
    };

    v2f vertLit (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.worldNormal = UnityObjectToWorldNormal(v.normal);
        o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        TRANSFER_SHADOW(o);
        UNITY_TRANSFER_FOG(o, o.pos);
        return o;
    }

    // 0 = shadow side, 1 = lit side, with a thin antialiased edge.
    float Band (float ndl)
    {
        return smoothstep(_Threshold - _Smoothness, _Threshold + _Smoothness, ndl);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // Main light (directional) + flat ambient + rim.
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vertLit
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float band = Band(saturate(dot(n, l)) * atten);
                float3 lit = _LightColor0.rgb * lerp(_ShadowTint.rgb, 1.0, band);

                // Flat ambient (sampled straight up) keeps the frame graphic instead of gradient-shaded.
                float3 ambient = ShadeSH9(float4(0, 1, 0, 1)) * _AmbientStrength;

                float rim = pow(1.0 - saturate(dot(n, v)), 4.0) * _RimStrength;
                float3 col = albedo.rgb * (ambient + lit) + _RimColor.rgb * rim * (0.4 + 0.6 * band);

                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        // Point / spot lights (the truck's interior lamps), banded the same way and added on top.
        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma vertex vertLit
            #pragma fragment fragAdd
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog

            fixed4 fragAdd (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(UnityWorldSpaceLightDir(i.worldPos));
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float band = Band(saturate(dot(n, l)));
                // Light falloff stepped into two levels so the lamp pools read as flat shapes too.
                float fall = atten > 0.35 ? 1.0 : (atten > 0.08 ? 0.55 : atten * 6.0);
                float3 col = albedo.rgb * _LightColor0.rgb * lerp(_ShadowTint.rgb * 0.5, 1.0, band) * fall * _PointStrength;

                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0,0,0,0));
                return c;
            }
            ENDCG
        }

        // Inverted hull outline, constant width in screen pixels.
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode"="Always" }
            Cull Front
            CGPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_fog

            struct v2fo
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2fo vertOutline (appdata v)
            {
                v2fo o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                float2 dir = TransformViewToProjection(viewNormal.xy);
                float len = length(dir);
                dir = len > 1e-5 ? dir / len : float2(0, 0);
                // Pixels -> clip space: 2 / screen size, times w to undo the perspective divide.
                o.pos.xy += dir * (_OutlineWidth * 2.0 / _ScreenParams.xy) * o.pos.w;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 fragOutline (v2fo i) : SV_Target
            {
                if (_OutlineWidth <= 0.01) discard;
                fixed3 albedo = (tex2D(_MainTex, i.uv) * _Color).rgb;
                // Mostly the palette's darkest brown, tinted a little by the surface colour.
                fixed4 c = fixed4(lerp(_OutlineColor.rgb, albedo * 0.3, 0.25), 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
