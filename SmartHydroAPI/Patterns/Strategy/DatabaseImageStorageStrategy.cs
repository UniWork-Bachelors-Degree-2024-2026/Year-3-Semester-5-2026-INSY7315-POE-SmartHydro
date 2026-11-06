using Microsoft.AspNetCore.Http;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Strategy
{
    // Concrete image storage strategy persisting binary byte arrays directly in PostgreSQL bytea columns
    public class DatabaseImageStorageStrategy : IImageStorageStrategy
    {
        // Reads form file stream into byte array for in-database persistence
        public async Task<ImageStorageResult> StoreImageAsync(IFormFile file, string? deviceMac, CancellationToken ct = default)
        {
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, ct);
            var bytes = memoryStream.ToArray();

            return new ImageStorageResult(
                StorageLocation: "DATABASE",
                ImageSizeBytes: bytes.Length,
                ImageData: bytes,
                ContentType: file.ContentType ?? "image/jpeg"
            );
        }

        // Retrieves binary payload directly from the CameraCapture entity
        public Task<(byte[] Data, string ContentType)?> RetrieveImageAsync(CameraCapture capture, CancellationToken ct = default)
        {
            if (capture.ImageData is null || capture.ImageData.Length == 0)
                return Task.FromResult<(byte[] Data, string ContentType)?>(null);

            return Task.FromResult<(byte[] Data, string ContentType)?>((capture.ImageData, capture.ContentType));
        }
    }
}
