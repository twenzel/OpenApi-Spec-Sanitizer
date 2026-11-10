// Copyright David Turland 2023. Licensed under the Apache License, Version 2.0.
using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace OpenApiSpecSanitizer.Tests;

public sealed class MigrationTests
{
    private static Specification Yaml(string text) => SpecificationLoader.Parse(text, SpecificationFormat.Yaml);
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("less_simple.yaml")]
    [InlineData("less_simple.json")]
    public async Task FixtureAnalysis_PreservesExpectedUnusedAndUndefinedPaths(string filename)
    {
        var specification = await new SpecificationLoader().LoadAsync(Fixture(filename));
        var result = new Analyzer().Analyze(specification.Document);
        Assert.Equal(new[] { "/components/parameters/unusedParameter", "/components/schemas/schemaA" }, result.UnusedComponents);
        Assert.Equal(new[] { "/components/responses/undefinedA" }, result.UndefinedComponents);
    }

    [Fact]
    public async Task ExampleFixture_HasNoUnusedOrUndefinedComponents()
    {
        var specification = await new SpecificationLoader().LoadAsync(Fixture("api-with-examples.yaml"));
        var result = new Analyzer().Analyze(specification.Document);
        Assert.Empty(result.UnusedComponents);
        Assert.Empty(result.UndefinedComponents);
    }

    [Fact]
    public async Task SimpleFixture_IgnoresUndefinedReferencesInUnusedComponents()
    {
        var spec = await new SpecificationLoader().LoadAsync(Fixture("simple.yaml"));
        var result = new Analyzer().Analyze(spec.Document);
        Assert.Equal(new[] { "/components/requestBodies/requestBodyAUnused" }, result.UnusedComponents);
        Assert.Equal(new[] { "/components/requestBodies/requestBodyMissingRequired", "/components/schemas/schemaPlainMissingUnused" },
            result.UndefinedComponents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cycles_AreUnusedUntilReachableFromRoot(bool reachable)
    {
        var spec = Yaml("openapi: 3.0.0\ncomponents:\n  schemas:\n    A:\n      $ref: '#/components/schemas/B'\n    B:\n      $ref: '#/components/schemas/A'\n");
        if (reachable) spec.Document["paths"] = JsonNode.Parse("{\"/test\":{\"$ref\":\"#/components/schemas/A\"}}");
        var result = new Analyzer().Analyze(spec.Document);
        Assert.Equal(reachable ? Array.Empty<string>() : new[] { "/components/schemas/A", "/components/schemas/B" }, result.UnusedComponents);
        Assert.Empty(result.UndefinedComponents);
    }

    [Fact]
    public void PointerAndSecurity_PreserveEscapedAndImplicitReferences()
    {
        var spec = Yaml("openapi: 3.1.0\nsecurity:\n  - auth: []\npaths:\n  /a:\n    $ref: '#/components/schemas/A~1B~0C/properties/id'\ncomponents:\n  securitySchemes:\n    auth:\n      type: http\n      scheme: bearer\n  schemas:\n    A/B~C:\n      properties:\n        id:\n          type: string\n");
        var result = new Analyzer().Analyze(spec.Document);
        Assert.Empty(result.UnusedComponents);
        Assert.Empty(result.UndefinedComponents);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("'2.0'")]
    public void Swagger_SupportsQuotedAndNumericVersionAndSecurity(string version)
    {
        var spec = Yaml($"swagger: {version}\nsecurity:\n  - auth: []\npaths:\n  /a:\n    $ref: '#/definitions/Used'\ndefinitions:\n  Used:\n    type: string\n  Unused:\n    type: number\nsecurityDefinitions:\n  auth:\n    type: basic\n");
        var result = new Analyzer().Analyze(spec.Document);
        Assert.Equal(new[] { "/definitions/Unused" }, result.UnusedComponents);
        Assert.Empty(result.UndefinedComponents);
    }

    [Theory]
    [InlineData("openapi: 4.0.0")]
    [InlineData("openapi: .3.0.0")]
    [InlineData("swagger: 1.9")]
    [InlineData("info: {}")]
    public void Versions_RejectInvalidOrMissingVersion(string text) =>
        Assert.Throws<InvalidDataException>(() => new Analyzer().Analyze(Yaml(text).Document));

    [Theory]
    [InlineData("../file.yaml#/components/schemas/A")]
    [InlineData("other.json#/A")]
    [InlineData("https://example.com/spec#/A")]
    [InlineData("#anchor")]
    public void References_RejectUnsupportedTargets(string reference)
    {
        var doc = Yaml("openapi: 3.0.0").Document;
        doc["$ref"] = reference;
        Assert.Throws<NotSupportedException>(() => new Analyzer().Analyze(doc));
    }

    [Fact]
    public void Sanitization_RemovesOrTagsWithoutChangingOriginal()
    {
        var spec = Yaml("openapi: 3.0.0\ncomponents:\n  schemas:\n    A/B:\n      type: string\n");
        var analysis = new Analyzer().Analyze(spec.Document);
        var tagged = new Sanitizer().Sanitize(spec.Document, analysis, tag: "x-unused");
        Assert.True(tagged["components"]!["schemas"]!["A/B"]!["x-unused"]!.GetValue<bool>());
        var removed = new Sanitizer().Sanitize(spec.Document, analysis, SanitizeMode.Remove);
        Assert.Empty((JsonObject)removed["components"]!["schemas"]!);
        Assert.Equal("string", spec.Document["components"]!["schemas"]!["A/B"]!["type"]!.GetValue<string>());
        Assert.Null(spec.Document["components"]!["schemas"]!["A/B"]!["x-unused"]);
    }

    [Fact]
    public void Sanitization_RefusesUndefinedReferences()
    {
        var spec = Yaml("openapi: 3.0.0\npaths:\n  /a:\n    $ref: '#/missing'\n");
        var result = new Analyzer().Analyze(spec.Document);
        Assert.Equal(new[] { "/missing" }, result.UndefinedComponents);
        Assert.Throws<InvalidDataException>(() => new Sanitizer().Sanitize(spec.Document, result));
    }

    [Theory]
    [InlineData(SpecificationFormat.Yaml)]
    [InlineData(SpecificationFormat.Json)]
    public async Task OutputSafety_RoundTripsTypesAndNeverOverwrites(SpecificationFormat format)
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, format == SpecificationFormat.Json ? "spec.json" : "spec.yaml");
            var doc = JsonNode.Parse("{\"openapi\":\"3.0.0\",\"info\":{\"version\":\"1.0\",\"title\":\"true\"},\"enabled\":true,\"count\":42,\"empty\":null}")!.AsObject();
            var spec = new Specification(doc, format, source);
            File.WriteAllText(source, "original");
            var dumper = new SpecificationDumper();
            Assert.Throws<IOException>(() => dumper.Dump(spec, doc, source));
            Assert.Equal("original", File.ReadAllText(source));
            var output = dumper.Dump(spec, doc);
            Assert.EndsWith(format == SpecificationFormat.Json ? "spec.san.json" : "spec.san.yaml", output);
            var loaded = await new SpecificationLoader().LoadAsync(output);
            Assert.True(JsonNode.DeepEquals(doc, loaded.Document));
            Assert.Throws<IOException>(() => dumper.Dump(spec, doc));
            Assert.Throws<IOException>(() => dumper.Dump(spec with { SourcePath = null }, doc));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Loader_LoadsHttpUrlWithQueryUsingPathExtension()
    {
        using var client = new HttpClient(new ResponseHandler());
        var spec = await new SpecificationLoader(client).LoadAsync("https://example.test/spec.yaml?version=1");
        Assert.Equal(SpecificationFormat.Yaml, spec.Format);
        Assert.Equal("3.0.0", spec.Document["openapi"]!.GetValue<string>());
        Assert.Null(spec.SourcePath);
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("openapi: 3.0.0") });
    }

    [Theory]
    [InlineData("-h", 0)]
    [InlineData("--version", 0)]
    [InlineData("--bogus", 2)]
    [InlineData("-o", 2)]
    public async Task Cli_HandlesHelpVersionAndInvalidArguments(string option, int expected)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(expected, await Program.RunAsync(new[] { option }, output, error));
        Assert.Contains(expected == 0 ? (option == "-h" ? "Usage:" : "1.0.0") : "Error:",
            expected == 0 ? output.ToString() : error.ToString());
    }

    [Fact]
    public async Task Cli_StrictLaxAndUndefinedHaveDistinctExitCodes()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await Program.RunAsync(new[] { Fixture("simple_unused.yaml") }, output, error));
        Assert.Contains("/components/requestBodies/requestBodyAUnused", output.ToString());
        Assert.Equal(0, await Program.RunAsync(new[] { Fixture("simple_unused.yaml"), "-l" }, output, error));
        Assert.Equal(2, await Program.RunAsync(new[] { Fixture("simple.yaml"), "-l" }, output, error));
        Assert.Contains("Undefined references:", error.ToString());
        Assert.Equal(2, await Program.RunAsync(new[] { Fixture("simple_unused.yaml"), "-r", "-t", "x" }, output, error));
    }

    [Fact]
    public async Task Cli_RemoveWritesSanitizedCopyOnlyWhenRequested()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml");
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Assert.Equal(0, await Program.RunAsync(new[] { Fixture("simple_unused.yaml"), "-l", "-o", path }, output, error));
            Assert.False(File.Exists(path));
            Assert.Equal(0, await Program.RunAsync(new[] { Fixture("simple_unused.yaml"), "-s", "-l", "-r", "-o", path }, output, error));
            var saved = await new SpecificationLoader().LoadAsync(path);
            Assert.Empty(new Analyzer().Analyze(saved.Document).UnusedComponents);
            Assert.False(saved.Document["components"]!["requestBodies"]!.AsObject().ContainsKey("requestBodyAUnused"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AllComponentCategories_AreAnalyzedAndAnalyzerCanBeReused()
    {
        var analyzer = new Analyzer();
        var doc = Yaml("openapi: 3.1.1").Document;
        var groups = new JsonObject();
        doc["components"] = groups;
        var categories = new[] { "schemas", "parameters", "responses", "requestBodies", "headers", "examples", "links", "callbacks", "securitySchemes", "pathItems" };
        foreach (var category in categories) groups[category] = new JsonObject { ["Unused"] = new JsonObject() };
        Assert.Equal(categories.Select(c => "/components/" + c + "/Unused").OrderBy(p => p, StringComparer.Ordinal),
            analyzer.Analyze(doc).UnusedComponents);
        var next = analyzer.Analyze(Yaml("openapi: 3.0.0").Document);
        Assert.Empty(next.UnusedComponents);
        Assert.Empty(next.UndefinedComponents);
    }

    [Fact]
    public void PercentEncodedReferences_ResolveWithoutChangingComponentNames()
    {
        var doc = Yaml("openapi: 3.0.0\npaths:\n  /a:\n    $ref: '#/components/schemas/A%20B'\ncomponents:\n  schemas:\n    A B:\n      type: string\n").Document;
        var result = new Analyzer().Analyze(doc);
        Assert.Empty(result.UnusedComponents);
        Assert.Empty(result.UndefinedComponents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cli_TagWritesDefaultOrCustomPropertyInJson(bool custom)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var source = "{\"openapi\":\"3.0.0\",\"components\":{\"schemas\":{\"Unused\":{\"type\":\"string\"}}}}";
        var args = new List<string> { "-j", source, "-s", "-l", "-o", path };
        if (custom) args.AddRange(new[] { "-t", "x-unused" });
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Assert.Equal(0, await Program.RunAsync(args.ToArray(), output, error));
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.True(saved["components"]!["schemas"]!["Unused"]![custom ? "x-unused" : "unused"]!.GetValue<bool>());
            Assert.Equal("string", saved["components"]!["schemas"]!["Unused"]!["type"]!.GetValue<string>());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Loader_RejectsMalformedAndNonObjectInput()
    {
        Assert.ThrowsAny<YamlDotNet.Core.YamlException>(() => Yaml("unbalanced brackets: ]["));
        Assert.Throws<InvalidDataException>(() => Yaml("- one\n- two"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SpecificationLoader.Parse("{", SpecificationFormat.Json));
        await Assert.ThrowsAsync<IOException>(() => new SpecificationLoader().LoadAsync("nonexistent.yaml"));
    }
}
