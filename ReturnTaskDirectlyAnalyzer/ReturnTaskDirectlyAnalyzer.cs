using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReturnTaskDirectlyAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class ReturnTaskDirectlyAnalyzer : DiagnosticAnalyzer
{
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		context.RegisterSyntaxNodeAction(OnMethodAnalysis, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.ParenthesizedLambdaExpression);
	}

	private static void OnMethodAnalysis(SyntaxNodeAnalysisContext context)
	{
		var taskSymbol = context.Compilation.GetTypeByMetadataName(typeof(Task).FullName!)!;
		var taskWithValueSymbol = context.Compilation.GetTypeByMetadataName(typeof(Task<>).FullName!)!;
		var valueTaskSymbol = context.Compilation.GetTypeByMetadataName(typeof(ValueTask).FullName!)!;
		var valueTaskWithValueSymbol = context.Compilation.GetTypeByMetadataName(typeof(ValueTask<>).FullName!)!;

		var node = context.Node;

		static bool IsMethodValid(SyntaxNode node, [NotNullWhen(true)] IMethodSymbol? methodSymbol)
		{
			return methodSymbol is not null
			       && methodSymbol.IsAsync
			       && !methodSymbol.ReturnsVoid
			       && node.DescendantNodes().Any(n => n.IsKind(SyntaxKind.AwaitExpression));
		}

		BlockSyntax? body;
		ExpressionSyntax? expressionBody;
		ITypeSymbol returnTypeSymbol;
		if (node.IsKind(SyntaxKind.MethodDeclaration) || node.IsKind(SyntaxKind.LocalFunctionStatement))
		{
			var methodSymbol = (IMethodSymbol?)context.SemanticModel.GetDeclaredSymbol(node);
			if (!IsMethodValid(node, methodSymbol))
			{
				return;
			}

			body = (node as MethodDeclarationSyntax)?.Body ?? (node as LocalFunctionStatementSyntax)?.Body;
			expressionBody = (node as MethodDeclarationSyntax)?.ExpressionBody?.Expression ?? (node as LocalFunctionStatementSyntax)?.ExpressionBody?.Expression;
			returnTypeSymbol = methodSymbol.ReturnType;
		}
		else if (context.Node.IsKind(SyntaxKind.ParenthesizedLambdaExpression))
		{
			var lambdaSymbol = (IMethodSymbol?)context.SemanticModel.GetSymbolInfo(node).Symbol;
			if (!IsMethodValid(node, lambdaSymbol))
			{
				return;
			}

			body = (node as ParenthesizedLambdaExpressionSyntax)!.Block;
			expressionBody = (node as ParenthesizedLambdaExpressionSyntax)!.ExpressionBody;
			returnTypeSymbol = lambdaSymbol.ReturnType;
		}
		else
		{
			return;
		}

		var semanticModel = context.SemanticModel;
		if (expressionBody is not null && TryGetDiagnosticForExpressionBody(semanticModel, expressionBody, returnTypeSymbol, out var diagnostic))
		{
			context.ReportDiagnostic(diagnostic);

			return;
		}

		if ((taskSymbol.Equals(returnTypeSymbol, SymbolEqualityComparer.Default)
		     || valueTaskSymbol.Equals(returnTypeSymbol, SymbolEqualityComparer.Default))
		    && body is not null
		    && TryGetDiagnosticForTaskReturn(semanticModel, body, returnTypeSymbol, out diagnostic))
		{
			context.ReportDiagnostic(diagnostic);

			return;
		}

		if ((taskWithValueSymbol.Equals(returnTypeSymbol.OriginalDefinition, SymbolEqualityComparer.Default)
		     || valueTaskWithValueSymbol.Equals(returnTypeSymbol.OriginalDefinition, SymbolEqualityComparer.Default))
		    && body is not null
		    && TryGetDiagnosticForTaskWithValueReturn(semanticModel, body, returnTypeSymbol, out diagnostic))
		{
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static bool TryGetDiagnosticForExpressionBody(SemanticModel semanticModel, ExpressionSyntax expressionBody, ITypeSymbol returnTypeSymbol, [NotNullWhen(true)] out Diagnostic? diagnostic)
	{
		diagnostic = null;
		if (expressionBody is AwaitExpressionSyntax awaitExpression && !HasCovariantReturn(semanticModel, awaitExpression, returnTypeSymbol))
		{
			diagnostic = Diagnostic.Create(DiagnosticDescriptors.ReturnTaskDirectly, expressionBody.GetLocation());

			return true;
		}

		return false;
	}

	private static bool TryGetDiagnosticForTaskReturn(SemanticModel semanticModel, BlockSyntax methodBody, ITypeSymbol returnTypeSymbol, [NotNullWhen(true)] out Diagnostic? diagnostic)
	{
		diagnostic = null;
		if (methodBody.ContainsUsingStatement())
		{
			return false;
		}

		bool IsAwaitCandidateForOptimization(AwaitExpressionSyntax awaitExpression)
		{
			return (awaitExpression.IsNextStatementReturnStatement()
			        || methodBody.Statements.Last() is ExpressionStatementSyntax expressionStatement && expressionStatement.Expression.Equals(awaitExpression))
			       && !HasCovariantReturn(semanticModel, awaitExpression, returnTypeSymbol)
			       && !awaitExpression.HasParent(SyntaxKind.TryStatement)
			       && !awaitExpression.HasParent(SyntaxKind.UsingStatement)
			       && !(awaitExpression.Parent?.Parent is BlockSyntax block && block.ContainsUsingStatement());
		}

		var awaitExpressions = methodBody.DescendantNodes(node => !node.IsKind(SyntaxKind.LocalFunctionStatement))
			.OfType<AwaitExpressionSyntax>()
			.ToList();
		if (awaitExpressions.All(IsAwaitCandidateForOptimization))
		{
			var additionalLocations = awaitExpressions.Skip(1).Select(a => a.GetLocation());
			diagnostic = Diagnostic.Create(DiagnosticDescriptors.ReturnTaskDirectly, awaitExpressions[0].GetLocation(), additionalLocations);

			return true;
		}

		return false;
	}

	private static bool TryGetDiagnosticForTaskWithValueReturn(SemanticModel semanticModel, BlockSyntax methodBody, ITypeSymbol returnTypeSymbol, [NotNullWhen(true)] out Diagnostic? diagnostic)
	{
		diagnostic = null;
		if (methodBody.ContainsUsingStatement())
		{
			return false;
		}

		bool IsAwaitCandidateForOptimization(AwaitExpressionSyntax awaitExpression)
		{
			return awaitExpression.Parent is ReturnStatementSyntax returnStatement
			       && !HasCovariantReturn(semanticModel, awaitExpression, returnTypeSymbol)
			       && !returnStatement.HasParent(SyntaxKind.TryStatement)
			       && !returnStatement.HasParent(SyntaxKind.UsingStatement)
			       && !(returnStatement.Parent is BlockSyntax block && block.ContainsUsingStatement());
		}

		var awaitExpressions = methodBody.DescendantNodes(node => !node.IsKind(SyntaxKind.LocalFunctionStatement))
			.OfType<AwaitExpressionSyntax>()
			.ToList();
		var returnStatements = methodBody.DescendantNodes(node => !node.IsKind(SyntaxKind.LocalFunctionStatement)).OfType<ReturnStatementSyntax>();
		if (returnStatements.All(r => r.Expression.IsKind(SyntaxKind.AwaitExpression)) && awaitExpressions.All(IsAwaitCandidateForOptimization))
		{
			var additionalLocations = awaitExpressions.Skip(1).Select(a => a.GetLocation());
			diagnostic = Diagnostic.Create(DiagnosticDescriptors.ReturnTaskDirectly, awaitExpressions[0].GetLocation(), additionalLocations);

			return true;
		}

		return false;
	}

	private static bool HasCovariantReturn(SemanticModel semanticModel, AwaitExpressionSyntax awaitExpression, ITypeSymbol returnTypeSymbol)
	{
		if (awaitExpression.Expression is InvocationExpressionSyntax invocation)
		{
			return HasCovariantReturn(semanticModel, invocation, returnTypeSymbol);
		}

		return false;
	}

	private static bool HasCovariantReturn(SemanticModel semanticModel, InvocationExpressionSyntax invocation, ITypeSymbol returnTypeSymbol)
	{
		var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (methodSymbol?.Name == "ConfigureAwait" && invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax memberInvocation })
		{
			return HasCovariantReturn(semanticModel, memberInvocation, returnTypeSymbol);
		}

		var returnStatementTypeSymbol = methodSymbol?.ReturnType;

		return returnStatementTypeSymbol is not null && !returnTypeSymbol.Equals(returnStatementTypeSymbol, SymbolEqualityComparer.Default);
	}

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(DiagnosticDescriptors.ReturnTaskDirectly);
}