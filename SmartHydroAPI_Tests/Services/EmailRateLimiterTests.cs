using SmartHydroAPI.Services;

namespace SmartHydroAPI.Tests.Services
{
    public class EmailRateLimiterTests
    {
        // Waits under a minute should be shown in whole seconds, rounded up, never less than one second
        [Theory]
        [InlineData(0, "1 second")]
        [InlineData(0.4, "1 second")]
        [InlineData(1, "1 second")]
        [InlineData(1.2, "2 seconds")]
        [InlineData(45, "45 seconds")]
        [InlineData(59, "59 seconds")]
        public void DescribeWait_UnderAMinute_ShowsSeconds(double seconds, string expected)
        {
            Assert.Equal(expected, EmailRateLimiter.DescribeWait(TimeSpan.FromSeconds(seconds)));
        }

        // Waits of a minute or more should be shown in whole minutes, rounded up
        [Theory]
        [InlineData(60, "1 minute")]
        [InlineData(61, "2 minutes")]
        [InlineData(600, "10 minutes")]
        [InlineData(899, "15 minutes")]
        public void DescribeWait_AMinuteOrMore_ShowsMinutes(double seconds, string expected)
        {
            Assert.Equal(expected, EmailRateLimiter.DescribeWait(TimeSpan.FromSeconds(seconds)));
        }
    }
}