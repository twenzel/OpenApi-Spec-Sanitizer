// Copyright David Turland 2023. Licensed under the Apache License, Version 2.0.
using System.Text.Json;
using YamlDotNet.Core;

namespace OpenApiSpecSanitizer;

public static class Program
{
    public const string Help = @"Usage: openapi-spec-sanitizer [options] filename
Analyze Swagger 2.0 or OpenAPI 3.x specifications from a file or HTTP(S) URL.
  -s, --sanitize       Write a sanitized copy (default mode: tag)
  -t, --tag TAG        Mark unused components with TAG: true (default: unused)
  -r, --remove         Remove unused components (exclusive with --tag)
  -o, --output PATH    Output path; defaults to <input>.san.<extension>
  -l, --lax            Allow unused components; never ignore undefined references
  -y, --yaml           Allow inline YAML as filename
  -j, --json           Allow inline JSON as filename (exclusive with --yaml)
  -q, --quiet          Suppress reports and progress
  -v, --verbose        Show progress
  -g, --debug          Include required paths and exception details
  -h, --help           Show help
      --version        Show version
Exit codes: 0 success, 1 unused components in strict mode, 2 invalid input or failure.
External references are unsupported. Analysis alone never writes files.";

    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out, Console.Error);

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        var debug = args.Contains("--debug") || args.Contains("-g");
        try
        {
            if (args.Contains("--help") || args.Contains("-h")) { await output.WriteLineAsync(Help); return 0; }
            if (args.Contains("--version")) { await output.WriteLineAsync("openapi-spec-sanitizer 1.0.0"); return 0; }
            string? source = null, destination = null;
            string tag = "unused";
            bool sanitize = false, remove = false, explicitTag = false, lax = false, quiet = false;
            SpecificationFormat? format = null;
            bool positionalOnly = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {arg}.");
                if (arg == "--" && !positionalOnly) { positionalOnly = true; continue; }
                if (!positionalOnly && arg.StartsWith('-'))
                    switch (arg)
                    {
                        case "-s": case "--sanitize": sanitize = true; break;
                        case "-r": case "--remove": remove = true; break;
                        case "-t": case "--tag": explicitTag = true; tag = Value(); break;
                        case "-o": case "--output": destination = Value(); break;
                        case "-l": case "--lax": lax = true; break;
                        case "-q": case "--quiet": quiet = true; break;
                        case "-g": case "--debug": case "-v": case "--verbose": break;
                        case "-y": case "--yaml": SetFormat(SpecificationFormat.Yaml); break;
                        case "-j": case "--json": SetFormat(SpecificationFormat.Json); break;
                        default: throw new ArgumentException($"Unknown option: {arg}");
                    }
                else if (source is null) source = arg;
                else throw new ArgumentException("Only one input specification is supported.");
            }
            if (source is null) throw new ArgumentException("An input specification is required. Use --help for usage.");
            if (remove && explicitTag) throw new ArgumentException("--remove and --tag are mutually exclusive.");
            if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("Tag cannot be empty.");
            var specification = await new SpecificationLoader().LoadAsync(source, format);
            var analysis = new Analyzer().Analyze(specification.Document);
            if (!quiet)
            {
                await output.WriteLineAsync("---------------- Analyzer Report ----------------");
                await output.WriteLineAsync("Undefined components:");
                foreach (var path in analysis.UndefinedComponents) await output.WriteLineAsync("  " + path);
                await output.WriteLineAsync("Unused components:");
                foreach (var path in analysis.UnusedComponents) await output.WriteLineAsync("  " + path);
                if (debug)
                {
                    await output.WriteLineAsync("Required paths:");
                    foreach (var path in analysis.RequiredPaths) await output.WriteLineAsync("  " + path);
                }
            }
            if (analysis.UndefinedComponents.Count > 0)
                throw new InvalidDataException("Undefined references: " + string.Join(", ", analysis.UndefinedComponents));
            if (analysis.UnusedComponents.Count > 0 && !lax) return 1;
            if (sanitize)
            {
                var document = new Sanitizer().Sanitize(specification.Document, analysis,
                    remove ? SanitizeMode.Remove : SanitizeMode.Tag, tag);
                var path = new SpecificationDumper().Dump(specification, document, destination);
                if (!quiet) await output.WriteLineAsync("Saved " + path);
            }
            return 0;

            void SetFormat(SpecificationFormat value)
            {
                if (format.HasValue && format != value) throw new ArgumentException("--yaml and --json are mutually exclusive.");
                format = value;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or InvalidOperationException
            or NotSupportedException or HttpRequestException or JsonException or YamlException or UnauthorizedAccessException
            or OperationCanceledException)
        {
            await error.WriteLineAsync(debug ? exception.ToString() : "Error: " + exception.Message);
            return 2;
        }
    }
}
