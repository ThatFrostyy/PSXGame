using System;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

namespace PSXGame;

public class Renderer : IDisposable
{
    private readonly GL _gl;
    private Vector2D<int> _screenSize;
    private readonly ShaderProgram _planeShader;

    public Renderer(GL gl, Vector2D<int> screenSize)
    {
        _gl = gl;
        _screenSize = screenSize;
        _planeShader = new ShaderProgram(gl, PlaneVert, PlaneFrag);
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
        _planeShader.SetFloat("uFlashlightOn", cam.FlashlightOn ? 1f : 0f);
        _planeShader.SetVector2("uResolution", new Vector2D<float>(_screenSize.X, _screenSize.Y));
        scene.PlaneMesh.Draw();

        _gl.Enable(EnableCap.CullFace);
    }

    public void Resize(Vector2D<int> newSize)
    {
        _screenSize = newSize;
    }

    public void Dispose()
    {
        _planeShader.Dispose();
    }

    private const string PlaneVert =
"#version 330 core\n" +
"layout(location=0) in vec3 aPos;\n" +
"layout(location=1) in vec3 aNormal;\n" +
"layout(location=2) in vec2 aUV;\n" +
"layout(location=3) in vec3 aColor;\n" +
"out vec3 vColor;\n" +
"out vec2 vUV;\n" +
"uniform mat4 uModel;\n" +
"uniform mat4 uView;\n" +
"uniform mat4 uProjection;\n" +
"void main() {\n" +
"    vColor = aColor;\n" +
"    vUV = aUV * 40.0;\n" +
"    vec4 clip = uProjection * uView * uModel * vec4(aPos, 1.0);\n" +
"    float snap = 240.0;\n" +
"    clip.xy = floor(clip.xy * snap) / snap;\n" +
"    gl_Position = clip;\n" +
"}\n";

    private const string PlaneFrag =
"#version 330 core\n" +
"in vec3 vColor;\n" +
"in vec2 vUV;\n" +
"out vec4 fragColor;\n" +
"uniform vec3 uCamPos;\n" +
"uniform vec3 uCamDir;\n" +
"uniform float uFlashlightOn;\n" +
"uniform vec2 uResolution;\n" +
"void main() {\n" +
"    vec2 g = abs(fract(vUV - 0.5) - 0.5) / fwidth(vUV);\n" +
"    float line = 1.0 - min(min(g.x, g.y), 1.0);\n" +
"    vec3 col = vColor + line * 0.05;\n" +
"    col *= vec3(0.60, 0.68, 0.82);\n" +
"    vec2 d = gl_FragCoord.xy / uResolution;\n" +
"    float bayer = fract(sin(dot(floor(d * 320.0), vec2(12.9898, 78.233))) * 43758.5453);\n" +
"    col += (bayer - 0.5) / 90.0;\n" +
"    vec3 worldPos = vec3(vUV.x / 40.0 * 80.0 - 40.0, 0.0, vUV.y / 40.0 * 80.0 - 40.0);\n" +
"    vec3 toFrag = normalize(worldPos - uCamPos);\n" +
"    float beam = pow(max(dot(toFrag, uCamDir), 0.0), 22.0) * uFlashlightOn;\n" +
"    float dist = length(worldPos - uCamPos);\n" +
"    col += vec3(1.0, 0.95, 0.8) * beam * smoothstep(16.0, 0.0, dist) * 2.0;\n" +
"    col = floor(col * 28.0) / 28.0;\n" +
"    fragColor = vec4(col, 1.0);\n" +
"}\n";
}
