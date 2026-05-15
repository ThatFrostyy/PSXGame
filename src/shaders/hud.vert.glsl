#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
out vec2 vUV;
uniform float uAspectRatio;
void main(){
    vec2 scale=vec2(0.07,0.07*uAspectRatio);
    vec2 offset=vec2(-0.82,-0.65);
    gl_Position=vec4(aPos*scale+offset,0.0,1.0);
    vUV=aUV;
}
