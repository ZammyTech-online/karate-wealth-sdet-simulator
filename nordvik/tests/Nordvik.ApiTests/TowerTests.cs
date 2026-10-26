using System.Net;
using System.Text.Json;
using Nordvik.Host;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>
/// La Torre de control y la escalera de niveles (ADR-014): encender y apagar en caliente, dependencias, misiones,
/// la pasarela y, sobre todo, que código, perfiles, academia y escalera digan lo mismo.
/// </summary>
[Collection("nordvik")]
public class TowerTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public TowerTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => Task.CompletedTask;
    /// <summary>Pase lo que pase, el resto de la suite necesita todo encendido.</summary>
    public async Task DisposeAsync() => await _f.Supervisor.Start(_f.Supervisor.Catalog.Keys);

    private static Escalera Escalera(NordvikFixture f) => new(NordvikFixture.RepoRoot, f.Supervisor.Catalog);

    private async Task<bool> Up(int port)
    {
        try { return (await _f.Http.GetAsync(NordvikFixture.Url(port, "/health"))).IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
    }

    // ---------- Coherencia: una sola verdad ----------

    [Fact]
    public void Each_level_profile_is_exactly_the_services_of_waves_up_to_that_level()
    {
        var e = Escalera(_f);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, e.File.Levels.Select(l => l.Id).OrderBy(i => i));
        foreach (var level in e.File.Levels)
        {
            var file = Path.Combine(NordvikFixture.RepoRoot, "profiles", level.Profile + ".json");
            Assert.True(File.Exists(file), "falta el perfil " + level.Profile);
            var profile = JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("services").EnumerateArray().Select(s => s.GetString()!).OrderBy(s => s);
            Assert.Equal(e.ServicesOfLevel(level.Id), profile);
        }
        Assert.Equal(168, e.ServicesOfLevel(4).Count);
        Assert.Equal(156, e.ServicesOfLevel(4).Count(n => _f.Supervisor.Catalog[n].Info.Domain != "X-external"));
    }

    [Fact]
    public void Every_lesson_belongs_to_one_level_and_only_uses_services_of_that_level()
    {
        var e = Escalera(_f);
        var lessons = e.Lessons();
        Assert.Equal(34, lessons.Count);
        var listed = e.File.Levels.SelectMany(l => l.Lessons).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(lessons.Select(l => l.Id).OrderBy(x => x), listed.OrderBy(x => x));
        foreach (var lesson in lessons)
        {
            Assert.NotEmpty(lesson.Services);
            var outside = lesson.Services.Except(e.ServicesOfLevel(lesson.Level)).ToList();
            Assert.True(outside.Count == 0, $"{lesson.Id} (nivel {lesson.Level}) usa servicios de un nivel superior: {string.Join(", ", outside)}");
        }
        // Cada nivel necesita algo que el anterior no tiene (si no, la lección estaría en el nivel equivocado)
        foreach (var level in e.File.Levels.Where(l => l.Id > 0))
            Assert.Contains(lessons.Where(l => l.Level == level.Id), l => l.Services.Except(e.ServicesOfLevel(level.Id - 1)).Any());
    }

    [Fact]
    public void The_academy_script_reads_the_levels_from_the_escalera()
    {
        var script = File.ReadAllText(Path.Combine(NordvikFixture.RepoRoot, "academia", "academia.ps1"));
        Assert.Contains("escalera.json", script);
        Assert.DoesNotContain("L2[2-7]", script);
    }

    [Fact]
    public async Task Every_mission_step_is_a_real_endpoint_of_a_service_of_its_level()
    {
        var e = Escalera(_f);
        Assert.True(e.File.Missions.Count >= 20);
        Assert.Equal(e.File.Missions.Count, e.File.Missions.Select(m => m.Id).Distinct().Count());
        var lessons = e.Lessons().Select(l => l.Id).ToHashSet();
        var traps = Nordvik.Services.ChaosControl.Catalog.Select(t => t.Id).ToHashSet();
        var specs = new Dictionary<string, JsonElement>();
        foreach (var m in e.File.Missions)
        {
            Assert.Contains(m.Level, e.File.Levels.Select(l => l.Id));
            var services = m.Services.Concat(m.Steps.Select(s => s.Service)).Distinct().ToList();
            foreach (var s in services)
            {
                Assert.True(_f.Supervisor.Knows(s), $"{m.Id}: no existe {s}");
                Assert.True(Nordvik.Host.Escalera.LevelOf(_f.Supervisor.Catalog[s].Info) <= m.Level, $"{m.Id} (nivel {m.Level}) usa {s}, que es de un nivel superior");
                foreach (var d in _f.Supervisor.DependenciesOf(s))
                    Assert.True(Nordvik.Host.Escalera.LevelOf(_f.Supervisor.Catalog[d].Info) <= m.Level, $"{m.Id}: {s} depende de {d}, de un nivel superior");
            }
            Assert.All(m.Lessons, l => Assert.Contains(l, lessons));
            Assert.All(m.Traps, t => Assert.Contains(t, traps));
            if (m.Flow is not null) Assert.True(File.Exists(Path.Combine(NordvikFixture.RepoRoot, "docs", "flows", m.Flow)), $"{m.Id}: falta el flujo {m.Flow}");
            Assert.NotEmpty(m.Steps);
            foreach (var step in m.Steps)
            {
                if (!specs.TryGetValue(step.Service, out var spec))
                {
                    var r = await _f.Get(_f.Supervisor.Catalog[step.Service].Info.Port, "/swagger/v1/swagger.json");
                    Assert.Equal(200, r.Code);
                    specs[step.Service] = spec = r.Json;
                }
                Assert.True(spec.GetProperty("paths").TryGetProperty(step.Path, out var ops), $"{m.Id}: {step.Service} no tiene {step.Path}");
                Assert.True(ops.TryGetProperty(step.Method.ToLowerInvariant(), out _), $"{m.Id}: {step.Service} no acepta {step.Method} {step.Path}");
            }
        }
        // Todos los flujos documentados tienen su misión
        var flows = Directory.GetFiles(Path.Combine(NordvikFixture.RepoRoot, "docs", "flows"), "F-*.md").Select(Path.GetFileName);
        Assert.All(flows, f => Assert.Contains(e.File.Missions, m => m.Flow == f));
    }

    // ---------- La API de la torre ----------

    [Fact]
    public async Task Status_levels_and_domains_describe_the_whole_company()
    {
        var st = await _f.Get(Ports.Tower, "/api/v1/estado");
        Assert.Equal(200, st.Code);
        Assert.Equal(168, st.Int("total"));
        Assert.Equal(156, st.Int("businessServices"));
        Assert.Equal(12, st.Int("externals"));
        Assert.Equal(168, st.Int("running"));
        Assert.Equal(4, st.Int("currentLevel"));
        Assert.True(st.Json.GetProperty("memoryMb").GetInt64() > 0);

        var lv = await _f.Get(Ports.Tower, "/api/v1/niveles");
        var levels = lv.Json.GetProperty("levels").EnumerateArray().ToList();
        Assert.Equal(new[] { 8, 30, 79, 137, 168 }, levels.Select(l => l.GetProperty("services").GetInt32()));
        Assert.All(levels, l => Assert.Equal("ON", l.GetProperty("state").GetString()));
        Assert.Equal(187, levels.Sum(l => l.GetProperty("exercises").GetInt32()));
        Assert.Equal(30, levels.Sum(l => l.GetProperty("traps").GetArrayLength()));
        Assert.Equal("Pensiones", levels[3].GetProperty("newDomains").EnumerateArray().First(d => d.GetProperty("code").GetString() == "L-pensions").GetProperty("name").GetString());
        (await _f.Get(Ports.Tower, "/api/v1/niveles/7")).ShouldBeProblem(404, "LEVEL_NOT_FOUND");

        var dom = await _f.Get(Ports.Tower, "/api/v1/dominios");
        Assert.Equal(18, dom.Json.GetArrayLength());
        var pensions = dom.Json.EnumerateArray().First(d => d.GetProperty("code").GetString() == "L-pensions");
        Assert.Equal(14, pensions.GetProperty("services").GetInt32());

        var traps = await _f.Get(Ports.Tower, "/api/v1/trampas");
        Assert.Equal(30, traps.Json.GetArrayLength());
        Assert.Equal(11, traps.Json.EnumerateArray().Count(t => t.GetProperty("nature").GetString() == "ACTIVABLE"));
    }

    [Fact]
    public async Task Service_card_explains_what_it_is_who_needs_it_and_where_to_practise()
    {
        var r = await _f.Get(Ports.Tower, "/api/v1/servicios/product-catalog");
        Assert.Equal(200, r.Code);
        Assert.Equal("Mercado", r.Json.GetProperty("service").GetProperty("domainName").GetString());
        Assert.Equal(0, r.Json.GetProperty("service").GetProperty("level").GetInt32());
        Assert.Contains(r.Json.GetProperty("endpoints").EnumerateArray(), e => e.GetProperty("path").GetString() == "/api/v1/funds/{isin}");
        Assert.Contains(r.Json.GetProperty("lessons").EnumerateArray(), l => l.GetProperty("id").GetString() == "L00");
        Assert.Contains(r.Json.GetProperty("missions").EnumerateArray(), m => m.GetProperty("id").GetString() == "M01");
        Assert.True(r.Json.GetProperty("dependents").GetArrayLength() > 0);
        Assert.EndsWith(":16801/swagger", r.Str("swagger"));

        var list = await _f.Get(Ports.Tower, "/api/v1/servicios?dominio=L-pensions");
        Assert.Equal(14, list.Int("count"));
        Assert.Equal(8, (await _f.Get(Ports.Tower, "/api/v1/servicios?nivel=0")).Int("count"));
        (await _f.Get(Ports.Tower, "/api/v1/servicios/no-existe")).ShouldBeProblem(404, "SERVICE_NOT_FOUND");
    }

    [Fact]
    public async Task A_leaf_service_switches_off_and_on_again_and_keeps_its_data()
    {
        var port = _f.Supervisor.Catalog["esg-data"].Info.Port;
        Assert.Empty(_f.Supervisor.DependentsOf("esg-data"));
        var before = await _f.Get(port, "/health");
        Assert.Equal(200, before.Code);

        var off = await _f.Post(Ports.Tower, "/api/v1/servicios/esg-data/apagar");
        Assert.Equal(200, off.Code);
        Assert.Equal("esg-data", off.Json.GetProperty("stopped")[0].GetString());
        Assert.False(await Up(port));
        Assert.False(_f.Supervisor.IsRunning("esg-data"));
        (await _f.Get(Ports.Tower, "/api/v1/proxy/esg-data/health")).ShouldBeProblem(503, "SERVICE_OFF");
        Assert.Equal(3, (await _f.Get(Ports.Tower, "/api/v1/estado")).Int("currentLevel"));
        Assert.Equal(0, (await _f.Post(Ports.Tower, "/api/v1/servicios/esg-data/apagar")).Json.GetProperty("stopped").GetArrayLength());

        var on = await _f.Post(Ports.Tower, "/api/v1/servicios/esg-data/encender");
        Assert.Equal(200, on.Code);
        Assert.Contains("esg-data", on.Json.GetProperty("started").EnumerateArray().Select(x => x.GetString()));
        Assert.True(await Up(port));
        Assert.Equal(4, (await _f.Get(Ports.Tower, "/api/v1/estado")).Int("currentLevel"));
    }

    [Fact]
    public async Task Switching_off_something_others_need_asks_first_and_cascades_only_when_told()
    {
        (await _f.Post(Ports.Tower, "/api/v1/servicios/clock-service/apagar")).ShouldBeProblem(409, "CORE_SERVICE");
        // Un servicio que depende directamente de product-catalog
        var direct = _f.Supervisor.Catalog.Values.First(s => (s.Info.DependsOn ?? Array.Empty<string>()).Contains("product-catalog")).Info;
        var blocked = await _f.Post(Ports.Tower, "/api/v1/servicios/product-catalog/apagar");
        blocked.ShouldBeProblem(409, "HAS_RUNNING_DEPENDENTS");
        var dependents = blocked.Json.GetProperty("dependents").EnumerateArray().Select(d => d.GetString()!).ToList();
        Assert.Contains(direct.Name, dependents);
        Assert.Equal(_f.Supervisor.DependentsOf("product-catalog").OrderBy(x => x), dependents);
        Assert.True(_f.Supervisor.IsRunning("product-catalog"), "sin cascada no se apaga nada");

        try
        {
            var cascade = await _f.Post(Ports.Tower, "/api/v1/servicios/product-catalog/apagar?cascada=true");
            Assert.Equal(200, cascade.Code);
            var stopped = cascade.Json.GetProperty("stopped").EnumerateArray().Select(d => d.GetString()!).ToList();
            Assert.Contains("product-catalog", stopped);
            Assert.All(dependents, d => Assert.Contains(d, stopped));
            Assert.True(stopped.IndexOf(direct.Name) < stopped.IndexOf("product-catalog"), "primero se apaga quien depende, después aquello de lo que depende");
            Assert.False(await Up(direct.Port));

            // Encender ese servicio trae de vuelta product-catalog (su dependencia), y antes que él
            var on = await _f.Post(Ports.Tower, $"/api/v1/servicios/{direct.Name}/encender");
            var started = on.Json.GetProperty("started").EnumerateArray().Select(d => d.GetString()!).ToList();
            Assert.True(started.IndexOf("product-catalog") < started.IndexOf(direct.Name), "las dependencias arrancan antes");
            Assert.True(await Up(direct.Port));
            Assert.Equal(200, (await _f.Get(Ports.Catalog, "/api/v1/funds/SE0000000001")).Code);
        }
        finally { await _f.Supervisor.Start(_f.Supervisor.Catalog.Keys); }
    }

    [Fact]
    public async Task A_domain_switches_off_and_on_as_a_block()
    {
        try
        {
            var first = await _f.Post(Ports.Tower, "/api/v1/dominios/M-insurance/apagar");
            if (first.Code == 409)
            {
                first.ShouldBeProblem(409, "HAS_RUNNING_DEPENDENTS");
                Assert.All(first.Json.GetProperty("dependents").EnumerateArray(), d => Assert.NotEqual("M-insurance", _f.Supervisor.Catalog[d.GetString()!].Info.Domain));
                first = await _f.Post(Ports.Tower, "/api/v1/dominios/M-insurance/apagar?cascada=true");
            }
            Assert.Equal(200, first.Code);
            var dom = (await _f.Get(Ports.Tower, "/api/v1/dominios")).Json.EnumerateArray().First(d => d.GetProperty("code").GetString() == "M-insurance");
            Assert.Equal(0, dom.GetProperty("running").GetInt32());
            Assert.Equal("PARTIAL", (await _f.Get(Ports.Tower, "/api/v1/niveles/4")).Str("state"));

            var on = await _f.Post(Ports.Tower, "/api/v1/dominios/M-insurance/encender");
            Assert.Equal(200, on.Code);
            Assert.Equal(4, on.Json.GetProperty("started").EnumerateArray().Count(s => _f.Supervisor.Catalog[s.GetString()!].Info.Domain == "M-insurance"));
            (await _f.Post(Ports.Tower, "/api/v1/dominios/Z-nada/encender")).ShouldBeProblem(404, "DOMAIN_NOT_FOUND");
        }
        finally { await _f.Supervisor.Start(_f.Supervisor.Catalog.Keys); }
    }

    [Fact]
    public async Task Levels_switch_the_whole_company_down_to_eight_services_and_back_up_to_168()
    {
        try
        {
            var down = await _f.Post(Ports.Tower, "/api/v1/niveles/0/encender");
            Assert.Equal(200, down.Code);
            Assert.Equal(160, down.Json.GetProperty("stopped").GetArrayLength());
            Assert.Equal(8, down.Int("running"));
            Assert.Equal(0, (await _f.Get(Ports.Tower, "/api/v1/estado")).Int("currentLevel"));
            Assert.False(await Up(Ports.Iam));
            Assert.Equal(200, (await _f.Get(Ports.Catalog, "/api/v1/funds/SE0000000001")).Code);

            // La misión M04 (alta de cliente, nivel 1) enciende solo lo que necesita
            var mission = await _f.Get(Ports.Tower, "/api/v1/misiones/M04");
            Assert.False(mission.Json.GetProperty("ready").GetBoolean());
            Assert.Contains("iam-service", mission.Json.GetProperty("missing").EnumerateArray().Select(m => m.GetString()));
            var prep = await _f.Post(Ports.Tower, "/api/v1/misiones/M04/preparar");
            Assert.True(prep.Json.GetProperty("ready").GetBoolean());
            Assert.True(prep.Int("running") < 30);
            Assert.True((await _f.Get(Ports.Tower, "/api/v1/misiones/M04")).Json.GetProperty("ready").GetBoolean());
            var token = await _f.Erik();
            Assert.Equal(200, (await _f.Get(Ports.Customer, "/api/v1/customers?limit=1", NordvikFixture.Bearer(token))).Code);

            // Lo mismo con una lección
            var lessons = await _f.Get(Ports.Tower, "/api/v1/lecciones");
            var l20 = lessons.Json.EnumerateArray().First(l => l.GetProperty("id").GetString() == "L20");
            Assert.False(l20.GetProperty("ready").GetBoolean());
            Assert.True((await _f.Post(Ports.Tower, "/api/v1/lecciones/L20/preparar")).Code == 200);
            Assert.True((await _f.Get(Ports.Tower, "/api/v1/lecciones")).Json.EnumerateArray().First(l => l.GetProperty("id").GetString() == "L20").GetProperty("ready").GetBoolean());
            (await _f.Post(Ports.Tower, "/api/v1/lecciones/L99/preparar")).ShouldBeProblem(404, "LESSON_NOT_FOUND");

            // "sumar" añade el nivel 2 sin apagar lo que ya había
            var add = await _f.Post(Ports.Tower, "/api/v1/niveles/2/encender?modo=sumar");
            Assert.Equal(0, add.Json.GetProperty("stopped").GetArrayLength());
            Assert.Equal(2, (await _f.Get(Ports.Tower, "/api/v1/estado")).Int("currentLevel"));
            (await _f.Post(Ports.Tower, "/api/v1/niveles/2/encender?modo=raro")).ShouldBeProblem(400, "INVALID_MODE");
        }
        finally
        {
            var up = await _f.Post(Ports.Tower, "/api/v1/niveles/4/encender");
            Assert.Equal(200, up.Code);
            Assert.Equal(0, up.Json.GetProperty("failed").EnumerateObject().Count());
        }
        // Todo vuelve a funcionar tras reiniciar: cada servicio sobrevive a un apagado y encendido
        Assert.Equal(168, (await _f.Get(Ports.Tower, "/api/v1/estado")).Int("running"));
        foreach (var s in _f.Supervisor.Catalog.Values)
            Assert.True(await Up(s.Info.Port), s.Info.Name + " no volvió");
        var ana = await _f.Ana();
        Assert.Equal(200, (await _f.Get(Ports.Customer, "/api/v1/customers/c0000000-0000-4000-8000-000000000001", NordvikFixture.Bearer(ana))).Code);
    }

    [Fact]
    public async Task Level_three_services_work_without_their_optional_level_four_helpers()
    {
        try
        {
            // insurance-policy y tax-withholding son del nivel 4: death-claims y payout-execution solo los usan si están encendidos
            Assert.Equal(200, (await _f.Post(Ports.Tower, "/api/v1/servicios/insurance-policy/apagar?cascada=true")).Code);
            Assert.Equal(200, (await _f.Post(Ports.Tower, "/api/v1/servicios/tax-withholding/apagar?cascada=true")).Code);
            Assert.True(_f.Supervisor.IsRunning("death-claims"));
            Assert.True(_f.Supervisor.IsRunning("payout-execution"));

            var card = await _f.Get(Ports.Tower, "/api/v1/servicios/death-claims");
            Assert.Contains(card.Json.GetProperty("optionalDependencies").EnumerateArray(), d => d.GetProperty("name").GetString() == "insurance-policy" && !d.GetProperty("running").GetBoolean());

            var customer = await People.Clean(_f);
            await Pensions.PlanId(_f, customer);
            var claim = await _f.Send(HttpMethod.Post, Ports.DeathClaims, "/api/v1/death-claims",
                JsonSerializer.Serialize(new { customerId = customer, dateOfDeath = "2026-10-01", reportedBy = "Hija" }), NordvikFixture.Bearer(await _f.Olof()));
            Assert.True(claim.Code == 201, claim.Body);
            Assert.Equal("NOT_CHECKED", claim.Json.GetProperty("insurancePart").GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, claim.Json.GetProperty("insurancePart").GetProperty("policies").ValueKind);

            // Sin tax-withholding, la pensión se paga igual con la retención provisional del 30 %
            var (_, plan) = await Pensions.FundedPlan(_f, new DateOnly(1959, 3, 15), "120000.00");
            var erik = NordvikFixture.Bearer(await _f.Erik());
            var po = await _f.Send(HttpMethod.Post, Ports.PayoutPlanner, $"/api/v1/pension-plans/{plan}/payout-plans", "{\"type\":\"TERM\",\"termYears\":5,\"startPeriod\":\"2027-02\"}", erik);
            Assert.True(po.Code == 201, po.Body);
            await _f.Send(HttpMethod.Post, Ports.PayoutPlanner, $"/api/v1/payout-plans/{po.Str("id")}/activate", null, erik);
            await _f.Freeze("2027-02-05T10:00:00");
            var run = await _f.Send(HttpMethod.Post, Ports.PayoutExecution, "/api/v1/payout-runs", "{\"period\":\"2027-02\"}", NordvikFixture.Bearer(await _f.Olof()));
            Assert.True(run.Code is 200 or 201, run.Body);
            var line = run.Json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("payoutPlanId").GetString() == po.Str("id"));
            Assert.Equal("PROVISIONAL_30", line.GetProperty("taxBasis").GetString());
        }
        finally
        {
            await _f.ResetClock();
            await _f.Supervisor.Start(_f.Supervisor.Catalog.Keys);
        }
    }

    [Fact]
    public async Task The_gateway_forwards_method_body_headers_and_status()
    {
        var funds = await _f.Get(Ports.Tower, "/api/v1/proxy/product-catalog/api/v1/funds?category=EQUITY");
        Assert.Equal(200, funds.Code);
        Assert.Equal("application/json", funds.ContentType?.MediaType);
        Assert.All(funds.Json.GetProperty("items").EnumerateArray(), f => Assert.Equal("EQUITY", f.GetProperty("category").GetString()));

        var token = await _f.Erik();
        var me = await _f.Get(Ports.Tower, "/api/v1/proxy/iam-service/api/v1/users/me", NordvikFixture.Bearer(token));
        Assert.Equal(200, me.Code);
        (await _f.Get(Ports.Tower, "/api/v1/proxy/iam-service/api/v1/users/me")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");

        try
        {
            var frozen = await _f.Send(HttpMethod.Post, Ports.Tower, "/api/v1/proxy/clock-service/api/v1/clock/freeze", "{\"at\":\"2026-10-20T10:00:00\"}");
            Assert.Equal(200, frozen.Code);
            Assert.StartsWith("2026-10-20T10:00:00", (await _f.Get(Ports.Clock, "/api/v1/clock/now")).Str("now"));
        }
        finally { await _f.ResetClock(); }

        var notFound = await _f.Get(Ports.Tower, "/api/v1/proxy/product-catalog/api/v1/funds/SE9999999999");
        Assert.Equal(404, notFound.Code);
        Assert.Equal("product-catalog", notFound.Str("service"));
        (await _f.Get(Ports.Tower, "/api/v1/proxy/no-existe/health")).ShouldBeProblem(404, "SERVICE_NOT_FOUND");
    }

    [Fact]
    public async Task The_web_and_the_flow_documents_are_served()
    {
        var page = await _f.Get(Ports.Tower, "/");
        Assert.Equal(200, page.Code);
        Assert.Equal("text/html", page.ContentType?.MediaType);
        Assert.Contains("Torre de control", page.Body);
        Assert.Equal(200, (await _f.Get(Ports.Tower, "/app.js")).Code);
        Assert.Equal(200, (await _f.Get(Ports.Tower, "/styles.css")).Code);

        var flow = await _f.Get(Ports.Tower, "/api/v1/flujos/F-07-traspaso.md");
        Assert.Equal(200, flow.Code);
        Assert.StartsWith("# F-07", flow.Body);
        (await _f.Get(Ports.Tower, "/api/v1/flujos/..%2F..%2FNordvik.sln")).ShouldBeProblem(404, "FLOW_NOT_FOUND");
        (await _f.Get(Ports.Tower, "/api/v1/nada")).ShouldBeProblem(404, "ROUTE_NOT_FOUND");
        (await _f.Get(Ports.Tower, "/api/v1/misiones/M99")).ShouldBeProblem(404, "MISSION_NOT_FOUND");
        Assert.Equal(3, (await _f.Get(Ports.Tower, "/api/v1/misiones?nivel=0")).Json.GetArrayLength());
    }
}
