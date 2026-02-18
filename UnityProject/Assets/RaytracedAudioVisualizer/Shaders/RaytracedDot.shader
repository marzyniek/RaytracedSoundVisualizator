Shader "Custom/RaytracedDot"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Initial Color", Color) = (1,1,0,1)
        _FadeColor ("Faded Color", Color) = (1,1,0,0)
        _Lifetime ("Lifetime", Float) = 1.5
        _Offset ("Wall Offset", Float) = 0.02 
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            struct DotData
            {
                float3 position;
                float padding1;
                float3 normal;
                float padding2;
                float4 color;
                float startTime;
                float energy;
                float intensity;
                float padding4;
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<DotData> _DotBuffer;
            #endif

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _FadeColor;
            float _Lifetime;
            // 2. DECLARE VARIABLE
            float _Offset; 

            void setup() {}

            float4x4 LookAtMatrix(float3 position, float3 forward)
            {
                float3 up = float3(0, 1, 0);
                if(abs(dot(forward, up)) > 0.99) up = float3(0, 0, 1);
                
                float3 x = normalize(cross(up, forward));
                float3 y = cross(forward, x);
                
                return float4x4(
                    x.x, y.x, forward.x, position.x,
                    x.y, y.y, forward.y, position.y,
                    x.z, y.z, forward.z, position.z,
                    0,   0,   0,         1
                );
            }

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float4 worldPos = 0;
                float4 finalColor = _Color;

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    DotData data = _DotBuffer[instanceID];

                    float age = _Time.y - data.startTime;
                    float t = age / _Lifetime; 
                    float liveliness = step(0.0, age) * step(age, _Lifetime);

                    
                    float fadeState = max(1.0 - (t * 3.0), (t * 3.0) - 2.0);
                    float animT = clamp(fadeState, 0.0, 1.0);

                    finalColor = lerp(data.color, float4(data.color.rgb, 0.0), animT);
                    finalColor.a *= data.intensity;
                    float scale = data.energy * 0.2 * liveliness; 

                    float3 offsetPos = data.position + (data.normal * _Offset);
                    float4x4 mat = LookAtMatrix(offsetPos, -data.normal);
                    
                    float4 localPos = v.vertex * scale;
                    localPos.w = 1; 
                    worldPos = mul(mat, localPos);
                #else
                    worldPos = v.vertex;
                #endif

                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = finalColor;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if(i.color.a <= 0.01) discard;
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}