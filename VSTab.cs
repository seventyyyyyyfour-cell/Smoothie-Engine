using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace SmoothieEngine;

public sealed class VsNodeDto
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public bool TargetSelf { get; set; } = true;
    public int Target { get; set; } = -1;
    public string Key { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string Check { get; set; } = "";
    public string MathOp { get; set; } = "";
    public string ChangeMode { get; set; } = "";
    public float Value { get; set; }
    public float AmountX { get; set; }
    public float AmountY { get; set; }
    public bool PerSecond { get; set; } = true;
    public bool Circle { get; set; } = true;
    public string VarTarget { get; set; } = "";
    public string VarKey { get; set; } = "";
    public string VarValue { get; set; } = "";
    public string VarAmount { get; set; } = "";
}

public sealed class VsLinkDto
{
    public int A { get; set; }
    public int B { get; set; }
    public int Port { get; set; }
}

public sealed class VsVariableDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

public sealed class VsValueDto
{
    public string Id { get; set; } = "";
    public float F { get; set; }
    public bool B { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string Key { get; set; } = "";
    public int Entity { get; set; } = -1;
}

public sealed class VsInstanceDto
{
    public int Entity { get; set; } = -1;
    public int Graph { get; set; } = -1;
    public bool Enabled { get; set; } = true;
    public List<VsValueDto> Values { get; set; } = new();
}

public sealed class VsGraphItemDto
{
    public string Name { get; set; } = "";
    public float[] Pan { get; set; } = { 0f, 0f };
    public List<VsVariableDto> Variables { get; set; } = new();
    public List<VsNodeDto> Nodes { get; set; } = new();
    public List<VsLinkDto> Links { get; set; } = new();
}

public sealed class VsGraphDto
{
    public List<VsGraphItemDto> Graphs { get; set; } = new();
    public List<VsInstanceDto> Instances { get; set; } = new();
}

public static class VsTab
{
    public static IReadOnlyList<Entity>? Entities;
    public static Action<string>? Log;
    public static Action<bool, Vector2>? SpawnObject;
    public static readonly List<VsGraph> Library = new();

    const float BarH = 36f;
    const float PanelW = 240f;
    const float NodeW = 170f;
    const float NodeH = 76f;
    const float TitleH = 22f;

    static readonly Color PortTrue = new(80, 200, 110, 255);
    static readonly Color PortFalse = new(220, 90, 90, 255);

    static readonly Dictionary<NodeKind, Color> KindColors = new()
    {
        [NodeKind.Start]     = new Color(46, 150, 90, 255),
        [NodeKind.Update]    = new Color(40, 150, 160, 255),
        [NodeKind.Key]       = new Color(140, 90, 180, 255),
        [NodeKind.Condition] = new Color(190, 150, 40, 255),
        [NodeKind.And]       = new Color(200, 130, 60, 255),
        [NodeKind.Move]      = new Color(44, 93, 135, 255),
        [NodeKind.Rotate]    = new Color(190, 80, 130, 255),
        [NodeKind.Scale]     = new Color(100, 180, 100, 255),
        [NodeKind.NewObject] = new Color(180, 180, 180, 255),
        [NodeKind.ChangeVar] = new Color(170, 120, 55, 255),
        [NodeKind.Math] = new Color(125, 95, 175, 255),
        [NodeKind.MakeVector2] = new Color(55, 150, 155, 255),
        [NodeKind.GetPosition] = new Color(55, 150, 155, 255)
    };

    sealed class Ctx
    {
        public readonly Entity Self;
        public readonly ScriptInstance Inst;
        public readonly VsGraph Graph;

        public Ctx(Entity self, ScriptInstance inst)
        {
            Self = self;
            Inst = inst;
            Graph = inst.Graph;
        }
    }

    static VsGraph? _g;
    static VsNode? _selected;
    static VsNode? _dragNode;
    static VsNode? _linkFrom;
    static int _linkPort;
    static Vector2 _grab;
    static bool _panning;
    static object? _capOwner;
    static bool _running;

    static Vector2 Pan
    {
        get => _g?.Pan ?? Vector2.Zero;
        set { if (_g != null) _g.Pan = value; }
    }

    public static VsGraph? Current => _g;
    public static bool Capturing => _capOwner != null;

    public static void OpenForEntity(Entity? entity)
    {
        if (entity == null)
        {
            Open(null);
            return;
        }

        if (entity.VisualScript != null)
        {
            if (entity.VisualScript.Scripts.Count == 0)
                entity.VisualScript.AddExampleScript();

            ScriptInstance? inst = entity.VisualScript.Scripts.FirstOrDefault();
            Open(inst?.Graph);
            return;
        }

        if (entity.Scripts.Count == 0)
        {
            Open(null);
            return;
        }

        Open(entity.Scripts.First().Graph);
    }

    public static void CancelCapture() => _capOwner = null;

    public static VsGraph NewGraph()
    {
        var g = new VsGraph { Name = $"Script {Library.Count + 1}" };
        Library.Add(g);
        return g;
    }

    public static void Open(VsGraph? g)
    {
        if (ReferenceEquals(g, _g)) return;
        _g = g;
        _selected = null;
        _dragNode = null;
        _linkFrom = null;
        _capOwner = null;
        _panning = false;
        Ui.CancelEdit();
    }

    public static void Start()
    {
        if (Entities != null)
            foreach (Entity e in Entities)
                foreach (ScriptInstance s in e.Scripts)
                    s.Started = false;
        _running = true;
    }

    public static void Stop()
    {
        _running = false;
        foreach (VsGraph g in Library)
            foreach (VsNode n in g.Nodes)
                n.Flash = 0f;
    }

    public static void Tick(float dt)
    {
        if (!_running || Entities == null) return;

        foreach (VsGraph g in Library) DecayGraph(g, dt);

        bool typing = Ui.IsEditing || Capturing;

        foreach (Entity e in Entities.ToArray())
        {
            if (!e.Active) continue;
            foreach (ScriptInstance inst in e.Scripts.ToArray())
            {
                if (!inst.Enabled) continue;
                inst.Sync();

                var ctx = new Ctx(e, inst);
                bool first = !inst.Started;
                inst.Started = true;

                foreach (VsNode n in inst.Graph.Nodes.ToArray())
                {
                    bool fire = n.Kind switch
                    {
                        NodeKind.Start  => first,
                        NodeKind.Update => true,
                        NodeKind.Key    => !typing && KeyFires(n, ctx),
                        _               => false
                    };
                    if (fire) Run(n, ctx, dt, 0, new HashSet<VsNode>());
                }
            }
        }
    }

    static VsValue? Var(Ctx c, string id) =>
        id.Length > 0 && c.Inst.Values.TryGetValue(id, out VsValue? v) ? v : null;

    static Entity? ValidEntity(Entity? e)
    {
        if (e == null || Entities == null) return null;
        for (int i = 0; i < Entities.Count; i++)
            if (ReferenceEquals(Entities[i], e)) return e;
        return null;
    }

