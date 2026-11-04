using Microsoft.AspNetCore.Authorization;
using AgfAdmin.Security;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Wie is er ingelogd: de schermen tonen "Ingelogd als …" en vragen nergens meer een medewerker-naam.
    [Authorize]
    [ApiController]
    [Route("api/me")]
    public class MeController : ControllerBase
    {
        private readonly ICurrentUser _user;

        public MeController(ICurrentUser user)
        {
            _user = user;
        }

        [HttpGet]
        public IActionResult Get() => Ok(new CurrentUserDto
        {
            UserId = _user.UserId,
            DisplayName = _user.DisplayName,
            Roles = _user.Roles.ToArray(),
            Permissions = _user.Permissions.ToArray(),
        });
    }

    public sealed class CurrentUserDto
    {
        public string UserId { get; set; }
        public string DisplayName { get; set; }
        public string[] Roles { get; set; }
        public string[] Permissions { get; set; }
    }
}
