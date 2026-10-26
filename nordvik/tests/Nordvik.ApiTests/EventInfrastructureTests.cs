using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>Un receptor de webhooks de usar y tirar (como haría un mock de Karate).</summary>
public sealed class WebhookSink : IAsyncDisposable
{
    private readonly WebApplication _app;
    public ConcurrentQueue<(string Body, Dictionary<string, string> Headers)> Received { get; } = new();
    public int FailFirst { get; set; }
    private int _calls;
    public int Port { get; }
    public string Url => $"http://localhost:{Port}/hooks";

    private WebhookSink(WebApplication app, int port) { _app = app; Port = port; }

    public static async Task<WebhookSink> Start(int failFirst = 0)
    {
        var port = Random.Shared.Next(21000, 29000);
        var b = WebApplication.CreateBuilder();
        b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(k => k.ListenLocalhost(port));
        var app = b.Build();
        var sink = new WebhookSink(app, port) { FailFirst = failFirst };
        app.MapPost("/hooks", async (HttpContext http) =>
        {
            var body = await new StreamReader(http.Request.Body).ReadToEndAsync();
            var n = Interlocked.Increment(ref sink._calls);
            if (n <= sink.FailFirst) return Results.StatusCode(503);
            sink.Received.Enqueue((body, http.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
            return Results.Ok();
        });
        await app.StartAsync();
        return sink;
    }

    public int Calls => _calls;
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public static class Eventually
{
    /// <summary>Repite hasta que se cumple (consistencia eventual), como retry until de Karate.</summary>
    public static async Task<T> Until<T>(Func<Task<T>> get, Func<T, bool> ok, int timeoutMs = 8000, string? what = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        T last = await get();
        while (!ok(last))
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Xunit.Sdk.XunitException($"No se cumplió a tiempo: {what}. Último valor: {last}");
            await Task.Delay(150);
            last = await get();
        }
        return last;
    }
}

[Collection("nordvik")]
public class EventBusTests
{
    private readonly NordvikFixture _f;
    public EventBusTests(NordvikFixture f) => _f = f;

    private static string Ev(string topic, string key, string? id = null, string payload = "{\"x\":1}") =>
        $"{{{(id is null ? "" : $"\"eventId\":\"{id}\",")}\"topic\":\"{topic}\",\"key\":\"{key}\",\"payload\":{payload}}}";

    [Fact]
    public async Task Publishing_twice_the_same_event_id_is_ignored()
    {
        var id = "evt_test_" + Guid.NewGuid().ToString("N");
        var a = await _f.Post(Ports.EventBus, "/api/v1/events", Ev("test.thing.happened", "k1", id));
        Assert.Equal(202, a.Code);
        Assert.Equal(1, a.Int("accepted"));
        var b = await _f.Post(Ports.EventBus, "/api/v1/events", Ev("test.thing.happened", "k1", id));
        Assert.Equal(0, b.Int("accepted"));
        Assert.Equal(1, b.Int("duplicates"));
        var one = await _f.Get(Ports.EventBus, $"/api/v1/events/{id}");
        Assert.Equal("k1", one.Str("key"));
        Assert.Equal("manual", one.Str("producer"));
        Assert.StartsWith("corr-", one.Str("correlationId"));
    }

    [Fact]
    public async Task Invalid_events_are_explained()
    {
        var r = await _f.Post(Ports.EventBus, "/api/v1/events", "[{\"topic\":\"Order Created\",\"payload\":[]},{\"topic\":\"ok.topic\",\"key\":\"1\",\"payload\":{}}]");
        r.ShouldBeProblem(400, "VALIDATION_FAILED");
        var fields = r.Json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Equal(new[] { "[0].topic", "[0].key", "[0].payload" }, fields);
        (await _f.Get(Ports.EventBus, "/api/v1/events/evt_no_existe")).ShouldBeProblem(404, "EVENT_NOT_FOUND");
    }

