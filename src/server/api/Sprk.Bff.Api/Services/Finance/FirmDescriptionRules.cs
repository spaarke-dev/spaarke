// ebilling-r1 task 130e-1 (DEF-060, EB-D-212): the one rule for a firm description, the per-rule text the rule set editor writes on a policy version
// and the writer copies onto a billing Signal's firm reason.
//
// Component Justification (CLAUDE.md section 11):
//   (1) Existing - FirmRejectionFindings.IsInvisibleControl was the read side's rune predicate (130d); RuleSetService.ValidateRule validates a
//       version's other columns. Neither checks free text a firm will read.
//   (2) Extension - the predicate MOVES here (FirmRejectionFindings calls it), so the read side and every write-side check share one definition;
//       a copy in each consumer (editor, mapper, replay, writer preflight) would let them drift (design note section 3.1).
//   (3) Cost of doing nothing - partner-visible editor text would reach a Signal with markup, template braces, invisible characters or the rule
//       set's own name, and the writer, the mapper and the projection could each accept a different set of inputs.

using System.Globalization;
using System.Text;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The result of <see cref="FirmDescriptionRules.Validate(string?)"/>: either the text to store (trimmed; <c>null</c> when it was blank, which
/// means "no description") or the stable code of the first check it failed. Never carries the refused text.
/// </summary>
/// <param name="Text">The trimmed text when it passed; <c>null</c> when it was blank or refused.</param>
/// <param name="RefusalCode">One of the <see cref="FirmDescriptionRules"/> codes when refused; <c>null</c> when it passed.</param>
public readonly record struct FirmDescriptionCheck(string? Text, string? RefusalCode)
{
    /// <summary>The text passed (or was blank).</summary>
    public bool IsValid => RefusalCode is null;
}

/// <summary>
/// The ONE validation of a firm description (<c>sprk_policyversion.sprk_firmdescription</c>, DEF-060 design note section 3.1), used by the rule
/// editor and publish (<see cref="RuleSetService"/>), the billing Signal mapper and the plan replay, and the Signal writer's preflight; the
/// projection's read filter uses the same rune predicate (<see cref="IsInvisibleControl"/>).
/// </summary>
/// <remarks>
/// The checks, in the order the code runs them (the first that fails gives the code):
/// <list type="number">
/// <item>trim; a blank text means "no description" (valid, <c>null</c>);</item>
/// <item>with a rule type (<see cref="Validate(string?, string?, string?, string?)"/>): a Duplicate rule takes none (<see cref="OnDuplicate"/>);</item>
/// <item>no unpaired UTF-16 surrogate (<see cref="InvisibleCharacter"/>: it is no character at all, and rune enumeration would hide it as U+FFFD);</item>
/// <item>no rune of category Control, Format, LineSeparator or ParagraphSeparator (<see cref="InvisibleCharacter"/>);</item>
/// <item>at most <see cref="MaxRunes"/> Unicode scalars (runes, so a character outside the Basic Multilingual Plane counts once; <see cref="TooLong"/>);</item>
/// <item>no <c>{</c> or <c>}</c>, so no template placeholder can exist (<see cref="Braces"/>);</item>
/// <item>no <c>&lt;</c> or <c>&gt;</c>, no markup (<see cref="Markup"/>);</item>
/// <item>when the version's names are given, neither its rule set name nor its policy name appears in it, case-insensitively (uac-r2 Q4 and C4;
/// <see cref="NamesTheRule"/>).</item>
/// </list>
/// A refusal is a code only: no caller logs, returns or echoes the refused text.
/// </remarks>
public static class FirmDescriptionRules
{
    /// <summary>The cap on a description, in runes after trimming (the read side's cut is unchanged: up to 200 plus "...").</summary>
    public const int MaxRunes = 200;

    /// <summary>More than <see cref="MaxRunes"/> runes after trimming.</summary>
    public const string TooLong = "firm_description_too_long";

    /// <summary>A <c>{</c> or <c>}</c>: a placeholder or template syntax.</summary>
    public const string Braces = "firm_description_braces";

    /// <summary>A <c>&lt;</c> or <c>&gt;</c>: markup.</summary>
    public const string Markup = "firm_description_markup";

    /// <summary>A rune of category Control, Format, LineSeparator or ParagraphSeparator, or an unpaired UTF-16 surrogate.</summary>
    public const string InvisibleCharacter = "firm_description_invisible_character";

    /// <summary>The text contains the version's own rule set name or policy name (case-insensitive).</summary>
    public const string NamesTheRule = "firm_description_names_rule";

    /// <summary>A Duplicate rule never carries a description: firms always see the standard duplicate wording (design section 5).</summary>
    public const string OnDuplicate = "firm_description_duplicate";

    /// <summary>The stable log code of a stored description the mapper or the plan replay dropped (logged with the code and ids only).</summary>
    public const string DroppedLogCode = "EBILLING-FIRM-DESCRIPTION-DROPPED";

    /// <summary>The content checks only (the writer's preflight: it does not know the rule's names).</summary>
    public static FirmDescriptionCheck Validate(string? raw) => Validate(raw, null, null);

    /// <summary>The content checks, then the name check against <paramref name="ruleSetName"/> and <paramref name="policyName"/> (a blank name is
    /// not checked).</summary>
    public static FirmDescriptionCheck Validate(string? raw, string? ruleSetName, string? policyName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new FirmDescriptionCheck(null, null);
        }

        var text = raw.Trim();
        if (HasUnpairedSurrogate(text))
        {
            return Refused(InvisibleCharacter);
        }

        var runes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            runes++;
            if (IsInvisibleControl(rune))
            {
                return Refused(InvisibleCharacter);
            }
        }

        if (runes > MaxRunes)
        {
            return Refused(TooLong);
        }

        if (text.AsSpan().IndexOfAny('{', '}') >= 0)
        {
            return Refused(Braces);
        }

        if (text.AsSpan().IndexOfAny('<', '>') >= 0)
        {
            return Refused(Markup);
        }

        if (Names(text, ruleSetName) || Names(text, policyName))
        {
            return Refused(NamesTheRule);
        }

        return new FirmDescriptionCheck(text, null);
    }

    /// <summary>
    /// Fix round 1 F4: the copy-time and edit-time check of a version of a known rule type: a Duplicate rule takes no description
    /// (<see cref="OnDuplicate"/>), so a stored one is never copied to its Signal; any other type is
    /// <see cref="Validate(string?, string?, string?)"/>. <paramref name="ruleTypeRaw"/> is <c>sprk_ruletype</c> as text (name or option value).
    /// </summary>
    public static FirmDescriptionCheck Validate(string? raw, string? ruleSetName, string? policyName, string? ruleTypeRaw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new FirmDescriptionCheck(null, null);
        }

        if (Sprk.Bff.Api.Services.Signals.RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out var ruleType)
            && ruleType == Sprk.Bff.Api.Services.Signals.RuleType.Duplicate)
        {
            return Refused(OnDuplicate);
        }

        return Validate(raw, ruleSetName, policyName);
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The one rule for a scalar (rune) a firm-facing text may not hold: Unicode category Control, Format, LineSeparator or ParagraphSeparator
    /// (the C0 and C1 controls, the directional marks and overrides, U+061C, the zero-width characters, U+2060 to U+2064, U+FEFF, U+00AD, the TAG
    /// characters U+E0000 to U+E007F, U+2028 and U+2029). The write side refuses a text holding one; the read side
    /// (<see cref="FirmRejectionFindings.ShownFirmReason"/>) removes it.
    /// </summary>
    public static bool IsInvisibleControl(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static bool Names(string text, string? name) =>
        !string.IsNullOrWhiteSpace(name) && text.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static FirmDescriptionCheck Refused(string code) => new(null, code);
}
