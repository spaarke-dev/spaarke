using System.Text;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// Translates the GA-Dataverse-MCP <c>read_query</c> SQL SELECT dialect into an OData query
/// for the Dataverse Web API (spaarke-ai-architecture-redesign-r1 task 008, FR-P0-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract source</b>: the GA MCP <c>read_query</c> tool accepts "basic SELECT queries
/// with an explicit list of selected fields" (research note
/// <c>research-dataverse-mcp-2026-07.md</c> + Learn tool description). This translator accepts
/// the single-table core of that dialect and rejects the GA-documented unsupported features
/// with the SAME vocabulary so LLM retry behavior transfers across a future MCP transport swap:
/// </para>
/// <list type="bullet">
/// <item>SUPPORTED: <c>SELECT [TOP n] col, col2 FROM table [WHERE …] [ORDER BY col [ASC|DESC], …]</c></item>
/// <item>WHERE: column-to-literal comparisons (=, !=, &lt;&gt;, &lt;, &lt;=, &gt;, &gt;=, LIKE),
/// <c>IS [NOT] NULL</c>, combined with AND/OR and parentheses.</item>
/// <item>REJECTED (GA-unsupported): subqueries, HAVING, DISTINCT, WITH, OFFSET, UNION, CAST,
/// CONVERT, TOP PERCENT.</item>
/// <item>REJECTED (native-transport limitation, clearly messaged): JOIN, GROUP BY, aggregates
/// (COUNT/SUM/AVG/MIN/MAX), GETDATE()/GETUTCDATE(), column-to-column comparison — these are
/// GA-supported via the TDS endpoint but have no single OData equivalent; the handler tells
/// the model to reformulate.</item>
/// <item>REJECTED (read-only invariant): anything that is not a single SELECT statement.
/// This is the mechanical enforcement of <c>side_effect_class = read</c> (ADR-039).</item>
/// </list>
/// <para>
/// Pure + deterministic: no I/O, no clock, no configuration — unit-tested exhaustively in
/// <c>DataverseSqlQueryTranslatorTests</c>.
/// </para>
/// </remarks>
internal static partial class DataverseSqlQueryTranslator
{
    /// <summary>Default row cap when the query has no TOP clause (ADR-016 quota posture).</summary>
    internal const int DefaultTop = 50;

    /// <summary>Hard row cap — TOP values above this are clamped (ADR-016).</summary>
    internal const int MaxTop = 200;

    private static readonly Regex IdentifierRegex = MyIdentifierRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$", RegexOptions.IgnoreCase)]
    private static partial Regex MyIdentifierRegex();

    internal sealed record TranslationResult(
        bool Success,
        string? EntityLogicalName,
        IReadOnlyList<string>? Columns,
        string? ODataQuery,
        int Top,
        string? Error,
        // G-P3 UAT round-5 R5-C (2026-07-07): the UNESCAPED filter/orderby parts, exposed so
        // the handler can rewrite LOOKUP column references to their Web-API `_{name}_value`
        // form (metadata-aware — the translator itself stays pure/metadata-free) and
        // reassemble via AssembleODataQuery.
        string? RawFilter = null,
        string? RawOrderBy = null)
    {
        public static TranslationResult Fail(string error) =>
            new(false, null, null, null, 0, error);
    }

    /// <summary>
    /// Assembles the OData query string from structured parts — the ONE assembly site,
    /// shared by <see cref="Translate"/> and the handler's R5-C lookup-column rewrite.
    /// </summary>
    internal static string AssembleODataQuery(
        IEnumerable<string> selectColumns, string? rawFilter, string? rawOrderBy, int top)
    {
        var sb = new StringBuilder();
        sb.Append("$select=").Append(string.Join(",", selectColumns));
        if (rawFilter is not null) sb.Append("&$filter=").Append(Uri.EscapeDataString(rawFilter));
        if (rawOrderBy is not null) sb.Append("&$orderby=").Append(Uri.EscapeDataString(rawOrderBy));
        sb.Append("&$top=").Append(top);
        return sb.ToString();
    }

