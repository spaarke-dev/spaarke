using System.Text;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Deterministic mid-elicitation turn routing (FR-P2-03 / canonical walkthrough
/// steps 10-12): while an elicitation gate is pending, an incoming utterance is an
/// ANSWER to the pending invocation UNLESS it is a hard-slash command or an
/// explicit restart. Both escapes are exact string checks — this class contains
/// the platform's ONLY answer-vs-command disambiguation and it is deliberately
/// classification-free (ADR-039 MUST NOT: no second intent-detection mechanism;
/// the deterministic escape set below is the ratified OQ-3 contract, not a
/// heuristic to grow).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — client
/// <c>HardSlashExecutor</c> handles hard slashes CLIENT-side; no server-side home
/// exists for the answer-vs-escape decision or the elicitation turn framing.
/// (2) <i>Extension</i> — the logic is shared by <c>ChatEndpoints</c> (turn
/// routing) and <see cref="BindingCapabilityTool"/> (clarify/modal messages);
/// a single pure static home keeps the "deterministic only" property auditable.
/// (3) <i>Cost-of-doing-nothing</i> — walkthrough step 12 fails: "7/9/2026 and
/// yes me" would re-enter cold dispatch instead of resolving the pending
/// create-task invocation.
/// </para>
/// <para>
/// All builders are grounded (ADR-039): field wording comes exclusively from the
/// declared schema (<see cref="ElicitationField"/> name + maker prompt); no field
/// is ever invented here.
/// </para>
/// </remarks>
public static class ElicitationTurnRouter
{
    /// <summary>
    /// The closed, exact-match (case-insensitive, trimmed, trailing punctuation
    /// ignored) explicit-restart vocabulary. An utterance equal to one of these
    /// breaks out of a pending elicitation deterministically.
    /// </summary>
    internal static readonly IReadOnlyList<string> RestartPhrases = new[]
    {
        "restart", "start over", "cancel", "never mind", "nevermind", "forget it", "stop",
    };

    /// <summary>
    /// The closed, exact-match (case-insensitive, trimmed, trailing punctuation ignored) bare-
    /// affirmation vocabulary. An utterance equal to one of these is a bare "go ahead" with no
    /// content of its own — the Confirmation Policy v2 origin classifier
    /// (<c>Services/Ai/Chat/Gate/RequestOriginClassifier.cs</c>, E-1) binds it to the immediately-
    /// preceding model proposal instead of treating it as an independent explicit request.
    /// Deliberately closed (ADR-039 MUST NOT: no growing heuristic) — the same discipline as
    /// <see cref="RestartPhrases"/>.
    /// </summary>
    internal static readonly IReadOnlyList<string> AffirmationPhrases = new[]
    {
        "go ahead", "yes", "yes please", "do it", "ok", "okay", "sure",
        "proceed", "confirm", "go for it", "yep", "yeah", "please do",
    };

    /// <summary>Hard-slash escape: the utterance is a slash command (deterministic prefix check).</summary>
    public static bool IsHardSlash(string? message)
        => !string.IsNullOrWhiteSpace(message) && message.TrimStart().StartsWith('/');

    /// <summary>
    /// Deterministic bare-affirmation check (Policy v2 note E-1): true when the utterance is
    /// exactly one of the closed <see cref="AffirmationPhrases"/> (trimmed, trailing
    /// <c>. ! ?</c> ignored, case-insensitive). A bare affirmation carries no request of its
    /// own — the origin classifier binds it to the preceding single-complete proposal or
    /// falls back to <c>inferred</c>. Anything with additional content (e.g. "yes, and also …")
    /// is NOT a bare affirmation and does not match.
    /// </summary>
    public static bool ClassifyBareAffirmation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim().TrimEnd('.', '!', '?').Trim();
        foreach (var phrase in AffirmationPhrases)
        {
            if (string.Equals(normalized, phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Explicit-restart escape: exact match against the closed restart vocabulary.</summary>
    public static bool IsExplicitRestart(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim().TrimEnd('.', '!', '?');
        foreach (var phrase in RestartPhrases)
        {
            if (string.Equals(normalized, phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Grounded tool result returned to the model when a capability invocation is
    /// missing required args under <c>capture_mode: loop-elicitation</c> — instructs
    /// the model to produce THE clarifying turn using only declared field names +
    /// maker prompts, then re-invoke once the user answers.
    /// </summary>
    public static string BuildClarifyInstruction(
        string consumerType,
        string toolName,
        IReadOnlyList<ElicitationField> missingFields)
    {
        var sb = new StringBuilder();
        sb.Append("MISSING REQUIRED INPUTS for capability '").Append(consumerType).Append("': ");
        sb.Append(DescribeFields(missingFields));
        sb.Append(". The invocation is suspended (it did NOT execute). ");
        sb.Append("Ask the user ONE concise clarifying question that collects exactly these missing inputs, ");
        sb.Append("using their declared names/prompts above — do not invent, rename, or add fields, and do not guess values. ");
        sb.Append("When the user answers, invoke '").Append(toolName);
        sb.Append("' again ONCE with the arguments already provided plus the answered values.");
        return sb.ToString();
    }

    /// <summary>
    /// Grounded tool result returned to the model when the Binding declares
    /// <c>capture_mode: modal</c> — the wizard surface collects the fields;
    /// the model must NOT run conversational Q&amp;A in parallel.
    /// </summary>
    public static string BuildModalNotice(
        string consumerType,
        IReadOnlyList<ElicitationField> missingFields)
    {
        return
            $"INPUT FORM OPENED for capability '{consumerType}' — this capability collects its remaining " +
            $"required inputs ({string.Join(", ", missingFields.Select(f => f.Name))}) in a form, not in chat. " +
            "Tell the user briefly to complete the form that just opened. Do not ask for these values in chat " +
            "and do not invoke this capability again this turn.";
    }

    /// <summary>
    /// Frames a mid-elicitation utterance as the ANSWER to the pending invocation
    /// (walkthrough step 12). The frame is prepended to the effective message handed
    /// to the loop (same composed-message pattern as attachments, FR-07) — the
    /// PERSISTED history keeps the user's original text. The routing here is state
    /// (a pending ledger gate exists), not intent detection.
    /// </summary>
    public static string BuildAnswerFrame(PendingInvocation pending, string userMessage)
    {
        var fields = pending.MissingFields is { Count: > 0 }
            ? string.Join(", ", pending.MissingFields)
            : "(unrecorded)";

        var sb = new StringBuilder();
        sb.AppendLine("[PLATFORM ELICITATION CONTEXT — not user text]");
        sb.Append("An invocation of the capability tool '").Append(pending.ToolId)
          .Append("' is suspended, awaiting these missing required inputs: ").Append(fields).AppendLine(".");
        sb.Append("Arguments already provided (JSON): ").AppendLine(pending.ArgsJson);
        sb.Append("Treat the user's message below as answers to the missing inputs. Parse each answer into its ")
          .Append("declared field and invoke '").Append(pending.ToolId)
          .AppendLine("' again ONCE with the previously provided arguments plus the parsed values.");
        sb.AppendLine("Do not start a different task. If the message does not supply every missing input, ask only for what is still missing.");
        sb.AppendLine();
        sb.Append("USER MESSAGE: ").Append(userMessage);
        return sb.ToString();
    }

    private static string DescribeFields(IReadOnlyList<ElicitationField> fields)
        => string.Join("; ", fields.Select(f =>
            string.IsNullOrWhiteSpace(f.Prompt)
                ? $"'{f.Name}'"
                : $"'{f.Name}' ({f.Prompt})"));
}
