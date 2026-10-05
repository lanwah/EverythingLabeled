namespace EverythingLabeled.Service.Services.Abstractions;

public sealed class ServiceResult<T>
{
    private ServiceResult(bool isSuccess, T? data, string? message, string? code, object? details)
    {
        IsSuccess = isSuccess;
        Data = data;
        Message = message;
        Code = code;
        Details = details;
    }

    public bool IsSuccess { get; }
    public T? Data { get; }
    public string? Message { get; }
    public string? Code { get; }
    public object? Details { get; }

    public static ServiceResult<T> Ok(T data, string? message = null) =>
        new(true, data, message, null, null);

    public static ServiceResult<T> Fail(string code, string message, object? details = null) =>
        new(false, default, message, code, details);
}
