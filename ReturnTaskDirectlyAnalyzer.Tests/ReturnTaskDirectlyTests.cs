using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace ReturnTaskDirectlyAnalyzer.Tests;

using Verify = CodeFixVerifier<ReturnTaskDirectlyAnalyzer, ReturnTaskDirectlyFixer, CSharpCodeFixTest<ReturnTaskDirectlyAnalyzer, ReturnTaskDirectlyFixer, XUnitVerifier>, XUnitVerifier>;

public class ReturnTaskDirectlyTests
{
	private const string Scaffold = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnitTests;

public class Test {{
	private readonly DataService _dataService;
 
	{0}

	public Task DoSomethingAsync() {{
		return Task.Delay(1000);
	}}

	public Task<int> GetSomethingAsync() {{
		return Task.FromResult(2);
	}}

	public Task<List<int>> GetListAsync() {{
		return Task.FromResult(new List<int>() {{ 1, 2, 3 }});
	}}

	public Task Accept(Func<Task> func) {{
		return func();
	}}

	public Task<T> AcceptValue<T>(Func<Task<T>> func) {{
		return func();
	}}
}}

public class MyDisposable : IDisposable, IAsyncDisposable {{
	public void Dispose() {{
	}}

	public ValueTask DisposeAsync() {{
		return default;
	}} 
}}

public class DataService {{
	public Task<List<int>> GetListAsync() {{
		return Task.FromResult(new List<int>() {{ 1, 2, 3 }});
	}}
}}
";

	// I know regions are bad, please don't report me to the region police.
	#region ShouldRaise
	
