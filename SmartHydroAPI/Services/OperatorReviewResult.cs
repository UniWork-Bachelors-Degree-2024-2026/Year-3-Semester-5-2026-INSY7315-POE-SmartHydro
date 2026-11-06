namespace SmartHydroAPI.Services
{
    public enum OperatorReviewOutcome
    {
        Reviewed,
        NotFound
    }

    public sealed record OperatorReviewResult(OperatorReviewOutcome Outcome, int Id, string Email, string Status, bool NotificationSent)
    {
        // Builds the result returned when no operator awaiting approval matches the requested id
        public static OperatorReviewResult NotFound(int id) =>
            new(OperatorReviewOutcome.NotFound, id, string.Empty, string.Empty, false);

        // Builds the result returned once an operator has been approved or denied
        public static OperatorReviewResult Reviewed(int id, string email, string status, bool notificationSent) =>
            new(OperatorReviewOutcome.Reviewed, id, email, status, notificationSent);
    }
}