    static Entity? ResolveTarget(VsNode n, Ctx c)
    {
        VsValue? v = Var(c, n.VarTarget);
        if (v != null) return ValidEntity(v.E);
        return n.TargetSelf ? c.Self : ValidEntity(n.Target);
    }

    static float ResolveValue(VsNode n, Ctx c) => Var(c, n.VarValue)?.F ?? n.Value;
    static Vector2 ResolveAmount(VsNode n, Ctx c) => Var(c, n.VarAmount)?.V ?? n.Amount;
    static KeyboardKey ResolveKey(VsNode n, Ctx c) => Var(c, n.VarKey)?.K ?? n.Key;

    static bool KeyFires(VsNode n, Ctx c)
    {
        KeyboardKey k = ResolveKey(n, c);
        return n.Trigger switch
        {
            KeyTrigger.Held    => Raylib.IsKeyDown(k),
            KeyTrigger.Pressed => Raylib.IsKeyPressed(k),
            _                  => Raylib.IsKeyReleased(k)
        };
    }

    static void Run(VsNode n, Ctx c, float dt, int depth, HashSet<VsNode> seen)
    {
        if (depth > 64 || !seen.Add(n)) return;
        n.Flash = 0.25f;

        int port = 0;
        Entity? t = ResolveTarget(n, c);

        switch (n.Kind)
        {
            case NodeKind.Condition:
                port = Evaluate(n, c) ? 0 : 1;
                break;
            case NodeKind.And:
                port = EvaluateAnd(n, c, 0) ? 0 : 1;
                break;
            case NodeKind.NewObject:
                SpawnObject?.Invoke(n.Circle, ResolveAmount(n, c));
                port = 0;
                break;
            case NodeKind.ChangeVar:
            {
                VsVariable? variable = c.Graph.Variables.Find(v => v.Id == n.VarValue);
                if (variable != null && c.Inst.Values.TryGetValue(variable.Id, out VsValue? value))
                {
                    if (n.ChangeMode == VariableChangeMode.Set)
                    {
                        VsValue? source = Var(c, n.VarAmount);
                        if (source != null && variable.Type == c.Graph.Variables.Find(v => v.Id == n.VarAmount)?.Type)
                        {
                            value.F = source.F;
                            value.B = source.B;
                            value.V = source.V;
                            value.K = source.K;
                            value.E = source.E;
                        }
                    }
                    else if (variable.Type == VarType.Float)
                    {
                        float amount = n.ChangeMode == VariableChangeMode.Subtract ? -n.Value : n.Value;
                        value.F += amount;
                    }
                    else if (variable.Type == VarType.Vector2)
                    {
                        Vector2 amount = n.ChangeMode == VariableChangeMode.Subtract ? -n.Amount : n.Amount;
                        value.V += amount;
                    }
                }
                break;
            }
            case NodeKind.Math:
            {
                VsValue? a = Var(c, n.VarAmount);
                VsValue? b = Var(c, n.VarTarget);
                VsValue? output = Var(c, n.VarValue);
                if (a != null && b != null && output != null)
                {
                    float result = n.MathOp switch
                    {
                        MathOperation.Add => a.F + b.F,
                        MathOperation.Subtract => a.F - b.F,
                        MathOperation.Multiply => a.F * b.F,
                        MathOperation.Divide when b.F != 0f => a.F / b.F,
                        _ => output.F
                    };
                    if (float.IsFinite(result)) output.F = result;
                }
                break;
            }
            case NodeKind.MakeVector2:
            {
                VsValue? x = Var(c, n.VarAmount);
                VsValue? y = Var(c, n.VarTarget);
                VsValue? output = Var(c, n.VarValue);
                if (x != null && y != null && output != null)
                    output.V = new Vector2(x.F, y.F);
                break;
            }
            case NodeKind.GetPosition:
            {
                VsValue? output = Var(c, n.VarValue);
                if (t != null && output != null)
                    output.V = t.Position;
                break;
            }
            case NodeKind.Move:
                if (t != null) t.Position += ResolveAmount(n, c) * (n.PerSecond ? dt : 1f);
                break;
            case NodeKind.Rotate:
                if (t != null) t.Rotation += ResolveValue(n, c) * (n.PerSecond ? dt : 1f);
                break;
            case NodeKind.Scale:
                if (t != null) t.Scale += ResolveAmount(n, c) * (n.PerSecond ? dt : 1f);
                break;
        }

        foreach (VsLink l in c.Graph.Links)
            if (l.A == n && l.Port == port && !IsInputLink(l)) Run(l.B, c, dt, depth + 1, seen);
    }

    static bool Evaluate(VsNode n, Ctx c)
    {
        if (n.Check is CondType.BoolTrue or CondType.BoolFalse)
        {
            VsValue? value = Var(c, n.VarAmount);
            if (value == null) return false;
            return n.Check == CondType.BoolTrue ? value.B : !value.B;
        }

        if (n.Check is CondType.FloatGreater or CondType.FloatLess)
        {
            VsValue? value = Var(c, n.VarAmount);
            if (value == null) return false;

            float threshold = ResolveValue(n, c);
            return n.Check == CondType.FloatGreater
                ? value.F > threshold
                : value.F < threshold;
        }

        Entity? t = ResolveTarget(n, c);
        float v = ResolveValue(n, c);
        return n.Check switch
        {
            CondType.KeyHeld  => Raylib.IsKeyDown(ResolveKey(n, c)),
            CondType.XGreater => t != null && t.Position.X > v,
            CondType.XLess    => t != null && t.Position.X < v,
            CondType.YGreater => t != null && t.Position.Y > v,
            _                 => t != null && t.Position.Y < v
        };
    }

    static bool IsInputLink(VsLink l) =>
        l.B.Kind == NodeKind.And && (l.A.Kind is NodeKind.Condition or NodeKind.And);

    static int InputCount(VsGraph g, VsNode n) => g.Links.Count(l => l.B == n && IsInputLink(l));

    static bool EvalNode(VsNode n, Ctx c, int depth)
    {
        if (depth > 16) return false;
        return n.Kind switch
        {
            NodeKind.Condition => Evaluate(n, c),
            NodeKind.And       => EvaluateAnd(n, c, depth + 1),
            _                  => false
        };
    }

    static bool EvaluateAnd(VsNode n, Ctx c, int depth)
    {
        bool any = false;
        foreach (VsLink l in c.Graph.Links)
        {
            if (l.B != n || !IsInputLink(l)) continue;
            any = true;
            bool v = EvalNode(l.A, c, depth);
            if (l.Port != 0) v = !v;
            if (!v) return false;
        }
        return any;
    }

    static void DecayGraph(VsGraph g, float dt)
    {
        foreach (VsNode n in g.Nodes)
            if (n.Flash > 0f) n.Flash = MathF.Max(0f, n.Flash - dt);
    }

