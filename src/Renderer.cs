using System;
using System.IO;
using Silk.NET.OpenGL;
using Silk.NET.Maths;
using StbImageSharp;

namespace PSXGame;

public class Renderer : IDisposable
{
    private readonly GL _gl;
    private Vector2D<int> _screenSize;

    // PSX internal resolution — rendered here, then upscaled
    private const int PsxW = 320;
    private const int PsxH = 240;

    // Offscreen FBO for PSX-res 3D scene
    private uint _fbo;
    private uint _fboColorTex;
    private uint _fboDepthRbo;

    private readonly ShaderProgram _planeShader;
    private readonly ShaderProgram _propShader;
    private readonly ShaderProgram _batteryShader;
    private readonly ShaderProgram _upscaleShader;

    private readonly uint _batteryTexture;
    private readonly uint _whiteTex;

    // Full-screen quad for upscale pass
    private readonly uint _quadVao;
    private readonly uint _quadVbo;

    // HUD quad (drawn at full res on top of upscaled image)
    private readonly uint _hudVao;
    private readonly uint _hudVbo;

    public Renderer(GL gl, Vector2D<int> screenSize)
    {
        _gl = gl;
        _screenSize = screenSize;

        _planeShader   = new ShaderProgram(gl, PlaneVert,   PlaneFrag);
        _propShader    = new ShaderProgram(gl, PropVert,    PropFrag);
        _batteryShader = new ShaderProgram(gl, HudVert,     HudFrag);
        _upscaleShader = new ShaderProgram(gl, UpscaleVert, UpscaleFrag);

        _batteryTexture = LoadBatteryTexture();
        _whiteTex       = MakeWhiteTexture();

        CreateFbo();
        CreateQuad(out _quadVao, out _quadVbo);
        CreateHudQuad(out _hudVao, out _hudVbo);
    }

