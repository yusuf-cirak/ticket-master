using YC.BuildingBlocks.Observability;
using FluentAssertions;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class LogSamplingTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(599)]
    public void Error_responses_always_flush_regardless_of_rate(int status) =>
        LogSamplingMiddleware.ShouldFlush(status, sampleRate: 0.0, roll: 0.99).Should().BeTrue();

    [Fact]
    public void Healthy_request_flushes_when_roll_is_below_rate() =>
        LogSamplingMiddleware.ShouldFlush(200, sampleRate: 0.2, roll: 0.1).Should().BeTrue();

    [Fact]
    public void Healthy_request_drops_when_roll_is_at_or_above_rate() =>
        LogSamplingMiddleware.ShouldFlush(200, sampleRate: 0.2, roll: 0.2).Should().BeFalse();

    [Fact]
    public void Rate_zero_drops_every_healthy_request() =>
        LogSamplingMiddleware.ShouldFlush(200, sampleRate: 0.0, roll: 0.0).Should().BeFalse();

    [Fact]
    public void Rate_one_keeps_every_request() =>
        LogSamplingMiddleware.ShouldFlush(200, sampleRate: 1.0, roll: 0.999).Should().BeTrue();

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(499)]
    public void Non_5xx_is_subject_to_sampling(int status) =>
        LogSamplingMiddleware.ShouldFlush(status, sampleRate: 0.0, roll: 0.0).Should().BeFalse();
}
