using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReturnTaskDirectlyAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReturnTaskDirectlyAnalyzer : DiagnosticAnalyzer
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
			var methodSymbol = (IMethodSymbol?) context.SemanticModel.GetDeclaredSymbol(node);
			if (!IsMethodValid(node, methodSymbol))
			{
				return;
			}
			
			body = (node as MethodDeclarationSyntax)?.Body ?? (node as LocalFunctionStatementSyntax)?.Body;
			expressionBody = (node as MethodDeclarationSyntax)?.ExpressionBody?.Expression ?? (node as LocalFunctionStatementSyntax)?.ExpressionBody?.Expression;
			returnTypeSymbol = methodSymbol.ReturnType;
		} else if (context.Node.IsKind(SyntaxKind.ParenthesizedLambdaExpression))
		{
			var lambdaSymbol = (IMethodSymbol?) context.SemanticModel.GetSymbolInfo(node).Symbol;
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
		
		if (expressionBody is not null && TryGetDiagnosticForExpressionBody(expressionBody, out var diagnostic))
		{
			context.ReportDiagnostic(diagnostic);

			return;
		}
		
		if (body is not null && TryGetDiagnosticForTaskReturn(body, taskSymbol, returnTypeSymbol, out diagnostic))
		{
			context.ReportDiagnostic(diagnostic);

			return;
		}

		if (body is not null && TryGetDiagnosticForTaskWithValueReturn(body, taskWithValueSymbol, returnTypeSymbol, out diagnostic))
		{
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static bool TryGetDiagnosticForExpressionBody(ExpressionSyntax expressionBody, [NotNullWhen(true)] out Diagnostic? diagnostic)
	{
		diagnostic = null;
		if (expressionBody.IsKind(SyntaxKind.AwaitExpression))
		{
			diagnostic = Diagnostic.Create(DiagnosticDescriptors.ReturnTaskDirectly, expressionBody.GetLocation());

			return true;
		}

		return false;
	}

	private static bool TryGetDiagnosticForTaskReturn(BlockSyntax methodBody, INamedTypeSymbol taskSymbol, ITypeSymbol returnTypeSymbol, [NotNullWhen(true)] out Diagnostic? diagnostic)
	{
		diagnostic = null;
		if (!taskSymbol.Equals(returnTypeSymbol, SymbolEqualityComparer.Default) || methodBody.ContainsUsingStatement())
		{
			return false;
		}
		
		bool IsAwaitCandidateForOptimization(SyntaxNode awaitExpression)
		{
			return (awaitExpression.IsNextStatementReturnStatement()
			        || methodBody.Statements.Last() is ExpressionStatementSyntax expressionStatement && expressionStatement.Expression.Equals(awaitExpression))
			       && !awaitExpression.HasParent(SyntaxKind.TryStatement)
			       && !awaitExpression.HasParent(SyntaxKind.UsingStatement)
			       && !(awaitExpression.Parent?.Parent is BlockSyntax block && block.ContainsUsingStatement());
		}

		var awaitExpressions = methodBody.DescendantNodes().Where(node => node.IsKind(SyntaxKind.AwaitExpression)).ToList();
		if (awaitExpressions.All(IsAwaitCandidateForOptimization))
		{
			var additionalLocations = awaitExpressions.Skip(1).Select(a => a.GetLocation());
			diagnostic = Diagnostic.Create(DiagnosticDescriptors.ReturnTaskDirectly, awaitExpressions[0].GetLocation(), additionalLocations);

			return true;
		}

		return false;
	}
	
	private static bool TryGetDiagnosticForTaskWithValueReturn(BlockSyntax methodBody, INamedTypeSymbol taskWithValueSymbol, ITypeSymbol returnTypeSymbol, [NotNullWhen(true)] out Diagnostic? diagnostic)
	{
		diagnostic = null;
		if (!taskWithValueSymbol.Equals(returnTypeSymbol.OriginalDefinition, SymbolEqualityComparer.Default) || methodBody.ContainsUsingStatement())
		{
			return false;
		}

		bool IsAwaitCandidateForOptimization(ReturnStatementSyntax returnStatement)
		{
			return returnStatement.Expression.IsKind(SyntaxKind.AwaitExpression) 
			       && !returnStatement.HasParent(SyntaxKind.TryStatement) 
			       && !returnStatement.HasParent(SyntaxKind.UsingStatement)
			       && !(returnStatement.Parent is BlockSyntax block && block.ContainsUsingStatement());
		}
			
		var returnStatements = methodBody.DescendantNodes().OfType<ReturnStatementSyntax>().ToList();
		if (returnStatements.All(IsAwaitCandidateForOptimization))
		{
			var additionalLocations = returnStatements.Skip(1).Select(a => a.Expression!.GetLocation());
			diagnostic = Diagnostic.Create(DiagnosticDescriptors.ReturnTaskDirectly, returnStatements[0].Expression!.GetLocation(), additionalLocations);

			return true;
		}

		return false;
	}

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(DiagnosticDescriptors.ReturnTaskDirectly);
}