namespace PDFEditor.Core.Models.Layout
{
    public class LayoutCharacter
    {
        public char Char { get; set; }
        public PdfRect BBox { get; set; } = new PdfRect(0, 0, 0, 0);
        public string FontName { get; set; } = string.Empty;
        public float FontSize { get; set; }
        public string Color { get; set; } = "#000000";
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
    }
}
