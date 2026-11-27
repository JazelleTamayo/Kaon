namespace Kaon.Helpers;

// Saves proof-of-payment pictures OUTSIDE wwwroot, so they are never publicly browsable.
// They are served only through PaymentController, to the owner and to the order's customer.
public static class ProofStorage
{
    public const long MaxBytes = 5 * 1024 * 1024;

    // Set at startup from Kaon:DataDir; when empty, App_Data next to the app is used
    public static string? Root { get; set; }

    public static string Folder(IWebHostEnvironment env, string sub = "proofs") =>
        Path.Combine(string.IsNullOrWhiteSpace(Root) ? Path.Combine(env.ContentRootPath, "App_Data") : Root, sub);

    public static string PathFor(IWebHostEnvironment env, string fileName, string sub = "proofs") =>
        Path.Combine(Folder(env, sub), Path.GetFileName(fileName));

    public static async Task<(string? Name, string? Error)> SaveAsync(IFormFile file, IWebHostEnvironment env, string sub = "proofs")
    {
        if (file.Length == 0) return (null, "That file is empty.");
        if (file.Length > MaxBytes) return (null, "Please use a picture under 5 MB.");

        // Check the real file type from its first bytes, not the file name
        var head = new byte[12];
        await using var input = file.OpenReadStream();
        var read = await input.ReadAsync(head, 0, head.Length);
        var ext = Detect(head, read);
        if (ext == null) return (null, "Please upload a photo or screenshot (JPG, PNG or WebP).");

        Directory.CreateDirectory(Folder(env, sub));
        var name = Guid.NewGuid().ToString("N") + ext;
        input.Position = 0;
        await using var output = File.Create(PathFor(env, name, sub));
        await input.CopyToAsync(output);
        return (name, null);
    }

    public static void Delete(IWebHostEnvironment env, string? fileName, string sub = "proofs")
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var path = PathFor(env, fileName, sub);
        if (File.Exists(path)) File.Delete(path);
    }

    public static string ContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private static string? Detect(byte[] h, int n)
    {
        if (n >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (n >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (n >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' &&
            h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
        return null;
    }
}
