namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Represents an image processing service
/// </summary>
public interface IImageProcessor
{
    byte[] ConvertPdfPageToImage(byte[] pdfPageData, string format = "PNG", float dpi = 300f);
    byte[] ResizeImage(byte[] imageData, int width, int height);
    byte[] ConvertImageFormat(byte[] imageData, string targetFormat);
    byte[] ApplyOcr(byte[] imageData);
}
