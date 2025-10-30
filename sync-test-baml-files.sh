#!/bin/bash
set -e

# BAML .NET Test Files Sync Script
# This script copies BAML test files from the upstream BAML repository
# to our .NET test directory, ensuring we have the latest test functions.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="$SCRIPT_DIR/baml/integ-tests/baml_src"
TARGET_DIR="$SCRIPT_DIR/tests/Baml.Net.Tests/TestBamlSrc"

echo "=========================================="
echo "BAML Test Files Sync"
echo "=========================================="
echo "Source: $SOURCE_DIR"
echo "Target: $TARGET_DIR"
echo ""

# Check if source directory exists
if [ ! -d "$SOURCE_DIR" ]; then
    echo "ERROR: Source directory not found: $SOURCE_DIR"
    echo "Please ensure the BAML repository is cloned at: $SCRIPT_DIR/baml"
    exit 1
fi

# Create target directory if it doesn't exist
mkdir -p "$TARGET_DIR"

# Copy clients.baml (LLM client configurations)
echo "Copying clients.baml..."
cp "$SOURCE_DIR/clients.baml" "$TARGET_DIR/"

# Copy generators.baml (code generation settings)
echo "Copying generators.baml..."
cp "$SOURCE_DIR/generators.baml" "$TARGET_DIR/"

# Copy test-files directory (all test functions)
echo "Copying test-files directory..."
rm -rf "$TARGET_DIR/test-files"
cp -r "$SOURCE_DIR/test-files" "$TARGET_DIR/"

# Copy fiddle-examples (useful examples)
if [ -d "$SOURCE_DIR/fiddle-examples" ]; then
    echo "Copying fiddle-examples..."
    rm -rf "$TARGET_DIR/fiddle-examples"
    cp -r "$SOURCE_DIR/fiddle-examples" "$TARGET_DIR/"
fi

# Copy media files (for image/audio/video tests)
echo "Copying media files..."
if [ -f "$SOURCE_DIR/shrek.png" ]; then
    cp "$SOURCE_DIR/shrek.png" "$TARGET_DIR/"
fi
if [ -f "$SOURCE_DIR/xkcd-grownups.png" ]; then
    cp "$SOURCE_DIR/xkcd-grownups.png" "$TARGET_DIR/"
fi
if [ -f "$SOURCE_DIR/sample-5s.mp4" ]; then
    cp "$SOURCE_DIR/sample-5s.mp4" "$TARGET_DIR/"
fi
if [ -f "$SOURCE_DIR/dummy.pdf" ]; then
    cp "$SOURCE_DIR/dummy.pdf" "$TARGET_DIR/"
fi

# Create a README in the target directory
cat > "$TARGET_DIR/README.md" << 'EOF'
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
EOF

echo ""
echo "=========================================="
echo "Sync completed successfully!"
echo "=========================================="
echo ""
echo "Files copied to: $TARGET_DIR"
echo ""
echo "Summary of copied files:"
find "$TARGET_DIR" -type f -name "*.baml" | wc -l | xargs echo "  - BAML files:"
find "$TARGET_DIR" -type f \( -name "*.png" -o -name "*.jpg" -o -name "*.mp4" -o -name "*.pdf" \) | wc -l | xargs echo "  - Media files:"
echo ""
echo "Next steps:"
echo "  1. Create .env file with API keys (see .env.example)"
echo "  2. Run: cd engine && baml-cli generate --from ../tests/Baml.Net.Tests/TestBamlSrc"
echo "  3. Update tests to use the new BAML functions"
echo ""
