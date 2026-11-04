namespace EducationHub.Shared.Common.Models;

/// <summary>
/// Non-generic result envelope for operations that return no payload.
/// </summary>
/// <remarks>
/// <see cref="Result{T}"/> derives from this type on purpose. MediatR pipeline behaviours
/// are constrained with <c>where TResponse : Result</c>, so without this inheritance a
/// behaviour could only ever intercept the handful of handlers that return a bare
/// <see cref="Result"/> and would silently pass through every <see cref="Result{T}"/>
/// handler - losing exception mapping and validation on most of the API.
/// </remarks>
public class Result
{
    protected Result(bool succeeded, string message, IReadOnlyList<Error> errors)
    {
        Succeeded = succeeded;
        Message = message;
        Errors = errors;
    }

    public bool Succeeded { get; }

    /// <summary>Convenience inverse of <see cref="Succeeded"/>.</summary>
    public bool IsFailure => !Succeeded;

    public string Message { get; }

    public IReadOnlyList<Error> Errors { get; }

    public static Result Success(string message = "Success")
        => new(true, message, []);

    public static Result Failure(string message, IReadOnlyList<Error>? errors = null)
        => new(false, message, errors ?? [new Error("Error", message)]);

    public static Result Failure(IReadOnlyList<Error> errors, string message = "Validation failed")
        => new(false, message, errors);

    public static Result Failure(string code, string message)
        => new(false, message, [new Error(code, message)]);
}

/// <summary>
/// The single response envelope for the entire EducationHub API. Every handler returns a
/// <see cref="Result{T}"/> so the wire format never changes shape, whether the outcome is
/// success, not-found, conflict or validation failure.
/// </summary>
/// <remarks>
/// Deliberately mirrors the house convention used across the reference solutions
/// (<c>Succeeded</c> / <c>Data</c> / <c>Message</c> / <c>Errors</c>) rather than the
/// minimal <c>{ isSuccess, message, data }</c> shape, because FluentValidation produces a
/// *list* of field-level errors and the minimal shape has nowhere to put them.
/// </remarks>
public class Result<T> : Result
{
    protected Result(bool succeeded, T? data, string message, IReadOnlyList<Error> errors)
        : base(succeeded, message, errors)
    {
        Data = data;
    }

    public T? Data { get; }

    public static Result<T> Success(T data, string message = "Success")
        => new(true, data, message, []);

    public static new Result<T> Success(string message = "Success")
        => new(true, default, message, []);

    public static new Result<T> Failure(string message, IReadOnlyList<Error>? errors = null)
        => new(false, default, message, errors ?? [new Error("Error", message)]);

    public static new Result<T> Failure(IReadOnlyList<Error> errors, string message = "Validation failed")
        => new(false, default, message, errors);

    public static new Result<T> Failure(string code, string message)
        => new(false, default, message, [new Error(code, message)]);

    /// <summary>Forwards a failure from another result, keeping its message and error list intact.</summary>
    public static Result<T> FromFailure(Result failure)
        => new(false, default, failure.Message, failure.Errors);
}
