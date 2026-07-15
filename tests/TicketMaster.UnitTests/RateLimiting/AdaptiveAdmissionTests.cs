using FluentAssertions;
using YC.BuildingBlocks.RateLimiting;
using Xunit;

namespace TicketMaster.UnitTests.RateLimiting;

/// <summary>
/// Layer-2 adaptive admission control: the AIMD step function, load-signal normalization, and the in-process
/// concurrency gate — all exercised without the background timer or a real process/Redis.
/// </summary>
public class AdaptiveAdmissionTests
{
    private static AdaptiveOptions Opts(int min = 10, int max = 1000, int step = 10,
        double healthy = 0.70, double overload = 0.85) => new()
    {
        MinLimit = min,
        MaxLimit = max,
        Step = step,
        HealthyThreshold = healthy,
        OverloadThreshold = overload
    };

    // --- AIMD step function -------------------------------------------------

    [Fact]
    public void Healthy_score_adds_step()
    {
        Aimd.NextLimit(current: 100, score: 0.10, Opts()).Should().Be(110); // < HealthyThreshold ⇒ +Step
    }

    [Fact]
    public void Overloaded_score_decreases()
    {
        Aimd.NextLimit(current: 100, score: 0.95, Opts()).Should().Be(70); // > OverloadThreshold ⇒ ×0.7
    }

    [Fact]
    public void Middle_score_holds()
    {
        Aimd.NextLimit(current: 100, score: 0.80, Opts()).Should().Be(100); // between thresholds ⇒ unchanged
    }

    [Fact]
    public void Increase_is_clamped_to_max()
    {
        Aimd.NextLimit(current: 995, score: 0.0, Opts(max: 1000, step: 10)).Should().Be(1000);
    }

    [Fact]
    public void Decrease_is_clamped_to_min()
    {
        Aimd.NextLimit(current: 12, score: 1.0, Opts(min: 10)).Should().Be(10); // 12×0.7=8, clamped up to Min
    }

    [Fact]
    public void Sequence_of_scores_drives_additive_increase_then_decrease()
    {
        var o = Opts(min: 10, max: 1000, step: 10);
        var limit = 100;

        // Three healthy ticks: additive increase.
        foreach (var _ in Enumerable.Range(0, 3))
            limit = Aimd.NextLimit(limit, 0.1, o);
        limit.Should().Be(130);

        // One overloaded tick: multiplicative decrease ×0.7.
        limit = Aimd.NextLimit(limit, 0.99, o);
        limit.Should().Be(91);

        // A middle tick: hold.
        limit = Aimd.NextLimit(limit, 0.78, o);
        limit.Should().Be(91);
    }

    // --- Load signal normalization ------------------------------------------

    [Fact]
    public void ThreadPool_normalizes_against_cores_times_100()
    {
        LoadMath.NormalizeThreadPool(pendingWorkItems: 0, cores: 4).Should().Be(0);
        LoadMath.NormalizeThreadPool(pendingWorkItems: 200, cores: 4).Should().Be(0.5); // 200 / (4*100)
        LoadMath.NormalizeThreadPool(pendingWorkItems: 10_000, cores: 4).Should().Be(1); // clamped
    }

    [Fact]
    public void Latency_normalizes_as_ratio_to_baseline_clamped()
    {
        LoadMath.NormalizeLatency(ewmaMs: 0, baselineMs: 200).Should().Be(0);
        LoadMath.NormalizeLatency(ewmaMs: 200, baselineMs: 200).Should().Be(0.5); // baseline ⇒ half-saturated
        LoadMath.NormalizeLatency(ewmaMs: 400, baselineMs: 200).Should().Be(1);   // 2× baseline ⇒ saturated
        LoadMath.NormalizeLatency(ewmaMs: 999, baselineMs: 200).Should().Be(1);   // clamped
    }

    [Fact]
    public void Composite_is_weighted_average_clamped()
    {
        var o = Opts(); // all weights = 1
        LoadMath.Composite(cpu: 0.2, threadPool: 0.4, latency: 0.6, error: 0.8, o)
            .Should().BeApproximately(0.5, 1e-9); // mean of the four

        // Weighting: error dominates.
        var errHeavy = new AdaptiveOptions { CpuWeight = 0, ThreadPoolWeight = 0, LatencyWeight = 0, ErrorWeight = 1 };
        LoadMath.Composite(0, 0, 0, error: 0.9, errHeavy).Should().Be(0.9);
    }

    // --- In-process admission gate ------------------------------------------

    [Fact]
    public void Gate_admits_up_to_current_then_rejects_then_frees_on_dispose()
    {
        var limit = new AdmissionLimit(2);
        var gate = new InProcAdmissionGate(limit);

        gate.TryEnter(out var lease1).Should().BeTrue();  // 1 in-flight
        gate.TryEnter(out var lease2).Should().BeTrue();  // 2 in-flight (== limit)
        gate.TryEnter(out _).Should().BeFalse();          // 3rd exceeds the limit

        lease1.Dispose();                                  // frees a slot
        gate.TryEnter(out var lease3).Should().BeTrue();   // now admits again

        lease2.Dispose();
        lease3.Dispose();
    }

    [Fact]
    public void Gate_lease_dispose_is_idempotent()
    {
        var limit = new AdmissionLimit(1);
        var gate = new InProcAdmissionGate(limit);

        gate.TryEnter(out var lease).Should().BeTrue();
        lease.Dispose();
        lease.Dispose(); // double dispose must not free a second (non-existent) slot

        // Only one slot should be free — a single enter succeeds, and the limit still caps at 1.
        gate.TryEnter(out var lease2).Should().BeTrue();
        gate.TryEnter(out _).Should().BeFalse();
        lease2.Dispose();
    }
}
