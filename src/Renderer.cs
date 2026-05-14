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
    private readonly ShaderProgram _planeShader;
    private readonly ShaderProgram _batteryShader;
    private readonly uint _batteryTexture;
    private readonly uint _hudVao;
    private readonly uint _hudVbo;

    public Renderer(GL gl, Vector2D<int> screenSize)
    {
        _gl = gl;
        _screenSize = screenSize;
        _planeShader = new ShaderProgram(gl, PlaneVert, PlaneFrag);
        _batteryShader = new ShaderProgram(gl, HudVert, HudFrag);
        _batteryTexture = LoadBatteryTexture();

        _hudVao = _gl.GenVertexArray();
        _hudVbo = _gl.GenBuffer();
        float[] hudVerts =
        [
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        ];
        _gl.BindVertexArray(_hudVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _hudVbo);
        unsafe
        {
            fixed (float* p = hudVerts)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(hudVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
        }
        unsafe
        {
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
    }

    public void Render(Scene scene, Camera cam)
    {
        _gl.Viewport(0, 0, (uint)_screenSize.X, (uint)_screenSize.Y);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = (float)_screenSize.X / _screenSize.Y;
        var view = cam.GetViewMatrix();
        var proj = cam.GetProjectionMatrix(aspect);

        // 1. Draw skybox first (depth always passes, no depth write)
        scene.Skybox.Draw(view, proj);

        // 2. Draw ground plane normally
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Disable(EnableCap.CullFace);

        _planeShader.Use();
        _planeShader.SetMatrix4("uView", view);
        _planeShader.SetMatrix4("uProjection", proj);
        _planeShader.SetMatrix4("uModel", Matrix4X4<float>.Identity);
        _planeShader.SetVec3("uCamPos", cam.Position);
        _planeShader.SetVec3("uCamDir", cam.Front);
        Vector3D<float> lightPos = cam.Position + (cam.Front * 0.45f) - (cam.Up * 0.35f) + (cam.Right * 0.16f);
        _planeShader.SetVec3("uLightPos", lightPos);
        float flashlight = cam.FlashlightOn ? cam.FlashlightIntensity : 0f;
        _planeShader.SetFloat("uFlashlightOn", flashlight);
        _planeShader.SetVector2("uResolution", new Vector2D<float>(_screenSize.X, _screenSize.Y));
        scene.PlaneMesh.Draw();

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _batteryShader.Use();
        _batteryShader.SetFloat("uBatteryLevel", cam.BatteryLevel);
        _batteryShader.SetInt("uBatteryTex", 0);
        _batteryShader.SetFloat("uAspectRatio", aspect);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _batteryTexture);
        _gl.BindVertexArray(_hudVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.CullFace);
    }

    public void Resize(Vector2D<int> newSize)
    {
        _screenSize = newSize;
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_batteryTexture);
        _gl.DeleteBuffer(_hudVbo);
        _gl.DeleteVertexArray(_hudVao);
        _batteryShader.Dispose();
        _planeShader.Dispose();
    }

    private uint LoadBatteryTexture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "src", "textures", "battery.png");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "src", "textures", "battery.png");
        }

        using var fs = File.OpenRead(path);
        var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = img.Data)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8, (uint)img.Width, (uint)img.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            }
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return tex;
    }

    private const string PlaneVert =
"#version 330 core\n" +
"layout(location=0) in vec3 aPos;\n" +
"layout(location=1) in vec3 aNormal;\n" +
"layout(location=2) in vec2 aUV;\n" +
"layout(location=3) in vec3 aColor;\n" +
"out vec3 vColor;\n" +
"out vec2 vUV;\n" +
"out vec3 vWorldPos;\n" +
"uniform mat4 uModel;\n" +
"uniform mat4 uView;\n" +
"uniform mat4 uProjection;\n" +
"void main() {\n" +
"    vColor = aColor;\n" +
"    vUV = aUV * 40.0;\n" +
"    vec4 worldPos = uModel * vec4(aPos, 1.0);\n" +
"    vWorldPos = worldPos.xyz;\n" +
"    vec4 clip = uProjection * uView * worldPos;\n" +
"    float snap = 240.0;\n" +
"    clip.xy = floor(clip.xy * snap) / snap;\n" +
"    gl_Position = clip;\n" +
"}\n";

    private const string PlaneFrag =
