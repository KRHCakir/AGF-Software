using System.Globalization;
using System.Text;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface ICustomerService
    {
        Task<ServiceResult<CustomerDto>> CreateAsync(CreateOperationalCustomerDto request, CancellationToken cancellationToken = default);
    }

    // Klant aanmaken vanuit Verkoop: idempotent op RequestId (identieke herhaling -> 200 met de bestaande klant, afwijkende
    // payload -> 409), lege optionele velden consequent als null, en gelijkende namen alleen na expliciete bevestiging.
    // Namen zijn niet uniek: twee klanten mogen dezelfde naam hebben als de gebruiker dat bevestigt.
    public sealed class CustomerService : ICustomerService
    {
        public const int MaxNameLength = 200;
        public const int MaxContactPersonLength = 100;
        public const int MaxEmailLength = 200;
        public const int MaxPhoneLength = 40;
        public const int MaxShippingAddressLength = 300;
        // De bestaande kantoorflow (POST /api/customers) bewaarde altijd 14 dagen als er niets was opgegeven; dat blijft zo.
        public const int DefaultPaymentTermsDays = 14;
        public const string SimilarNamesCode = "similar-names";

        private readonly AppDbContext _db;

        public CustomerService(AppDbContext db) => _db = db;

        private sealed record Normalized(Guid RequestId, string Name, string ContactPerson, string Email, string Phone, string ShippingAddress, bool AcknowledgeSimilarNames);

        public async Task<ServiceResult<CustomerDto>> CreateAsync(CreateOperationalCustomerDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Normalize(request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<CustomerDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var existing = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.RequestId == command.RequestId, cancellationToken);
            if (existing != null)
            {
                return ResultForRetry(existing, command);
            }

            if (!command.AcknowledgeSimilarNames)
            {
                var similar = await SimilarAsync(command.Name, cancellationToken);
                if (similar.Count > 0)
                {
                    var message = $"A customer with a similar name already exists ({similar.Count}); confirm explicitly to create another one.";
                    return ServiceResult<CustomerDto>.Conflict(message, new { message, code = SimilarNamesCode, similarCustomers = similar });
                }
            }

            var customer = new Customer
            {
                RequestId = command.RequestId,
                Name = command.Name,
                ContactPerson = command.ContactPerson,
                Email = command.Email,
                Phone = command.Phone,
                ShippingAddress = command.ShippingAddress,
                PaymentTermsDays = DefaultPaymentTermsDays,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Customers.Add(customer);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                // Gelijktijdige herhaling met dezelfde requestId: de unieke index heeft de tweede weggeschreven; antwoord als replay.
                _db.ChangeTracker.Clear();
                var concurrent = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.RequestId == command.RequestId, cancellationToken);
                if (concurrent == null)
                {
                    throw;
                }
                return ResultForRetry(concurrent, command);
            }
            return ServiceResult<CustomerDto>.Created(ToDto(customer));
        }

        private static Normalized Normalize(CreateOperationalCustomerDto request, Dictionary<string, List<string>> errors)
        {
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return null;
            }
            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            var name = ValidationErrors.RequiredText(request.Name, MaxNameLength, "name", errors);
            var contact = ValidationErrors.OptionalText(request.ContactPerson, MaxContactPersonLength, "contactPerson", errors);
            var email = ValidationErrors.OptionalText(request.Email, MaxEmailLength, "email", errors);
            if (email != null && !LooksLikeEmail(email))
            {
                ValidationErrors.Add(errors, "email", "This does not look like an e-mail address.");
            }
            var phone = ValidationErrors.OptionalText(request.Phone, MaxPhoneLength, "phone", errors);
            var address = ValidationErrors.OptionalText(request.ShippingAddress, MaxShippingAddressLength, "shippingAddress", errors);
            return errors.Count > 0 ? null : new Normalized(request.RequestId!.Value, name, contact, email, phone, address, request.AcknowledgeSimilarNames);
        }

        // Bewust licht: één @ met tekst ervoor en erna en zonder spaties; geen volledige RFC-controle.
        private static bool LooksLikeEmail(string value)
        {
            var at = value.IndexOf('@');
            return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
        }

        // Replay: dezelfde requestId met exact dezelfde genormaliseerde gegevens geeft de bestaande klant terug; anders 409.
        // De bevestigingsvlag telt niet mee: een bevestigde herhaling van dezelfde klant is dezelfde opdracht.
        private static ServiceResult<CustomerDto> ResultForRetry(Customer existing, Normalized command)
        {
            var same = string.Equals(existing.Name, command.Name, StringComparison.Ordinal)
                && string.Equals(Blank(existing.ContactPerson), command.ContactPerson, StringComparison.Ordinal)
                && string.Equals(Blank(existing.Email), command.Email, StringComparison.Ordinal)
                && string.Equals(Blank(existing.Phone), command.Phone, StringComparison.Ordinal)
                && string.Equals(Blank(existing.ShippingAddress), command.ShippingAddress, StringComparison.Ordinal);
            return same
                ? ServiceResult<CustomerDto>.Ok(ToDto(existing))
                : ServiceResult<CustomerDto>.Conflict($"RequestId {command.RequestId} was already used for a different customer.");
        }

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private async Task<List<SimilarCustomerDto>> SimilarAsync(string name, CancellationToken cancellationToken)
        {
            var target = NormalizeName(name);
            // Klantenbestand is klein; vergelijken gebeurt genormaliseerd in geheugen (de database kent geen genormaliseerde kolom).
            var customers = await _db.Customers.AsNoTracking().Select(c => new { c.Id, c.Name, c.ContactPerson, c.Phone }).ToListAsync(cancellationToken);
            return customers
                .Where(c => IsSimilar(target, NormalizeName(c.Name)))
                .OrderBy(c => c.Name)
                .Select(c => new SimilarCustomerDto { Id = c.Id, Name = c.Name, ContactPerson = Blank(c.ContactPerson), Phone = Blank(c.Phone) })
                .ToList();
        }

        // Gelijk, of de één bevat de ander (vanaf 3 tekens). Vangt hoofdletters, accenten en leestekens; geen fuzzy matching.
        public static bool IsSimilar(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            var shorter = a.Length <= b.Length ? a : b;
            var longer = a.Length <= b.Length ? b : a;
            return shorter.Length >= 3 && longer.Contains(shorter, StringComparison.Ordinal);
        }

        // Kleine letters, zonder accenten en leestekens, één spatie tussen woorden.
        public static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            var pendingSpace = false;
            foreach (var ch in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch))
                {
                    if (pendingSpace && builder.Length > 0) builder.Append(' ');
                    pendingSpace = false;
                    builder.Append(char.ToLowerInvariant(ch));
                }
                else
                {
                    pendingSpace = true;
                }
            }
            return builder.ToString();
        }

        private static CustomerDto ToDto(Customer customer) => new()
        {
            Id = customer.Id,
            Name = customer.Name,
            VATNumber = Blank(customer.VATNumber),
            BillingAddress = Blank(customer.BillingAddress),
            ShippingAddress = Blank(customer.ShippingAddress),
            ContactPerson = Blank(customer.ContactPerson),
            Email = Blank(customer.Email),
            Phone = Blank(customer.Phone),
            PaymentTermsDays = customer.PaymentTermsDays,
            CreatedAt = customer.CreatedAt,
        };
    }
}
