using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace SmoothieEngine;

public enum ShapeType { Square, Circle, Triangle, Hexagon }
public enum BodyType { Dynamic, Static }
public enum ColliderShape { Box, Circle, Triangle, Hexagon }

public sealed class PhysicsBody
{
    public BodyType Type = BodyType.Dynamic;
    public float GravityScale = 1f;
    public float Mass = 1f;
    public float Restitution;
    public float Friction = 0.4f;
    public Vector2 Velocity;

    public float AngularVelocity;
    public float AngularDamping = 0.1f;
    public bool LockRotation;

    public PhysicsBody Clone() => (PhysicsBody)MemberwiseClone();
}

public sealed class Collider
{
    public ColliderShape Shape = ColliderShape.Box;
    public Vector2 Size = Vector2.One;
    public bool IsTrigger;

    public Collider Clone() => (Collider)MemberwiseClone();
}

public sealed class CameraComponent
{
    public float Zoom = 1f;

    public CameraComponent Clone() => (CameraComponent)MemberwiseClone();
}

public sealed class Entity
{
    public const float BaseSize = 100f;

    public string Name = "Object";
    public ShapeType Shape;
    public bool Active = true;
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale = Vector2.One;
    public Color Color = Color.White;
    public PhysicsBody? Physics;
    public Collider? Collider;
    public CameraComponent? Camera;
    public VisualScript? VisualScript;
    List<ScriptInstance> _scripts = new();

    public List<ScriptInstance> Scripts
    {
        get => VisualScript != null ? VisualScript.Scripts : _scripts;
        set
        {
            if (VisualScript != null) VisualScript.Scripts = value;
            else _scripts = value;
        }
    }

    public Entity? Parent;
    public Vector2 LocalPosition;
    public float LocalRotation;
    public Vector2 LocalScale = Vector2.One;
    Vector2 _appliedPosition;
    float _appliedRotation;
    Vector2 _appliedScale = Vector2.One;

    public bool IsDescendantOf(Entity other)
    {
        int guard = 0;
        for (Entity? p = Parent; p != null && guard++ < 64; p = p.Parent)
            if (ReferenceEquals(p, other)) return true;
        return false;
    }

    public bool IsRelatedTo(Entity other) => IsDescendantOf(other) || other.IsDescendantOf(this);

    public int Depth
    {
        get
        {
            int d = 0;
            for (Entity? p = Parent; p != null && d < 64; p = p.Parent) d++;
            return d;
        }
    }

    public bool SetParent(Entity? parent)
    {
        if (parent != null && (ReferenceEquals(parent, this) || parent.IsDescendantOf(this))) return false;
        Parent = parent;
        if (parent != null) CaptureLocal();
        return true;
    }

    static Vector2 SafeScale(Vector2 s) =>
        new(MathF.Abs(s.X) < 0.0001f ? 1f : s.X, MathF.Abs(s.Y) < 0.0001f ? 1f : s.Y);

    void Remember()
    {
        _appliedPosition = Position;
        _appliedRotation = Rotation;
        _appliedScale = Scale;
    }

    void CaptureLocal()
    {
        Entity p = Parent!;
        Vector2 ps = SafeScale(p.Scale);
        LocalPosition = Rotate(Position - p.Position, -p.Rotation) / ps;
        LocalRotation = Rotation - p.Rotation;
        LocalScale = Scale / ps;
        Remember();
    }

    public void SyncToParent()
    {
        if (Parent == null) return;
        if (Position != _appliedPosition || Rotation != _appliedRotation || Scale != _appliedScale)
            CaptureLocal();

        Entity p = Parent;
        Vector2 ps = SafeScale(p.Scale);
        Position = p.Position + Rotate(LocalPosition * ps, p.Rotation);
        Rotation = p.Rotation + LocalRotation;
        Scale = LocalScale * ps;
        Remember();
    }

    public Entity Clone()
    {
        Entity clone = (Entity)MemberwiseClone();
        clone.Physics = Physics?.Clone();
        clone.Collider = Collider?.Clone();
        clone.Camera = Camera?.Clone();
        clone.VisualScript = VisualScript?.Clone();
        var graphCopies = new Dictionary<VsGraph, VsGraph>();
        clone._scripts = clone.VisualScript?.Scripts ?? Scripts.Select(s => s.Clone(graphCopies)).ToList();
        return clone;
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * MathF.PI / 180f;
        float c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public bool Contains(Vector2 world)
    {
        Vector2 l = Rotate(world - Position, -Rotation);
        Vector2 h = Scale * BaseSize * 0.5f;
        float nx = l.X / h.X, ny = l.Y / h.Y;

        return Shape switch
        {
            ShapeType.Square   => MathF.Abs(nx) <= 1f && MathF.Abs(ny) <= 1f,
            ShapeType.Triangle => ny >= -1f && ny <= 1f && MathF.Abs(nx) <= (ny + 1f) * 0.5f,
            ShapeType.Hexagon  => MathF.Abs(nx) <= 1f && MathF.Abs(ny) <= MathF.Sqrt(3f) * 0.5f
                                  && MathF.Sqrt(3f) * MathF.Abs(nx) + MathF.Abs(ny) <= MathF.Sqrt(3f),
            _                  => nx * nx + ny * ny <= 1f
        };
    }

    public Vector2[] Corners()
    {
        Vector2 h = Scale * BaseSize * 0.5f;
        Vector2[] c =
        {
            new(-h.X, -h.Y), new(h.X, -h.Y), new(h.X, h.Y), new(-h.X, h.Y)
        };
        for (int i = 0; i < 4; i++) c[i] = Position + Rotate(c[i], Rotation);
        return c;
    }

    public void Draw(float alpha = 1f)
    {
        Color col = alpha >= 1f
            ? Color
            : new Color(Color.R, Color.G, Color.B, (byte)(Color.A * alpha));

        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, Position.Y, 0f);
        Rlgl.Rotatef(Rotation, 0f, 0f, 1f);
        Rlgl.Scalef(Scale.X, Scale.Y, 1f);

        float h = BaseSize * 0.5f;
        switch (Shape)
        {
            case ShapeType.Square:
                Raylib.DrawRectangleRec(new Rectangle(-h, -h, BaseSize, BaseSize), col);
                break;
            case ShapeType.Circle:
                Raylib.DrawPoly(Vector2.Zero, 64, h, 0f, col);
                break;
            case ShapeType.Triangle:
                Raylib.DrawTriangle(new Vector2(0, -h), new Vector2(-h, h), new Vector2(h, h), col);
                break;
            case ShapeType.Hexagon:
                Raylib.DrawPoly(Vector2.Zero, 6, h, 0f, col);
                break;
        }

        Rlgl.PopMatrix();
    }
}

internal static class PhysicsSimulation
{
    const float Step = 1f / 60f;
    const float Gravity = 980f;
    const int VelocityIterations = 10;
    const int PositionIterations = 3;
    const float PositionSlop = 0.5f;
    const float PositionCorrection = 0.4f;
    const float RestitutionThreshold = 20f;
    const float RadToDeg = 180f / MathF.PI;

    static float _accumulator;
    static readonly List<Entity> Bodies = new();
    static readonly List<Contact> Contacts = new();

    struct Manifold
    {
        public Vector2 Normal;
        public int Count;
        public Vector2 P0, P1;
        public float D0, D1;

        public float Depth => Count > 1 ? MathF.Max(D0, D1) : D0;

        public void Add(Vector2 p, float d)
        {
            if (Count == 0) { P0 = p; D0 = d; }
            else            { P1 = p; D1 = d; }
            Count++;
        }
    }

    sealed class Contact
    {
        public Entity A = null!, B = null!;
        public Vector2 Normal, Tangent, RA, RB;
        public float InvMassA, InvMassB, InvIA, InvIB;
        public float NormalMass, TangentMass;
        public float Bias, Friction;
        public float Pn, Pt;
    }

    public static void Reset() => _accumulator = 0f;

    public static void Update(IReadOnlyList<Entity> entities, float deltaTime)
    {
        _accumulator = MathF.Min(_accumulator + deltaTime, Step * 8f);
        while (_accumulator >= Step)
        {
            SimulateStep(entities, Step);
            _accumulator -= Step;
        }
    }

    static void SimulateStep(IReadOnlyList<Entity> entities, float dt)
    {
        Bodies.Clear();
        foreach (Entity e in entities)
            if (e.Active && e.Collider != null) Bodies.Add(e);

        foreach (Entity e in entities)
        {
            if (!e.Active || e.Parent != null || e.Physics is not { Type: BodyType.Dynamic } body) continue;
            body.Velocity += new Vector2(0f, Gravity * body.GravityScale) * dt;
            if (body.LockRotation) body.AngularVelocity = 0f;
            else body.AngularVelocity *= 1f / (1f + dt * MathF.Max(body.AngularDamping, 0f));
        }

        BuildContacts();

        for (int it = 0; it < VelocityIterations; it++)
            foreach (Contact c in Contacts)
                SolveContact(c);

        foreach (Entity e in entities)
        {
            if (!e.Active || e.Parent != null || e.Physics is not { Type: BodyType.Dynamic } body) continue;
            e.Position += body.Velocity * dt;
            if (!body.LockRotation)
                e.Rotation += body.AngularVelocity * RadToDeg * dt;
        }

        for (int it = 0; it < PositionIterations; it++)
            CorrectPositions();
    }

    static void BuildContacts()
    {
        Contacts.Clear();

        for (int i = 0; i < Bodies.Count; i++)
        {
            Entity a = Bodies[i];
            for (int j = i + 1; j < Bodies.Count; j++)
            {
                Entity b = Bodies[j];
                if (a.Collider!.IsTrigger || b.Collider!.IsTrigger) continue;
                if (a.IsRelatedTo(b)) continue;

                float invMassA = InverseMass(a), invMassB = InverseMass(b);
                if (invMassA + invMassB == 0f) continue;
                if (!Collide(a, b, out Manifold m)) continue;

                float restitution = MathF.Max(a.Physics?.Restitution ?? 0f, b.Physics?.Restitution ?? 0f);
                float friction = MathF.Sqrt(
                    MathF.Max(0f, a.Physics?.Friction ?? 0.4f) *
                    MathF.Max(0f, b.Physics?.Friction ?? 0.4f));

                for (int k = 0; k < m.Count; k++)
                {
                    Vector2 p = k == 0 ? m.P0 : m.P1;
                    var c = new Contact
                    {
                        A = a, B = b,
                        Normal = m.Normal,
                        Tangent = new Vector2(m.Normal.Y, -m.Normal.X),
                        RA = p - a.Position,
                        RB = p - b.Position,
                        InvMassA = invMassA, InvMassB = invMassB,
                        InvIA = InverseInertia(a), InvIB = InverseInertia(b),
                        Friction = friction
                    };

                    float rnA = Cross(c.RA, c.Normal), rnB = Cross(c.RB, c.Normal);
                    float rtA = Cross(c.RA, c.Tangent), rtB = Cross(c.RB, c.Tangent);
                    float kn = invMassA + invMassB + c.InvIA * rnA * rnA + c.InvIB * rnB * rnB;
                    float kt = invMassA + invMassB + c.InvIA * rtA * rtA + c.InvIB * rtB * rtB;
                    c.NormalMass  = kn > 0f ? 1f / kn : 0f;
                    c.TangentMass = kt > 0f ? 1f / kt : 0f;

                    float vn = Vector2.Dot(RelativeVelocity(c), c.Normal);
                    c.Bias = vn < -RestitutionThreshold ? -restitution * vn : 0f;

                    Contacts.Add(c);
                }
            }
        }
    }

