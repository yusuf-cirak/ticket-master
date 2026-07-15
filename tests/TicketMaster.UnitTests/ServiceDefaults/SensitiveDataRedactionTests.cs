using FluentAssertions;
using YC.BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class SensitiveDataRedactionTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("authorization")]
    [InlineData("api_key")]
    [InlineData("credit_card")]
    [InlineData("iban")]
    [InlineData("tc_kimlik")]
    [InlineData("email")]
    [InlineData("refresh_token")]
    public void Sensitive_keys_are_flagged(string key) =>
        SensitiveDataLogRecordProcessor.IsSensitive(key).Should().BeTrue();

    [Theory]
    [InlineData("PASSWORD")]
    [InlineData("Authorization")]
    [InlineData("Api_Key")]
    public void Matching_is_case_insensitive(string key) =>
        SensitiveDataLogRecordProcessor.IsSensitive(key).Should().BeTrue();

    [Theory]
    [InlineData("order_id")]
    [InlineData("status_code")]
    [InlineData("request_path")]
    [InlineData("correlation_id")]
    public void Non_sensitive_keys_pass_through(string key) =>
        SensitiveDataLogRecordProcessor.IsSensitive(key).Should().BeFalse();
}
