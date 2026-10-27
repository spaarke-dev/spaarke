// spaarke-ontology-platform-r1 task 101 (fix round 1, F3-4) — LIVE proof of the rule-authoring routes against a real Dataverse, as
// REAL users: an Ontology Administrator's publish supersedes the one open Signal of an enabled rule and re-raises it as episode 2;
// a Spaarke Console User's dry run is refused (403 ontology.admin.forbidden, nothing evaluated) and their save is refused by
// Dataverse itself (403 ontology.admin.refused, nothing created).
//
// Opt-in only (skip-via-return, the repo's convention): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_101_DATAVERSE_URL=https://<env>.crm.dynamics.com        (spaarkedev1)
//   SPAARKE_LIVE_101_ADMIN_ID=<systemuserid of a user holding Spaarke Ontology Administrator>
//   SPAARKE_LIVE_101_CONSOLE_USER_ID=<systemuserid of a Spaarke Console User WITHOUT it>
//   SPAARKE_LIVE_101_MATTER_ID=<sprk_matterid of an ACTIVE matter the admin can read: the test rule holds for it alone>
//   SPAARKE_LIVE_101_WRITER_ID=<systemuserid of the Ontology writer principal (spaarkedev1: 3121bf1b-9fbf-f111-aaaf-0022482913fc)>
// and an `az login` session for an identity that may create policies and impersonate (System Administrator).
//
// Substitutions are WorkAssignmentCreateLiveTests' (its factory is reused): the inbound token carries the acting user's real oid;
// the caller's Dataverse is the operator's token + MSCRMCallerID (WhoAmI answered with the acting user); the BFF's app identity
// (the evaluator's reads) is the operator's AzureCliCredential. One more, here: the Signal WRITER's managed identity cannot be
// reached from a workstation, so OntologyWriterDataverseClient is the operator's ServiceClient impersonating the writer principal
// (the SignalWriterSeamTests method).
//
// Every row is named "zz-101-test …" (policy code ZZ-101-…) and deleted in `finally`: the policy's Signals, any Decision Record citing
// its versions, the versions and the policy. Every delete is checked and the zz-101 count is confirmed zero by query.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.IntegrationTests.WorkAssignments;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Ontology;

public sealed class OntologyAdminAuthoringLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_101_DATAVERSE_URL";
    private const string Root = "/api/v1/ontology/admin/authoring";
    private const string Template = "zz-101-test: this matter needs a look.";
    private const string DecidePlan = """{"actions":["send-budget-inquiry"]}""";

    private readonly ITestOutputHelper _out;

    public OntologyAdminAuthoringLiveTests(ITestOutputHelper output) => _out = output;

    private static string BodyFor(Guid matter) =>
        $$"""{"type":"Existence","subject":"sprk_matter","when":{"sprk_matterid":"{{matter:D}}","statecode":0},"all":[]}""";

    private static object Draft(string body, string template = Template) => new
    {
        ruleType = "Existence",
        ruleBody = body,
        messageTemplate = template,
        shortHeadline = "zz-101-test",
        proposedAction = "zz-101-test: review the matter",
        decisionPlan = DecidePlan,
    };

    [Fact]
    public async Task LiveMode_AnAdminPublish_SupersedesTheOneOpenSignal_AndReRaisesItAsEpisode2()
    {
        if (Environment.GetEnvironmentVariable(UrlVar) is not { Length: > 0 } url)
            return; // not opted in
        if (!TryIds(out var admin, out _, out var matter, out var writer))
            return;

        var credential = new AzureCliCredential();
        using var dv = await WorkAssignmentCreateLiveTests.DataverseClientAsync(url, credential);
        await using var factory = new WorkAssignmentCreateLiveTests.LiveWorkAssignmentFactory(url, credential, _out);
        using var writerClient = WriterClient(url, credential, writer);
        await using var host = WithWriter(factory, writerClient);
        using var bff = host.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var code = "ZZ-101-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Guid? policyId = null;
        try
        {
            await factory.ActAsAsync(admin);

            // 1. A new rule that holds for the one configured matter: dry run, then save (Off), as the admin.
            var v1Body = BodyFor(matter);
            var dry1 = await bff.PostAsJsonAsync($"{Root}/dry-run",
                new { lane = "Decide", ruleType = "Existence", ruleBody = v1Body, messageTemplate = Template });
            _out.WriteLine($"[admin] dry run (new) -> HTTP {(int)dry1.StatusCode} {await dry1.Content.ReadAsStringAsync()}");
            dry1.StatusCode.Should().Be(HttpStatusCode.OK);

            var save = await bff.PostAsJsonAsync($"{Root}/policies",
                new { policyCode = code, name = "zz-101-test live publish", lane = "Decide", draft = Draft(v1Body) });
            var saveText = await save.Content.ReadAsStringAsync();
            _out.WriteLine($"[admin] save -> HTTP {(int)save.StatusCode} {saveText}");
            if (save.StatusCode == HttpStatusCode.Created)
                policyId = JsonDocument.Parse(saveText).RootElement.GetProperty("policyId").GetGuid();
            save.StatusCode.Should().Be(HttpStatusCode.Created);

            // 2. The operator turns it On, and the job's single-policy pass raises episode 1 (as the writer).
            (await dv.PatchAsync($"sprk_policies({policyId:D})", JsonContent.Create(new { sprk_enabled = true }))).EnsureSuccessStatusCode();
            using (var scope = host.Services.CreateScope())
            {
                var first = await scope.ServiceProvider.GetRequiredService<IOntologyAdminAuthoringSignalEngine>()
                    .ReevaluateAsync(policyId!.Value, CancellationToken.None);
                _out.WriteLine($"[job] episode 1 pass: success={first.Success} raised={first.Raised} {first.FailureSummary}");
                first.Success.Should().BeTrue();
                first.Raised.Should().Be(1, "the rule holds for exactly the configured matter");
            }

            // 3. The admin dry-runs an edited rule (its template) on the policy, then publishes it.
            var v2Body = BodyFor(matter);
            const string v2Template = Template + " (v2)";
            var dry2 = await bff.PostAsJsonAsync($"{Root}/dry-run",
                new { policyId, ruleType = "Existence", ruleBody = v2Body, messageTemplate = v2Template });
            var dry2Text = await dry2.Content.ReadAsStringAsync();
            _out.WriteLine($"[admin] dry run (policy) -> HTTP {(int)dry2.StatusCode} {dry2Text}");
            dry2.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonDocument.Parse(dry2Text).RootElement.GetProperty("wouldReraise").GetInt32().Should().Be(1);

            var publish = await bff.PostAsJsonAsync($"{Root}/policies/{policyId:D}/versions", new { draft = Draft(v2Body, v2Template) });
            var publishText = await publish.Content.ReadAsStringAsync();
            _out.WriteLine($"[admin] publish -> HTTP {(int)publish.StatusCode} {publishText}");
            publish.StatusCode.Should().Be(HttpStatusCode.OK);
            var published = JsonDocument.Parse(publishText).RootElement;
            published.GetProperty("versionNumber").GetInt32().Should().Be(2);
            published.GetProperty("reevaluation").GetString().Should().Be("completed");
            published.GetProperty("supersededClosed").GetInt32().Should().Be(1);
            published.GetProperty("raised").GetInt32().Should().Be(1);

            // 4. Read back as the operator: episode 1 closed, episode 2 open, citing the new version.
            var signals = (await dv.GetFromJsonAsync<JsonElement>(
                    $"sprk_signals?$select=sprk_dedupekey,sprk_signalstatus,_sprk_policyversion_value&$filter=_sprk_policy_value eq {policyId:D}"))
                .GetProperty("value").EnumerateArray().ToList();
            foreach (var s in signals)
                _out.WriteLine($"[read] {s.GetProperty("sprk_dedupekey").GetString()} status={s.GetProperty("sprk_signalstatus")}");
            var episode2 = signals.Should().ContainSingle(s => s.GetProperty("sprk_dedupekey").GetString()!.EndsWith("|2", StringComparison.Ordinal)).Subject;
            episode2.GetProperty("sprk_signalstatus").GetInt32().Should().BeOneOf(SignalWriter.SignalStatusOpen, SignalWriter.SignalStatusAcknowledged);
            episode2.GetProperty("_sprk_policyversion_value").GetGuid().Should().Be(published.GetProperty("policyVersionId").GetGuid());
            signals.Should().ContainSingle(s => s.GetProperty("sprk_dedupekey").GetString()!.EndsWith("|1", StringComparison.Ordinal)
                && s.GetProperty("sprk_signalstatus").GetInt32() != SignalWriter.SignalStatusOpen, "episode 1 was superseded");
        }
        finally
        {
            await CleanUpAsync(dv, code);
        }
    }

    [Fact]
    public async Task LiveMode_AConsoleUser_DryRunIs403_AndSaveIsRefusedByDataverse_403()
    {
        if (Environment.GetEnvironmentVariable(UrlVar) is not { Length: > 0 } url)
            return; // not opted in
        if (!TryIds(out _, out var consoleUser, out var matter, out var writer))
            return;

        var credential = new AzureCliCredential();
        using var dv = await WorkAssignmentCreateLiveTests.DataverseClientAsync(url, credential);
        await using var factory = new WorkAssignmentCreateLiveTests.LiveWorkAssignmentFactory(url, credential, _out);
        using var writerClient = WriterClient(url, credential, writer);
        await using var host = WithWriter(factory, writerClient);
        using var bff = host.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var code = "ZZ-101-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var body = BodyFor(matter);
        try
        {
            await factory.ActAsAsync(consoleUser);

            // 1. The dry run: the privilege, asked as the Console User, is not held. Nothing is evaluated.
            var dry = await bff.PostAsJsonAsync($"{Root}/dry-run",
                new { lane = "Decide", ruleType = "Existence", ruleBody = body, messageTemplate = Template });
            var dryText = await dry.Content.ReadAsStringAsync();
            _out.WriteLine($"[console] dry run -> HTTP {(int)dry.StatusCode} {dryText}");
            dry.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            dryText.Should().Contain("ontology.admin.forbidden");

            // 2. The save. A Console User cannot obtain a proof (step 1), so one is planted in the test host's own tenant cache: the
            //    save then reaches the deep insert, and Dataverse's refusal of it AS THE CONSOLE USER is what is under test.
            using (var scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ITenantCache>().SetAsync("live", OntologyAdminAuthoringService.DryRunProofResource,
                    OntologyAdminAuthoringService.ProofKey(consoleUser, null, DecisionLane.Decide, "Existence", body, Template),
                    OntologyAdminAuthoringService.DryRunProofVersion, new OntologyAdminAuthoringService.DryRunProof(DateTimeOffset.UtcNow, null),
                    TimeSpan.FromMinutes(5));
            }

            var before = await CountZzPoliciesAsync(dv);
            var save = await bff.PostAsJsonAsync($"{Root}/policies",
                new { policyCode = code, name = "zz-101-test console user", lane = "Decide", draft = Draft(body) });
            var saveText = await save.Content.ReadAsStringAsync();
            _out.WriteLine($"[console] save -> HTTP {(int)save.StatusCode} {saveText}");
            save.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Dataverse refuses the Console User's create");
            saveText.Should().Contain("ontology.admin.refused");
            (await CountZzPoliciesAsync(dv)).Should().Be(before, "a refused save creates nothing");
        }
        finally
        {
            await CleanUpAsync(dv, code);
        }
    }

    private bool TryIds(out Guid admin, out Guid consoleUser, out Guid matter, out Guid writer)
    {
        var ok = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_101_ADMIN_ID"), out admin)
                 & Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_101_CONSOLE_USER_ID"), out consoleUser)
                 & Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_101_MATTER_ID"), out matter)
                 & Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_101_WRITER_ID"), out writer);
        if (!ok)
            _out.WriteLine("SPAARKE_LIVE_101_ADMIN_ID / _CONSOLE_USER_ID / _MATTER_ID / _WRITER_ID are not all set; skipped");
        return ok;
    }

    /// <summary>The operator's ServiceClient impersonating the writer principal (the SignalWriterSeamTests method).</summary>
    private static ServiceClient WriterClient(string url, TokenCredential credential, Guid writer)
    {
        var instance = new Uri(url.TrimEnd('/'));
        var scope = $"{instance.GetLeftPart(UriPartial.Authority)}/.default";
        var client = new ServiceClient(
            instanceUrl: instance,
            tokenProviderFunction: _ => Task.FromResult(credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None).Token),
            useUniqueInstance: true);
        if (!client.IsReady)
            throw new InvalidOperationException($"Writer ServiceClient not ready: {client.LastError}");
        client.CallerId = writer;
        return client;
    }

    private static WebApplicationFactory<Program> WithWriter(WorkAssignmentCreateLiveTests.LiveWorkAssignmentFactory factory, ServiceClient writer) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<OntologyWriterDataverseClient>();
            services.AddSingleton(sp => new OntologyWriterDataverseClient(() => writer, sp.GetRequiredService<ILogger<OntologyWriterDataverseClient>>()));
        }));

    private static async Task<int> CountZzPoliciesAsync(HttpClient dv) =>
        (await dv.GetFromJsonAsync<JsonElement>("sprk_policies?$select=sprk_policyid&$filter=startswith(sprk_policycode,'ZZ-101-')"))
        .GetProperty("value").GetArrayLength();

    /// <summary>Removes every row a run of this class may have left, by the policy code: Signals, Decision Records, versions, policy.</summary>
    private async Task CleanUpAsync(HttpClient dv, string code)
    {
        var leaks = new List<string>();
        var policies = (await dv.GetFromJsonAsync<JsonElement>($"sprk_policies?$select=sprk_policyid&$filter=sprk_policycode eq '{code}'"))
            .GetProperty("value").EnumerateArray().Select(p => p.GetProperty("sprk_policyid").GetGuid()).ToList();
        // Turned Off FIRST, every one of them: if a delete below then fails, the leaked rule is Off and the nightly job never fires it on
        // a real matter (review pass 2).
        foreach (var policy in policies)
            leaks.AddRange(await DisableAsync(dv, policy));

        foreach (var policy in policies)
        {
            var versions = (await dv.GetFromJsonAsync<JsonElement>($"sprk_policyversions?$select=sprk_policyversionid&$filter=_sprk_policy_value eq {policy:D}"))
                .GetProperty("value").EnumerateArray().Select(v => v.GetProperty("sprk_policyversionid").GetGuid()).ToList();
            foreach (var signal in (await dv.GetFromJsonAsync<JsonElement>($"sprk_signals?$select=sprk_signalid&$filter=_sprk_policy_value eq {policy:D}"))
                         .GetProperty("value").EnumerateArray())
                leaks.AddRange(await DeleteAsync(dv, $"sprk_signals({signal.GetProperty("sprk_signalid").GetGuid():D})"));
            foreach (var version in versions)
                foreach (var record in (await dv.GetFromJsonAsync<JsonElement>(
                             $"sprk_decisionrecords?$select=sprk_decisionrecordid&$filter=_sprk_policyversion_value eq {version:D}"))
                         .GetProperty("value").EnumerateArray())
                    leaks.AddRange(await DeleteAsync(dv, $"sprk_decisionrecords({record.GetProperty("sprk_decisionrecordid").GetGuid():D})"));
            leaks.AddRange(await DeleteAsync(dv, $"sprk_policies({policy:D})"));
            foreach (var version in versions)
                leaks.AddRange(await DeleteAsync(dv, $"sprk_policyversions({version:D})"));
        }

        var remaining = await CountZzPoliciesAsync(dv);
        _out.WriteLine($"cleanup {code}: {policies.Count} policies; leaks = [{string.Join("; ", leaks)}]; ZZ-101 policies remaining = {remaining}");
        leaks.Should().BeEmpty("every zz-101 row is removed");
        remaining.Should().Be(0, "no zz-101 policy is left behind");
    }

    /// <summary>PATCH sprk_enabled=false. If-Match * makes it an update only: it never creates a row that is already gone.</summary>
    private static async Task<List<string>> DisableAsync(HttpClient dv, Guid policy)
    {
        var path = $"sprk_policies({policy:D})";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(new { sprk_enabled = false }) };
            request.Headers.TryAddWithoutValidation("If-Match", "*");
            using var r = await dv.SendAsync(request);
            return r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound
                ? new List<string>()
                : new List<string> { $"{path} (turn Off): HTTP {(int)r.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new List<string> { $"{path} (turn Off): {ex.Message}" };
        }
    }

    private static async Task<List<string>> DeleteAsync(HttpClient dv, string path)
    {
        try
        {
            var r = await dv.DeleteAsync(path);
            return r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound
                ? new List<string>()
                : new List<string> { $"{path}: HTTP {(int)r.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new List<string> { $"{path}: {ex.Message}" };
        }
    }
}
