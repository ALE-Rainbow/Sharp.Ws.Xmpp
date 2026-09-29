# Coding Guidelines

## 1. Scope

These guidelines apply to all production code in this repository, with priority order:

1. Existing repository patterns.
2. These project guidelines.
3. Industry-standard .NET library practices.

## 2. Target Framework Compatibility

The workspace contains projects targeting `.NET Standard 2.0`, `.NET Core 3.1`, and `.NET 10`.

- Shared library code **must compile for all intended target frameworks**.
- Prefer APIs available in `.NET Standard 2.0` for shared components.
- Use conditional compilation only when unavoidable.
- Avoid introducing dependencies that drop support for existing target frameworks unless explicitly approved.

## 3. API Design (Library-First)

- Design public APIs for **stability and backward compatibility**.
- Prefer small, composable types and methods.
- Keep public surface area minimal; use `internal` for non-public implementation details.
- Use `EventHandler` / `EventHandler<TEventArgs>` for events (matches existing code).
- Preserve semantic behavior of existing APIs; treat breaking changes as major-version work.

## 4. Naming and Layout

- Use C# naming conventions:
  - `PascalCase`: public/protected types, methods, properties, events.
  - `camelCase`: local variables and private fields (existing pattern uses no `_` prefix).
- One type per file unless tightly coupled helper types are private and trivial.
- Keep file and type names aligned.

## 5. Null Validation and Argument Checks

- Validate all public method inputs.
- Follow existing repository style (`ThrowIfNull`, `ThrowIfNullOrEmpty`, explicit guard clauses) where available.
- Throw the most specific exception type:
  - `ArgumentNullException` for null
  - `ArgumentException` for invalid value/format
  - `ArgumentOutOfRangeException` for range violations

## 6. Exceptions and Error Behavior

- Do not swallow exceptions silently.
- Add context when rethrowing or wrapping.
- Preserve stack traces (`throw;`, not `throw ex;`).
- Reserve exceptions for exceptional paths; avoid exception-driven flow control.

## 7. Resource Management

- Implement `IDisposable` for types owning disposable resources (matches `XmppClient` pattern).
- Follow standard dispose pattern for non-trivial ownership.
- Dispose deterministically; use `using` where possible.
- After disposal, throw `ObjectDisposedException` from invalid operations.

## 8. Threading and Async

- Prefer async APIs for I/O-bound work in new code.
- If async is added to library code, provide `CancellationToken` where practical.
- Never block on async (`.Result`, `.Wait()`) in library internals.
- Ensure event invocation and shared state updates are thread-safe.

## 9. Documentation

- Public types and members must have XML documentation (`///`) including:
  - Purpose/behavior
  - Parameter meanings
  - Exceptions thrown
- Keep docs synchronized with behavior changes.
- Include usage examples for non-obvious APIs.

## 10. Logging and Diagnostics

- Use structured logging patterns.
- Do not log secrets, credentials, or sensitive identifiers.
- Log actionable context (operation, entity, outcome, error category).

## 11. Code Style and Maintainability

- Prefer clarity over cleverness.
- Keep methods focused; extract complex logic into private helpers.
- Remove dead/commented-out code before merge.
- Keep comments intent-focused; do not restate obvious code.
- Prefer primary constructors.

## 12. Testing Requirements

- Add/adjust tests for any behavior change.
- Cover:
  - Happy path
  - Validation/guard failures
  - Error and edge conditions
- Keep tests deterministic and isolated from external systems unless explicitly marked integration tests.

## 13. Versioning and Compatibility

- Treat public API changes as compatibility-sensitive.
- Document breaking changes explicitly.
- Maintain semantic versioning discipline for packages.

## 14. Security Baseline

- Never commit secrets, tokens, or credentials.
- Validate and sanitize all externally supplied data.
- Prefer secure defaults (encrypted transport, strict certificate validation unless explicitly configurable).

## 15. Pull Request Quality Gate

A PR is ready when all are true:

- Builds cleanly for all affected target frameworks.
- Public API changes are intentional and documented.
- XML docs updated for changed public members.
- Tests added/updated and passing.
- No debug leftovers, commented dead code, or secret material.