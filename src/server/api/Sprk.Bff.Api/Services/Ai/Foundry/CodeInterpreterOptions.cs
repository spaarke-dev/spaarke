using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Configuration options for <see cref="CodeInterpreterBridge"/> and <see cref="Chat.Tools.CodeInterpreterTools"/>.
///
/// Bound from appsettings section <c>CodeInterpreter</c> in Program.cs / ConfigurationModule.cs.
///
/// ADR-018: Kill switch — <see cref="Enabled"/> must be checked before every Code Interpreter
///          tool invocation. When <c>false</c>, tools return a user-readable unavailability string
///          instead of throwing an exception, so the AI model can gracefully inform the user.
///
/// ADR-016: Concurrency gate — <see cref="MaxConcurrency"/> controls the <see cref="System.Threading.SemaphoreSlim"/>
///          in <see cref="Chat.Tools.CodeInterpreterTools"/>. Calls that cannot acquire within
///          <see cref="SandboxTimeoutSeconds"/> receive a 429-equivalent rejection string.
///
/// ADR-015: Data governance — only caller-supplied data excerpts are forwarded to the sandbox.
///          Full documents, PII, and raw content MUST NOT be sent to the Code Interpreter.
/// </summary>
public sealed class CodeInterpreterOptions
{
    /// <summary>Configuration section name used for binding in ConfigurationModule.cs.</summary>
    public const string SectionName = "CodeInterpreter";

    /// <summary>
    /// Kill switch (ADR-018). When <c>false</c>, <see cref="Chat.Tools.CodeInterpreterTools"/>
    /// returns a graceful unavailability string on every call rather than invoking the sandbox.
    /// Default: <c>false</c> (opt-in — must be explicitly enabled in configuration).
    /// </summary>
    [Required]
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Maximum number of concurrent Code Interpreter sandbox invocations per BFF instance (ADR-016).
    /// Enforced via a static <see cref="System.Threading.SemaphoreSlim"/> in
    /// <see cref="Chat.Tools.CodeInterpreterTools"/>. Requests that cannot acquire the semaphore
    /// within <see cref="SandboxTimeoutSeconds"/> return a 429-equivalent user-readable message.
    /// Default: 2 concurrent sandbox calls.
    /// </summary>
    [Required]
    [Range(1, 32)]
    public int MaxConcurrency { get; init; } = 2;

    /// <summary>
    /// Timeout in seconds to wait for the concurrency semaphore and for a single Code Interpreter
    /// sandbox run to complete before returning a timeout message to the AI model (ADR-016).
    /// Default: 30 seconds.
    /// </summary>
    [Required]
    [Range(5, 300)]
    public int SandboxTimeoutSeconds { get; init; } = 30;
}
