using NextResta.Infrastructure.Services;

namespace NextResta.UnitTests.Infrastructure.EmailValidator;

/// <summary>
/// Integration tests for EmailValidator - Production Integration Testing
/// Tests real-world scenarios, performance benchmarks, and system integration
/// </summary>
public class EmailValidatorIntegrationTests
{
    #region Real-World Email Datasets

    [Theory]
    [InlineData("john.doe@gmail.com")]
    [InlineData("jane.smith@outlook.com")]
    [InlineData("bob.johnson@yahoo.com")]
    [InlineData("alice.brown@hotmail.com")]
    [InlineData("charlie.wilson@protonmail.com")]
    [InlineData("diana.miller@icloud.com")]
    [InlineData("edward.davis@aol.com")]
    [InlineData("fiona.garcia@zoho.com")]
    public void IsValidEmail_PopularEmailProviders_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("support@microsoft.com")]
    [InlineData("info@google.com")]
    [InlineData("contact@apple.com")]
    [InlineData("help@amazon.com")]
    [InlineData("service@facebook.com")]
    [InlineData("admin@twitter.com")]
    [InlineData("team@linkedin.com")]
    [InlineData("noreply@github.com")]
    public void IsValidEmail_CorporateEmails_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("student@harvard.edu")]
    [InlineData("professor@mit.edu")]
    [InlineData("admin@stanford.edu")]
    [InlineData("research@berkeley.edu")]
    [InlineData("info@oxford.ac.uk")]
    [InlineData("contact@cambridge.ac.uk")]
    [InlineData("support@sorbonne.fr")]
    [InlineData("help@tu-berlin.de")]
    public void IsValidEmail_EducationalEmails_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Government and Organization Emails

    [Theory]
    [InlineData("info@whitehouse.gov")]
    [InlineData("contact@nasa.gov")]
    [InlineData("support@irs.gov")]
    [InlineData("help@fbi.gov")]
    [InlineData("admin@cdc.gov")]
    [InlineData("service@usps.gov")]
    [InlineData("team@treasury.gov")]
    [InlineData("office@state.gov")]
    public void IsValidEmail_GovernmentEmails_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("info@redcross.org")]
    [InlineData("contact@unicef.org")]
    [InlineData("support@who.int")]
    [InlineData("help@un.org")]
    [InlineData("admin@worldbank.org")]
    [InlineData("service@imf.org")]
    [InlineData("team@wto.org")]
    [InlineData("office@nato.int")]
    public void IsValidEmail_InternationalOrganizations_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Modern Startup and Tech Company Emails

    [Theory]
    [InlineData("founders@startup.app")]
    [InlineData("team@techcompany.dev")]
    [InlineData("api@service.tech")]
    [InlineData("support@platform.cloud")]
    [InlineData("hello@agency.digital")]
    [InlineData("contact@studio.design")]
    [InlineData("info@business.online")]
    [InlineData("admin@company.ai")]
    public void IsValidEmail_ModernTechEmails_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Comprehensive Disposable Email Detection

    [Theory]
    [InlineData("test@tempmail.org")]
    [InlineData("user@10minutemail.com")]
    [InlineData("fake@guerrillamail.com")]
    [InlineData("spam@mailinator.com")]
    [InlineData("temp@yopmail.com")]
    [InlineData("throwaway@throwaway.email")]
    [InlineData("disposable@temp-mail.org")]
    [InlineData("junk@maildrop.cc")]
    [InlineData("temporary@sharklasers.com")]
    [InlineData("fake@guerrillamailblock.com")]
    [InlineData("spam@pokemail.net")]
    [InlineData("junk@spam4.me")]
    [InlineData("temp@mohmal.com")]
    [InlineData("fake@emailondeck.com")]
    [InlineData("disposable@fakeinbox.com")]
    [InlineData("temporary@trashmail.com")]
    [InlineData("junk@dispostable.com")]
    [InlineData("spam@tempail.com")]
    [InlineData("fake@getnada.com")]
    [InlineData("temp@armyspy.com")]
    public void IsValidEmail_KnownDisposableEmails_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Performance Benchmarks

    [Fact]
    public void IsValidEmail_SingleValidation_ShouldCompleteQuickly()
    {
        // Arrange
        var email = "user@example.com";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);
        stopwatch.Stop();

        // Assert
        Assert.True(result);
        Assert.True(stopwatch.ElapsedMilliseconds < 10, $"Single validation took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");
    }

    [Fact]
    public void IsValidEmail_BulkValidation_ShouldMaintainPerformance()
    {
        // Arrange
        var emails = GenerateTestEmails(1000);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var results = emails.Select(email => NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email)).ToList();
        stopwatch.Stop();

        // Assert
        Assert.Equal(1000, results.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Bulk validation took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    [Fact]
    public void ValidateEmail_DetailedValidation_ShouldMaintainPerformance()
    {
        // Arrange
        var emails = GenerateTestEmails(500);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var results = emails.Select(email => NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email)).ToList();
        stopwatch.Stop();

        // Assert
        Assert.Equal(500, results.Count);
        Assert.All(results, result => Assert.NotNull(result));
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Detailed validation took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    #endregion

    #region Memory Usage Tests

    [Fact]
    public void IsValidEmail_RepeatedValidations_ShouldNotLeakMemory()
    {
        // Arrange
        var email = "user@example.com";
        var initialMemory = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < 10000; i++)
        {
            NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        Assert.True(memoryIncrease < 1024 * 1024, $"Memory increased by {memoryIncrease} bytes, expected < 1MB");
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public void IsValidEmail_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var emails = GenerateTestEmails(100);
        var tasks = new List<Task<bool>>();

        // Act
        foreach (var email in emails)
        {
            tasks.Add(Task.Run(() => NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email)));
        }

        var results = Task.WhenAll(tasks).Result;

        // Assert
        Assert.Equal(100, results.Length);
        Assert.All(results, result => Assert.IsType<bool>(result));
    }

    [Fact]
    public void ValidateEmail_ConcurrentDetailedValidation_ShouldBeThreadSafe()
    {
        // Arrange
        var emails = GenerateTestEmails(50);
        var tasks = new List<Task<EmailValidationResult>>();

        // Act
        foreach (var email in emails)
        {
            tasks.Add(Task.Run(() => NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email)));
        }

        var results = Task.WhenAll(tasks).Result;

        // Assert
        Assert.Equal(50, results.Length);
        Assert.All(results, result => 
        {
            Assert.NotNull(result);
            Assert.IsType<EmailValidationResult>(result);
        });
    }

    #endregion

    #region Edge Case Integration

    [Theory]
    [InlineData("user+tag+subtag@domain.com")]
    [InlineData("very.long.local.part.with.many.dots@domain.com")]
    [InlineData("user123456789@sub.domain.example.com")]
    [InlineData("test-email-address@hyphenated-domain.co")]
    [InlineData("numbers123@domain123.net")]
    public void IsValidEmail_ComplexValidFormats_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("user@domain..com")]
    [InlineData("user@.domain.com")]
    [InlineData("user@domain.com.")]
    [InlineData("user@-domain.com")]
    [InlineData("user@domain-.com")]
    [InlineData("user@@domain.com")]
    [InlineData("@domain.com")]
    [InlineData("user@")]
    public void IsValidEmail_MalformedDomains_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Helper Methods

    private static List<string> GenerateTestEmails(int count)
    {
        var emails = new List<string>();
        var domains = new[] { "example.com", "test.org", "domain.net", "service.co", "business.io" };
        var tlds = new[] { "com", "org", "net", "co", "io", "app", "dev", "tech" };

        for (int i = 0; i < count; i++)
        {
            var localPart = $"user{i}";
            var domain = domains[i % domains.Length];
            
            if (i % 10 == 0)
            {
                // Add some plus addressing
                localPart += $"+tag{i}";
            }
            
            if (i % 15 == 0)
            {
                // Add some modern TLDs
                var modernDomain = $"company{i}.{tlds[i % tlds.Length]}";
                emails.Add($"{localPart}@{modernDomain}");
            }
            else
            {
                emails.Add($"{localPart}@{domain}");
            }
        }

        return emails;
    }

    #endregion

    #region Regression Tests

    [Fact]
    public void IsValidEmail_PreviouslyFailingCases_ShouldNowPass()
    {
        // These are emails that might have failed in previous versions
        var previouslyProblematicEmails = new[]
        {
            "user@domain.app",
            "admin@company.dev", 
            "test@service.tech",
            "api@platform.cloud",
            "hello@startup.ai"
        };

        foreach (var email in previouslyProblematicEmails)
        {
            var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);
            Assert.True(result, $"Email {email} should be valid but was rejected");
        }
    }

    [Fact]
    public void ValidateEmail_ErrorMessageConsistency_ShouldReturnConsistentMessages()
    {
        // Arrange & Act
        var emptyResult = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail("");
        var tooLongResult = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(new string('a', 300) + "@domain.com");
        var invalidFormatResult = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail("invalid-format");
        var reservedTldResult = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail("user@domain.con");
        var disposableResult = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail("user@10minutemail.com");

        // Assert
        Assert.Equal("Email address is required", emptyResult.ErrorMessage);
        Assert.Equal("Email address is too long (maximum 254 characters)", tooLongResult.ErrorMessage);
        Assert.Equal("Email address format is invalid", invalidFormatResult.ErrorMessage);
        Assert.Equal("Email domain has an invalid or reserved TLD", reservedTldResult.ErrorMessage);
        Assert.Equal("Disposable email addresses are not allowed", disposableResult.ErrorMessage);
    }

    #endregion
}
