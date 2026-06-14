# POCO mapping example

Maps query rows into a typed record with the source-generated, reflection-free (AOT-safe) mapper:

```csharp
using LadybugDB.Mapping;

[LadybugRow]
public sealed record Person(string Name, long Age, DateOnly Registered);

foreach (Person p in result.Map<Person>())   // generated; also MapAsync<Person>()
    Console.WriteLine($"{p.Name} ({p.Age})");
```

Result columns are matched to the record's constructor parameters / settable properties by name,
case-insensitively. The generator ships inside the `LadybugDB` package as an analyzer, so no extra
package reference is needed.

```bash
dotnet run --project examples/poco-mapping/PocoMapping.csproj
```
