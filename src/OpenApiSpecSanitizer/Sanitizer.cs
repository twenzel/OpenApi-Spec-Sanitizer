// Copyright David Turland 2023. Licensed under the Apache License, Version 2.0.
using System.Text.Json.Nodes;

namespace OpenApiSpecSanitizer;

public enum SanitizeMode { Tag, Remove }

public sealed class Sanitizer
{
    public JsonObject Sanitize(JsonObject document, AnalysisResult analysis, SanitizeMode mode = SanitizeMode.Tag,
        string tag = "unused")
    {
        if (analysis.UndefinedComponents.Count > 0)
            throw new InvalidDataException("Cannot sanitize a specification with undefined references.");
        if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("Tag cannot be empty.", nameof(tag));
        var result = (JsonObject)document.DeepClone();
        foreach (var path in analysis.UnusedComponents)
        {
            var parts = path.Split('/').Skip(1).Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();
            JsonNode? parent = result;
            foreach (var part in parts.SkipLast(1)) parent = parent?[part];
            if (parent is not JsonObject map) throw new InvalidDataException($"Component not found: {path}");
            if (mode == SanitizeMode.Remove) map.Remove(parts[^1]);
            else if (map[parts[^1]] is JsonObject component) component[tag] = true;
            else throw new InvalidDataException($"Cannot tag a non-object component: {path}");
        }
        return result;
    }
}
