# .NET Testing Guidelines

Modern testing patterns using TUnit, AwesomeAssertions, and TestContainers.

## Table of Contents

- [Core Principles](#core-principles)
- [Test Framework: TUnit](#test-framework-tunit)
- [Assertions: AwesomeAssertions](#assertions-awesomeassertions)
- [Mocking: FakeItEasy](#mocking-fakeiteasy)
- [Integration Testing](#integration-testing)
- [Test Organization](#test-organization)
- [Best Practices](#best-practices)
- [Quality Gates](#quality-gates)
- [Migration: xUnit to TUnit](#migration-xunit-to-tunit)
- [Troubleshooting](#troubleshooting)
- [References](#references)

For running TUnit tests, see [RUN-TUNIT-TESTS.md](RUN-TUNIT-TESTS.md).

## Core Principles

1. **Add tests for every behavioral change**
2. **Sociable tests** - Use real objects with actual dependencies, avoid over-mocking. Unit is not necessarily a single class.
3. **Test behavior** not implementation
4. **High-level integration tests preferred** - Test through HTTP endpoints when possible to cover full stack
5. **Real dependencies** - Use TestContainers for databases, WebApplicationFactory for APIs
6. **AAA Pattern** - Arrange, Act, Assert

## Test Framework: TUnit

| Feature       | Usage                                                                  |
| ------------- | ---------------------------------------------------------------------- |
| Test method   | `[Test]` attribute, async by default                                   |
| Parameterized | `[Arguments]`, `[Matrix]`, `[MethodDataSource]`                        |
| Lifecycle     | `[Before(Test)]`, `[After(Test)]`, `[Before(Class)]`, `[After(Class)]` |

**Naming:** `MethodName_Scenario_ExpectedResult`

## Assertions: AwesomeAssertions

AwesomeAssertions is a fork of FluentAssertions with a different license. Uses FluentAssertions syntax.

Use `using var _ = new AssertionScope();` to report all failures together.

## Mocking: FakeItEasy

**Mocking is a last resort.** Prefer real implementations and TestContainers. Use mocks only for external dependencies that cannot run in containers.

## Integration Testing

| Tool                  | Purpose                                                        |
| --------------------- | -------------------------------------------------------------- |
| WebApplicationFactory | Test ASP.NET Core apps end-to-end through HTTP                 |
| TestContainers        | Real databases in Docker (PostgreSQL, SQL Server, Redis, etc.) |
| FakeTimeProvider      | Test time-dependent code (`Microsoft.Extensions.Time.Testing`) |

## Test Organization

```
src/Features/Orders/
  OrderService.cs

tests/Features/Orders/
  OrderServiceTests.cs
```

**File naming:** `{ClassName}Tests.cs`
**Directory structure:** Mirror `src` structure

## Best Practices

**DO:**

- Write tests alongside code (TDD)
- Test behavior, not implementation
- Use descriptive names: `MethodName_Scenario_ExpectedResult`
- Keep tests isolated (no shared state)
- Use real implementations - mocking is last resort
- Sociable tests over isolated unit tests
- Use AssertionScope for multiple assertions
- Test edge cases (null, empty, boundaries, exceptions)
- Clean up resources (`IAsyncLifetime`, `IDisposable`)
- Use high-level integration tests - WebApplicationFactory for spinning up the full app
- Use TestContainers for real databases
- Use `FakeTimeProvider` for time-dependent code

**DON'T:**

- Mock internal dependencies (use real implementations or TestContainers)
- Write brittle tests (avoid coupling to implementation)
- Share state between tests
- Ignore flaky tests (fix or remove)
- Test private methods directly
- Use `Thread.Sleep` (use `Task.Delay` or `FakeTimeProvider`)
- Catch exceptions in tests (use exception assertions)
- Mock DbContext (use in-memory or TestContainers)

**For running TUnit tests**, see [RUN-TUNIT-TESTS.md](RUN-TUNIT-TESTS.md) for comprehensive guide on filtering, parallelization, debugging, and CI/CD patterns.

## Quality Gates

Run before committing: `dotnet build && dotnet test && dotnet format --verify-no-changes`

## Migration: xUnit to TUnit

| xUnit                       | TUnit                                         |
| --------------------------- | --------------------------------------------- |
| `[Fact]`                    | `[Test]`                                      |
| `[Theory]` + `[InlineData]` | `[Test]` + `[Arguments]`                      |
| `[Theory]` + `[MemberData]` | `[Test]` + `[Matrix]` or `[MethodDataSource]` |
| `IClassFixture<T>`          | `[Before(Class)]` / `[After(Class)]`          |

|

## Troubleshooting

| Issue       | Fix                                                                                   |
| ----------- | ------------------------------------------------------------------------------------- |
| Flaky tests | Check shared state, use `FakeTimeProvider`, use TestContainers, ensure cleanup        |
| Slow tests  | Profile, reduce DB/network calls, parallelize, optimize setup                         |
| CI failures | Check timezones (use `TimeProvider.GetUtcNow()`), verify env vars, use TestContainers |

## References

- [TUnit Documentation](https://github.com/thomhurst/TUnit)
- [AwesomeAssertions Documentation](https://github.com/thomhurst/AwesomeAssertions)
- [FakeItEasy Documentation](https://fakeiteasy.github.io/)
- [TestContainers for .NET](https://dotnet.testcontainers.org/)
- [WebApplicationFactory](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [FakeTimeProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.time.testing.faketimeprovider)