    static void SolveContact(Contact c)
    {
        Vector2 rv = RelativeVelocity(c);
        float lambdaT = -Vector2.Dot(rv, c.Tangent) * c.TangentMass;
        float maxFriction = c.Friction * c.Pn;
        float newPt = Math.Clamp(c.Pt + lambdaT, -maxFriction, maxFriction);
        lambdaT = newPt - c.Pt;
        c.Pt = newPt;
        ApplyImpulse(c, lambdaT * c.Tangent);

        rv = RelativeVelocity(c);
        float vn = Vector2.Dot(rv, c.Normal);
        float lambdaN = -(vn - c.Bias) * c.NormalMass;
        float newPn = MathF.Max(c.Pn + lambdaN, 0f);
        lambdaN = newPn - c.Pn;
        c.Pn = newPn;
        ApplyImpulse(c, lambdaN * c.Normal);
    }

    static void ApplyImpulse(Contact c, Vector2 p)
    {
        if (c.InvMassA > 0f)
        {
            PhysicsBody body = c.A.Physics!;
            body.Velocity -= p * c.InvMassA;
            body.AngularVelocity -= c.InvIA * Cross(c.RA, p);
        }
        if (c.InvMassB > 0f)
        {
            PhysicsBody body = c.B.Physics!;
            body.Velocity += p * c.InvMassB;
            body.AngularVelocity += c.InvIB * Cross(c.RB, p);
        }
    }

    static void CorrectPositions()
    {
        for (int i = 0; i < Bodies.Count; i++)
        {
            Entity a = Bodies[i];
            for (int j = i + 1; j < Bodies.Count; j++)
            {
                Entity b = Bodies[j];
                if (a.Collider!.IsTrigger || b.Collider!.IsTrigger) continue;
                if (a.IsRelatedTo(b)) continue;

                float invMassA = InverseMass(a), invMassB = InverseMass(b);
                float total = invMassA + invMassB;
                if (total == 0f || !Collide(a, b, out Manifold m)) continue;

                float correction = MathF.Max(m.Depth - PositionSlop, 0f) * PositionCorrection / total;
                a.Position -= m.Normal * (correction * invMassA);
                b.Position += m.Normal * (correction * invMassB);
            }
        }
    }

    static float InverseMass(Entity e) =>
        e.Parent == null && e.Physics is { Type: BodyType.Dynamic } body ? 1f / MathF.Max(body.Mass, 0.01f) : 0f;

    static float InverseInertia(Entity e)
    {
        if (e.Parent != null || e.Physics is not { Type: BodyType.Dynamic, LockRotation: false } body || e.Collider == null)
            return 0f;

        float m = MathF.Max(body.Mass, 0.01f);
        Vector2 h = ColliderHalfSize(e);
        float inertia = e.Collider.Shape == ColliderShape.Circle
            ? 0.5f * m * MathF.Min(h.X, h.Y) * MathF.Min(h.X, h.Y)
            : m * (h.X * h.X + h.Y * h.Y) / 3f;
        return 1f / MathF.Max(inertia, 0.0001f);
    }

    static Vector2 Velocity(Entity e) =>
        e.Parent == null && e.Physics is { Type: BodyType.Dynamic } p ? p.Velocity : Vector2.Zero;

    static float AngularVelocity(Entity e) =>
        e.Parent == null && e.Physics is { Type: BodyType.Dynamic } p ? p.AngularVelocity : 0f;

    static Vector2 RelativeVelocity(Contact c) =>
        (Velocity(c.B) + CrossW(AngularVelocity(c.B), c.RB)) -
        (Velocity(c.A) + CrossW(AngularVelocity(c.A), c.RA));

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    static Vector2 CrossW(float w, Vector2 r) => new(-w * r.Y, w * r.X);

    static Vector2 ColliderHalfSize(Entity e) =>
        Vector2.Abs(e.Scale) * e.Collider!.Size * Entity.BaseSize * 0.5f;

    static bool Collide(Entity a, Entity b, out Manifold m)
    {
        m = default;
        Vector2 ha = ColliderHalfSize(a), hb = ColliderHalfSize(b);

        float reach = ha.Length() + hb.Length();
        if (Vector2.DistanceSquared(a.Position, b.Position) > reach * reach) return false;

        bool circleA = a.Collider!.Shape == ColliderShape.Circle;
        bool circleB = b.Collider!.Shape == ColliderShape.Circle;

        if (circleA && circleB) return CircleCircle(a, b, ha, hb, out m);
        if (!circleA && !circleB) return PolygonPolygon(a, b, out m);
        return CirclePolygon(a, b, circleA, ha, hb, out m);
    }

    static bool CircleCircle(Entity a, Entity b, Vector2 ha, Vector2 hb, out Manifold m)
    {
        m = default;
        float ra = MathF.Min(ha.X, ha.Y), rb = MathF.Min(hb.X, hb.Y);
        Vector2 delta = b.Position - a.Position;
        float radius = ra + rb;
        float d2 = delta.LengthSquared();
        if (d2 >= radius * radius) return false;

        float dist = MathF.Sqrt(d2);
        m.Normal = dist > 0.0001f ? delta / dist : Vector2.UnitX;
        m.Add(a.Position + m.Normal * ra, radius - dist);
        return true;
    }

    static int PolygonVertexCount(ColliderShape shape) => shape switch
    {
        ColliderShape.Triangle => 3,
        ColliderShape.Hexagon => 6,
        _ => 4
    };

    static void PolygonVertices(Entity e, Span<Vector2> v)
    {
        Vector2 h = ColliderHalfSize(e);
        switch (e.Collider!.Shape)
        {
            case ColliderShape.Triangle:
                v[0] = new Vector2(0f, -h.Y);
                v[1] = new Vector2(h.X, h.Y);
                v[2] = new Vector2(-h.X, h.Y);
                break;
            case ColliderShape.Hexagon:
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * MathF.Tau / 6f;
                    v[i] = new Vector2(MathF.Cos(angle) * h.X, MathF.Sin(angle) * h.Y);
                }
                break;
            default:
                v[0] = new Vector2(-h.X, -h.Y);
                v[1] = new Vector2(h.X, -h.Y);
                v[2] = new Vector2(h.X, h.Y);
                v[3] = new Vector2(-h.X, h.Y);
                break;
        }

        for (int i = 0; i < PolygonVertexCount(e.Collider.Shape); i++)
            v[i] = e.Position + Rotate(v[i], e.Rotation);
    }

    static float MaxSeparation(ReadOnlySpan<Vector2> va, int countA, ReadOnlySpan<Vector2> vb, int countB, out int edge)
    {
        float best = float.NegativeInfinity;
        edge = 0;
        for (int i = 0; i < countA; i++)
        {
            Vector2 v1 = va[i];
            Vector2 e = va[(i + 1) % countA] - v1;
            Vector2 n = Vector2.Normalize(new Vector2(e.Y, -e.X));

            float minSep = float.PositiveInfinity;
            for (int j = 0; j < countB; j++)
                minSep = MathF.Min(minSep, Vector2.Dot(n, vb[j] - v1));

            if (minSep > best) { best = minSep; edge = i; }
        }
        return best;
    }

    static int ClipSegment(ref Vector2 p0, ref Vector2 p1, Vector2 n, float offset)
    {
        float d0 = Vector2.Dot(n, p0) - offset;
        float d1 = Vector2.Dot(n, p1) - offset;
        if (d0 >= 0f && d1 >= 0f) return 2;
        if (d0 < 0f && d1 < 0f) return 0;

        Vector2 hit = p0 + (p1 - p0) * (d0 / (d0 - d1));
        if (d0 < 0f) p0 = hit; else p1 = hit;
        return 2;
    }

    static bool PolygonPolygon(Entity a, Entity b, out Manifold m)
    {
        m = default;
        Span<Vector2> va = stackalloc Vector2[6];
        Span<Vector2> vb = stackalloc Vector2[6];
        int countA = PolygonVertexCount(a.Collider!.Shape);
        int countB = PolygonVertexCount(b.Collider!.Shape);
        PolygonVertices(a, va);
        PolygonVertices(b, vb);

        float sepA = MaxSeparation(va, countA, vb, countB, out int edgeA);
        if (sepA > 0f) return false;
        float sepB = MaxSeparation(vb, countB, va, countA, out int edgeB);
        if (sepB > 0f) return false;

        bool flip = sepB > sepA + 0.1f;
        Span<Vector2> rf  = flip ? vb : va;
        Span<Vector2> inc = flip ? va : vb;
        int refCount = flip ? countB : countA;
        int incCount = flip ? countA : countB;
        int refEdge = flip ? edgeB : edgeA;

        Vector2 r1 = rf[refEdge], r2 = rf[(refEdge + 1) % refCount];
        Vector2 side = Vector2.Normalize(r2 - r1);
        Vector2 refN = new(side.Y, -side.X);

        int incEdge = 0;
        float minDot = float.MaxValue;
        for (int i = 0; i < incCount; i++)
        {
            Vector2 e = inc[(i + 1) % incCount] - inc[i];
            float d = Vector2.Dot(refN, Vector2.Normalize(new Vector2(e.Y, -e.X)));
            if (d < minDot) { minDot = d; incEdge = i; }
        }

        Vector2 c0 = inc[incEdge], c1 = inc[(incEdge + 1) % incCount];
        if (ClipSegment(ref c0, ref c1,  side, Vector2.Dot( side, r1)) < 2) return false;
        if (ClipSegment(ref c0, ref c1, -side, Vector2.Dot(-side, r2)) < 2) return false;

        m.Normal = flip ? -refN : refN;
        float s0 = Vector2.Dot(refN, c0 - r1);
        float s1 = Vector2.Dot(refN, c1 - r1);
        if (s0 <= 0f) m.Add(c0, -s0);
        if (s1 <= 0f) m.Add(c1, -s1);
        return m.Count > 0;
    }

    static bool CirclePolygon(Entity a, Entity b, bool circleIsA, Vector2 ha, Vector2 hb, out Manifold m)
    {
        m = default;
        Entity circle = circleIsA ? a : b;
        Entity polygon = circleIsA ? b : a;
        Vector2 circleHalf = circleIsA ? ha : hb;
        float radius = MathF.Min(circleHalf.X, circleHalf.Y);

        Span<Vector2> vertices = stackalloc Vector2[6];
        int count = PolygonVertexCount(polygon.Collider!.Shape);
        PolygonVertices(polygon, vertices);

        float maxSeparation = float.NegativeInfinity;
        Vector2 nearest = default;
        float nearestDistanceSquared = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector2 start = vertices[i];
            Vector2 edge = vertices[(i + 1) % count] - start;
            Vector2 outward = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
            maxSeparation = MathF.Max(maxSeparation, Vector2.Dot(outward, circle.Position - start));

            float edgeLengthSquared = edge.LengthSquared();
            float t = edgeLengthSquared > 0f
                ? Math.Clamp(Vector2.Dot(circle.Position - start, edge) / edgeLengthSquared, 0f, 1f)
                : 0f;
            Vector2 point = start + edge * t;
            float distanceSquared = Vector2.DistanceSquared(circle.Position, point);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = point;
            }
        }

        if (maxSeparation > radius) return false;

        Vector2 circleToPolygon;
        float depth;
        if (maxSeparation >= 0f)
        {
            float distance = MathF.Sqrt(nearestDistanceSquared);
            if (distance > radius) return false;
            circleToPolygon = distance > 1e-6f ? (nearest - circle.Position) / distance : Vector2.UnitX;
            depth = radius - distance;
        }
        else
        {
            int closestEdge = 0;
            float closestSeparation = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector2 edge = vertices[(i + 1) % count] - vertices[i];
                Vector2 outward = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
                float separation = Vector2.Dot(outward, circle.Position - vertices[i]);
                if (separation > closestSeparation)
                {
                    closestSeparation = separation;
                    closestEdge = i;
                }
            }

            Vector2 edgeDirection = Vector2.Normalize(vertices[(closestEdge + 1) % count] - vertices[closestEdge]);
            Vector2 outwardNormal = new(edgeDirection.Y, -edgeDirection.X);
            circleToPolygon = -outwardNormal;
            depth = radius - closestSeparation;
            nearest = circle.Position + outwardNormal * -closestSeparation;
        }

        m.Normal = circleIsA ? circleToPolygon : -circleToPolygon;
        m.Add(nearest, depth);
        return true;
    }

    static Vector2 Rotate(Vector2 value, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new Vector2(value.X * cosine - value.Y * sine, value.X * sine + value.Y * cosine);
    }
}

