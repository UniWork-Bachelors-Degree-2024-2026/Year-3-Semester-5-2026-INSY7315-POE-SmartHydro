using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;

namespace SmartHydroAPI.Tests.Services
{
    public class ThresholdServiceTests
    {
        // An operator should get every active structure of their site, each filled with default ranges
        [Fact]
        public async Task GetForSiteAsync_OwnSite_ReturnsStructuresWithDefaults()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);

            var result = await service.GetForSiteAsync(TestData.OwnedSiteId, TestData.Operator());

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.DoesNotContain(result, s => s.StructureId == TestData.DeletedTowerId);
            var tower = Assert.Single(result, s => s.StructureId == TestData.OwnedTowerId);
            Assert.Equal(4, tower.Thresholds.Count);
            Assert.All(tower.Thresholds, t => Assert.True(t.IsDefault));
        }

        // Asking for another operator's site should return null rather than leak its ranges
        [Fact]
        public async Task GetForSiteAsync_OtherOperatorsSite_ReturnsNull()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);

            var result = await service.GetForSiteAsync(TestData.OtherSiteId, TestData.Operator());

            Assert.Null(result);
        }

        // A worker assigned to the site should be able to read its ranges
        [Fact]
        public async Task GetForSiteAsync_AssignedWorker_CanRead()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);

            var result = await service.GetForSiteAsync(TestData.OwnedSiteId, TestData.Worker());

            Assert.NotNull(result);
        }

        // A saved range should show as not default while the other readings stay on their defaults
        [Fact]
        public async Task GetForStructureAsync_SavedRange_IsMarkedNotDefault()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            database.Thresholds.Add(new Threshold { Id = 77, StructureId = TestData.OwnedTowerId, SensorType = "PH", MinValue = 6.0m, MaxValue = 7.0m });
            database.SaveChanges();
            var service = new ThresholdService(database);

            var result = await service.GetForStructureAsync(TestData.OwnedTowerId, TestData.Operator());

            Assert.NotNull(result);
            var ph = Assert.Single(result.Thresholds, t => t.SensorType == "PH");
            Assert.False(ph.IsDefault);
            Assert.Equal(6.0m, ph.MinValue);
            Assert.Equal(3, result.Thresholds.Count(t => t.IsDefault));
        }

        // A deleted structure should not be returned
        [Fact]
        public async Task GetForStructureAsync_DeletedStructure_ReturnsNull()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);

            var result = await service.GetForStructureAsync(TestData.DeletedTowerId, TestData.Operator());

            Assert.Null(result);
        }

        // Saving a range for the first time should add a new row with the sensor name upper-cased
        [Fact]
        public async Task UpsertAsync_NewRange_AddsRow()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);
            var dto = new UpdateThresholdDto { StructureId = TestData.OwnedTowerId, SensorType = " ph ", MinValue = 5.8m, MaxValue = 6.2m };

            var saved = await service.UpsertAsync(dto, TestData.Operator());

            Assert.NotNull(saved);
            Assert.Equal("PH", saved.SensorType);
            Assert.Equal(1, await database.Thresholds.CountAsync());
        }

        // Saving a range that already exists should update it instead of adding a second row
        [Fact]
        public async Task UpsertAsync_ExistingRange_UpdatesRow()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            database.Thresholds.Add(new Threshold { StructureId = TestData.OwnedTowerId, SensorType = "EC", MinValue = 1m, MaxValue = 2m });
            database.SaveChanges();
            var service = new ThresholdService(database);
            var dto = new UpdateThresholdDto { StructureId = TestData.OwnedTowerId, SensorType = "EC", MinValue = 1.4m, MaxValue = 2.2m };

            await service.UpsertAsync(dto, TestData.Operator());

            var row = Assert.Single(database.Thresholds);
            Assert.Equal(1.4m, row.MinValue);
            Assert.Equal(2.2m, row.MaxValue);
        }

        // Fodder pH saves through the same structure-scoped path as tower pH
        [Fact]
        public async Task UpsertAsync_PhOnFodderTray_Persists()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);
            var dto = new UpdateThresholdDto { StructureId = TestData.OwnedFodderId, SensorType = "PH", MinValue = 5m, MaxValue = 6m };

            var saved = await service.UpsertAsync(dto, TestData.Operator());
            Assert.NotNull(saved);
            var loaded = await service.GetForStructureAsync(TestData.OwnedFodderId, TestData.Operator());
            Assert.Equal(5m, Assert.Single(loaded!.Thresholds, x => x.SensorType == "PH").MinValue);
        }

        // A worker may read ranges but must not be able to change them
        [Fact]
        public async Task UpsertAsync_Worker_ReturnsNullAndSavesNothing()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);
            var dto = new UpdateThresholdDto { StructureId = TestData.OwnedTowerId, SensorType = "PH", MinValue = 5m, MaxValue = 6m };

            var saved = await service.UpsertAsync(dto, TestData.Worker());

            Assert.Null(saved);
            Assert.Empty(database.Thresholds);
        }

        // An operator must not be able to change a structure on another operator's site
        [Fact]
        public async Task UpsertAsync_OtherOperatorsStructure_ReturnsNull()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);
            var dto = new UpdateThresholdDto { StructureId = TestData.OtherTowerId, SensorType = "PH", MinValue = 5m, MaxValue = 6m };

            var saved = await service.UpsertAsync(dto, TestData.Operator());

            Assert.Null(saved);
        }

        // Deleting a saved range should remove it so the structure goes back to the default
        [Fact]
        public async Task DeleteAsync_OwnRange_RemovesIt()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            database.Thresholds.Add(new Threshold { Id = 5, StructureId = TestData.OwnedTowerId, SensorType = "PH", MinValue = 6m, MaxValue = 7m });
            database.SaveChanges();
            var service = new ThresholdService(database);

            var deleted = await service.DeleteAsync(5, TestData.Operator());

            Assert.True(deleted);
            Assert.Empty(database.Thresholds);
        }

        // An operator must not be able to delete a range on another operator's structure
        [Fact]
        public async Task DeleteAsync_OtherOperatorsRange_ReturnsFalse()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            database.Thresholds.Add(new Threshold { Id = 6, StructureId = TestData.OtherTowerId, SensorType = "PH", MinValue = 6m, MaxValue = 7m });
            database.SaveChanges();
            var service = new ThresholdService(database);

            var deleted = await service.DeleteAsync(6, TestData.Operator());

            Assert.False(deleted);
            Assert.Single(database.Thresholds);
        }

        // Deleting a range that does not exist should report false
        [Fact]
        public async Task DeleteAsync_MissingRange_ReturnsFalse()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var service = new ThresholdService(database);

            var deleted = await service.DeleteAsync(999, TestData.Operator());

            Assert.False(deleted);
        }
    }
}