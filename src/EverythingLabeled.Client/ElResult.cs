namespace EverythingLabeled.Client;

public sealed class ElResult<T>
{
    private ElResult(bool isSuccess, T? data, string? message, ElError? error)
    {
        IsSuccess = isSuccess;
        Data = data;
        Message = message;
        Error = error;
    }

    public bool IsSuccess { get; }
    public T? Data { get; }
    public string? Message { get; }
    public ElError? Error { get; }

    public static ElResult<T> Ok(T data, string? message = null) =>
        new(true, data, message, null);

    public static ElResult<T> Fail(ElError error) =>
        new(false, default, null, error);
}
