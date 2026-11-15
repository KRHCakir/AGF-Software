using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgfAdmin.Security
{
    // Extra laag voor de demo online (docs/demo-online.md). De toegangspoort is Cloudflare Access: de klant krijgt een code per mail en
    // Cloudflare zet bij elk verzoek een ondertekend jeton in de kop Cf-Access-Jwt-Assertion. Zijn CF_ACCESS_AUD en CF_ACCESS_TEAM_DOMAIN
    // ingesteld, dan neemt de API alleen verzoeken aan met een geldig jeton (handtekening, doelgroep, uitgever, geldigheid). Zonder die
    // instellingen (lokaal) geldt deze controle niet. Dit is geen login: de gebruiker van de app blijft de standaardgebruiker.
    public sealed class CloudflareAccessOptions
    {
        public const string AudienceKey = "CF_ACCESS_AUD";
        public const string TeamDomainKey = "CF_ACCESS_TEAM_DOMAIN";

        public string Audience { get; init; }
        // Bijvoorbeeld jouwteam.cloudflareaccess.com (met of zonder https://).
        public string TeamDomain { get; init; }

        public bool Enabled => Audience != null;
        public string Issuer => $"https://{TeamDomain}";
        public string CertsUrl => $"{Issuer}/cdn-cgi/access/certs";

        // Eén van de twee ingesteld is een vergissing: dan niet stil zonder controle starten.
        public static CloudflareAccessOptions From(IConfiguration configuration)
        {
            var audience = configuration[AudienceKey]?.Trim();
            var team = configuration[TeamDomainKey]?.Trim();
            if (team != null && team.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) team = team["https://".Length..];
            team = team?.TrimEnd('/');
            if (string.IsNullOrEmpty(audience) && string.IsNullOrEmpty(team)) return new CloudflareAccessOptions();
            if (string.IsNullOrEmpty(audience) || string.IsNullOrEmpty(team) || team.Contains('/'))
            {
                throw new InvalidOperationException($"Cloudflare Access: zet {AudienceKey} én {TeamDomainKey} (zoals jouwteam.cloudflareaccess.com), of geen van beide.");
            }
            return new CloudflareAccessOptions { Audience = audience, TeamDomain = team };
        }
    }

    public interface IAccessSigningKeys
    {
        // refresh: opnieuw ophalen (na een onbekende sleutel), maar niet vaker dan eens per paar minuten.
        Task<IReadOnlyCollection<SecurityKey>> GetAsync(bool refresh, CancellationToken cancellationToken);
    }

    // De publieke sleutels van het Access-team, een uur bewaard. Lukt ophalen niet, dan blijven de vorige gelden; zonder sleutels
    // wordt elk verzoek geweigerd (de controle valt nooit open).
    public sealed class CloudflareAccessSigningKeys(IHttpClientFactory http, CloudflareAccessOptions options, TimeProvider clock, ILogger<CloudflareAccessSigningKeys> logger) : IAccessSigningKeys
    {
        public const string HttpClientName = "cloudflare-access";
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinRefresh = TimeSpan.FromMinutes(5);
        private readonly SemaphoreSlim _lock = new(1, 1);
        private IReadOnlyCollection<SecurityKey> _keys = Array.Empty<SecurityKey>();
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
        private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
        private DateTimeOffset _attemptedAt = DateTimeOffset.MinValue;
        private bool _lastFailed;

        public async Task<IReadOnlyCollection<SecurityKey>> GetAsync(bool refresh, CancellationToken cancellationToken)
        {
            if (Fresh(refresh)) return _keys;
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (Fresh(refresh)) return _keys;
                _lastFailed = true;
                try
                {
                    var json = await http.CreateClient(HttpClientName).GetStringAsync(options.CertsUrl, cancellationToken);
                    var fetched = new JsonWebKeySet(json).GetSigningKeys().ToArray();
                    if (fetched.Length == 0)
                    {
                        logger.LogWarning("Cloudflare Access: het antwoord bevat geen ondertekeningssleutels; de vorige blijven gelden.");
                    }
                    else
                    {
                        _keys = fetched;
                        _fetchedAt = clock.GetUtcNow();
                        _lastFailed = false;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ArgumentException or System.Text.Json.JsonException)
                {
                    logger.LogWarning("Cloudflare Access: sleutels ophalen lukte niet ({Error}); de vorige blijven gelden.", exception.GetType().Name);
                }
                // Ook na een mislukte poging even wachten, zodat een storing bij Cloudflare niet elk verzoek laat ophalen.
                _attemptedAt = clock.GetUtcNow();
                return _keys;
            }
            finally
            {
                _lock.Release();
            }
        }

        // Na een mislukte poging korter wachten (RetryAfterFailure), zodat herstel snel wordt opgepikt; _fetchedAt is alleen de laatste geslaagde ophaling.
        private bool Fresh(bool refresh)
        {
            var now = clock.GetUtcNow();
            var sinceAttempt = now - _attemptedAt;
            if (_keys.Count == 0) return sinceAttempt < RetryAfterFailure;
            if (refresh) return sinceAttempt < (_lastFailed ? RetryAfterFailure : MinRefresh);
            return now - _fetchedAt < CacheFor || sinceAttempt < RetryAfterFailure && _lastFailed;
        }
    }

    public sealed record AccessCheck(bool Allowed, string Email, string Reason);

    public sealed class CloudflareAccessValidator(CloudflareAccessOptions options, IAccessSigningKeys keys)
    {
        private static readonly JsonWebTokenHandler Handler = new();

        public async Task<AccessCheck> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token)) return new AccessCheck(false, null, "missing");
            var result = await ValidateWithAsync(token, await keys.GetAsync(false, cancellationToken));
            // Cloudflare wisselt zijn sleutels af en toe: bij een onbekende sleutel één keer opnieuw ophalen.
            if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException)
            {
                result = await ValidateWithAsync(token, await keys.GetAsync(true, cancellationToken));
            }
            if (!result.IsValid) return new AccessCheck(false, null, result.Exception?.GetType().Name ?? "invalid");
            var email = result.ClaimsIdentity?.FindFirst("email")?.Value;
            return new AccessCheck(true, string.IsNullOrWhiteSpace(email) ? null : email, null);
        }

        private Task<TokenValidationResult> ValidateWithAsync(string token, IReadOnlyCollection<SecurityKey> signingKeys) =>
            Handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKeys = signingKeys,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            });
    }

    public sealed class CloudflareAccessMiddleware(RequestDelegate next, CloudflareAccessOptions options, CloudflareAccessValidator validator, ILogger<CloudflareAccessMiddleware> logger)
    {
        public const string HeaderName = "Cf-Access-Jwt-Assertion";
        // Het e-mailadres uit een geldig jeton, alleen om te tonen (api/me); nooit de gebruiker die iets vastlegt.
        public const string EmailItem = "agf.cloudflare-access.email";

        public async Task InvokeAsync(HttpContext context)
        {
            if (!options.Enabled || Exempt(context))
            {
                await next(context);
                return;
            }
            var check = await validator.ValidateAsync(context.Request.Headers[HeaderName].ToString(), context.RequestAborted);
            if (!check.Allowed)
            {
                logger.LogWarning("Cloudflare Access: verzoek naar {Path} geweigerd ({Reason}).", context.Request.Path.Value, check.Reason);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Geen geldige toegang via Cloudflare Access. Open de demo opnieuw via het adres dat je hebt gekregen." });
                return;
            }
            context.Items[EmailItem] = check.Email;
            await next(context);
        }

        // De gezondheidscontrole van Compose, en verzoeken vanuit de API-container zelf (het vulscript scripts/demo-seed.sh draait daar
        // tegen localhost). Van buiten komt een verzoek alleen via de website binnen, nooit via de loopback van de API-container.
        public static bool Exempt(HttpContext context)
        {
            if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)) return true;
            var ip = context.Connection.RemoteIpAddress;
            if (ip == null) return false;
            return IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
        }
    }
}
