using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Partijen: nummering volgens de bedrijfsinstelling, los aanmaken, dubbelcontrole op de sleutel, nummer corrigeren (met reden en
    // historie), verwijderen zonder koppelingen en een bestaande inkooporder koppelen. Een nieuwe partij samen met een inkooporder loopt
    // via SupplyCommitmentService (één save). De regels van regel 23 bewaakt de TraceabilityGuard daarnaast bij elke schrijver.
    public sealed partial class PartijService
    {
        // Pogingen bij een gelijktijdig toegekend volgnummer (unieke index op periode + volgnummer of op de sleutel).
        public const int MaxNumberAttempts = 5;
        // Zoveel nummers die al met de hand zijn bezet slaat het voorstel hoogstens over voordat het opgeeft.
        private const int MaxSkippedNumbers = 1000;

        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        // Zonder ingelogde gebruiker (tests, seed) de standaardgebruiker; in de API altijd de gebruiker van het verzoek.
        public PartijService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _db = db;
            _user = currentUser ?? CurrentUser.Default;
        }

        // ---------------- Instellingen en voorstel ----------------

        public async Task<PartijSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(_db, cancellationToken);
            return new PartijSettingsDto
            {
                Mode = settings.PartijNumberMode, Format = settings.PartijNumberFormat, SequenceReset = settings.PartijSequenceReset,
                SequenceDigits = settings.PartijSequenceDigits, SalesFormat = settings.SalesPartijNumberFormat, SalesSequenceDigits = settings.SalesPartijSequenceDigits
            };
        }

        public async Task<ServiceResult<PartijSettingsDto>> UpdateSettingsAsync(PartijSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = SettingsErrors(request?.Mode, request?.Format, request?.SequenceReset);
            // Verkoopformaat en aantal cijfers zijn later toegevoegd: weggelaten = ongewijzigd laten.
            var salesFormatError = request?.SalesFormat == null ? null : PartijRules.FormatError(request.SalesFormat);
            if (salesFormatError != null)
            {
                errors["salesFormat"] = new[] { salesFormatError };
            }
            if (request?.SequenceDigits != null && !PartijRules.IsSequenceDigits(request.SequenceDigits.Value))
            {
                errors["sequenceDigits"] = new[] { DigitsMessage };
            }
            if (request?.SalesSequenceDigits != null && !PartijRules.IsSequenceDigits(request.SalesSequenceDigits.Value))
            {
                errors["salesSequenceDigits"] = new[] { DigitsMessage };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<PartijSettingsDto>.Validation(errors);
            }
            var settings = await _db.CompanySettings.FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                settings = new CompanySettings();
                _db.CompanySettings.Add(settings);
            }
            settings.PartijNumberMode = request.Mode;
            settings.PartijNumberFormat = request.Format.Trim();
            settings.PartijSequenceReset = request.SequenceReset;
            if (request.SalesFormat != null)
            {
                settings.SalesPartijNumberFormat = request.SalesFormat.Trim();
            }
            if (request.SequenceDigits != null)
            {
                settings.PartijSequenceDigits = request.SequenceDigits.Value;
            }
            if (request.SalesSequenceDigits != null)
            {
                settings.SalesPartijSequenceDigits = request.SalesSequenceDigits.Value;
            }
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<PartijSettingsDto>.Ok(await GetSettingsAsync(cancellationToken));
        }

        // Volgende voorgestelde nummer van deze soort voor vandaag; met format/reset/digits een voorbeeld voor nog niet opgeslagen
        // instellingen. Een voorstel is geen reservering: bij opslaan wordt het nummer opnieuw (in de transactie) toegekend.
        public async Task<ServiceResult<PartijNextNumberDto>> NextNumberAsync(string format = null, string reset = null, string kind = null, int? digits = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveKind = string.IsNullOrWhiteSpace(kind) ? PartijRules.KindPurchase : kind.Trim();
            if (!PartijRules.IsKind(effectiveKind))
            {
                return ServiceResult<PartijNextNumberDto>.Validation(new Dictionary<string, string[]> { ["kind"] = new[] { $"Kind must be one of: {string.Join(", ", PartijRules.Kinds)}." } });
            }
            var settings = await SettingsAsync(_db, cancellationToken);
            var effectiveFormat = string.IsNullOrWhiteSpace(format) ? FormatOf(settings, effectiveKind) : format.Trim();
            var effectiveReset = string.IsNullOrWhiteSpace(reset) ? settings.PartijSequenceReset : reset.Trim();
            var effectiveDigits = digits ?? DigitsOf(settings, effectiveKind);
            var errors = SettingsErrors(settings.PartijNumberMode, effectiveFormat, effectiveReset);
            if (!PartijRules.IsSequenceDigits(effectiveDigits))
            {
                errors["sequenceDigits"] = new[] { DigitsMessage };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<PartijNextNumberDto>.Validation(errors);
            }
            var next = await AllocateAsync(_db, effectiveKind, effectiveFormat, effectiveReset, effectiveDigits, _db.BusinessCalendar.Today(), async: true, cancellationToken);
            return ServiceResult<PartijNextNumberDto>.Ok(new PartijNextNumberDto
            {
                Kind = effectiveKind,
                Mode = settings.PartijNumberMode,
                Format = effectiveFormat,
                SequenceReset = effectiveReset,
                SequenceDigits = effectiveDigits,
                Suggestion = settings.PartijNumberMode == PartijRules.ModeAuto ? next.Number : null
            });
        }

        internal static string FormatOf(CompanySettings settings, string kind) =>
            kind == PartijRules.KindSales ? settings.SalesPartijNumberFormat : settings.PartijNumberFormat;

        internal static int DigitsOf(CompanySettings settings, string kind) =>
            kind == PartijRules.KindSales ? settings.SalesPartijSequenceDigits : settings.PartijSequenceDigits;

        private static readonly string DigitsMessage = $"The sequence needs between {PartijRules.MinSequenceDigits} and {PartijRules.MaxSequenceDigits} digits.";

        // ---------------- Zoeken op nummer (dubbelcontrole) ----------------

        // Binnen één soort: een inkoop- en een verkoop-partij mogen hetzelfde nummer hebben (uniek per soort).
        public async Task<PartijSummaryDto> FindByNumberAsync(string number, string kind = PartijRules.KindPurchase, CancellationToken cancellationToken = default)
        {
            var key = PartijRules.NumberKey(number);
            if (key == null || !PartijRules.IsKind(kind))
            {
                return null;
            }
            var partij = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.Kind == kind && p.NumberKey == key, cancellationToken);
            return partij == null ? null : await SummaryAsync(partij, cancellationToken);
        }

        // ---------------- Los aanmaken ----------------

        public async Task<ServiceResult<PartijSummaryDto>> CreateAsync(CreatePartijDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            if (request == null || request.RequestId == null || request.RequestId == Guid.Empty)
            {
                errors["requestId"] = new[] { "A client-generated, non-empty requestId is required." };
            }
            var note = string.IsNullOrWhiteSpace(request?.Note) ? null : request.Note.Trim();
            if (note != null && note.Length > PartijRules.MaxNoteLength)
            {
                errors["note"] = new[] { $"This field may contain at most {PartijRules.MaxNoteLength} characters." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<PartijSummaryDto>.Validation(errors);
            }

            for (var attempt = 1; ; attempt++)
            {
                var replay = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.RequestId == request.RequestId, cancellationToken);
                if (replay != null)
                {
                    return SameRequest(replay, request.Number, note)
                        ? ServiceResult<PartijSummaryDto>.Ok(await SummaryAsync(replay, cancellationToken))
                        : ServiceResult<PartijSummaryDto>.Conflict($"RequestId {request.RequestId} was already used for lot {replay.Number}.");
                }

                var built = await BuildNewAsync<PartijSummaryDto>(request.Number, "number", PartijRules.KindPurchase, cancellationToken);
                if (built.Refused != null)
                {
                    // Een gelijktijdige herhaling van dezelfde aanvraag kan de partij net hebben opgeslagen: dan is dit een replay, geen nummerconflict.
                    var raced = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.RequestId == request.RequestId, cancellationToken);
                    if (raced != null)
                    {
                        return SameRequest(raced, request.Number, note)
                            ? ServiceResult<PartijSummaryDto>.Ok(await SummaryAsync(raced, cancellationToken))
                            : ServiceResult<PartijSummaryDto>.Conflict($"RequestId {request.RequestId} was already used for lot {raced.Number}.");
                    }
                    return built.Refused;
                }
                built.Partij.RequestId = request.RequestId;
                built.Partij.Note = note;
                _db.Partijen.Add(built.Partij);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return ServiceResult<PartijSummaryDto>.Created(await SummaryAsync(built.Partij, cancellationToken));
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < MaxNumberAttempts)
                {
                    // Een gelijktijdige schrijver koos hetzelfde nummer (of dezelfde aanvraag kwam twee keer): opnieuw, met de replay eerst.
                    _db.ChangeTracker.Clear();
                }
            }
        }

        // Een nieuwe (nog niet opgeslagen) partij van deze soort: een eigen nummer na de dubbelcontrole, of het volgende voorgestelde
        // nummer (alleen in modus Auto). Een bestaand nummer van dezelfde soort geeft 409 PARTIJ_EXISTS met die partij, nooit een tweede.
        internal async Task<(Partij Partij, ServiceResult<T> Refused)> BuildNewAsync<T>(string requestedNumber, string field, string kind, CancellationToken cancellationToken)
        {
            var settings = await SettingsAsync(_db, cancellationToken);
            var now = DateTime.UtcNow;
            var actor = _user.Actor(PartijRules.MaxActorLength);
            if (string.IsNullOrWhiteSpace(requestedNumber))
            {
                if (settings.PartijNumberMode != PartijRules.ModeAuto)
                {
                    return (null, ServiceResult<T>.Validation(new Dictionary<string, string[]>
                    {
                        [field] = new[] { "Numbering is set to own numbers only: type the lot number." }
                    }));
                }
                var next = await AllocateAsync(_db, kind, FormatOf(settings, kind), settings.PartijSequenceReset, DigitsOf(settings, kind), _db.BusinessCalendar.Today(),
                    async: true, cancellationToken);
                return (new Partij
                {
                    Kind = kind, Number = next.Number, NumberKey = PartijRules.NumberKey(next.Number), SequencePeriod = next.Period, Sequence = next.Sequence,
                    CreatedAt = now, CreatedBy = actor
                }, null);
            }

            var number = requestedNumber.Trim();
            var numberError = NumberError(number);
            if (numberError != null)
            {
                return (null, ServiceResult<T>.Validation(new Dictionary<string, string[]> { [field] = new[] { numberError } }));
            }
            var existing = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.Kind == kind && p.NumberKey == PartijRules.NumberKey(number), cancellationToken);
            if (existing != null)
            {
                return (null, await ExistsConflictAsync<T>(existing, cancellationToken));
            }
            return (new Partij { Kind = kind, Number = number, NumberKey = PartijRules.NumberKey(number), CreatedAt = now, CreatedBy = actor }, null);
        }

        // ---------------- Nummer corrigeren ----------------

        // Een verkoop-partij waarvan de order nog niets gereserveerd of geleverd heeft (en niet geannuleerd is) mag zonder reden worden
        // aangepast (NumberChanged); daarna, en bij elke inkoop-partij, alleen met reden (NumberCorrected). Altijd met historie.
        public async Task<ServiceResult<PartijSummaryDto>> CorrectNumberAsync(int id, CorrectPartijNumberDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            var number = request?.Number?.Trim();
            var numberError = NumberError(number);
            if (numberError != null)
            {
                errors["number"] = new[] { numberError };
            }
            var reason = request?.Reason?.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                errors["reason"] = new[] { "A reason is required to correct a lot number." };
            }
            else if (reason.Length > PartijRules.MaxReasonLength)
            {
                errors["reason"] = new[] { $"This field may contain at most {PartijRules.MaxReasonLength} characters." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<PartijSummaryDto>.Validation(errors);
            }

            var partij = await _db.Partijen.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (partij == null)
            {
                return ServiceResult<PartijSummaryDto>.NotFound($"Lot {id} does not exist.");
            }
            if (string.Equals(partij.Number, number, StringComparison.Ordinal))
            {
                return ServiceResult<PartijSummaryDto>.Validation(new Dictionary<string, string[]> { ["number"] = new[] { "The new number is the same as the current number." } });
            }
            var key = PartijRules.NumberKey(number);
            var other = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.NumberKey == key && p.Id != id, cancellationToken);
            if (other != null)
            {
                return await ExistsConflictAsync<PartijSummaryDto>(other, cancellationToken);
            }

            _db.PartijAuditEvents.Add(new PartijAuditEvent
            {
                PartijId = partij.Id, EventType = PartijRules.EventNumberCorrected, OldValue = partij.Number, NewValue = number, Reason = reason,
                ActorReference = _user.Actor(PartijRules.MaxActorLength), OccurredAt = DateTime.UtcNow
            });
            partij.Number = number;
            partij.NumberKey = key;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.NumberKey == key, cancellationToken);
                return concurrent == null ? throw exception : await ExistsConflictAsync<PartijSummaryDto>(concurrent, cancellationToken);
            }
            return ServiceResult<PartijSummaryDto>.Ok(await SummaryAsync(partij, cancellationToken));
        }

        // ---------------- Verwijderen ----------------

        public async Task<ServiceResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var partij = await _db.Partijen.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (partij == null)
            {
                return ServiceResult<bool>.NotFound($"Lot {id} does not exist.");
            }
            if (await IsLinkedAsync(id, cancellationToken))
            {
                return ServiceResult<bool>.Conflict($"Lot {partij.Number} is linked to a purchase order or pallet line and cannot be deleted.");
            }
            _db.PartijAuditEvents.Add(new PartijAuditEvent
            {
                PartijId = partij.Id, EventType = PartijRules.EventDeleted, OldValue = partij.Number,
                ActorReference = _user.Actor(PartijRules.MaxActorLength), OccurredAt = DateTime.UtcNow
            });
            _db.Partijen.Remove(partij);
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<bool>.Ok(true);
        }

        // ---------------- Inkooporder koppelen ----------------

        // Zolang er niets ontvangen is: de order en al haar regels naar de gekozen partij (ook een andere dan eerst). Na ontvangst alleen
        // een order zonder partij, met reden: dan krijgen ook de ontvangen palletregels zonder partij deze partij (het gat uit de migratie).
        // Hun LotNumber blijft staan. Een regel of palletregel van een andere partij wordt nooit omgehangen.
        public async Task<ServiceResult<PartijSummaryDto>> LinkOrderAsync(int partijId, LinkPartijDto request, CancellationToken cancellationToken = default)
        {
            if (request?.SupplyCommitmentId == null)
            {
                return ServiceResult<PartijSummaryDto>.Validation(new Dictionary<string, string[]> { ["supplyCommitmentId"] = new[] { "Choose a purchase order." } });
            }
            var partij = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.Id == partijId, cancellationToken);
            if (partij == null)
            {
                return ServiceResult<PartijSummaryDto>.NotFound($"Lot {partijId} does not exist.");
            }
            var order = await _db.SupplyCommitments.Include(c => c.Lines).SingleOrDefaultAsync(c => c.Id == request.SupplyCommitmentId, cancellationToken);
            if (order == null)
            {
                return ServiceResult<PartijSummaryDto>.NotFound($"Purchase order {request.SupplyCommitmentId} does not exist.");
            }
            var lineIds = order.Lines.Select(l => l.Id).ToList();
            var contents = await _db.PalletContentLines.Where(c => lineIds.Contains(c.SupplyCommitmentLineId)).ToListAsync(cancellationToken);
            if (order.PartijId == partij.Id && order.Lines.All(l => l.PartijId == partij.Id) && contents.All(c => c.PartijId != null))
            {
                return ServiceResult<PartijSummaryDto>.Ok(await SummaryAsync(partij, cancellationToken));
            }
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason != null && reason.Length > PartijRules.MaxReasonLength)
            {
                return ServiceResult<PartijSummaryDto>.Validation(new Dictionary<string, string[]> { ["reason"] = new[] { $"This field may contain at most {PartijRules.MaxReasonLength} characters." } });
            }
            if (contents.Count > 0)
            {
                if (order.PartijId != null)
                {
                    return ServiceResult<PartijSummaryDto>.Conflict($"Purchase order {order.OrderNumber} has received goods; its lot is fixed. Correct the lot number instead.");
                }
                if (order.Lines.Any(l => l.PartijId != null && l.PartijId != partij.Id) || contents.Any(c => c.PartijId != null && c.PartijId != partij.Id))
                {
                    return ServiceResult<PartijSummaryDto>.Conflict($"Purchase order {order.OrderNumber} already has received goods of another lot; lots are never mixed.");
                }
                if (reason == null)
                {
                    return ServiceResult<PartijSummaryDto>.Validation(new Dictionary<string, string[]> { ["reason"] = new[] { "Goods have already been received: give a reason for linking the lot." } });
                }
            }

            var oldPartijNumber = order.PartijId == null ? null
                : await _db.Partijen.AsNoTracking().Where(p => p.Id == order.PartijId).Select(p => p.Number).SingleAsync(cancellationToken);
            order.PartijId = partij.Id;
            order.UpdatedAt = DateTime.UtcNow;
            foreach (var line in order.Lines)
            {
                line.PartijId = partij.Id;
            }
            foreach (var content in contents.Where(c => c.PartijId == null))
            {
                content.PartijId = partij.Id;
            }
            _db.PartijAuditEvents.Add(new PartijAuditEvent
            {
                PartijId = partij.Id, EventType = PartijRules.EventOrderLinked, SupplyCommitmentId = order.Id, OldValue = oldPartijNumber, NewValue = partij.Number,
                Reason = reason, ActorReference = _user.Actor(PartijRules.MaxActorLength), OccurredAt = DateTime.UtcNow
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<PartijSummaryDto>.Conflict($"Purchase order {order.OrderNumber} was changed at the same time; reload and try again.");
            }
            return ServiceResult<PartijSummaryDto>.Ok(await SummaryAsync(partij, cancellationToken));
        }

        // ---------------- Gedeeld ----------------

        // Volgende vrije voorstel van deze soort in de periode: hoogste volgnummer + 1 (opgeslagen, of al in deze save toegekend), en
        // verder zolang dat nummer al met de hand bestaat (een eigen nummer verschuift de teller niet). Elke soort heeft een eigen teller.
        // Uniek gemaakt door de database (soort + periode + volgnummer, en soort + sleutel).
        internal static async Task<(string Number, string Period, int Sequence)> AllocateAsync(AppDbContext db, string kind, string format, string reset, int digits,
            DateOnly today, bool async, CancellationToken cancellationToken)
        {
            var period = PartijRules.SequencePeriod(reset, today);
            var storedQuery = db.Partijen.AsNoTracking().Where(p => p.Kind == kind && p.SequencePeriod == period).Select(p => p.Sequence);
            var stored = (async ? await storedQuery.MaxAsync(cancellationToken) : storedQuery.Max()) ?? 0;
            var pending = db.ChangeTracker.Entries<Partij>().Where(e => e.State == EntityState.Added && e.Entity.Kind == kind).Select(e => e.Entity).ToList();
            var sequence = Math.Max(stored, pending.Where(p => p.SequencePeriod == period).Select(p => p.Sequence ?? 0).DefaultIfEmpty(0).Max()) + 1;
            for (var skipped = 0; skipped < MaxSkippedNumbers; skipped++, sequence++)
            {
                var number = PartijRules.Format(format, today, sequence, digits);
                var key = PartijRules.NumberKey(number);
                var takenQuery = db.Partijen.AsNoTracking().Where(p => p.Kind == kind && p.NumberKey == key);
                if (pending.All(p => p.NumberKey != key) && !(async ? await takenQuery.AnyAsync(cancellationToken) : takenQuery.Any()))
                {
                    return (number, period, sequence);
                }
            }
            throw new InvalidOperationException($"No free lot number after {MaxSkippedNumbers} numbers in period '{period}'.");
        }

        // Het nummer van een verkoop-partij ligt vast zodra er op haar order iets gereserveerd (of toegewezen, en dus ook geleverd) is,
        // of als de order geannuleerd is. Daarvoor mag het zonder reden worden aangepast (regel 24).
        internal static async Task<bool> SalesNumberFixedAsync(AppDbContext db, int salesOrderId, bool async, CancellationToken cancellationToken)
        {
            var query = db.SalesOrders.AsNoTracking().Where(o => o.Id == salesOrderId
                && (o.Status != ReservationRules.OrderStatusOpen || o.Lines.Any(l => db.StockReservations.Any(r => r.SalesOrderLineId == l.Id))));
            return async ? await query.AnyAsync(cancellationToken) : query.Any();
        }

        internal static async Task<CompanySettings> SettingsAsync(AppDbContext db, CancellationToken cancellationToken) =>
            await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken) ?? new CompanySettings();

        internal static string NumberError(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
            {
                return "A lot number is required.";
            }
            if (number.Trim().Length > PartijRules.MaxNumberLength)
            {
                return $"A lot number may contain at most {PartijRules.MaxNumberLength} characters.";
            }
            return PartijRules.NumberKey(number) == null ? "A lot number needs at least one letter or digit." : null;
        }

        internal async Task<ServiceResult<T>> ExistsConflictAsync<T>(Partij existing, CancellationToken cancellationToken)
        {
            var message = $"Lot {existing.Number} already exists. Use the existing lot or choose another number.";
            return ServiceResult<T>.Conflict(message, new PartijExistsConflictDto { Message = message, Partij = await SummaryAsync(existing, cancellationToken) });
        }

        private static Dictionary<string, string[]> SettingsErrors(string mode, string format, string reset)
        {
            var errors = new Dictionary<string, string[]>();
            if (!PartijRules.IsMode(mode))
            {
                errors["mode"] = new[] { $"Mode must be one of: {string.Join(", ", PartijRules.Modes)}." };
            }
            var formatError = PartijRules.FormatError(format);
            if (formatError != null)
            {
                errors["format"] = new[] { formatError };
            }
            if (!PartijRules.IsReset(reset))
            {
                errors["sequenceReset"] = new[] { $"Sequence reset must be one of: {string.Join(", ", PartijRules.Resets)}." };
            }
            return errors;
        }

        private static bool SameRequest(Partij existing, string number, string note) =>
            string.Equals(existing.Note, note, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(number) ? existing.Sequence != null : PartijRules.NumberKey(number) == existing.NumberKey);

        private async Task<bool> IsLinkedAsync(int id, CancellationToken cancellationToken) =>
            await _db.SupplyCommitments.AsNoTracking().AnyAsync(c => c.PartijId == id, cancellationToken)
            || await _db.SupplyCommitmentLines.AsNoTracking().AnyAsync(l => l.PartijId == id, cancellationToken)
            || await _db.PalletContentLines.AsNoTracking().AnyAsync(c => c.PartijId == id, cancellationToken);

        internal async Task<PartijSummaryDto> SummaryAsync(Partij partij, CancellationToken cancellationToken) => new()
        {
            Id = partij.Id,
            Number = partij.Number,
            Kind = partij.Kind,
            SalesOrderId = partij.SalesOrderId,
            Status = partij.Status,
            Note = partij.Note,
            CreatedAt = DateTime.SpecifyKind(partij.CreatedAt, DateTimeKind.Utc),
            CreatedBy = partij.CreatedBy,
            Linked = await IsLinkedAsync(partij.Id, cancellationToken)
        };
    }
}
