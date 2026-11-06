using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public static class AccountActions
    {
        public const string Approve = "approve";
        public const string Deny = "deny";
        public const string Deactivate = "deactivate";
        public const string Reactivate = "reactivate";
        public const string Delete = "delete";
        public const string Reopen = "reopen";
        public const string Edit = "edit";
    }

    public sealed record StatusOption(string Status, string Action);

    public static class UserStatusRules
    {
        private static readonly Dictionary<string, string[]> ActionsByStatus = new(StringComparer.Ordinal)
        {
            [UserStatus.PendingApproval] = new[] { AccountActions.Approve, AccountActions.Deny, AccountActions.Delete },
            [UserStatus.Approved] = new[] { AccountActions.Deactivate, AccountActions.Delete },
            [UserStatus.Disabled] = new[] { AccountActions.Reactivate, AccountActions.Delete },
            [UserStatus.Denied] = new[] { AccountActions.Approve, AccountActions.Reopen, AccountActions.Delete }
        };

        private static readonly Dictionary<string, string> StatusAfterAction = new(StringComparer.Ordinal)
        {
            [AccountActions.Approve] = UserStatus.Approved,
            [AccountActions.Deny] = UserStatus.Denied,
            [AccountActions.Deactivate] = UserStatus.Disabled,
            [AccountActions.Reactivate] = UserStatus.Approved,
            [AccountActions.Reopen] = UserStatus.PendingApproval
        };

        // Lists the admin actions allowed on an account in its current status; protected accounts (the admin themselves or a configuration account) get none
        public static IReadOnlyList<string> AllowedActions(string status, bool isProtected)
        {
            if (isProtected || !ActionsByStatus.TryGetValue(status, out var actions))
                return Array.Empty<string>();

            return actions;
        }

        // Lists the statuses an account can be moved to from its current status, each with the action that performs the move
        public static IReadOnlyList<StatusOption> StatusOptions(string status, bool isProtected)
        {
            var options = new List<StatusOption>();

            foreach (var action in AllowedActions(status, isProtected))
            {
                var next = NextStatus(action);

                if (next is not null)
                    options.Add(new StatusOption(next, action));
            }

            return options;
        }

        // Checks whether an admin action may be applied to an account in the given status
        public static bool IsAllowed(string status, string action)
        {
            return ActionsByStatus.TryGetValue(status, out var actions) && actions.Contains(action);
        }

        // Returns the status an account moves to after an action, or null for an action like delete that removes the account
        public static string? NextStatus(string action)
        {
            if (StatusAfterAction.TryGetValue(action, out var next))
                return next;

            return null;
        }

        // Only approved accounts may sign in or keep using an existing session
        public static bool CanSignIn(string status)
        {
            return status == UserStatus.Approved;
        }

        // Describes why an action is not possible for the account's current status, for the admin's error message
        public static string DescribeRefusal(string status, string action)
        {
            return $"This account is {Label(status)}, so it cannot be {PastTense(action)}.";
        }

        // Turns a stored status into the wording shown to people
        public static string Label(string status)
        {
            if (status == UserStatus.PendingApproval)
                return "pending approval";

            return status.ToLowerInvariant();
        }

        // Turns an action into the past-tense wording used in messages
        private static string PastTense(string action)
        {
            if (action == AccountActions.Deny)
                return "denied";

            if (action == AccountActions.Delete)
                return "deleted";

            if (action == AccountActions.Reopen)
                return "moved back to pending";

            return action + "d";
        }
    }
}