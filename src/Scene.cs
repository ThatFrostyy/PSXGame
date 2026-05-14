using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

namespace PSXGame;

public class Scene : IDisposable
{
    public Mesh PlaneMesh { get; private set; }
    public Skybox Skybox { get; private set; }

    public Scene(GL gl)
    {
        PlaneMesh = BuildPlane(gl);
        Skybox = new Skybox(gl);
    }

    private static Mesh BuildPlane(GL gl)
    {
        float s = 40f;
        var verts = new List<float>();

        void V(float x, float z, float u, float v)
        {
            verts.Add(x); verts.Add(0); verts.Add(z);
            verts.Add(0); verts.Add(1); verts.Add(0);
            verts.Add(u); verts.Add(v);
            verts.Add(0.15f); verts.Add(0.14f); verts.Add(0.12f);
        }

        V(-s, -s, 0, 0); V(s, -s, 1, 0); V(s, s, 1, 1);
        V(-s, -s, 0, 0); V(s, s, 1, 1); V(-s, s, 0, 1);

        return new Mesh(gl, verts.ToArray());
    }

    public void Dispose()
    {
        PlaneMesh.Dispose();
        Skybox.Dispose();
    }
}