    [Fact]
    public async Task Cursor_reading_with_topic_filters_and_wildcards()
    {
        var run = Guid.NewGuid().ToString("N")[..6];
        var start = (await _f.Post(Ports.EventBus, "/api/v1/events", Ev($"cursor.r{run}.a", "1"))).Json.GetProperty("lastSeq").GetInt64() - 1;
        await _f.Post(Ports.EventBus, "/api/v1/events", $"[{Ev($"cursor.r{run}.b", "2")},{Ev($"other.r{run}.c", "3")},{Ev($"cursor.r{run}.a", "4")}]");
        var exact = await _f.Get(Ports.EventBus, $"/api/v1/events?topics=cursor.r{run}.a&after={start}");
        Assert.Equal(new[] { "1", "4" }, exact.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        var prefix = await _f.Get(Ports.EventBus, $"/api/v1/events?topics=cursor.r{run}.*&after={start}");
        Assert.Equal(3, prefix.Json.GetProperty("items").GetArrayLength());
        var page = await _f.Get(Ports.EventBus, $"/api/v1/events?topics=cursor.r{run}.*,other.r{run}.c&after={start}&limit=2");
        Assert.True(page.Json.GetProperty("hasMore").GetBoolean());
        var next = await _f.Get(Ports.EventBus, $"/api/v1/events?topics=cursor.r{run}.*,other.r{run}.c&after={page.Json.GetProperty("lastSeq").GetInt64()}");
        Assert.Equal(2, next.Json.GetProperty("items").GetArrayLength());
        var seqs = next.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("seq").GetInt64()).ToList();
        Assert.Equal(seqs.OrderBy(x => x), seqs);
        var topics = await _f.Get(Ports.EventBus, "/api/v1/topics");
        Assert.Contains(topics.Json.EnumerateArray(), t => t.GetProperty("topic").GetString() == $"cursor.r{run}.a" && t.GetProperty("events").GetInt64() == 2);
    }

    [Fact]
    public async Task Webhook_subscription_receives_signed_events_after_retries()
    {
        await using var sink = await WebhookSink.Start(failFirst: 2);
        var run = Guid.NewGuid().ToString("N")[..6];
        var sub = await _f.Post(Ports.EventBus, "/api/v1/subscriptions", $"{{\"name\":\"t-{run}\",\"topic\":\"hook.r{run}.*\",\"callbackUrl\":\"{sink.Url}\",\"secret\":\"s3cr3t\"}}");
        Assert.Equal(201, sub.Code);
        await _f.Post(Ports.EventBus, "/api/v1/events", Ev($"hook.r{run}.done", "A", payload: "{\"amount\":\"10.00\"}"));
        await _f.Post(Ports.EventBus, "/api/v1/events", Ev($"nothook.r{run}.done", "B"));
        await Eventually.Until(() => Task.FromResult(sink.Received.Count), n => n >= 1, 10000, "entrega del webhook");
        var (body, headers) = sink.Received.First();
        Assert.Equal(3, sink.Calls); // dos 503 y luego 200
        Assert.Equal("sha256=" + Nordvik.Shared.Canonical.HmacHex("s3cr3t", body), headers["X-Nordvik-Signature"]);
        Assert.Equal($"hook.r{run}.done", JsonDocument.Parse(body).RootElement.GetProperty("topic").GetString());
        var deliveries = await _f.Get(Ports.EventBus, $"/api/v1/subscriptions/{sub.Str("id")}/deliveries");
        Assert.Equal(new[] { "RETRY", "RETRY", "DELIVERED" }, deliveries.Json.EnumerateArray().Select(d => d.GetProperty("outcome").GetString()));
        Assert.Equal(new long[] { 1, 2, 3 }, deliveries.Json.EnumerateArray().Select(d => d.GetProperty("attempt").GetInt64()));
        await Task.Delay(500);
        Assert.Single(sink.Received);
    }

