using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

namespace PSXGame;

public class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocations = new();

    public ShaderProgram(GL gl, string vertSrc, string fragSrc)
    {
        _gl = gl;

        uint vert = Compile(ShaderType.VertexShader, vertSrc);
        uint frag = Compile(ShaderType.FragmentShader, fragSrc);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vert);
        _gl.AttachShader(_handle, frag);
        _gl.LinkProgram(_handle);

        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new Exception($"Shader link error: {_gl.GetProgramInfoLog(_handle)}");

        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);
    }

    private uint Compile(ShaderType type, string src)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, src);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
            throw new Exception($"{type} compile error:\n{_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    public void Use() => _gl.UseProgram(_handle);

    private int GetUniformLocationCached(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            return location;

        location = _gl.GetUniformLocation(_handle, name);
        _uniformLocations[name] = location;
        return location;
    }

    public void SetMatrix4(string name, Matrix4X4<float> mat)
    {
        int loc = GetUniformLocationCached(name);
        if (loc < 0) return;
        unsafe { _gl.UniformMatrix4(loc, 1, false, (float*)&mat); }
    }

    public void SetVec3(string name, Vector3D<float> v)
    {
        int loc = GetUniformLocationCached(name);
        if (loc >= 0) _gl.Uniform3(loc, v.X, v.Y, v.Z);
    }

    public void SetVector2(string name, Vector2D<float> v)
    {
        int loc = GetUniformLocationCached(name);
        if (loc >= 0) _gl.Uniform2(loc, v.X, v.Y);
    }

    public void SetFloat(string name, float v)
    {
        int loc = GetUniformLocationCached(name);
        if (loc >= 0) _gl.Uniform1(loc, v);
    }

    public void SetInt(string name, int v)
    {
        int loc = GetUniformLocationCached(name);
        if (loc >= 0) _gl.Uniform1(loc, v);
    }

    public void Dispose() => _gl.DeleteProgram(_handle);
}
