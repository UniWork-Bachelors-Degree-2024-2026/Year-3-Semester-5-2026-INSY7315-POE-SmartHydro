using SmartHydroAPI.Services;

namespace SmartHydroAPI.Tests.Services
{
    public class PasswordHashingTests
    {
        // A new hash should use the PBKDF2-SHA256$iterations$salt$hash format with 100000 iterations
        [Fact]
        public void HashPassword_UsesExpectedFormat()
        {
            var hash = AuthenticationService.HashPassword("Str0ng!Passw0rd");

            var parts = hash.Split('$');
            Assert.Equal(4, parts.Length);
            Assert.Equal("PBKDF2-SHA256", parts[0]);
            Assert.Equal("100000", parts[1]);
            Assert.DoesNotContain("Str0ng!Passw0rd", hash);
        }

        // Hashing the same password twice should give different results because each gets its own salt
        [Fact]
        public void HashPassword_SamePasswordTwice_GivesDifferentHashes()
        {
            var first = AuthenticationService.HashPassword("Str0ng!Passw0rd");
            var second = AuthenticationService.HashPassword("Str0ng!Passw0rd");

            Assert.NotEqual(first, second);
        }

        // The correct password should verify against its own hash
        [Fact]
        public void VerifyPassword_CorrectPassword_ReturnsTrue()
        {
            var hash = AuthenticationService.HashPassword("Str0ng!Passw0rd");

            Assert.True(AuthenticationService.VerifyPassword("Str0ng!Passw0rd", hash));
        }

        // A wrong password, including one that differs only in letter case, should fail
        [Theory]
        [InlineData("str0ng!passw0rd")]
        [InlineData("Str0ng!Passw0rd ")]
        [InlineData("")]
        public void VerifyPassword_WrongPassword_ReturnsFalse(string attempt)
        {
            var hash = AuthenticationService.HashPassword("Str0ng!Passw0rd");

            Assert.False(AuthenticationService.VerifyPassword(attempt, hash));
        }

        // A damaged or unknown stored hash should fail safely instead of throwing
        [Theory]
        [InlineData("")]
        [InlineData("not-a-hash")]
        [InlineData("MD5$1$abc$def")]
        [InlineData("PBKDF2-SHA256$notanumber$AAAA$AAAA")]
        [InlineData("PBKDF2-SHA256$100000$!!!$###")]
        public void VerifyPassword_MalformedHash_ReturnsFalse(string storedHash)
        {
            Assert.False(AuthenticationService.VerifyPassword("Str0ng!Passw0rd", storedHash));
        }
    }
}