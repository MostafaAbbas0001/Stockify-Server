namespace Stockify.Models.Dtos;

/// <summary>A generated report file, ready to download.</summary>
public sealed record ReportFile(byte[] Content, string ContentType, string FileName);
