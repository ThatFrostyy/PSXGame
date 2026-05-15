using System;
using Silk.NET.Input;

namespace PSXGame;

public sealed class PlayerController
{
    private const float BatteryLifeSeconds = 300f;
    private const float MoveSpeed = 5f;
    private float _batterySeconds = BatteryLifeSeconds;
    private float _footstepTimer;

    public void Update(
        Camera camera,
        IKeyboard keyboard,
        float mouseDeltaX,
        float mouseDeltaY,
        float dt,
        Random rng,
        Action<Random>? playFootstep,
        Action<bool>? setFlashlightFlickerLoop)
    {
        camera.Yaw += mouseDeltaX * 0.12f;
        camera.Pitch -= mouseDeltaY * 0.12f;
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
            camera.MoveForward(moveForward * MoveSpeed * dt);
            camera.MoveRight(moveRight * MoveSpeed * dt);

            _footstepTimer -= dt;
            if (_footstepTimer <= 0f)
            {
                playFootstep?.Invoke(rng);
                _footstepTimer = 0.47f;
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
        bool shouldFlicker = camera.FlashlightOn && batteryLevel < 0.2f;
        setFlashlightFlickerLoop?.Invoke(shouldFlicker);
        bool flickerOff = shouldFlicker && rng.NextSingle() < (0.12f + (0.2f - batteryLevel) * 1.8f);
        camera.FlashlightIntensity = flickerOff ? 0f : 1f;
        camera.BatteryLevel = batteryLevel;
    }

    public bool TryToggleFlashlight(Camera camera)
    {
        if (_batterySeconds <= 0f)
        {
            return false;
        }

        camera.FlashlightOn = !camera.FlashlightOn;
        return true;
    }
}
