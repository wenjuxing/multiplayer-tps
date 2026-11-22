// 以加法混合渲染带亮度和软粒子过渡的特效
 Shader "Particles/Additive Intensify" {
 // 配置粒子贴图、颜色、亮度和软边过渡
 Properties {
     _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
     _MainTex ("Particle Texture", 2D) = "white" {}
     _InvFade ("Soft Particles Factor", Range(0.01,3.0)) = 1.0
     _Glow ("Intensity", Range(0, 5)) = 1
 }
 
 Category {
     Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
     // 将粒子颜色按透明度叠加到画面
     Blend SrcAlpha One
     ColorMask RGB
     Cull Off Lighting Off ZWrite Off
     
     SubShader {
         Pass {
         
             CGPROGRAM
             #pragma vertex vert
             #pragma fragment frag
             #pragma target 2.0
             #pragma multi_compile_particles
             #pragma multi_compile_fog
 
             #include "UnityCG.cginc"
 
             sampler2D _MainTex;
             fixed4 _TintColor;
             half _Glow;
             
             struct appdata_t {
                 float4 vertex : POSITION;
                 fixed4 color : COLOR;
                 float2 texcoord : TEXCOORD0;
                 UNITY_VERTEX_INPUT_INSTANCE_ID
             };
 
             struct v2f {
                 float4 vertex : SV_POSITION;
                 fixed4 color : COLOR;
                 float2 texcoord : TEXCOORD0;
                 UNITY_FOG_COORDS(1)
                 #ifdef SOFTPARTICLES_ON
                 float4 projPos : TEXCOORD2;
                 #endif
                 UNITY_VERTEX_OUTPUT_STEREO
             };
             
             float4 _MainTex_ST;
 
             // 计算粒子顶点位置并传递颜色和贴图坐标
             v2f vert (appdata_t v)
             {
                 v2f o;
                 UNITY_SETUP_INSTANCE_ID(v);
                 UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                 // 转换顶点位置，软粒子额外保存屏幕位置和深度
                 o.vertex = UnityObjectToClipPos(v.vertex);
                 #ifdef SOFTPARTICLES_ON
                 o.projPos = ComputeScreenPos (o.vertex);
                 COMPUTE_EYEDEPTH(o.projPos.z);
                 #endif
                 // 合成粒子颜色和亮度并传递贴图与雾效坐标
                 o.color = v.color * _TintColor;
                 o.color.rgb *= _Glow;
                 o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
                 UNITY_TRANSFER_FOG(o,o.vertex);
                 return o;
             }
 
             // 软粒子使用的场景深度贴图和过渡强度
             sampler2D_float _CameraDepthTexture;
             float _InvFade;
             
             // 根据场景深度、贴图和雾效计算像素颜色
             fixed4 frag (v2f i) : SV_Target
             {
                 #ifdef SOFTPARTICLES_ON
                 // 粒子靠近场景表面时淡出
                 float sceneZ = LinearEyeDepth (SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                 float partZ = i.projPos.z;
                 float fade = saturate (_InvFade * (sceneZ-partZ));
                 i.color.a *= fade;
                 #endif
                 
                 // 采样粒子贴图并用黑色雾保留加法混合效果
                 fixed4 col = 2.0f * i.color * tex2D(_MainTex, i.texcoord);
                 UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0,0,0,0));
                 return col;
             }
             ENDCG 
         }
     }    
 }
 }
 