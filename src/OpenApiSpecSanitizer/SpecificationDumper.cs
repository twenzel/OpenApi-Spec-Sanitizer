// Copyright David Turland 2023. Licensed under the Apache License, Version 2.0.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace OpenApiSpecSanitizer;

public sealed class SpecificationDumper
{
    public string Dump(Specification specification, JsonObject document, string? outputPath = null)
    {
        if (outputPath is null)
        {
            var source = specification.SourcePath ?? throw new IOException("URL or inline input requires --output.");
            outputPath = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source)
                + ".san" + Path.GetExtension(source));
        }
        var text = specification.Format == SpecificationFormat.Json
            ? document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine
            : new SerializerBuilder().WithQuotingNecessaryStrings().DisableAliases().Build().Serialize(ToObject(document));
        // CreateNew makes overwrite protection atomic, including input files and symbolic links.
        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
        return Path.GetFullPath(outputPath);
    }

    private static object? ToObject(JsonNode? node) => node switch
    {
        JsonObject map => map.ToDictionary(p => p.Key, p => ToObject(p.Value)),
        JsonArray list => list.Select(ToObject).ToList(),
        null => null,
        _ => JsonSerializer.Deserialize<object>(node.ToJsonString()) is JsonElement value ? Element(value) : null
    };

    private static object? Element(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var number) ? (object)number : value.GetDouble(),
        _ => null
    };
}
