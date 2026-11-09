// Grainy old-film look: noise grain (stronger in the dark), a little desaturation, a vignette and a slight flicker.
// Used by FilmGrain.cs on the player camera. You don't need to touch this file: tweak the numbers on FilmGrain instead.
Shader "Hidden/Stalker/FilmGrain"
{
    Properties { _MainTex ("", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;     // how strong the grain is
            float _GrainSize;     // grain size in screen pixels
            float _Seed;          // changes every grain "frame"
            float _Desaturate;    // 0 = full colour, 1 = black and white
            float _Vignette;      // how dark the screen edges get
            float _Brightness;    // flicker multiplier (around 1)

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));

                // Old film is a bit washed out
                col.rgb = lerp(col.rgb, lum.xxx, _Desaturate);

                // Grain: one random value per grain cell, re-rolled every grain frame.
                // Stronger in the darks and midtones (like real film), so black areas "crawl".
                float2 cell = floor(i.uv * _ScreenParams.xy / max(_GrainSize, 1.0));
                float n = Hash(cell + _Seed) - 0.5;
                col.rgb += n * _Intensity * lerp(1.0, 0.35, saturate(lum));

                // Flicker + vignette
                col.rgb *= _Brightness;
                float2 d = i.uv - 0.5;
                col.rgb *= saturate(1.0 - dot(d, d) * _Vignette * 2.0);

                col.rgb = saturate(col.rgb);
                return col;
            }
            ENDCG
        }
    }
}
