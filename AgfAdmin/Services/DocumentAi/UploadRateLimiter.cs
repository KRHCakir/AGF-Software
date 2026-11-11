namespace AgfAdmin.Services.DocumentAi
{
    // Uploads per gebruiker per minuut (in het geheugen; bij meerdere servers per server).
    public sealed class UploadRateLimiter(TimeProvider clock)
    {
        private readonly Dictionary<string, Queue<DateTimeOffset>> _uploads = new();

        public bool TryAcquire(string user, int perMinute)
        {
            var now = clock.GetUtcNow();
            lock (_uploads)
            {
                if (!_uploads.TryGetValue(user, out var times)) _uploads[user] = times = new Queue<DateTimeOffset>();
                while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1)) times.Dequeue();
                if (times.Count >= Math.Max(1, perMinute)) return false;
                times.Enqueue(now);
                return true;
            }
        }
    }
}
