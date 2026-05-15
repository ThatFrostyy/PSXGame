#version 330 core
in vec2 vUV;
out vec4 fragColor;
uniform sampler2D uBatteryTex;
uniform float uBatteryLevel;
void main(){
    vec2 uv=vec2(vUV.x,1.0-vUV.y);
    vec4 tex=texture(uBatteryTex,uv);
    if(tex.a<0.05) discard;
    float luma=dot(tex.rgb,vec3(0.299,0.587,0.114));
    if(luma>=0.5){ fragColor=vec4(floor(tex.rgb*28.0)/28.0,tex.a); return; }
    float l=0.263,r=0.773,b=0.133,t=0.922;
    if(uv.x<l||uv.x>r||uv.y<b||uv.y>t){ fragColor=vec4(0,0,0,1); return; }
    vec2 inner=vec2((uv.x-l)/(r-l),(uv.y-b)/(t-b));
    float cellIndex=floor(inner.y*3.0);
    float cellLocalY=fract(inner.y*3.0);
    float cellLocalX=inner.x;
    float cellW=153.0,cellH=104.0;
    vec2 cellPx=vec2(cellLocalX*cellW,cellLocalY*cellH);
    float padPx=8.0,radiusPx=10.0;
    vec2 innerSize=vec2(cellW-padPx*2.0,cellH-padPx*2.0);
    vec2 q=abs(cellPx-vec2(cellW,cellH)*0.5)-innerSize*0.5+vec2(radiusPx);
    float sdf=length(max(q,0.0))-radiusPx;
    if(sdf>0.0){ fragColor=vec4(0,0,0,1); return; }
    float cellFilled=(1.0-clamp(uBatteryLevel,0.0,1.0))*3.0;
    bool lit=cellIndex>=floor(cellFilled);
    if(cellIndex==floor(cellFilled)) lit=cellLocalY>=fract(cellFilled);
    vec3 col;
    if(!lit)                       col=vec3(0.0,0.0,0.0);
    else if(uBatteryLevel>0.66)    col=vec3(0.06,0.82,0.19);
    else if(uBatteryLevel>0.33)    col=vec3(0.83,0.68,0.07);
    else                           col=vec3(0.82,0.12,0.06);
    fragColor=vec4(floor(col*28.0)/28.0,1.0);
}
