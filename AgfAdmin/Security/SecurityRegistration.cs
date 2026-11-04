using Microsoft.AspNetCore.Authorization;

namespace AgfAdmin.Security
{
    public static class SecurityRegistration
    {
        // Aanmelding (voorlopig de standaardgebruiker), de huidige gebruiker voor de services en één beleid per gebied.
        public static IServiceCollection AddAgfSecurity(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DefaultUserOptions>(configuration.GetSection(DefaultUserOptions.Section));
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpCurrentUser>();
            services.AddAuthentication(DefaultUserAuthenticationHandler.Scheme)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DefaultUserAuthenticationHandler>(DefaultUserAuthenticationHandler.Scheme, null);
            services.AddSingleton<IAuthorizationHandler, ReadsForAllHandler>();
            services.AddAuthorization(options =>
            {
                foreach (var area in Permissions.All)
                {
                    options.AddPolicy(area, policy => policy.RequireAuthenticatedUser().RequireClaim(Permissions.ClaimType, area));
                }
                options.AddPolicy(Permissions.MasterDataWrites, policy => policy.RequireAuthenticatedUser().AddRequirements(new ReadsForAllRequirement(Permissions.MasterData)));
                options.AddPolicy(Permissions.AdminWrites, policy => policy.RequireAuthenticatedUser().AddRequirements(new ReadsForAllRequirement(Permissions.Admin)));
                // Een endpoint zonder eigen beleid vraagt in elk geval een aangemelde gebruiker.
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            });
            return services;
        }
    }

    // Lezen (GET/HEAD) mag elke aangemelde gebruiker; schrijven vraagt het gebied.
    public sealed class ReadsForAllRequirement : IAuthorizationRequirement
    {
        public ReadsForAllRequirement(string permission)
        {
            Permission = permission;
        }

        public string Permission { get; }
    }

    public sealed class ReadsForAllHandler : AuthorizationHandler<ReadsForAllRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ReadsForAllRequirement requirement)
        {
            var method = (context.Resource as HttpContext)?.Request.Method;
            if (HttpMethods.IsGet(method ?? "") || HttpMethods.IsHead(method ?? "") || context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
