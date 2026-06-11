# Draft Request: Support Graphiti Parameter Binding Shapes

Graphiti's LadybugDB driver uses Kuzu-style prepared statements with list, array, empty-list, and
null parameters. The previous .NET binding rejected those shapes in `PreparedStatement.Bind(object?)`,
forcing Graphiti to literalize parameters before execution.

## Requested Fix

- Wrap `lbug_prepared_statement_bind_value`.
- Wrap native `lbug_value` creation for nulls, scalar values, and lists.
- Bind `IEnumerable` values through native `LIST` values instead of throwing.
- Create typed empty lists from generic CLR collection element types, using `lbug_data_type_create`
  and `lbug_value_create_default`, so empty lists do not become `LIST<ANY>`.
- Keep scalar overload behavior unchanged.

## Local Validation

From `tools/csharp_api`:

```powershell
dotnet build LadybugDB.slnx -c Release
dotnet test test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Release --filter FullyQualifiedName~PreparedStatementTests --verbosity minimal
dotnet test test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Release --verbosity minimal
.\build.ps1 --target Pack --package-version 0.17.0-alpha.2-graphiti.1
```

Observed results on 2026-06-11:

- Build succeeded with `0` warnings.
- Prepared-statement tests passed: `4` passed.
- Full binding suite passed: `29` passed.
- Package family packed and verified into `artifacts/` as `0.17.0-alpha.2-graphiti.1`.

## Graphiti Consumer Proof

Graphiti was pointed at the local artifacts feed and package version
`0.17.0-alpha.2-graphiti.1`. Its Ladybug package-runtime tests pass without the former statement
literalization workaround, including direct binding of `List<string>`, arrays, empty string arrays,
float arrays, and null values.