"#version 330 core\n" +
"in vec3 vColor;\n" +
"in vec2 vUV;\n" +
"in vec3 vWorldPos;\n" +
"out vec4 fragColor;\n" +
"uniform vec3 uCamPos;\n" +
"uniform vec3 uLightPos;\n" +
"uniform vec3 uCamDir;\n" +
"uniform float uFlashlightOn;\n" +
"uniform vec2 uResolution;\n" +
"void main() {\n" +
"    vec2 g = abs(fract(vUV - 0.5) - 0.5) / fwidth(vUV);\n" +
"    float line = 1.0 - min(min(g.x, g.y), 1.0);\n" +
"    vec3 col = vColor + line * 0.05;\n" +
"    col *= vec3(0.26, 0.30, 0.36);\n" +
"    vec2 d = gl_FragCoord.xy / uResolution;\n" +
"    float bayer = fract(sin(dot(floor(d * 320.0), vec2(12.9898, 78.233))) * 43758.5453);\n" +
"    col += (bayer - 0.5) / 90.0;\n" +
"    vec3 toFrag = normalize(vWorldPos - uLightPos);\n" +
"    float beam = pow(max(dot(toFrag, uCamDir), 0.0), 22.0) * uFlashlightOn;\n" +
"    float dist = length(vWorldPos - uLightPos);\n" +
"    col += vec3(1.0, 0.95, 0.8) * beam * smoothstep(14.0, 0.0, dist) * 2.35;\n" +
"    vec3 fogColor = vec3(0.015, 0.02, 0.03);\n" +
"    float fog = smoothstep(7.5, 28.0, length(vWorldPos - uCamPos));\n" +
"    col = mix(col, fogColor, fog);\n" +
"    col = floor(col * 28.0) / 28.0;\n" +
"    fragColor = vec4(col, 1.0);\n" +
"}\n";

private const string HudVert =
"#version 330 core\n" +
"layout(location=0) in vec2 aPos;\n" +
"layout(location=1) in vec2 aUV;\n" +
"out vec2 vUV;\n" +
"uniform float uAspectRatio;\n" +
"void main(){\n" +
"    vec2 scale = vec2(0.07, 0.07 * uAspectRatio);\n" +  // smaller
"    vec2 offset = vec2(-0.82, -0.65);\n" +
"    gl_Position = vec4(aPos * scale + offset, 0.0, 1.0);\n" +
"    vUV = aUV;\n" +
"}\n";

private const string HudFrag =
"#version 330 core\n" +
"in vec2 vUV;\n" +
"out vec4 fragColor;\n" +
"uniform sampler2D uBatteryTex;\n" +
"uniform float uBatteryLevel;\n" +
"void main(){\n" +
"    vec2 uv = vec2(vUV.x, 1.0 - vUV.y);\n" +
"    vec4 tex = texture(uBatteryTex, uv);\n" +
"    if (tex.a < 0.05) discard;\n" +
"    float luma = dot(tex.rgb, vec3(0.299, 0.587, 0.114));\n" +
"    if (luma >= 0.5) {\n" +
"        fragColor = vec4(floor(tex.rgb * 28.0) / 28.0, tex.a);\n" +
"        return;\n" +
"    }\n" +
    // exact inner bounds measured from texture pixels
    // left=79/300, right=232/300, top=53/398, bottom=367/398
"    float l=0.263, r=0.773, b=0.133, t=0.922;\n" +
"    if (uv.x < l || uv.x > r || uv.y < b || uv.y > t) {\n" +
"        fragColor = vec4(0.0, 0.0, 0.0, 1.0);\n" +
"        return;\n" +
"    }\n" +
"    vec2 inner = vec2((uv.x - l)/(r - l), (uv.y - b)/(t - b));\n" +
    // inner area is 153x314px, each cell is 153x(314/3)=153x104px
"    float cellIndex = floor(inner.y * 3.0);\n" +
"    float cellLocalY = fract(inner.y * 3.0);\n" +
"    float cellLocalX = inner.x;\n" +
"    float cellW = 153.0, cellH = 104.0;\n" +
"    vec2 cellPx = vec2(cellLocalX * cellW, cellLocalY * cellH);\n" +
"    float padPx = 8.0;\n" +
"    float radiusPx = 10.0;\n" +
"    vec2 innerSize = vec2(cellW - padPx*2.0, cellH - padPx*2.0);\n" +
"    vec2 q = abs(cellPx - vec2(cellW, cellH)*0.5) - innerSize*0.5 + vec2(radiusPx);\n" +
"    float sdf = length(max(q, 0.0)) - radiusPx;\n" +
"    if (sdf > 0.0) {\n" +
"        fragColor = vec4(0.0, 0.0, 0.0, 1.0);\n" +
"        return;\n" +
"    }\n" +
"    float cellFilled = (1.0 - clamp(uBatteryLevel, 0.0, 1.0)) * 3.0;\n" +
"    bool lit = cellIndex >= floor(cellFilled);\n" +
"    if (cellIndex == floor(cellFilled)) lit = cellLocalY >= fract(cellFilled);\n" +
"    vec3 col;\n" +
"    if (!lit)                      { col = vec3(0.0,  0.0,  0.0 ); }\n" +
"    else if (uBatteryLevel > 0.66) { col = vec3(0.06, 0.82, 0.19); }\n" +
"    else if (uBatteryLevel > 0.33) { col = vec3(0.83, 0.68, 0.07); }\n" +
"    else                           { col = vec3(0.82, 0.12, 0.06); }\n" +
"    fragColor = vec4(floor(col * 28.0) / 28.0, 1.0);\n" +
"}\n";
}
