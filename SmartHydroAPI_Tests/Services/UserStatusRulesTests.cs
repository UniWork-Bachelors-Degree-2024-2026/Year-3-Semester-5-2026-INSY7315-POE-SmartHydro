using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Tests.Services
{
    public class UserStatusRulesTests
    {
        // A protected account, such as the admin's own, should never offer any actions
        [Fact]
        public void AllowedActions_ProtectedAccount_ReturnsNothing()
        {
            var actions = UserStatusRules.AllowedActions(UserStatus.Approved, true);

            Assert.Empty(actions);
        }

        // An unknown status should offer no actions
        [Fact]
        public void AllowedActions_UnknownStatus_ReturnsNothing()
        {
            var actions = UserStatusRules.AllowedActions("Banana", false);

            Assert.Empty(actions);
        }

        // A pending account can be approved, denied or deleted
        [Fact]
        public void AllowedActions_Pending_AllowsApproveDenyDelete()
        {
            var actions = UserStatusRules.AllowedActions(UserStatus.PendingApproval, false);

            Assert.Equal(new[] { AccountActions.Approve, AccountActions.Deny, AccountActions.Delete }, actions);
        }

        // Each status should allow or block an action exactly as the rules table says
        [Theory]
        [InlineData(UserStatus.PendingApproval, AccountActions.Approve, true)]
        [InlineData(UserStatus.Approved, AccountActions.Deactivate, true)]
        [InlineData(UserStatus.Approved, AccountActions.Approve, false)]
        [InlineData(UserStatus.Disabled, AccountActions.Reactivate, true)]
        [InlineData(UserStatus.Disabled, AccountActions.Deny, false)]
        [InlineData(UserStatus.Denied, AccountActions.Reopen, true)]
        [InlineData(UserStatus.Denied, AccountActions.Deactivate, false)]
        public void IsAllowed_FollowsRulesTable(string status, string action, bool expected)
        {
            Assert.Equal(expected, UserStatusRules.IsAllowed(status, action));
        }

        // Each status-changing action should lead to the right next status
        [Theory]
        [InlineData(AccountActions.Approve, UserStatus.Approved)]
        [InlineData(AccountActions.Deny, UserStatus.Denied)]
        [InlineData(AccountActions.Deactivate, UserStatus.Disabled)]
        [InlineData(AccountActions.Reactivate, UserStatus.Approved)]
        [InlineData(AccountActions.Reopen, UserStatus.PendingApproval)]
        public void NextStatus_StatusChangingAction_ReturnsNewStatus(string action, string expected)
        {
            Assert.Equal(expected, UserStatusRules.NextStatus(action));
        }

        // Delete removes the account rather than moving it, so it has no next status
        [Fact]
        public void NextStatus_Delete_ReturnsNull()
        {
            Assert.Null(UserStatusRules.NextStatus(AccountActions.Delete));
        }

        // Status options should leave out delete because it does not lead to another status
        [Fact]
        public void StatusOptions_Approved_ExcludesDelete()
        {
            var options = UserStatusRules.StatusOptions(UserStatus.Approved, false);

            var option = Assert.Single(options);
            Assert.Equal(UserStatus.Disabled, option.Status);
            Assert.Equal(AccountActions.Deactivate, option.Action);
        }

        // Only approved accounts may sign in
        [Theory]
        [InlineData(UserStatus.Approved, true)]
        [InlineData(UserStatus.PendingApproval, false)]
        [InlineData(UserStatus.Disabled, false)]
        [InlineData(UserStatus.Denied, false)]
        public void CanSignIn_OnlyApproved(string status, bool expected)
        {
            Assert.Equal(expected, UserStatusRules.CanSignIn(status));
        }

        // The refusal message should use readable wording for both the status and the action
        [Theory]
        [InlineData(UserStatus.PendingApproval, AccountActions.Deactivate, "This account is pending approval, so it cannot be deactivated.")]
        [InlineData(UserStatus.Approved, AccountActions.Deny, "This account is approved, so it cannot be denied.")]
        [InlineData(UserStatus.Approved, AccountActions.Reopen, "This account is approved, so it cannot be moved back to pending.")]
        public void DescribeRefusal_UsesReadableWording(string status, string action, string expected)
        {
            Assert.Equal(expected, UserStatusRules.DescribeRefusal(status, action));
        }
    }
}