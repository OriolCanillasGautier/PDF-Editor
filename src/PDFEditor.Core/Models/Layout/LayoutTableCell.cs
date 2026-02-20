using System.Collections.Generic;
using System.Linq;

namespace PDFEditor.Core.Models.Layout
{
    public class LayoutTableCell
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int RowSpan { get; set; } = 1;
        public int ColSpan { get; set; } = 1;
        public PdfRect BBox { get; set; } = new PdfRect(0, 0, 0, 0);
        public List<LayoutCharacter> Content { get; set; } = new();

        public string Text => string.Concat(Content.OrderByDescending(c => c.BBox.Y).ThenBy(c => c.BBox.X).Select(c => c.Char));
    }
}
