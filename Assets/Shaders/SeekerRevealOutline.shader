Shader "Hidden/PropHunt/SeekerRevealOutline"
{
    Properties
    {
        _Color ("Reveal Color", Color) = (1, 0.16, 0.03, 0.05)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.025
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SeekerRevealOutline"
            Cull Front
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            fixed4 _Color;
            float _OutlineWidth;

            v2f vert(appdata input)
            {
                v2f output;
                float3 expanded = input.vertex.xyz + normalize(input.normal) * _OutlineWidth;
                output.vertex = UnityObjectToClipPos(float4(expanded, 1));
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }

    Fallback Off
}
