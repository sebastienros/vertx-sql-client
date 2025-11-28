## Building

- Run `dotnet build` to build the solution

## Testing

- Run `dotnet test` to run all the tests
- Use `--filter-class` to run all methods in a given test class. Pass one or more fully qualified type names (i.e., 'MyNamespace.MyClass' or 'MyNamespace.MyClass+InnerClass'). 
- Use `--filter-method` to run a given test method. Pass one or more fully qualified method names (i.e., 'MyNamespace.MyClass.MyTestMethod').

Note: 
- Wildcard '*' is supported at the beginning and/or end of each filter.
- Specifying more than one is an OR operation.
- The the solution is already built, use the `--no-build` to skip the build before starting tests.
