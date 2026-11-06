using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using SmartHydroAPI.Controllers;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;

namespace SmartHydroAPI.Tests.Services
{
    public class DeviceAccessTests
    {
        private const string OwnedMac = "AA:BB:CC:DD:EE:01";
        private const string OtherMac = "AA:BB:CC:DD:EE:02";
        private const string DeletedMac = "AA:BB:CC:DD:EE:03";

        // Seeds the test sites and puts one board on an owned structure, one on another operator's structure and one on a deleted structure
        private static SmartHydroAPI.Data.ApplicationDbContext SeededDatabase()
        {
            var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            database.Devices.Add(new Device { MacAddress = OwnedMac, StructureId = TestData.OwnedTowerId });
            database.Devices.Add(new Device { MacAddress = OtherMac, StructureId = TestData.OtherTowerId });
            database.Devices.Add(new Device { MacAddress = DeletedMac, StructureId = TestData.DeletedTowerId });
            database.SaveChanges();
            return database;
        }

        // A worker may switch and dose a board on a site they were assigned to
        [Fact]
        public async Task CanOperateDevice_WorkerOnAssignedSite_IsAllowed()
        {
            using var database = SeededDatabase();

            Assert.True(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Worker(), OwnedMac, default));
        }

        // The MAC address may arrive in lower case or without colons, as it does from the app and the boxes
        [Theory]
        [InlineData("aa:bb:cc:dd:ee:01")]
        [InlineData("AABBCCDDEE01")]
        public async Task CanOperateDevice_WorkerWithOtherMacSpelling_IsAllowed(string mac)
        {
            using var database = SeededDatabase();

            Assert.True(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Worker(), mac, default));
        }

        // A worker must not reach a board on a site they were not assigned to
        [Fact]
        public async Task CanOperateDevice_WorkerOnAnotherSite_IsRefused()
        {
            using var database = SeededDatabase();

            Assert.False(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Worker(), OtherMac, default));
        }

        // A worker with no site assigned must not reach any board
        [Fact]
        public async Task CanOperateDevice_WorkerWithNoSite_IsRefused()
        {
            using var database = SeededDatabase();
            var unassigned = TestData.User("new.worker@farm.co.za", UserRoles.Worker);

            Assert.False(await DeviceAccess.CanOperateDeviceAsync(database, unassigned, OwnedMac, default));
        }

        // A board on a deleted structure is out of reach for everyone but an admin who can still see it through the rule for admins
        [Fact]
        public async Task CanOperateDevice_BoardOnDeletedStructure_IsRefusedForWorkerAndOperator()
        {
            using var database = SeededDatabase();

            Assert.False(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Worker(), DeletedMac, default));
            Assert.False(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Operator(), DeletedMac, default));
        }

        // Operators keep exactly the access they had: their own boards only
        [Fact]
        public async Task CanOperateDevice_Operator_OwnBoardOnly()
        {
            using var database = SeededDatabase();

            Assert.True(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Operator(), OwnedMac, default));
            Assert.False(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Operator(), OtherMac, default));
        }

        // An admin may reach any board that is not on a deleted structure
        [Fact]
        public async Task CanOperateDevice_Admin_ReachesEveryActiveBoard()
        {
            using var database = SeededDatabase();

            Assert.True(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Admin(), OwnedMac, default));
            Assert.True(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Admin(), OtherMac, default));
            Assert.False(await DeviceAccess.CanOperateDeviceAsync(database, TestData.Admin(), DeletedMac, default));
        }

        // The rule for changing how a box connects is unchanged, so a worker still cannot use it
        [Fact]
        public async Task CanControlDevice_Worker_StaysRefused()
        {
            using var database = SeededDatabase();

            Assert.False(await DeviceAccess.CanControlDeviceAsync(database, TestData.Worker(), OwnedMac, default));
            Assert.True(await DeviceAccess.CanControlDeviceAsync(database, TestData.Operator(), OwnedMac, default));
        }

        // Switching relays and dosing are open to all three roles
        [Theory]
        [InlineData(typeof(ActuatorsController), nameof(ActuatorsController.SendContinuousCommand))]
        [InlineData(typeof(ActuatorsController), nameof(ActuatorsController.SendTimedCommand))]
        [InlineData(typeof(DosingController), nameof(DosingController.EnqueueCommand))]
        public void ControlEndpoints_AllowWorkers(Type controller, string action)
        {
            var roles = RolesOf(controller, action);

            Assert.Contains(UserRoles.Worker, roles);
            Assert.Contains(UserRoles.Operator, roles);
            Assert.Contains(UserRoles.Admin, roles);
        }

        // Changing how a box connects stays with admins and operators
        [Theory]
        [InlineData(nameof(DevicesController.RequestOfflineMode))]
        [InlineData(nameof(DevicesController.RequestOnlineMode))]
        [InlineData(nameof(DevicesController.RequestLoraMode))]
        public void ModeEndpoints_RefuseWorkers(string action)
        {
            var roles = RolesOf(typeof(DevicesController), action);

            Assert.DoesNotContain(UserRoles.Worker, roles);
            Assert.Contains(UserRoles.Operator, roles);
            Assert.Contains(UserRoles.Admin, roles);
        }

        // Changing healthy ranges also stays with admins and operators
        [Theory]
        [InlineData(nameof(ThresholdsController.UpsertThreshold))]
        [InlineData(nameof(ThresholdsController.Delete))]
        public void ThresholdEndpoints_RefuseWorkers(string action)
        {
            var roles = RolesOf(typeof(ThresholdsController), action);

            Assert.DoesNotContain(UserRoles.Worker, roles);
        }

        // Reads the roles an endpoint accepts from its Authorize attribute
        private static string[] RolesOf(Type controller, string action)
        {
            var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{controller.Name}.{action} was not found.");

            return method.GetCustomAttributes<AuthorizeAttribute>()
                .Where(a => a.Roles != null)
                .SelectMany(a => a.Roles!.Split(','))
                .Select(r => r.Trim())
                .ToArray();
        }
    }
}