    /// <summary>
    /// Translates <paramref name="querytext"/> (GA MCP read_query dialect) to an OData query
    /// string (without entity-set prefix). Never throws for bad input — returns a failed
    /// result with an LLM-actionable error message.
    /// </summary>
    internal static TranslationResult Translate(string querytext)
    {
        if (string.IsNullOrWhiteSpace(querytext))
        {
            return TranslationResult.Fail("querytext is required and must be a SQL SELECT statement.");
        }

        var sql = querytext.Trim().TrimEnd(';').Trim();

        if (sql.Contains(';'))
        {
            return TranslationResult.Fail("Only a single SELECT statement is allowed (no statement batching).");
        }
        if (sql.Contains("--", StringComparison.Ordinal) || sql.Contains("/*", StringComparison.Ordinal))
        {
            return TranslationResult.Fail("SQL comments are not allowed in querytext.");
        }

        // Reject GA-unsupported / unsupported-by-native-transport features by scanning the RAW
        // text first (word-boundary regex) so the diagnostic names the actual offending feature
        // even when the query also contains syntax the tokenizer cannot digest (aliases, dots).
        var featureRejection = ScanForUnsupportedFeatures(sql);
        if (featureRejection is not null)
        {
            return TranslationResult.Fail(featureRejection);
        }

        List<Token> tokens;
        try
        {
            tokens = Tokenize(sql);
        }
        catch (FormatException ex)
        {
            return TranslationResult.Fail(ex.Message);
        }

        var cursor = new TokenCursor(tokens);

        if (!cursor.TryConsumeKeyword("SELECT"))
        {
            return TranslationResult.Fail("querytext must be a SELECT statement — dataverse.read_query is read-only. Use dataverse.create_record / update_record / delete_record for writes.");
        }

        // TOP n
        var top = DefaultTop;
        if (cursor.TryConsumeKeyword("TOP"))
        {
            if (!cursor.TryConsumeNumber(out var topLiteral) || !int.TryParse(topLiteral, out var parsedTop) || parsedTop < 1)
            {
                return TranslationResult.Fail("TOP must be followed by a positive integer.");
            }
            if (cursor.PeekIsKeyword("PERCENT"))
            {
                return TranslationResult.Fail("TOP PERCENT is not supported.");
            }
            top = Math.Min(parsedTop, MaxTop);
        }

        // Select list — explicit column names required (GA dialect: "explicit list of selected fields").
        var columns = new List<string>();
        while (true)
        {
            if (cursor.TryConsumeOperator("*"))
            {
                return TranslationResult.Fail("SELECT * is not supported — list explicit column logical names (use dataverse.describe on the table to discover them).");
            }
            if (!cursor.TryConsumeIdentifier(out var column))
            {
                return TranslationResult.Fail("Expected a column logical name in the SELECT list.");
            }
            if (!IdentifierRegex.IsMatch(column))
            {
                return TranslationResult.Fail($"'{column}' is not a valid Dataverse column logical name.");
            }
            columns.Add(column.ToLowerInvariant());
            if (!cursor.TryConsumeOperator(","))
            {
                break;
            }
        }

        // FROM table
        if (!cursor.TryConsumeKeyword("FROM"))
        {
            return TranslationResult.Fail("Expected FROM <table_logical_name> after the SELECT list.");
        }
        if (!cursor.TryConsumeIdentifier(out var table) || !IdentifierRegex.IsMatch(table))
        {
            return TranslationResult.Fail("Expected a single table logical name after FROM (use dataverse.describe with path 'tables/' to discover tables).");
        }
        table = table.ToLowerInvariant();

        // Optional WHERE
        string? filter = null;
        if (cursor.TryConsumeKeyword("WHERE"))
        {
            var (ok, filterOrError) = ParseBooleanExpression(cursor, stopKeywords: new[] { "ORDER" });
            if (!ok)
            {
                return TranslationResult.Fail(filterOrError);
            }
            filter = filterOrError;
        }

        // Optional ORDER BY
        string? orderBy = null;
        if (cursor.TryConsumeKeyword("ORDER"))
        {
            if (!cursor.TryConsumeKeyword("BY"))
            {
                return TranslationResult.Fail("Expected BY after ORDER.");
            }
            var orderParts = new List<string>();
            while (true)
            {
                if (!cursor.TryConsumeIdentifier(out var orderCol) || !IdentifierRegex.IsMatch(orderCol))
                {
                    return TranslationResult.Fail("Expected a column logical name in the ORDER BY clause.");
                }
                var direction = "";
                if (cursor.TryConsumeKeyword("DESC")) direction = " desc";
                else if (cursor.TryConsumeKeyword("ASC")) direction = " asc";
                orderParts.Add(orderCol.ToLowerInvariant() + direction);
                if (!cursor.TryConsumeOperator(","))
                {
                    break;
                }
            }
            orderBy = string.Join(",", orderParts);
        }

        if (!cursor.AtEnd)
        {
            return TranslationResult.Fail($"Unexpected token '{cursor.Peek()?.Text}' — supported shape: SELECT [TOP n] cols FROM table [WHERE …] [ORDER BY …].");
        }

        return new TranslationResult(
            true, table, columns,
            AssembleODataQuery(columns, filter, orderBy, top),
            top, null,
            RawFilter: filter,
            RawOrderBy: orderBy);
    }

