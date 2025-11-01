---
date: 2025-10-30T02:06:20Z
researcher: claude
git_commit: 882b12ecf376fbc808fc4460ea3c2f5b9e9a288d
branch: main
repository: bamldotnet
topic: "Adding C# Support to BAML Code Generation"
tags: [research, codebase, baml, csharp, typescript, code-generation, generators]
status: complete
last_updated: 2025-01-30
last_updated_by: claude
---

# Research: Adding C# Support to BAML Code Generation

**Date**: 2025-10-30T02:06:20Z
**Researcher**: claude
**Git Commit**: 882b12ecf376fbc808fc4460ea3c2f5b9e9a288d
**Branch**: main
**Repository**: bamldotnet

## Research Question
Research @baml folder to see how could we add C# support to BAML. Check out how the TypeScript support is implemented. DO NOT care about what runtime code will be generated, I just want C# support working with empty file generation but have the logic in place.

## Summary

BAML uses a trait-based code generation system where language-specific generators implement the `LanguageFeatures` trait to transform an Intermediate Representation (IR) into target language client code. The TypeScript implementation demonstrates a template-based approach using Askama/Jinja2 templates. To add C# support, you would need to:

1. Add a `Csharp` variant to the `GeneratorOutputType` enum
2. Create a new generator crate at `baml/engine/generators/languages/csharp/`
3. Implement the `LanguageFeatures` trait with a `CsharpLanguageFeatures` struct
4. Add dispatch logic in `generators_lib::generate_sdk()`
5. Create type conversion logic from IR to C# types (similar to TypeScript's `TypeTS`)
6. Generate files (can start with empty content, following the established structure)

## Detailed Findings

### Language Registration Pattern

Languages are registered through an enum-based system at `/Users/attila/workspaces/bamldotnet/baml/engine/baml-lib/baml-types/src/generator.rs:13-34`:

```rust
#[derive(
    Debug, Clone, Copy,
    strum::Display, strum::EnumString, strum::VariantArray,
    PartialEq, Eq
)]
pub enum GeneratorOutputType {
    #[strum(serialize = "typescript")]
    Typescript,

    #[strum(serialize = "python/pydantic")]
    PythonPydantic,

    // Add C# here:
    // #[strum(serialize = "csharp")]
    // Csharp,
}
```

The enum uses `strum` derive macros for automatic string conversion, CLI argument parsing, and iteration capabilities. Each variant needs:
- A `#[strum(serialize = "...")]` attribute defining its string representation
- Default client mode implementation (Sync/Async) at lines 42-71
- CLI integration via `clap::ValueEnum` trait at lines 73-84

### Core Generator Interface

The `LanguageFeatures` trait at `/Users/attila/workspaces/bamldotnet/baml/engine/generators/utils/dir_writer/src/lib.rs:150-215` defines the contract:

```rust
pub trait LanguageFeatures: Default + Sized {
    const CONTENT_PREFIX: &'static str;  // Header comment for generated files

    fn name() -> &'static str;  // Language identifier

    fn generate_sdk_files(
        &self,
        collector: &mut FileCollector<Self>,
        ir: Arc<IntermediateRepr>,
        args: &GeneratorArgs,
    ) -> Result<(), anyhow::Error>;  // Main generation logic
}
```

### TypeScript Implementation Architecture

The TypeScript generator at `/Users/attila/workspaces/bamldotnet/baml/engine/generators/languages/typescript/src/lib.rs` shows the implementation pattern:

#### 1. Generator Structure (lines 22-50)
```rust
#[derive(Default, Debug)]
pub struct TsLanguageFeatures;

impl LanguageFeatures for TsLanguageFeatures {
    const CONTENT_PREFIX: &'static str = r#"
    // Installation instructions and linting disables
    "#;

    fn name() -> &'static str {
        "typescript"
    }
}
```

#### 2. Type System (src/type.rs:90-236)
TypeScript types are represented by a `TypeTS` enum with variants for:
- Primitives: String, Int, Float, Bool
- Collections: List, Map
- Named types: Class, Enum, TypeAlias, Interface
- Special: Media, Union, Literal, Any

Each type has associated metadata (`TypeMetaTS`) tracking:
- Optional wrapping (| null)
- Checked wrapping (runtime validation)
- Stream state wrapping (for async operations)

