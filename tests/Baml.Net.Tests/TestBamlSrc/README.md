# Test BAML Files

This directory contains BAML test files synced from the upstream BAML repository.

## Source

These files are copied from: `/baml/integ-tests/baml_src`

## Updating

To refresh these files with the latest from the BAML repository, run:

```bash
./sync-test-baml-files.sh
```

## Structure

- `clients.baml` - LLM client configurations (GPT-4, Claude, etc.)
- `generators.baml` - Code generation settings for .NET
- `test-files/` - BAML function definitions for testing
  - `functions/` - Test functions for various input/output types
  - `providers/` - Provider-specific test functions
  - `strategies/` - Retry and fallback strategy tests
  - And more...

## Usage in Tests

Tests can reference functions from these BAML files by name:

```csharp
var runtime = BamlRuntime.FromDirectory(_bamlSrcPath);
var asyncRuntime = new BamlRuntimeAsync(runtime);

var result = await asyncRuntime.CallFunctionAsync(
    "TestFnNamedArgsSingleStringList",
    new Dictionary<string, object> { ["myArg"] = new[] { "a", "b", "c" } }
);
```

## Do Not Edit

**Do not manually edit files in this directory!**

These files are automatically synced from the upstream BAML repository.
Any manual changes will be overwritten on the next sync.

If you need custom test functions, create them in a separate directory.