    /// <summary>
    /// Word-boundary scan of the raw SQL for features the tool rejects, returning the
    /// LLM-actionable diagnostic for the FIRST match (mutations checked first — they get the
    /// read-only redirect). Null when clean.
    /// </summary>
    private static string? ScanForUnsupportedFeatures(string sql)
    {
        static bool Has(string sql, string pattern) =>
            Regex.IsMatch(sql, pattern, RegexOptions.IgnoreCase);

        if (Has(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE)\b"))
            return "dataverse.read_query is strictly read-only (side_effect_class=read). Use the dataverse write tools for mutations.";
        if (Has(sql, @"\b(JOIN|INNER|OUTER|CROSS)\b"))
            return "JOINs are not supported by the native dataverse.read_query transport. Query one table at a time and correlate by id columns across calls.";
        if (Has(sql, @"\bGROUP\s+BY\b") || Has(sql, @"\b(COUNT|SUM|AVG|MIN|MAX)\s*\("))
            return "GROUP BY and aggregate functions are not supported by the native dataverse.read_query transport. Retrieve rows and aggregate from the results.";
        if (Has(sql, @"\bHAVING\b"))
            return "HAVING is not supported (per the GA read_query dialect).";
        if (Has(sql, @"\bDISTINCT\b"))
            return "DISTINCT is not supported in select lists (per the GA read_query dialect).";
        if (Has(sql, @"\bUNION\b"))
            return "UNION is not supported (per the GA read_query dialect).";
        if (Has(sql, @"\bOFFSET\b"))
            return "OFFSET is not supported (per the GA read_query dialect). Use TOP.";
        if (Has(sql, @"^\s*WITH\b"))
            return "WITH (CTEs) is not supported (per the GA read_query dialect).";
        if (Has(sql, @"\b(CAST|CONVERT)\s*\("))
            return "CAST/CONVERT are not supported (per the GA read_query dialect).";
        if (Has(sql, @"\b(GETDATE|GETUTCDATE)\b"))
            return "GETDATE()/GETUTCDATE() are not supported by the native transport. Compare against a literal ISO-8601 datetime (e.g. createdon >= '2026-01-01T00:00:00Z').";
        if (Has(sql, @"\bSELECT\b.*\bSELECT\b"))
            return "Subqueries are not supported (per the GA read_query dialect). Split the logic into two queries: compute the aggregate first, then filter using literal values.";
        return null;
    }

    // ── WHERE clause ───────────────────────────────────────────────────────────

    private static (bool Ok, string FilterOrError) ParseBooleanExpression(TokenCursor cursor, string[] stopKeywords)
    {
        var sb = new StringBuilder();
        var (ok, error) = ParseBooleanTerm(cursor, sb, stopKeywords);
        if (!ok) return (false, error!);

        while (true)
        {
            if (cursor.PeekIsAnyKeyword(stopKeywords) || cursor.AtEnd)
            {
                return (true, sb.ToString());
            }
            // A closing parenthesis terminates the current (nested) boolean expression; the
            // enclosing ParseBooleanTerm consumes it.
            if (cursor.Peek() is { Kind: TokenKind.Operator, Text: ")" })
            {
                return (true, sb.ToString());
            }
            if (cursor.TryConsumeKeyword("AND"))
            {
                sb.Append(" and ");
            }
            else if (cursor.TryConsumeKeyword("OR"))
            {
                sb.Append(" or ");
            }
            else
            {
                return (false, $"Expected AND/OR in the WHERE clause but found '{cursor.Peek()?.Text}'.");
            }
            (ok, error) = ParseBooleanTerm(cursor, sb, stopKeywords);
            if (!ok) return (false, error!);
        }
    }

    private static (bool Ok, string? Error) ParseBooleanTerm(TokenCursor cursor, StringBuilder sb, string[] stopKeywords)
    {
        if (cursor.TryConsumeOperator("("))
        {
            sb.Append('(');
            var (ok, inner) = ParseBooleanExpression(cursor, stopKeywords: Array.Empty<string>());
            if (!ok) return (false, inner);
            sb.Append(inner);
            if (!cursor.TryConsumeOperator(")"))
            {
                return (false, "Unbalanced parentheses in the WHERE clause.");
            }
            sb.Append(')');
            return (true, null);
        }

        if (!cursor.TryConsumeIdentifier(out var column) || !IdentifierRegex.IsMatch(column))
        {
            return (false, "Expected a column logical name on the left side of a WHERE comparison (subqueries and expressions are not supported).");
        }
        column = column.ToLowerInvariant();

        // IS [NOT] NULL
        if (cursor.TryConsumeKeyword("IS"))
        {
            var negated = cursor.TryConsumeKeyword("NOT");
            if (!cursor.TryConsumeKeyword("NULL"))
            {
                return (false, "Expected NULL after IS / IS NOT.");
            }
            sb.Append(column).Append(negated ? " ne null" : " eq null");
            return (true, null);
        }

        // LIKE
        if (cursor.TryConsumeKeyword("LIKE"))
        {
            if (!cursor.TryConsumeString(out var pattern))
            {
                return (false, "LIKE requires a string literal pattern.");
            }
            if (pattern.Contains('_') || pattern.Trim('%').Contains('%'))
            {
                return (false, "Only leading/trailing % wildcards are supported in LIKE patterns on the native transport.");
            }
            var core = EscapeODataString(pattern.Trim('%'));
            var starts = pattern.StartsWith('%');
            var ends = pattern.EndsWith('%');
            sb.Append((starts, ends) switch
            {
                (true, true) => $"contains({column},'{core}')",
                (false, true) => $"startswith({column},'{core}')",
                (true, false) => $"endswith({column},'{core}')",
                _ => $"{column} eq '{core}'"
            });
            return (true, null);
        }

        if (!cursor.TryConsumeComparisonOperator(out var sqlOp))
        {
            return (false, $"Expected a comparison operator after '{column}' (=, !=, <>, <, <=, >, >=, LIKE, IS NULL).");
        }
        var odataOp = sqlOp switch
        {
            "=" => "eq",
            "!=" or "<>" => "ne",
            "<" => "lt",
            "<=" => "le",
            ">" => "gt",
            ">=" => "ge",
            _ => null
        };
        if (odataOp is null)
        {
            return (false, $"Comparison operator '{sqlOp}' is not supported.");
        }

        if (cursor.TryConsumeString(out var strLiteral))
        {
            sb.Append($"{column} {odataOp} '{EscapeODataString(strLiteral)}'");
            return (true, null);
        }
        if (cursor.TryConsumeNumber(out var numLiteral))
        {
            sb.Append($"{column} {odataOp} {numLiteral}");
            return (true, null);
        }
        if (cursor.TryConsumeKeyword("NULL"))
        {
            sb.Append($"{column} {odataOp} null");
            return (true, null);
        }
        if (cursor.TryConsumeKeyword("TRUE") || cursor.TryConsumeKeyword("FALSE"))
        {
            sb.Append($"{column} {odataOp} {cursor.LastConsumed!.Text.ToLowerInvariant()}");
            return (true, null);
        }
        if (cursor.PeekIsIdentifier())
        {
            return (false, "Column-to-column comparisons are not supported by the native transport — compare columns against literals.");
        }
        return (false, $"Expected a literal value after '{column} {sqlOp}'.");
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''");

    // ── Tokenizer ─────────────────────────────────────────────────────────────

    private enum TokenKind { Identifier, Number, String, Operator }

    private sealed record Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Identifier, sql[start..i]));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < sql.Length && char.IsDigit(sql[i + 1])))
            {
                var start = i;
                i++;
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                tokens.Add(new Token(TokenKind.Number, sql[start..i]));
                continue;
            }

            if (c == '\'')
            {
                var sb = new StringBuilder();
                i++;
                var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                        closed = true;
                        i++;
                        break;
                    }
                    sb.Append(sql[i]);
                    i++;
                }
                if (!closed) throw new FormatException("Unterminated string literal in querytext.");
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            // multi-char operators first
            if (i + 1 < sql.Length)
            {
                var two = sql.Substring(i, 2);
                if (two is "<=" or ">=" or "<>" or "!=")
                {
                    tokens.Add(new Token(TokenKind.Operator, two));
                    i += 2;
                    continue;
                }
            }
            if (c is '=' or '<' or '>' or ',' or '(' or ')' or '*')
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString()));
                i++;
                continue;
            }

            throw new FormatException($"Unexpected character '{c}' in querytext.");
        }
        return tokens;
    }

    private sealed class TokenCursor
    {
        private readonly List<Token> _tokens;
        private int _index;

        public TokenCursor(List<Token> tokens) => _tokens = tokens;

        public bool AtEnd => _index >= _tokens.Count;
        public Token? LastConsumed { get; private set; }

        public Token? Peek() => AtEnd ? null : _tokens[_index];

        public bool PeekIsKeyword(string keyword) =>
            Peek() is { Kind: TokenKind.Identifier } t && t.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);

        public bool PeekIsAnyKeyword(string[] keywords) => keywords.Any(PeekIsKeyword);

        public bool PeekIsIdentifier() => Peek() is { Kind: TokenKind.Identifier };

        public bool TryConsumeKeyword(string keyword)
        {
            if (!PeekIsKeyword(keyword)) return false;
            LastConsumed = _tokens[_index++];
            return true;
        }

        public bool TryConsumeIdentifier(out string identifier)
        {
            identifier = string.Empty;
            if (Peek() is not { Kind: TokenKind.Identifier } t) return false;
            // Reserved words never double as column/table names here.
            if (t.Text.ToUpperInvariant() is "FROM" or "WHERE" or "ORDER" or "BY" or "AND" or "OR" or "NULL" or "IS" or "NOT" or "LIKE" or "ASC" or "DESC" or "TOP" or "SELECT")
            {
                return false;
            }
            identifier = t.Text;
            LastConsumed = _tokens[_index++];
            return true;
        }

        public bool TryConsumeNumber(out string number)
        {
            number = string.Empty;
            if (Peek() is not { Kind: TokenKind.Number } t) return false;
            number = t.Text;
            LastConsumed = _tokens[_index++];
            return true;
        }

        public bool TryConsumeString(out string value)
        {
            value = string.Empty;
            if (Peek() is not { Kind: TokenKind.String } t) return false;
            value = t.Text;
            LastConsumed = _tokens[_index++];
            return true;
        }

        public bool TryConsumeOperator(string op)
        {
            if (Peek() is not { Kind: TokenKind.Operator } t || t.Text != op) return false;
            LastConsumed = _tokens[_index++];
            return true;
        }

        public bool TryConsumeComparisonOperator(out string op)
        {
            op = string.Empty;
            if (Peek() is not { Kind: TokenKind.Operator } t) return false;
            if (t.Text is not ("=" or "!=" or "<>" or "<" or "<=" or ">" or ">=")) return false;
            op = t.Text;
            LastConsumed = _tokens[_index++];
            return true;
        }
    }
}
