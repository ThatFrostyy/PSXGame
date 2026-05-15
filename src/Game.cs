using System;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

namespace PSXGame;

public class Game
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private Renderer _renderer = null!;
    private Camera _camera = null!;
    private Scene _scene = null!;
    private IKeyboard _keyboard = null!;
    private IMouse _mouse = null!;
    private AmbientAudio _ambient = null!;
    private bool _firstMove = true;
    private Vector2D<float> _lastMousePos;
    private readonly Random _rng = new(42);
    private readonly PlayerController _playerController = new();

    public void Run()
    {
        var options = WindowOptions.Default with
        {
            Title = "Night Walk",
            Size = new Vector2D<int>(960, 720),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
                ContextFlags.Default, new APIVersion(3, 3)),
            VSync = true,
        };
        _window = Window.Create(options);
        _window.Load    += OnLoad;
        _window.Update  += OnUpdate;
        _window.Render  += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClose;
        _window.Run();
    }

    private void OnLoad()
    {
        try
        {
            _gl    = _window.CreateOpenGL();
            _input = _window.CreateInput();
            _keyboard = _input.Keyboards[0];
            _mouse    = _input.Mice[0];
            _mouse.Cursor.CursorMode = CursorMode.Raw;
            _camera   = new Camera(new Vector3D<float>(0f, 1.7f, 0f));
            _scene    = new Scene(_gl);
            _renderer = new Renderer(_gl, _window.Size);

            _keyboard.KeyDown += (_, key, _) =>
            {
                if (key == Key.F)
                {
                    if (_camera != null && _playerController.TryToggleFlashlight(_camera))
                    {
                        _ambient?.PlayFlashlightClick();
                    }
                }
            };

            try
            {
                _ambient = new AmbientAudio();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AmbientAudio failed: {ex.Message}. Continuing without audio.");
                _ambient = null!;
            }

            _gl.Enable(EnableCap.DepthTest);
            Console.WriteLine("WASD = move | Mouse = look | ESC = quit");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("=== FATAL ERROR IN OnLoad ===");
            Console.Error.WriteLine(ex.ToString());
            Console.Error.WriteLine("==============================");
            _window.Close();
        }
    }

    private void OnUpdate(double delta)
    {
        if (_camera == null || _keyboard == null || _mouse == null)
            return;

        float dt = (float)delta;
        if (_keyboard.IsKeyPressed(Key.Escape)) _window.Close();

        var mp = new Vector2D<float>(_mouse.Position.X, _mouse.Position.Y);
        if (_firstMove) { _lastMousePos = mp; _firstMove = false; }
        float dx = mp.X - _lastMousePos.X;
        float dy = mp.Y - _lastMousePos.Y;
        _lastMousePos = mp;

        _playerController.Update(
            _camera,
            _keyboard,
            dx,
            dy,
            dt,
            _rng,
            rng => _ambient?.PlayDirtFootstep(rng),
            enabled => _ambient?.SetFlashlightFlickerLoop(enabled));
    }

    private void OnResize(Vector2D<int> size)
    {
        _renderer.Resize(size);
    }

    private void OnRender(double delta)
    {
        _renderer.Render(_scene, _camera);
    }

    private void OnClose()
    {
        _renderer.Dispose();
        _scene.Dispose();
        _ambient?.Dispose();
        _input.Dispose();
    }
}
