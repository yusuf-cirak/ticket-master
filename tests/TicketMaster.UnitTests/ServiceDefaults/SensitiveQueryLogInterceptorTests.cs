using YC.BuildingBlocks.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class SensitiveQueryLogInterceptorTests
{
    private static IQueryCollection Query(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, StringValues>();
        foreach (var (key, value) in pairs)
            dict[key] = value;
        return new QueryCollection(dict);
    }

    [Fact]
    public void Redacts_sensitive_parameter_values_keeps_others()
    {
        var result = SensitiveQueryLogInterceptor.Redact(
            Query(("page", "2"), ("access_token", "abc.def"), ("filter", "active"), ("sig", "ZZ")));

        result.Should().Contain("page=2").And.Contain("filter=active");
        result.Should().Contain("access_token=***").And.Contain("sig=***");
        result.Should().NotContain("abc.def").And.NotContain("ZZ");
    }

    [Fact]
    public void Redaction_is_case_insensitive_on_parameter_name()
    {
        var result = SensitiveQueryLogInterceptor.Redact(Query(("Api_Key", "live-123")));

        result.Should().Be("Api_Key=***");
    }

    [Fact]
    public void Empty_query_yields_empty_string() =>
        SensitiveQueryLogInterceptor.Redact(Query()).Should().BeEmpty();
}
