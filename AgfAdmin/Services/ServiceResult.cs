namespace AgfAdmin.Services
{
    public enum ServiceResultStatus
    {
        Created,
        Ok,
        Validation,
        NotFound,
        Conflict
    }

    // Uitkomst van een domeinservice; de controller vertaalt deze één-op-één naar een HTTP-status.
    public sealed class ServiceResult<T>
    {
        private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

        private ServiceResult(ServiceResultStatus status, T value, IReadOnlyDictionary<string, string[]> errors, string message, object conflictBody = null)
        {
            Status = status;
            Value = value;
            Errors = errors ?? NoErrors;
            Message = message;
            ConflictBody = conflictBody;
        }

        public ServiceResultStatus Status { get; }
        public T Value { get; }
        public IReadOnlyDictionary<string, string[]> Errors { get; }
        public string Message { get; }
        // Optioneel volledig 409-antwoordlichaam (bevat zelf ook `message`); zonder dit wordt alleen { message } gestuurd.
        public object ConflictBody { get; }

        public static ServiceResult<T> Created(T value) => new(ServiceResultStatus.Created, value, null, null);
        public static ServiceResult<T> Ok(T value) => new(ServiceResultStatus.Ok, value, null, null);
        public static ServiceResult<T> Validation(IReadOnlyDictionary<string, string[]> errors) => new(ServiceResultStatus.Validation, default, errors, null);
        public static ServiceResult<T> NotFound(string message) => new(ServiceResultStatus.NotFound, default, null, message);
        public static ServiceResult<T> Conflict(string message) => new(ServiceResultStatus.Conflict, default, null, message);
        public static ServiceResult<T> Conflict(string message, object body) => new(ServiceResultStatus.Conflict, default, null, message, body);
    }
}
