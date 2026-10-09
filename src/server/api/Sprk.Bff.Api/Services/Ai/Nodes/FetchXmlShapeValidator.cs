// ISS-018 (#1452, owner decision D-77): the ONE FetchXML list-shape check. Three callers share it:
//   1. QueryDataverseNodeExecutor — runs it on the RENDERED query before Dataverse sees it and fails the node loudly;
//   2. the repo regression test (tests/integration/regression/Ai/Issue1452_*) — renders every repo playbook and
//      runs it on the result, and runs it on the authored template;
//   3. the playbook deploy lint (scripts/common/Assert-PlaybookFetchXmlShape.ps1, called by Deploy-Playbook.ps1) — loads THIS FILE with Add-Type and runs it on the authored template.
// Because PowerShell compiles this file on its own, it must stay dependency-free: BCL only, explicit usings, no
// project types, no file-scoped namespace.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Sprk.Bff.Api.Services.Ai.Nodes
{
    /// <summary>
    /// Checks that every FetchXML list operator carries its values the way Dataverse reads them: as
    /// <c>&lt;value&gt;</c> child elements. Dataverse ignores the <c>value</c> attribute on a list operator, so
    /// <c>operator="in" value="a,b"</c> becomes a condition with NO values and the whole query fails with
    /// "The value passed for ConditionOperator.In is empty" — for a real list of ids as much as for an empty one
    /// (ISS-018). The check reports problems; it never rewrites the query.
    /// </summary>
    /// <remarks>
    /// <para><b>Rendered mode</b> (<c>authoredTemplate: false</c>, the executor): the text Dataverse will execute.
    /// Any leftover <c>{{</c> is a problem (a template that did not render).</para>
    /// <para><b>Authored mode</b> (<c>authoredTemplate: true</c>, the deploy lint and the repo test): the text a maker
    /// stores in <c>sprk_playbooknode.sprk_configjson</c>. Scalar templates (<c>value="{{todayUtc}}"</c>) are allowed.
    /// A list operator's values must be literal <c>&lt;value&gt;</c> children or, for <c>in</c> only, exactly one
    /// <c>{{fetchInGuids path}}</c> expression; <c>joinIds</c> is never allowed in FetchXML.</para>
    /// </remarks>
    public static class FetchXmlShapeValidator
    {
        /// <summary>The Handlebars helper that writes a GUID list as <c>&lt;value&gt;</c> children (TemplateEngine).</summary>
        public const string ListHelperName = "fetchInGuids";

        // Operators that take their values only from <value> children, with the number of values each accepts
        // (Microsoft Learn, "FetchXml condition operators"; the fiscal ones take a period and a year).
        private static readonly Dictionary<string, (int Min, int Max)> ListOperators =
            new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase)
            {
                ["in"] = (1, int.MaxValue),
                ["not-in"] = (1, int.MaxValue),
                ["contain-values"] = (1, int.MaxValue),
                ["not-contain-values"] = (1, int.MaxValue),
                ["between"] = (2, 2),
                ["not-between"] = (2, 2),
                ["in-fiscal-period-and-year"] = (2, 2),
                ["in-or-after-fiscal-period-and-year"] = (2, 2),
                ["in-or-before-fiscal-period-and-year"] = (2, 2),
            };

        // Where {{fetchInGuids …}} may stand in for the children: `in` ONLY. The helper fails closed by writing the
        // impossible match (Guid.Empty) for an empty or invalid list — "selects nothing" under `in`, but under `not-in`
        // the same value matches EVERY row (fail-open), so the helper is refused there.
        private static readonly HashSet<string> GuidListOperators =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "in" };

        // Negated list operators: the empty GUID (fetchInGuids' "select nothing" value) inverts to "select everything".
        private static readonly HashSet<string> NegatedListOperators =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "not-in", "not-contain-values" };

        private static readonly Regex ListHelperExpression = new Regex(
            @"^\s*\{\{\s*" + ListHelperName + @"\s+[A-Za-z_][A-Za-z0-9_.]*\s*\}\}\s*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex JoinIdsUse = new Regex(@"\{\{[^}]*\bjoinIds\b", RegexOptions.CultureInvariant);

        /// <summary>True when <paramref name="operatorName"/> takes its values from <c>&lt;value&gt;</c> children.</summary>
        public static bool IsListOperator(string? operatorName) =>
            operatorName is not null && ListOperators.ContainsKey(operatorName.Trim());

        /// <summary>
        /// Returns every shape problem in <paramref name="fetchXml"/>, each naming the condition's attribute and
        /// operator. An empty list means the query is well-shaped.
        /// </summary>
        /// <param name="fetchXml">The FetchXML text.</param>
        /// <param name="authoredTemplate">
        /// <c>true</c> for an authored template (deploy lint, repo test); <c>false</c> for the rendered text that
        /// will be executed (the executor).
        /// </param>
        public static IReadOnlyList<string> Validate(string? fetchXml, bool authoredTemplate = false)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(fetchXml))
            {
                problems.Add("FetchXML is empty.");
                return problems;
            }

            if (JoinIdsUse.IsMatch(fetchXml))
            {
                problems.Add(
                    "FetchXML uses the joinIds helper, which writes a comma list that Dataverse does not read as a list. " +
                    "Use <condition operator=\"in\">{{" + ListHelperName + " path.ids}}</condition>.");
            }

            if (!authoredTemplate && fetchXml.IndexOf("{{", StringComparison.Ordinal) >= 0)
            {
                problems.Add("FetchXML still contains an unrendered template ('{{'); it would be sent to Dataverse as literal text.");
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(fetchXml);
            }
            catch (XmlException ex)
            {
                problems.Add("FetchXML is not well-formed XML: " + ex.Message);
                return problems;
            }

            foreach (var condition in document.Descendants().Where(e => e.Name.LocalName == "condition"))
            {
                var op = condition.Attribute("operator")?.Value?.Trim();
                if (op is null || !ListOperators.TryGetValue(op, out var arity))
                {
                    continue;
                }

                var label = Describe(condition, op);
                var valueAttribute = condition.Attribute("value");
                if (valueAttribute is not null)
                {
                    problems.Add(
                        label + ": a list operator takes its values only from <value> child elements; Dataverse ignores " +
                        "the value attribute (\"" + Truncate(valueAttribute.Value) + "\") and the list is empty.");
                }

                var values = condition.Elements().Where(e => e.Name.LocalName == "value").ToList();
                var text = string.Concat(condition.Nodes().OfType<XText>().Select(t => t.Value));
                var hasText = !string.IsNullOrWhiteSpace(text);

                if (authoredTemplate && hasText)
                {
                    if (values.Count == 0 && GuidListOperators.Contains(op) && ListHelperExpression.IsMatch(text))
                    {
                        continue; // {{fetchInGuids path}} renders one <value> per id (Guid.Empty when the list is empty).
                    }

                    problems.Add(
                        label + ": list values must be literal <value> children or, for operator=\"in\" only, exactly one {{" + ListHelperName +
                        " path}} expression; found \"" + Truncate(text.Trim()) + "\".");
                    continue;
                }

                if (!authoredTemplate && hasText)
                {
                    problems.Add(label + ": unexpected text \"" + Truncate(text.Trim()) + "\" inside a list condition.");
                }

                if (values.Count < arity.Min || values.Count > arity.Max)
                {
                    var expected = arity.Min == arity.Max
                        ? "exactly " + arity.Min
                        : "at least " + arity.Min;
                    problems.Add(label + ": needs " + expected + " <value> child element(s); found " + values.Count + ".");
                }

                if (!authoredTemplate && NegatedListOperators.Contains(op)
                    && values.Any(v => Guid.TryParse(v.Value.Trim(), out var id) && id == Guid.Empty))
                {
                    problems.Add(
                        label + ": contains the empty GUID, the impossible match fetchInGuids writes for an empty or invalid " +
                        "list; under a negated operator it matches EVERY row (fail-open).");
                }

                if (values.Any(v => string.IsNullOrWhiteSpace(v.Value)))
                {
                    problems.Add(label + ": has an empty <value> element.");
                }
            }

            return problems;
        }

        private static string Describe(XElement condition, string op)
        {
            var attribute = condition.Attribute("attribute")?.Value ?? "(no attribute)";
            var entity = condition.Attribute("entityname")?.Value;
            return "condition " + (entity is null ? string.Empty : entity + ".") + attribute + " operator=\"" + op + "\"";
        }

        private static string Truncate(string value) => value.Length <= 120 ? value : value.Substring(0, 120) + "…";
    }
}
