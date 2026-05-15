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
/// </summary>
/// <remarks>
/// Assimp calls are serialized with a private lock because this loader uses a shared
/// static <see cref="Assimp"/> API instance. This guarantees that import/material-query/free
/// operations in this class do not execute concurrently.
/// </remarks>
public static class ModelLoader
{
    private static readonly Assimp _assimp = Assimp.GetApi();
    private static readonly object _assimpLock = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LoadedModel> _loadedModelCache = new(StringComparer.OrdinalIgnoreCase);

    // Updated record to include the GL context for cleanup
    public record LoadedModel(GL Gl, List<(Mesh Mesh, uint Texture)> Parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (mesh, texture) in Parts)
            {
                // Clean up the Mesh (VBO/VAO)
                mesh.Dispose();
                
                // Explicitly delete the texture from GPU memory
                if (texture != 0)
                {
                    Gl.DeleteTexture(texture);
                }
            }
        }
    }

    public static unsafe LoadedModel Load(GL gl, string fbxPath, string textureDir)
    {
        string cacheKey = Path.GetFullPath(fbxPath);
        if (_loadedModelCache.TryGetValue(cacheKey, out var cached))
            return cached;

        string modelBaseName = Path.GetFileNameWithoutExtension(fbxPath);
        var parts = new List<(Mesh, uint)>();

        lock (_assimpLock)
        {
            var scene = _assimp.ImportFile(fbxPath,
                (uint)(PostProcessSteps.Triangulate |
                    PostProcessSteps.GenerateNormals |
                    PostProcessSteps.FlipUVs |
                    PostProcessSteps.JoinIdenticalVertices));

            if (scene == null)
                throw new InvalidDataException($"Assimp failed to load '{fbxPath}'");

            try
            {
                if ((scene->MFlags & Assimp.SceneFlagsIncomplete) != 0 || scene->MRootNode == null)
                    throw new InvalidDataException($"Assimp failed to load '{fbxPath}'");

                ProcessNode(gl, scene, scene->MRootNode, textureDir, modelBaseName, parts);
            }
            finally
            {
                _assimp.FreeScene(scene);
            }
        }

        // Pass the GL context to the record so it can delete textures later
        var loaded = new LoadedModel(gl, parts);
        _loadedModelCache[cacheKey] = loaded;
        return loaded;
    }

    public static void ClearCache()
    {
        foreach (var model in _loadedModelCache.Values)
            model.Dispose();
        _loadedModelCache.Clear();
    }

    private static unsafe void ProcessNode(GL gl, Silk.NET.Assimp.Scene* scene,
        Node* node, string textureDir, string modelBaseName, List<(Mesh, uint)> parts)
    {
        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            var aiMesh = scene->MMeshes[node->MMeshes[i]];
            parts.Add(BuildMeshPart(gl, scene, aiMesh, textureDir, modelBaseName));
        }
        for (uint i = 0; i < node->MNumChildren; i++)
            ProcessNode(gl, scene, node->MChildren[i], textureDir, modelBaseName, parts);
    }

    private static unsafe (Mesh, uint) BuildMeshPart(GL gl,
        Silk.NET.Assimp.Scene* scene, Silk.NET.Assimp.Mesh* aiMesh,
        string textureDir, string modelBaseName)
    {
        var verts = new List<float>(capacity: (int)aiMesh->MNumVertices * 11);

        for (uint i = 0; i < aiMesh->MNumVertices; i++)
        {
            var p = aiMesh->MVertices[i];
            verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);

            var n = aiMesh->MNormals != null ? aiMesh->MNormals[i] : default;
            verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);

            if (aiMesh->MTextureCoords[0] != null)
            {
                var uv = aiMesh->MTextureCoords[0][i];
                verts.Add(uv.X); verts.Add(uv.Y);
            }
            else { verts.Add(0f); verts.Add(0f); }

            if (aiMesh->MColors[0] != null)
            {
                var c = aiMesh->MColors[0][i];
                verts.Add(c.X); verts.Add(c.Y); verts.Add(c.Z);
            }
            else { verts.Add(1f); verts.Add(1f); verts.Add(1f); }
        }

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
        uint tex = 0;

        if (aiMesh->MMaterialIndex < scene->MNumMaterials)
        {
            var mat = scene->MMaterials[aiMesh->MMaterialIndex];
            var texPath = GetDiffuseTexturePath(mat);
            if (texPath != null)
            {
                string candidate = Path.Combine(textureDir, Path.GetFileName(texPath));
                if (!System.IO.File.Exists(candidate)) candidate = texPath;
                if (System.IO.File.Exists(candidate))
                    tex = LoadTexture(gl, candidate);
            }
        }

        if (tex == 0)
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string candidate = Path.Combine(textureDir, modelBaseName + ext);
                if (System.IO.File.Exists(candidate))
                {
                    tex = LoadTexture(gl, candidate);
                    break;
                }
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
        using var fs = System.IO.File.OpenRead(path);
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
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        return tex;
    }
}