    // -------------------------------------------------------------------------
    // FBO
    // -------------------------------------------------------------------------
    private void CreateFbo()
    {
        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        // Color texture — PSX res, nearest filter so it stays blocky when upscaled
        _fboColorTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fboColorTex);
        unsafe { _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgb8, PsxW, PsxH, 0, PixelFormat.Rgb, PixelType.UnsignedByte, null); }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _fboColorTex, 0);

        // Depth renderbuffer
        _fboDepthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _fboDepthRbo);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, PsxW, PsxH);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _fboDepthRbo);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new Exception("PSX FBO is not complete!");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void DestroyFbo()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_fboColorTex);
        _gl.DeleteRenderbuffer(_fboDepthRbo);
    }

    // -------------------------------------------------------------------------
    // Quad helpers
    // -------------------------------------------------------------------------
    private void CreateQuad(out uint vao, out uint vbo)
    {
        // Full-screen quad: pos(2) + uv(2)
        float[] verts = [
            -1f, -1f,  0f, 0f,
             1f, -1f,  1f, 0f,
             1f,  1f,  1f, 1f,
            -1f, -1f,  0f, 0f,
             1f,  1f,  1f, 1f,
            -1f,  1f,  0f, 1f,
        ];
        vao = _gl.GenVertexArray();
        vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* p = verts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
    }

    private void CreateHudQuad(out uint vao, out uint vbo)
    {
        float[] hudVerts = [
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        ];
        vao = _gl.GenVertexArray();
        vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* p = hudVerts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(hudVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
    }

    // -------------------------------------------------------------------------
    // Render
    // -------------------------------------------------------------------------
    public void Render(Scene scene, Camera cam)
    {
        // ---- PASS 1: render scene into PSX-res FBO ---------------------------
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, PsxW, PsxH);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);

        float aspect = (float)PsxW / PsxH;
        var view = cam.GetViewMatrix();
        var proj = cam.GetProjectionMatrix(aspect);

        // Skybox
        scene.Skybox.Draw(view, proj);

        // Ground plane
        _gl.Disable(EnableCap.CullFace);
        _planeShader.Use();
        _planeShader.SetMatrix4("uView",       view);
        _planeShader.SetMatrix4("uProjection", proj);
        _planeShader.SetMatrix4("uModel",      Matrix4X4<float>.Identity);
        _planeShader.SetVec3("uCamPos",  cam.Position);
        _planeShader.SetVec3("uCamDir",  cam.Front);
        Vector3D<float> lightPos = cam.Position
            + (cam.Front * 0.45f) - (cam.Up * 0.35f) + (cam.Right * 0.16f);
        _planeShader.SetVec3("uLightPos", lightPos);
        float flashlight = cam.FlashlightOn ? cam.FlashlightIntensity : 0f;
        _planeShader.SetFloat("uFlashlightOn", flashlight);
        _planeShader.SetVector2("uResolution", new Vector2D<float>(PsxW, PsxH));
        scene.PlaneMesh.Draw();

        // Props
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _propShader.Use();
        _propShader.SetMatrix4("uView",       view);
        _propShader.SetMatrix4("uProjection", proj);
        _propShader.SetVec3("uCamPos",    cam.Position);
        _propShader.SetVec3("uCamDir",    cam.Front);
        _propShader.SetVec3("uLightPos",  lightPos);
        _propShader.SetFloat("uFlashlightOn", flashlight);
        _propShader.SetVector2("uResolution", new Vector2D<float>(PsxW, PsxH));
        _propShader.SetInt("uDiffuse", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        foreach (var prop in scene.Props)
        {
            _propShader.SetMatrix4("uModel", prop.Transform);
            foreach (var (mesh, tex) in prop.Model.Parts)
            {
                _gl.BindTexture(TextureTarget.Texture2D, tex != 0 ? tex : _whiteTex);
                mesh.Draw();
            }
        }
        _gl.Enable(EnableCap.CullFace);

        // ---- PASS 2: upscale FBO → screen with PSX post-processing -----------
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_screenSize.X, (uint)_screenSize.Y);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        _upscaleShader.Use();
        _upscaleShader.SetInt("uScene", 0);
        _upscaleShader.SetVector2("uScreenSize", new Vector2D<float>(_screenSize.X, _screenSize.Y));
        _upscaleShader.SetVector2("uPsxSize",    new Vector2D<float>(PsxW, PsxH));
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _fboColorTex);
        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        // ---- PASS 3: HUD drawn at full resolution on top ---------------------
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        float hudAspect = (float)_screenSize.X / _screenSize.Y;
        _batteryShader.Use();
        _batteryShader.SetFloat("uBatteryLevel", cam.BatteryLevel);
        _batteryShader.SetInt("uBatteryTex", 0);
        _batteryShader.SetFloat("uAspectRatio", hudAspect);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _batteryTexture);
        _gl.BindVertexArray(_hudVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Resize(Vector2D<int> newSize)
    {
        _screenSize = newSize;
        // FBO stays fixed at PSX res — no need to recreate
    }

    public void Dispose()
    {
        DestroyFbo();
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteBuffer(_hudVbo);
        _gl.DeleteVertexArray(_hudVao);
        _gl.DeleteTexture(_batteryTexture);
        _gl.DeleteTexture(_whiteTex);
        _upscaleShader.Dispose();
        _batteryShader.Dispose();
        _propShader.Dispose();
        _planeShader.Dispose();
    }

    // -------------------------------------------------------------------------
    // Texture helpers
    // -------------------------------------------------------------------------
    private uint LoadBatteryTexture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "src", "textures", "battery.png");
        if (!File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "src", "textures", "battery.png");
        using var fs = File.OpenRead(path);
        var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        return UploadTexture((uint)img.Width, (uint)img.Height, img.Data, repeat: false);
    }

    private uint MakeWhiteTexture()
    {
        byte[] white = [255, 255, 255, 255];
        return UploadTexture(1, 1, white, repeat: false);
    }

    private uint UploadTexture(uint w, uint h, byte[] data, bool repeat)
    {
        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = data)
                _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8, w, h, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        int wrap = repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        return tex;
    }

    // =========================================================================
    // Shaders
    // =========================================================================

    // --- Shared verts (plane + prop) -----------------------------------------
    private const string PlaneVert =
"#version 330 core\n" +
"layout(location=0) in vec3 aPos;\n" +
"layout(location=1) in vec3 aNormal;\n" +
"layout(location=2) in vec2 aUV;\n" +
"layout(location=3) in vec3 aColor;\n" +
"out vec3 vColor; out vec2 vUV; out vec3 vWorldPos;\n" +
"uniform mat4 uModel, uView, uProjection;\n" +
"void main(){\n" +
"    vColor=aColor; vUV=aUV*40.0;\n" +
"    vec4 wp=uModel*vec4(aPos,1.0); vWorldPos=wp.xyz;\n" +
"    vec4 clip=uProjection*uView*wp;\n" +
"    clip.xy=floor(clip.xy*240.0)/240.0;\n" +  // vertex snap
"    gl_Position=clip;\n" +
"}\n";

    private const string PlaneFrag =