public static class Theme
{
    public static readonly Color Window    = new(32, 32, 32, 255);
    public static readonly Color Panel     = new(56, 56, 56, 255);
    public static readonly Color PanelDark = new(42, 42, 42, 255);
    public static readonly Color Header    = new(72, 72, 72, 255);
    public static readonly Color Border    = new(24, 24, 24, 255);
    public static readonly Color Button    = new(88, 88, 88, 255);
    public static readonly Color Hover     = new(104, 104, 104, 255);
    public static readonly Color Accent    = new(44, 93, 135, 255);
    public static readonly Color Text      = new(210, 210, 210, 255);
    public static readonly Color Dim       = new(150, 150, 150, 255);
    public static readonly Color Orange    = new(255, 160, 0, 255);
}

public static class Ui
{
    public static Vector2 Mouse;
    public static bool Down, Pressed, Released;
    public static Rectangle? InteractionClip;

    static int _idCounter;
    static int _active;
    static int _editing;
    static string _buf = "";
    static Vector2 _pressPos;
    static float _pressValue;
    static bool _moved;

    public static bool IsEditing => _editing != 0;
    public static bool IsDragging => _active != 0;

    public static void Begin()
    {
        InteractionClip = null;
        Mouse    = Raylib.GetMousePosition();
        Down     = Raylib.IsMouseButtonDown(MouseButton.Left);
        Pressed  = Raylib.IsMouseButtonPressed(MouseButton.Left);
        Released = Raylib.IsMouseButtonReleased(MouseButton.Left);
        _idCounter = 0;
    }

    public static void End()
    {
        if (Released) _active = 0;
    }

    public static void CancelEdit() => _editing = 0;

    public static bool Hit(Rectangle r) =>
        Raylib.CheckCollisionPointRec(Mouse, r) &&
        (InteractionClip == null || Raylib.CheckCollisionPointRec(Mouse, InteractionClip.Value));

    public static void Text(string s, float x, float y, int size, Color c)
        => Raylib.DrawText(s, (int)x, (int)y, size, c);

    public static void TextCentered(string s, Rectangle r, int size, Color c)
    {
        int w = Raylib.MeasureText(s, size);
        Raylib.DrawText(s, (int)(r.X + (r.Width - w) / 2f), (int)(r.Y + (r.Height - size) / 2f), size, c);
    }

    static int TextInput(ref string s, bool numeric)
    {
        int ch;
        while ((ch = Raylib.GetCharPressed()) > 0)
        {
            char c = (char)ch;
            bool ok = numeric ? (char.IsDigit(c) || c == '.' || c == '-') : c >= 32;
            if (ok && s.Length < 64) s += c;
        }

        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && s.Length > 0)
            s = s[..^1];

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) return 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return 2;
        return 0;
    }

    public static bool Button(Rectangle r, string label, bool toggled = false)
    {
        bool hover = Hit(r);
        Color bg = toggled ? Theme.Accent : hover ? Theme.Hover : Theme.Button;
        Raylib.DrawRectangleRec(r, bg);
        Raylib.DrawRectangleLinesEx(r, 1, Theme.Border);
        TextCentered(label, r, 14, Theme.Text);
        return hover && Pressed && _active == 0 && _editing == 0;
    }

    public static bool Swatch(Rectangle r, Color c, bool selected)
    {
        bool hover = Hit(r);
        Raylib.DrawRectangleRec(r, c);
        Raylib.DrawRectangleLinesEx(r, selected ? 2 : 1, selected ? Color.White : Theme.Border);
        return hover && Pressed && _active == 0;
    }

    public static bool Checkbox(Rectangle r, ref bool value)
    {
        bool hover = Hit(r);
        Raylib.DrawRectangleRec(r, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(r, 1, Theme.Border);
        if (value)
            Raylib.DrawRectangle((int)r.X + 4, (int)r.Y + 4, (int)r.Width - 8, (int)r.Height - 8, Theme.Accent);
        if (hover && Pressed)
        {
            value = !value;
            return true;
        }
        return false;
    }

    public static bool DragFloat(Rectangle r, ref float v, float speed, float min, float max)
    {
        int id = ++_idCounter;
        bool hover = Hit(r);
        bool changed = false;

        if (_editing == id)
        {
            Raylib.DrawRectangleRec(r, new Color(25, 25, 25, 255));
            Raylib.DrawRectangleLinesEx(r, 1, Theme.Accent);
            Text(_buf + (Raylib.GetTime() % 1.0 < 0.5 ? "|" : ""), r.X + 5, r.Y + 4, 14, Theme.Text);

            int res = TextInput(ref _buf, true);
            bool clickOutside = Pressed && !hover;
            if (res == 1 || clickOutside)
            {
                if (float.TryParse(_buf, NumberStyles.Float, CultureInfo.InvariantCulture, out float nv))
                {
                    v = Math.Clamp(nv, min, max);
                    changed = true;
                }
                _editing = 0;
            }
            else if (res == 2) _editing = 0;
            return changed;
        }

        if (hover && Pressed && _active == 0)
        {
            _active = id;
            _pressPos = Mouse;
            _pressValue = v;
            _moved = false;
        }

        if (_active == id)
        {
            float dx = Mouse.X - _pressPos.X;
            if (MathF.Abs(dx) > 3f) _moved = true;
            if (_moved)
            {
                v = Math.Clamp(_pressValue + dx * speed, min, max);
                changed = true;
            }
            if (Released)
            {
                if (!_moved)
                {
                    _editing = id;
                    _buf = v.ToString("0.##", CultureInfo.InvariantCulture);
                }
                _active = 0;
            }
        }

        Raylib.DrawRectangleRec(r, hover || _active == id ? Theme.Hover : Theme.Button);
        Raylib.DrawRectangleLinesEx(r, 1, Theme.Border);
        TextCentered(v.ToString("0.##", CultureInfo.InvariantCulture), r, 14, Theme.Text);
        return changed;
    }

    public static bool TextField(Rectangle r, ref string value)
    {
        int id = ++_idCounter;
        bool hover = Hit(r);

        if (_editing == id)
        {
            Raylib.DrawRectangleRec(r, new Color(25, 25, 25, 255));
            Raylib.DrawRectangleLinesEx(r, 1, Theme.Accent);
            Text(_buf + (Raylib.GetTime() % 1.0 < 0.5 ? "|" : ""), r.X + 5, r.Y + 4, 14, Theme.Text);

            int res = TextInput(ref _buf, false);
            if (res == 1 || (Pressed && !hover))
            {
                value = _buf.Length > 0 ? _buf : value;
                _editing = 0;
                return true;
            }
            if (res == 2) _editing = 0;
            return false;
        }

        if (hover && Pressed && _active == 0)
        {
            _editing = id;
            _buf = value;
        }

        Raylib.DrawRectangleRec(r, hover ? Theme.Hover : Theme.Button);
        Raylib.DrawRectangleLinesEx(r, 1, Theme.Border);
        Text(value, r.X + 5, r.Y + 4, 14, Theme.Text);
        return false;
    }
}

internal static class Program
{
    const int ToolbarH = 44;
    const int LeftW    = 240;
    const int RightW   = 300;
    const int BottomH  = 160;
    const float GridStep = 50f;
    const float SnapStep = 25f;
    const string ProjectsDir = "Projects";

    const float ButtonsGapDown = 40f;

    const float ModifierRowH   = 42f;
    const float ModifierRowGap = 4f;
    const float ModifierHeadH  = 24f;
    const int   ModifierCount  = 4;
    const float ModifierPanelH = ModifierHeadH + ModifierCount * (ModifierRowH + ModifierRowGap) + 4f;

    static readonly List<Entity> Entities = new();
    static readonly List<string> LogLines = new();
    static readonly Dictionary<ShapeType, int> Counters = new();
    static readonly Color[] Palette =
    {
        new(231, 76, 60, 255), new(230, 126, 34, 255), new(241, 196, 15, 255), new(46, 204, 113, 255),
        new(26, 188, 156, 255), new(52, 152, 219, 255), new(155, 89, 182, 255), new(236, 240, 241, 255)
    };

    static Entity? _selected;
    static readonly List<Entity> _selection = new();
    static Entity? _dragging;
    static Vector2 _dragOffset;
    static readonly Dictionary<Entity, Vector2> _dragStartPositions = new();
    static ShapeType? _placing;
    static bool _snap = true;
    static bool _panning;
    static int _paletteIndex;
    static float _hierScroll;
    static Entity? _hierPressed;
    static Entity? _hierDragging;
    static Vector2 _hierPressPos;
    static Camera2D _cam;

