#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
layout(location=3) in vec3 aColor;
out vec3 vColor; out vec2 vUV; out vec3 vWorldPos;
uniform mat4 uModel, uView, uProjection;
void main(){
    vColor=aColor; vUV=aUV*40.0;
    vec4 wp=uModel*vec4(aPos,1.0); vWorldPos=wp.xyz;
    vec4 clip=uProjection*uView*wp;
    clip.xy=floor(clip.xy*240.0)/240.0;
    gl_Position=clip;
}
