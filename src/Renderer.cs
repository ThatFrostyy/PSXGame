using System;
using System.Collections.Generic;
using System.IO;
using Silk.NET.OpenGL;
using Silk.NET.Maths;
using StbImageSharp;

namespace PSXGame;

public class Renderer : IDisposable
{
    private readonly GL _gl;
    private Vector2D<int> _screenSize;

    // PSX internal resolution — rendered here, then upscaled
    private const int PsxW = 320;
    private const int PsxH = 240;

    // Offscreen FBO for PSX-res 3D scene
    private uint _fbo;
    private uint _fboColorTex;
    private uint _fboDepthRbo;

    private readonly ShaderProgram _planeShader;
    private readonly ShaderProgram _propShader;
    private readonly ShaderProgram _batteryShader;
    private readonly ShaderProgram _upscaleShader;

    private readonly uint _batteryTexture;
    private readonly uint _groundTexture;
    private readonly uint _wallTexture;
    private readonly uint _whiteTex;

    // Full-screen quad for upscale pass
    private readonly uint _quadVao;
    private readonly uint _quadVbo;

    // HUD quad (drawn at full res on top of upscaled image)
    private readonly uint _hudVao;
    private readonly uint _hudVbo;
    private readonly uint _instanceVbo;
    private readonly HashSet<Mesh> _configuredInstanceMeshes = new();

    private const int InstancingFallbackThreshold = 2;

    public Renderer(GL gl, Vector2D<int> screenSize)
    {
        _gl = gl;
        _screenSize = screenSize;

        _planeShader   = CreateShaderProgram(gl, "plane.vert.glsl", "plane.frag.glsl");
        _propShader    = CreateShaderProgram(gl, "prop.vert.glsl", "prop.frag.glsl");
        _batteryShader = CreateShaderProgram(gl, "hud.vert.glsl", "hud.frag.glsl");
        _upscaleShader = CreateShaderProgram(gl, "upscale.vert.glsl", "upscale.frag.glsl");

        _batteryTexture = LoadBatteryTexture();
        _groundTexture = LoadGroundTexture();
        _wallTexture = LoadTextureFromPath(repeat: true, "grass", "grass06.png");
        _whiteTex       = MakeWhiteTexture();

        _planeShader.Use();
        _planeShader.SetInt("uGroundTex", 0);

        _propShader.Use();
        _propShader.SetInt("uDiffuse", 0);
        _propShader.SetInt("uUseInstancing", 0);

        _batteryShader.Use();
        _batteryShader.SetInt("uBatteryTex", 0);

        _upscaleShader.Use();
        _upscaleShader.SetInt("uScene", 0);
        _upscaleShader.SetFloat("uMapHalfExtent", Scene.MapHalfExtent);
        _upscaleShader.SetFloat("uEdgeForestBand", Scene.EdgeForestBand);

        CreateFbo();
        CreateQuad(out _quadVao, out _quadVbo);
        CreateHudQuad(out _hudVao, out _hudVbo);

        _instanceVbo = _gl.GenBuffer();
    }

