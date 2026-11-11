namespace AgfAdmin.Services.DocumentAi
{
    // Opslag van documenten: een sleutel per bestand, nooit overschreven.
    public interface IDocumentStore
    {
        Task<string> SaveAsync(byte[] bytes, string extension, CancellationToken cancellationToken);
        Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken);
        void Delete(string key);
    }

    // Bestanden in een instelbare map (DocumentAi:StoragePath), per maand een submap, met een willekeurige naam: geen bedrijfsnaam of
    // factuurnummer in het pad. Back-ups en versleuteling van die map horen bij de server (zie docs/ai-factuur.md).
    public sealed class FileDocumentStore(string root) : IDocumentStore
    {
        public async Task<string> SaveAsync(byte[] bytes, string extension, CancellationToken cancellationToken)
        {
            // De extensie komt van ons zelf (.pdf, .jpg, .png, .heic); toch nooit een padscheidingsteken in de naam.
            if (!System.Text.RegularExpressions.Regex.IsMatch(extension ?? "", @"^\.[a-z0-9]{1,8}$")) throw new ArgumentException("Invalid document extension.", nameof(extension));
            var key = $"{DateTime.UtcNow:yyyy-MM}/{Guid.NewGuid():N}{extension}";
            var path = PathOf(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // CreateNew faalt als het bestand al bestaat: dan blijft dat bestand staan. Alleen een bestand dat deze aanroep zelf aanmaakte en
            // niet helemaal kon schrijven, wordt weer weggehaald.
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            try
            {
                await using (stream) await stream.WriteAsync(bytes, cancellationToken);
            }
            catch
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
            return key;
        }

        public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken) => File.ReadAllBytesAsync(PathOf(key), cancellationToken);

        public void Delete(string key)
        {
            var path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
        }

        // Een sleutel komt altijd van ons zelf; toch nooit buiten de map.
        private string PathOf(string key)
        {
            var baseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var full = Path.GetFullPath(Path.Combine(baseDir, key));
            if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid document key.");
            return full;
        }
    }
}
