using Microsoft.AspNetCore.Http;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Strategy
{
    // Result container returned after persisting image payload through an image storage strategy
    public record ImageStorageResult(string StorageLocation, int ImageSizeBytes, byte[]? ImageData, string ContentType);

    // Strategy interface defining pluggable image storage providers (database BLOB vs local disk/volume)
    public interface IImageStorageStrategy
    {
        // Stores uploaded image stream and returns metadata descriptor (deviceMac is nullable for manual or generic uploads)
        Task<ImageStorageResult> StoreImageAsync(IFormFile file, string? deviceMac, CancellationToken ct = default);

        // Loads raw binary image payload from storage location
        Task<(byte[] Data, string ContentType)?> RetrieveImageAsync(CameraCapture capture, CancellationToken ct = default);
    }
}