"#version 330 core\n" +
"in vec3 vColor; in vec2 vUV; in vec3 vWorldPos;\n" +
"out vec4 fragColor;\n" +
"uniform vec3 uCamPos, uLightPos, uCamDir;\n" +
"uniform float uFlashlightOn;\n" +
"uniform vec2 uResolution;\n" +
"void main(){\n" +
"    vec2 g=abs(fract(vUV-0.5)-0.5)/fwidth(vUV);\n" +
"    float line=1.0-min(min(g.x,g.y),1.0);\n" +
"    vec3 col=vColor+line*0.05;\n" +
"    col*=vec3(0.26,0.30,0.36);\n" +
// flashlight: bright cone that multiplies existing colour
"    vec3 toFrag=normalize(vWorldPos-uLightPos);\n" +
"    float beam=pow(max(dot(toFrag,uCamDir),0.0),12.0)*uFlashlightOn;\n" +
"    float dist=length(vWorldPos-uLightPos);\n" +
"    float atten=smoothstep(18.0,0.5,dist);\n" +
"    float lit=beam*atten;\n" +
"    col=col*(1.0+lit*7.0*vec3(1.0,0.90,0.70));\n" +
// fog swallows everything
"    vec3 fogColor=vec3(0.01,0.02,0.06);\n" +
"    float fog=smoothstep(3.0,16.0,length(vWorldPos-uCamPos));\n" +
"    col=mix(col,fogColor,fog);\n" +
// PSX colour crush (fewer steps = more banding)
"    col=floor(col*24.0)/24.0;\n" +
"    fragColor=vec4(col,1.0);\n" +
"}\n";

    private const string PropVert =
"#version 330 core\n" +
"layout(location=0) in vec3 aPos;\n" +
"layout(location=1) in vec3 aNormal;\n" +
"layout(location=2) in vec2 aUV;\n" +
"layout(location=3) in vec3 aColor;\n" +
"out vec3 vColor; out vec2 vUV; out vec3 vWorldPos; out vec3 vNormal;\n" +
"uniform mat4 uModel, uView, uProjection;\n" +
"void main(){\n" +
"    vColor=aColor; vUV=aUV;\n" +
"    vec4 wp=uModel*vec4(aPos,1.0); vWorldPos=wp.xyz;\n" +
"    vNormal=normalize(mat3(uModel)*aNormal);\n" +
"    vec4 clip=uProjection*uView*wp;\n" +
"    clip.xy=floor(clip.xy*240.0)/240.0;\n" +
"    gl_Position=clip;\n" +
"}\n";

    private const string PropFrag =
"#version 330 core\n" +
"in vec3 vColor; in vec2 vUV; in vec3 vWorldPos; in vec3 vNormal;\n" +
"out vec4 fragColor;\n" +
"uniform sampler2D uDiffuse;\n" +
"uniform vec3 uCamPos, uLightPos, uCamDir;\n" +
"uniform float uFlashlightOn;\n" +
"uniform vec2 uResolution;\n" +
"void main(){\n" +
"    vec4 texel=texture(uDiffuse,vUV);\n" +
"    if(texel.a<0.3) discard;\n" +
"    vec3 col=texel.rgb*vColor*vec3(0.26,0.30,0.36);\n" +
"    float ambient=0.06+0.04*max(dot(vNormal,normalize(vec3(0.3,0.6,-0.7))),0.0);\n" +
"    col*=(ambient+0.2);\n" +
// same flashlight as ground
"    vec3 toFrag=normalize(vWorldPos-uLightPos);\n" +
"    float beam=pow(max(dot(toFrag,uCamDir),0.0),12.0)*uFlashlightOn;\n" +
"    float dist=length(vWorldPos-uLightPos);\n" +
"    float atten=smoothstep(18.0,0.5,dist);\n" +
"    float lit=beam*atten;\n" +
"    col=col*(1.0+lit*7.0*vec3(1.0,0.90,0.70));\n" +
// fog
"    vec3 fogColor=vec3(0.01,0.02,0.06);\n" +
"    float fog=smoothstep(3.0,16.0,length(vWorldPos-uCamPos));\n" +
"    col=mix(col,fogColor,fog);\n" +
"    col=floor(col*24.0)/24.0;\n" +
"    fragColor=vec4(col,1.0);\n" +
"}\n";

    // --- Upscale pass: nearest-neighbour + scanlines + vignette + dither -----
    private const string UpscaleVert =
"#version 330 core\n" +
"layout(location=0) in vec2 aPos;\n" +
"layout(location=1) in vec2 aUV;\n" +
"out vec2 vUV;\n" +
"void main(){ vUV=aUV; gl_Position=vec4(aPos,0.0,1.0); }\n";

    private const string UpscaleFrag =
