using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Last-resort handler for exceptions that escape an endpoint. Every failure is returned as an RFC 9457
/// (problem+json) <see cref="ProblemDetails"/> with a stable <c>type</c>/<c>title</c>, a <c>traceId</c> and the
/// durable <c>correlation_id</c> (stamped for every problem response by the customizer in
/// <see cref="ExceptionHandlingExtensions"/>). Server faults (5xx) are logged with the exception and never leak
/// internal detail in Production; client faults (4xx) are logged as warnings. Domain logic uses the Result monad,
/// so reaching here generally means an unexpected fault — kept deliberately small, mapping only framework cases.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away mid-request: there is nothing to write and it is not a server fault. Stop the
        // pipeline with nginx's 499 ("client closed request") and don't pollute error logs.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request aborted by the client ({Path}).", httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusClientClosedRequest;
            return true;
        }

        // Headers already on the wire — we can't rewrite the status/body; let the server reset the connection.
        if (httpContext.Response.HasStarted)
        {
            logger.LogError(exception, "Unhandled exception after the response started; cannot write ProblemDetails.");
            return false;
        }

        // Boundary validation failures (from the MediatR ValidationBehavior) → RFC 9457 400 with a field→errors map.
        if (exception is ValidationException validation)
        {
            logger.LogWarning("Validation failed: {Count} error(s).", validation.Errors.Count());
            var errors = validation.Errors
                .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(f => f.ErrorMessage).Distinct().ToArray());

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                }
            });
        }

        var (status, title) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            logger.LogWarning(exception, "Request failed ({Status}): {Message}", status, exception.Message);

        httpContext.Response.StatusCode = status;

        // Hide internal detail for server faults in Production; surface it everywhere else for debuggability.
        var detail = status >= StatusCodes.Status500InternalServerError && environment.IsProduction()
            ? "An unexpected error occurred."
            : exception.Message;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
            // Type/Instance/traceId/correlation_id are filled by the ProblemDetails defaults + customizer.
        });
    }

    // nginx convention for a client that disconnected before the response (no standard HTTP code for it).
    private const int StatusClientClosedRequest = 499;

    private static (int Status, string Title) Map(Exception exception) => exception switch
    {
        BadHttpRequestException bad => (bad.StatusCode, "Bad Request"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };
}
