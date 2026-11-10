// Copyright David Turland 2023. Licensed under the Apache License, Version 2.0.
using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace OpenApiSpecSanitizer;

public enum SpecificationFormat { Yaml, Json }

public sealed record Specification(JsonObject Document, SpecificationFormat Format, string? SourcePath);

public sealed class SpecificationLoader
{
    private static readonly HttpClient Client = new();
    private readonly HttpClient client;

    public SpecificationLoader(HttpClient? client = null) => this.client = client ?? Client;

    public async Task<Specification> LoadAsync(string source, SpecificationFormat? inlineFormat = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(source))
            return Parse(await File.ReadAllTextAsync(source, cancellationToken), FormatFor(source), Path.GetFullPath(source));
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            return Parse(await client.GetStringAsync(uri, cancellationToken), FormatFor(uri.AbsolutePath));
        if (inlineFormat.HasValue)
            return Parse(source, inlineFormat.Value);
        throw new IOException($"Input file not found or unsupported URL: {source}");
    }

    public static SpecificationFormat FormatFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".yaml" or ".yml" => SpecificationFormat.Yaml,
        ".json" => SpecificationFormat.Json,
        _ => throw new InvalidDataException("Use a .yaml, .yml, or .json specification.")
    };

    public static Specification Parse(string text, SpecificationFormat format, string? sourcePath = null)
    {
        JsonNode? node = format == SpecificationFormat.Json
            ? JsonNode.Parse(text)
            : ToJson(new DeserializerBuilder().WithDuplicateKeyChecking()
                .WithAttemptingUnquotedStringTypeDeserialization().Build().Deserialize<object>(text));
        return new Specification(node as JsonObject ?? throw new InvalidDataException("Specification must be an object."),
            format, sourcePath);
    }

    private static JsonNode? ToJson(object? value) => ToJson(value, 0);

    private static JsonNode? ToJson(object? value, int depth)
    {
        if (depth > 128) throw new InvalidDataException("YAML nesting exceeds 128 levels or contains cyclic aliases.");
        if (value is IDictionary map)
        {
            var result = new JsonObject();
            foreach (DictionaryEntry entry in map)
                result.Add(Convert.ToString(entry.Key, CultureInfo.InvariantCulture)
                    ?? throw new InvalidDataException("Null mapping key."), ToJson(entry.Value, depth + 1));
            return result;
        }
        if (value is IList list)
        {
            var result = new JsonArray();
            foreach (var item in list) result.Add(ToJson(item, depth + 1));
            return result;
        }
        return JsonSerializer.SerializeToNode(value);
    }
}
