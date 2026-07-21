Shader "Prop Hunt/Zone Energy Dome"
{
    Properties
    {
        _EnergyColor ("Energy Color", Color) = (0.04, 0.76, 1.0, 0.85)
        _BodyAlpha ("Body Alpha", Range(0, 0.2)) = 0.075
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 1.1
        _StreakStrength ("Streak Strength", Range(0, 2)) = 1.0
        _PulseSpeed ("Pulse Speed", Range(0.1, 3)) = 0.72
        _ScrollSpeed ("Scroll Speed", Range(0.1, 3)) = 0.48
        _ShrinkIntensity ("Shrink Intensity", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+40"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            fixed4 _EnergyColor;
            float _BodyAlpha;
            float _FresnelStrength;
            float _StreakStrength;
            float _PulseSpeed;
            float _ScrollSpeed;
            float _ShrinkIntensity;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float time = _Time.y;
                float animatedScroll = _ScrollSpeed * (1.0 + _ShrinkIntensity * 0.45);
                float2 uv = input.uv;
                float warpedU = uv.x + sin(uv.y * 31.0 - time * animatedScroll * 1.7) * 0.014;
                float warpedV = uv.y + sin(uv.x * 47.0 + time * animatedScroll) * 0.018;

                float verticalArc = pow(saturate(1.0 - abs(sin((warpedU * 31.0 + warpedV * 3.0 + time * animatedScroll) * UNITY_PI))), 15.0);
                float crossArc = pow(saturate(1.0 - abs(sin((warpedV * 18.0 - warpedU * 7.0 - time * animatedScroll * 1.4) * UNITY_PI))), 18.0);
                float electricNoise = (verticalArc * 0.72 + crossArc * 0.48) * _StreakStrength;

                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                float fresnel = pow(1.0 - saturate(abs(dot(normalize(input.worldNormal), viewDirection))), 2.2) * _FresnelStrength;
                float groundRim = pow(saturate(1.0 - uv.y), 11.0);
                float topCurve = smoothstep(0.72, 1.0, uv.y) * (0.35 + 0.65 * verticalArc);
                float pulse = 1.0 + _ShrinkIntensity * (0.08 + 0.07 * sin(time * _PulseSpeed * 6.28318 + uv.y * 8.0));
                float topFade = 1.0 - smoothstep(0.96, 1.0, uv.y) * 0.45;

                float intensity = (electricNoise * 1.15 + fresnel * 0.7 + groundRim * 1.35 + topCurve * 0.35) * pulse;
                float alpha = (_BodyAlpha + electricNoise * 0.19 + fresnel * 0.23 + groundRim * 0.34 + topCurve * 0.07) * _EnergyColor.a * topFade * pulse;
                float3 color = _EnergyColor.rgb * (1.0 + intensity * (1.15 + _ShrinkIntensity * 0.25));
                return fixed4(color, saturate(alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
