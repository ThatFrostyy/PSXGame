using System;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

namespace PSXGame;

public class Skybox : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly ShaderProgram _shader;

    public Skybox(GL gl)
    {
        _gl = gl;
        _shader = new ShaderProgram(gl, Vert, Frag);

        // Large cube, no tricks - just a big box around the world
        float s = 90f;
        float[] verts = {
            -s, s,-s,  -s,-s,-s,   s,-s,-s,   s,-s,-s,   s, s,-s,  -s, s,-s,
            -s,-s, s,  -s,-s,-s,  -s, s,-s,  -s, s,-s,  -s, s, s,  -s,-s, s,
             s,-s,-s,   s,-s, s,   s, s, s,   s, s, s,   s, s,-s,   s,-s,-s,
            -s,-s, s,  -s, s, s,   s, s, s,   s, s, s,   s,-s, s,  -s,-s, s,
            -s, s,-s,   s, s,-s,   s, s, s,   s, s, s,  -s, s, s,  -s, s,-s,
            -s,-s,-s,  -s,-s, s,   s,-s,-s,   s,-s,-s,  -s,-s, s,   s,-s, s,
        };

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = verts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(verts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false,
                3 * sizeof(float), (void*)0);
        }
        _gl.BindVertexArray(0);
    }

    public void Draw(Matrix4X4<float> view, Matrix4X4<float> projection)
    {
        // Remove translation from view matrix so skybox is always centered
        var v = new Matrix4X4<float>(
            view.M11, view.M12, view.M13, 0,
            view.M21, view.M22, view.M23, 0,
            view.M31, view.M32, view.M33, 0,
            0, 0, 0, 1);

        // DepthTest must be ENABLED for DepthFunc/DepthMask to have any effect.
        // We write gl_Position with w==1 and use DepthFunc.LessOrEqual so the
        // skybox renders behind everything that was already in the depth buffer,
        // but the depth buffer itself is not written (DepthMask false).
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);

        _shader.Use();
        _shader.SetMatrix4("uView", v);
        _shader.SetMatrix4("uProjection", projection);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
        _gl.BindVertexArray(0);

        // Restore state to sane defaults for subsequent draws
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
        // NOTE: DepthTest stays enabled; Renderer.Render() relies on it.
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _shader.Dispose();
    }

private const string Vert =
"#version 330 core\n" +
"layout(location=0) in vec3 aPos;\n" +
"out vec3 vDir;\n" +
"uniform mat4 uView;\n" +
"uniform mat4 uProjection;\n" +
"void main() {\n" +
"    vDir = aPos;\n" +
"    vec4 clip = uProjection * uView * vec4(aPos, 1.0);\n" +
"    gl_Position = clip.xyww;\n" +
"}\n";

    private const string Frag =
"#version 330 core\n" +
"in vec3 vDir;\n" +
"out vec4 fragColor;\n" +
"float hash(vec3 p) {\n" +
"    p = fract(p * 0.3183099 + 0.1);\n" +
"    p *= 17.0;\n" +
"    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));\n" +
"}\n" +
"void main() {\n" +
"    vec3 dir = normalize(vDir);\n" +
"    float t = clamp(dir.y * 0.5 + 0.5, 0.0, 1.0);\n" +
"    vec3 sky = mix(vec3(0.04, 0.04, 0.10), vec3(0.0, 0.0, 0.04), t);\n" +
"    float starVis = smoothstep(0.05, 0.25, dir.y);\n" +
"    vec3 sg = floor(dir * 90.0);\n" +
"    float star = step(0.988, hash(sg));\n" +
"    float sbright = hash(sg + vec3(1.0, 0.0, 0.0)) * 0.7 + 0.3;\n" +
"    sky += star * sbright * starVis * vec3(0.85, 0.9, 1.0);\n" +
"    vec3 moonDir = normalize(vec3(0.3, 0.6, -0.7));\n" +
"    float md = dot(dir, moonDir);\n" +
"    float moon = smoothstep(0.9980, 0.9995, md);\n" +
"    float glow = smoothstep(0.970, 0.9980, md) * 0.12;\n" +
"    sky += moon * vec3(0.92, 0.94, 0.85) * 2.0;\n" +
"    sky += glow * vec3(0.5, 0.55, 0.45);\n" +
"    fragColor = vec4(sky, 1.0);\n" +
"}\n";
}
