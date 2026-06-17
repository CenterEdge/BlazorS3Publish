# AGENTS.md

## General

- Make only high confidence suggestions when reviewing code changes.
- Always use C# 14 language features.
- `TreatWarningsAsErrors` is enabled across the solution. Warnings must be fixed or suppressed with a justification comment.
- Always use nullable reference types. `Nullable` is enabled across the solution.
- Never change `global.json` unless explicitly asked to.
- Never change `NuGet.config` files unless explicitly asked to. In particular, never commit credentials to `NuGet.config` files.
- Build output uses `UseArtifactsOutput` and is found in `src/artifacts/`.

## Formatting

- Apply the code-formatting style defined in `.editorconfig`.
- Prefer file-scoped namespace declarations and single-line using directives.
- Insert a newline before the opening curly brace of any code block (e.g., after `if`, `for`, `while`, `foreach`, `using`, `try`, etc.).
- Insert a blank line after the closing curly brace of any code block unless the next line is also a closing curly brace.
- Ensure that the final return statement of a method is on its own line.
- Use pattern matching and switch expressions wherever possible.
- Use `nameof` instead of string literals when referring to member names.

### Code comments

- Err on the side of over-commenting code when the reasoning is not obvious. Comments should explain **WHY** code is written a particular way; the **WHY** is the most important part.
- Do comment non-obvious implementation details: concurrency hazards, lifecycle constraints, compatibility requirements, platform quirks, upstream workarounds, and intentional deviations from the obvious helper or API.
- When parsing strings, logs, command output, protocol payloads, or other loosely structured data, include a comment with an example of the raw format being parsed. Show edge cases, escaping rules, delimiters, optional fields, or malformed-but-observed inputs when they affect the parser.
- When code follows an external standard, protocol, or ecosystem convention, include valid links to the relevant source material so future readers can verify the rule and understand why the code follows it.
- Do not add comments that simply narrate clear code, such as "set the timeout" immediately before assigning a timeout.
- Keep workaround comments close to the workaround. Include an issue link when the workaround is tied to an upstream bug, and describe the condition for removing it when that is known.

Good comments explain the constraint or tradeoff:

```csharp
// Read both streams concurrently to avoid deadlock when a pipe buffer fills.
var stdoutTask = process.StandardOutput.ReadToEndAsync();
var stderrTask = process.StandardError.ReadToEndAsync();
```

```csharp
// Endpoint adoption runs on the command path, so fail quickly when stale metadata
// points at a dead or reused port.
var timeout = TimeSpan.FromSeconds(2);
```

```csharp
// The temporary config is disposed when this method returns. That is intentional:
// only `dotnet new install` consumes the config; later template creation uses the
// already-installed template hive and ambient NuGet configuration.
using var temporaryConfig = await TemporaryNuGetConfig.CreateAsync(mappings);
```

```csharp
// Workaround for an upstream library bug on Windows where URI SANs are formatted
// differently than the verifier expects. Cryptographic verification still runs;
// only the identity checks are performed manually from the certificate extensions.
var result = await VerifyWithManualIdentityFallbackAsync(bundle, cancellationToken);
```

```csharp
public required IReadOnlyList<PipelineStep> Steps
{
    get;
    init
    {
        field = value;
        // IMPORTANT: The ResourceNameComparer must be used here to ensure correct lookup behavior
        // based on resource names, NOT the default reference equality. This is because resources
        // may be swapped out (referred to as bait-and-switch) during model transformations.
        StepToResourceMap = field.ToLookup(s => s.Resource, s => s, new ResourceNameComparer());
    }
}
```

```csharp
// Output sensitive message content for GenAI.
// A convention for libraries that output GenAI telemetry is to use
// `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`.
// See:
// - https://opentelemetry.io/blog/2024/otel-generative-ai/
// - https://github.com/search?q=org%3Aopen-telemetry+OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT&type=code
context.EnvironmentVariables[KnownOtelConfigNames.InstrumentationGenAiCaptureMessageContent] = "true";
```

```csharp
// If we have multiple endpoints for the same scheme, differentiate them by appending a number.
// Start numbering with the second endpoint so the first stays just http/https, which preserves
// the same behavior as "dotnet run". Only do this in Run mode because, in Publish mode, those
// extra endpoints with generic names would not be easily usable.
var endpointName = bindingAddress.Scheme;
if (endpointCountByScheme[bindingAddress.Scheme] > 1)
{
    endpointName += endpointCountByScheme[bindingAddress.Scheme];
}
```

```csharp
// The implementation here is less than ideal, but we don't have a clean way of building resource
// types that change their behavior based on context. In this case, publish mode needs the resource
// to behave like a ContainerResource instead of a ProjectResource, so we remove the ProjectResource
// from the application model and add a new ContainerResource in its place.
//
// There are still dangling references to the original ProjectResource in the application model, but
// in publish mode it won't be used. This is a limitation of the current design.
builder.ApplicationBuilder.Resources.Remove(builder.Resource);
```

Parsing comments should show the raw shape and important edge cases:

```csharp
// Parse resource log lines emitted as:
//   [2026-05-10T18:34:22.123Z] frontend stdout: Now listening on: http://localhost:5221
// The message can contain additional ':' characters, so split only on the first
// " stdout: " or " stderr: " delimiter after the resource name.
var match = s_logLineRegex.Match(line);
```

```csharp
// The endpoint metadata sidecar uses the DevTools /json/version shape:
//   { "webSocketDebuggerUrl": "ws://127.0.0.1:50981/devtools/browser/<id>" }
// Older Chromium builds can omit the property while the browser is still starting;
// treat that as a retryable probe failure rather than invalid metadata.
var endpoint = payload.WebSocketDebuggerUrl;
```

Avoid comments that restate the code:

```csharp
// Set the timeout to two seconds.
var timeout = TimeSpan.FromSeconds(2);

// Create a list.
var resources = new List<Resource>();
```

### Nullable Reference Types

- Always use `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.