    // -------------------------------------------------------------------------
    // FBO
    // -------------------------------------------------------------------------
    private void CreateFbo()
    {
        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        // Color texture — PSX res, nearest filter so it stays blocky when upscaled
        _fboColorTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fboColorTex);
        unsafe { _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgb8, PsxW, PsxH, 0, PixelFormat.Rgb, PixelType.UnsignedByte, null); }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _fboColorTex, 0);

        // Depth renderbuffer
        _fboDepthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _fboDepthRbo);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, PsxW, PsxH);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _fboDepthRbo);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new Exception("PSX FBO is not complete!");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void DestroyFbo()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_fboColorTex);
        _gl.DeleteRenderbuffer(_fboDepthRbo);
    }

    // -------------------------------------------------------------------------
    // Quad helpers
    // -------------------------------------------------------------------------
    private void CreateQuad(out uint vao, out uint vbo)
    {
        // Full-screen quad: pos(2) + uv(2)
        float[] verts = [
            -1f, -1f,  0f, 0f,
             1f, -1f,  1f, 0f,
             1f,  1f,  1f, 1f,
            -1f, -1f,  0f, 0f,
             1f,  1f,  1f, 1f,
            -1f,  1f,  0f, 1f,
        ];
        vao = _gl.GenVertexArray();
        vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* p = verts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
    }

    private void CreateHudQuad(out uint vao, out uint vbo)
    {
        float[] hudVerts = [
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        ];
        vao = _gl.GenVertexArray();
        vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* p = hudVerts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(hudVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
    }

    // -------------------------------------------------------------------------
    // Render
    // -------------------------------------------------------------------------
    public void Render(Scene scene, Camera cam)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(cam);

        // ---- PASS 1: render scene into PSX-res FBO ---------------------------
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, PsxW, PsxH);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);

        float aspect = (float)PsxW / PsxH;
        var view = cam.GetViewMatrix();
        var proj = cam.GetProjectionMatrix(aspect);

        // Skybox
        scene.Skybox.Draw(view, proj);

        // Ground plane
        _gl.Disable(EnableCap.CullFace);
        _planeShader.Use();
        _planeShader.SetMatrix4("uView",       view);
        _planeShader.SetMatrix4("uProjection", proj);
        _planeShader.SetMatrix4("uModel",      Matrix4X4<float>.Identity);
        _planeShader.SetVec3("uCamPos",  cam.Position);
        _planeShader.SetVec3("uCamDir",  cam.Front);
        Vector3D<float> lightPos = cam.Position
            + (cam.Front * 0.45f) - (cam.Up * 0.35f) + (cam.Right * 0.16f);
        _planeShader.SetVec3("uLightPos", lightPos);
        float flashlight = cam.FlashlightOn ? cam.FlashlightIntensity : 0f;
        _planeShader.SetFloat("uFlashlightOn", flashlight);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _groundTexture);
        _planeShader.SetVector2("uResolution", new Vector2D<float>(PsxW, PsxH));
        scene.PlaneMesh.Draw();

        // Props
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _propShader.Use();
        _propShader.SetMatrix4("uView",       view);
        _propShader.SetMatrix4("uProjection", proj);
        _propShader.SetVec3("uCamPos",    cam.Position);
        _propShader.SetVec3("uCamDir",    cam.Front);
        _propShader.SetVec3("uLightPos",  lightPos);
        _propShader.SetFloat("uFlashlightOn", flashlight);
        _propShader.SetVector2("uResolution", new Vector2D<float>(PsxW, PsxH));

        // Perimeter walls (tile billboard, open top for skybox visibility)
        _propShader.SetMatrix4("uModel", Matrix4X4<float>.Identity);
        _propShader.SetInt("uUseInstancing", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _wallTexture);
        scene.WallMesh.Draw();

        _gl.ActiveTexture(TextureUnit.Texture0);
        foreach (var (model, transforms) in scene.PropsByModel)
        {
            if (transforms.Count == 0)
                continue;

            bool useInstancing = transforms.Count > InstancingFallbackThreshold;

            if (useInstancing)
            {
                UploadInstanceTransforms(transforms);
            }

            foreach (var (mesh, tex) in model.Parts)
            {
                _gl.BindTexture(TextureTarget.Texture2D, tex != 0 ? tex : _whiteTex);
                if (useInstancing)
                {
                    _propShader.SetInt("uUseInstancing", 1);
                    if (_configuredInstanceMeshes.Add(mesh))
                    {
                        mesh.ConfigureInstanceMatrixAttributes(_instanceVbo);
                    }
                    mesh.DrawInstanced((uint)transforms.Count);
                }
                else
                {
                    _propShader.SetInt("uUseInstancing", 0);
                    foreach (var transform in transforms)
                    {
                        _propShader.SetMatrix4("uModel", transform);
                        mesh.Draw();
                    }
                }
            }
        }


        // Fender enemy (single dynamic instance)
        _propShader.SetInt("uUseInstancing", 0);
        foreach (var (mesh, tex) in scene.Fender.Model.Parts)
        {
            _gl.BindTexture(TextureTarget.Texture2D, tex != 0 ? tex : _whiteTex);
            _propShader.SetMatrix4("uModel", scene.Fender.Transform);
            mesh.Draw();
        }
        _gl.Enable(EnableCap.CullFace);

        // ---- PASS 2: HUD drawn into PSX-res buffer (so post FX affect UI) ---
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        float hudAspect = (float)PsxW / PsxH;
        _batteryShader.Use();
        _batteryShader.SetFloat("uBatteryLevel", cam.BatteryLevel);
        _batteryShader.SetFloat("uAspectRatio", hudAspect);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _batteryTexture);
        _gl.BindVertexArray(_hudVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);

        // ---- PASS 3: upscale FBO → screen with PSX post-processing -----------
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_screenSize.X, (uint)_screenSize.Y);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        _upscaleShader.Use();
        _upscaleShader.SetVector2("uScreenSize", new Vector2D<float>(_screenSize.X, _screenSize.Y));
        _upscaleShader.SetVector2("uPsxSize",    new Vector2D<float>(PsxW, PsxH));
        _upscaleShader.SetVector2("uCameraXZ", new Vector2D<float>(cam.Position.X, cam.Position.Z));
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _fboColorTex);
        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Resize(Vector2D<int> newSize)
    {
        _screenSize = newSize;
        // FBO stays fixed at PSX res — no need to recreate
    }

    public void Dispose()
    {
        _configuredInstanceMeshes.Clear();
        DestroyFbo();
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteBuffer(_hudVbo);
        _gl.DeleteVertexArray(_hudVao);
        _gl.DeleteBuffer(_instanceVbo);
        _gl.DeleteTexture(_batteryTexture);
        _gl.DeleteTexture(_groundTexture);
        _gl.DeleteTexture(_wallTexture);
        _gl.DeleteTexture(_whiteTex);
        _upscaleShader.Dispose();
        _batteryShader.Dispose();
        _propShader.Dispose();
        _planeShader.Dispose();
    }

    // -------------------------------------------------------------------------
    // Texture helpers
    // -------------------------------------------------------------------------
    private uint LoadBatteryTexture()
    {
        return LoadTextureFromPath(repeat: false, "battery.png");
    }

    private uint MakeWhiteTexture()
    {
        byte[] white = [255, 255, 255, 255];
        return UploadTexture(1, 1, white, repeat: false);
    }

    private uint LoadGroundTexture()
    {
        return LoadTextureFromPath(repeat: true, "grass", "grass01.png");
    }

    private uint LoadTextureFromPath(bool repeat, params string[] relativePathSegments)
    {
        string relativePath = Path.Combine(relativePathSegments);
        string path = Path.Combine(AppContext.BaseDirectory, "src", "textures", relativePath);
        if (!File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "src", "textures", relativePath);

        using var fs = File.OpenRead(path);
        var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        return UploadTexture((uint)img.Width, (uint)img.Height, img.Data, repeat);
    }

    private uint UploadTexture(uint w, uint h, byte[] data, bool repeat)
    {
        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = data)
                _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8, w, h, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        int wrap = repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        return tex;
    }

    private void UploadInstanceTransforms(List<Matrix4X4<float>> transforms)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        unsafe
        {
            fixed (Matrix4X4<float>* ptr = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(transforms))
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(transforms.Count * sizeof(float) * 16),
                    ptr,
                    BufferUsageARB.StreamDraw);
            }
        }
    }

    private ShaderProgram CreateShaderProgram(GL gl, string vertexShaderFile, string fragmentShaderFile)
    {
        string vertPath = ResolveShaderPath(vertexShaderFile);
        string fragPath = ResolveShaderPath(fragmentShaderFile);
        try
        {
            string vertSource = File.ReadAllText(vertPath);
            string fragSource = File.ReadAllText(fragPath);
            return new ShaderProgram(gl, vertSource, fragSource);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load or compile shaders '{vertexShaderFile}' and '{fragmentShaderFile}' ({vertPath}, {fragPath}): {ex.Message}", ex);
        }
    }

    private string ResolveShaderPath(string shaderFile)
    {
        string baseDirPath = Path.Combine(AppContext.BaseDirectory, "src", "shaders", shaderFile);
        if (File.Exists(baseDirPath))
            return baseDirPath;

        string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "shaders", shaderFile);
        if (File.Exists(cwdPath))
            return cwdPath;

        throw new FileNotFoundException($"Shader file not found: {shaderFile}. Tried '{baseDirPath}' and '{cwdPath}'.");
    }
}