    static string TargetName(VsNode n)
    {
        if (n.TargetSelf) return "Self";
        if (n.Target == null) return "None";
        return ValidEntity(n.Target) == null ? "(missing)" : n.Target.Name;
    }

    static string? Bound(string id)
    {
        if (id.Length == 0 || _g == null) return null;
        VsVariable? v = _g.Variables.Find(x => x.Id == id);
        return v?.Name;
    }

    static string TargetLabel(VsNode n)
    {
        string? b = Bound(n.VarTarget);
        return b != null ? "$" + b : TargetName(n);
    }

    static string KeyLabel(VsNode n)
    {
        string? b = Bound(n.VarKey);
        return b != null ? "$" + b : n.Key.ToString();
    }

    static string ValueLabel(VsNode n)
    {
        string? b = Bound(n.VarValue);
        return b != null ? "$" + b : F(n.Value);
    }

    static string AmountLabel(VsNode n)
    {
        string? b = Bound(n.VarAmount);
        return b != null ? "$" + b : $"{F(n.Amount.X)}, {F(n.Amount.Y)}";
    }

    static void StepTarget(VsNode n, int dir)
    {
        int count = (Entities?.Count ?? 0) + 2;
        int idx = 0;
        if (!n.TargetSelf)
        {
            idx = 1;
            if (n.Target != null && Entities != null)
                for (int i = 0; i < Entities.Count; i++)
                    if (ReferenceEquals(Entities[i], n.Target)) idx = i + 2;
        }

        int pos = ((idx + dir) % count + count) % count;
        n.TargetSelf = pos == 0;
        n.Target = pos >= 2 && Entities != null ? Entities[pos - 2] : null;
    }

    static T Next<T>(T v) where T : struct, Enum
    {
        T[] all = Enum.GetValues<T>();
        return all[(Array.IndexOf(all, v) + 1) % all.Length];
    }

    static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static Color Lighten(Color c, int amount) =>
        new((byte)Math.Min(255, c.R + amount), (byte)Math.Min(255, c.G + amount), (byte)Math.Min(255, c.B + amount), c.A);

    static string Fit(string s, float width, int size)
    {
        if (Raylib.MeasureText(s, size) <= width) return s;
        while (s.Length > 1 && Raylib.MeasureText(s + "...", size) > width) s = s[..^1];
        return s + "...";
    }

    static string CondLabel(CondType c) => c switch
    {
        CondType.KeyHeld  => "Key Held",
        CondType.XGreater => "X >",
        CondType.XLess    => "X <",
        CondType.YGreater => "Y >",
        CondType.YLess    => "Y <",
        CondType.FloatGreater => "Float >",
        CondType.FloatLess => "Float <",
        CondType.BoolTrue => "Bool True",
        _ => "Bool False"
    };

    static (string, string) Describe(VsNode n)
    {
        string mode = n.PerSecond ? "/s" : "once";
        return n.Kind switch
        {
            NodeKind.Start     => ("Runs once on Play", ""),
            NodeKind.Update    => ("Runs every frame", ""),
            NodeKind.Key       => ($"Key: {KeyLabel(n)}", n.Trigger.ToString()),
            NodeKind.And       => ("All inputs true", $"{InputCount(_g!, n)} input(s)"),
            NodeKind.Condition => n.Check switch
            {
                CondType.KeyHeld => ("Key held?", KeyLabel(n)),
                CondType.FloatGreater or CondType.FloatLess =>
                    ($"${Bound(n.VarAmount) ?? "?"}", $"{CondLabel(n.Check)} {ValueLabel(n)}"),
                CondType.BoolTrue or CondType.BoolFalse =>
                    ($"${Bound(n.VarAmount) ?? "?"}", CondLabel(n.Check)),
                _ => (TargetLabel(n), $"{CondLabel(n.Check)} {ValueLabel(n)}")
            },
            NodeKind.Scale     => (TargetLabel(n), $"{AmountLabel(n)} {mode}"),
            NodeKind.Move      => (TargetLabel(n), $"{AmountLabel(n)} {mode}"),
            NodeKind.NewObject => ("Spawns on trigger", n.Circle ? "Circle" : "Square"),
            NodeKind.ChangeVar => (Bound(n.VarValue) ?? "Select a variable",
                n.ChangeMode == VariableChangeMode.Set
                    ? $"= {Bound(n.VarAmount) ?? "Select source"}"
                    : $"{n.ChangeMode} {(_g!.Variables.Find(v => v.Id == n.VarValue)?.Type == VarType.Vector2 ? $"{F(n.Amount.X)}, {F(n.Amount.Y)}" : F(n.Value))}"),
            NodeKind.Math => ($"{Bound(n.VarValue) ?? "Select output"} =",
                $"{Bound(n.VarAmount) ?? "A"} {MathSymbol(n.MathOp)} {Bound(n.VarTarget) ?? "B"}"),
            NodeKind.MakeVector2 => ($"{Bound(n.VarValue) ?? "Select output"} =",
                $"({Bound(n.VarAmount) ?? "X"}, {Bound(n.VarTarget) ?? "Y"})"),
            NodeKind.GetPosition => ($"{Bound(n.VarValue) ?? "Select output"} =",
                $"Position of {TargetLabel(n)}"),
            _                  => (TargetLabel(n), $"{ValueLabel(n)} deg {mode}")
        };
    }

    static string MathSymbol(MathOperation operation) => operation switch
    {
        MathOperation.Add => "+",
        MathOperation.Subtract => "-",
        MathOperation.Multiply => "*",
        _ => "/"
    };

    public static void Clear()
    {
        Library.Clear();
        _g = null;
        _selected = null;
        _dragNode = null;
        _linkFrom = null;
        _capOwner = null;
        _panning = false;
        Ui.CancelEdit();
    }

