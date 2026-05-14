using System;
using Silk.NET.Maths;

namespace PSXGame;

public class Camera
{
    public Vector3D<float> Position;
    public Vector3D<float> Front;
    public Vector3D<float> Up;
    public Vector3D<float> Right;

    public float Yaw = -90f;
    public float Pitch = 0f;
    public float Fov = 73f; 
    public bool FlashlightOn = true;
    public float FlashlightIntensity = 1f;
    public float BatteryLevel = 1f;

    private static readonly Vector3D<float> WorldUp = new(0, 1, 0);

    public Camera(Vector3D<float> startPos)
    {
        Position = startPos;
        Front = new Vector3D<float>(0, 0, -1);
        Up = WorldUp;
        Right = new Vector3D<float>(1, 0, 0);
        UpdateVectors();
    }

    public void UpdateVectors()
    {
        float yawRad = ToRad(Yaw);
        float pitchRad = ToRad(Pitch);

        Front = Vector3D.Normalize(new Vector3D<float>(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)
        ));

        Right = Vector3D.Normalize(Vector3D.Cross(Front, WorldUp));
        Up = Vector3D.Normalize(Vector3D.Cross(Right, Front));
    }

    public void MoveForward(float amount)
    {
        // Lock Y so we walk on the floor (classic FPS behavior)
        var flatFront = Vector3D.Normalize(new Vector3D<float>(Front.X, 0, Front.Z));
        Position += flatFront * amount;
    }

    public void MoveRight(float amount)
    {
        Position += Right * amount;
    }

    public Matrix4X4<float> GetViewMatrix()
    {
        return Matrix4X4.CreateLookAt(Position, Position + Front, Up);
    }

    public Matrix4X4<float> GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4X4.CreatePerspectiveFieldOfView(ToRad(Fov), aspectRatio, 0.05f, 100f);
    }

    private static float ToRad(float deg) => deg * MathF.PI / 180f;
}
