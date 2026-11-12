using System.Security.Cryptography;
using System.Text;
using AgfDocumentAi;
using ImageMagick;

namespace AgfAdmin.Services.DocumentAi
{
    // Een geüpload document, klaar om te bewaren en te lezen: het origineel (met hash) en, als dat nodig is, een versie voor de weergave en
    // de AI (een HEIC-foto als JPEG; een grote foto verkleind, zodat hij binnen de beeldgrens van de aanbieder valt).
    public sealed record PreparedDocument(byte[] Original, string MediaType, string Extension, string Sha256, DocumentInput ForAi, byte[] Display, string DisplayMediaType);

    public static class UploadedDocuments
    {
        // Foutcodes (de frontend toont er een korte uitleg bij).
        public const string Empty = "empty";
        public const string TooLarge = "tooLarge";
        public const string Unsupported = "unsupported";
        public const string Encrypted = "encrypted";
        public const string Unreadable = "unreadable";

        private const int MaxImageSide = 2000;
        // Grens vóór het uitpakken: een klein bestand met enorme afmetingen zou anders veel geheugen kosten (zeker een telefoonfoto: 50 MP).
        private const long MaxImagePixels = 60_000_000;
        private const int MaxImageBytesForAi = 3_500_000;
        private static readonly string[] HeifBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"];

        // Herkent het type aan de inhoud (niet aan de bestandsnaam): PDF, JPEG, PNG of HEIC/HEIF.
        public static string Sniff(byte[] bytes)
        {
            if (bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-') return "application/pdf";
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G' && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return "image/png";
            if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp" && HeifBrands.Contains(Encoding.ASCII.GetString(bytes, 8, 4))) return "image/heic";
            return null;
        }

        public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        public static (PreparedDocument Document, string Error) Prepare(string fileName, byte[] bytes, long maxBytes)
        {
            if (bytes == null || bytes.Length == 0) return (null, Empty);
            if (bytes.Length > maxBytes) return (null, TooLarge);
            var mediaType = Sniff(bytes);
            if (mediaType == null) return (null, Unsupported);
            var name = string.IsNullOrWhiteSpace(fileName) ? "document" : Path.GetFileName(fileName.Trim());
            var hash = Sha256(bytes);
            if (mediaType == "application/pdf")
            {
                // Een PDF met wachtwoord of beveiliging kan de aanbieder niet lezen: zeg dat meteen in plaats van een fout terug te krijgen.
                var text = Encoding.Latin1.GetString(bytes);
                if (text.Contains("/Encrypt", StringComparison.Ordinal)) return (null, Encrypted);
                // Een afgekapte PDF (zonder %%EOF aan het eind) kan de aanbieder niet lezen: meteen "onleesbaar".
                if (!text[Math.Max(0, text.Length - 2048)..].Contains("%%EOF", StringComparison.Ordinal)) return (null, Unreadable);
                return (new PreparedDocument(bytes, mediaType, ".pdf", hash, new DocumentInput(name, bytes, mediaType), null, null), null);
            }
            try
            {
                var info = new MagickImageInfo(bytes);
                if ((long)info.Width * info.Height > MaxImagePixels) return (null, Unreadable);
                using var image = new MagickImage(bytes);
                var heic = mediaType == "image/heic";
                var large = Math.Max(image.Width, image.Height) > MaxImageSide || bytes.Length > MaxImageBytesForAi;
                if (!heic && !large)
                {
                    return (new PreparedDocument(bytes, mediaType, mediaType == "image/png" ? ".png" : ".jpg", hash, new DocumentInput(name, bytes, mediaType), null, null), null);
                }
                image.AutoOrient();
                if (large) image.Resize(new MagickGeometry(MaxImageSide, MaxImageSide) { Greater = true });
                image.Format = MagickFormat.Jpeg;
                image.Quality = 85;
                var jpeg = image.ToByteArray();
                var ai = new DocumentInput(Path.ChangeExtension(name, ".jpg"), jpeg, "image/jpeg");
                // Een HEIC-foto kan een browser niet tonen: de weergave is de JPEG. Een grote JPEG of PNG toont de browser zelf.
                return (new PreparedDocument(bytes, mediaType, heic ? ".heic" : mediaType == "image/png" ? ".png" : ".jpg", hash, ai,
                    heic ? jpeg : null, heic ? "image/jpeg" : null), null);
            }
            catch (MagickException)
            {
                return (null, Unreadable);
            }
        }
    }
}
