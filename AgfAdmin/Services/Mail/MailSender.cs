using System.Collections.Concurrent;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AgfAdmin.Services.Mail
{
    // Eén dunne laag voor het versturen van mail (documenten deel 3). Nu SMTP; een Microsoft 365-afzender via Graph volgt in de mailbox-ronde
    // als een tweede klasse naast deze. De nagebootste afzender is voor tests en de rondleiding: die verstuurt nooit iets over het netwerk.
    public interface IMailSender
    {
        string Kind { get; }
        Task<MailSendResult> SendAsync(OutgoingMail mail, SmtpOptions smtp, CancellationToken cancellationToken);
    }

    public sealed record OutgoingMail(string FromAddress, string FromName, IReadOnlyList<string> To, IReadOnlyList<string> Cc, string Subject, string Body, string AttachmentName, byte[] Attachment);

    // Wat de SMTP-afzender nodig heeft; het wachtwoord is hier al ontsleuteld en komt nooit in een log of een foutmelding.
    public sealed record SmtpOptions(string Host, int Port, string Security, string UserName, string Password);

    // Uncertain: de uitkomst is onbekend (de verbinding viel weg tijdens het versturen); de server kan de mail wel hebben aangenomen.
    // Dat is iets anders dan een aantoonbaar mislukte verzending (niet verbonden, aanmelden geweigerd, door de server afgewezen).
    public sealed record MailSendResult(bool Success, string MessageId, string Error, bool Uncertain = false);

    public sealed class SmtpMailSender : IMailSender
    {
        public const string Name = "Smtp";
        public string Kind => Name;

        public async Task<MailSendResult> SendAsync(OutgoingMail mail, SmtpOptions smtp, CancellationToken cancellationToken)
        {
            var message = MimeFor(mail);
            var sending = false;
            try
            {
                using var client = new SmtpClient { Timeout = 30_000 };
                var security = smtp.Security switch
                {
                    DocumentMailRules.SecuritySslOnConnect => SecureSocketOptions.SslOnConnect,
                    DocumentMailRules.SecurityNone => SecureSocketOptions.None,
                    _ => SecureSocketOptions.StartTls,
                };
                await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
                if (!string.IsNullOrEmpty(smtp.UserName)) await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
                sending = true;
                await client.SendAsync(message, cancellationToken);
                // De server heeft de mail nu geaccepteerd: een fout bij het netjes afsluiten maakt de verzending niet mislukt (opnieuw proberen = dubbel).
                try
                {
                    await client.DisconnectAsync(true, CancellationToken.None);
                }
                catch (Exception)
                {
                }
                return new MailSendResult(true, message.MessageId, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Alleen het soort fout en de melding van de server; nooit het wachtwoord (MailSendService maskeert het daarnaast nog).
                // Een antwoord van de server met een foutcode (SmtpCommandException) is een aantoonbare afwijzing. Een andere fout tijdens het
                // versturen zelf (verbinding weg, time-out) laat open of de server de mail al had aangenomen: onbekende uitkomst.
                var uncertain = sending && exception is not SmtpCommandException;
                return new MailSendResult(false, null, $"{exception.GetType().Name}: {exception.Message}", uncertain);
            }
        }

        public static MimeMessage MimeFor(OutgoingMail mail)
        {
            var message = new MimeMessage { Subject = mail.Subject };
            message.From.Add(new MailboxAddress(mail.FromName ?? "", mail.FromAddress));
            foreach (var to in mail.To) message.To.Add(MailboxAddress.Parse(to));
            foreach (var cc in mail.Cc) message.Cc.Add(MailboxAddress.Parse(cc));
            var body = new BodyBuilder { TextBody = mail.Body };
            body.Attachments.Add(mail.AttachmentName, mail.Attachment, new ContentType("application", "pdf"));
            message.Body = body.ToMessageBody();
            message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
            return message;
        }
    }

    // Nagebootste afzender: bewaart wat er verstuurd zou zijn, in het geheugen, en zegt "verstuurd". Nooit netwerk. Voor tests en de
    // rondleiding (Mail:Sender = Fake); FailWith laat de volgende verzending mislukken.
    public sealed class FakeMailSender : IMailSender
    {
        public const string Name = "Fake";
        public string Kind => Name;
        public ConcurrentQueue<OutgoingMail> Sent { get; } = new();
        public string FailWith { get; set; }

        public Task<MailSendResult> SendAsync(OutgoingMail mail, SmtpOptions smtp, CancellationToken cancellationToken)
        {
            if (FailWith is { } error)
            {
                FailWith = null;
                return Task.FromResult(new MailSendResult(false, null, error));
            }
            Sent.Enqueue(mail);
            return Task.FromResult(new MailSendResult(true, $"<{Guid.NewGuid():N}@nagebootst.agf.local>", null));
        }
    }
}
