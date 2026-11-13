namespace AgfAdmin.DTOs
{
    // De afzender via SMTP (documenten deel 3), onder Beheer. Password: alleen schrijven (leeg laten = ongewijzigd, ClearPassword = wissen);
    // terug komt alleen HasPassword. Configured: versturen kan (server en van-adres ingevuld, of de nagebootste afzender).
    public sealed class MailSettingsDto
    {
        public string Host { get; set; }
        public int Port { get; set; } = 587;
        public string Security { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool ClearPassword { get; set; }
        public bool HasPassword { get; set; }
        public string FromAddress { get; set; }
        public string FromName { get; set; }
        public string Signature { get; set; }
        public string Sender { get; set; }
        public bool Configured { get; set; }
    }

    public sealed class MailStatusDto
    {
        public bool Configured { get; set; }
        public string Sender { get; set; }
    }
}
