namespace PDFOrganizerAPI.Services
{

      
 public static class FileValidateService
{
    private static readonly Dictionary<string, List<string>> FileSignatures = new Dictionary<string, List<string>>()
    {
        { "pdf", new List<string> { "25 50 44 46 2d" } },
        { "docx", new List<string> { "50 4b 03 04" } },
        { "xlsx", new List<string> { "50 4b 03 04" } },
        { "pptx", new List<string> { "50 4b 03 04" } },
        { "png", new List<string> { "89 50 4e 47 0d 0a 1a 0a" } },
        { "jpg", new List<string> { "ff d8 ff" } },
        { "jpeg", new List<string> { "ff d8 ff" } },
        { "gif", new List<string> { "47 49 46 38" } },
        { "zip", new List<string> { "50 4b 03 04" } },
        { "mp4", new List<string> { "00 00 00 18 66 74 79 70" } },
        { "rar", new List<string> { "52 61 72 21 1a 07 00" } }
    };

    private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB

    public static async Task<bool> ValidateFileTypeAsync(IFormFile file, List<string> expectedTypes)
    {
        if (file == null || file.Length == 0 || file.Length > MaxFileSize)
        {
            return false;
        }

        byte[] fileHeader = new byte[8];
        using (var stream = file.OpenReadStream())
        {
            await stream.ReadAsync(fileHeader, 0, fileHeader.Length);
        }

        string hexHeader = BitConverter.ToString(fileHeader).Replace("-", " ").ToLower();

        foreach (var type in expectedTypes)
        {
            if (FileSignatures.TryGetValue(type.ToLower(), out var signatures))
            {
                foreach (var signature in signatures)
                {

                    if (hexHeader.StartsWith(signature))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}



    }

