using Microsoft.AspNetCore.Http;
using BuildingBlocks.Domain.Errors;
using YC.Monad;

namespace BuildingBlocks.Web;

/// <summary>
/// Maps a YC.Monad <c>Result</c>/<c>Result&lt;T&gt;</c> to an ASP.NET Core <see cref="IResult"/> so every endpoint
/// funnels success and failure through one shape. Failures become RFC 9457 problem responses: a
/// <see cref="ValidationError"/> → <c>ValidationProblem</c> (400, field → messages); any other error → <c>Problem</c>
/// with the error's HTTP status (or 500 when unspecified) and the error code surfaced as an extension. The global
/// exception handler stamps traceId/correlation_id, so those need not be added here.
/// </summary>
public static class ResultEndpointExtensions
{
    /// <summary>Success → 200 with the value; failure → problem response.</summary>
    public static IResult ToApiResult<T>(this Result<T> result)
        => result.IsSuccess ? TypedResults.Ok(result.Value) : ToProblem(result.Error);

    /// <summary>Success → 200 (no body); failure → problem response.</summary>
    public static IResult ToApiResult(this Result result)
        => result.IsSuccess ? TypedResults.Ok() : ToProblem(result.Error);

    /// <summary>Success → 201 at <paramref name="location"/> with the value; failure → problem response.</summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, string location)
        => result.IsSuccess ? TypedResults.Created(location, result.Value) : ToProblem(result.Error);

    private static IResult ToProblem(Error error)
    {
        if (error is ValidationError validation)
            return TypedResults.ValidationProblem(validation.Errors.ToDictionary(e => e.Key, e => e.Value));

        var status = error.Status > 0 ? error.Status : StatusCodes.Status500InternalServerError;
        return TypedResults.Problem(
            detail: error.Message,
            statusCode: status,
            extensions: string.IsNullOrEmpty(error.Code) ? null : new Dictionary<string, object> { ["code"] = error.Code });
    }
}
