using System;

namespace PDFEditor.Core.Models.Layout
{
    public record PdfRect(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
        public float Bottom => Y + Height;
        public float CenterX => X + (Width / 2);
        public float CenterY => Y + (Height / 2);

        // Helper to check if two boxes overlap (crucial for table cells)
        public bool Intersects(PdfRect other) =>
            X < other.Right && Right > other.X &&
            Y < other.Bottom && Bottom > other.Y;
    }
}
