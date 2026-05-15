#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
layout(location=3) in vec3 aColor;
layout(location=4) in vec4 iModelCol0;
layout(location=5) in vec4 iModelCol1;
layout(location=6) in vec4 iModelCol2;
layout(location=7) in vec4 iModelCol3;
out vec3 vColor; out vec2 vUV; out vec3 vWorldPos; out vec3 vNormal;
uniform mat4 uModel, uView, uProjection;
uniform int uUseInstancing;
void main(){
    vColor=aColor; vUV=aUV;
    mat4 instanceModel = mat4(iModelCol0, iModelCol1, iModelCol2, iModelCol3);
    mat4 model = (uUseInstancing == 1) ? instanceModel : uModel;
    vec4 wp=model*vec4(aPos,1.0); vWorldPos=wp.xyz;
    vNormal=normalize(mat3(model)*aNormal);
    vec4 clip=uProjection*uView*wp;
    clip.xy=floor(clip.xy*240.0)/240.0;
    gl_Position=clip;
}
