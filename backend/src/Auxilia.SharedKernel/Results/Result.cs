using System.Diagnostics.CodeAnalysis;

namespace Auxilia.SharedKernel.Results;

/// <summary>Outcome of an operation without a value. Managers return results instead of throwing for expected failures.</summary>
public class Result
{
    private readonly Error? _error;

    protected Result(Error? error) => _error = error;

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => _error is not null;

    /// <summary>The failure; <c>null</c> when <see cref="IsSuccess"/> is true.</summary>
    public Error? Error => _error;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<TValue> Success<TValue>(TValue value) => new(value, null);

    public static Result<TValue> Failure<TValue>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default, error);
    }

    public static implicit operator Result(Error error) => Failure(error);

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess() : onFailure(Error);
    }
}

/// <summary>Outcome of an operation that produces a <typeparamref name="TValue"/> on success.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value of a successful result. Reading it on a failure is a programming error.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result: {Error}");

    public static implicit operator Result<TValue>(TValue value) => Result.Success(value);

    public static implicit operator Result<TValue>(Error error) => Result.Failure<TValue>(error);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(Value) : onFailure(Error);
    }

    public Result<TOut> Map<TOut>(Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result.Success(map(Value)) : Result.Failure<TOut>(Error);
    }

    public async Task<Result<TOut>> BindAsync<TOut>(Func<TValue, Task<Result<TOut>>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? await next(Value).ConfigureAwait(false) : Result.Failure<TOut>(Error);
    }
}
