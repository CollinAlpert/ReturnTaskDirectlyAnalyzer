# ReturnTaskDirectlyAnalyzer

This Roslyn Analyzer warns developers about using the async/await pattern in a way where, instead of awaiting a ``Task``, it could be returned directly.\
This is better for many reasons, first and foremost because the compiler doesn't need to generate a state machine for the async method during compile time and awaiting the resulting ``Task`` is deferred to the caller.\
Here's a few methods where this analyzer kicks in:
```c#
public async Task RunAsync() {
    await Task.Delay(1000);
}

public async Task RunAsync() {
    var random = new Random().Next();
    if(random % 2 == 0) {
        await Task.Delay(1000).ConfigureAwait(false);
        return;
    }
    
    await DoSomethingAsync(); 
}

public async Task<int> RunAsync() {
    var random = new Random().Next();
    if(random % 2 == 0) {
        return await GetSomeValueAsync();
    }
    
    return await GetSomeOtherValueAsync();
}
```
After applying the code fix which the analyzer supplies, these methods would be transformed to the following:
```c#
public Task RunAsync() {
    return Task.Delay(1000);
}

public Task RunAsync() {
    var random = new Random().Next();
    if(random % 2 == 0) {
        return Task.Delay(1000);
    }
    
    return DoSomethingAsync(); 
}

public Task<int> RunAsync() {
    var random = new Random().Next();
    if(random % 2 == 0) {
        return GetSomeValueAsync();
    }
    
    return GetSomeOtherValueAsync();
}
```
The analyzer will not report when a ``Task`` is awaited within a `try` or a `using` block.\
To see a list of all supported cases, please have a look at the [ReturnTaskDirectlyTests.cs](https://github.com/CollinAlpert/ReturnTaskDirectlyTests/blob/master/ReturnTaskDirectlyAnalyzer.Tests/ReturnTaskDirectlyTests.cs) file.

## Installation
This analyzer is published in [NuGet](https://nuget.org/packages/ReturnTaskDirectlyAnalyzer) and can be installed using the following methods.\
Using the .NET Core command-line interface:
```
dotnet add package ReturnTaskDirectlyAnalyzer
```
The NuGet package manager console:
```
Install-Package ReturnTaskDirectlyAnalyzer
```
Or by adding the ``PackageReference`` to your project manually and supplying the latest version:
```
<PackageReference Inlude="ReturnTaskDirectlyAnalyzer" Version="xxx" />
```

## Contributing
If you identify a false positive (the analyzer reports an `await` when it shouldn't), please do the following:
1. Fork the repository (if you haven't already)
2. Create a new branch from ``master``
3. Add a test demonstrating the false positive in the `ShouldNotRaise` section of the analyzer's tests.
4. Open a PR with the failing test which targets the `master` branch.
I will then push a fix in your PR and complete it. If you know what the fix is and can supply it within the PR, even better :)

If you have identified a case where the analyzer does not report an ``await`` even though it should, follow the same steps as above, however add the test in the `ShouldRaise` section.

If you are ever not sure about the analyzer's behavior, are having trouble creating a test or have general questions, please feel free to open an issue.