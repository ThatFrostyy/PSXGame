#version 330 core
in vec2 vUV;
out vec4 fragColor;
uniform sampler2D uScene;
uniform vec2 uScreenSize;
uniform vec2 uPsxSize;
uniform vec2 uCameraXZ;
uniform float uMapHalfExtent;
void main(){
    vec3 col=texture(uScene,vUV).rgb;
    float psxRow=floor(vUV.y*uPsxSize.y);
    float scanline=mod(psxRow,2.0)<1.0 ? 0.75 : 1.0;
    col*=scanline;
    float psxCol=floor(vUV.x*uPsxSize.x);
    float grille=mod(psxCol,3.0)<2.0 ? 1.0 : 0.82;
    col*=grille;
    vec2 px=floor(vUV*uScreenSize);
    float bayer=fract(sin(dot(px,vec2(12.9898,78.233)))*43758.5453);
    col+=(bayer-0.5)/55.0;
    vec2 uv2=vUV*2.0-1.0;
    float vig=1.0-dot(uv2,uv2)*0.45;
    vig=clamp(vig,0.0,1.0);
    col*=vig;
    col=vec3(col.r*0.96, col.g*1.02, col.b*0.94);

    float edgeDist=max(abs(uCameraXZ.x), abs(uCameraXZ.y));
    float edgeBand=18.0;
    float t=clamp((edgeDist-(uMapHalfExtent-edgeBand))/edgeBand,0.0,1.0);
    float noise=fract(sin(dot(vUV*uScreenSize+uCameraXZ*4.0,vec2(127.1,311.7)))*43758.5453);
    vec3 staticCol=mix(vec3(0.04,0.08,0.10), vec3(0.65,0.70,0.74), noise);
    vec2 blurShift = vec2(1.0/uPsxSize.x, 1.0/uPsxSize.y) * (0.35 + t * 0.8);
    vec3 blurCol=texture(uScene,vUV+blurShift).rgb*0.25
               +texture(uScene,vUV-blurShift).rgb*0.25
               +texture(uScene,vUV+vec2(blurShift.x,-blurShift.y)).rgb*0.25
               +texture(uScene,vUV+vec2(-blurShift.x,blurShift.y)).rgb*0.25;
    col=mix(col, blurCol, t*0.45);
    col=mix(col, staticCol, t*0.28);

    fragColor=vec4(clamp(col,0.0,1.0),1.0);
}
