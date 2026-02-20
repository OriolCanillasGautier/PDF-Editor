using System;

namespace PDFEditor.Core.Models.Layout
{
    public class PdfLine
    {
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }

        // Tolerance to account for slight rendering inaccuracies in PDFs
        public bool IsHorizontal => Math.Abs(Y1 - Y2) < 1.0f;
        public bool IsVertical => Math.Abs(X1 - X2) < 1.0f;
    }
}
