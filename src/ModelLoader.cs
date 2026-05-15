using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using Silk.NET.Maths;
using StbImageSharp;

namespace PSXGame;

/// <summary>
/// Loads an FBX (or any Assimp-supported format) into one or more GPU meshes,
/// each paired with an optional diffuse texture.
/// Vertex layout matches the existing Mesh stride: pos(3)+normal(3)+uv(2)+color(3) = 11 floats.
/// </summary>
public static class ModelLoader
{
    // Shared Assimp instance — cheap to reuse across loads.
    private static readonly Assimp _assimp = Assimp.GetApi();

    public record LoadedModel(List<(Mesh Mesh, uint Texture)> Parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (mesh, _) in Parts)
                mesh.Dispose();
        }
    }

    public static unsafe LoadedModel Load(GL gl, string fbxPath, string textureDir)
    {
        var scene = _assimp.ImportFile(fbxPath,
            (uint)(PostProcessSteps.Triangulate |
                   PostProcessSteps.GenerateNormals |
                   PostProcessSteps.FlipUVs |
                   PostProcessSteps.JoinIdenticalVertices));

        if (scene == null || (scene->MFlags & Assimp.SceneFlagsIncomplete) != 0 || scene->MRootNode == null)
            throw new InvalidDataException($"Assimp failed to load '{fbxPath}'");

        var parts = new List<(Mesh, uint)>();
        ProcessNode(gl, scene, scene->MRootNode, textureDir, parts);
        _assimp.FreeScene(scene);
        return new LoadedModel(parts);
    }

    private static unsafe void ProcessNode(GL gl, Silk.NET.Assimp.Scene* scene,
        Node* node, string textureDir, List<(Mesh, uint)> parts)
    {
        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            var aiMesh = scene->MMeshes[node->MMeshes[i]];
            parts.Add(BuildMeshPart(gl, scene, aiMesh, textureDir));
        }
        for (uint i = 0; i < node->MNumChildren; i++)
            ProcessNode(gl, scene, node->MChildren[i], textureDir, parts);
    }

    private static unsafe (Mesh, uint) BuildMeshPart(GL gl,
        Silk.NET.Assimp.Scene* scene, Silk.NET.Assimp.Mesh* aiMesh, string textureDir)
    {
        var verts = new List<float>(capacity: (int)aiMesh->MNumVertices * 11);

        for (uint i = 0; i < aiMesh->MNumVertices; i++)
        {
            // Position
            var p = aiMesh->MVertices[i];
            verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);

            // Normal
            var n = aiMesh->MNumNormals > 0 ? aiMesh->MNormals[i] : default;
            verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);

            // UV (channel 0)
            if (aiMesh->MTextureCoords[0] != null)
            {
                var uv = aiMesh->MTextureCoords[0][i];
                verts.Add(uv.X); verts.Add(uv.Y);
            }
            else
            {
                verts.Add(0f); verts.Add(0f);
            }

            // Vertex color (channel 0) — fall back to neutral grey
            if (aiMesh->MColors[0] != null)
            {
                var c = aiMesh->MColors[0][i];
                verts.Add(c.R); verts.Add(c.G); verts.Add(c.B);
            }
            else
            {
                verts.Add(1f); verts.Add(1f); verts.Add(1f);
            }
        }

        // Re-index triangles into a flat unindexed buffer (matches existing Mesh design)
        var flat = new List<float>(capacity: (int)aiMesh->MNumFaces * 3 * 11);
        for (uint f = 0; f < aiMesh->MNumFaces; f++)
        {
            var face = aiMesh->MFaces[f];
            if (face.MNumIndices != 3) continue;
            for (int k = 0; k < 3; k++)
            {
                uint idx = face.MIndices[k];
                int off = (int)idx * 11;
                for (int j = 0; j < 11; j++)
                    flat.Add(verts[off + j]);
            }
        }

        var mesh = new Mesh(gl, flat.ToArray());

        // Resolve diffuse texture from the material
        uint tex = 0;
        if (aiMesh->MMaterialIndex < scene->MNumMaterials)
        {
            var mat = scene->MMaterials[aiMesh->MMaterialIndex];
            var texPath = GetDiffuseTexturePath(mat);
            if (texPath != null)
            {
                // Try the texture dir, then the path as-is
                string candidate = Path.Combine(textureDir, Path.GetFileName(texPath));
                if (!File.Exists(candidate)) candidate = texPath;
                if (File.Exists(candidate))
                    tex = LoadTexture(gl, candidate);
            }
        }

        return (mesh, tex);
    }

    private static unsafe string? GetDiffuseTexturePath(Silk.NET.Assimp.Material* mat)
    {
        AssimpString path = default;
        var result = _assimp.GetMaterialTexture(mat,
            TextureType.Diffuse, 0, ref path,
            null, null, null, null, null, null);
        if (result != Return.Success) return null;
        string s = path.AsString;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static uint LoadTexture(GL gl, string path)
    {
        using var fs = File.OpenRead(path);
        var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = img.Data)
                gl.TexImage2D(TextureTarget.Texture2D, 0,
                    (int)InternalFormat.Rgba8,
                    (uint)img.Width, (uint)img.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        // Nearest for that PSX look
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        return tex;
    }
}
