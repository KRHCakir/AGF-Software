using System.Collections.Generic;
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
                ServiceResultStatus.Conflict => controller.Conflict(result.ConflictBody ?? new { message = result.Message }),
                ServiceResultStatus.Unavailable => controller.StatusCode(StatusCodes.Status503ServiceUnavailable, result.ConflictBody ?? new { message = result.Message }),
                _ => throw new InvalidOperationException($"Unhandled service result status {result.Status}.")
            };

        // Sorteren op de server (feedback 22 november): `sort` moet een veld uit de toegestane lijst van die lijst zijn en `direction` asc
        // of desc; anders 400 met de toegestane velden. Leeg = de standaardvolgorde (request null).
        public static bool TryReadSort<T>(this ControllerBase controller, string sort, string direction, IReadOnlyDictionary<string, SortField<T>> fields,
            out SortRequest request, out IActionResult rejected)
        {
            var errors = new Dictionary<string, string[]>();
            request = ListSort.Parse(sort, direction, fields.Keys, errors);
            rejected = errors.Count == 0 ? null : controller.BadRequest(new ValidationProblemDetails(errors));
            return rejected == null;
        }
    }
}
