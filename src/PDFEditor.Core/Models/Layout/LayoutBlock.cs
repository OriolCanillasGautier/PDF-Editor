using System.Collections.Generic;

namespace PDFEditor.Core.Models.Layout
{
    public class LayoutBlock
    {
        public List<LayoutLine> Lines { get; set; } = new();
        public PdfRect BBox { get; set; } = new PdfRect(0, 0, 0, 0);
    }
}