    [Fact]
    public async Task Exhausted_retries_end_in_dead_letters_and_can_be_replayed()
    {
        var admin = await _f.Admin();
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/event-bus/retryBackoffSeconds", "{\"value\":\"0\"}", NordvikFixture.Bearer(admin));
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/event-bus/maxAttempts", "{\"value\":\"3\"}", NordvikFixture.Bearer(admin));
        try
        {
            await Task.Delay(2300); // event-bus relee la configuración cada 2 s
            await using var sink = await WebhookSink.Start(failFirst: 3);
            var run = Guid.NewGuid().ToString("N")[..6];
            var sub = await _f.Post(Ports.EventBus, "/api/v1/subscriptions", $"{{\"name\":\"dl-{run}\",\"topic\":\"dead.r{run}.x\",\"callbackUrl\":\"{sink.Url}\"}}");
            var ev = "evt_dl_" + run;
            await _f.Post(Ports.EventBus, "/api/v1/events", Ev($"dead.r{run}.x", "Z", ev));
            var dl = await Eventually.Until(() => _f.Get(Ports.EventBus, "/api/v1/dead-letters"),
                r => r.Json.EnumerateArray().Any(d => d.GetProperty("eventId").GetString() == ev), 10000, "mensaje muerto");
            var item = dl.Json.EnumerateArray().First(d => d.GetProperty("eventId").GetString() == ev);
            Assert.Equal(3, item.GetProperty("attempts").GetInt64());
            Assert.Equal("HTTP 503", item.GetProperty("lastError").GetString());
            var replay = await _f.Post(Ports.EventBus, $"/api/v1/dead-letters/{item.GetProperty("id").GetInt64()}/replay");
            Assert.True(replay.Json.GetProperty("delivered").GetBoolean());
            Assert.Single(sink.Received);
            (await _f.Post(Ports.EventBus, $"/api/v1/dead-letters/{item.GetProperty("id").GetInt64()}/replay")).ShouldBeProblem(409, "ALREADY_REPLAYED");
            Assert.False((await _f.Delete(Ports.EventBus, $"/api/v1/subscriptions/{sub.Str("id")}")).Json.GetProperty("active").GetBoolean());
        }
        finally
        {
            await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/event-bus/retryBackoffSeconds", "{\"value\":\"1,2,4,8\"}", NordvikFixture.Bearer(admin));
            await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/event-bus/maxAttempts", "{\"value\":\"5\"}", NordvikFixture.Bearer(admin));
        }
    }