    static int IndexOf(IReadOnlyList<Entity> list, Entity? e)
    {
        if (e == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], e)) return i;
        return -1;
    }

    public static VsGraphDto Export(IReadOnlyList<Entity> entities)
    {
        foreach (Entity e in entities)
            foreach (ScriptInstance s in e.Scripts)
                if (!Library.Contains(s.Graph)) Library.Add(s.Graph);

        var dto = new VsGraphDto();

        foreach (VsGraph g in Library)
        {
            var gd = new VsGraphItemDto { Name = g.Name, Pan = new[] { g.Pan.X, g.Pan.Y } };

            foreach (VsVariable v in g.Variables)
                gd.Variables.Add(new VsVariableDto { Id = v.Id, Name = v.Name, Type = v.Type.ToString() });

            foreach (VsNode n in g.Nodes)
            {
                gd.Nodes.Add(new VsNodeDto
                {
                    Name = n.Name,
                    Kind = n.Kind.ToString(),
                    X = n.Rect.X,
                    Y = n.Rect.Y,
                    TargetSelf = n.TargetSelf,
                    Target = IndexOf(entities, n.Target),
                    Key = n.Key.ToString(),
                    Trigger = n.Trigger.ToString(),
                    Check = n.Check.ToString(),
                    MathOp = n.MathOp.ToString(),
                    ChangeMode = n.ChangeMode.ToString(),
                    Value = n.Value,
                    AmountX = n.Amount.X,
                    AmountY = n.Amount.Y,
                    PerSecond = n.PerSecond,
                    Circle = n.Circle,
                    VarTarget = n.VarTarget,
                    VarKey = n.VarKey,
                    VarValue = n.VarValue,
                    VarAmount = n.VarAmount
                });
            }

            foreach (VsLink l in g.Links)
            {
                int a = g.Nodes.IndexOf(l.A), b = g.Nodes.IndexOf(l.B);
                if (a >= 0 && b >= 0) gd.Links.Add(new VsLinkDto { A = a, B = b, Port = l.Port });
            }

            dto.Graphs.Add(gd);
        }

        for (int i = 0; i < entities.Count; i++)
        {
            foreach (ScriptInstance s in entities[i].Scripts)
            {
                var id = new VsInstanceDto { Entity = i, Graph = Library.IndexOf(s.Graph), Enabled = s.Enabled };
                foreach ((string key, VsValue v) in s.Values)
                {
                    id.Values.Add(new VsValueDto
                    {
                        Id = key,
                        F = v.F,
                        B = v.B,
                        X = v.V.X,
                        Y = v.V.Y,
                        Key = v.K.ToString(),
                        Entity = IndexOf(entities, v.E)
                    });
                }
                dto.Instances.Add(id);
            }
        }

        return dto;
    }

    public static void Import(VsGraphDto? dto, IReadOnlyList<Entity> entities)
    {
        Clear();
        foreach (Entity e in entities) e.Scripts.Clear();
        if (dto == null) return;

        foreach (VsGraphItemDto gd in dto.Graphs)
        {
            var g = new VsGraph
            {
                Name = string.IsNullOrWhiteSpace(gd.Name) ? $"Script {Library.Count + 1}" : gd.Name,
                Pan = gd.Pan is { Length: >= 2 } ? new Vector2(gd.Pan[0], gd.Pan[1]) : Vector2.Zero
            };

            foreach (VsVariableDto vd in gd.Variables)
            {
                g.Variables.Add(new VsVariable
                {
                    Id = string.IsNullOrEmpty(vd.Id) ? Guid.NewGuid().ToString("N") : vd.Id,
                    Name = vd.Name,
                    Type = Enum.TryParse(vd.Type, true, out VarType vt) ? vt : VarType.Float
                });
            }

            var made = new List<VsNode?>();
            foreach (VsNodeDto d in gd.Nodes)
            {
                if (!Enum.TryParse(d.Kind, true, out NodeKind kind))
                {
                    made.Add(null);
                    continue;
                }

                var node = new VsNode
                {
                    Name = string.IsNullOrWhiteSpace(d.Name) ? kind.ToString() : d.Name,
                    Kind = kind,
                    Rect = new Rectangle(d.X, d.Y, NodeW, NodeH),
                    TargetSelf = d.TargetSelf && d.Target < 0,
                    Target = d.Target >= 0 && d.Target < entities.Count ? entities[d.Target] : null,
                    Key = Enum.TryParse(d.Key, true, out KeyboardKey key) ? key : KeyboardKey.Right,
                    Trigger = Enum.TryParse(d.Trigger, true, out KeyTrigger trigger) ? trigger : KeyTrigger.Held,
                    Check = Enum.TryParse(d.Check, true, out CondType check) ? check : CondType.KeyHeld,
                    MathOp = Enum.TryParse(d.MathOp, true, out MathOperation mathOp) ? mathOp : MathOperation.Add,
                    ChangeMode = Enum.TryParse(d.ChangeMode, true, out VariableChangeMode changeMode)
                        ? changeMode
                        : VariableChangeMode.Add,
                    Value = d.Value,
                    Amount = new Vector2(d.AmountX, d.AmountY),
                    PerSecond = d.PerSecond,
                    Circle = d.Circle,
                    VarTarget = d.VarTarget ?? "",
                    VarKey = d.VarKey ?? "",
                    VarValue = d.VarValue ?? "",
                    VarAmount = d.VarAmount ?? ""
                };

                g.Nodes.Add(node);
                made.Add(node);
                g.Counters.TryGetValue(kind, out int count);
                g.Counters[kind] = count + 1;
            }

            foreach (VsLinkDto l in gd.Links)
            {
                if (l.A < 0 || l.B < 0 || l.A >= made.Count || l.B >= made.Count || l.A == l.B) continue;
                VsNode? a = made[l.A], b = made[l.B];
                if (a == null || b == null) continue;
                g.Links.Add(new VsLink { A = a, B = b, Port = l.Port == 1 ? 1 : 0 });
            }

            Library.Add(g);
        }

        foreach (VsInstanceDto id in dto.Instances)
        {
            if (id.Entity < 0 || id.Entity >= entities.Count) continue;
            if (id.Graph < 0 || id.Graph >= Library.Count) continue;

            var inst = new ScriptInstance { Graph = Library[id.Graph], Enabled = id.Enabled };
            foreach (VsValueDto vd in id.Values)
            {
                inst.Values[vd.Id] = new VsValue
                {
                    F = vd.F,
                    B = vd.B,
                    V = new Vector2(vd.X, vd.Y),
                    K = Enum.TryParse(vd.Key, true, out KeyboardKey k) ? k : KeyboardKey.Space,
                    E = vd.Entity >= 0 && vd.Entity < entities.Count ? entities[vd.Entity] : null
                };
            }

            inst.Sync();
            Entity owner = entities[id.Entity];
            owner.VisualScript ??= new VisualScript();
            owner.Scripts.Add(inst);
        }
    }

    static VsNode Create(NodeKind kind, float x, float y)
    {
        VsGraph g = _g!;
        g.Counters.TryGetValue(kind, out int count);
        g.Counters[kind] = ++count;

        var node = new VsNode
        {
            Name = $"{kind} {count}",
            Kind = kind,
            Rect = new Rectangle(x, y, NodeW, NodeH)
        };

        switch (kind)
        {
            case NodeKind.Condition:
                node.Key = KeyboardKey.Space;
                break;
            case NodeKind.Move:
                node.Amount = new Vector2(200f, 0f);
                break;
            case NodeKind.Rotate:
                node.Value = 90f;
                break;
            case NodeKind.Scale:
                node.Amount = new Vector2(1.5f, 1.5f);
                break;
            case NodeKind.NewObject:
                node.Amount = new Vector2(1f, 1f);
                break;
        }

        g.Nodes.Add(node);
        return node;
    }

    static void AddNode(NodeKind kind, Rectangle canvas)
    {
        if (_g == null) return;
        float step = (_g.Nodes.Count % 6) * 24f;
        float x = canvas.Width / 2f - NodeW / 2f - Pan.X + step;
        float y = canvas.Height / 2f - NodeH / 2f - Pan.Y + step;
        VsNode n = Create(kind, x, y);
        Select(n);
        Log?.Invoke($"VS: added {n.Name}");
    }

    static void Select(VsNode? n)
    {
        if (!ReferenceEquals(n, _selected))
        {
            _capOwner = null;
            Ui.CancelEdit();
        }
        _selected = n;
    }

    static void DeleteNode(VsNode n)
    {
        _g!.Links.RemoveAll(l => l.A == n || l.B == n);
        _g.Nodes.Remove(n);
        if (_selected == n) _selected = null;
        if (_linkFrom == n) _linkFrom = null;
        if (_dragNode == n) _dragNode = null;
        _capOwner = null;
        Ui.CancelEdit();
        Log?.Invoke($"VS: deleted {n.Name}");
    }

    static void DuplicateSelected()
    {
        if (_g == null || _selected == null) return;

        VsNode source = _selected;
        VsNode copy = source.Clone();
        copy.Name += " Copy";
        copy.Rect.X += 32f;
        copy.Rect.Y += 32f;
        copy.Flash = 0f;
        _g.Nodes.Add(copy);
        Select(copy);
        Log?.Invoke($"VS: duplicated {source.Name}");
    }

    static void TryLink(VsNode a, VsNode b, int port)
    {
        _linkFrom = null;
        List<VsLink> links = _g!.Links;

        if (b.Kind is NodeKind.Start or NodeKind.Update or NodeKind.Key)
        {
            Log?.Invoke($"VS: {b.Name} is an event node and can't receive links");
            return;
        }

        VsLink? same = links.Find(l => l.A == a && l.B == b);
        if (same != null && same.Port == port)
        {
            links.Remove(same);
            Log?.Invoke($"VS: disconnected {a.Name} -> {b.Name}");
            return;
        }
        if (same != null)
        {
            same.Port = port;
            Log?.Invoke($"VS: {a.Name} -> {b.Name} now on {(port == 0 ? "True" : "False")}");
            return;
        }

        links.RemoveAll(l => l.A == b && l.B == a);
        links.Add(new VsLink { A = a, B = b, Port = port });
        Log?.Invoke($"VS: connected {a.Name} -> {b.Name}");
    }

    static Color LinkColor(VsNode from, int port) =>
        (from.Kind == NodeKind.Condition || from.Kind == NodeKind.And) ? (port == 0 ? PortTrue : PortFalse) : Theme.Orange;

    static void DrawArrow(Vector2 from, Vector2 to, Color color)
    {
        Vector2 mid = (from + to) * 0.5f;
        Vector2 dir = to - from;
        if (dir.LengthSquared() < 1f) return;
        dir = Vector2.Normalize(dir);
        Vector2 perp = new(-dir.Y, dir.X);

        const float len = 16f, half = 9f;
        Vector2 tip = mid + dir * (len * 0.5f);
        Vector2 baseCenter = mid - dir * (len * 0.5f);
        Vector2 l = baseCenter + perp * half;
        Vector2 r = baseCenter - perp * half;

        Raylib.DrawTriangle(tip, l, r, color);
        Raylib.DrawTriangle(tip, r, l, color);
    }

    public static void Draw(Rectangle area)
    {
        Raylib.DrawRectangleRec(area, new Color(30, 30, 30, 255));

        if (_g == null)
        {
            Ui.Text("Select an object with a Visual Script modifier, or add one in the Inspector.",
                area.X + 16, area.Y + 52, 14, Theme.Dim);
            return;
        }

        VsGraph g = _g;
        if (!_running) DecayGraph(g, Raylib.GetFrameTime());

        Rectangle canvas = new(area.X, area.Y + BarH, area.Width, MathF.Max(0f, area.Height - BarH));
        Rectangle panel = new(area.X + area.Width - PanelW, canvas.Y, PanelW, canvas.Height);
        Vector2 origin = new Vector2(canvas.X, canvas.Y) + Pan;
        bool inCanvas = Ui.Hit(canvas) && !Ui.Hit(panel);

        Rectangle Screen(VsNode n) => new(origin.X + n.Rect.X, origin.Y + n.Rect.Y, n.Rect.Width, n.Rect.Height);
        Vector2 Center(VsNode n)
        {
            Rectangle s = Screen(n);
            return new Vector2(s.X + s.Width / 2f, s.Y + s.Height / 2f);
        }
        VsNode? Top()
        {
            for (int i = g.Nodes.Count - 1; i >= 0; i--)
                if (Ui.Hit(Screen(g.Nodes[i]))) return g.Nodes[i];
            return null;
        }

        bool controlDown = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (_selected != null && !Ui.IsEditing && !Capturing)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Delete))
                DeleteNode(_selected);
            else if (controlDown && Raylib.IsKeyPressed(KeyboardKey.D))
                DuplicateSelected();
        }

        VsNode? hovered = inCanvas ? Top() : null;

        if (Ui.Pressed && inCanvas && !Ui.IsDragging)
        {
            if (hovered != null)
            {
                Select(hovered);
                _dragNode = hovered;
                Rectangle s = Screen(hovered);
                _grab = Ui.Mouse - new Vector2(s.X, s.Y);
                g.Nodes.Remove(hovered);
                g.Nodes.Add(hovered);
            }
            else
            {
                Select(null);
                _panning = true;
            }
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Middle) && inCanvas) _panning = true;

        if (_panning)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left) || Raylib.IsMouseButtonDown(MouseButton.Middle))
            {
                Pan += Raylib.GetMouseDelta();
                origin = new Vector2(canvas.X, canvas.Y) + Pan;
            }
            else _panning = false;
        }

        if (_dragNode != null)
        {
            if (Ui.Down)
            {
                Vector2 p = Ui.Mouse - _grab - origin;
                _dragNode.Rect.X = p.X;
                _dragNode.Rect.Y = p.Y;
            }
            else _dragNode = null;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Right) && inCanvas)
        {
            if (hovered == null || hovered == _linkFrom)
            {
                _linkFrom = null;
            }
            else if (_linkFrom == null)
            {
                _linkFrom = hovered;
                Rectangle s = Screen(hovered);
                bool hasPorts = hovered.Kind == NodeKind.Condition || hovered.Kind == NodeKind.And;
                _linkPort = hasPorts && Ui.Mouse.Y > s.Y + s.Height / 2f ? 1 : 0;
            }
            else
            {
                TryLink(_linkFrom, hovered, _linkPort);
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _linkFrom = null;

        Raylib.BeginScissorMode((int)canvas.X, (int)canvas.Y, (int)canvas.Width, (int)canvas.Height);

        Color grid = new(40, 40, 40, 255);
        float gx = ((Pan.X % 40f) + 40f) % 40f;
        float gy = ((Pan.Y % 40f) + 40f) % 40f;
        for (float x = canvas.X + gx; x < canvas.X + canvas.Width; x += 40f)
            Raylib.DrawLine((int)x, (int)canvas.Y, (int)x, (int)(canvas.Y + canvas.Height), grid);
        for (float y = canvas.Y + gy; y < canvas.Y + canvas.Height; y += 40f)
            Raylib.DrawLine((int)canvas.X, (int)y, (int)(canvas.X + canvas.Width), (int)y, grid);

        foreach (VsLink l in g.Links)
        {
            Vector2 ca = Center(l.A), cb = Center(l.B);
            Color c = LinkColor(l.A, l.Port);
            Raylib.DrawLineEx(ca, cb, 3f, c);
            DrawArrow(ca, cb, c);
        }

        if (_linkFrom != null)
            Raylib.DrawLineEx(Center(_linkFrom), Ui.Mouse, 2f, LinkColor(_linkFrom, _linkPort));

        foreach (VsNode n in g.Nodes)
        {
            Rectangle s = Screen(n);
            Color kc = KindColors[n.Kind];
            bool hot = n == hovered || n == _dragNode;

            Raylib.DrawRectangleRec(s, hot ? Theme.Panel : Theme.PanelDark);
            Raylib.DrawRectangle((int)s.X, (int)s.Y, (int)s.Width, (int)TitleH,
                n.Flash > 0f ? Lighten(kc, 90) : hot ? Lighten(kc, 25) : kc);
            Ui.Text(Fit(n.Name, s.Width - 12, 14), s.X + 6, s.Y + 4, 14, Color.White);

            bool cond = n.Kind is NodeKind.Condition or NodeKind.And;
            float textW = s.Width - (cond ? 34f : 12f);
            (string l1, string l2) = Describe(n);
            Ui.Text(Fit(l1, textW, 12), s.X + 6, s.Y + TitleH + 6, 12, Theme.Text);
            Ui.Text(Fit(l2, textW, 12), s.X + 6, s.Y + TitleH + 22, 12, Theme.Dim);

            if (cond)
            {
                float cy = s.Y + s.Height / 2f;
                Raylib.DrawLine((int)(s.X + s.Width - 24), (int)cy, (int)(s.X + s.Width), (int)cy, Theme.Border);
                Ui.Text("T", s.X + s.Width - 16, s.Y + TitleH + 4, 12, PortTrue);
                Ui.Text("F", s.X + s.Width - 16, s.Y + s.Height - 17, 12, PortFalse);
            }

            Color outline = n == _linkFrom ? Color.White : n == _selected ? Theme.Orange : Theme.Border;
            Raylib.DrawRectangleLinesEx(s, n == _selected || n == _linkFrom ? 2 : 1, outline);
        }

        Ui.Text("LMB drag node = move   LMB drag empty / MMB = pan   Del = delete   Ctrl+D = duplicate node",
            canvas.X + 10, canvas.Y + canvas.Height - 36, 12, Theme.Dim);
        Ui.Text("RMB node, then RMB another = connect (again = disconnect)   Condition/And: RMB top half = True, bottom = False",
            canvas.X + 10, canvas.Y + canvas.Height - 20, 12, Theme.Dim);

        Raylib.EndScissorMode();

        Raylib.DrawRectangle((int)area.X, (int)area.Y, (int)area.Width, (int)BarH, Theme.Panel);
        Raylib.DrawLine((int)area.X, (int)(area.Y + BarH), (int)(area.X + area.Width), (int)(area.Y + BarH), Theme.Border);

        NodeKind[] kinds = Enum.GetValues<NodeKind>();
        float bw = Math.Clamp((area.Width - 16f - 80f) / kinds.Length - 6f, 40f, 100f);
        float bx = area.X + 8f;
        foreach (NodeKind kind in kinds)
        {
            var r = new Rectangle(bx, area.Y + 6f, bw, 24f);
            string label = kind switch
            {
                NodeKind.ChangeVar => "+ Var",
                NodeKind.Math => "+ Math",
                NodeKind.MakeVector2 => "+ Vec2",
                NodeKind.GetPosition => "+ Get Pos",
                _ => "+ " + kind
            };
            if (Ui.Button(r, label))
                AddNode(kind, canvas);
            Raylib.DrawRectangle((int)r.X, (int)(r.Y + r.Height - 3), (int)r.Width, 3, KindColors[kind]);
            bx += bw + 6f;
        }

        if (_running)
            Ui.Text("RUNNING", area.X + area.Width - 72, area.Y + 11, 14, PortTrue);

        if (_selected != null) DrawPanel(_selected, panel);
        else DrawVarPanel(panel);
    }

    public static bool KeyField(Rectangle r, object owner, ref KeyboardKey key)
    {
        if (ReferenceEquals(_capOwner, owner))
        {
            Ui.Button(r, "Press a key...", true);
            int k = (int)Raylib.GetKeyPressed();
            if (k == 0) return false;

            bool changed = k != (int)KeyboardKey.Escape;
            if (changed) key = (KeyboardKey)k;
            _capOwner = null;
            return changed;
        }

        if (Ui.Button(r, key.ToString())) _capOwner = owner;
        return false;
    }

    static void TargetRow(VsNode n, float x, float y, float w)
    {
        Ui.Text("Target", x, y + 4, 14, Theme.Text);
        var prev = new Rectangle(x + 60, y, 22, 22);
        var next = new Rectangle(x + w - 22, y, 22, 22);
        var name = new Rectangle(x + 86, y, w - 86 - 26, 22);

        Raylib.DrawRectangleRec(name, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(name, 1, Theme.Border);
        Ui.TextCentered(Fit(TargetName(n), name.Width - 8, 14), name, 14, Theme.Text);

        if (Ui.Button(prev, "<")) StepTarget(n, -1);
        if (Ui.Button(next, ">")) StepTarget(n, 1);
        DimRow(n.VarTarget.Length > 0, x + 56, y, w - 56);
    }

    static void KeyRow(VsNode n, float x, float y, float w)
    {
        Ui.Text("Key", x, y + 4, 14, Theme.Text);
        KeyboardKey k = n.Key;
        if (KeyField(new Rectangle(x + 70, y, w - 70, 22), n, ref k)) n.Key = k;
        DimRow(n.VarKey.Length > 0, x + 70, y, w - 70);
    }

    static void ValueRow(string label, float x, float y, float w, ref float value, float speed, bool dim)
    {
        Ui.Text(label, x, y + 4, 14, Theme.Text);
        float v = value;
        if (Ui.DragFloat(new Rectangle(x + 70, y, w - 70, 22), ref v, speed, -100000f, 100000f))
            value = v;
        DimRow(dim, x + 70, y, w - 70);
    }

    static void DimRow(bool on, float x, float y, float w)
    {
        if (on) Raylib.DrawRectangle((int)x, (int)y, (int)w, 22, new Color(56, 56, 56, 150));
    }

    static void BindRow(ref string bound, VarType type, float x, float y, float w, string label = "Var")
    {
        Ui.Text(label, x, y + 4, 14, Theme.Text);
        var prev = new Rectangle(x + 60, y, 22, 22);
        var next = new Rectangle(x + w - 22, y, 22, 22);
        var name = new Rectangle(x + 86, y, w - 86 - 26, 22);

        List<VsVariable> options = _g!.Variables.Where(v => v.Type == type).ToList();
        string current = bound;
        int idx = options.FindIndex(v => v.Id == current) + 1;
        string valueLabel = idx > 0 ? options[idx - 1].Name : options.Count == 0 ? "No variables" : "None";

        Raylib.DrawRectangleRec(name, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(name, 1, Theme.Border);
        Ui.TextCentered(Fit(valueLabel, name.Width - 8, 14), name, 14, idx > 0 ? Theme.Orange : Theme.Dim);

        int dir = 0;
        if (Ui.Button(prev, "<")) dir = -1;
        if (Ui.Button(next, ">")) dir = 1;
        if (dir == 0) return;

        int count = options.Count + 1;
        int pos = ((idx + dir) % count + count) % count;
        bound = pos == 0 ? "" : options[pos - 1].Id;
    }

    static void ChangeVariableRow(VsNode n, float x, float y, float w)
    {
        Ui.Text("Var", x, y + 4, 14, Theme.Text);
        var prev = new Rectangle(x + 60, y, 22, 22);
        var next = new Rectangle(x + w - 22, y, 22, 22);
        var name = new Rectangle(x + 86, y, w - 86 - 26, 22);
        List<VsVariable> options = _g!.Variables;
        string current = n.VarValue;
        int idx = options.FindIndex(v => v.Id == current) + 1;
        string label = idx > 0 ? options[idx - 1].Name : options.Count == 0 ? "No variables" : "None";

        Raylib.DrawRectangleRec(name, Theme.PanelDark);
        Raylib.DrawRectangleLinesEx(name, 1, Theme.Border);
        Ui.TextCentered(Fit(label, name.Width - 8, 14), name, 14, idx > 0 ? Theme.Orange : Theme.Dim);

        int dir = 0;
        if (Ui.Button(prev, "<")) dir = -1;
        if (Ui.Button(next, ">")) dir = 1;
        if (dir == 0) return;

        int count = options.Count + 1;
        int pos = ((idx + dir) % count + count) % count;
        n.VarValue = pos == 0 ? "" : options[pos - 1].Id;
    }

    static void ClearBindings(string id)
    {
        foreach (VsNode n in _g!.Nodes)
        {
            if (n.VarTarget == id) n.VarTarget = "";
            if (n.VarKey == id) n.VarKey = "";
            if (n.VarValue == id) n.VarValue = "";
            if (n.VarAmount == id) n.VarAmount = "";
        }
    }

    static void DrawVarPanel(Rectangle panel)
    {
        VsGraph g = _g!;

        Raylib.DrawRectangleRec(panel, Theme.Panel);
        Raylib.DrawRectangleLinesEx(panel, 1, Theme.Border);
        Raylib.DrawRectangle((int)panel.X, (int)panel.Y, (int)panel.Width, 22, Theme.Header);
        Ui.Text("Script", panel.X + 10, panel.Y + 4, 14, Theme.Text);

        float x = panel.X + 12;
        float w = panel.Width - 24;
        float y = panel.Y + 34;

        string name = g.Name;
        if (Ui.TextField(new Rectangle(x, y, w, 22), ref name)) g.Name = name;
        y += 32;

        Ui.Text("Variables", x, y, 14, Theme.Text);
        y += 24;

        VsVariable? remove = null;
        foreach (VsVariable v in g.Variables)
        {
            string vn = v.Name;
            if (Ui.TextField(new Rectangle(x, y, w - 92, 22), ref vn)) v.Name = vn;

            if (Ui.Button(new Rectangle(x + w - 88, y, 62, 22), v.Type.ToString()))
            {
                v.Type = Next(v.Type);
                ClearBindings(v.Id);
            }

            if (Ui.Button(new Rectangle(x + w - 22, y, 22, 22), "x")) remove = v;
            y += 28;
        }

        if (remove != null)
        {
            ClearBindings(remove.Id);
            g.Variables.Remove(remove);
        }

        if (Ui.Button(new Rectangle(x, y + 4, w, 24), "+ Variable"))
            g.Variables.Add(new VsVariable { Name = $"var{g.Variables.Count + 1}" });
        y += 40;

        Ui.Text("Select a node to bind its fields", x, y, 12, Theme.Dim);
        Ui.Text("to variables. Values are set per", x, y + 16, 12, Theme.Dim);
        Ui.Text("object in the Inspector.", x, y + 32, 12, Theme.Dim);
    }

    static void DrawPanel(VsNode n, Rectangle panel)
    {
        Raylib.DrawRectangleRec(panel, Theme.Panel);
        Raylib.DrawRectangleLinesEx(panel, 1, Theme.Border);
        Raylib.DrawRectangle((int)panel.X, (int)panel.Y, (int)panel.Width, 22, Theme.Header);
        Ui.Text("Node", panel.X + 10, panel.Y + 4, 14, Theme.Text);

        float x = panel.X + 12;
        float w = panel.Width - 24;
        float y = panel.Y + 34;

        string name = n.Name;
        if (Ui.TextField(new Rectangle(x, y, w, 22), ref name)) n.Name = name;
        y += 28;

        Ui.Text(n.Kind.ToString(), x, y, 14, KindColors[n.Kind]);
        y += 26;

        switch (n.Kind)
        {
            case NodeKind.Start:
                Ui.Text("Fires once when Play starts.", x, y, 12, Theme.Dim);
                y += 20;
                break;

            case NodeKind.Update:
                Ui.Text("Fires every frame while playing.", x, y, 12, Theme.Dim);
                y += 20;
                break;

            case NodeKind.Key:
                KeyRow(n, x, y, w);
                y += 28;
                BindRow(ref n.VarKey, VarType.Key, x, y, w);
                y += 28;
                Ui.Text("When", x, y + 4, 14, Theme.Text);
                if (Ui.Button(new Rectangle(x + 70, y, w - 70, 22), n.Trigger.ToString()))
                    n.Trigger = Next(n.Trigger);
                y += 28;
                break;

            case NodeKind.Condition:
                Ui.Text("Check", x, y + 4, 14, Theme.Text);
                if (Ui.Button(new Rectangle(x + 70, y, w - 70, 22), CondLabel(n.Check)))
                    n.Check = Next(n.Check);
                y += 28;
                if (n.Check == CondType.KeyHeld)
                {
                    KeyRow(n, x, y, w);
                    y += 28;
                    BindRow(ref n.VarKey, VarType.Key, x, y, w);
                    y += 28;
                }
                else if (n.Check is CondType.FloatGreater or CondType.FloatLess)
                {
                    BindRow(ref n.VarAmount, VarType.Float, x, y, w, "Float");
                    y += 28;
                    ValueRow("Threshold", x, y, w, ref n.Value, 1f, n.VarValue.Length > 0);
                    y += 28;
                    BindRow(ref n.VarValue, VarType.Float, x, y, w, "Var");
                    y += 28;
                }
                else if (n.Check is CondType.BoolTrue or CondType.BoolFalse)
                {
                    BindRow(ref n.VarAmount, VarType.Bool, x, y, w, "Bool");
                    y += 28;
                }
                else
                {
                    TargetRow(n, x, y, w);
                    y += 28;
                    BindRow(ref n.VarTarget, VarType.Entity, x, y, w);
                    y += 28;
                    ValueRow("Value", x, y, w, ref n.Value, 1f, n.VarValue.Length > 0);
                    y += 28;
                    BindRow(ref n.VarValue, VarType.Float, x, y, w);
                    y += 28;
                }
                Ui.Text("True -> green links, False -> red.", x, y, 12, Theme.Dim);
                y += 20;
                break;

            case NodeKind.And:
                Ui.Text($"Inputs linked: {InputCount(_g!, n)}", x, y, 14, Theme.Text);
                y += 22;
                Ui.Text("True only when all inputs pass.", x, y, 12, Theme.Dim);
                y += 16;
                Ui.Text("Input from T port = must be true.", x, y, 12, Theme.Dim);
                y += 16;
                Ui.Text("Input from F port = must be false.", x, y, 12, Theme.Dim);
                y += 20;
                break;

            case NodeKind.Move:
            case NodeKind.Scale:
            {
                TargetRow(n, x, y, w);
                y += 28;
                BindRow(ref n.VarTarget, VarType.Entity, x, y, w);
                y += 28;
                float ax = n.Amount.X, ay = n.Amount.Y;
                bool dim = n.VarAmount.Length > 0;
                ValueRow("X", x, y, w, ref ax, 1f, dim);
                y += 28;
                ValueRow("Y", x, y, w, ref ay, 1f, dim);
                y += 28;
                n.Amount = new Vector2(ax, ay);
                BindRow(ref n.VarAmount, VarType.Vector2, x, y, w);
                y += 28;
                Ui.Text("Mode", x, y + 4, 14, Theme.Text);
                if (Ui.Button(new Rectangle(x + 70, y, w - 70, 22), n.PerSecond ? "Per Second" : "Instant"))
                    n.PerSecond = !n.PerSecond;
                y += 28;
                break;
            }

            case NodeKind.Rotate:
            {
                TargetRow(n, x, y, w);
                y += 28;
                BindRow(ref n.VarTarget, VarType.Entity, x, y, w);
                y += 28;
                ValueRow("Degrees", x, y, w, ref n.Value, 1f, n.VarValue.Length > 0);
                y += 28;
                BindRow(ref n.VarValue, VarType.Float, x, y, w);
                y += 28;
                Ui.Text("Mode", x, y + 4, 14, Theme.Text);
                if (Ui.Button(new Rectangle(x + 70, y, w - 70, 22), n.PerSecond ? "Per Second" : "Instant"))
                    n.PerSecond = !n.PerSecond;
                y += 28;
                break;
            }

            case NodeKind.ChangeVar:
            {
                ChangeVariableRow(n, x, y, w);
                y += 28;
                VsVariable? variable = _g!.Variables.Find(v => v.Id == n.VarValue);
                bool numeric = variable?.Type is VarType.Float or VarType.Vector2;
                if (variable != null && !numeric)
                    n.ChangeMode = VariableChangeMode.Set;
                if (numeric)
                {
                    Ui.Text("Change", x, y + 4, 14, Theme.Text);
                    if (Ui.Button(new Rectangle(x + 70, y, w - 70, 22), n.ChangeMode.ToString()))
                        n.ChangeMode = Next(n.ChangeMode);
                    y += 28;
                }

                if (variable != null && n.ChangeMode == VariableChangeMode.Set)
                {
                    BindRow(ref n.VarAmount, variable.Type, x, y, w, "From");
                    y += 28;
                }
                else if (variable?.Type == VarType.Float)
                {
                    ValueRow("Amount", x, y, w, ref n.Value, 1f, false);
                    y += 28;
                }
                else if (variable?.Type == VarType.Vector2)
                {
                    float ax = n.Amount.X, ay = n.Amount.Y;
                    ValueRow("X amount", x, y, w, ref ax, 1f, false);
                    y += 28;
                    ValueRow("Y amount", x, y, w, ref ay, 1f, false);
                    y += 28;
                    n.Amount = new Vector2(ax, ay);
                }
                Ui.Text(n.ChangeMode == VariableChangeMode.Set
                    ? "Copies another variable of the same type."
                    : "Changes a Float or Vector2 variable by an amount.", x, y, 12, Theme.Dim);
                y += 18;
                break;
            }

            case NodeKind.Math:
            {
                BindRow(ref n.VarAmount, VarType.Float, x, y, w, "A");
                y += 28;
                BindRow(ref n.VarTarget, VarType.Float, x, y, w, "B");
                y += 28;
                Ui.Text("Operation", x, y + 4, 14, Theme.Text);
                if (Ui.Button(new Rectangle(x + 70, y, w - 70, 22), $"{n.MathOp} ({MathSymbol(n.MathOp)})"))
                    n.MathOp = Next(n.MathOp);
                y += 28;
                BindRow(ref n.VarValue, VarType.Float, x, y, w, "Output");
                y += 28;
                Ui.Text("Stores the result in the output variable.", x, y, 12, Theme.Dim);
                y += 18;
                break;
            }

            case NodeKind.MakeVector2:
            {
                BindRow(ref n.VarAmount, VarType.Float, x, y, w, "X");
                y += 28;
                BindRow(ref n.VarTarget, VarType.Float, x, y, w, "Y");
                y += 28;
                BindRow(ref n.VarValue, VarType.Vector2, x, y, w, "Output");
                y += 28;
                Ui.Text("Combines two Float variables into a Vector2.", x, y, 12, Theme.Dim);
                y += 18;
                break;
            }

            case NodeKind.GetPosition:
                TargetRow(n, x, y, w);
                y += 28;
                BindRow(ref n.VarTarget, VarType.Entity, x, y, w);
                y += 28;
                BindRow(ref n.VarValue, VarType.Vector2, x, y, w, "Output");
                y += 28;
                Ui.Text("Stores the target's position in a Vector2.", x, y, 12, Theme.Dim);
                y += 18;
                break;
        }

        float by = MathF.Max(y + 10f, panel.Y + panel.Height - 40f);
        if (Ui.Button(new Rectangle(x, by, w, 28), "Delete Node"))
            DeleteNode(n);
    }
}