#### 3. IR to TypeScript Conversion (src/ir_to_ts/)
The conversion happens in multiple modules:
- `mod.rs`: Main type conversion functions (`type_to_ts`, `stream_type_to_ts`)
- `classes.rs`: BAML classes → TypeScript interfaces
- `enums.rs`: BAML enums → TypeScript enums
- `functions.rs`: BAML functions → TypeScript function signatures
- `type_aliases.rs`: Type alias handling with circular reference detection

#### 4. Code Generation Flow (lib.rs:52-388)

The `generate_sdk_files()` method orchestrates generation:

```rust
// Stage 1: Prepare data from IR
let types = ir.walk_classes().chain(ir.walk_enums()).collect();
let functions = ir.walk_functions().map(ir_function_to_ts).collect();

// Stage 2: Generate base files (always created)
collector.add_file("index.ts", render_index(...))?;
collector.add_file("types.ts", render_types(...))?;
collector.add_file("async_client.ts", render_async_client(...))?;
// ... more files

// Stage 3: Conditional generation (e.g., React support)
if args.client_type == GeneratorOutputType::TypescriptReact {
    collector.add_file("react/hooks.tsx", render_react_hooks(...))?;
}

// Stage 4: Post-processing (ESM transformation)
if args.module_format == ModuleFormat::Esm {
    collector.modify_files(add_js_suffix_to_imports);
}
```

#### 5. Template System
Templates are stored in `src/_templates/` as Jinja2 files (.j2) and rendered using Askama:

```rust
#[derive(askama::Template)]
#[template(path = "async_client.ts.j2", escape = "none")]
struct AsyncClient<'a> {
    functions: &'a [FunctionTS],
    types: &'a [String],
}

pub fn render_async_client(...) -> Result<String> {
    AsyncClient { functions, types }.render()
}
```

### Generator Dispatch System

The dispatch happens in `/Users/attila/workspaces/bamldotnet/baml/engine/generators/utils/generators_lib/src/lib.rs:19-49`:

```rust
pub fn generate_sdk(ir: Arc<IntermediateRepr>, gen: &GeneratorArgs) -> Result<...> {
    let res = match gen.client_type {
        GeneratorOutputType::Typescript | GeneratorOutputType::TypescriptReact => {
            use generators_typescript::TsLanguageFeatures;
            let features = TsLanguageFeatures;
            features.generate_sdk(ir, gen)?
        }
        // Add C# case here:
        // GeneratorOutputType::Csharp => {
        //     use generators_csharp::CsharpLanguageFeatures;
        //     let features = CsharpLanguageFeatures;
        //     features.generate_sdk(ir, gen)?
        // }
    };
    Ok(res)
}
```

### Build System Integration

The Cargo workspace at `/Users/attila/workspaces/bamldotnet/baml/engine/Cargo.toml` manages all generator crates:

```toml
[workspace]
members = [
    "generators/languages/typescript",
    "generators/languages/python",
    # Add: "generators/languages/csharp",
]
```

Each generator has its own `Cargo.toml` with dependencies on:
- `dir-writer`: File writing utilities
- `internal-baml-core`: Access to IR
- `askama` (optional): Template engine
- Language-specific dependencies

## Implementation Steps for C# Support

### Step 1: Add Language Variant

Edit `/Users/attila/workspaces/bamldotnet/baml/engine/baml-lib/baml-types/src/generator.rs`:

```rust
pub enum GeneratorOutputType {
    // ... existing variants

    #[strum(serialize = "csharp")]
    Csharp,
}

impl GeneratorOutputType {
    pub fn default_client_mode(&self) -> GeneratorDefaultClientMode {
        match self {
            // ... existing cases
            Self::Csharp => GeneratorDefaultClientMode::Async, // or Sync
        }
    }
}
```

### Step 2: Create Generator Crate

Create `/Users/attila/workspaces/bamldotnet/baml/engine/generators/languages/csharp/`:

```
csharp/
├── Cargo.toml
└── src/
    └── lib.rs
```

`Cargo.toml`:
```toml
[package]
name = "generators-csharp"
version.workspace = true
authors.workspace = true

[dependencies]
dir-writer = { path = "../../utils/dir_writer" }
internal-baml-core = { path = "../../../baml-lib/baml-core" }
anyhow.workspace = true
indexmap.workspace = true
```

### Step 3: Implement LanguageFeatures

