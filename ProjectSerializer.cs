using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace SmoothieEngine;

public sealed class LoadedProject
{
    public List<Entity> Entities { get; init; } = new();
    public Vector2 CameraTarget { get; init; }
    public float CameraZoom { get; init; } = 1f;
    public VsGraphDto? Vs { get; init; }
}

public static class ProjectSerializer
{
    public const string Extension = ".smoothieproject";
    const int CurrentVersion = 6;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    sealed class ProjectDto
    {
        public int Version { get; set; } = CurrentVersion;
        public float[] CameraTarget { get; set; } = { 0f, 0f };
        public float CameraZoom { get; set; } = 1f;
        public List<EntityDto> Entities { get; set; } = new();
        public VsGraphDto? Vs { get; set; }
    }

    sealed class EntityDto
    {
        public string Name { get; set; } = "Object";
        public string Shape { get; set; } = nameof(ShapeType.Square);
        public bool Active { get; set; } = true;
        public int Parent { get; set; } = -1;
        public float[] Position { get; set; } = { 0f, 0f };
        public float Rotation { get; set; }
        public float[] Scale { get; set; } = { 1f, 1f };
        public byte[] Color { get; set; } = { 255, 255, 255, 255 };
        public PhysicsDto? Physics { get; set; }
        public ColliderDto? Collider { get; set; }
        public CameraDto? Camera { get; set; }
        public string? VisualScriptName { get; set; }
    }

    sealed class PhysicsDto
    {
        public string Type { get; set; } = nameof(BodyType.Dynamic);
        public float GravityScale { get; set; } = 1f;
        public float Mass { get; set; } = 1f;
        public float Restitution { get; set; }
        public float Friction { get; set; } = 0.4f;
        public float[] Velocity { get; set; } = { 0f, 0f };
        public float AngularDamping { get; set; } = 0.1f;
        public bool LockRotation { get; set; }
    }

    sealed class ColliderDto
    {
        public string Shape { get; set; } = nameof(ColliderShape.Box);
        public float[] Size { get; set; } = { 1f, 1f };
        public bool IsTrigger { get; set; }
    }

    sealed class CameraDto
    {
        public float Zoom { get; set; } = 1f;
    }

