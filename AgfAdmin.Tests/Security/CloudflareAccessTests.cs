using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AgfAdmin.Security;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgfAdmin.Tests.Security;

// De extra laag voor de demo online: met CF_ACCESS_AUD en CF_ACCESS_TEAM_DOMAIN neemt de API alleen verzoeken aan met een geldig
// Cloudflare Access-jeton (handtekening, doelgroep, uitgever, geldigheid). Zonder die instellingen geen controle (lokaal).
public class CloudflareAccessTests
{
    private const string Team = "agf-demo.cloudflareaccess.com";
    private const string Audience = "aud-0123456789abcdef";
    private static readonly CloudflareAccessOptions Options = new() { Audience = Audience, TeamDomain = Team };
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "sleutel-1" };

    private sealed class FixedKeys(params SecurityKey[][] rounds) : IAccessSigningKeys
    {
        public List<bool> Requests { get; } = new();
        public Task<IReadOnlyCollection<SecurityKey>> GetAsync(bool refresh, CancellationToken cancellationToken)
        {
            Requests.Add(refresh);
            return Task.FromResult<IReadOnlyCollection<SecurityKey>>(rounds[Math.Min(Requests.Count - 1, rounds.Length - 1)]);
        }
    }

    private static string Token(SecurityKey key, string audience = Audience, string issuer = "https://" + Team, DateTime? expires = null, string email = "klant@voorbeeld.nl") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = (expires ?? DateTime.UtcNow.AddMinutes(30)).AddHours(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            Subject = new ClaimsIdentity(new[] { new Claim("email", email) }),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });

    [Fact]
    public async Task Only_a_token_signed_by_Access_for_this_application_and_still_valid_is_accepted()
    {
        var validator = new CloudflareAccessValidator(Options, new FixedKeys(new SecurityKey[] { _key }));
        var ok = await validator.ValidateAsync(Token(_key), default);
        Assert.Equal((true, "klant@voorbeeld.nl"), (ok.Allowed, ok.Email));

        Assert.Equal((false, "missing"), ((await validator.ValidateAsync("", default)).Allowed, (await validator.ValidateAsync(null, default)).Reason));
        Assert.False((await validator.ValidateAsync(Token(_key, audience: "andere-toepassing"), default)).Allowed);
        Assert.False((await validator.ValidateAsync(Token(_key, issuer: "https://ander-team.cloudflareaccess.com"), default)).Allowed);
        Assert.False((await validator.ValidateAsync(Token(_key, expires: DateTime.UtcNow.AddMinutes(-5)), default)).Allowed);
        Assert.False((await validator.ValidateAsync("geen.geldig.jeton", default)).Allowed);
        var stranger = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "sleutel-1" };
        Assert.False((await validator.ValidateAsync(Token(stranger), default)).Allowed);
    }

    [Fact]
    public async Task After_Cloudflare_rotates_its_keys_the_new_key_is_fetched_once()
    {
        var newKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "sleutel-2" };
        var keys = new FixedKeys(new SecurityKey[] { _key }, new SecurityKey[] { _key, newKey });
        var check = await new CloudflareAccessValidator(Options, keys).ValidateAsync(Token(newKey), default);
        Assert.True(check.Allowed);
        Assert.Equal(new[] { false, true }, keys.Requests);
    }

    private static DefaultHttpContext Request(string path = "/api/me", string token = null, IPAddress from = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = from ?? IPAddress.Parse("172.18.0.4");
        context.Response.Body = new MemoryStream();
        if (token != null) context.Request.Headers[CloudflareAccessMiddleware.HeaderName] = token;
        return context;
    }

    private async Task<(bool Passed, DefaultHttpContext Context)> RunAsync(CloudflareAccessOptions options, DefaultHttpContext context)
    {
        var passed = false;
        var middleware = new CloudflareAccessMiddleware(_ => { passed = true; return Task.CompletedTask; }, options,
            new CloudflareAccessValidator(options, new FixedKeys(new SecurityKey[] { _key })), NullLogger<CloudflareAccessMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return (passed, context);
    }

    [Fact]
    public async Task The_middleware_refuses_a_request_without_a_valid_token_and_passes_the_email_on_for_display()
    {
        var (refused, context) = await RunAsync(Options, Request());
        Assert.False(refused);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains("Cloudflare Access", JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("message").GetString());

        var (passed, valid) = await RunAsync(Options, Request(token: Token(_key)));
        Assert.True(passed);
        Assert.Equal("klant@voorbeeld.nl", valid.Items[CloudflareAccessMiddleware.EmailItem]);
        Assert.False((await RunAsync(Options, Request(token: Token(_key, audience: "ander")))).Passed);
    }

    [Fact]
    public async Task Health_checks_and_requests_from_inside_the_API_container_need_no_token_and_without_settings_there_is_no_check()
    {
        Assert.True((await RunAsync(Options, Request("/health"))).Passed);
        Assert.True((await RunAsync(Options, Request(from: IPAddress.Loopback))).Passed);
        Assert.True((await RunAsync(Options, Request(from: IPAddress.Parse("::ffff:127.0.0.1")))).Passed);
        Assert.True((await RunAsync(Options, Request(from: IPAddress.IPv6Loopback))).Passed);
        Assert.False((await RunAsync(Options, Request("/healthz"))).Passed);

        var off = await RunAsync(new CloudflareAccessOptions(), Request());
        Assert.True(off.Passed);
        Assert.False(off.Context.Items.ContainsKey(CloudflareAccessMiddleware.EmailItem));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void Both_settings_or_none_and_the_team_domain_may_be_written_as_an_address()
    {
        Assert.False(CloudflareAccessOptions.From(Config()).Enabled);
        var options = CloudflareAccessOptions.From(Config((CloudflareAccessOptions.AudienceKey, Audience), (CloudflareAccessOptions.TeamDomainKey, "https://" + Team + "/")));
        Assert.Equal((true, "https://" + Team, $"https://{Team}/cdn-cgi/access/certs"), (options.Enabled, options.Issuer, options.CertsUrl));
        Assert.Throws<InvalidOperationException>(() => CloudflareAccessOptions.From(Config((CloudflareAccessOptions.AudienceKey, Audience))));
        Assert.Throws<InvalidOperationException>(() => CloudflareAccessOptions.From(Config((CloudflareAccessOptions.TeamDomainKey, Team))));
        Assert.Throws<InvalidOperationException>(() => CloudflareAccessOptions.From(Config((CloudflareAccessOptions.AudienceKey, Audience), (CloudflareAccessOptions.TeamDomainKey, Team + "/pad"))));
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private string Jwks()
    {
        var parameters = _key.Rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = _key.KeyId, alg = "RS256", use = "sig",
            n = Base64UrlEncoder.Encode(parameters.Modulus), e = Base64UrlEncoder.Encode(parameters.Exponent) } } });
    }

    [Fact]
    public async Task The_public_keys_are_fetched_from_the_team_kept_for_an_hour_and_kept_when_Cloudflare_is_unreachable()
    {
        var reachable = true;
        var handler = new ScriptedHandler(_ => reachable
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Jwks()) }
            : new HttpResponseMessage(HttpStatusCode.BadGateway));
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var keys = new CloudflareAccessSigningKeys(new Factory(handler), Options, clock, NullLogger<CloudflareAccessSigningKeys>.Instance);

        var first = await keys.GetAsync(false, default);
        Assert.Equal("sleutel-1", Assert.Single(first).KeyId);
        Assert.Equal($"https://{Team}/cdn-cgi/access/certs", Assert.Single(handler.Urls));
        await keys.GetAsync(false, default);
        await keys.GetAsync(true, default);
        Assert.Single(handler.Urls);

        reachable = false;
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal("sleutel-1", Assert.Single(await keys.GetAsync(false, default)).KeyId);
        Assert.Equal(2, handler.Urls.Count);
        Assert.True((await new CloudflareAccessValidator(Options, keys).ValidateAsync(Token(_key), default)).Allowed);
    }
}
