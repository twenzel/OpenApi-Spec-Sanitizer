// Copyright David Turland 2023. Licensed under the Apache License, Version 2.0.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenApiSpecSanitizer;

public sealed record AnalysisResult(IReadOnlyList<string> UnusedComponents, IReadOnlyList<string> UndefinedComponents,
    IReadOnlyList<string> RequiredPaths);

public sealed class Analyzer
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "schemas", "parameters", "responses", "requestBodies", "headers", "examples", "links", "callbacks",
        "securitySchemes", "pathItems"
    };

    public AnalysisResult Analyze(JsonObject document)
    {
        var swagger = document["swagger"]?.ToString();
        if (swagger is not null ? swagger != "2.0" && !(document["swagger"] is JsonValue version &&
            version.TryGetValue<double>(out var number) && number == 2.0) :
            !Regex.IsMatch(document["openapi"]?.ToString() ?? "", @"^3\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$"))
            throw new InvalidDataException("Expected Swagger 2.0 or OpenAPI 3.x.y.");

        var components = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (swagger is not null)
        {
            foreach (var category in new[] { "definitions", "parameters", "responses", "securityDefinitions" })
                Declare(document[category], "/" + category);
        }
        else if (document["components"] is JsonObject groups)
            foreach (var group in groups.Where(g => Categories.Contains(g.Key)))
                Declare(group.Value, "/components/" + Escape(group.Key));

        // Each node points to its children and local reference target. Component declarations
        // are excluded from the root walk, so isolated dependency cycles remain unused.
        var nodes = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        Walk(document, "");
        var required = new HashSet<string>(StringComparer.Ordinal);
        var undefined = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue("");
        while (queue.TryDequeue(out var path))
        {
            if (!required.Add(path)) continue;
            if (!nodes.ContainsKey(path)) { undefined.Add(path); continue; }
            var ancestor = path;
            while (ancestor.LastIndexOf('/') is var slash && slash >= 0)
            {
                ancestor = ancestor[..slash];
                if (components.ContainsKey(ancestor)) { queue.Enqueue(ancestor); break; }
            }
            foreach (var target in edges[path]) queue.Enqueue(target);
        }
        return new AnalysisResult(components.Keys.Where(p => !required.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            undefined.OrderBy(p => p, StringComparer.Ordinal).ToArray(), required.OrderBy(p => p, StringComparer.Ordinal).ToArray());

        void Declare(JsonNode? group, string prefix)
        {
            if (group is JsonObject map)
                foreach (var entry in map) components.Add(prefix + "/" + Escape(entry.Key), entry.Value);
        }

        void Walk(JsonNode? node, string path)
        {
            nodes.Add(path, node);
            var targets = new List<string>();
            edges.Add(path, targets);
            if (node is JsonObject map)
            {
                foreach (var entry in map)
                {
                    var child = path + "/" + Escape(entry.Key);
                    Walk(entry.Value, child);
                    if (!components.ContainsKey(child)) targets.Add(child);
                    if (entry.Key == "$ref")
                    {
                        if (entry.Value is not JsonValue scalar || !scalar.TryGetValue<string>(out var reference))
                            throw new InvalidDataException($"$ref at {path} must be a string.");
                        if (!reference.StartsWith('#'))
                            throw new NotSupportedException($"External reference at {path}: {reference}");
                        var pointer = Uri.UnescapeDataString(reference[1..]);
                        if (pointer.Length > 0 && !pointer.StartsWith('/'))
                            throw new NotSupportedException($"Only local JSON Pointer references are supported: {reference}");
                        targets.Add(pointer);
                    }
                    if (entry.Key == "security" && entry.Value is JsonArray requirements)
                        foreach (var requirement in requirements.OfType<JsonObject>())
                            foreach (var name in requirement.Select(r => r.Key))
                                targets.Add((swagger is null ? "/components/securitySchemes/" : "/securityDefinitions/") + Escape(name));
                }
            }
            else if (node is JsonArray array)
                for (var i = 0; i < array.Count; i++)
                {
                    var child = path + "/" + i;
                    Walk(array[i], child);
                    targets.Add(child);
                }
        }
    }

    internal static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
}
