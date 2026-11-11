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
            var key = $"{DateTime.UtcNow:yyyy-MM}/{Guid.NewGuid():N}{extension}";
            var path = PathOf(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                await stream.WriteAsync(bytes, cancellationToken);
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
            var full = Path.GetFullPath(Path.Combine(root, key));
            if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid document key.");
            return full;
        }
    }
}
