namespace HomeShowing.BuildingBlocks.Results;

public enum ErrorKind { Validation, NotFound, Forbidden, Conflict, Unauthorized, Unprocessable, RateLimited }

public sealed record Error(ErrorKind Kind, string Title, string? Detail = null, IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static Error NotFound(string what) => new(ErrorKind.NotFound, $"{what} not found");
    public static Error Forbidden(string detail) => new(ErrorKind.Forbidden, "Forbidden", detail);
    public static Error Conflict(string title, string? detail = null, IReadOnlyDictionary<string, object?>? ext = null) => new(ErrorKind.Conflict, title, detail, ext);
    public static Error Validation(string detail) => new(ErrorKind.Validation, "Validation failed", detail);
    public static Error Unprocessable(string title, string? detail = null) => new(ErrorKind.Unprocessable, title, detail);
    public static Error Unauthorized(string detail) => new(ErrorKind.Unauthorized, "Unauthorized", detail);
    public static Error RateLimited(string detail) => new(ErrorKind.RateLimited, "Too many requests", detail);
}

/// <summary>Application services return results instead of throwing for expected failures.
/// REST endpoints map them to ProblemDetails; MCP tools map them to tool errors.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {Error!.Title}");

    private Result(T? value, Error? error) { _value = value; Error = error; }
    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(Error error) => new(default, error);
    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}