"#version 330 core\n" +
"in vec2 vUV;\n" +
"out vec4 fragColor;\n" +
"uniform sampler2D uScene;\n" +
"uniform vec2 uScreenSize;\n" +
"uniform vec2 uPsxSize;\n" +
"void main(){\n" +
// Nearest-neighbour sample (texture already set to NEAREST, but be explicit)
"    vec3 col=texture(uScene,vUV).rgb;\n" +
// Scanlines: every other screen-pixel row darkened, based on PSX pixel row
"    float psxRow=floor(vUV.y*uPsxSize.y);\n" +
"    float scanline=mod(psxRow,2.0)<1.0 ? 0.75 : 1.0;\n" +
"    col*=scanline;\n" +
// Aperture-grille vertical lines (subtle)
"    float psxCol=floor(vUV.x*uPsxSize.x);\n" +
"    float grille=mod(psxCol,3.0)<2.0 ? 1.0 : 0.82;\n" +
"    col*=grille;\n" +
// Ordered dither at screen res to break up banding
"    vec2 px=floor(vUV*uScreenSize);\n" +
"    float bayer=fract(sin(dot(px,vec2(12.9898,78.233)))*43758.5453);\n" +
"    col+=(bayer-0.5)/55.0;\n" +
// Vignette
"    vec2 uv2=vUV*2.0-1.0;\n" +
"    float vig=1.0-dot(uv2,uv2)*0.45;\n" +
"    vig=clamp(vig,0.0,1.0);\n" +
"    col*=vig;\n" +
// Slight green-tint CRT phosphor shift
"    col=vec3(col.r*0.96, col.g*1.02, col.b*0.94);\n" +
"    fragColor=vec4(clamp(col,0.0,1.0),1.0);\n" +
"}\n";

    // --- HUD shaders (unchanged) ----------------------------------------------
    private const string HudVert =
"#version 330 core\n" +
"layout(location=0) in vec2 aPos;\n" +
"layout(location=1) in vec2 aUV;\n" +
"out vec2 vUV;\n" +
"uniform float uAspectRatio;\n" +
"void main(){\n" +
"    vec2 scale=vec2(0.07,0.07*uAspectRatio);\n" +
"    vec2 offset=vec2(-0.82,-0.65);\n" +
"    gl_Position=vec4(aPos*scale+offset,0.0,1.0);\n" +
"    vUV=aUV;\n" +
"}\n";

    private const string HudFrag =
"#version 330 core\n" +
"in vec2 vUV;\n" +
"out vec4 fragColor;\n" +
"uniform sampler2D uBatteryTex;\n" +
"uniform float uBatteryLevel;\n" +
"void main(){\n" +
"    vec2 uv=vec2(vUV.x,1.0-vUV.y);\n" +
"    vec4 tex=texture(uBatteryTex,uv);\n" +
"    if(tex.a<0.05) discard;\n" +
"    float luma=dot(tex.rgb,vec3(0.299,0.587,0.114));\n" +
"    if(luma>=0.5){ fragColor=vec4(floor(tex.rgb*28.0)/28.0,tex.a); return; }\n" +
"    float l=0.263,r=0.773,b=0.133,t=0.922;\n" +
"    if(uv.x<l||uv.x>r||uv.y<b||uv.y>t){ fragColor=vec4(0,0,0,1); return; }\n" +
"    vec2 inner=vec2((uv.x-l)/(r-l),(uv.y-b)/(t-b));\n" +
"    float cellIndex=floor(inner.y*3.0);\n" +
"    float cellLocalY=fract(inner.y*3.0);\n" +
"    float cellLocalX=inner.x;\n" +
"    float cellW=153.0,cellH=104.0;\n" +
"    vec2 cellPx=vec2(cellLocalX*cellW,cellLocalY*cellH);\n" +
"    float padPx=8.0,radiusPx=10.0;\n" +
"    vec2 innerSize=vec2(cellW-padPx*2.0,cellH-padPx*2.0);\n" +
"    vec2 q=abs(cellPx-vec2(cellW,cellH)*0.5)-innerSize*0.5+vec2(radiusPx);\n" +
"    float sdf=length(max(q,0.0))-radiusPx;\n" +
"    if(sdf>0.0){ fragColor=vec4(0,0,0,1); return; }\n" +
"    float cellFilled=(1.0-clamp(uBatteryLevel,0.0,1.0))*3.0;\n" +
"    bool lit=cellIndex>=floor(cellFilled);\n" +
"    if(cellIndex==floor(cellFilled)) lit=cellLocalY>=fract(cellFilled);\n" +
"    vec3 col;\n" +
"    if(!lit)                       col=vec3(0.0,0.0,0.0);\n" +
"    else if(uBatteryLevel>0.66)    col=vec3(0.06,0.82,0.19);\n" +
"    else if(uBatteryLevel>0.33)    col=vec3(0.83,0.68,0.07);\n" +
"    else                           col=vec3(0.82,0.12,0.06);\n" +
"    fragColor=vec4(floor(col*28.0)/28.0,1.0);\n" +
"}\n";
}
