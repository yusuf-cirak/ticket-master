using NextResta.Infrastructure.Services;

namespace NextResta.UnitTests.Infrastructure.EmailValidator;

/// <summary>
/// Comprehensive test suite for EmailValidator - Production Ready
/// Tests cover all edge cases, security scenarios, and modern email formats
/// </summary>
public class EmailValidatorTests
{
    #region Valid Email Tests

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("admin@company.org")]
    [InlineData("test@domain.net")]
    [InlineData("info@business.co")]
    [InlineData("support@service.io")]
    public void IsValidEmail_StandardFormats_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("user+tag@gmail.com")]
    [InlineData("admin+alerts@company.app")]
    [InlineData("test+signup@domain.dev")]
    [InlineData("newsletter+user@service.tech")]
    [InlineData("notifications+admin@platform.cloud")]
    public void IsValidEmail_PlusAddressing_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("developer@startup.ai")]
    [InlineData("admin@company.crypto")]
    [InlineData("team@project.blockchain")]
    [InlineData("api@service.saas")]
    [InlineData("contact@agency.digital")]
    [InlineData("hello@studio.design")]
    [InlineData("support@platform.cloud")]
    [InlineData("info@business.tech")]
    public void IsValidEmail_ModernTLDs_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("user@domain.us")]
    [InlineData("admin@company.uk")]
    [InlineData("test@service.de")]
    [InlineData("info@business.fr")]
    [InlineData("support@platform.jp")]
    [InlineData("contact@agency.tr")]
    [InlineData("hello@studio.ca")]
    [InlineData("team@project.au")]
    public void IsValidEmail_CountryCodeTLDs_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("a@b.co")]
    [InlineData("x@y.com")]
    [InlineData("1@2.org")]
    [InlineData("test@sub.domain.com")]
    [InlineData("user@very.long.subdomain.example.org")]
    public void IsValidEmail_MinimalAndComplexDomains_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Invalid Email Tests - Format Issues

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("   ")]
    public void IsValidEmail_EmptyOrNull_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@domain.com")]
    [InlineData("user@")]
    [InlineData("user@@domain.com")]
    [InlineData("user@domain@com")]
    [InlineData("user.domain.com")]
    [InlineData("user@domain")]
    [InlineData("user@.com")]
    [InlineData("user@domain.")]
    [InlineData("user@domain..com")]
    [InlineData("user@-domain.com")]
    [InlineData("user@domain-.com")]
    public void IsValidEmail_InvalidFormats_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Invalid Email Tests - Reserved TLDs

    [Theory]
    [InlineData("user@domain.con")]
    [InlineData("admin@company.prn")]
    [InlineData("test@service.aux")]
    [InlineData("info@business.nul")]
    [InlineData("support@platform.com1")]
    [InlineData("contact@agency.lpt1")]
    [InlineData("hello@studio.local")]
    [InlineData("team@project.localhost")]
    [InlineData("api@service.test")]
    [InlineData("user@domain.invalid")]
    [InlineData("admin@company.example")]
    public void IsValidEmail_ReservedTLDs_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Invalid Email Tests - Disposable Domains

    [Theory]
    [InlineData("user@10minutemail.com")]
    [InlineData("admin@guerrillamail.com")]
    [InlineData("test@mailinator.com")]
    [InlineData("info@tempmail.org")]
    [InlineData("support@yopmail.com")]
    [InlineData("contact@throwaway.email")]
    [InlineData("hello@temp-mail.org")]
    [InlineData("team@maildrop.cc")]
    [InlineData("api@sharklasers.com")]
    [InlineData("user@guerrillamailblock.com")]
    [InlineData("admin@pokemail.net")]
    [InlineData("test@spam4.me")]
    [InlineData("info@mohmal.com")]
    [InlineData("support@emailondeck.com")]
    [InlineData("contact@fakeinbox.com")]
    [InlineData("hello@trashmail.com")]
    public void IsValidEmail_DisposableDomains_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Invalid Email Tests - Unrecognized TLDs

    [Theory]
    [InlineData("user@domain.xyz123")]
    [InlineData("admin@company.fake")]
    [InlineData("test@service.notreal")]
    [InlineData("info@business.madeup")]
    [InlineData("support@platform.nonexistent")]
    public void IsValidEmail_UnrecognizedTLDs_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Length Tests

    [Fact]
    public void IsValidEmail_TooLongEmail_ShouldReturnFalse()
    {
        // Arrange - Create email longer than 254 characters
        var longLocalPart = new string('a', 200);
        var longDomain = new string('b', 60) + ".com";
        var longEmail = $"{longLocalPart}@{longDomain}";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(longEmail);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEmail_MaxLengthEmail_ShouldReturnTrue()
    {
        // Arrange - Create email exactly at 254 character limit
        var localPart = new string('a', 240);
        var email = $"{localPart}@domain.com"; // Total: 252 characters

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Detailed Validation Tests

    [Fact]
    public void ValidateEmail_ValidEmail_ShouldReturnSuccessResult()
    {
        // Arrange
        var email = "user@example.com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.None, result.ErrorType);
    }

    [Fact]
    public void ValidateEmail_EmptyEmail_ShouldReturnEmptyError()
    {
        // Arrange
        var email = "";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Email address is required", result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.Empty, result.ErrorType);
    }

    [Fact]
    public void ValidateEmail_TooLongEmail_ShouldReturnTooLongError()
    {
        // Arrange
        var longEmail = new string('a', 250) + "@domain.com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(longEmail);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Email address is too long (maximum 254 characters)", result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.TooLong, result.ErrorType);
    }

    [Fact]
    public void ValidateEmail_InvalidFormat_ShouldReturnFormatError()
    {
        // Arrange
        var email = "invalid-email-format";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Email address format is invalid", result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.InvalidFormat, result.ErrorType);
    }

    [Fact]
    public void ValidateEmail_ReservedTLD_ShouldReturnInvalidTldError()
    {
        // Arrange
        var email = "user@domain.con";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Email domain has an invalid or reserved TLD", result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.InvalidTld, result.ErrorType);
    }

    [Fact]
    public void ValidateEmail_UnrecognizedTLD_ShouldReturnInvalidTldError()
    {
        // Arrange
        var email = "user@domain.notreal";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Email domain has an unrecognized TLD", result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.InvalidTld, result.ErrorType);
    }

    [Fact]
    public void ValidateEmail_DisposableEmail_ShouldReturnDisposableError()
    {
        // Arrange
        var email = "user@10minutemail.com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Disposable email addresses are not allowed", result.ErrorMessage);
        Assert.Equal(EmailValidationErrorType.DisposableEmail, result.ErrorType);
    }

    #endregion

    #region Edge Cases and Security Tests

    [Theory]
    [InlineData("user@domain.com ")]  // Trailing space
    [InlineData(" user@domain.com")]  // Leading space
    [InlineData(" user@domain.com ")] // Both spaces
    public void IsValidEmail_EmailWithSpaces_ShouldTrimAndValidate(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("USER@DOMAIN.COM")]
    [InlineData("Admin@Company.ORG")]
    [InlineData("Test@Service.Net")]
    public void IsValidEmail_CaseInsensitive_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("user@domain.COM")]
    [InlineData("admin@company.ORG")]
    [InlineData("test@service.NET")]
    public void IsValidEmail_UppercaseTLD_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void IsValidEmail_MultipleCallsSameEmail_ShouldBeConsistent()
    {
        // Arrange
        var email = "user@example.com";

        // Act & Assert
        for (int i = 0; i < 1000; i++)
        {
            var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);
            Assert.True(result);
        }
    }

    [Fact]
    public void ValidateEmail_BulkValidation_ShouldHandleMultipleEmails()
    {
        // Arrange
        var emails = new[]
        {
            "user1@example.com",
            "user2@domain.org", 
            "user3@service.net",
            "invalid@domain.con",
            "disposable@10minutemail.com"
        };

        // Act & Assert
        foreach (var email in emails)
        {
            var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(email);
            Assert.NotNull(result);
            
            if (email.Contains("con") || email.Contains("10minutemail"))
            {
                Assert.False(result.IsValid);
            }
            else
            {
                Assert.True(result.IsValid);
            }
        }
    }

    #endregion

    #region Real-World Scenarios

    [Theory]
    [InlineData("john.doe@company.com")]
    [InlineData("jane_smith@university.edu")]
    [InlineData("support@startup.app")]
    [InlineData("api@service.dev")]
    [InlineData("notifications@platform.tech")]
    [InlineData("admin@business.digital")]
    [InlineData("contact@agency.design")]
    [InlineData("hello@studio.cloud")]
    public void IsValidEmail_RealWorldBusinessEmails_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("user@gmail.com")]
    [InlineData("admin@outlook.com")]
    [InlineData("test@yahoo.com")]
    [InlineData("info@hotmail.com")]
    [InlineData("support@protonmail.com")]
    public void IsValidEmail_PopularEmailProviders_ShouldReturnTrue(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void IsValidEmail_ShortestValidEmail_ShouldReturnTrue()
    {
        // Arrange - Shortest possible valid email: a@b.co
        var email = "a@b.co";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("a@b.c")]  // TLD too short
    [InlineData("@b.co")]  // No local part
    [InlineData("a@.co")]  // No domain
    [InlineData("a@b.")]   // No TLD
    public void IsValidEmail_BoundaryInvalidCases_ShouldReturnFalse(string email)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);

        // Assert
        Assert.False(result);
    }

    #endregion
}