	private const string SingleAwait = @"
async Task RunAsync() {
	{|#0:await DoSomethingAsync()|};
}
";
	
	private const string SingleAwaitFixed = @"
Task RunAsync() {
	return DoSomethingAsync();
}
";
	
	private const string SingleAwait2 = @"
public async Task RunAsync() {
	{|#0:await Task.Delay(1000)|};
}
";
	
	private const string SingleAwaitFixed2 = @"
public Task RunAsync() {
	return Task.Delay(1000);
}
";
	
	private const string SingleAwaitExpression = @"
public async Task RunAsync() => {|#0:await DoSomethingAsync()|};
";
	
	private const string SingleAwaitExpressionFixed = @"
public Task RunAsync() => DoSomethingAsync();
";
	private const string SingleAwaitWithReturnExpression = @"
public async Task<int> RunAsync() => {|#0:await GetSomethingAsync()|};
";
	
	private const string SingleAwaitWithReturnExpressionFixed = @"
public Task<int> RunAsync() => GetSomethingAsync();
";
	
	private const string SingleAwaitWithReturn = @"
public async Task<int> RunAsync() {
	return {|#0:await GetSomethingAsync()|};
}
";
	
	private const string SingleAwaitWithReturnFixed = @"
public Task<int> RunAsync() {
	return GetSomethingAsync();
}
";
	
	private const string MultipleStatementsWithSingleAwait = @"
public async Task RunAsync() {
	var guid = Guid.NewGuid();

	{|#0:await DoSomethingAsync()|};
}
";
	
	private const string MultipleStatementsWithSingleAwaitFixed = @"
public Task RunAsync() {
	var guid = Guid.NewGuid();

	return DoSomethingAsync();
}
";
	
	private const string MultipleStatementsWithReturn = @"
public async Task<int> RunAsync() {
	var task = GetSomethingAsync();
	Console.WriteLine(task.Id);

	return {|#0:await task|};
}
";
	
	private const string MultipleStatementsWithReturnFixed = @"
public Task<int> RunAsync() {
	var task = GetSomethingAsync();
	Console.WriteLine(task.Id);

	return task;
}
";

	private const string MultipleReturnStatements = @"
public async Task<int> RunAsync() {
	var guid = Guid.NewGuid().ToString();
	if(guid.StartsWith(""a"")) {
		return {|#0:await Task.FromResult(6)|};
	}

	if(guid.StartsWith(""b"")) {
		return {|#1:await Task.FromResult(3)|};
	}

	return {|#2:await GetSomethingAsync()|};
}
";
	
	private const string MultipleReturnStatementsFixed = @"
public Task<int> RunAsync() {
	var guid = Guid.NewGuid().ToString();
	if(guid.StartsWith(""a"")) {
		return Task.FromResult(6);
	}

	if(guid.StartsWith(""b"")) {
		return Task.FromResult(3);
	}

	return GetSomethingAsync();
}
";
	
	private const string WithNonRelevantUsingBlock = @"
public async Task<int> RunAsync() {
	var task = GetSomethingAsync();
	using (var _ = new MyDisposable()) {
		Console.WriteLine(task.Id);
	}

	if(task.IsCompleted) {
		return {|#0:await Task.FromResult(5)|};
	}

	return {|#1:await task|};
}
";
	
	private const string WithNonRelevantUsingBlockFixed = @"
public Task<int> RunAsync() {
	var task = GetSomethingAsync();
	using (var _ = new MyDisposable()) {
		Console.WriteLine(task.Id);
	}

	if(task.IsCompleted) {
		return Task.FromResult(5);
	}

	return task;
}
";
	
	private const string WithNonRelevantTryBlock = @"
public async Task RunAsync() {
	var task = DoSomethingAsync();
	try {
		Console.WriteLine(task.Id);
	} catch (Exception) {
	}

	{|#0:await task|};
}
";
	
	private const string WithNonRelevantTryBlockFixed = @"
public Task RunAsync() {
	var task = DoSomethingAsync();
	try {
		Console.WriteLine(task.Id);
	} catch (Exception) {
	}

	return task;
}
";

	private const string WithMultipleAwaits = @"
private async Task RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		{|#0:await DoSomethingAsync()|};
		return;
	}

	{|#1:await Task.Delay(1000)|};
}
";

	private const string WithMultipleAwaitsFixed = @"
private Task RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		return DoSomethingAsync();
	}

	return Task.Delay(1000);
}
";

	private const string LocalFunction = @"
private Task RunAsync()
{
	async Task ComputeAsync() {
		{|#0:await Task.Delay(1000)|};
	}

	return ComputeAsync();
}
";
	
	private const string LocalFunctionFixed = @"
private Task RunAsync()
{
	Task ComputeAsync() {
		return Task.Delay(1000);
	}

	return ComputeAsync();
}
";
	
	private const string WithConfigureAwait = @"
private async Task RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		{|#0:await DoSomethingAsync().ConfigureAwait(false)|};
		return;
	}

	{|#1:await Task.Delay(1000)|};
}
";
	
	private const string WithConfigureAwaitFixed = @"
private Task RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		return DoSomethingAsync();
	}

	return Task.Delay(1000);
}
";
	
	private const string WithUnrelatedUsingStatement = @"
private async Task RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		using var _ = new MyDisposable();
		Console.WriteLine(2);
	}

	{|#0:await Task.Delay(1000)|};
}
";
	
	private const string WithUnrelatedUsingStatementFixed = @"
private Task RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		using var _ = new MyDisposable();
		Console.WriteLine(2);
	}

	return Task.Delay(1000);
}
";
	
	private const string LambdaExpression = @"
private Task RunAsync()
{
	return Accept(async () => {|#0:await DoSomethingAsync()|});
}
";
	
	private const string LambdaExpressionFixed = @"
private Task RunAsync()
{
	return Accept(() => DoSomethingAsync());
}
";
	
	private const string LambdaExpressionWithReturn = @"
private Task RunAsync()
{
	return AcceptValue(async () => {|#0:await GetSomethingAsync()|});
}
";
	
	private const string LambdaExpressionWithReturnFixed = @"
private Task RunAsync()
{
	return AcceptValue(() => GetSomethingAsync());
}
";
	
	private const string LambdaBlock = @"
private Task RunAsync()
{
	return Accept(async () => 
	{
		var guid = Guid.NewGuid();
		if(guid == Guid.NewGuid()) {
			{|#0:await DoSomethingAsync()|};
			return;
		}
		
		{|#1:await Task.CompletedTask|};
	});
}
";
	
	private const string LambdaBlockFixed = @"
private Task RunAsync()
{
	return Accept(() => 
	{
		var guid = Guid.NewGuid();
		if(guid == Guid.NewGuid()) {
			return DoSomethingAsync();
		}
		
		return Task.CompletedTask;
	});
}
";
	
	private const string LambdaBlockWithReturn = @"
private Task RunAsync()
{
	return AcceptValue(async () => 
	{
		var guid = Guid.NewGuid();
		if(guid == Guid.NewGuid()) {
			return {|#0:await GetSomethingAsync()|};
		}
		
		return {|#1:await Task.FromResult(5)|};
	});
}
";
	
	private const string LambdaBlockWithReturnFixed = @"
private Task RunAsync()
{
	return AcceptValue(() => 
	{
		var guid = Guid.NewGuid();
		if(guid == Guid.NewGuid()) {
			return GetSomethingAsync();
		}
		
		return Task.FromResult(5);
	});
}
";
	
	#endregion

	#region ShouldNotRaise

	private const string NonTaskMethod = @"
public void Run() {
}
";
	
	private const string NonTaskMethod2 = @"
public int Run() {
	return 5;
}
";
	
	private const string NoAwait = @"
public async Task Run() {
	Console.WriteLine(""Hello World"");
}
";
	
	private const string AsyncVoidMethod = @"
public async void Run() {
	await Task.CompletedTask;
}
";
	
	private const string CorrectUsage = @"
public Task RunAsync() {
	return DoSomethingAsync();
}
";
	
	private const string CorrectUsage2 = @"
public Task<int> RunAsync() {
	return GetSomethingAsync();
}
";
	
	private const string CorrectUsageWithMultipleStatements = @"
public Task RunAsync() {
	var guid = Guid.NewGuid();
	Console.WriteLine(guid);

	return DoSomethingAsync(); 
}
";
	
	private const string CorrectUsageWithMultipleStatements2 = @"
public Task<int> RunAsync() {
	var guid = Guid.NewGuid();
	Console.WriteLine(guid);

	return GetSomethingAsync(); 
}
";
	
	private const string CorrectUsageWithMultipleReturnStatements = @"
public Task RunAsync() {
	var guid = Guid.NewGuid().ToString();
	if(guid.StartsWith(""a"")) {
		return DoSomethingAsync();
	}

	return Task.CompletedTask;
}
";
	
	private const string CorrectUsageWithMultipleReturnStatements2 = @"
public Task RunAsync() {
	var guid = Guid.NewGuid().ToString();
	if(guid.StartsWith(""a"")) {
		return Task.FromResult(5);
	}

	return GetSomethingAsync();
}
";
	
	private const string MixedReturn = @"
public async Task<int> RunAsync()
{
	var guid = Guid.NewGuid();
	if (guid.ToString().StartsWith(""a""))
	{
		return await Task.FromResult(2);
	}

	return 6;
}
";
	
	private const string InUsingBlock = @"
public async Task RunAsync() {
	using (var _ = new MyDisposable()) {
		await DoSomethingAsync();
	}
}
";
	
	private const string InUsingBlockWithReturn = @"
public async Task RunAsync() {
	using (var _ = new MyDisposable()) {
		await DoSomethingAsync();
		return;
	}
}
";
	
	private const string InUsingBlockWithMultipleStatements = @"
public async Task RunAsync() {
	using (var _ = new MyDisposable()) {
		await DoSomethingAsync();
	}

	await Task.CompletedTask;
}
";
	
	private const string InUsingBlockWithMultipleStatementsWithReturn = @"
public async Task RunAsync() {
	using (var _ = new MyDisposable()) {
		await DoSomethingAsync();
		return;
	}

	await Task.CompletedTask;
}
";
	
	private const string InUsingStatement = @"
public async Task RunAsync() {
	using var _ = new MyDisposable();
	await DoSomethingAsync();
}
";
	
	private const string InNestedUsingStatement = @"
public async Task RunAsync() {
	var x = new Random().Next();
	if(x == 3)
	{
		using var _ = new MyDisposable();
		await DoSomethingAsync();
		return;
	}

	await Task.CompletedTask;
}
";
	
	private const string InNestedUsingStatement2 = @"
public async Task<int> RunAsync() {
	var x = new Random().Next();
	if(x == 3)
	{
		using var _ = new MyDisposable();
		return await GetSomethingAsync();
	}

	return await Task.FromResult(5);
}
";
	
	private const string InAwaitUsingBlock = @"
public async Task RunAsync() {
	await using (var _ = new MyDisposable()) {
		await DoSomethingAsync();
	}
}
";
	
	private const string InAwaitUsingStatement = @"
public async Task RunAsync() {
	await using var _ = new MyDisposable();
	await DoSomethingAsync();
}
";
	
	private const string InTryBlock = @"
public async Task RunAsync() {
	try {
		await Task.Delay(1000);
	} catch (Exception) {
	}
}
";
	
	private const string InTryBlockWithReturn = @"
public async Task RunAsync() {
	try {
		await Task.Delay(1000);
		return;
	} catch (Exception) {
	}
}
";
	
	private const string InTryBlockWithMultipleStatements = @"
public async Task RunAsync() {
	try {
		await Task.Delay(1000);
	} catch (Exception) {
	}

	await DoSomethingAsync();
}
";
	
	private const string InTryBlockWithMultipleStatementsWithReturn = @"
public async Task RunAsync() {
	try {
		await Task.Delay(1000);
		return;
	} catch (Exception) {
	}

	await DoSomethingAsync();
}
";
	
	private const string InTryBlockWithValueReturn = @"
public async Task<int> RunAsync()
{
	try
	{
		return await GetSomethingAsync();
	}
	catch (Exception)
	{
		return 2;
	}
}
";
	
	private const string MultipleAwaitExpressions = @"
public async Task RunAsync() {
	var x = 2;
	await Task.Delay(1000);
	Console.WriteLine(x);
	await DoSomethingAsync();
}
";
	
	private const string MultipleAwaitExpressionsNested = @"
public async Task RunAsync() {
	var x = 2;
	if(x % 2 == 0) {
		await Task.Delay(1000);
	}

	Console.WriteLine(x);
	await DoSomethingAsync();
}
";
	
	private const string MultipleAwaitExpressionsNestedWithReturn = @"
private async Task<int> RunAsync()
{
	var x = new Random().Next();
	if(x == 3)
	{
		await DoSomethingAsync();
		return 2;
	}

	return await Task.FromResult(1000);
}
";
	
	private const string MultipleAwaitExpressionsInLoop = @"
public async Task RunAsync() {
	for(var i = 0; i < 10; i++) {
		await Task.Delay(200);
	}
}
";
	
	private const string CovariantReturnType = @"
public async Task<IEnumerable<int>> RunAsync() {
	return await GetListAsync();
}
";
	
	private const string CovariantReturnTypeWithMethodCall = @"
public async Task<IEnumerable<int>> RunAsync() {
	return await _dataService.GetListAsync();
}
";

	private const string CorrectLambdaExpression = @"
public void Run() {
	Accept(() => DoSomethingAsync());
}
";
	
	private const string CorrectLambdaExpression2 = @"
public void Run() {
	Accept(DoSomethingAsync);
}
";

	private const string CorrectLambdaExpressionWithReturn = @"
public void Run() {
	AcceptValue(() => GetSomethingAsync());
}
";
	
	private const string CorrectLambdaExpressionWithReturn2 = @"
public void Run() {
	AcceptValue(GetSomethingAsync);
}
";
	
	private const string CorrectLambdaBlock = @"
public async Task RunAsync() {
	Accept(() => {
		var x = Guid.NewGuid();

		return DoSomethingAsync();
	});
}
";
	
	private const string CorrectLambdaBlockWithReturn = @"
public async Task RunAsync() {
	AcceptValue(() => {
		var x = Guid.NewGuid();

		return GetSomethingAsync();
	});
}
";

	#endregion
	
	[Theory]
	[InlineData(SingleAwait, SingleAwaitFixed)]
	[InlineData(SingleAwait2, SingleAwaitFixed2)]
	[InlineData(SingleAwaitExpression, SingleAwaitExpressionFixed)]
	[InlineData(SingleAwaitWithReturnExpression, SingleAwaitWithReturnExpressionFixed)]
	[InlineData(SingleAwaitWithReturn, SingleAwaitWithReturnFixed)]
	[InlineData(MultipleStatementsWithSingleAwait, MultipleStatementsWithSingleAwaitFixed)]
	[InlineData(MultipleStatementsWithReturn, MultipleStatementsWithReturnFixed)]
	[InlineData(MultipleReturnStatements, MultipleReturnStatementsFixed, 3)]
	[InlineData(WithNonRelevantUsingBlock, WithNonRelevantUsingBlockFixed, 2)]
	[InlineData(WithNonRelevantTryBlock, WithNonRelevantTryBlockFixed)]
	[InlineData(WithMultipleAwaits, WithMultipleAwaitsFixed, 2)]
	[InlineData(LocalFunction, LocalFunctionFixed)]
	[InlineData(WithConfigureAwait, WithConfigureAwaitFixed, 2)]
	[InlineData(WithUnrelatedUsingStatement, WithUnrelatedUsingStatementFixed)]
	[InlineData(LambdaExpression, LambdaExpressionFixed)]
	[InlineData(LambdaExpressionWithReturn, LambdaExpressionWithReturnFixed)]
	[InlineData(LambdaBlock, LambdaBlockFixed, 2)]
	[InlineData(LambdaBlockWithReturn, LambdaBlockWithReturnFixed, 2)]
	public Task ShouldRaise(string method, string fixedMethod, int diagnosticLocations = 1)
	{
		var source = string.Format(Scaffold, method);
		var fixedSource = string.Format(Scaffold, fixedMethod);
		var expectedDiagnostic = new DiagnosticResult(DiagnosticDescriptors.ReturnTaskDirectly);
		for (var i = 0; i < diagnosticLocations; i++)
		{
			expectedDiagnostic = expectedDiagnostic.WithLocation(i);
		}

		return Verify.VerifyCodeFixAsync(source, expectedDiagnostic, fixedSource);
	}

	[Theory]
	[InlineData(NonTaskMethod)]
	[InlineData(NonTaskMethod2)]
	[InlineData(NoAwait)]
	[InlineData(AsyncVoidMethod)]
	[InlineData(CorrectUsage)]
	[InlineData(CorrectUsage2)]
	[InlineData(CorrectUsageWithMultipleStatements)]
	[InlineData(CorrectUsageWithMultipleStatements2)]
	[InlineData(CorrectUsageWithMultipleReturnStatements)]
	[InlineData(CorrectUsageWithMultipleReturnStatements2)]
	[InlineData(MixedReturn)]
	[InlineData(InUsingBlock)]
	[InlineData(InUsingBlockWithReturn)]
	[InlineData(InUsingBlockWithMultipleStatements)]
	[InlineData(InUsingBlockWithMultipleStatementsWithReturn)]
	[InlineData(InUsingStatement)]
	[InlineData(InNestedUsingStatement)]
	[InlineData(InNestedUsingStatement2)]
	[InlineData(InAwaitUsingBlock)]
	[InlineData(InAwaitUsingStatement)]
	[InlineData(InTryBlock)]
	[InlineData(InTryBlockWithReturn)]
	[InlineData(InTryBlockWithMultipleStatements)]
	[InlineData(InTryBlockWithMultipleStatementsWithReturn)]
	[InlineData(InTryBlockWithValueReturn)]
	[InlineData(MultipleAwaitExpressions)]
	[InlineData(MultipleAwaitExpressionsNested)]
	[InlineData(MultipleAwaitExpressionsNestedWithReturn)]
	[InlineData(MultipleAwaitExpressionsInLoop)]
	[InlineData(CovariantReturnType)]
	[InlineData(CovariantReturnTypeWithMethodCall)]
	[InlineData(CorrectLambdaExpression)]
	[InlineData(CorrectLambdaExpression2)]
	[InlineData(CorrectLambdaExpressionWithReturn)]
	[InlineData(CorrectLambdaExpressionWithReturn2)]
	[InlineData(CorrectLambdaBlock)]
	[InlineData(CorrectLambdaBlockWithReturn)]
	public Task ShouldNotRaise(string method)
	{
		var source = string.Format(Scaffold, method);
		
		return Verify.VerifyAnalyzerAsync(source);
	}
}