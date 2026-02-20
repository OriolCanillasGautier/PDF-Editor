using System.Collections.Generic;
using System.Linq;

namespace PDFEditor.Core.Models.Layout
{
    public class LayoutLine
    {
        public List<LayoutCharacter> Characters { get; set; } = new();
        public PdfRect BBox { get; set; } = new PdfRect(0, 0, 0, 0); // Computed from Characters
        public string Text => string.Concat(Characters.Select(c => c.Char));
    }
}