    public static void Save(string path, IEnumerable<Entity> entities, Vector2 cameraTarget, float cameraZoom, VsGraphDto? vs = null)
    {
        List<Entity> list = entities.ToList();
        var index = new Dictionary<Entity, int>();
        for (int i = 0; i < list.Count; i++) index[list[i]] = i;

        var dto = new ProjectDto
        {
            CameraTarget = new[] { cameraTarget.X, cameraTarget.Y },
            CameraZoom = cameraZoom,
            Entities = list
                .Select(e => ToDto(e, e.Parent != null && index.TryGetValue(e.Parent, out int p) ? p : -1))
                .ToList(),
            Vs = vs
        };

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(dto, Options);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static bool TryLoad(string path, out LoadedProject? project, out string error)
    {
        project = null;
        error = "";

        if (!File.Exists(path))
        {
            error = $"File not found: {path}";
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            ProjectDto? dto = JsonSerializer.Deserialize<ProjectDto>(json, Options);
            if (dto == null)
            {
                error = "File is empty or invalid.";
                return false;
            }

            if (dto.Version > CurrentVersion)
            {
                error = $"Project version {dto.Version} is newer than this engine supports ({CurrentVersion}).";
                return false;
            }

            List<Entity> entities = dto.Entities.Select(FromDto).ToList();
            for (int i = 0; i < entities.Count; i++)
            {
                int p = dto.Entities[i].Parent;
                if (p >= 0 && p < entities.Count && p != i)
                    entities[i].SetParent(entities[p]);
            }

            project = new LoadedProject
            {
                Entities = entities,
                CameraTarget = ToVector(dto.CameraTarget, Vector2.Zero),
                CameraZoom = Math.Clamp(dto.CameraZoom, 0.1f, 8f),
                Vs = dto.Vs
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    static EntityDto ToDto(Entity e, int parentIndex) => new()
    {
        Name = e.Name,
        Shape = e.Shape.ToString(),
        Active = e.Active,
        Parent = parentIndex,
        Position = new[] { e.Position.X, e.Position.Y },
        Rotation = e.Rotation,
        Scale = new[] { e.Scale.X, e.Scale.Y },
        Color = new[] { e.Color.R, e.Color.G, e.Color.B, e.Color.A },
        VisualScriptName = e.VisualScript?.Name,
        Camera = e.Camera == null ? null : new CameraDto { Zoom = e.Camera.Zoom },
        Physics = e.Physics == null ? null : new PhysicsDto
        {
            Type = e.Physics.Type.ToString(),
            GravityScale = e.Physics.GravityScale,
            Mass = e.Physics.Mass,
            Restitution = e.Physics.Restitution,
            Friction = e.Physics.Friction,
            Velocity = new[] { e.Physics.Velocity.X, e.Physics.Velocity.Y },
            AngularDamping = e.Physics.AngularDamping,
            LockRotation = e.Physics.LockRotation
        },
        Collider = e.Collider == null ? null : new ColliderDto
        {
            Shape = e.Collider.Shape.ToString(),
            Size = new[] { e.Collider.Size.X, e.Collider.Size.Y },
            IsTrigger = e.Collider.IsTrigger
        }
    };

    static Entity FromDto(EntityDto d)
    {
        Vector2 scale = ToVector(d.Scale, Vector2.One);
        scale = new Vector2(Math.Clamp(scale.X, 0.05f, 100f), Math.Clamp(scale.Y, 0.05f, 100f));

        byte[] c = d.Color is { Length: >= 3 } ? d.Color : new byte[] { 255, 255, 255, 255 };

        return new Entity
        {
            Name = string.IsNullOrWhiteSpace(d.Name) ? "Object" : d.Name,
            Shape = Enum.TryParse(d.Shape, true, out ShapeType s) ? s : ShapeType.Square,
            Active = d.Active,
            Position = ToVector(d.Position, Vector2.Zero),
            Rotation = d.Rotation,
            Scale = scale,
            Color = new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : (byte)255),
            VisualScript = d.VisualScriptName == null ? null : new VisualScript { Name = d.VisualScriptName },
            Camera = d.Camera == null ? null : new CameraComponent
            {
                Zoom = Math.Clamp(d.Camera.Zoom, 0.1f, 8f)
            },
            Physics = d.Physics == null ? null : new PhysicsBody
            {
                Type = Enum.TryParse(d.Physics.Type, true, out BodyType bodyType) ? bodyType : BodyType.Dynamic,
                GravityScale = Math.Clamp(d.Physics.GravityScale, 0f, 10f),
                Mass = Math.Clamp(d.Physics.Mass, 0.01f, 1000f),
                Restitution = Math.Clamp(d.Physics.Restitution, 0f, 1f),
                Friction = Math.Clamp(d.Physics.Friction, 0f, 1f),
                Velocity = ToVector(d.Physics.Velocity, Vector2.Zero),
                AngularDamping = Math.Clamp(d.Physics.AngularDamping, 0f, 20f),
                LockRotation = d.Physics.LockRotation
            },
            Collider = d.Collider == null ? null : new Collider
            {
                Shape = Enum.TryParse(d.Collider.Shape, true, out ColliderShape colliderShape)
                    ? colliderShape
                    : ColliderShape.Box,
                Size = ClampColliderSize(ToVector(d.Collider.Size, Vector2.One)),
                IsTrigger = d.Collider.IsTrigger
            }
        };
    }

    static Vector2 ClampColliderSize(Vector2 size) =>
        new(Math.Clamp(size.X, 0.05f, 20f), Math.Clamp(size.Y, 0.05f, 20f));

    static Vector2 ToVector(float[]? a, Vector2 fallback) =>
        a is { Length: >= 2 } ? new Vector2(a[0], a[1]) : fallback;
}