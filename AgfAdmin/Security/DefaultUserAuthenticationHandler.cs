using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgfAdmin.Security
{
    // Voorlopige aanmelding: elk verzoek is van de ingestelde standaardgebruiker (DefaultUserOptions). Dit is de enige plek die
    // bepaalt wie de gebruiker is; een echte aanmelding (cookie of token) komt hier in de plaats, met dezelfde claims.
    public sealed class DefaultUserAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Scheme = "AgfDefaultUser";

        private readonly IOptionsMonitor<DefaultUserOptions> _user;

        public DefaultUserAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
            IOptionsMonitor<DefaultUserOptions> user)
            : base(options, logger, encoder)
        {
            _user = user;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = CurrentUser.FromOptions(_user.CurrentValue);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId),
                new(ClaimTypes.Name, user.DisplayName),
            };
            claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(user.Permissions.Select(permission => new Claim(Permissions.ClaimType, permission)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
