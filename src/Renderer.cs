using System;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

namespace PSXGame;

public class Renderer : IDisposable
{
    private readonly GL _gl;
    private readonly Vector2D<int> _screenSize;
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
        scene.PlaneMesh.Draw();

        _gl.Enable(EnableCap.CullFace);
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
"    gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);\n" +
"}\n";

    private const string PlaneFrag =
"#version 330 core\n" +
"in vec3 vColor;\n" +
"in vec2 vUV;\n" +
"out vec4 fragColor;\n" +
"void main() {\n" +
"    vec2 g = abs(fract(vUV - 0.5) - 0.5) / fwidth(vUV);\n" +
"    float line = 1.0 - min(min(g.x, g.y), 1.0);\n" +
"    vec3 col = vColor + line * 0.05;\n" +
"    col *= vec3(0.60, 0.68, 0.82);\n" +
"    fragColor = vec4(col, 1.0);\n" +
"}\n";
}
