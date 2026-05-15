using Silk.NET.Maths;

namespace PSXGame;

public sealed class FenderEnemy
{
    private const float WalkSpeed = 3.8f;
    private const float SprintSpeed = 5.8f;
    private const float ChaseSpeed = 6.2f;
    private const float WanderRetargetMin = 2.2f;
    private const float WanderRetargetMax = 5.5f;

    private Vector2D<float> _moveDir = new(1f, 0f);
    private float _retargetTimer;

    public ModelLoader.LoadedModel Model { get; }
    public Vector2D<float> Position { get; private set; }
    public bool IsChasing { get; private set; }
    public bool IsSprinting { get; private set; }
    public float Yaw { get; private set; }
    public Matrix4X4<float> Transform { get; private set; }

    public FenderEnemy(ModelLoader.LoadedModel model, Vector2D<float> start)
    {
        Model = model;
        Position = start;
        RebuildTransform();
    }

    public void Update(float dt, Vector3D<float> playerPos, Random rng)
    {
        var player2 = new Vector2D<float>(playerPos.X, playerPos.Z);
        Vector2D<float> toPlayer = player2 - Position;
        float distToPlayer = toPlayer.Length;

        if (!IsChasing && distToPlayer < 1.35f)
            IsChasing = true;

        IsSprinting = !IsChasing && distToPlayer < 14f;

        if (IsChasing && distToPlayer > 0.001f)
        {
            _moveDir = toPlayer / distToPlayer;
        }
        else
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                float a = rng.NextSingle() * MathF.Tau;
                _moveDir = new Vector2D<float>(MathF.Cos(a), MathF.Sin(a));
                _retargetTimer = WanderRetargetMin + (rng.NextSingle() * (WanderRetargetMax - WanderRetargetMin));
            }
        }

        float speed = IsChasing ? ChaseSpeed : (IsSprinting ? SprintSpeed : WalkSpeed);
        Position += _moveDir * speed * dt;

        float edge = Scene.MapHalfExtent - 1.25f;
        if (Position.X < -edge || Position.X > edge)
        {
            Position = new Vector2D<float>(Math.Clamp(Position.X, -edge, edge), Position.Y);
            _moveDir = new Vector2D<float>(-_moveDir.X, _moveDir.Y);
        }
        if (Position.Y < -edge || Position.Y > edge)
        {
            Position = new Vector2D<float>(Position.X, Math.Clamp(Position.Y, -edge, edge));
            _moveDir = new Vector2D<float>(_moveDir.X, -_moveDir.Y);
        }

        Yaw = MathF.Atan2(_moveDir.X, _moveDir.Y);
        RebuildTransform();
    }

    public float FootstepInterval => (IsChasing || IsSprinting) ? 0.24f : 0.34f;

    // Animation key mapping in the FBX:
    // - "injured walk" while walking
    // - "chase" while sprinting/chasing
    public string AnimationKey => (IsChasing || IsSprinting) ? "chase" : "injured walk";

    private void RebuildTransform()
    {
        Transform =
            Matrix4X4.CreateScale(0.01f) *
            Matrix4X4.CreateRotationY(Yaw) *
            Matrix4X4.CreateTranslation(new Vector3D<float>(Position.X, 0f, Position.Y));
    }
}
