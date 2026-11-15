using System.Globalization;
using Npgsql;

namespace AgfAdmin.Configuration
{
    // De demo-omgeving online (docs/demo-online.md), aan met AGF_DEMO_ENVIRONMENT=true. Ze dwingt af dat een klant er veilig mee kan
    // oefenen: alleen de nagebootste mailafzender (nooit SMTP), AI-lezen alleen met een echte sleutel en een harde limiet per dag en per
    // maand, en de API start alleen op een database waarvan de naam met agf_demo begint. De instellingen zijn platte omgevingsvariabelen,
    // zodat ze in deploy/.env staan zoals in deploy/.env.demo.example.
    public sealed class DemoEnvironment
    {
        public const string EnabledKey = "AGF_DEMO_ENVIRONMENT";
        public const string AiDailyLimitKey = "AGF_DEMO_AI_DAILY_LIMIT";
        public const string AiMonthlyLimitKey = "AGF_DEMO_AI_MONTHLY_LIMIT_EUR";
        public const string DatabasePrefix = "agf_demo";
        public const int DefaultAiDailyLimit = 20;
        public const decimal DefaultAiMonthlyLimitEur = 5m;

        public bool Enabled { get; init; }
        // Zoveel AI-lezingen per kalenderdag (Europe/Amsterdam) in deze demo, ook mislukte: elke lezing kost geld.
        public int AiDailyLimit { get; init; } = DefaultAiDailyLimit;
        // Bovengrens voor de maandlimiet uit Instellingen: daar kan een lagere staan, nooit een hogere.
        public decimal AiMonthlyLimitEur { get; init; } = DefaultAiMonthlyLimitEur;

        public static readonly DemoEnvironment Off = new();

        public static bool IsEnabled(IConfiguration configuration) =>
            bool.TryParse(configuration[EnabledKey]?.Trim(), out var enabled) && enabled;

        // Een onleesbare limiet is een fout bij het opstarten, geen stille terugval naar iets ruimers.
        public static DemoEnvironment From(IConfiguration configuration)
        {
            if (!IsEnabled(configuration)) return Off;
            return new DemoEnvironment
            {
                Enabled = true,
                AiDailyLimit = Read(configuration, AiDailyLimitKey, DefaultAiDailyLimit, value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null),
                AiMonthlyLimitEur = Read(configuration, AiMonthlyLimitKey, DefaultAiMonthlyLimitEur, value =>
                    decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var eur) && eur >= 0m ? eur : null),
            };
        }

        // Zonder demo-modus geldt de instelling zelf; in de demo nooit meer dan de bovengrens.
        public decimal MonthlyLimitEur(decimal configured) => Enabled ? Math.Min(configured, AiMonthlyLimitEur) : configured;

        // De demo mag nooit op agf_smoke of een echte klantdatabase draaien: null als de database goed is, anders waarom niet.
        public static string RefusedDatabaseReason(string connectionString)
        {
            string database;
            try
            {
                database = new NpgsqlConnectionStringBuilder(connectionString).Database;
            }
            catch (ArgumentException)
            {
                return "De verbindingsreeks is niet leesbaar.";
            }
            if (string.IsNullOrWhiteSpace(database)) return "De verbindingsreeks noemt geen database.";
            return database.StartsWith(DatabasePrefix, StringComparison.Ordinal)
                ? null
                : $"De database heet '{database}'; in de demo-omgeving moet de naam met {DatabasePrefix} beginnen.";
        }

        // Bij het opstarten (Program.cs), vóór er iets met de database gebeurt.
        public static void EnsureAllowedDatabase(IConfiguration configuration, string connectionString)
        {
            if (!IsEnabled(configuration)) return;
            if (RefusedDatabaseReason(connectionString) is string reason)
            {
                throw new InvalidOperationException(
                    $"Demo-omgeving ({EnabledKey}=true) gestopt: {reason} Zo draait de demo nooit op agf_smoke of een echte database; zie docs/demo-online.md.");
            }
        }

        private static T Read<T>(IConfiguration configuration, string key, T fallback, Func<string, T?> parse) where T : struct
        {
            var raw = configuration[key]?.Trim();
            if (string.IsNullOrEmpty(raw)) return fallback;
            return parse(raw) ?? throw new InvalidOperationException($"{key} heeft een ongeldige waarde ('{raw}'): een geheel getal of bedrag van 0 of meer.");
        }
    }
}
