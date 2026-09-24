using System.Globalization;
using Cascade.Sim.Core;
using YamlDotNet.RepresentationModel;

namespace Cascade.Sim.Content;

public sealed class ContentException(string message) : Exception(message);

/// <summary>
/// A strict view over one YAML mapping. Every read names a required key; reading a missing key, or leaving
/// a key in the file that nothing read, is an error (D-021). That is how "no tunable number is hard-coded"
/// stays true: a typo in balance.yaml fails loudly instead of silently falling back to a default.
/// Numbers are parsed from their text, never through floating point.
/// </summary>
public sealed class ContentNode
{
    private readonly YamlMappingNode _map;
    private readonly string _file;
    private readonly string _path;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly List<ContentNode> _children = [];

    private ContentNode(YamlMappingNode map, string file, string path)
    {
        _map = map;
        _file = file;
        _path = path;
    }

    public string Path => _path;

    /// <param name="wrapListAs">If the file's top level is a list, present it as a mapping with this one key.</param>
    public static ContentNode LoadFile(string filePath, string? wrapListAs = null)
    {
        if (!File.Exists(filePath)) throw new ContentException($"Content file not found: {filePath}");
        var stream = new YamlStream();
        using (var reader = new StreamReader(filePath))
        {
            try { stream.Load(reader); }
            catch (YamlDotNet.Core.YamlException e)
            {
                throw new ContentException($"{System.IO.Path.GetFileName(filePath)}:{e.Start.Line}: YAML error: {e.Message}");
            }
        }
        var top = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        if (wrapListAs is not null && top is YamlSequenceNode seq)
            top = new YamlMappingNode(new YamlScalarNode(wrapListAs), seq);
        if (top is not YamlMappingNode root)
            throw new ContentException($"{filePath}: expected a YAML {(wrapListAs is null ? "mapping" : "list")} at the top level.");
        return new ContentNode(root, System.IO.Path.GetFileName(filePath), "");
    }

    public bool Has(string key) => _map.Children.ContainsKey(new YamlScalarNode(key));

    public ContentNode Child(string key)
    {
        var node = Get(key);
        if (node is not YamlMappingNode m) throw Error(key, node, "expected a mapping");
        var child = new ContentNode(m, _file, Join(key));
        _children.Add(child);
        return child;
    }

    public IReadOnlyList<ContentNode> List(string key)
    {
        var node = Get(key);
        if (node is not YamlSequenceNode seq) throw Error(key, node, "expected a list");
        var result = new List<ContentNode>(seq.Children.Count);
        for (int i = 0; i < seq.Children.Count; i++)
        {
            if (seq.Children[i] is not YamlMappingNode m) throw Error($"{key}[{i}]", seq.Children[i], "expected a mapping");
            var child = new ContentNode(m, _file, $"{Join(key)}[{i}]");
            _children.Add(child);
            result.Add(child);
        }
        return result;
    }

    /// <summary>Keys of a mapping in file order, for maps of named records (e.g. goods by id). Read each with <see cref="Child"/>.</summary>
    public IReadOnlyList<string> Keys() => _map.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList();

    public string Str(string key) => Scalar(key);

    /// <summary>A list of plain values, e.g. [a, b, c].</summary>
    public IReadOnlyList<string> StrList(string key)
    {
        var node = Get(key);
        if (node is not YamlSequenceNode seq) throw Error(key, node, "expected a list");
        return seq.Children.Select((c, i) => c is YamlScalarNode s && s.Value is not null
            ? s.Value : throw Error($"{key}[{i}]", c, "expected a single value")).ToList();
    }

    public int[] IntList(string key) => ParseList(key, s => int.Parse(s.Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    public Fixed[] FixedList(string key) => ParseList(key, Core.Fixed.Parse);

    private T[] ParseList<T>(string key, Func<string, T> parse)
    {
        var items = StrList(key);
        try { return items.Select(parse).ToArray(); }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw Error(key, Get(key), e.Message);
        }
    }
    public string? OptStr(string key) => Has(key) ? Scalar(key) : null;

    public long Long(string key) => Parse(key, s => long.Parse(s.Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    public int Int(string key) => Parse(key, s => int.Parse(s.Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    public Fixed Fixed(string key) => Parse(key, Core.Fixed.Parse);
    public Fine Fine(string key) => Parse(key, Core.Fine.Parse);
    public DateOnly Date(string key) => Parse(key, s => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture));

    public bool Bool(string key) => Scalar(key) switch
    {
        "true" => true,
        "false" => false,
        var s => throw Error(key, Get(key), $"expected true or false, got '{s}'"),
    };

    /// <summary>Throws if any key in this mapping or its read children was never read.</summary>
    public void EnsureAllUsed()
    {
        var unused = new List<string>();
        foreach (var k in _map.Children.Keys)
        {
            var name = ((YamlScalarNode)k).Value!;
            if (!_used.Contains(name)) unused.Add($"{_file}:{k.Start.Line}: unknown or unused key '{Join(name)}'");
        }
        if (unused.Count > 0) throw new ContentException(string.Join(Environment.NewLine, unused));
        foreach (var c in _children) c.EnsureAllUsed();
    }

    private YamlNode Get(string key)
    {
        if (!_map.Children.TryGetValue(new YamlScalarNode(key), out var node))
            throw new ContentException($"{_file}:{_map.Start.Line}: missing required key '{Join(key)}'");
        _used.Add(key);
        return node;
    }

    private string Scalar(string key)
    {
        var node = Get(key);
        if (node is not YamlScalarNode s || s.Value is null) throw Error(key, node, "expected a single value");
        return s.Value;
    }

    private T Parse<T>(string key, Func<string, T> parse)
    {
        var node = Get(key);
        var text = Scalar(key);
        try { return parse(text); }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw Error(key, node, e.Message);
        }
    }

    private string Join(string key) => _path.Length == 0 ? key : $"{_path}.{key}";

    private ContentException Error(string key, YamlNode node, string what) =>
        new($"{_file}:{node.Start.Line}: '{Join(key)}': {what}");
}
