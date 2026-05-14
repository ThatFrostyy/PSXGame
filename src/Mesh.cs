using System;
using Silk.NET.OpenGL;

namespace PSXGame;

/// <summary>
/// A simple VAO/VBO wrapper.
/// Vertex layout: position(3) + normal(3) + uv(2) + color(3) = 11 floats per vertex
/// </summary>
public class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly int _vertexCount;

    public const int Stride = 11 * sizeof(float);

    public Mesh(GL gl, float[] vertices)
    {
        _gl = gl;
        _vertexCount = vertices.Length / 11;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        unsafe
        {
            fixed (float* ptr = vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * sizeof(float)),
                    ptr,
                    BufferUsageARB.StaticDraw);
        }

        // position  loc=0
        _gl.EnableVertexAttribArray(0);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)Stride, (void*)0); }

        // normal    loc=1
        _gl.EnableVertexAttribArray(1);
        unsafe { _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)Stride, (void*)(3 * sizeof(float))); }

        // uv        loc=2
        _gl.EnableVertexAttribArray(2);
        unsafe { _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)Stride, (void*)(6 * sizeof(float))); }

        // color     loc=3
        _gl.EnableVertexAttribArray(3);
        unsafe { _gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)Stride, (void*)(8 * sizeof(float))); }

        _gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