    static Rectangle _toolbar, _hierarchy, _inspector, _bottom, _scene, _tabStrip;
    static bool _mouseInScene;
    static Vector2 _mouseWorld;
    static bool _assets;
    static bool _vsTab;
    static bool _sTab;
    static bool _modifierMenuOpen;
    static bool _isPlaying;
    static Vector2 _editorCameraTarget;
    static float _editorCameraZoom = 1f;
    static Entity? _playCamera;
    static readonly Dictionary<Entity, Entity> PlaySnapshot = new();
    static float _inspectorScroll;
    static string _projectName = "project";
    static string[] _assetFiles = Array.Empty<string>();

    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(1280, 720, "Smoothie Engine");
        Raylib.SetWindowMinSize(900, 520);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);

        _cam = new Camera2D { Zoom = 1f };

        VsTab.Entities = Entities;
        VsTab.Log = Log;

        Log("Smoothie Engine ready.");
        Log("Pick a shape in the toolbar, then click in the Scene to place it.");
        Log("Ctrl+click = multi-select, MMB/RMB drag = pan, Wheel = zoom, Del = delete, Ctrl+D = duplicate, F = focus, Ctrl+S = save, Ctrl+O = open scenes");
        Spawn(ShapeType.Square, new Vector2(-100, 0));
        Spawn(ShapeType.Circle, new Vector2(100, 0));
        Select(null);
        RefreshAssets();

        while (!Raylib.WindowShouldClose())
        {
            Ui.Begin();
            ComputeLayout();
            UpdateScene();
            if (_isPlaying)
            {
                float dt = MathF.Min((float)Raylib.GetFrameTime(), 0.1f);
                VsTab.Tick(dt);
                PhysicsSimulation.Update(Entities, dt);
            }
            SyncParenting();
            UpdatePlayCamera();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Theme.Window);

            DrawScene();
            DrawToolbar();
            DrawHierarchy();
            DrawInspector();
            DrawBottomPanel();

            Raylib.EndDrawing();
            Ui.End();
        }

        Raylib.CloseWindow();
    }

    static void Log(string msg)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
        if (LogLines.Count > 200) LogLines.RemoveAt(0);
    }

    static Vector2 Snap(Vector2 p) =>
        new(MathF.Round(p.X / SnapStep) * SnapStep, MathF.Round(p.Y / SnapStep) * SnapStep);

    static void Select(Entity? e, bool additive = false)
    {
        if (additive)
        {
            if (e == null) return;
            if (_selection.Contains(e))
            {
                _selection.Remove(e);
                if (ReferenceEquals(e, _selected))
                    _selected = _selection.LastOrDefault();
            }
            else
            {
                _selection.Add(e);
                _selected = e;
            }
        }
        else
        {
            _selection.Clear();
            if (e != null) _selection.Add(e);
            _selected = e;
        }

        Ui.CancelEdit();
        _modifierMenuOpen = false;
        _inspectorScroll = 0f;

        if (_vsTab)
        {
            if (_selected == null)
                VsTab.Open(null);
            else if (_selected.VisualScript != null)
                VsTab.OpenForEntity(_selected);
            else if (_selected.Scripts.Count > 0)
                VsTab.OpenForEntity(_selected);
            else
                VsTab.Open(null);
        }
    }

    static Entity Spawn(ShapeType shape, Vector2 pos)
    {
        Counters.TryGetValue(shape, out int n);
        Counters[shape] = ++n;

        var e = new Entity
        {
            Name = n == 1 ? shape.ToString() : $"{shape} ({n})",
            Shape = shape,
            Position = pos,
            Color = Palette[_paletteIndex++ % Palette.Length]
        };
        Entities.Add(e);
        Select(e);
        Log($"Created {e.Name}");
        return e;
    }

    static void DeleteSelected()
    {
        if (_selection.Count == 0) return;
        var deleting = new HashSet<Entity>(_selection);
        foreach (Entity entity in deleting)
        {
            Log($"Deleted {entity.Name}");
            foreach (Entity child in Entities)
                if (ReferenceEquals(child.Parent, entity) && !deleting.Contains(child))
                    child.SetParent(null);
            Entities.Remove(entity);
        }
        Select(null);
    }

    static void DuplicateSelected()
    {
        if (_selection.Count == 0) return;
        List<Entity> selectedOriginals = _selection.ToList();
        Entity? selectedOriginal = _selected;
        var originals = new List<Entity>();
        var included = new HashSet<Entity>();

        void AddSubtree(Entity entity)
        {
            if (!included.Add(entity)) return;
            originals.Add(entity);
            foreach (Entity child in Entities)
                if (ReferenceEquals(child.Parent, entity))
                    AddSubtree(child);
        }

        foreach (Entity entity in selectedOriginals) AddSubtree(entity);

        var copies = originals.ToDictionary(entity => entity, entity => entity.Clone());
        Vector2 offset = new(SnapStep, SnapStep);
        foreach (Entity original in originals)
        {
            Entity copy = copies[original];
            copy.Name += " Copy";
            copy.Position += offset;
            Entity? parent = original.Parent != null && copies.TryGetValue(original.Parent, out Entity? parentCopy)
                ? parentCopy
                : original.Parent;
            copy.SetParent(parent);
            Entities.Add(copy);
        }

        Entity primary = selectedOriginal != null && copies.TryGetValue(selectedOriginal, out Entity? selectedCopy)
            ? selectedCopy
            : copies[selectedOriginals[^1]];
        Select(primary);
        foreach (Entity original in selectedOriginals)
            if (!ReferenceEquals(original, selectedOriginal))
                _selection.Add(copies[original]);
        Log($"Duplicated {originals.Count} object(s), including children");
    }

    static void CreateCameraObject()
    {
        int number = Entities.Count(e => e.Camera != null) + 1;
        var camera = new Entity
        {
            Name = number == 1 ? "Camera" : $"Camera {number}",
            Position = _cam.Target,
            Camera = new CameraComponent()
        };
        Entities.Add(camera);
        Select(camera);
        Log($"Created {camera.Name}");
    }

    static void AddModifier()
    {
        _modifierMenuOpen = !_modifierMenuOpen;
    }

    static void StartSimulation()
    {
        if (_isPlaying) return;
        _dragging = null;
        _editorCameraTarget = _cam.Target;
        _editorCameraZoom = _cam.Zoom;
        _playCamera = Entities.FirstOrDefault(e => e.Active && e.Camera != null);
        PlaySnapshot.Clear();
        foreach (Entity e in Entities)
            PlaySnapshot[e] = e.Clone();
        PhysicsSimulation.Reset();
        VsTab.Start();
        _isPlaying = true;
        if (_playCamera != null)
            Log($"Camera mode started using {_playCamera.Name}.");
        else
            Log("Play mode started without a camera component.");
    }

    static void StopSimulation()
    {
        if (!_isPlaying) return;
        foreach ((Entity e, Entity snapshot) in PlaySnapshot)
        {
            if (!Entities.Contains(e)) continue;
            e.Name = snapshot.Name;
            e.Shape = snapshot.Shape;
            e.Active = snapshot.Active;
            e.Position = snapshot.Position;
            e.Rotation = snapshot.Rotation;
            e.Scale = snapshot.Scale;
            e.Color = snapshot.Color;
            e.Physics = snapshot.Physics?.Clone();
            e.Collider = snapshot.Collider?.Clone();
            e.Camera = snapshot.Camera?.Clone();
        }
        PlaySnapshot.Clear();
        PhysicsSimulation.Reset();
        VsTab.Stop();
        _isPlaying = false;
        _cam.Target = _editorCameraTarget;
        _cam.Zoom = _editorCameraZoom;
        _playCamera = null;
        Log("Physics simulation stopped; scene restored.");
    }

    static string Sanitize(string name)
    {
        string clean = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
        return clean.Length == 0 ? "project" : clean;
    }

    static string ProjectPathFor(string name) =>
        Path.Combine(ProjectsDir, Sanitize(name) + ProjectSerializer.Extension);

    static void RefreshAssets()
    {
        _assetFiles = Directory.Exists(ProjectsDir)
            ? Directory.EnumerateFiles(ProjectsDir, "*" + ProjectSerializer.Extension)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(f => f)
                .ToArray()
            : Array.Empty<string>();
    }

    static void SaveScene()
    {
        if (_isPlaying)
        {
            Log("Stop play mode before saving.");
            return;
        }

        try
        {
            _projectName = Sanitize(_projectName);
            string path = ProjectPathFor(_projectName);
            bool existed = File.Exists(path);

            Directory.CreateDirectory(ProjectsDir);
            ProjectSerializer.Save(path, Entities, _cam.Target, _cam.Zoom, VsTab.Export(Entities));

            Log($"{(existed ? "Saved" : "Created")} '{_projectName}' ({Entities.Count} objects)");
            RefreshAssets();
        }
        catch (Exception ex)
        {
            Log($"Save failed: {ex.Message}");
        }
    }

    static void LoadScene(string name)
    {
        string path = ProjectPathFor(name);
        if (!ProjectSerializer.TryLoad(path, out LoadedProject? project, out string error) || project == null)
        {
            Log($"Load failed: {error}");
            return;
        }

        StopSimulation();
        _dragging = null;
        _placing = null;
        Select(null);

        Entities.Clear();
        Entities.AddRange(project.Entities);
        VsTab.Import(project.Vs, Entities);

        Counters.Clear();
        foreach (Entity e in Entities)
        {
            Counters.TryGetValue(e.Shape, out int n);
            Counters[e.Shape] = n + 1;
        }

        _cam.Target = project.CameraTarget;
        _cam.Zoom = project.CameraZoom;
        _projectName = Sanitize(name);
        Log($"Loaded '{_projectName}' ({Entities.Count} objects)");
    }

    static void NewProject()
    {
        StopSimulation();
        _dragging = null;
        _placing = null;
        Select(null);
        Entities.Clear();
        Counters.Clear();
        VsTab.Clear();
        _cam.Target = Vector2.Zero;
        _cam.Zoom = 1f;
        _projectName = "untitled";
        Log("New Project. Rename it in the Hierarchy, then press Ctrl+S to save.");
    }

    static void SyncParenting()
    {
        foreach (Entity e in Entities.Where(c => c.Parent != null).OrderBy(c => c.Depth).ToList())
            e.SyncToParent();
    }

    static void UpdatePlayCamera()
    {
        if (!_isPlaying || _playCamera == null) return;

        if (!Entities.Contains(_playCamera) || !_playCamera.Active || _playCamera.Camera == null)
        {
            _playCamera = Entities.FirstOrDefault(e => e.Active && e.Camera != null);
            if (_playCamera == null) return;
        }

        _cam.Target = _playCamera.Position;
        _cam.Zoom = Math.Clamp(_playCamera.Camera!.Zoom, 0.1f, 8f);
    }

    static void BuildRows(List<(Entity E, int Depth)> rows)
    {
        foreach (Entity e in Entities)
            if (e.Parent == null || !Entities.Contains(e.Parent))
                AddRow(rows, e, 0);
    }

    static void AddRow(List<(Entity E, int Depth)> rows, Entity e, int depth)
    {
        rows.Add((e, depth));
        if (depth > 32) return;
        foreach (Entity c in Entities)
            if (ReferenceEquals(c.Parent, e)) AddRow(rows, c, depth + 1);
    }

    static void StepParent(Entity e, int dir)
    {
        var options = new List<Entity?> { null };
        foreach (Entity c in Entities)
            if (!ReferenceEquals(c, e) && !c.IsDescendantOf(e)) options.Add(c);

        int idx = options.FindIndex(o => ReferenceEquals(o, e.Parent));
        if (idx < 0) idx = 0;
        int pick = ((idx + dir) % options.Count + options.Count) % options.Count;
        e.SetParent(options[pick]);
        Log($"{e.Name} parent -> {options[pick]?.Name ?? "None"}");
    }

    static void ParentRow(Entity e, float x, float y)
    {
        Ui.Text("Parent", x, y + 3, 14, Theme.Text);
        float fw = _inspector.Width - 114f;
        var prev = new Rectangle(x + 90, y, 22, 22);
        var next = new Rectangle(x + 90 + fw - 22, y, 22, 22);
        var box = new Rectangle(x + 116, y, fw - 48, 22);

        Raylib.DrawRectangleRec(box, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(box, 1, Theme.Border);
        Ui.TextCentered(e.Parent?.Name ?? "None", box, 14, Theme.Text);

        if (_isPlaying) return;
        if (Ui.Button(prev, "<")) StepParent(e, -1);
        if (Ui.Button(next, ">")) StepParent(e, 1);
    }

    static Entity? Pick(Vector2 world)
    {
        for (int i = Entities.Count - 1; i >= 0; i--)
            if (Entities[i].Active && Entities[i].Contains(world))
                return Entities[i];
        return null;
    }

    static void ComputeLayout()
    {
        int w = Raylib.GetScreenWidth();
        int h = Raylib.GetScreenHeight();

        _toolbar   = new Rectangle(0, 0, w, ToolbarH);
        _hierarchy = new Rectangle(0, ToolbarH, LeftW, h - ToolbarH);
        _inspector = new Rectangle(w - RightW, ToolbarH, RightW, h - ToolbarH);
        _bottom    = new Rectangle(LeftW, h - BottomH, w - LeftW - RightW, BottomH);
        _scene     = new Rectangle(LeftW, ToolbarH, w - LeftW - RightW, h - ToolbarH - BottomH);

        _cam.Offset = new Vector2(_scene.X + _scene.Width / 2f, _scene.Y + _scene.Height / 2f);
        _tabStrip = new Rectangle(_scene.X, _scene.Y, 150, 24);
        _mouseInScene = Ui.Hit(_scene) && !_vsTab && !_sTab && !Ui.Hit(_tabStrip);
        _mouseWorld = Raylib.GetScreenToWorld2D(Ui.Mouse, _cam);
    }

    static void UpdateScene()
    {
        bool typing = Ui.IsEditing;

        if (!typing)
        {
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) SaveScene();
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.O)) { _assets = true; RefreshAssets(); }
            if (_isPlaying)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Escape)) StopSimulation();
            }
            else if (!_vsTab && !_sTab)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Delete)) DeleteSelected();
                if (Raylib.IsKeyPressed(KeyboardKey.D) && ctrl) DuplicateSelected();
                if (Raylib.IsKeyPressed(KeyboardKey.F) && _selected != null) _cam.Target = _selected.Position;
                if (Raylib.IsKeyPressed(KeyboardKey.One))   _placing = ShapeType.Square;
                if (Raylib.IsKeyPressed(KeyboardKey.Two))   _placing = ShapeType.Circle;
                if (Raylib.IsKeyPressed(KeyboardKey.Three)) _placing = ShapeType.Triangle;
                if (Raylib.IsKeyPressed(KeyboardKey.Four))  _placing = ShapeType.Hexagon;
                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    if (_modifierMenuOpen) _modifierMenuOpen = false;
                    else if (_placing != null) _placing = null;
                    else Select(null);
                }
            }
        }

        if (!_isPlaying && _mouseInScene && !Ui.IsDragging)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0f)
            {
                Vector2 before = _mouseWorld;
                _cam.Zoom = Math.Clamp(_cam.Zoom * (1f + wheel * 0.1f), 0.1f, 8f);
                Vector2 after = Raylib.GetScreenToWorld2D(Ui.Mouse, _cam);
                _cam.Target += before - after;
                _mouseWorld = Raylib.GetScreenToWorld2D(Ui.Mouse, _cam);
            }
        }

        bool panPressed = !_isPlaying && (Raylib.IsMouseButtonPressed(MouseButton.Middle) || Raylib.IsMouseButtonPressed(MouseButton.Right));
        bool panDown = !_isPlaying && (Raylib.IsMouseButtonDown(MouseButton.Middle) || Raylib.IsMouseButtonDown(MouseButton.Right));
        if (panPressed && _mouseInScene)
        {
            _panning = true;
            if (Raylib.IsMouseButtonPressed(MouseButton.Right)) _placing = null;
        }
        if (!panDown) _panning = false;
        if (_panning) _cam.Target -= Raylib.GetMouseDelta() / _cam.Zoom;

        if (!_isPlaying && _mouseInScene && Raylib.IsMouseButtonPressed(MouseButton.Left) && !Ui.IsDragging)
        {
            if (_placing != null)
            {
                Vector2 p = _snap ? Snap(_mouseWorld) : _mouseWorld;
                Spawn(_placing.Value, p);
                if (!Raylib.IsKeyDown(KeyboardKey.LeftShift)) _placing = null;
            }
            else
            {
                Entity? hit = Pick(_mouseWorld);
                bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                bool wasSelected = hit != null && _selection.Contains(hit);
                if (ctrl)
                {
                    if (hit != null) Select(hit, additive: true);
                }
                else
                {
                    Select(hit);
                }

                _dragging = hit != null && !(ctrl && wasSelected) ? hit : null;
                _dragStartPositions.Clear();
                if (_dragging != null)
                {
                    _dragOffset = hit!.Position - _mouseWorld;
                    foreach (Entity entity in _selection)
                        _dragStartPositions[entity] = entity.Position;
                }
            }
        }

        if (!_isPlaying && _dragging != null)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                Vector2 p = Raylib.GetScreenToWorld2D(Ui.Mouse, _cam) + _dragOffset;
                Vector2 target = _snap ? Snap(p) : p;
                Vector2 delta = target - _dragStartPositions[_dragging];
                foreach ((Entity entity, Vector2 start) in _dragStartPositions)
                    entity.Position = start + delta;
            }
            else
            {
                _dragging = null;
                _dragStartPositions.Clear();
            }
        }
    }

    static void DrawSceneTabs()
    {
        if (Ui.Button(new Rectangle(_scene.X, _scene.Y, 70, 24), "Scene", !_vsTab)) setSceneTabs(false, false);
        if (Ui.Button(new Rectangle(_scene.X + 76, _scene.Y, 140, 24), "Visual Scripting", _vsTab)) setSceneTabs(true, false);
        if (Ui.Button(new Rectangle(_scene.X + 222, _scene.Y, 100, 24), "Scripting", _sTab)) setSceneTabs(false, true);
    }
    static void setSceneTabs(bool vsTab, bool sTab)
    {
        _vsTab = vsTab;
        _sTab = sTab;

        if (_vsTab)
        {
            if (_selected == null)
                VsTab.Open(null);
            else
                VsTab.OpenForEntity(_selected);
        }
        else if (!_sTab)
        {
            VsTab.Open(null);
        }
    }

    static void DrawScene()
    {
        if (_vsTab)
        {
            Raylib.BeginScissorMode((int)_scene.X, (int)_scene.Y, (int)_scene.Width, (int)_scene.Height);
            VsTab.Draw(new Rectangle(_scene.X, _scene.Y + 24, _scene.Width, _scene.Height - 24));
            DrawSceneTabs();
            Raylib.EndScissorMode();
            Raylib.DrawRectangleLinesEx(_scene, 1, Theme.Border);
            return;
        }

        if (_sTab)
        {
            Raylib.BeginScissorMode((int)_scene.X, (int)_scene.Y, (int)_scene.Width, (int)_scene.Height);
            STab.Draw(new Rectangle(_scene.X, _scene.Y + 24, _scene.Width, _scene.Height - 24));
            DrawSceneTabs();
            Raylib.EndScissorMode();
            Raylib.DrawRectangleLinesEx(_scene, 1, Theme.Border);
            return;
        }

        Raylib.BeginScissorMode((int)_scene.X, (int)_scene.Y, (int)_scene.Width, (int)_scene.Height);
        Raylib.DrawRectangleRec(_scene, new Color(38, 38, 38, 255));

        Raylib.BeginMode2D(_cam);

        if (!_isPlaying) DrawGrid();

        foreach (Entity e in Entities)
        {
            if (!e.Active || (_isPlaying && e.Camera != null)) continue;
            if (e.Camera == null) e.Draw();
            else DrawCameraGizmo(e, _selection.Contains(e));
        }

        foreach (Entity e in Entities)
        {
            if (e.Active && e.Collider != null && (_isPlaying || _selection.Contains(e)))
                DrawCollider(e);
        }

        foreach (Entity selected in _selection)
        {
            if (!selected.Active || selected.Camera != null) continue;
            Vector2[] c = selected.Corners();
            float th = 2f / _cam.Zoom;
            for (int i = 0; i < 4; i++)
                Raylib.DrawLineEx(c[i], c[(i + 1) % 4], th, Theme.Orange);

            if (!ReferenceEquals(selected, _selected)) continue;
            float s = 6f / _cam.Zoom;
            Vector2 p = selected.Position;
            Raylib.DrawLineEx(p - new Vector2(s, 0), p + new Vector2(s, 0), th, Theme.Orange);
            Raylib.DrawLineEx(p - new Vector2(0, s), p + new Vector2(0, s), th, Theme.Orange);
        }

        if (_placing != null && _mouseInScene)
        {
            var ghost = new Entity
            {
                Shape = _placing.Value,
                Position = _snap ? Snap(_mouseWorld) : _mouseWorld,
                Color = Palette[_paletteIndex % Palette.Length]
            };
            ghost.Draw(0.5f);
        }

        Raylib.EndMode2D();

        DrawSceneTabs();

        if (_placing != null)
            Ui.Text($"Placing {_placing}  -  click to place (Shift = keep placing, Esc/RMB = cancel)",
                _scene.X + 160, _scene.Y + 5, 14, Theme.Orange);

        string status = $"Zoom {_cam.Zoom:0.00}x   ({_mouseWorld.X:0}, {_mouseWorld.Y:0})";
        int sw = Raylib.MeasureText(status, 14);
        Ui.Text(status, _scene.X + _scene.Width - sw - 10, _scene.Y + _scene.Height - 22, 14, Theme.Dim);

        Raylib.EndScissorMode();
        Raylib.DrawRectangleLinesEx(_scene, 1, Theme.Border);
    }

    static void DrawGrid()
    {
        Vector2 tl = Raylib.GetScreenToWorld2D(new Vector2(_scene.X, _scene.Y), _cam);
        Vector2 br = Raylib.GetScreenToWorld2D(new Vector2(_scene.X + _scene.Width, _scene.Y + _scene.Height), _cam);
        float th = 1f / _cam.Zoom;

        Color minor = new(52, 52, 52, 255);
        Color major = new(68, 68, 68, 255);

        int x0 = (int)MathF.Floor(tl.X / GridStep), x1 = (int)MathF.Ceiling(br.X / GridStep);
        int y0 = (int)MathF.Floor(tl.Y / GridStep), y1 = (int)MathF.Ceiling(br.Y / GridStep);

        for (int x = x0; x <= x1; x++)
            Raylib.DrawLineEx(new Vector2(x * GridStep, tl.Y), new Vector2(x * GridStep, br.Y), th, x % 5 == 0 ? major : minor);
        for (int y = y0; y <= y1; y++)
            Raylib.DrawLineEx(new Vector2(tl.X, y * GridStep), new Vector2(br.X, y * GridStep), th, y % 5 == 0 ? major : minor);

        Raylib.DrawLineEx(new Vector2(tl.X, 0), new Vector2(br.X, 0), th * 2f, new Color(160, 60, 60, 255));
        Raylib.DrawLineEx(new Vector2(0, tl.Y), new Vector2(0, br.Y), th * 2f, new Color(60, 160, 60, 255));
    }

    static void DrawCameraGizmo(Entity entity, bool selected)
    {
        CameraComponent camera = entity.Camera!;
        float zoom = Math.Clamp(camera.Zoom, 0.1f, 8f);
        float width = _scene.Width / zoom;
        float height = _scene.Height / zoom;
        var frame = new Rectangle(
            entity.Position.X - width * 0.5f,
            entity.Position.Y - height * 0.5f,
            width,
            height);
        Color frameColor = selected ? Theme.Orange : new Color(80, 180, 230, 170);
        Raylib.DrawRectangleLinesEx(frame, 2f / _cam.Zoom, frameColor);

        float unit = 1f / _cam.Zoom;
        float bodyWidth = 30f * unit;
        float bodyHeight = 22f * unit;
        var body = new Rectangle(
            entity.Position.X - bodyWidth * 0.5f,
            entity.Position.Y - bodyHeight * 0.5f,
            bodyWidth,
            bodyHeight);
        Color fill = selected ? Theme.Orange : new Color(55, 145, 190, 255);
        Raylib.DrawRectangleRec(body, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(body, 2f * unit, fill);
        Vector2 lensTip = new(entity.Position.X + bodyWidth * 0.5f + 8f * unit, entity.Position.Y);
        Raylib.DrawLineEx(
            new Vector2(entity.Position.X + bodyWidth * 0.5f, entity.Position.Y - bodyHeight * 0.3f),
            lensTip, 2f * unit, fill);
        Raylib.DrawLineEx(
            lensTip,
            new Vector2(entity.Position.X + bodyWidth * 0.5f, entity.Position.Y + bodyHeight * 0.3f),
            2f * unit, fill);
        Raylib.DrawLineEx(
            new Vector2(entity.Position.X + bodyWidth * 0.5f, entity.Position.Y + bodyHeight * 0.3f),
            new Vector2(entity.Position.X + bodyWidth * 0.5f, entity.Position.Y - bodyHeight * 0.3f),
            2f * unit, fill);
    }

    static void DrawCameraIcon(Vector2 center, Color color)
    {
        var body = new Rectangle(center.X - 7f, center.Y - 5f, 11f, 10f);
        Raylib.DrawRectangleRec(body, color);
        Raylib.DrawTriangle(
            new Vector2(center.X + 4f, center.Y - 3f),
            new Vector2(center.X + 9f, center.Y - 6f),
            new Vector2(center.X + 9f, center.Y + 6f),
            color);
    }

    static void Panel(Rectangle r, string title)
    {
        Raylib.DrawRectangleRec(r, Theme.Panel);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)r.Width, 26, Theme.Header);
        Ui.Text(title, r.X + 10, r.Y + 6, 14, Theme.Text);
        Raylib.DrawRectangleLinesEx(r, 1, Theme.Border);
    }

    static void DrawToolbar()
    {
        Raylib.DrawRectangleRec(_toolbar, Theme.Header);
        Raylib.DrawLine(0, ToolbarH - 1, (int)_toolbar.Width, ToolbarH - 1, Theme.Border);

        float x = 12;
        Ui.Text("Create:", x, 14, 14, Theme.Dim);
        x += 62;

        foreach (ShapeType s in Enum.GetValues<ShapeType>())
        {
            var r = new Rectangle(x, 8, 80, 28);
            if (Ui.Button(r, s.ToString(), _placing == s))
                _placing = _placing == s ? null : s;
            x += 86;
        }

        x += 14;
        if (Ui.Button(new Rectangle(x, 8, 78, 28), "Camera"))
            CreateCameraObject();
        x += 84;
        if (Ui.Button(new Rectangle(x, 8, 80, 28), _snap ? "Snap: On" : "Snap: Off", _snap)) _snap = !_snap;
        x += 86;
        if (Ui.Button(new Rectangle(x, 8, 90, 28), "Reset View"))
        {
            _cam.Target = Vector2.Zero;
            _cam.Zoom = 1f;
        }

        x += 110;
        if (Ui.Button(new Rectangle(x, 8, 64, 28), "Save")) SaveScene();
        x += 70;
        if (Ui.Button(new Rectangle(x, 8, 64, 28), "Load", _assets)) { _assets = true; RefreshAssets(); }
        x += 64;
        if (_isPlaying)
            Ui.Button(new Rectangle(x, 8, 60, 28), "Playing", true);
        else if (Ui.Button(new Rectangle(x, 8, 60, 28), "Play"))
            StartSimulation();

        float controlsRight = x + 60;
        if (_isPlaying)
        {
            x += 64;
            if (Ui.Button(new Rectangle(x, 8, 60, 28), "Stop"))
                StopSimulation();
            controlsRight = x + 60;
        }

        string title = $"Smoothie Engine   {Raylib.GetFPS()} FPS";
        int tw = Raylib.MeasureText(title, 14);
        float titleX = _toolbar.Width - tw - 12;
        if (titleX >= controlsRight + 8) Ui.Text(title, titleX, 14, 14, Theme.Dim);
    }

    static void DrawHierarchy()
    {
        Panel(_hierarchy, "Hierarchy");

        var list = new Rectangle(_hierarchy.X + 1, _hierarchy.Y + 27, _hierarchy.Width - 2, _hierarchy.Height - 28);
        const float rowH = 24f;
        float content = (Entities.Count + 1) * rowH;

        if (Ui.Hit(list)) _hierScroll -= Raylib.GetMouseWheelMove() * rowH * 2f;
        _hierScroll = Math.Clamp(_hierScroll, 0f, MathF.Max(0f, content - list.Height));

        Raylib.BeginScissorMode((int)list.X, (int)list.Y, (int)list.Width, (int)list.Height);

        float y = list.Y - _hierScroll;
        string projectName = _projectName;
        if (Ui.TextField(new Rectangle(list.X + 8, y + 1, list.Width - 16, rowH - 2), ref projectName))
            _projectName = Sanitize(projectName);
        Raylib.DrawLine((int)list.X, (int)(y + rowH), (int)(list.X + list.Width), (int)(y + rowH), Theme.Border);
        y += rowH;

        var rows = new List<(Entity E, int Depth)>();
        BuildRows(rows);
        bool overList = Ui.Hit(list);
        bool overRow = false;
        Entity? dropTarget = null;

        foreach ((Entity e, int depth) in rows)
        {
            var row = new Rectangle(list.X, y, list.Width, rowH);
            bool hover = Ui.Hit(row) && overList;
            float indent = depth * 16f;

            if (_selection.Contains(e)) Raylib.DrawRectangleRec(row, Theme.Accent);
            else if (hover && _hierDragging == null) Raylib.DrawRectangleRec(row, Theme.Hover);

            if (hover)
            {
                overRow = true;
                if (Ui.Pressed && !Ui.IsDragging)
                {
                    bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                    bool wasSelected = _selection.Contains(e);
                    Select(e, additive: ctrl);
                    if (!_isPlaying)
                    {
                        _hierPressed = ctrl && wasSelected ? null : e;
                        _hierPressPos = Ui.Mouse;
                    }
                }

                if (_hierDragging != null && !ReferenceEquals(e, _hierDragging) && !e.IsDescendantOf(_hierDragging))
                {
                    dropTarget = e;
                    Raylib.DrawRectangleLinesEx(row, 2, Theme.Orange);
                }
            }

            Vector2 iconCenter = new(row.X + 24 + indent, row.Y + rowH / 2f);
            if (e.Camera != null) DrawCameraIcon(iconCenter, new Color(80, 180, 230, 255));
            else DrawIcon(e.Shape, iconCenter, e.Color);
            Ui.Text(e.Name, row.X + 40 + indent, row.Y + 5, 14, e.Active ? Theme.Text : Theme.Dim);
            y += rowH;
        }

        if (_hierPressed != null)
        {
            if (Ui.Down)
            {
                if (_hierDragging == null && !_isPlaying && Vector2.Distance(Ui.Mouse, _hierPressPos) > 6f)
                    _hierDragging = _hierPressed;
            }
            else
            {
                if (_hierDragging != null && overList)
                {
                    if (dropTarget != null)
                    {
                        _hierDragging.SetParent(dropTarget);
                        Log($"Parented {_hierDragging.Name} -> {dropTarget.Name}");
                    }
                    else if (!overRow && _hierDragging.Parent != null)
                    {
                        _hierDragging.SetParent(null);
                        Log($"Unparented {_hierDragging.Name}");
                    }
                }
                _hierPressed = null;
                _hierDragging = null;
            }
        }

        Raylib.EndScissorMode();

        if (_hierDragging != null)
            Ui.Text(_hierDragging.Name, Ui.Mouse.X + 12, Ui.Mouse.Y + 4, 14, Theme.Orange);
    }

    static void DrawIcon(ShapeType s, Vector2 c, Color col)
    {
        const float r = 6f;
        switch (s)
        {
            case ShapeType.Square:   Raylib.DrawRectangleV(c - new Vector2(r, r), new Vector2(r * 2, r * 2), col); break;
            case ShapeType.Circle:   Raylib.DrawCircleV(c, r, col); break;
            case ShapeType.Triangle: Raylib.DrawTriangle(c + new Vector2(0, -r), c + new Vector2(-r, r), c + new Vector2(r, r), col); break;
            case ShapeType.Hexagon:  Raylib.DrawPoly(c, 6, r, 0f, col); break;
        }
    }

    static void DrawCollider(Entity e)
    {
        Collider collider = e.Collider!;
        Vector2 half = Vector2.Abs(e.Scale) * collider.Size * Entity.BaseSize * 0.5f;
        Color color = new(80, 210, 235, 210);
        if (collider.Shape == ColliderShape.Circle)
        {
            float radius = MathF.Min(half.X, half.Y);
            Raylib.DrawCircleLines((int)e.Position.X, (int)e.Position.Y, radius, color);

            float a = e.Rotation * MathF.PI / 180f;
            Vector2 tip = e.Position + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            Raylib.DrawLineEx(e.Position, tip, 2f / _cam.Zoom, color);
        }
        else
        {
            int count = collider.Shape switch
            {
                ColliderShape.Triangle => 3,
                ColliderShape.Hexagon => 6,
                _ => 4
            };

            Vector2[] vertices = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 local = collider.Shape switch
                {
                    ColliderShape.Triangle => i switch
                    {
                        0 => new Vector2(0f, -half.Y),
                        1 => new Vector2(half.X, half.Y),
                        _ => new Vector2(-half.X, half.Y)
                    },
                    ColliderShape.Hexagon => new Vector2(
                        MathF.Cos(i * MathF.Tau / count) * half.X,
                        MathF.Sin(i * MathF.Tau / count) * half.Y),
                    _ => i switch
                    {
                        0 => new Vector2(-half.X, -half.Y),
                        1 => new Vector2(half.X, -half.Y),
                        2 => new Vector2(half.X, half.Y),
                        _ => new Vector2(-half.X, half.Y)
                    }
                };
                float angle = e.Rotation * MathF.PI / 180f;
                float cosine = MathF.Cos(angle);
                float sine = MathF.Sin(angle);
                vertices[i] = e.Position + new Vector2(
                    local.X * cosine - local.Y * sine,
                    local.X * sine + local.Y * cosine);
            }
            for (int i = 0; i < count; i++)
                Raylib.DrawLineEx(vertices[i], vertices[(i + 1) % count], 2f / _cam.Zoom, color);
        }
    }

    static void DrawInspector()
    {
        Panel(_inspector, "Inspector");
        Entity? e = _selected;

        float x = _inspector.X + 12;

        if (e == null)
        {
            Ui.Text("No object selected", x, _inspector.Y + 38, 14, Theme.Dim);
            return;
        }

        Rectangle content = new(_inspector.X + 1, _inspector.Y + 27, _inspector.Width - 2, _inspector.Height - 28);
        if (Ui.Hit(content)) _inspectorScroll = MathF.Max(0f, _inspectorScroll - Raylib.GetMouseWheelMove() * 36f);
        Raylib.BeginScissorMode((int)content.X, (int)content.Y, (int)content.Width, (int)content.Height);
        Ui.InteractionClip = content;
        float y = content.Y + 11 - _inspectorScroll;

        bool active = e.Active;
        if (Ui.Checkbox(new Rectangle(x, y, 20, 20), ref active)) e.Active = active;
        string name = e.Name;
        if (Ui.TextField(new Rectangle(x + 28, y - 1, _inspector.Width - 52, 22), ref name)) e.Name = name;
        y += 30;
        Ui.Text($"Shape: {e.Shape}", x, y, 14, Theme.Dim);
        y += 28;

        Section("Transform", ref y);
        ParentRow(e, x, y);
        y += 28;
        Vector2 pos = e.Position;
        if (Vec2Row("Position", x, y, ref pos, 1f, -100000f, 100000f)) e.Position = pos;
        y += 28;

        float rot = e.Rotation;
        Ui.Text("Rotation", x, y + 3, 14, Theme.Text);
        if (Ui.DragFloat(new Rectangle(x + 90, y, 82, 22), ref rot, 1f, -100000f, 100000f)) e.Rotation = rot;
        y += 28;

        Vector2 scale = e.Scale;
        if (Vec2Row("Scale", x, y, ref scale, 0.01f, 0.05f, 100f)) e.Scale = scale;
        y += 40;

        Section("Renderer", ref y);
        Ui.Text("Color", x, y + 3, 14, Theme.Text);
        for (int i = 0; i < Palette.Length; i++)
        {
            var sw = new Rectangle(x + 70 + i * 26, y, 22, 22);
            if (Ui.Swatch(sw, Palette[i], false)) e.Color = Palette[i];
        }
        y += 32;

        float r = e.Color.R, g = e.Color.G, b = e.Color.B;
        bool cc = false;
        float fieldW = _inspector.Width - 24 - 30;

        Ui.Text("R", x, y + 3, 14, new Color(230, 90, 90, 255));
        cc |= Ui.DragFloat(new Rectangle(x + 24, y, fieldW, 22), ref r, 1f, 0f, 255f);
        y += 26;
        Ui.Text("G", x, y + 3, 14, new Color(90, 200, 90, 255));
        cc |= Ui.DragFloat(new Rectangle(x + 24, y, fieldW, 22), ref g, 1f, 0f, 255f);
        y += 26;
        Ui.Text("B", x, y + 3, 14, new Color(100, 140, 240, 255));
        cc |= Ui.DragFloat(new Rectangle(x + 24, y, fieldW, 22), ref b, 1f, 0f, 255f);
        y += 32;

        if (cc) e.Color = new Color((byte)r, (byte)g, (byte)b, (byte)255);

        if (e.Physics != null)
        {
            Section("Physics Object", ref y);
            Ui.Text("Body Type", x, y, 14, Theme.Text);
            if (Ui.Button(new Rectangle(x + 90, y, _inspector.Width - 112, 22), e.Physics.Type.ToString()))
                e.Physics.Type = (BodyType)(((int)e.Physics.Type + 1) % Enum.GetValues<BodyType>().Length);
            y += 26;

            float gravity = e.Physics.GravityScale;
            Ui.Text("Gravity", x, y + 3, 14, Theme.Text);
            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22), ref gravity, 0.05f, 0f, 10f))
                e.Physics.GravityScale = gravity;
            y += 26;

            float mass = e.Physics.Mass;
            Ui.Text("Mass", x, y, 14, Theme.Text);
            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22), ref mass, 0.05f, 0.01f, 1000f))
                e.Physics.Mass = mass;
            y += 26;

            float bounce = e.Physics.Restitution;
            Ui.Text("Restitution", x, y, 14, Theme.Text);
            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22), ref bounce, 0.01f, 0f, 1f))
                e.Physics.Restitution = bounce;
            y += 26;

            float friction = e.Physics.Friction;
            Ui.Text("Friction", x, y, 14, Theme.Text);
            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22), ref friction, 0.01f, 0f, 1f))
                e.Physics.Friction = friction;
            y += 26;

            float angDrag = e.Physics.AngularDamping;
            Ui.Text("Ang. Drag", x, y, 14, Theme.Text);
            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22), ref angDrag, 0.01f, 0f, 20f))
                e.Physics.AngularDamping = angDrag;
            y += 28;

            bool lockRot = e.Physics.LockRotation;
            if (Ui.Checkbox(new Rectangle(x, y, 20, 20), ref lockRot)) e.Physics.LockRotation = lockRot;
            Ui.Text("Freeze Rotation", x + 28, y, 14, Theme.Text);
            y += 30;
        }

        if (e.Collider != null)
        {
            Section("Collider", ref y);
            Ui.Text("Shape", x, y + 3, 14, Theme.Text);
            if (Ui.Button(new Rectangle(x + 90, y, _inspector.Width - 112, 22), e.Collider.Shape.ToString()))
            {
                int shapeCount = Enum.GetValues<ColliderShape>().Length;
                e.Collider.Shape = (ColliderShape)(((int)e.Collider.Shape + 1) % shapeCount);
            }
            y += 26;

            Vector2 colliderSize = e.Collider.Size;
            if (Vec2Row("Size", x, y, ref colliderSize, 0.01f, 0.05f, 20f))
                e.Collider.Size = colliderSize;
            y += 26;

            bool trigger = e.Collider.IsTrigger;
            if (Ui.Checkbox(new Rectangle(x, y, 20, 20), ref trigger)) e.Collider.IsTrigger = trigger;
            Ui.Text("Is Trigger", x + 28, y, 14, Theme.Text);
            y += 30;
        }

        if (e.Camera != null)
        {
            Section("Camera", ref y);
            Ui.Text("Zoom", x, y + 3, 14, Theme.Text);
            float zoom = e.Camera.Zoom;
            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22), ref zoom, 0.05f, 0.1f, 8f))
                e.Camera.Zoom = zoom;
            y += 28;
            Ui.Text(ReferenceEquals(e, _playCamera) && _isPlaying
                ? "Active camera in Play mode"
                : "Used by Play mode camera view", x, y + 2, 12, Theme.Dim);
            y += 22;
        }

        if (e.VisualScript != null)
        {
            Section("Visual Script", ref y);
            Ui.Text("Script Name", x, y + 3, 14, Theme.Text);
            string scriptName = e.VisualScript.Name;
            if (Ui.TextField(new Rectangle(x + 90, y - 1, _inspector.Width - 112, 22), ref scriptName))
                e.VisualScript.Name = scriptName;
            y += 28;

            Ui.Text("Nodes", x, y + 3, 14, Theme.Text);
            Ui.Text(e.VisualScript.Nodes.Count.ToString(), x + 90, y + 3, 14, Theme.Dim);
            y += 28;

            if (Ui.Button(new Rectangle(x + 90, y, _inspector.Width - 112, 22), "Add Example Script"))
            {
                e.VisualScript.AddExampleScript();
                Log($"Added example script to {e.Name}");
            }
            y += 30;

            foreach (ScriptInstance instance in e.Scripts)
            {
                instance.Sync();
                if (instance.Graph.Variables.Count == 0) continue;

                Ui.Text(instance.Graph.Name + " Variables", x, y + 3, 13, Theme.Dim);
                y += 24;
                foreach (VsVariable variable in instance.Graph.Variables)
                {
                    VsValue value = instance.Values[variable.Id];
                    switch (variable.Type)
                    {
                        case VarType.Float:
                        {
                            Ui.Text(variable.Name, x, y + 3, 14, Theme.Text);
                            float number = value.F;
                            if (Ui.DragFloat(new Rectangle(x + 90, y, _inspector.Width - 112, 22),
                                    ref number, 0.05f, -100000f, 100000f))
                                value.F = number;
                            y += 28;
                            break;
                        }
                        case VarType.Bool:
                            Ui.Checkbox(new Rectangle(x, y, 20, 20), ref value.B);
                            Ui.Text(variable.Name, x + 28, y + 2, 14, Theme.Text);
                            y += 26;
                            break;
                        case VarType.Vector2:
                            Vec2Row(variable.Name, x, y, ref value.V, 0.05f, -100000f, 100000f);
                            y += 26;
                            break;
                        case VarType.Key:
                        {
                            Ui.Text(variable.Name, x, y + 4, 14, Theme.Text);
                            KeyboardKey key = value.K;
                            if (VsTab.KeyField(new Rectangle(x + 90, y, _inspector.Width - 112, 22), value, ref key))
                                value.K = key;
                            y += 28;
                            break;
                        }
                        case VarType.Entity:
                            EntityValueRow(variable.Name, x, y, _inspector.Width - 24, value);
                            y += 28;
                            break;
                    }
                }
            }
        }

        y += ButtonsGapDown;

        float bw = (_inspector.Width - 36) / 2f;
        float tbw = bw * 2;
        Rectangle addButton = new(x, y, tbw, 28);
        if (Ui.Button(addButton, "Add Modifier", _modifierMenuOpen))
            AddModifier();

        float cx = addButton.X + addButton.Width - 18f, cy = addButton.Y + 14f;
        if (_modifierMenuOpen)
            Raylib.DrawTriangle(new Vector2(cx, cy - 3), new Vector2(cx - 5, cy + 3), new Vector2(cx + 5, cy + 3), Theme.Text);
        else
            Raylib.DrawTriangle(new Vector2(cx, cy + 3), new Vector2(cx + 5, cy - 3), new Vector2(cx - 5, cy - 3), Theme.Text);

        if (_modifierMenuOpen)
        {
            DrawModifierMenu(e, addButton);
        }
        else if (!_isPlaying)
        {
            if (Ui.Button(new Rectangle(x, y + 38, bw, 28), "Duplicate")) DuplicateSelected();
            if (Ui.Button(new Rectangle(x + bw + 12, y + 38, bw, 28), "Delete")) DeleteSelected();
        }
        else
        {
            Ui.Text("Stop play mode to edit objects", x, y + 42, 12, Theme.Dim);
        }

        float contentBottom = y + (_modifierMenuOpen ? 34 + ModifierPanelH + 12 : _isPlaying ? 60 : 66);
        float maxScroll = MathF.Max(0f, contentBottom + _inspectorScroll - content.Y - content.Height);
        _inspectorScroll = Math.Clamp(_inspectorScroll, 0f, maxScroll);
        Ui.InteractionClip = null;
        Raylib.EndScissorMode();
    }

    static void ToggleComponent(Entity e, int index)
    {
        if (index == 0)
        {
            if (e.Physics == null) { e.Physics = new PhysicsBody(); Log($"Added Physics Object to {e.Name}"); }
            else                   { e.Physics = null;              Log($"Removed Physics Object from {e.Name}"); }
        }
        else if (index == 1)
        {
            if (e.Collider == null) { e.Collider = new Collider(); Log($"Added Collider to {e.Name}"); }
            else                    { e.Collider = null;           Log($"Removed Collider from {e.Name}"); }
        }
        else if (index == 2)
        {
            if (e.VisualScript == null)
            {
                e.VisualScript = new VisualScript();
                e.VisualScript.AddExampleScript();
                Log($"Added Visual Script to {e.Name}");
            }
            else
            {
                e.VisualScript = null;
                Log($"Removed Visual Script from {e.Name}");
            }
        }
        else if (index == 3)
        {
            if (e.Camera == null)
            {
                e.Camera = new CameraComponent();
                Log($"Added Camera to {e.Name}");
            }
            else
            {
                e.Camera = null;
                Log($"Removed Camera from {e.Name}");
            }
        }
    }

    static void DrawModifierMenu(Entity e, Rectangle anchor)
    {
        var panel = new Rectangle(anchor.X, anchor.Y + anchor.Height + 6, anchor.Width, ModifierPanelH);

        Raylib.DrawRectangleRec(panel, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(panel, 1, Theme.Border);
        Raylib.DrawRectangle((int)panel.X, (int)panel.Y, 3, (int)panel.Height, Theme.Accent);
        Ui.Text("ADD COMPONENT", panel.X + 12, panel.Y + 8, 11, Theme.Dim);
        Raylib.DrawLine((int)panel.X + 1, (int)(panel.Y + ModifierHeadH - 2),
                        (int)(panel.X + panel.Width - 1), (int)(panel.Y + ModifierHeadH - 2), Theme.Border);

        (string Name, string Desc, Color Accent, bool Has)[] entries =
        {
            ("Physics Object", "Gravity, mass, spin and collisions", Theme.Orange, e.Physics != null),
            ("Collider",       "Box, circle, triangle or hexagon",   new Color(80, 210, 235, 255), e.Collider != null), 
            ("Visual Script", "Add a visual script to this object", new Color(80, 80, 235, 255), e.VisualScript != null),
            ("Camera", "Follow this object during Play", new Color(80, 180, 230, 255), e.Camera != null)
        };

        int toggle = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            var (name, desc, accent, has) = entries[i];
            var row = new Rectangle(panel.X + 8, panel.Y + ModifierHeadH + i * (ModifierRowH + ModifierRowGap),
                                    panel.Width - 16, ModifierRowH);
            bool hover = Ui.Hit(row);

            Raylib.DrawRectangleRec(row, hover ? Theme.Hover : Theme.Button);
            Raylib.DrawRectangleLinesEx(row, 1, Theme.Border);
            Raylib.DrawRectangle((int)row.X, (int)row.Y, 4, (int)row.Height, has ? accent : Theme.Dim);

            Ui.Text(name, row.X + 14, row.Y + 7, 14, Theme.Text);
            Ui.Text(desc, row.X + 14, row.Y + 25, 11, hover ? Theme.Text : Theme.Dim);

            var chip = new Rectangle(row.X + row.Width - 66, row.Y + (row.Height - 20) / 2f, 56, 20);
            Raylib.DrawRectangleRec(chip, has ? new Color(150, 60, 60, 255) : Theme.Accent);
            Raylib.DrawRectangleLinesEx(chip, 1, Theme.Border);
            Ui.TextCentered(has ? "Remove" : "Add", chip, 12, Theme.Text);

            if (hover && Ui.Pressed && !Ui.IsDragging && !Ui.IsEditing) toggle = i;
        }

        if (toggle >= 0)
        {
            ToggleComponent(e, toggle);
            _modifierMenuOpen = false;
        }
        else if (Ui.Pressed && !Ui.Hit(panel) && !Ui.Hit(anchor))
        {
            _modifierMenuOpen = false;
        }
    }

    static void Section(string title, ref float y)
    {
        Raylib.DrawRectangle((int)_inspector.X + 4, (int)y, (int)_inspector.Width - 8, 22, Theme.Header);
        Ui.Text(title, _inspector.X + 12, y + 4, 14, Theme.Text);
        y += 30;
    }

    static bool Vec2Row(string label, float x, float y, ref Vector2 v, float speed, float min, float max)
    {
        float vx = v.X, vy = v.Y;
        Ui.Text(label, x, y + 3, 14, Theme.Text);

        Ui.Text("X", x + 76, y + 3, 14, new Color(230, 90, 90, 255));
        bool a = Ui.DragFloat(new Rectangle(x + 90, y, 82, 22), ref vx, speed, min, max);
        Ui.Text("Y", x + 178, y + 3, 14, new Color(90, 200, 90, 255));
        bool b = Ui.DragFloat(new Rectangle(x + 192, y, 82, 22), ref vy, speed, min, max);

        if (a || b) v = new Vector2(vx, vy);
        return a || b;
    }

    static void EntityValueRow(string label, float x, float y, float width, VsValue value)
    {
        Ui.Text(label, x, y + 4, 14, Theme.Text);
        var previous = new Rectangle(x + 76, y, 22, 22);
        var next = new Rectangle(x + width - 22, y, 22, 22);
        var name = new Rectangle(x + 102, y, width - 102 - 26, 22);
        int index = value.E == null ? 0 : Entities.FindIndex(e => ReferenceEquals(e, value.E)) + 1;
        string text = value.E == null
            ? "None"
            : index > 0 ? value.E.Name : "(missing)";

        Raylib.DrawRectangleRec(name, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(name, 1, Theme.Border);
        Ui.TextCentered(FitText(text, name.Width - 8, 14), name, 14, Theme.Text);

        int direction = Ui.Button(previous, "<") ? -1 : Ui.Button(next, ">") ? 1 : 0;
        if (direction == 0) return;

        int count = Entities.Count + 1;
        int position = ((index + direction) % count + count) % count;
        value.E = position == 0 ? null : Entities[position - 1];
    }

    static string FitText(string text, float width, int size)
    {
        if (Raylib.MeasureText(text, size) <= width) return text;
        while (text.Length > 1 && Raylib.MeasureText(text + "...", size) > width)
            text = text[..^1];
        return text + "...";
    }

    static void DrawBottomPanel()
    {
        Panel(_bottom, _assets ? "Projects" : "Console");

        var tab = new Rectangle(_bottom.X + 80, _bottom.Y + 3, 70, 20);
        if (Ui.Button(tab, _assets ? "Console" : "Projects"))
        {
            _assets = !_assets;
            if (_assets) RefreshAssets();
        }

        if (_assets && Ui.Button(new Rectangle(_bottom.X + 158, _bottom.Y + 3, 50, 20), "New"))
            NewProject();

        var body = new Rectangle(_bottom.X + 1, _bottom.Y + 27, _bottom.Width - 2, _bottom.Height - 28);
        Raylib.BeginScissorMode((int)body.X, (int)body.Y, (int)body.Width, (int)body.Height);
        if (_assets) DrawSceneList(body);
        else DrawLog(body);
        Raylib.EndScissorMode();
    }

    static void DrawLog(Rectangle body)
    {
        int maxLines = (int)((body.Height - 8) / 16f);
        int start = Math.Max(0, LogLines.Count - maxLines);
        float y = body.Y + 6;
        for (int i = start; i < LogLines.Count; i++)
        {
            Ui.Text(LogLines[i], body.X + 10, y, 12, Theme.Dim);
            y += 16;
        }
    }

    static void DrawSceneList(Rectangle body)
    {
        if (_assetFiles.Length == 0)
        {
            Ui.Text("No saved Projects yet. Name your project in the Hierarchy and press Ctrl+S.", body.X + 10, body.Y + 8, 12, Theme.Dim);
            return;
        }

        float y = body.Y + 4;
        foreach (string f in _assetFiles)
        {
            var row = new Rectangle(body.X, y, body.Width, 22);
            bool hover = Ui.Hit(row) && Ui.Hit(body);
            bool current = f == _projectName;

            if (current) Raylib.DrawRectangleRec(row, Theme.Accent);
            else if (hover) Raylib.DrawRectangleRec(row, Theme.Hover);

            Ui.Text(current ? f + "  (current)" : f, row.X + 12, row.Y + 4, 14, Theme.Text);
            if (hover && Ui.Pressed && !Ui.IsDragging) LoadScene(f);
            y += 22;
        }
    }
}