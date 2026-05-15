#version 330 core
in vec2 vUV;
out vec4 fragColor;
uniform sampler2D uScene;
uniform vec2 uScreenSize;
uniform vec2 uPsxSize;
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
    fragColor=vec4(clamp(col,0.0,1.0),1.0);
}