    [Fact]
    public async Task Subscription_validation() =>
        (await _f.Post(Ports.EventBus, "/api/v1/subscriptions", "{\"topic\":\"\",\"callbackUrl\":\"ftp://x\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");

    [Fact]
    public async Task Relay_reports_its_producers_and_can_pause()
    {
        var s = await _f.Get(Ports.Relay, "/api/v1/relay/status");
        Assert.False(s.Json.GetProperty("paused").GetBoolean());
        var paused = await _f.Post(Ports.Relay, "/api/v1/relay/pause");
        Assert.True(paused.Json.GetProperty("paused").GetBoolean());
        var resumed = await _f.Post(Ports.Relay, "/api/v1/relay/resume");
        Assert.False(resumed.Json.GetProperty("paused").GetBoolean());
    }
}

[Collection("nordvik")]
public class AuditAndIdempotencyTests
{
    private readonly NordvikFixture _f;
    public AuditAndIdempotencyTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Audit_chain_detects_tampering()
    {
        var r = await _f.Post(Ports.Audit, "/api/v1/audit", "{\"actor\":\"asesor.erik\",\"action\":\"customer.read\",\"resource\":\"customer/abc\",\"details\":{\"why\":\"llamada\"}}");
        Assert.Equal(201, r.Code);
        Assert.Matches("^[0-9a-f]{64}$", r.Str("hash"));
        Assert.Equal("SUCCESS", r.Str("outcome"));
        Assert.True((await _f.Get(Ports.Audit, "/api/v1/audit/verify")).Json.GetProperty("valid").GetBoolean());
        var seq = r.Json.GetProperty("seq").GetInt64();
        await _f.Post(Ports.Audit, $"/_test/audit/{seq}/tamper");
        var broken = await _f.Get(Ports.Audit, "/api/v1/audit/verify");
        Assert.False(broken.Json.GetProperty("valid").GetBoolean());
        Assert.Equal(seq, broken.Json.GetProperty("brokenAtSeq").GetInt64());
        await _f.Post(Ports.Audit, "/_test/audit/repair");
        Assert.True((await _f.Get(Ports.Audit, "/api/v1/audit/verify")).Json.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Each_record_links_to_the_previous_hash()
    {
        var a = await _f.Post(Ports.Audit, "/api/v1/audit", "{\"actor\":\"a\",\"action\":\"x.y\",\"resource\":\"r/1\"}");
        var b = await _f.Post(Ports.Audit, "/api/v1/audit", "{\"actor\":\"a\",\"action\":\"x.y\",\"resource\":\"r/2\"}");
        // Puede haber registros de eventos entre medias: se busca el anterior de b
        var page = await _f.Get(Ports.Audit, "/api/v1/audit?limit=1000");
        var all = page.Json.GetProperty("items").EnumerateArray().ToList();
        var bSeq = b.Json.GetProperty("seq").GetInt64();
        var prev = all.First(x => x.GetProperty("seq").GetInt64() == bSeq - 1);
        Assert.Equal(prev.GetProperty("hash").GetString(), b.Str("prevHash"));
        Assert.True(bSeq > a.Json.GetProperty("seq").GetInt64());
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Audit_records_are_immutable(string method)
    {
        var r = await _f.Post(Ports.Audit, "/api/v1/audit", "{\"actor\":\"a\",\"action\":\"x.y\",\"resource\":\"r/1\"}");
        (await _f.Send(new HttpMethod(method), Ports.Audit, $"/api/v1/audit/{r.Str("id")}", "{}")).ShouldBeProblem(405, "AUDIT_IS_IMMUTABLE");
    }

    [Fact]
    public async Task Audit_validation() =>
        (await _f.Post(Ports.Audit, "/api/v1/audit", "{\"outcome\":\"MAYBE\"}")).ShouldBeProblem(400, "VALIDATION_FAILED");

    [Fact]
    public async Task Audit_copies_every_bus_event()
    {
        var id = "evt_aud_" + Guid.NewGuid().ToString("N");
        await _f.Post(Ports.EventBus, "/api/v1/events", $"{{\"eventId\":\"{id}\",\"topic\":\"audit.copy.test\",\"key\":\"K9\",\"producer\":\"tests\",\"payload\":{{\"secret\":\"no-se-copia\"}}}}");
        var page = await Eventually.Until(() => _f.Get(Ports.Audit, "/api/v1/audit?source=event:" + id), r => r.Int("total") == 1, 8000, "copia del evento");
        var rec = page.Json.GetProperty("items")[0];
        Assert.Equal("audit/K9", rec.GetProperty("resource").GetString());
        Assert.Equal("tests", rec.GetProperty("actor").GetString());
        Assert.DoesNotContain("no-se-copia", page.Body);
    }

    [Fact]
    public async Task Idempotency_store_claims_replays_and_rejects_other_bodies()
    {
        var key = "k-" + Guid.NewGuid().ToString("N");
        var claim = await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/orders/{key}", "{\"requestHash\":\"aaa\"}");
        Assert.Equal(201, claim.Code);
        Assert.Equal("IN_PROGRESS", claim.Str("state"));
        Assert.Equal("IN_PROGRESS", (await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/orders/{key}", "{\"requestHash\":\"aaa\"}")).Str("state"));
        (await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/orders/{key}", "{\"requestHash\":\"bbb\"}")).ShouldBeProblem(422, "REQUEST_HASH_MISMATCH");
        await _f.Post(Ports.Idempotency, $"/api/v1/keys/orders/{key}/response", "{\"status\":201,\"body\":{\"id\":\"o1\"},\"location\":\"/api/v1/orders/o1\"}");
        var again = await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/orders/{key}", "{\"requestHash\":\"aaa\"}");
        Assert.Equal(200, again.Code);
        Assert.Equal("COMPLETED", again.Str("state"));
        Assert.Equal("o1", again.Json.GetProperty("body").GetProperty("id").GetString());
        // El mismo nombre de clave en otro ámbito es otra clave
        Assert.Equal(201, (await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/payments/{key}", "{\"requestHash\":\"zzz\"}")).Code);
        Assert.Equal(204, (await _f.Delete(Ports.Idempotency, $"/api/v1/keys/payments/{key}")).Code);
        (await _f.Get(Ports.Idempotency, $"/api/v1/keys/payments/{key}")).ShouldBeProblem(404, "KEY_NOT_FOUND");
    }

    [Fact]
    public async Task Idempotency_keys_expire_after_24_virtual_hours()
    {
        var key = "exp-" + Guid.NewGuid().ToString("N");
        await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/orders/{key}", "{\"requestHash\":\"aaa\"}");
        try
        {
            await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":86401}");
            (await _f.Get(Ports.Idempotency, $"/api/v1/keys/orders/{key}")).ShouldBeProblem(404, "KEY_NOT_FOUND");
            Assert.Equal(201, (await _f.Send(HttpMethod.Put, Ports.Idempotency, $"/api/v1/keys/orders/{key}", "{\"requestHash\":\"bbb\"}")).Code);
        }
        finally { await _f.ResetClock(); }
    }
}

[Collection("nordvik")]
public class HashAndTraceTests
{
    private readonly NordvikFixture _f;
    public HashAndTraceTests(NordvikFixture f) => _f = f;

    [Fact]
    public async Task Canonical_json_sorts_keys_removes_spaces_and_trailing_zeros()
    {
        var r = await _f.Post(Ports.Hash, "/api/v1/hash/canonical", "{\"json\":{ \"b\": 1.50, \"a\": [3, 1, {\"z\": true, \"y\": null}], \"Ä\": \"Göteborg\" }}");
        Assert.Equal("{\"a\":[3,1,{\"y\":null,\"z\":true}],\"b\":1.5,\"Ä\":\"Göteborg\"}", r.Str("canonical"));
        Assert.Equal(Nordvik.Shared.Canonical.Sha256Hex(r.Str("canonical")), r.Str("sha256"));
        var same = await _f.Post(Ports.Hash, "/api/v1/hash/canonical", "{\"json\":{\"Ä\":\"Göteborg\",\"a\":[3,1,{\"y\":null,\"z\":true}],\"b\":1.5}}");
        Assert.Equal(r.Str("sha256"), same.Str("sha256"));
    }

    [Fact]
    public async Task Known_vectors_for_sha256_and_hmac()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", (await _f.Post(Ports.Hash, "/api/v1/hash/sha256", "{\"text\":\"abc\"}")).Str("hash"));
        var h = await _f.Post(Ports.Hash, "/api/v1/hmac", "{\"key\":\"Jefe\",\"message\":\"what do ya want for nothing?\"}");
        Assert.Equal("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", h.Str("signature"));
        var ok = await _f.Post(Ports.Hash, "/api/v1/hmac/verify", "{\"key\":\"Jefe\",\"message\":\"what do ya want for nothing?\",\"signature\":\"sha256=5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843\"}");
        Assert.True(ok.Json.GetProperty("valid").GetBoolean());
        var bad = await _f.Post(Ports.Hash, "/api/v1/hmac/verify", "{\"key\":\"Jefe\",\"message\":\"what do ya want for nothing? \",\"signature\":\"5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843\"}");
        Assert.False(bad.Json.GetProperty("valid").GetBoolean());
        (await _f.Post(Ports.Hash, "/api/v1/hash/canonical", "{}")).ShouldBeProblem(400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Tracer_rebuilds_the_call_chain_of_a_fixture()
    {
        var corr = "corr-trace-" + Guid.NewGuid().ToString("N")[..10];
        var prefix = "RUN-TR-" + Guid.NewGuid().ToString("N")[..6];
        var r = await _f.Send(HttpMethod.Post, Ports.Fixture, "/api/v1/fixtures/cliente-basico", $"{{\"externalRef\":\"{prefix}-1\"}}",
            req => req.Headers.TryAddWithoutValidation("X-Correlation-Id", corr));
        Assert.Equal(201, r.Code);
        Assert.Equal(corr, r.Headers.GetValues("X-Correlation-Id").Single());
        var t = await _f.Get(Ports.Tracer, $"/api/v1/traces/{corr}");
        Assert.Equal(200, t.Code);
        var services = t.Json.GetProperty("services").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("fixture-loader", services);
        Assert.Contains("customer-registry", services);
        Assert.Contains("synthetic-person-service", services);
        var chain = t.Json.GetProperty("chain").EnumerateArray().ToList();
        Assert.Equal("fixture-loader", chain[0].GetProperty("service").GetString());
        Assert.Equal(0, chain[0].GetProperty("depth").GetInt32());
        Assert.All(chain.Skip(1), h => Assert.True(h.GetProperty("depth").GetInt32() >= 1));
        Assert.Contains("└─", t.Str("tree"));
        await _f.Delete(Ports.Reset, $"/api/v1/runs/{prefix}");
        (await _f.Get(Ports.Tracer, "/api/v1/traces/corr-no-existe-nunca")).ShouldBeProblem(404, "TRACE_NOT_FOUND");
    }
}
