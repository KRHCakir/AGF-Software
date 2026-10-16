using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Gedeelde vertaling van een ServiceResult naar een HTTP-antwoord voor de dunne domeincontrollers.
    internal static class ServiceResultMapping
    {
        public static IActionResult ToActionResult<T>(this ControllerBase controller, ServiceResult<T> result, Func<T, IActionResult> onCreated = null) =>
            result.Status switch
            {
                ServiceResultStatus.Created => onCreated != null ? onCreated(result.Value) : controller.StatusCode(StatusCodes.Status201Created, result.Value),
                ServiceResultStatus.Ok => controller.Ok(result.Value),
                ServiceResultStatus.Validation => controller.BadRequest(new ValidationProblemDetails(
                    result.Errors.ToDictionary(e => e.Key, e => e.Value))),
                ServiceResultStatus.NotFound => controller.NotFound(new { message = result.Message }),
                ServiceResultStatus.Conflict => controller.Conflict(new { message = result.Message }),
                _ => throw new InvalidOperationException($"Unhandled service result status {result.Status}.")
            };
    }
}
