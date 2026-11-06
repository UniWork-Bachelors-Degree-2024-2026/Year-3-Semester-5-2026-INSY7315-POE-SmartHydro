using Microsoft.Extensions.Configuration;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Tests.Services
{
    public class SecretHasherTests
    {
        // Builds a hasher whose configuration holds the given pepper
        private static SecretHasher BuildHasher(string? pepper)
        {
            var settings = new Dictionary<string, string?> { ["Security:HmacPepper"] = pepper };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            return new SecretHasher(configuration);
        }

        // The hasher should refuse to start when no pepper is configured
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_MissingPepper_Throws(string? pepper)
        {
            Assert.Throws<InvalidOperationException>(() => BuildHasher(pepper));
        }

        // Hashing the same code twice should give the same 64-character hex value
        [Fact]
        public void Hash_SameInput_IsDeterministicHex()
        {
            var hasher = BuildHasher("test-pepper");

            var first = hasher.Hash("123456");
            var second = hasher.Hash("123456");

            Assert.Equal(first, second);
            Assert.Equal(64, first.Length);
            Assert.Matches("^[0-9A-F]+$", first);
        }

        // The hash should never be the plain code itself
        [Fact]
        public void Hash_DoesNotReturnPlainValue()
        {
            var hasher = BuildHasher("test-pepper");

            Assert.NotEqual("123456", hasher.Hash("123456"));
        }

        // Two servers with different peppers should produce different hashes for the same code
        [Fact]
        public void Hash_DifferentPepper_GivesDifferentHash()
        {
            var first = BuildHasher("pepper-one").Hash("123456");
            var second = BuildHasher("pepper-two").Hash("123456");

            Assert.NotEqual(first, second);
        }

        // The correct code should match its stored hash
        [Fact]
        public void Matches_CorrectCode_ReturnsTrue()
        {
            var hasher = BuildHasher("test-pepper");
            var stored = hasher.Hash("123456");

            Assert.True(hasher.Matches("123456", stored));
        }

        // A wrong code or a missing stored hash should not match
        [Theory]
        [InlineData("654321")]
        [InlineData("")]
        public void Matches_WrongCode_ReturnsFalse(string attempt)
        {
            var hasher = BuildHasher("test-pepper");
            var stored = hasher.Hash("123456");

            Assert.False(hasher.Matches(attempt, stored));
            Assert.False(hasher.Matches("123456", null!));
        }
    }
}