using System;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace PSXGame;

public sealed class PlayerController
{
    private const float BatteryLifeSeconds = 300f;
    private const float MoveSpeed = 5f;
    private const float MouseSensitivity = 0.12f;
    private const float FootstepInterval = 0.47f;
    private const float LowBatteryThreshold = 0.2f;
    private const float PlayerRadius = 0.5f;
    private float _batterySeconds = BatteryLifeSeconds;
    private float _footstepTimer;

    public void Update(
        Camera camera,
        IKeyboard keyboard,
        float mouseDeltaX,
        float mouseDeltaY,
        float dt,
        Scene scene,
        Random rng,
        Action<Random>? playFootstep,
        Action<bool>? setFlashlightFlickerLoop)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(scene);

        camera.Yaw += mouseDeltaX * MouseSensitivity;
        camera.Pitch -= mouseDeltaY * MouseSensitivity;
        camera.Pitch = Math.Clamp(camera.Pitch, -89f, 89f);
        camera.UpdateVectors();

        float moveForward = 0f;
        float moveRight = 0f;
        if (keyboard.IsKeyPressed(Key.W)) moveForward += 1f;
        if (keyboard.IsKeyPressed(Key.S)) moveForward -= 1f;
        if (keyboard.IsKeyPressed(Key.D)) moveRight += 1f;
        if (keyboard.IsKeyPressed(Key.A)) moveRight -= 1f;

        if (moveForward != 0f || moveRight != 0f)
        {
            float invLen = 1f / MathF.Sqrt((moveForward * moveForward) + (moveRight * moveRight));
            moveForward *= invLen;
            moveRight *= invLen;
            Vector3D<float> before = camera.Position;
            camera.MoveForward(moveForward * MoveSpeed * dt);
            camera.MoveRight(moveRight * MoveSpeed * dt);
            ResolveWorldCollisions(camera, before, scene);

            _footstepTimer -= dt;
            if (_footstepTimer <= 0f)
            {
                playFootstep?.Invoke(rng);
                _footstepTimer = FootstepInterval;
            }
        }
        else
        {
            _footstepTimer = 0f;
        }

        if (camera.FlashlightOn)
        {
            _batterySeconds = MathF.Max(0f, _batterySeconds - dt);
            if (_batterySeconds <= 0f)
            {
                camera.FlashlightOn = false;
            }
        }

        float batteryLevel = _batterySeconds / BatteryLifeSeconds;
        bool shouldFlicker = camera.FlashlightOn && batteryLevel < LowBatteryThreshold;
        setFlashlightFlickerLoop?.Invoke(shouldFlicker);
        bool flickerOff = shouldFlicker && rng.NextSingle() < (0.12f + (LowBatteryThreshold - batteryLevel) * 1.8f);
        camera.FlashlightIntensity = flickerOff ? 0f : 1f;
        camera.BatteryLevel = batteryLevel;
    }

    private static void ResolveWorldCollisions(Camera camera, Vector3D<float> fallbackPos, Scene scene)
    {
        float edge = Scene.MapHalfExtent - PlayerRadius;
        var clamped = camera.Position;
        clamped.X = Math.Clamp(clamped.X, -edge, edge);
        clamped.Z = Math.Clamp(clamped.Z, -edge, edge);
        camera.Position = clamped;

        var playerPos2D = new Vector2D<float>(camera.Position.X, camera.Position.Z);
        foreach (var (treePos, radius) in scene.TreeColliders)
        {
            float overlap = radius + PlayerRadius;
            Vector2D<float> delta = playerPos2D - treePos;
            float distSq = delta.LengthSquared;
            if (distSq >= overlap * overlap || distSq <= 0.0001f)
                continue;

            float dist = MathF.Sqrt(distSq);
            Vector2D<float> pushDir = delta / dist;
            playerPos2D = treePos + (pushDir * overlap);
        }

        camera.Position = new Vector3D<float>(playerPos2D.X, fallbackPos.Y, playerPos2D.Y);
    }

    public bool TryToggleFlashlight(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_batterySeconds <= 0f)
        {
            return false;
        }

        camera.FlashlightOn = !camera.FlashlightOn;
        return true;
    }
}
