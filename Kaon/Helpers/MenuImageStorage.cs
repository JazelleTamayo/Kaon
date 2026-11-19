namespace Kaon.Helpers;

// Saves owner-uploaded dish/package photos INSIDE wwwroot, so they are publicly
// servable like the seeded starter images. Only files this helper saved (under
// /img/menu/uploads/) are ever deleted; the seeded /img/menu/*.webp assets are not.
public static class MenuImageStorage
{
    public const long MaxBytes = 5 * 1024 * 1024;
    private const string UrlPrefix = "/img/menu/uploads/";

    private static string Folder(IWebHostEnvironment env) =>
        Path.Combine(env.WebRootPath, "img", "menu", "uploads");

    public static async Task<(string? Url, string? Error)> SaveAsync(IFormFile file, IWebHostEnvironment env)
    {
        if (file.Length == 0) return (null, "That file is empty.");
        if (file.Length > MaxBytes) return (null, "Please use a photo under 5 MB.");

        // Check the real file type from its first bytes, not the file name
        var head = new byte[12];
        await using var input = file.OpenReadStream();
        var read = await input.ReadAsync(head, 0, head.Length);
        var ext = Detect(head, read);
        if (ext == null) return (null, "Please upload a photo (JPG, PNG or WebP).");

        Directory.CreateDirectory(Folder(env));
        var name = Guid.NewGuid().ToString("N") + ext;
        input.Position = 0;
        await using var output = File.Create(Path.Combine(Folder(env), name));
        await input.CopyToAsync(output);
        return (UrlPrefix + name, null);
    }

    public static void DeleteIfUploaded(IWebHostEnvironment env, string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(UrlPrefix, StringComparison.Ordinal)) return;
        var path = Path.Combine(Folder(env), Path.GetFileName(url));
        if (File.Exists(path)) File.Delete(path);
    }

    private static string? Detect(byte[] h, int n)
    {
        if (n >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (n >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (n >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' &&
            h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
        return null;
    }
}
