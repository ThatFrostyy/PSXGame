using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

namespace PSXGame;

/// <summary>
/// A placed instance of a loaded model in the world.
/// </summary>
public readonly record struct PropInstance(
    ModelLoader.LoadedModel Model,
    Matrix4X4<float> Transform);

public class Scene : IDisposable
{
    public Mesh PlaneMesh { get; private set; }
    public Skybox Skybox { get; private set; }

    /// <summary>All placed tree and bush instances, ready for the renderer.</summary>
    public IReadOnlyList<PropInstance> Props => _props;
    private readonly List<PropInstance> _props = new();

    // Textures shared across all prop draws (keyed by GL handle)
    // Loaded models own their per-mesh textures; extra fallback textures live here.
    private readonly List<uint> _ownedTextures = new();

    public Scene(GL gl)
    {
        PlaneMesh = BuildPlane(gl);
        Skybox = new Skybox(gl);
        SpawnProps(gl);
    }

    // -------------------------------------------------------------------------
    // Ground plane
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // Prop spawning
    // -------------------------------------------------------------------------
    private void SpawnProps(GL gl)
    {
        string modelDir   = ResolveDir("src", "models");
        string textureDir = ResolveDir("src", "textures");

        var rng = new Random(1337); // fixed seed → same layout every run

        // --- Trees: ring around the edge (radius 28-38, outside fog zone) -------
        // Discover how many tree variants exist (tree1.fbx, tree2.fbx, …)
        var treeFiles = DiscoverModels(modelDir, "tree");
        var treeModels = CacheModels(gl, treeFiles, textureDir);

        // Place ~30 trees in a ring, slight radius randomness so it doesn't look uniform
        const int treeCount = 30;
        const float treeRingMin = 28f;
        const float treeRingMax = 37f;
        for (int i = 0; i < treeCount; i++)
        {
            float angle  = i * (MathF.Tau / treeCount) + rng.NextSingle() * 0.4f;
            float radius = treeRingMin + rng.NextSingle() * (treeRingMax - treeRingMin);
            float x      = MathF.Cos(angle) * radius;
            float z      = MathF.Sin(angle) * radius;
            float scale  = 0.9f + rng.NextSingle() * 0.6f;   // 0.9 – 1.5
            float yaw    = rng.NextSingle() * MathF.Tau;

            var model = treeModels[rng.Next(treeModels.Count)];
            _props.Add(new PropInstance(model, MakeTRS(x, 0f, z, yaw, scale)));
        }

        // --- Bushes: scattered inside radius 3-12 (near player) -----------------
        var bushFiles = DiscoverModels(modelDir, "bush");
        var bushModels = CacheModels(gl, bushFiles, textureDir);

        const int bushCount = 18;
        for (int i = 0; i < bushCount; i++)
        {
            // Polar placement, avoid spawning right on top of the player (min r=3)
            float angle  = rng.NextSingle() * MathF.Tau;
            float radius = 3f + rng.NextSingle() * 9f;
            float x      = MathF.Cos(angle) * radius;
            float z      = MathF.Sin(angle) * radius;
            float scale  = 0.5f + rng.NextSingle() * 0.5f;  // 0.5 – 1.0
            float yaw    = rng.NextSingle() * MathF.Tau;

            var model = bushModels[rng.Next(bushModels.Count)];
            _props.Add(new PropInstance(model, MakeTRS(x, 0f, z, yaw, scale)));
        }
    }

    /// <summary>
    /// Returns file paths for models named prefix1.fbx, prefix2.fbx, … that actually exist.
    /// </summary>
    private static List<string> DiscoverModels(string dir, string prefix)
    {
        var found = new List<string>();
        if (!System.IO.Directory.Exists(dir)) return found;

        for (int n = 1; n <= 20; n++)          // support up to 20 variants
        {
            string path = System.IO.Path.Combine(dir, $"{prefix}{n}.fbx");
            if (System.IO.File.Exists(path))
                found.Add(path);
            else if (n > 1)
                break;  // stop as soon as the sequence ends
        }
        return found;
    }

    /// <summary>
    /// Loads each model file once and returns the list. Duplicate paths reuse the same instance.
    /// </summary>
    private static List<ModelLoader.LoadedModel> CacheModels(
        GL gl, List<string> paths, string textureDir)
    {
        var models = new List<ModelLoader.LoadedModel>();
        foreach (var path in paths)
        {
            try
            {
                models.Add(ModelLoader.Load(gl, path, textureDir));
                Console.WriteLine($"[Scene] Loaded {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Scene] Failed to load {path}: {ex.Message}");
            }
        }
        // If nothing loaded (missing files etc.) return an empty list;
        // the spawn loops will simply produce no instances for that type.
        return models;
    }

    /// <summary>Build a TRS matrix: translate to (x,y,z), rotate Y by yaw, uniform scale.</summary>
    private static Matrix4X4<float> MakeTRS(float x, float y, float z, float yaw, float scale)
    {
        var t = Matrix4X4.CreateTranslation<float>(x, y, z);
        var r = Matrix4X4.CreateRotationY<float>(yaw);
        var s = Matrix4X4.CreateScale<float>(scale);
        return s * r * t;   // scale → rotate → translate
    }

    /// <summary>Resolve a path relative to the app base directory, with cwd fallback.</summary>
    private static string ResolveDir(params string[] parts)
    {
        string path = System.IO.Path.Combine(
            new string[] { AppContext.BaseDirectory }.Concat(parts) is var a
                ? System.IO.Path.Combine(a.ToArray())
                : "");
        // simpler:
        path = System.IO.Path.Combine(AppContext.BaseDirectory, System.IO.Path.Combine(parts));
        if (!System.IO.Directory.Exists(path))
            path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                                          System.IO.Path.Combine(parts));
        return path;
    }

    // -------------------------------------------------------------------------
    public void Dispose()
    {
        PlaneMesh.Dispose();
        Skybox.Dispose();

        // Dispose each unique model once (multiple PropInstances may share the same model)
        var disposed = new HashSet<ModelLoader.LoadedModel>();
        foreach (var prop in _props)
        {
            if (disposed.Add(prop.Model))
                prop.Model.Dispose();
        }

        foreach (var tex in _ownedTextures)
            ; // textures are owned by LoadedModel, nothing extra here
    }
}
