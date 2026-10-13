using System.Numerics;
using Raylib_cs;

namespace SmoothieEngine;

public enum NodeKind { Start, Update, Key, Condition, And, Move, Rotate, Scale, NewObject, ChangeVar, Math, MakeVector2, GetPosition }
public enum KeyTrigger { Held, Pressed, Released }
public enum CondType { KeyHeld, XGreater, XLess, YGreater, YLess, FloatGreater, FloatLess, BoolTrue, BoolFalse }
public enum MathOperation { Add, Subtract, Multiply, Divide }
public enum VariableChangeMode { Add, Subtract, Set }
public enum VarType { Float, Bool, Vector2, Key, Entity }

public sealed class VsVariable
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "var";
    public VarType Type;

    public VsVariable Clone() => (VsVariable)MemberwiseClone();
}

public sealed class VsValue
{
    public float F;
    public bool B;
    public Vector2 V;
    public KeyboardKey K = KeyboardKey.Space;
    public Entity? E;

    public VsValue Clone() => (VsValue)MemberwiseClone();
}

public sealed class VsNode
{
    public string Name = "";
    public NodeKind Kind;
    public Rectangle Rect;
    public bool TargetSelf = true;
    public Entity? Target;
    public KeyboardKey Key = KeyboardKey.Right;
    public KeyTrigger Trigger = KeyTrigger.Held;
    public CondType Check = CondType.KeyHeld;
    public MathOperation MathOp = MathOperation.Add;
    public VariableChangeMode ChangeMode = VariableChangeMode.Add;
    public float Value;
    public Vector2 Amount;
    public bool PerSecond = true;
    public bool Circle = true;
    public float Flash;
    public string VarTarget = "";
    public string VarKey = "";
    public string VarValue = "";
    public string VarAmount = "";

    public VsNode Clone() => (VsNode)MemberwiseClone();
}

public sealed class VsLink
{
    public VsNode A = null!, B = null!;
    public int Port;
}

public sealed class VisualScript
{
    public string Name = "Visual Script";
    public List<ScriptInstance> Scripts = new();
    public IReadOnlyList<VsNode> Nodes => Scripts.SelectMany(s => s.Graph.Nodes).ToList();

    public void AddExampleScript()
    {
        var graph = new VsGraph { Name = "Example Script" };

        var start = new VsNode
        {
            Name = "Start",
            Kind = NodeKind.Start,
            Rect = new Rectangle(0f, 0f, 170f, 76f)
        };

        var move = new VsNode
        {
            Name = "Move",
            Kind = NodeKind.Move,
            Rect = new Rectangle(260f, 40f, 170f, 76f),
            Amount = new Vector2(200f, 0f),
            PerSecond = true,
            TargetSelf = true
        };

        graph.Nodes.Add(start);
        graph.Nodes.Add(move);
        graph.Links.Add(new VsLink { A = start, B = move, Port = 0 });
        Scripts.Add(new ScriptInstance { Graph = graph, Enabled = true });
    }

    public VisualScript Clone() => new()
    {
        Name = Name,
        Scripts = CloneScripts(Scripts)
    };

    static List<ScriptInstance> CloneScripts(IEnumerable<ScriptInstance> scripts)
    {
        var graphCopies = new Dictionary<VsGraph, VsGraph>();
        return scripts.Select(s => s.Clone(graphCopies)).ToList();
    }
}

public sealed class VsGraph
{
    public string Name = "Script";
    public List<VsVariable> Variables = new();
    public List<VsNode> Nodes = new();
    public List<VsLink> Links = new();
    public Dictionary<NodeKind, int> Counters = new();
    public Vector2 Pan;

    public VsGraph Clone()
    {
        var clone = new VsGraph
        {
            Name = Name,
            Variables = Variables.Select(v => v.Clone()).ToList(),
            Counters = new Dictionary<NodeKind, int>(Counters),
            Pan = Pan
        };
        var nodeCopies = new Dictionary<VsNode, VsNode>();
        foreach (VsNode node in Nodes)
        {
            VsNode copy = node.Clone();
            nodeCopies.Add(node, copy);
            clone.Nodes.Add(copy);
        }
        foreach (VsLink link in Links)
            if (nodeCopies.TryGetValue(link.A, out VsNode? a) && nodeCopies.TryGetValue(link.B, out VsNode? b))
                clone.Links.Add(new VsLink { A = a, B = b, Port = link.Port });
        return clone;
    }
}

public sealed class ScriptInstance
{
    public VsGraph Graph = null!;
    public bool Enabled = true;
    public bool Started;
    public Dictionary<string, VsValue> Values = new();

    public ScriptInstance Clone(Dictionary<VsGraph, VsGraph>? graphCopies = null)
    {
        graphCopies ??= new Dictionary<VsGraph, VsGraph>();
        if (!graphCopies.TryGetValue(Graph, out VsGraph? graph))
        {
            graph = Graph.Clone();
            graphCopies.Add(Graph, graph);
        }

        return new ScriptInstance
        {
            Graph = graph,
            Enabled = Enabled,
            Values = Values.ToDictionary(kv => kv.Key, kv => kv.Value.Clone())
        };
    }

    public void Sync()
    {
        foreach (VsVariable v in Graph.Variables)
            if (!Values.ContainsKey(v.Id)) Values[v.Id] = new VsValue();

        if (Values.Count == Graph.Variables.Count) return;

        foreach (string k in Values.Keys.Where(k => Graph.Variables.All(v => v.Id != k)).ToList())
            Values.Remove(k);
    }
}
