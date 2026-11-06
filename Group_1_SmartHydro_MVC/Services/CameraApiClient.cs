using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    // One photo as the API describes it, used for both the gallery list and a single photo's details
    public sealed class ApiCapture
    {
        public long Id { get; set; }
        public string? DeviceMac { get; set; }
        public int? StructureId { get; set; }
        public string? Plant { get; set; }
        public string? Label { get; set; }
        public string? CameraLabel { get; set; }
        public bool IsFavourite { get; set; }
        public DateTime CapturedAt { get; set; }
        public int ImageSizeBytes { get; set; }
        public string? ContentType { get; set; }
    }

    // What the API said about a camera request: whether it worked, a message to show if it did not, and the photo id when one was created
    public sealed record CameraApiResult(bool Success, string Message, HttpStatusCode Status, long? PhotoId = null);

    // A new photo to store through the API
    public sealed class PhotoUpload
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Extension { get; set; } = ".jpg";
        public int StructureId { get; set; }
        public string Plant { get; set; } = "";
        public string Label { get; set; } = "";
        public string CameraLabel { get; set; } = "";
        public string? DeviceMac { get; set; }
        public DateTime? CapturedAtUtc { get; set; }
        public bool IsFavourite { get; set; }
    }

    // The changes to make to a stored photo; anything left null is kept as it is
    public sealed class PhotoChanges
    {
        public byte[]? Data { get; set; }
        public string Extension { get; set; } = ".jpg";
        public int? StructureId { get; set; }
        public string? Plant { get; set; }
        public string? Label { get; set; }
        public string? CameraLabel { get; set; }
        public DateTime? CapturedAtUtc { get; set; }
        public bool? IsFavourite { get; set; }
    }

    // Checks and describes the photo files the gallery accepts
    public static class CameraPhotoFile
    {
        public const long MaxBytes = 10 * 1024 * 1024;

        /* FileSignature (2026) documents using magic bytes to identify file types from their binary signatures. */
        // Checks the first bytes of a file to work out whether it is a real JPEG, PNG or WebP image, rather than trusting the file name or the content type sent by the browser
        public static bool TryDetectImage(byte[] data, out string extension)
        {
            extension = "";

            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                extension = ".jpg";
                return true;
            }

            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
                && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            {
                extension = ".png";
                return true;
            }

            if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
                && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            {
                extension = ".webp";
                return true;
            }

            return false;
        }

        // Returns the content type for an image extension, defaulting to JPEG
        public static string ContentTypeFor(string extension)
        {
            if (extension == ".png")
            {
                return "image/png";
            }

            if (extension == ".webp")
            {
                return "image/webp";
            }

            return "image/jpeg";
        }

        // Returns the file extension for an image content type, defaulting to .jpg
        public static string ExtensionFor(string? contentType)
        {
            if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                return ".png";
            }

            if (string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase))
            {
                return ".webp";
            }

            return ".jpg";
        }
    }

    // Talks to the API's camera endpoints on behalf of the signed-in user: the photo gallery, uploads, edits, deletes, and the board camera's live view and photo commands
    public sealed class CameraApiClient
    {
        private const int GalleryPageSize = 100;
        private const int MaxGalleryPages = 50;
        private const int StreamTimeoutSeconds = 300;

        private readonly SmartHydroApiHttpClient _api;

        public CameraApiClient(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Loads every photo on one structure, newest first, following the API's pages
        public async Task<List<ApiCapture>> GetGalleryAsync(int structureId, string accessToken, CancellationToken ct = default)
        {
            var captures = new List<ApiCapture>();

            for (var page = 1; page <= MaxGalleryPages; page++)
            {
                var path = $"api/Camera/gallery?structureId={structureId.ToString(CultureInfo.InvariantCulture)}&page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={GalleryPageSize.ToString(CultureInfo.InvariantCulture)}";
                using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

                if (!response.IsSuccessStatusCode)
                {
                    break;
                }

                var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, new GalleryPageDto(), ct);
                captures.AddRange(result.Items);

                if (page >= result.TotalPages)
                {
                    break;
                }
            }

            return captures;
        }

        // Finds the newest photo a camera board has stored, or null when it has none the user can see
        public async Task<ApiCapture?> GetLatestAsync(string deviceMac, string accessToken, CancellationToken ct = default)
        {
            var path = $"api/Camera/gallery?deviceMac={Uri.EscapeDataString(deviceMac)}&page=1&pageSize=1";
            using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, new GalleryPageDto(), ct);
            return result.Items.FirstOrDefault();
        }

        // Finds the newest photo stored for a structure, or null when none found
        public async Task<ApiCapture?> GetLatestForStructureAsync(int structureId, string accessToken, CancellationToken ct = default)
        {
            var path = $"api/Camera/gallery?structureId={structureId.ToString(CultureInfo.InvariantCulture)}&page=1&pageSize=1";
            using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, new GalleryPageDto(), ct);
            return result.Items.FirstOrDefault();
        }

        // Retrieves photos captured on a specific date for a device
        public async Task<List<ApiCapture>> GetPhotosByDateAsync(string deviceMac, string date, string accessToken, CancellationToken ct = default)
        {
            var path = $"api/Camera/gallery?deviceMac={Uri.EscapeDataString(deviceMac)}&date={Uri.EscapeDataString(date)}&page=1&pageSize=50";
            using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new List<ApiCapture>();
            }

            var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, new GalleryPageDto(), ct);
            return result.Items;
        }

        // Retrieves photos captured on a specific date for a structure
        public async Task<List<ApiCapture>> GetPhotosByDateAndStructureAsync(int structureId, string date, string accessToken, CancellationToken ct = default)
        {
            var path = $"api/Camera/gallery?structureId={structureId.ToString(CultureInfo.InvariantCulture)}&date={Uri.EscapeDataString(date)}&page=1&pageSize=50";
            using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new List<ApiCapture>();
            }

            var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, new GalleryPageDto(), ct);
            return result.Items;
        }

        // Reads one photo's details, or null when it is missing, deleted or not the user's to see
        public async Task<ApiCapture?> GetMetadataAsync(long id, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Camera/photos/{id.ToString(CultureInfo.InvariantCulture)}/metadata", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await SmartHydroApiHttpClient.ReadJsonAsync<ApiCapture?>(response, null, ct);
        }

        // Downloads a photo's image, or null when it is missing or not the user's to see
        public async Task<(byte[] Data, string ContentType)?> GetPhotoAsync(long id, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Camera/photos/{id.ToString(CultureInfo.InvariantCulture)}", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "image/jpeg";
            }

            var data = await response.Content.ReadAsByteArrayAsync(ct);
            if (data.Length == 0)
            {
                return null;
            }

            return (data, contentType);
        }

        // Stores a new photo on a structure
        public async Task<CameraApiResult> UploadAsync(PhotoUpload photo, string accessToken, CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();
            form.Add(BuildImage(photo.Data, photo.Extension), "file", "photo" + photo.Extension);
            form.Add(new StringContent(photo.StructureId.ToString(CultureInfo.InvariantCulture)), "structureId");
            form.Add(new StringContent(photo.Plant), "plant");
            form.Add(new StringContent(photo.Label), "label");
            form.Add(new StringContent(photo.CameraLabel), "cameraLabel");
            form.Add(new StringContent(photo.IsFavourite ? "true" : "false"), "isFavourite");

            if (!string.IsNullOrWhiteSpace(photo.DeviceMac))
            {
                form.Add(new StringContent(photo.DeviceMac), "deviceMac");
            }

            if (photo.CapturedAtUtc.HasValue)
            {
                form.Add(new StringContent(FormatTime(photo.CapturedAtUtc.Value)), "capturedAt");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/Camera/upload") { Content = form };
            using var response = await _api.SendAsync(request, accessToken, ct);

            if (!response.IsSuccessStatusCode)
            {
                return await ReadFailureAsync(response, "The photo could not be saved. Try again.", ct);
            }

            var created = await SmartHydroApiHttpClient.ReadJsonAsync(response, new UploadDto(), ct);
            return new CameraApiResult(true, "", response.StatusCode, created.CaptureId);
        }

        // Changes a stored photo's details, and its image when one is given
        public async Task<CameraApiResult> UpdateAsync(long id, PhotoChanges changes, string accessToken, CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();

            if (changes.Data != null)
            {
                form.Add(BuildImage(changes.Data, changes.Extension), "file", "photo" + changes.Extension);
            }

            if (changes.StructureId.HasValue)
            {
                form.Add(new StringContent(changes.StructureId.Value.ToString(CultureInfo.InvariantCulture)), "structureId");
            }

            if (changes.Plant != null)
            {
                form.Add(new StringContent(changes.Plant), "plant");
            }

            if (changes.Label != null)
            {
                form.Add(new StringContent(changes.Label), "label");
            }

            if (changes.CameraLabel != null)
            {
                form.Add(new StringContent(changes.CameraLabel), "cameraLabel");
            }

            if (changes.CapturedAtUtc.HasValue)
            {
                form.Add(new StringContent(FormatTime(changes.CapturedAtUtc.Value)), "capturedAt");
            }

            if (changes.IsFavourite.HasValue)
            {
                form.Add(new StringContent(changes.IsFavourite.Value ? "true" : "false"), "isFavourite");
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, $"api/Camera/photos/{id.ToString(CultureInfo.InvariantCulture)}") { Content = form };
            using var response = await _api.SendAsync(request, accessToken, ct);

            if (response.IsSuccessStatusCode)
            {
                return new CameraApiResult(true, "", response.StatusCode, id);
            }

            return await ReadFailureAsync(response, "The photo could not be saved. Try again.", ct);
        }

        // Marks or unmarks a photo as a favourite
        public async Task<bool> SetFavouriteAsync(long id, bool favourite, string accessToken, CancellationToken ct = default)
        {
            var value = favourite ? "true" : "false";
            using var response = await _api.SendAsync(HttpMethod.Put, $"api/Camera/{id.ToString(CultureInfo.InvariantCulture)}/favourite?value={value}", accessToken, null, ct);
            return response.IsSuccessStatusCode;
        }

        // Moves a photo to deleted items so it can still be restored
        public async Task<bool> DeleteAsync(long id, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Delete, $"api/Camera/photos/{id.ToString(CultureInfo.InvariantCulture)}", accessToken, null, ct);
            return response.IsSuccessStatusCode;
        }

        // Brings a photo back from deleted items
        public async Task<bool> RestoreAsync(long id, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Post, $"api/Camera/photos/{id.ToString(CultureInfo.InvariantCulture)}/restore", accessToken, null, ct);
            return response.IsSuccessStatusCode;
        }

        // Permanently removes a deleted photo (Admin only)
        public async Task<bool> PurgeAsync(long id, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Delete, $"api/Camera/photos/{id.ToString(CultureInfo.InvariantCulture)}/purge", accessToken, null, ct);
            return response.IsSuccessStatusCode;
        }

        // Queues a photo on a camera board and returns when the API accepted it, so the caller can wait for the photo to arrive
        public async Task<(CameraApiResult Result, DateTime RequestedAtUtc)> TriggerCaptureAsync(string deviceMac, string accessToken, bool flash = false, CancellationToken ct = default)
        {
            var sentAt = DateTime.UtcNow;
            using var response = await _api.SendAsync(HttpMethod.Post, "api/Camera/trigger-capture", accessToken, new { deviceMac, flash }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var failure = await ReadFailureAsync(response, "The board camera couldn't take the photo. Try again.", ct);
                return (failure, sentAt);
            }

            var queued = await SmartHydroApiHttpClient.ReadJsonAsync(response, new TriggerDto(), ct);
            var requestedAt = sentAt;
            if (queued.RequestedAt.HasValue)
            {
                requestedAt = ToUtc(queued.RequestedAt.Value);
            }

            return (new CameraApiResult(true, "", response.StatusCode), requestedAt);
        }

        // Treats a date from the API as UTC when it carries no time zone
        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        // Wraps image bytes as an upload part with the right content type
        private static ByteArrayContent BuildImage(byte[] data, string extension)
        {
            var image = new ByteArrayContent(data);
            image.Headers.ContentType = new MediaTypeHeaderValue(CameraPhotoFile.ContentTypeFor(extension));
            return image;
        }

        // Writes a UTC time in the round-trip format the API reads
        private static string FormatTime(DateTime utc)
        {
            return ToUtc(utc).ToString("o", CultureInfo.InvariantCulture);
        }

        // Turns a failed response into a result carrying the API's own message, or the fallback when it gave none
        private static async Task<CameraApiResult> ReadFailureAsync(HttpResponseMessage response, string fallback, CancellationToken ct)
        {
            var body = await SmartHydroApiHttpClient.ReadJsonAsync(response, new ErrorDto(), ct);
            var message = body.Message;

            if (string.IsNullOrWhiteSpace(message))
            {
                message = body.Detail;
            }

            if (string.IsNullOrWhiteSpace(message) || response.StatusCode == HttpStatusCode.Forbidden)
            {
                message = fallback;
            }

            return new CameraApiResult(false, message, response.StatusCode);
        }

        private sealed class GalleryPageDto
        {
            public List<ApiCapture> Items { get; set; } = new();
            public int TotalPages { get; set; }
        }

        private sealed class UploadDto
        {
            public long CaptureId { get; set; }
        }

        private sealed class TriggerDto
        {
            public DateTime? RequestedAt { get; set; }
        }

        private sealed class ErrorDto
        {
            public string? Message { get; set; }
            public string? Detail { get; set; }
        }
    }
}

//Reference List:

//FileSignature, 2026. Validate File Types by Magic Bytes. [online] Available at: <https://filesignature.org/guides/validate-file-type-magic-bytes> [Accessed 22 September 2026].
