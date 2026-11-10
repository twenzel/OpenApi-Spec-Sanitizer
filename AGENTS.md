# Repository Guidelines

## Structure

This is a C# 10 console application targeting .NET 10. The solution is
`OpenApiSpecSanitizer.slnx`. Production code is in `src/OpenApiSpecSanitizer/`:
`SpecificationLoader` handles file/HTTP(S) and inline YAML/JSON input, `Analyzer`
finds reachable, unused, and undefined nodes, `Sanitizer` tags/removes components,
`SpecificationDumper` writes copies, and `Program` provides the CLI.

Tests are in `tests/OpenApiSpecSanitizer.Tests/` using xUnit v2 and VSTest.
Original YAML/JSON fixtures remain directly in `tests/`. The Open Banking URL
list is retained in `tests/openbanking/` as optional manual integration input.
Default tests must be local and deterministic.

## Commands

- `dotnet build OpenApiSpecSanitizer.slnx --configuration Release`
- `dotnet test OpenApiSpecSanitizer.slnx --configuration Release`
- `dotnet run --project src/OpenApiSpecSanitizer -- --help`
- `dotnet pack src/OpenApiSpecSanitizer --configuration Release --output artifacts`

## Style and behavior

Use four spaces, file-scoped namespaces, nullable annotations, PascalCase public
members, and camelCase locals. Keep the C# language version pinned to 10.0.
Preserve Apache license notices and separate loading, analysis, mutation, and
output. Do not resolve external references silently. Never overwrite input or
existing output files. Test transitive reachability, cycles, JSON Pointer
escaping, YAML/JSON round trips, and CLI error behavior for related changes.

## Pull requests

Use short descriptive subjects and focused changes. Describe behavior changes
and checks run; include input/output examples for CLI changes. Update README
and this guide when commands, options, or project structure change.
