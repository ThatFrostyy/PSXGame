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
    private const int MaxLoadedTreeVariants = 10;
    public const float MapHalfExtent = 75f;
    private const float EdgeForestBand = 16f;
    private const float InnerForestRadius = 18f;
    private const float TreeCollisionRadius = 1.45f;
    private const float BushCollisionRadius = 0.8f;

    public Mesh PlaneMesh { get; private set; }
    public Skybox Skybox { get; private set; }

    /// <summary>Placed tree and bush instances grouped by model, ready for instanced rendering.</summary>
    public IReadOnlyDictionary<ModelLoader.LoadedModel, List<Matrix4X4<float>>> PropsByModel => _propsByModel;
    private readonly Dictionary<ModelLoader.LoadedModel, List<Matrix4X4<float>>> _propsByModel = new();
    public IReadOnlyList<(Vector2D<float> Position, float Radius)> TreeColliders => _treeColliders;
    private readonly List<(Vector2D<float> Position, float Radius)> _treeColliders = new();

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
        float s = MapHalfExtent;
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

        var rng = new Random(1337);

        // --- Trees ---
        var treeFiles = DiscoverModels(modelDir, "tree");
        var selectedTreeFiles = ChooseRandomSubset(treeFiles, MaxLoadedTreeVariants, rng);
        // Resolve the specific subdirectory for tree textures
        string treeTexDir = ResolveDir("src", "textures", "trees"); 
        var treeModels = CacheModels(gl, selectedTreeFiles, treeTexDir);

        SpawnTrees(rng, treeModels);

        // --- Bushes ---
        var bushFiles = DiscoverModels(modelDir, "bush");
        // Resolve the specific subdirectory for bush textures
        string bushTexDir = ResolveDir("src", "textures", "bushes");
        var bushModels = CacheModels(gl, bushFiles, bushTexDir);

        const int bushCount = 26;
        for (int i = 0; i < bushCount; i++)
        {
            float angle  = rng.NextSingle() * MathF.Tau;
            float radius = 7f + rng.NextSingle() * (MapHalfExtent - 14f);
            float x      = MathF.Cos(angle) * radius;
            float z      = MathF.Sin(angle) * radius;
            float scale  = 0.005f + rng.NextSingle() * 0.003f;
            float yaw    = rng.NextSingle() * MathF.Tau;

            if (bushModels.Count == 0) continue;
            var model = bushModels[rng.Next(bushModels.Count)];
            AddProp(model, MakeTRS(x, 0f, z, yaw, scale));
            _treeColliders.Add((new Vector2D<float>(x, z), BushCollisionRadius));
        }
    }

    private void SpawnTrees(Random rng, List<ModelLoader.LoadedModel> treeModels)
    {
        if (treeModels.Count == 0) return;

        // Dense tree belt near the map edges.
        SpawnTreeGroup(rng, treeModels, count: 180, minDist: 2.5f, samplePosition: () =>
        {
            float x = (rng.NextSingle() * 2f - 1f) * MapHalfExtent;
            float z = (rng.NextSingle() * 2f - 1f) * MapHalfExtent;
            if (MathF.Abs(x) < MapHalfExtent - EdgeForestBand && MathF.Abs(z) < MapHalfExtent - EdgeForestBand)
            {
                bool verticalBand = rng.NextSingle() < 0.5f;
                if (verticalBand) x = MathF.CopySign(MapHalfExtent - rng.NextSingle() * EdgeForestBand, rng.NextSingle() < 0.5f ? -1f : 1f);
                else z = MathF.CopySign(MapHalfExtent - rng.NextSingle() * EdgeForestBand, rng.NextSingle() < 0.5f ? -1f : 1f);
            }
            return new Vector2D<float>(x, z);
        });

        // Middle forest cluster.
        SpawnTreeGroup(rng, treeModels, count: 70, minDist: 2.9f, samplePosition: () =>
        {
            float angle = rng.NextSingle() * MathF.Tau;
            float radius = rng.NextSingle() * InnerForestRadius;
            return new Vector2D<float>(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        });
    }

    private void SpawnTreeGroup(Random rng, List<ModelLoader.LoadedModel> treeModels, int count, float minDist, Func<Vector2D<float>> samplePosition)
    {
        float minDistSq = minDist * minDist;
        int placed = 0;
        int attempts = 0;
        while (placed < count && attempts < count * 30)
        {
            attempts++;
            var pos = samplePosition();
            if (!IsFarEnough(pos, minDistSq))
                continue;

            float scale = 0.009f + rng.NextSingle() * 0.003f;
            float yaw = rng.NextSingle() * MathF.Tau;
            var model = treeModels[rng.Next(treeModels.Count)];
            AddProp(model, MakeTRS(pos.X, 0f, pos.Y, yaw, scale));
            _treeColliders.Add((pos, TreeCollisionRadius));
            placed++;
        }
    }

    private bool IsFarEnough(Vector2D<float> candidate, float minDistSq)
    {
        foreach (var collider in _treeColliders)
        {
            Vector2D<float> delta = candidate - collider.Position;
            if (delta.LengthSquared < minDistSq) return false;
        }
        return true;
    }

    private void AddProp(ModelLoader.LoadedModel model, Matrix4X4<float> transform)
    {
        if (!_propsByModel.TryGetValue(model, out var transforms))
        {
            transforms = new List<Matrix4X4<float>>();
            _propsByModel[model] = transforms;
        }
        transforms.Add(transform);
    }

    /// <summary>
    /// Returns file paths for models named prefix1.fbx, prefix2.fbx, … that actually exist.
    /// </summary>
    private static List<string> DiscoverModels(string dir, string prefix)
    {
        var found = new List<string>();
        if (!System.IO.Directory.Exists(dir)) return found;

        // Match any file starting with the prefix and ending in .fbx
        var files = System.IO.Directory.GetFiles(dir, $"{prefix}*.fbx");
        System.Array.Sort(files); // consistent ordering
        found.AddRange(files);

        if (found.Count == 0)
            Console.Error.WriteLine($"[Scene] No models found for prefix '{prefix}' in {dir}");
        else
            foreach (var f in found)
                Console.WriteLine($"[Scene] Discovered: {System.IO.Path.GetFileName(f)}");

        return found;
    }

    /// <summary>
    /// Selects up to <paramref name="maxCount"/> random unique file paths.
    /// </summary>
    private static List<string> ChooseRandomSubset(List<string> source, int maxCount, Random rng)
    {
        if (source.Count <= maxCount) return source;

        var shuffled = new List<string>(source);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled.GetRange(0, maxCount);
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
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, System.IO.Path.Combine(parts));
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

    }
}
