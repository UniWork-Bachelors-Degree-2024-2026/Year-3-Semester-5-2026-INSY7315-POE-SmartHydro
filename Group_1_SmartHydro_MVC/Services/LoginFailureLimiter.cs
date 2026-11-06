namespace Group_1_SmartHydro_MVC.Services
{
    // Whether a sign-in may go ahead, and when it may if it may not
    public sealed record LoginAttemptCheck(bool Allowed, TimeSpan RetryAfter);

    // Counts only failed sign-ins (a wrong password or a wrong code).
    // A person is limited by IP address and email together, and an IP address is also limited as a whole, so one person's
    // mistakes do not lock out the others on the same connection. Failures expire 15 minutes after they happened.
    public sealed class LoginFailureLimiter
    {
        public const int FailuresPerPerson = 5;
        public const int FailuresPerAddress = 20;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        private readonly Func<DateTime> _now;
        private readonly object _lock = new();
        private readonly Dictionary<string, List<DateTime>> _failures = new();

        public LoginFailureLimiter() : this(() => DateTime.UtcNow)
        {
        }

        // The clock is passed in so a test can move time forward without waiting
        public LoginFailureLimiter(Func<DateTime> now)
        {
            _now = now;
        }

        // Says whether this person may try now. When not, it says how long until their oldest failure falls out of the window
        public LoginAttemptCheck Check(string ip, string email)
        {
            lock (_lock)
            {
                var now = _now();
                var person = Recent(PersonKey(ip, email), now, create: false);
                var address = Recent(AddressKey(ip), now, create: false);

                if (person.Count >= FailuresPerPerson)
                    return new LoginAttemptCheck(false, WaitUntilFree(person, now));

                if (address.Count >= FailuresPerAddress)
                    return new LoginAttemptCheck(false, WaitUntilFree(address, now));

                return new LoginAttemptCheck(true, TimeSpan.Zero);
            }
        }

        // Counts one failed sign-in against the person and against their IP address
        public void RecordFailure(string ip, string email)
        {
            lock (_lock)
            {
                var now = _now();
                Recent(PersonKey(ip, email), now, create: true).Add(now);
                Recent(AddressKey(ip), now, create: true).Add(now);
            }
        }

        // Forgets this person's failures once they have signed in correctly
        public void Reset(string ip, string email)
        {
            lock (_lock)
            {
                _failures.Remove(PersonKey(ip, email));
            }
        }

        private static string PersonKey(string ip, string email) => "person:" + ip + "|" + email;

        private static string AddressKey(string ip) => "address:" + ip;

        // The failures of a key that are still inside the window, dropping the older ones. A list is only created
        // when a failure is being recorded, and a key with none left is forgotten, so the table never grows without limit
        private List<DateTime> Recent(string key, DateTime now, bool create)
        {
            if (!_failures.TryGetValue(key, out var list))
            {
                if (!create)
                    return new List<DateTime>();

                list = new List<DateTime>();
                _failures[key] = list;
            }

            list.RemoveAll(time => now - time >= Window);

            if (list.Count == 0 && !create)
                _failures.Remove(key);

            return list;
        }

        // How long until the oldest failure in the list leaves the window, and never less than a second
        private static TimeSpan WaitUntilFree(List<DateTime> failures, DateTime now)
        {
            var wait = failures.Min() + Window - now;
            return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        }
    }
}
