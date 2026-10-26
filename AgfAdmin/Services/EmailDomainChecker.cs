using DnsClient;

namespace AgfAdmin.Services
{
    public enum EmailDomainStatus
    {
        // Niet gecontroleerd (geen controleur geconfigureerd, bijvoorbeeld in tests): geen signaal.
        NotChecked,
        HasMailServer,
        // Het domein bestaat niet of heeft geen MX-record.
        NoMailServer,
        // DNS niet bereikbaar, time-out of andere fout (offline): alleen een signaal, nooit blokkeren.
        Unavailable
    }

    // Controle of een e-maildomein een mailserver heeft (MX). Alleen een waarschuwing bij opslaan; nooit blokkerend.
    public interface IEmailDomainChecker
    {
        Task<EmailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken = default);
    }

    public sealed class NullEmailDomainChecker : IEmailDomainChecker
    {
        public static readonly NullEmailDomainChecker Instance = new();
        public Task<EmailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken = default) => Task.FromResult(EmailDomainStatus.NotChecked);
    }

    // MX-opvraging via DnsClient met een harde time-out van 2 seconden en zonder herhaalpogingen.
    public sealed class DnsEmailDomainChecker : IEmailDomainChecker
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private readonly ILookupClient _client;

        public DnsEmailDomainChecker() : this(new LookupClient(new LookupClientOptions { Timeout = Timeout, Retries = 0, UseCache = true, ThrowDnsErrors = false })) {}

        public DnsEmailDomainChecker(ILookupClient client) => _client = client;

        public async Task<EmailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return EmailDomainStatus.NoMailServer;
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                var response = await _client.QueryAsync(domain, QueryType.MX, QueryClass.IN, timeout.Token);
                if (response.HasError)
                {
                    // NXDOMAIN: het domein bestaat niet (dus ook geen mailserver); andere fouten zeggen niets over het domein.
                    return response.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain ? EmailDomainStatus.NoMailServer : EmailDomainStatus.Unavailable;
                }
                return response.Answers.MxRecords().Any() ? EmailDomainStatus.HasMailServer : EmailDomainStatus.NoMailServer;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is DnsResponseException or OperationCanceledException or System.Net.Sockets.SocketException or TimeoutException)
            {
                return EmailDomainStatus.Unavailable;
            }
        }
    }
}
