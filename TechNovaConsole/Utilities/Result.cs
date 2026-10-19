using System;
using System.Collections.Generic;

namespace TechNovaConsole.Utilities;

/// <summary>
/// Simple result type to represent success or failure with optional value and error messages.
/// Used for expected validation and business rule failures.
/// </summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public IReadOnlyList<string> Errors { get; }

    private Result(bool isSuccess, IReadOnlyList<string> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors ?? Array.Empty<string>();
    }

    public static Result Success() => new(true, Array.Empty<string>());
    public static Result Failure(string error) => new(false, new[] { error });
    public static Result Failure(IEnumerable<string> errors) => new(false, new List<string>(errors));
}

/// <summary>
/// Generic result with a value for successful outcomes.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<string> Errors { get; }
    public T? Value { get; }

    private Result(bool isSuccess, T? value, IReadOnlyList<string> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors ?? Array.Empty<string>();
    }

    public static Result<T> Success(T value) => new(true, value, Array.Empty<string>());
    public static Result<T> Failure(string error) => new(false, default, new[] { error });
    public static Result<T> Failure(IEnumerable<string> errors) => new(false, default, new List<string>(errors));
}
