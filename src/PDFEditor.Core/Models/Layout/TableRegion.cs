using System.Collections.Generic;

namespace PDFEditor.Core.Models.Layout
{
    public class TableRegion
    {
        public PdfRect BBox { get; set; } = new PdfRect(0, 0, 0, 0);
        public List<LayoutTableCell> Cells { get; set; } = new();
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
    }
}
