namespace PendriveRescue.Infrastructure.FileSignatures;

public record FileSignature(string Extension, byte[] Header, string Description);

public static class SignatureDatabase
{
    public static readonly List<FileSignature> Signatures = new()
    {
        new FileSignature(".jpg", new byte[] { 0xFF, 0xD8, 0xFF }, "JPEG Image"),
        new FileSignature(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "PNG Image"),
        new FileSignature(".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "PDF Document"),
        new FileSignature(".docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "Word Document (ZIP based)"),
        new FileSignature(".xlsx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "Excel Workbook (ZIP based)"),
        new FileSignature(".pptx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "PowerPoint Presentation (ZIP based)"),
        new FileSignature(".zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "ZIP Archive"),
        new FileSignature(".mp4", new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }, "MP4 Video"),
        new FileSignature(".mp3", new byte[] { 0x49, 0x44, 0x33 }, "MP3 Audio (ID3v2)"),
        new FileSignature(".txt", new byte[] { 0xEF, 0xBB, 0xBF }, "TXT File (UTF-8 BOM)") // Optional, many TXT files don't have BOM
    };
}
