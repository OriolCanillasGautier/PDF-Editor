using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfAnnotationService (burn annotations into PDF)
/// </summary>
public class PdfAnnotationServiceTests
{
    private readonly PdfAnnotationService _annotationService = new();
    private readonly PdfOperations _pdfOps = new();

    [Fact]
    public void BurnAnnotations_EmptyList_ReturnsSameBytes()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _annotationService.BurnAnnotations(pdf, new List<PdfAnnotation>());
        Assert.Equal(pdf, result);
    }

    [Fact]
    public void BurnAnnotations_TextAnnotation_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Text,
                PageIndex = 0,
                X = 0.1, Y = 0.1,
                Width = 0.5, Height = 0.05,
                Text = "Test Annotation",
                FontSize = 12f,
                Color = "#FF0000"
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void BurnAnnotations_HighlightAnnotation_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Highlight,
                PageIndex = 0,
                X = 0.1, Y = 0.2,
                Width = 0.4, Height = 0.03,
                FillColor = "#FFFF00",
                FillOpacity = 0.5f
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void BurnAnnotations_RectangleAnnotation_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Rectangle,
                PageIndex = 0,
                X = 0.2, Y = 0.2,
                Width = 0.3, Height = 0.2,
                StrokeColor = "#0000FF",
                StrokeWidth = 2f,
                StrokeOpacity = 1f,
                FillColor = "#00FF00",
                FillOpacity = 0.2f
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
    }

    [Fact]
    public void BurnAnnotations_EllipseAnnotation_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Ellipse,
                PageIndex = 0,
                X = 0.3, Y = 0.3,
                Width = 0.2, Height = 0.15,
                StrokeColor = "#000000",
                StrokeWidth = 1f,
                FillOpacity = 0f
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
    }

    [Fact]
    public void BurnAnnotations_RedactAnnotation_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Redact,
                PageIndex = 0,
                X = 0.1, Y = 0.1,
                Width = 0.3, Height = 0.02
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
    }

    [Fact]
    public void BurnAnnotations_MultipleAnnotations_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Text,
                PageIndex = 0,
                X = 0.1, Y = 0.1, Width = 0.3, Height = 0.05,
                Text = "Annotation 1",
                FontSize = 10f, Color = "#000000"
            },
            new PdfAnnotation
            {
                Type = AnnotationType.Highlight,
                PageIndex = 0,
                X = 0.1, Y = 0.2, Width = 0.4, Height = 0.02,
                FillColor = "#FFFF00", FillOpacity = 0.3f
            },
            new PdfAnnotation
            {
                Type = AnnotationType.Rectangle,
                PageIndex = 0,
                X = 0.5, Y = 0.5, Width = 0.3, Height = 0.2,
                StrokeColor = "#FF0000", StrokeWidth = 2f, FillOpacity = 0f
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void BurnAnnotations_OutOfRangePage_SkipsGracefully()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Text,
                PageIndex = 99, // doesn't exist
                X = 0.1, Y = 0.1, Width = 0.3, Height = 0.05,
                Text = "Should be skipped"
            }
        };
        var result = _annotationService.BurnAnnotations(pdf, annotations);
        Assert.NotNull(result);
    }

    [Fact]
    public void PdfAnnotation_Clone_CreatesIndependentCopy()
    {
        var original = new PdfAnnotation
        {
            Type = AnnotationType.Text,
            Text = "Original",
            X = 0.5,
            Y = 0.5,
            PageIndex = 0
        };

        var clone = original.Clone();
        Assert.NotEqual(original.Id, clone.Id); // New ID
        Assert.Equal(original.Type, clone.Type);
        Assert.Equal(original.Text, clone.Text);
        Assert.Equal(original.X, clone.X);

        // Modifying clone doesn't affect original
        clone.Text = "Modified";
        Assert.Equal("Original", original.Text);
    }
}
