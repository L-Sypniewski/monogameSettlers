# Running TUnit Tests

Complete guide for executing existing TUnit tests with filtering, parallelization, and debugging.

## Basic Execution

```bash
# Run all tests in solution
dotnet test

# Run tests in specific project
dotnet test path/to/MyProject.Tests.csproj

# Run with specific verbosity
dotnet test --verbosity minimal    # Less output
dotnet test --verbosity normal     # Default
dotnet test --verbosity detailed   # More output
```

## Filtering Tests

TUnit uses `--treenode-filter` for precise test filtering with pattern: `/{assembly}/{namespace}/{class}/{test}[property=value]`

### By Test Name

```bash
# Exact test name
dotnet test -- --treenode-filter /*/*/*/AcceptCookiesTest

# Multiple test names (wildcard)
dotnet test -- --treenode-filter /*/*/*/Accept*Test
```

### By Class

```bash
# All tests in LoginTests class
dotnet test -- --treenode-filter /*/*/LoginTests/*

# All tests in multiple classes
dotnet test -- --treenode-filter /*/*/Login*/*
```

### By Namespace

```bash
# All tests in Features.Orders namespace
dotnet test -- --treenode-filter /*/Features.Orders/*/*

# All tests in Features namespace (including sub-namespaces)
dotnet test -- --treenode-filter /*/Features*/*/*
```

### By Category/Property

```bash
# Exclude specific category
dotnet test -- --treenode-filter "/*/*/*/*[Category!=Performance]"

# Include specific category
dotnet test -- --treenode-filter "/*/*/*/*[Category=Unit]"

# Custom property filtering
dotnet test -- --treenode-filter "/*/*/*/*[Priority=High]"
```

### Combined Filters

```bash
# LoginTests but exclude performance tests
dotnet test -- --treenode-filter "/*/*/LoginTests/*[Category!=Performance]"
```

## Test Categories

If tests are organized with `[Category]` attributes, you can run them selectively:

### Run by Category

```bash
# Fast tests first
dotnet test -- --treenode-filter "/*/*/*/*[Category=Unit]"

# Integration tests
dotnet test -- --treenode-filter "/*/*/*/*[Category=Integration]"

# E2E tests last
dotnet test -- --treenode-filter "/*/*/*/*[Category=E2E]"

# Exclude slow tests during development
dotnet test -- --treenode-filter "/*/*/*/*[Category!=E2E]"
```

## Parallel Execution

TUnit runs tests in parallel by default. Control parallelism when running tests:

### Command Line

```bash
# Limit maximum parallel tests
dotnet test -- --maximum-parallel-tests 8

# Limit to CPU count
dotnet test -- --maximum-parallel-tests 4
```

**Note**: Tests with `[ParallelLimit]`, `[ParallelGroup]`, or `[NotInParallel]` attributes will respect those constraints automatically when executed.

## Debugging Tests

### Enable Diagnostics

```bash
# TUnit-specific diagnostic logging
dotnet test -- --diagnostic

# Detailed console output
dotnet test --logger "console;verbosity=detailed"

# Control framework log level
dotnet test -- --log-level Debug   # Detailed stack traces, timing
dotnet test -- --log-level Trace   # Maximum verbosity
```

### Debugging in IDE

- Set breakpoints in test code
- Run "Debug Test" from test explorer
- TUnit's simplified architecture makes debugging straightforward

### Test Output Control

```bash
# Normal: Show failures only, buffer output (no real-time spam)
dotnet test -- --output Normal

# Detailed: Show all tests + real-time test output
dotnet test -- --output Detailed
```

## Common Patterns

### Development Workflow

```bash
# Quick feedback loop - fast tests only
dotnet test -- --treenode-filter "/*/*/*/*[Category=Unit]"

# Pre-commit - all tests except E2E
dotnet test -- --treenode-filter "/*/*/*/*[Category!=E2E]"

# Full validation - all tests
dotnet test
```

### CI/CD Pipeline

```bash
# Stage 1: Fast unit tests (fail fast)
dotnet test -- --treenode-filter "/*/*/*/*[Category=Unit]" --no-build

# Stage 2: Integration tests
dotnet test -- --treenode-filter "/*/*/*/*[Category=Integration]" --no-build

# Stage 3: E2E tests (expensive, run last)
dotnet test -- --treenode-filter "/*/*/*/*[Category=E2E]" --no-build
```

### Debugging Failing Tests

```bash
# Run specific failing test with diagnostics
dotnet test -- --treenode-filter /*/*/*/FailingTestName --diagnostic --output Detailed

# Run failing test class with detailed output
dotnet test -- --treenode-filter /*/*/FailingTestClass/* --log-level Debug
```

## Troubleshooting

| Issue                       | Solution                                                                                         |
| --------------------------- | ------------------------------------------------------------------------------------------------ |
| Tests not discovered        | Check `[Test]` attribute, ensure project references TUnit, rebuild                               |
| Parallel execution issues   | Use `[ParallelGroup]` or `[NotInParallel]`, check shared state                                   |
| Flaky tests                 | Use `FakeTimeProvider`, ensure cleanup, check TestContainers networking                          |
| Slow test execution         | Enable parallel execution, use `[Category]` to run fast tests first, profile with `--diagnostic` |
| Filter not working          | Check pattern syntax: `/{assembly}/{namespace}/{class}/{test}[property=value]`                   |
| Missing test output         | Use `--output Detailed` or `--logger "console;verbosity=detailed"`                               |
| CI failures (works locally) | Check timezones (use UTC), verify environment variables, ensure TestContainers networking        |

## References

- [TUnit Documentation](https://github.com/thomhurst/TUnit)
- [TUnit Command Line Flags](https://github.com/thomhurst/tunit/blob/main/docs/docs/reference/command-line-flags.md)
- [TUnit Test Filters](https://github.com/thomhurst/tunit/blob/main/docs/docs/execution/test-filters.md)
- [TUnit Parallelism](https://github.com/thomhurst/tunit/blob/main/docs/docs/parallelism/parallel-groups.md)
