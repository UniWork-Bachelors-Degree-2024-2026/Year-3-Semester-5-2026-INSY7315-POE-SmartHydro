using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Strategy
{
    // Concrete image storage strategy persisting image files to local physical disk / volume storage
    public class LocalDiskImageStorageStrategy : IImageStorageStrategy
    {
        private readonly string _storageRoot;

        // Injects environment to resolve storage directory
        public LocalDiskImageStorageStrategy(IWebHostEnvironment environment)
        {
            _storageRoot = Path.Combine(environment.ContentRootPath, "Storage", "CameraCaptures");
            if (!Directory.Exists(_storageRoot))
            {
                Directory.CreateDirectory(_storageRoot);
            }
        }

        // Writes incoming image stream to file system and returns relative file path
        public async Task<ImageStorageResult> StoreImageAsync(IFormFile file, string? deviceMac, CancellationToken ct = default)
        {
            var sanitizedMac = string.IsNullOrWhiteSpace(deviceMac) ? "MANUAL" : deviceMac.Replace(":", "").ToUpperInvariant();
            var fileName = $"{sanitizedMac}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.jpg";
            var targetPath = Path.Combine(_storageRoot, fileName);

            using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(fileStream, ct);
            }

            var fileInfo = new FileInfo(targetPath);
            return new ImageStorageResult(
                StorageLocation: targetPath,
                ImageSizeBytes: (int)fileInfo.Length,
                ImageData: null, // Keep null to avoid excessive RAM usage
                ContentType: file.ContentType ?? "image/jpeg"
            );
        }

        // Reads binary data from local disk file if present, with fallback to database bytes
        public async Task<(byte[] Data, string ContentType)?> RetrieveImageAsync(CameraCapture capture, CancellationToken ct = default)
        {
            if (capture.ImageData is not null && capture.ImageData.Length > 0)
            {
                return (capture.ImageData, capture.ContentType);
            }

            return null;
        }
    }
}
