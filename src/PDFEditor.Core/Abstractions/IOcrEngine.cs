namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Represents an OCR (Optical Character Recognition) engine
/// </summary>
public interface IOcrEngine
{
    Task<string> RecognizeText(byte[] imageData, string language = "eng");
    Task<List<OcrResult>> RecognizeTextRegions(byte[] imageData, string language = "eng");
    List<string> GetSupportedLanguages();
}

public class OcrResult
{
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public (int x, int y, int width, int height) BoundingBox { get; set; }
}
