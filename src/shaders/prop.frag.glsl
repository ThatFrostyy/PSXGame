#version 330 core
in vec3 vColor; in vec2 vUV; in vec3 vWorldPos; in vec3 vNormal;
out vec4 fragColor;
uniform sampler2D uDiffuse;
uniform vec3 uCamPos, uLightPos, uCamDir;
uniform float uFlashlightOn;
uniform vec2 uResolution;
void main(){
    vec4 texel=texture(uDiffuse,vUV);
    if(texel.a<0.3) discard;
    vec3 col=texel.rgb*vColor*vec3(0.26,0.30,0.36);
    float ambient=0.06+0.04*max(dot(vNormal,normalize(vec3(0.3,0.6,-0.7))),0.0);
    col*=(ambient+0.2);
    vec3 toFrag=normalize(vWorldPos-uLightPos);
    float beam=pow(max(dot(toFrag,uCamDir),0.0),12.0)*uFlashlightOn;
    float dist=length(vWorldPos-uLightPos);
    float atten=smoothstep(18.0,0.5,dist);
    float lit=beam*atten;
    col=col*(1.0+lit*7.0*vec3(1.0,0.90,0.70));
    vec3 fogColor=vec3(0.01,0.02,0.06);
    float fog=smoothstep(2.5,11.0,length(vWorldPos-uCamPos));
    col=mix(col,fogColor,fog);
    col=floor(col*24.0)/24.0;
    fragColor=vec4(col,1.0);
}
