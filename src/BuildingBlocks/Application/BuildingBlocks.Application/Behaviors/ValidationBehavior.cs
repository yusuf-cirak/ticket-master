using System.Reflection;
using FluentValidation;
using MediatR;
using BuildingBlocks.Domain.Errors;
using YC.Monad;

namespace BuildingBlocks.Application.Behaviors;

/// <summary>
/// Boundary input validation as a MediatR pipeline step: before a request reaches its handler, every
/// <see cref="IValidator{T}"/> registered for that request type runs. On failure the behavior does NOT throw —
/// it short-circuits by returning a failed <c>Result</c>/<c>Result&lt;T&gt;</c> carrying a <see cref="ValidationError"/>
/// (field → messages), which the endpoint result mapper renders as one consistent RFC 9457 400. Requests without a
/// validator pass straight through (zero-cost). Handlers therefore assume a valid request and never see a
/// <c>ValidationException</c>.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToArray();

        if (failures.Length == 0)
            return await next();

        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

        return ResultFailureFactory<TResponse>.Create(new ValidationError(errors));
    }
}

/// <summary>
/// Builds a failed <typeparamref name="TResponse"/> from a <see cref="Error"/> when the response is a
/// <c>Result</c> or <c>Result&lt;T&gt;</c>. The construction strategy is resolved once per closed
/// <typeparamref name="TResponse"/> and cached in the static field.
/// </summary>
internal static class ResultFailureFactory<TResponse>
{
    private static readonly Func<Error, TResponse> Factory = Build();

    public static TResponse Create(Error error) => Factory(error);

    private static Func<Error, TResponse> Build()
    {
        var type = typeof(TResponse);

        if (type == typeof(Result))
            return error => (TResponse)(object)Result.Failure(error);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failure = type.GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static, [typeof(Error)])
                ?? throw new InvalidOperationException($"Result<> is missing a static Failure(Error) method for {type}.");
            return error => (TResponse)failure.Invoke(null, [error])!;
        }

        return _ => throw new InvalidOperationException(
            $"ValidationBehavior requires the response to be Result or Result<T>, but the handler for this request returns {type}. " +
            "Validated requests must return a Result type so validation failures flow through the Result channel instead of an exception.");
    }
}
