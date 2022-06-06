using Microsoft.CodeAnalysis;

namespace ReturnTaskDirectlyAnalyzer;

public static class DiagnosticDescriptors
{
	public static readonly DiagnosticDescriptor ReturnTaskDirectly = new(
		"RTD001",
		"Return Task directly instead of awaiting it",
		"Return Task directly instead of awaiting it",
		"Performance",
		DiagnosticSeverity.Warning,
		true,
		"The compiler will create an unnecessary state machine when awaiting this Task. It is better to simply return it and let the caller await it."
	);
}