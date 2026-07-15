using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class GlobalExceptionHandlerTests
{
    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext Captured;
        public ValueTask WriteAsync(ProblemDetailsContext context) { Captured = context; return ValueTask.CompletedTask; }
        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context) { Captured = context; return ValueTask.FromResult(true); }
    }

    private sealed class StubEnvironment(string env) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = env;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static (GlobalExceptionHandler Handler, CapturingProblemDetailsService Problem) Build(string env)
    {
        var problem = new CapturingProblemDetailsService();
        var handler = new GlobalExceptionHandler(problem, new StubEnvironment(env), NullLogger<GlobalExceptionHandler>.Instance);
        return (handler, problem);
    }

    [Fact]
    public async Task Client_abort_returns_499_without_writing_a_body()
    {
        var (handler, problem) = Build("Production");
        var ctx = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        var handled = await handler.TryHandleAsync(ctx, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(499);
        problem.Captured.Should().BeNull();
    }

    [Fact]
    public async Task Unexpected_exception_is_500_and_hides_detail_in_production()
    {
        var (handler, problem) = Build("Production");
        var ctx = new DefaultHttpContext();

        await handler.TryHandleAsync(ctx, new InvalidOperationException("secret db string"), CancellationToken.None);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Captured!.ProblemDetails.Status.Should().Be(500);
        problem.Captured.ProblemDetails.Detail.Should().Be("An unexpected error occurred.");
        problem.Captured.ProblemDetails.Detail.Should().NotContain("secret");
    }

    [Fact]
    public async Task Unexpected_exception_surfaces_detail_outside_production()
    {
        var (handler, problem) = Build("Development");
        var ctx = new DefaultHttpContext();

        await handler.TryHandleAsync(ctx, new InvalidOperationException("boom"), CancellationToken.None);

        ctx.Response.StatusCode.Should().Be(500);
        problem.Captured!.ProblemDetails.Detail.Should().Be("boom");
    }

    [Fact]
    public async Task Bad_http_request_maps_to_its_own_status()
    {
        var (handler, problem) = Build("Production");
        var ctx = new DefaultHttpContext();

        await handler.TryHandleAsync(ctx, new BadHttpRequestException("malformed", StatusCodes.Status400BadRequest), CancellationToken.None);

        ctx.Response.StatusCode.Should().Be(400);
        problem.Captured!.ProblemDetails.Status.Should().Be(400);
        problem.Captured.ProblemDetails.Title.Should().Be("Bad Request");
        problem.Captured.ProblemDetails.Detail.Should().Be("malformed"); // client fault detail is safe to echo
    }
}
