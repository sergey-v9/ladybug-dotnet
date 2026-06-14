# Engine extensions example

Installs and loads an official Ladybug engine extension (`json`) and uses one of its functions.

```csharp
conn.InstallExtension("json");   // INSTALL json  (downloads on first use)
conn.LoadExtension("json");      // LOAD EXTENSION json
```

`INSTALL` needs network access to the extension repository, so the example tolerates an offline failure.

On Linux/macOS the binding loads the native engine with global symbol visibility
(`dlopen(RTLD_NOW | RTLD_GLOBAL)`) so the dynamically loaded extension `.so` resolves the engine's
symbols — the same approach the Java/Node/Python/Rust bindings take. Without it, extension loading fails
with undefined-symbol errors. No extra configuration is required from the consumer.

```bash
dotnet run --project examples/engine-extensions/EngineExtensions.csproj
```
