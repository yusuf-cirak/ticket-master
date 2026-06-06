using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;
using TicketMaster.Modules.Platform.Contracts;

namespace TicketMaster.Modules.Platform;

public static class PingEndpoint
{
    private const int MaxNoteLength = 500;

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/platform").WithTags("Platform");
        group.MapPost("/ping",
            async (PingRequest body, PlatformDbContext db, IOutboxWriter outbox, CancellationToken ct) =>
            {
                // Validate at the boundary before anything reaches the outbox payload.
                if (string.IsNullOrWhiteSpace(body.Note))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["note"] = ["Note is required."]
                    });
                if (body.Note.Length > MaxNoteLength)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["note"] = [$"Note must be at most {MaxNoteLength} characters."]
                    });

                var aggregateId = Guid.CreateVersion7().ToString();
                outbox.Enqueue(new PingIntegrationEvent(body.Note), aggregateType: "platform",
                    aggregateId: aggregateId);
                await db.SaveChangesAsync(ct); // single tx: here only the outbox row; real modules also persist state
                return Results.Accepted($"/ping/{aggregateId}", new { aggregateId });
            })
            .AllowAnonymous(); // diagnostic endpoint — intentionally open

        return app;
    }
}

public sealed record PingRequest(string Note);