using System.Security.Claims;

namespace AgfAdmin.Security
{
    // De gebruiker van het lopende verzoek, gelezen uit de claims die de aanmelding zette (nu: DefaultUserAuthenticationHandler).
    // Een echte aanmelding vervangt alleen die handler; deze klasse en alle services blijven gelijk.
    public sealed class HttpCurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpCurrentUser(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        private ClaimsPrincipal Principal => _accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new InvalidOperationException("Er is geen aangemelde gebruiker voor dit verzoek.");

        public string UserId => Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        public string DisplayName => Principal.FindFirstValue(ClaimTypes.Name);
        public IReadOnlyList<string> Roles => Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        public IReadOnlyList<string> Permissions => Principal.FindAll(Security.Permissions.ClaimType).Select(c => c.Value).ToArray();
        public bool Has(string permission) => Principal.HasClaim(Security.Permissions.ClaimType, permission);
    }
}
