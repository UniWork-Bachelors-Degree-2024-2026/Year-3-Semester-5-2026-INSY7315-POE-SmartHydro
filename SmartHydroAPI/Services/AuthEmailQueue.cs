using System.Threading.Channels;

namespace SmartHydroAPI.Services
{
    public enum AuthEmailJobKind
    {
        Register,
        ResendRegistration,
        PasswordReset
    }

    public sealed record AuthEmailJob(
        AuthEmailJobKind Kind,
        string Email,
        string VerificationTokenHash,
        string PasswordHash,
        string FarmName,
        string FullName);

    public sealed class AuthEmailQueue
    {
        private const int Capacity = 1000;

        private readonly Channel<AuthEmailJob> _channel = Channel.CreateBounded<AuthEmailJob>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });

        // Adds a job without waiting, returning false when the queue is full
        public bool TryEnqueue(AuthEmailJob job)
        {
            return _channel.Writer.TryWrite(job);
        }

        // Streams queued jobs to the background worker until the application stops
        public IAsyncEnumerable<AuthEmailJob> ReadAllAsync(CancellationToken ct)
        {
            return _channel.Reader.ReadAllAsync(ct);
        }
    }
}