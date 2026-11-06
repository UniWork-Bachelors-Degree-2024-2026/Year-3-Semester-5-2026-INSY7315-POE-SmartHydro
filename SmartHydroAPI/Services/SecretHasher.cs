using System.Security.Cryptography;
using System.Text;

namespace SmartHydroAPI.Services
{
    public sealed class SecretHasher
    {
        private const string KeyPurpose = "smarthydro-one-time-secret:";

        private readonly byte[] _key;

        public SecretHasher(IConfiguration configuration)
        {
            var pepper = configuration["Security:HmacPepper"];

            if (string.IsNullOrWhiteSpace(pepper))
                throw new InvalidOperationException("Security:HmacPepper is not configured.");

            _key = SHA256.HashData(Encoding.UTF8.GetBytes(KeyPurpose + pepper));
        }

        // Hashes a one-time code or token with HMAC-SHA256 under a key derived from the server pepper, so a copy of the database alone cannot be used to work out a code
        public string Hash(string value)
        {
            var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value ?? string.Empty));
            return Convert.ToHexString(hash);
        }

        // Checks a supplied code or token against a stored hash in constant time
        public bool Matches(string value, string storedHash)
        {
            var expected = Encoding.UTF8.GetBytes(storedHash ?? string.Empty);
            var actual = Encoding.UTF8.GetBytes(Hash(value));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}