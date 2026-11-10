# openapi-spec-sanitizer

A C# 10 console application and reusable API that detects unused and undefined
components in Swagger 2.0 and OpenAPI 3.x YAML or JSON specifications.
Converted from https://github.com/DavidTurland/openapi-spec-sanitizer.

## Build and run

Requires the .NET 10 SDK.
YAML uses YamlDotNet; JSON uses System.Text.Json.

```sh
dotnet build OpenApiSpecSanitizer.slnx --configuration Release
dotnet test OpenApiSpecSanitizer.slnx --configuration Release
dotnet run --project src/OpenApiSpecSanitizer -- --help
```

To package and install as a local .NET tool:

```sh
dotnet pack src/OpenApiSpecSanitizer --configuration Release --output artifacts
dotnet tool install --tool-path .tools --add-source ./artifacts OpenApiSpecSanitizer
```

Run `.tools/openapi-spec-sanitizer` (`.exe` on Windows). To publish an app:
`dotnet publish src/OpenApiSpecSanitizer --configuration Release --output artifacts/app`.

## Usage

```text
openapi-spec-sanitizer [options] filename

filename             Local .yaml/.yml/.json file or HTTP(S) URL
-s, --sanitize       Write a sanitized copy
-t, --tag TAG        Add TAG: true to unused components (default: unused)
-r, --remove         Delete unused components; exclusive with --tag
-o, --output PATH    Output path (default: original-name.san.extension)
-l, --lax            Tolerate unused components
-y, --yaml           Allow inline YAML input
-j, --json           Allow inline JSON input; exclusive with --yaml
-q, --quiet          Suppress reports and progress
-v, --verbose        Show progress (also the default)
-g, --debug          Include required paths and exception details
-h, --help           Show help
--version            Show version
```

Analyze without writing files:

```sh
dotnet run --project src/OpenApiSpecSanitizer -- tests/simple_unused.yaml -l
```

Tag the unused request body with `unused: true`, creating
`tests/simple_unused.san.yaml`:

```sh
dotnet run --project src/OpenApiSpecSanitizer -- tests/simple_unused.yaml -s -l
```

Remove the unused request body and write an explicit output file:

```sh
dotnet run --project src/OpenApiSpecSanitizer -- tests/simple_unused.yaml -s -l -r -o cleaned.yaml
```

URL and inline input require `--output` when sanitizing. Output keeps the input
format, regardless of output extension. Existing files are never overwritten,
including the input. YAML formatting, comments, and anchors are not preserved.

Exit codes: **0** success; **1** unused components found without `--lax`;
**2** invalid input, undefined required references, unsupported references,
argument errors, or I/O failures. As in the original strict workflow, use `-l`
with `-s` to permit sanitization when unused components exist.

## Analysis and API

Components are retained when reachable from outside reusable component declarations.
Transitive dependencies and reachable cycles are retained; isolated cycles are
unused. Missing references inside unused components do not invalidate the document.
JSON Pointer escaping and percent-encoded URI fragments are supported. References
to a component's child retain its owning component. Security requirement names
retain the corresponding security definitions.

OpenAPI categories: schemas, parameters, responses, requestBodies, headers,
examples, links, callbacks, securitySchemes, and pathItems. Swagger categories:
definitions, parameters, responses, and securityDefinitions. External file/URL
references and named anchors fail explicitly. HTTP(S) input has no remote cache;
FTP input is no longer supported. This is a dependency analyzer, not a complete
OpenAPI schema validator. Very deep YAML and cyclic YAML aliases are rejected.

```csharp
using OpenApiSpecSanitizer;

var spec = await new SpecificationLoader().LoadAsync("input.yaml");
var analysis = new Analyzer().Analyze(spec.Document);
if (analysis.UndefinedComponents.Count == 0)
{
    var cleaned = new Sanitizer().Sanitize(spec.Document, analysis, SanitizeMode.Remove);
    new SpecificationDumper().Dump(spec, cleaned, "cleaned.yaml");
}
```

Sanitization returns a copy. Analysis can be repeated without retaining earlier
results. Loading, analysis, mutation, output, and CLI parsing are separate classes.

## Tests

Default tests are local and deterministic, including a fake HTTP response for
URL loading. The historical `tests/openbanking/openbanking_specs.yaml` URL list
is retained for manual checks with downloaded specifications; remote downloads
and historical expected counts are not part of the default suite.

`tests/api-with-examples.yaml` is a snapshot of an
[OpenAPI Initiative example](https://github.com/OAI/OpenAPI-Specification).
Copyright David Turland 2023. [Apache License 2.0](LICENSE-2.0.txt).
