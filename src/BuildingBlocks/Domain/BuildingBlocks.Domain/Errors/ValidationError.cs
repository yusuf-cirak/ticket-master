using YC.Monad;

namespace BuildingBlocks.Domain.Errors;

/// <summary>
/// A <see cref="Error"/> that carries a field → messages map so a failed <c>Result</c>/<c>Result&lt;T&gt;</c>
/// can express multiple validation errors through the single Result error channel. The endpoint result mapper
/// recognizes this type and renders an RFC 9457 <c>ValidationProblemDetails</c> (HTTP 400) — one consistent
/// validation shape whether the failure comes from the pipeline validator or a domain factory.
/// </summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validation", "One or more validation errors occurred.", 400);
