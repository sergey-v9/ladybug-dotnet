using Microsoft.CodeAnalysis;

namespace LadybugDB.SourceGen;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor NoMappableMembers = new(
        id: "LBUG1001",
        title: "[LadybugRow] type has no mappable members",
        messageFormat: "Type '{0}' is annotated with [LadybugRow] but exposes no public constructor parameters or public settable properties to map result columns to",
        category: "LadybugDB.Mapping",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
