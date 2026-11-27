// -----------------------------------------------------------------------------
// BillingCodePackTests.cs
//
// Data-setup plan D5: the `legal-standard` pack carries the UTBMS code sets and codes
// (sprk_billingcodeset, sprk_billingcode) and the default code set of the matter types and
// practice areas (sprk_defaultbillingcodeset), so a new stamp has the codes the eBilling
// screens and invoice review need. H12b seeds them create-if-missing, like the rest of the
// pack: never an update, and a row the stamp already holds is matched, not duplicated.
//
// ADR-038: a real HttpClient over the hand-rolled fake handler of the seeder tests (no
// Mock<HttpMessageHandler>), against the REAL embedded pack.
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class BillingCodePackTests
{
    private const string Pack = "legal-standard";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    private const string SetsEntitySet = "sprk_billingcodesets";
    private const string CodesEntitySet = "sprk_billingcodes";
    private const string MatterTypesEntitySet = "sprk_mattertype_refs";
    private const string PracticeAreasEntitySet = "sprk_practicearea_refs";

    private const string CodeSetBind = "sprk_CodeSet@odata.bind";
    private const string DefaultSetBind = "sprk_DefaultBillingCodeSet@odata.bind";

    /// <summary>Every row of the pack: 8 code sets, 161 codes, and the seven taxonomy tables (87 rows).</summary>
    private const int PackRows = 8 + 161 + 87;

    // ---- seeding order ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task D5_OnAnEmptyStamp_CodeSetsAreCreatedBeforeTheirCodes_AndBothBeforeTheTypesThatNameThem()
    {
        var fake = new DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler { PersistPosts = true };

        var result = await RunAsync(fake);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        fake.Posts.Should().HaveCount(PackRows);
        var order = fake.Posts.Select(p => EntitySet(p.Uri)).ToList();
        order.LastIndexOf(SetsEntitySet).Should().BeLessThan(order.IndexOf(CodesEntitySet), "a code binds to its set, which must exist first");
        order.LastIndexOf(CodesEntitySet).Should().BeLessThan(order.IndexOf(MatterTypesEntitySet));
        order.LastIndexOf(SetsEntitySet).Should().BeLessThan(order.IndexOf(PracticeAreasEntitySet), "a default code set must exist before the row that names it");

        // Replaying the POSTs in order: every bind names a set that an EARLIER POST created, and each code is in the set its pack row names.
        var createdSets = new Dictionary<string, string>(StringComparer.Ordinal); // id -> name
        var codesPerSet = new Dictionary<string, int>(StringComparer.Ordinal);
        var bound = new Dictionary<string, int> { [MatterTypesEntitySet] = 0, [PracticeAreasEntitySet] = 0 };
        foreach (var (uri, body) in fake.Posts)
        {
            var entitySet = EntitySet(uri);
            var json = JsonDocument.Parse(body).RootElement;
            switch (entitySet)
            {
                case SetsEntitySet:
                    createdSets[json.GetProperty("sprk_billingcodesetid").GetString()!] = json.GetProperty("sprk_name").GetString()!;
                    break;
                case CodesEntitySet:
                    var set = BoundId(json, CodeSetBind, SetsEntitySet);
                    createdSets.Should().ContainKey(set, $"code {json.GetProperty("sprk_code").GetString()} is bound to a set created before it");
                    codesPerSet[createdSets[set]] = codesPerSet.GetValueOrDefault(createdSets[set]) + 1;
                    break;
                default:
                    if (json.TryGetProperty(DefaultSetBind, out _))
                    {
                        createdSets.Should().ContainKey(BoundId(json, DefaultSetBind, SetsEntitySet));
                        bound[entitySet]++;
                    }

                    break;
            }
        }

        createdSets.Values.Should().BeEquivalentTo(
            "UTBMS Litigation", "UTBMS Counseling", "UTBMS Project", "UTBMS Bankruptcy", "UTBMS IP Patent Prosecution", "UTBMS Trademark",
            "UTBMS Activity", "UTBMS Expense");
        codesPerSet.Should().Contain(new Dictionary<string, int>
        {
            ["UTBMS Litigation"] = 29,
            ["UTBMS Counseling"] = 4,
            ["UTBMS Project"] = 15,
            ["UTBMS Bankruptcy"] = 21,
            ["UTBMS IP Patent Prosecution"] = 23,
            ["UTBMS Trademark"] = 34,
            ["UTBMS Activity"] = 11,
            ["UTBMS Expense"] = 24,
        });
        bound[MatterTypesEntitySet].Should().Be(12, "12 of the 14 matter types name a default code set; Investigations and Tax are left to the admin");
        bound[PracticeAreasEntitySet].Should().Be(12, "12 of the 14 practice areas name one; Tax and Antitrust & Competition are left to the admin");
    }

    [Fact]
    public async Task D5_ALitigationMatterType_IsCreatedWithTheLitigationCodeSetAsItsDefault()
    {
        var fake = new DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler { PersistPosts = true };

        await RunAsync(fake);

        var litigationSet = PostedRow(fake, SetsEntitySet, "sprk_name", "UTBMS Litigation").GetProperty("sprk_billingcodesetid").GetString();
        BoundId(PostedRow(fake, MatterTypesEntitySet, "sprk_mattertypename", "Litigation"), DefaultSetBind, SetsEntitySet).Should().Be(litigationSet);
        BoundId(PostedRow(fake, PracticeAreasEntitySet, "sprk_practiceareaname", "Litigation & Disputes"), DefaultSetBind, SetsEntitySet).Should().Be(litigationSet);
        PostedRow(fake, MatterTypesEntitySet, "sprk_mattertypename", "Tax").TryGetProperty(DefaultSetBind, out _).Should().BeFalse("an unmapped type stays unmapped");
    }

    // ---- idempotence --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task D5_ASecondRun_WritesNothing()
    {
        var fake = new DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler { PersistPosts = true };
        await RunAsync(fake);
        var postsAfterFirstRun = fake.Posts.Count;
        var requestsAfterFirstRun = fake.Methods.Count;

        var second = await RunAsync(fake);

        second.Status.Should().Be(AppConfigSeederStatus.Ok, second.Diagnostic);
        second.Diagnostic.Should().Contain($"{PackRows} row(s) across 9 table(s), 0 created, {PackRows} already present")
            .And.NotContain("DIFFER", "what the first run created equals the pack");
        fake.Posts.Should().HaveCount(postsAfterFirstRun, "the second run creates nothing");
        fake.Methods.Skip(requestsAfterFirstRun).Should().OnlyContain(m => m == HttpMethod.Get, "and writes nothing of any kind");
    }

    // ---- an existing stamp's rows are never overwritten ---------------------------------------------------------------

    private const string OwnSetId = "99999999-0000-4000-8000-000000000001";
    private const string OwnCodeId = "99999999-0000-4000-8000-000000000002";
    private const string OwnMatterTypeId = "99999999-0000-4000-8000-000000000003";

    [Fact]
    public async Task D5_AStampThatAlreadyHoldsRows_KeepsThemUnchanged_AndNewCodesJoinItsOwnSet()
    {
        var fake = new DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler { PersistPosts = true };
        fake.Existing[SetsEntitySet] =
        [
            // The standalone script (or an admin) already created "UTBMS Litigation" under its own id, with the admin's own wording.
            $$"""{"sprk_billingcodesetid":"{{OwnSetId}}","sprk_name":"utbms litigation","sprk_body":"ABA","sprk_version":"1997","sprk_description":"our wording","statecode":0}""",
        ];
        fake.Existing[CodesEntitySet] =
        [
            $$"""{"sprk_billingcodeid":"{{OwnCodeId}}","_sprk_codeset_value":"{{OwnSetId}}","sprk_code":"L110","sprk_codetype":100000000,"sprk_guidancetext":"our guidance","statecode":0}""",
        ];
        fake.Existing[MatterTypesEntitySet] =
        [
            // The customer's own Litigation matter type has no default code set and keeps it that way.
            $$"""{"sprk_mattertype_refid":"{{OwnMatterTypeId}}","sprk_mattertypecode":"LIT","sprk_mattertypename":"Litigation","statecode":0}""",
        ];

        var result = await RunAsync(fake);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete,
            "the customer owns a row once it exists");
        fake.Posts.Should().HaveCount(PackRows - 3, "the present set, code and matter type are matched, not created again");
        var posted = fake.Posts.Select(p => (EntitySet: EntitySet(p.Uri), Json: JsonDocument.Parse(p.Body).RootElement)).ToList();
        posted.Should().NotContain(p => p.EntitySet == SetsEntitySet && p.Json.GetProperty("sprk_name").GetString() == "UTBMS Litigation");
        posted.Should().NotContain(p => p.EntitySet == CodesEntitySet && p.Json.GetProperty("sprk_code").GetString() == "L110",
            "L110 is present in the Litigation set");
        posted.Should().NotContain(p => p.EntitySet == MatterTypesEntitySet && p.Json.GetProperty("sprk_mattertypename").GetString() == "Litigation");

        // The other 28 Litigation codes join the set the stamp already has, not a second "UTBMS Litigation".
        var litigationCodes = posted.Where(p => p.EntitySet == CodesEntitySet && p.Json.GetProperty("sprk_code").GetString()!.StartsWith('L')).ToList();
        litigationCodes.Should().HaveCount(28).And.OnlyContain(p => BoundId(p.Json, CodeSetBind, SetsEntitySet) == OwnSetId);
        // ...and a practice area created now takes that set as its default.
        BoundId(posted.Single(p => p.EntitySet == PracticeAreasEntitySet && p.Json.GetProperty("sprk_practiceareaname").GetString() == "Litigation & Disputes").Json,
            DefaultSetBind, SetsEntitySet).Should().Be(OwnSetId);

        result.Diagnostic.Should().Contain("sprk_billingcodeset 'UTBMS Litigation' differs in").And.Contain("sprk_description")
            .And.Contain("sprk_billingcode 'L110' differs in").And.Contain("sprk_guidancetext", "reported in the diagnostic only");
    }

    [Fact]
    public async Task D5_ACodeThatExistsInAnotherSet_DoesNotStandInForTheUtbmsCode()
    {
        var fake = new DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler { PersistPosts = true };
        fake.Existing[SetsEntitySet] = [$$"""{"sprk_billingcodesetid":"{{OwnSetId}}","sprk_name":"Firm A codes","statecode":0}"""];
        fake.Existing[CodesEntitySet] =
        [
            // A firm's own "L110" is not the UTBMS one: a billing code is unique within its set (sprk_billingcode_codeset_code_key).
            $$"""{"sprk_billingcodeid":"{{OwnCodeId}}","_sprk_codeset_value":"{{OwnSetId}}","sprk_code":"L110","sprk_codetype":100000000,"statecode":0}""",
        ];

        var result = await RunAsync(fake);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        fake.Posts.Should().HaveCount(PackRows, "nothing in the pack is present");
        var litigationSet = PostedRow(fake, SetsEntitySet, "sprk_name", "UTBMS Litigation").GetProperty("sprk_billingcodesetid").GetString();
        BoundId(PostedRow(fake, CodesEntitySet, "sprk_code", "L110"), CodeSetBind, SetsEntitySet).Should().Be(litigationSet);
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete);
    }

    // ---- the none pack ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task D5_PackNone_SeedsNoBillingCodes_AndTouchesNothing()
    {
        var fake = new DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler();

        var result = await RunAsync(fake, "none");

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("starter taxonomy pack 'none': nothing seeded");
        fake.Methods.Should().BeEmpty("`none` means no code sets, no codes and no default code sets: not even a read");
    }

    // ---- the pack equals the eBilling seed files ------------------------------------------------------------------------

    [Fact]
    public void D5_ThePackHoldsExactlyTheRowsTheEbillingSeedFilesDefine()
    {
        var root = FindRepoRoot();
        var ebilling = Path.Combine(root, "scripts", "seed-data", "ebilling");
        var tables = ReferenceDataSet.Pack(Pack).ToDictionary(t => t.Table, StringComparer.Ordinal);
        var packSets = tables["sprk_billingcodeset"].Rows.ToDictionary(r => r.Values["sprk_name"].GetString()!, StringComparer.Ordinal);
        var packCodes = tables["sprk_billingcode"].Rows.ToLookup(r => r.Lookups["sprk_codeset"]);
        var tuning = JsonDocument.Parse(File.ReadAllText(Path.Combine(ebilling, "utbms-codeset-tuning.json"), Encoding.UTF8)).RootElement
            .GetProperty("sets").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, StringComparer.Ordinal);

        var files = Directory.EnumerateFiles(ebilling, "utbms-*.csv").Order(StringComparer.Ordinal).ToList();
        files.Should().HaveCount(8);
        packSets.Should().HaveCount(files.Count, "one pack set per CSV file");
        var csvRows = 0;
        foreach (var file in files)
        {
            var rows = ReadCsv(file);
            var setName = rows[0]["set_name"].Trim();
            packSets.Should().ContainKey(setName, Path.GetFileName(file));
            var set = packSets[setName];
            set.Values["sprk_body"].GetString().Should().Be(rows[0]["body"].Trim());
            set.Values["sprk_version"].GetString().Should().Be(rows[0]["version"].Trim());
            set.Values["sprk_sourceurl"].GetString().Should().Be(rows[0]["source_url"].Trim());
            tuning.TryGetValue(setName, out var tune).Should().Be(
                set.Values["sprk_description"].ValueKind == JsonValueKind.String, $"{setName}: tuning text is on a set exactly when the tuning file has it");
            if (tune.ValueKind == JsonValueKind.Object)
            {
                set.Values["sprk_description"].GetString().Should().Be(tune.GetProperty("description").GetString()!.Trim());
                set.Values["sprk_matchhints"].GetString().Should().Be(tune.GetProperty("matchHints").GetString()!.Trim());
            }

            var codes = packCodes[set.Id].ToDictionary(r => r.Values["sprk_code"].GetString()!, StringComparer.Ordinal);
            codes.Keys.Should().BeEquivalentTo(rows.Select(r => r["code"].Trim()), $"{setName}: the pack holds exactly the CSV's codes");
            foreach (var csv in rows)
            {
                csvRows++;
                var code = codes[csv["code"].Trim()].Values;
                code["sprk_codetype"].GetInt32().Should().Be(csv["code_type"].Trim() switch { "Task" => 100000000, "Activity" => 100000001, _ => 100000002 });
                Text(code["sprk_phase"]).Should().Be(NullIfEmpty(csv["phase"]));
                Text(code["sprk_description"]).Should().Be(NullIfEmpty(csv["description"]));
                Text(code["sprk_guidancetext"]).Should().Be(NullIfEmpty(csv["guidance_text"]), $"{setName} {csv["code"]}");
            }
        }

        csvRows.Should().Be(tables["sprk_billingcode"].Rows.Count, "no code in the pack that a CSV does not define");
        tuning.Keys.Should().OnlyContain(name => packSets.ContainsKey(name));
    }

    [Fact]
    public void D5_EveryDefaultCodeSet_IsAUsableTaskSet_NotTheSharedActivityOrExpenseSet()
    {
        // CodeSetResolver skips a mapping whose set is inactive or has no active Task code, so such a default would silently do nothing.
        var tables = ReferenceDataSet.Pack(Pack).ToDictionary(t => t.Table, StringComparer.Ordinal);
        var taskSets = tables["sprk_billingcode"].Rows
            .Where(r => r.Values["sprk_codetype"].GetInt32() == 100000000)
            .Select(r => r.Lookups["sprk_codeset"]).ToHashSet();
        var setNames = tables["sprk_billingcodeset"].Rows.ToDictionary(r => r.Id, r => r.Values["sprk_name"].GetString()!);

        foreach (var table in new[] { tables["sprk_mattertype_ref"], tables["sprk_practicearea_ref"] })
        {
            var mapped = table.Rows.Where(r => r.Lookups.ContainsKey("sprk_defaultbillingcodeset")).ToList();
            mapped.Should().NotBeEmpty(table.Table);
            foreach (var row in mapped)
            {
                var set = row.Lookups["sprk_defaultbillingcodeset"];
                taskSets.Should().Contain(set, $"{table.Table} '{row.DisplayName}' -> {setNames[set]}");
            }
        }
    }

    [Fact]
    public void D5_ThePackTableOrder_PutsCodeSetsBeforeCodesBeforeTheTypesThatPointAtThem()
    {
        var names = ReferenceDataSet.PackTableNames[Pack].ToList();

        names.IndexOf("sprk_billingcodeset").Should().Be(0);
        names.IndexOf("sprk_billingcode").Should().Be(1);
        names.IndexOf("sprk_mattertype_ref").Should().BeGreaterThan(names.IndexOf("sprk_billingcode"));
        names.IndexOf("sprk_practicearea_ref").Should().BeGreaterThan(names.IndexOf("sprk_billingcode"));
    }

    // ---- the loader refuses a pack that cannot be seeded in order -------------------------------------------------------

    private const string TargetIdA = "aaaaaaaa-0000-4000-8000-00000000000a";

    private static string TargetJson() => """
        { "table": "sprk_target", "entitySet": "sprk_targets", "primaryKey": "sprk_targetid", "match": "naturalKey", "nameColumn": "sprk_name",
          "columns": ["sprk_name"], "rows": [ { "sprk_targetid": "aaaaaaaa-0000-4000-8000-00000000000a", "sprk_name": "T" } ] }
        """;

    private static string SourceJson(string targetId = TargetIdA, string? scope = null, bool setLookup = true) => $$"""
        { "table": "sprk_source", "entitySet": "sprk_sources", "primaryKey": "sprk_sourceid", "match": "naturalKey", "nameColumn": "sprk_name",
          {{(scope is null ? string.Empty : $"\"naturalKeyScope\": \"{scope}\",")}}
          "lookups": [ { "column": "sprk_target", "navigationProperty": "sprk_Target", "targetTable": "sprk_target", "targetEntitySet": "sprk_targets" } ],
          "columns": ["sprk_name"],
          "rows": [ { "sprk_sourceid": "bbbbbbbb-0000-4000-8000-00000000000b", "sprk_name": "S"{{(setLookup ? $", \"sprk_target\": \"{targetId}\"" : string.Empty)}} } ] }
        """;

    [Fact]
    public void D5_ALookupMustPointAtAnEarlierTableOfThePack()
    {
        var source = ReferenceDataSet.Parse(SourceJson());
        var target = ReferenceDataSet.Parse(TargetJson());

        ReferenceDataSet.RequireLookupTargets(Pack, [target, source]); // in order: fine

        var later = () => ReferenceDataSet.RequireLookupTargets(Pack, [source, target]);
        later.Should().Throw<InvalidOperationException>().WithMessage("*sprk_source*sprk_target*EARLIER*");
        var absent = () => ReferenceDataSet.RequireLookupTargets(Pack, [source]);
        absent.Should().Throw<InvalidOperationException>().WithMessage("*EARLIER*");
    }

    [Fact]
    public void D5_ALookupMustPointAtARowThatExists_AndAScopedTableMustSetItsScopeOnEveryRow()
    {
        var target = ReferenceDataSet.Parse(TargetJson());

        var dangling = () => ReferenceDataSet.RequireLookupTargets(Pack,
            [target, ReferenceDataSet.Parse(SourceJson(targetId: "cccccccc-0000-4000-8000-00000000000c"))]);
        dangling.Should().Throw<InvalidOperationException>().WithMessage("*cccccccc-0000-4000-8000-00000000000c*is not a row of 'sprk_target'*");

        var unscoped = () => ReferenceDataSet.RequireLookupTargets(Pack,
            [target, ReferenceDataSet.Parse(SourceJson(scope: "sprk_target", setLookup: false))]);
        unscoped.Should().Throw<InvalidOperationException>().WithMessage("*match scope 'sprk_target' must be set on every row*");
    }

    [Fact]
    public void D5_TheParser_RefusesAScopeThatIsNotALookup_AndLookupsInATier1File()
    {
        var scopeNotALookup = () => ReferenceDataSet.Parse(SourceJson(scope: "sprk_nope"));
        scopeNotALookup.Should().Throw<InvalidOperationException>().WithMessage("*naturalKeyScope*sprk_nope*lookups*");

        var tier1WithLookups = () => ReferenceDataSet.Parse(SourceJson().Replace("\"match\": \"naturalKey\", \"nameColumn\": \"sprk_name\",", string.Empty, StringComparison.Ordinal));
        tier1WithLookups.Should().Throw<InvalidOperationException>().WithMessage("*lookups*starter-pack*");

        var inColumns = () => ReferenceDataSet.Parse(SourceJson().Replace("\"columns\": [\"sprk_name\"]", "\"columns\": [\"sprk_name\", \"sprk_target\"]", StringComparison.Ordinal)
            .Replace(", \"sprk_target\": \"" + TargetIdA + "\"", ", \"sprk_target\": null", StringComparison.Ordinal));
        inColumns.Should().Throw<InvalidOperationException>().WithMessage("*lookup column must not also be listed*");
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private static Task<AppConfigSeederResult> RunAsync(DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler fake, string pack = Pack)
        => new DataverseWebApiReferenceDataSeeder(
                new HttpClient(fake),
                Options.Create(new AppConfigSeedOptions()),
                NullLogger<DataverseWebApiReferenceDataSeeder>.Instance,
                _ => new DataverseWebApiReferenceDataSeederTests.StaticTokenCredential(),
                () => [],
                ReferenceDataSet.Pack)
            .SeedAsync(new AppConfigSeedInput("acme", TenantId, EnvUrl, pack), CancellationToken.None);

    private static string EntitySet(Uri uri) => uri.AbsolutePath.Split('/')[^1];

    /// <summary>The id a <c>Nav@odata.bind</c> property points at (<c>/entityset(id)</c>), asserting the entity set.</summary>
    private static string BoundId(JsonElement row, string bindProperty, string expectedEntitySet)
    {
        var target = row.GetProperty(bindProperty).GetString()!;
        target.Should().StartWith($"/{expectedEntitySet}(").And.EndWith(")");
        return target[(target.IndexOf('(', StringComparison.Ordinal) + 1)..^1];
    }

    private static JsonElement PostedRow(DataverseWebApiReferenceDataSeederTests.FakeDataverseHandler fake, string entitySet, string column, string value)
        => fake.Posts.Where(p => EntitySet(p.Uri) == entitySet)
            .Select(p => JsonDocument.Parse(p.Body).RootElement)
            .Single(r => r.TryGetProperty(column, out var v) && v.GetString() == value);

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!.Replace("\r\n", "\n", StringComparison.Ordinal) : null;

    private static string? NullIfEmpty(string value) => value.Trim() is { Length: > 0 } t ? t.Replace("\r\n", "\n", StringComparison.Ordinal) : null;

    /// <summary>A small RFC 4180 reader (quoted fields, doubled quotes, line breaks inside quotes); the first row is the header.</summary>
    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        var header = records[0];
        return records.Skip(1).Where(r => r.Count > 1).Select(r => header.Zip(r).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal)).ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "scripts", "seed-data", "ebilling")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root (scripts/seed-data/ebilling) not found above the test binary.");
    }
}