`src/lib.rs`:
```rust
use dir_writer::{FileCollector, GeneratorArgs, LanguageFeatures};
use internal_baml_core::ir::repr::IntermediateRepr;
use std::sync::Arc;

#[derive(Default)]
pub struct CsharpLanguageFeatures;

impl LanguageFeatures for CsharpLanguageFeatures {
    const CONTENT_PREFIX: &'static str = r#"
// ----------------------------------------------------------------------------
//
//  Welcome to Baml! To use this generated code, please run:
//
//  $ dotnet add package Baml
//
// ----------------------------------------------------------------------------

// This file was generated by BAML: please do not edit it. Instead, edit the
// BAML files and re-generate this code using: baml-cli generate
    "#;

    fn name() -> &'static str {
        "csharp"
    }

    fn generate_sdk_files(
        &self,
        collector: &mut FileCollector<Self>,
        ir: Arc<IntermediateRepr>,
        args: &GeneratorArgs,
    ) -> Result<(), anyhow::Error> {
        // Start with empty file generation
        collector.add_file("BamlClient.cs", "// TODO: Implement BAML client")?;
        collector.add_file("Types.cs", "// TODO: Generate types from IR")?;

        // Walk the IR to understand the structure (for future implementation)
        for class in ir.walk_classes() {
            // Future: Generate C# class/record for each BAML class
            println!("Found class: {}", class.elem.name);
        }

        for enum_ in ir.walk_enums() {
            // Future: Generate C# enum for each BAML enum
            println!("Found enum: {}", enum_.elem.name);
        }

        for function in ir.walk_functions() {
            // Future: Generate C# method for each BAML function
            println!("Found function: {}", function.elem.name());
        }

        Ok(())
    }
}
```

### Step 4: Add Generator Dispatch

Edit `/Users/attila/workspaces/bamldotnet/baml/engine/generators/utils/generators_lib/src/lib.rs`:

Add dependency in `Cargo.toml`:
```toml
generators-csharp = { path = "../languages/csharp" }
```

Add dispatch case:
```rust
GeneratorOutputType::Csharp => {
    use generators_csharp::CsharpLanguageFeatures;
    let features = CsharpLanguageFeatures;
    features.generate_sdk(ir, gen)?
}
```

### Step 5: Update Workspace

Edit `/Users/attila/workspaces/bamldotnet/baml/engine/Cargo.toml`:

```toml
[workspace]
members = [
    # ... existing members
    "generators/languages/csharp",
]
```

### Future Implementation Path

Once the basic structure is in place, you can expand the C# generator following the TypeScript pattern:

1. **Type System**: Create a `CsharpType` enum similar to `TypeTS` for representing C# types
2. **IR Conversion**: Implement conversion functions from IR types to C# types
3. **Code Generation**: Either use templates (T4/Scriban) or direct string building
4. **File Structure**: Generate typical C# project structure (Classes, Interfaces, Client, etc.)
5. **Package Management**: Consider NuGet package references and project file generation

The TypeScript implementation provides a clear template for:
- Walking the IR to extract types and functions
- Converting between type systems
- Managing file generation and organization
- Handling special cases (streaming, optional types, etc.)

## Architecture Documentation

The BAML code generation architecture follows these patterns:

1. **Trait-Based Extensibility**: New languages implement `LanguageFeatures` trait
2. **IR as Single Source of Truth**: All generators work from the same IR
3. **Template vs Direct Generation**: Languages can choose templates (TypeScript) or direct string building (Go)
4. **Atomic File Writing**: Files written to temp directory first, then atomically renamed
5. **Type System Mapping**: Each language defines its own type representation and conversion logic
6. **Post-Processing Hooks**: Support for language-specific transformations (ESM imports, formatting)

## Related Research

- TypeScript generator implementation: `/Users/attila/workspaces/bamldotnet/baml/engine/generators/languages/typescript/`
- Generator trait definition: `/Users/attila/workspaces/bamldotnet/baml/engine/generators/utils/dir_writer/src/lib.rs`
- Language registration: `/Users/attila/workspaces/bamldotnet/baml/engine/baml-lib/baml-types/src/generator.rs`

## Open Questions

1. Should C# use templates (T4/Scriban) or direct code generation?
2. Should generated code target .NET Standard, .NET Core, or .NET 5+?
3. How should async operations be handled - Task/async-await or callbacks?
4. Should the generator create a full project structure with .csproj file?