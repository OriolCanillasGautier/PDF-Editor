using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for XfdfAnnotationService (XFDF import/export)
/// </summary>
public class XfdfAnnotationServiceTests
{
    private readonly XfdfAnnotationService _sut = new();

    #region Export

    [Fact]
    public void ExportToXfdf_EmptyList_ReturnsValidXfdf()
    {
        var result = _sut.ExportToXfdf(new List<PdfAnnotation>());
        Assert.NotNull(result);
        Assert.Contains("xfdf", result);
    }

    [Fact]
    public void ExportToXfdf_SingleTextAnnotation_ContainsFreetext()
    {
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Text,
                PageIndex = 0,
                X = 0.1, Y = 0.2,
                Width = 0.5, Height = 0.1,
                Text = "Hello World",
                FontSize = 14f,
                Color = "#FF0000"
            }
        };

        var xfdf = _sut.ExportToXfdf(annotations);
        Assert.Contains("freetext", xfdf);
        Assert.Contains("Hello World", xfdf);
        Assert.Contains("page=\"0\"", xfdf);
    }

    [Fact]
    public void ExportToXfdf_HighlightAnnotation_ContainsHighlight()
    {
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Highlight,
                PageIndex = 1,
                FillColor = "#FFFF00"
            }
        };

        var xfdf = _sut.ExportToXfdf(annotations);
        Assert.Contains("highlight", xfdf);
        Assert.Contains("page=\"1\"", xfdf);
    }

    [Fact]
    public void ExportToXfdf_StampAnnotation_ContainsStampAndIcon()
    {
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Stamp,
                StampPreset = StampType.Approved,
                StampText = "APPROVED",
                PageIndex = 0
            }
        };

        var xfdf = _sut.ExportToXfdf(annotations);
        Assert.Contains("stamp", xfdf);
        Assert.Contains("Approved", xfdf);
        Assert.Contains("APPROVED", xfdf);
    }

    [Fact]
    public void ExportToXfdf_WithPdfFilePath_ContainsFElement()
    {
        var xfdf = _sut.ExportToXfdf(new List<PdfAnnotation>(), "test.pdf");
        Assert.Contains("href=\"test.pdf\"", xfdf);
    }

    [Fact]
    public void ExportToXfdf_MultipleAnnotations_ExportsAll()
    {
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation { Type = AnnotationType.Text, Text = "First" },
            new PdfAnnotation { Type = AnnotationType.Rectangle },
            new PdfAnnotation { Type = AnnotationType.StickyNote, NoteContent = "Note" }
        };

        var xfdf = _sut.ExportToXfdf(annotations);
        Assert.Contains("freetext", xfdf);
        Assert.Contains("square", xfdf);
        Assert.Contains("<text ", xfdf.Replace("freetext", "freeTEXT")); // "text" element for sticky note
    }

    [Fact]
    public void ExportToXfdf_ArrowAnnotation_ContainsStartEnd()
    {
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Arrow,
                X = 10, Y = 20,
                EndX = 100, EndY = 200
            }
        };

        var xfdf = _sut.ExportToXfdf(annotations);
        Assert.Contains("line", xfdf);
        Assert.Contains("start=", xfdf);
        Assert.Contains("end=", xfdf);
    }

    [Fact]
    public void ExportToXfdf_FreehandAnnotation_ContainsInklist()
    {
        var ann = new PdfAnnotation
        {
            Type = AnnotationType.FreehandDraw,
            StrokeWidth = 2f
        };
        ann.Points.Add((10, 20));
        ann.Points.Add((30, 40));

        var xfdf = _sut.ExportToXfdf(new List<PdfAnnotation> { ann });
        Assert.Contains("ink", xfdf);
        Assert.Contains("inklist", xfdf);
        Assert.Contains("gesture", xfdf);
    }

    [Fact]
    public void ExportToXfdf_AnnotationWithRotation_ContainsRotation()
    {
        var annotations = new List<PdfAnnotation>
        {
            new PdfAnnotation
            {
                Type = AnnotationType.Text,
                Rotation = 45.0
            }
        };

        var xfdf = _sut.ExportToXfdf(annotations);
        Assert.Contains("rotation=\"45\"", xfdf);
    }

    #endregion

    #region Import

    [Fact]
    public void ImportFromXfdf_ValidXfdf_ReturnsAnnotations()
    {
        var xfdfContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<xfdf xmlns=""http://ns.adobe.com/xfdf/"" xml:space=""preserve"">
  <annots>
    <freetext page=""0"" color=""#FF0000"" opacity=""1.00"" rect=""10.00,20.00,100.00,50.00"" name=""ann1"" fontsize=""12.0"">
      <contents>Test text</contents>
    </freetext>
  </annots>
</xfdf>";

        var result = _sut.ImportFromXfdf(xfdfContent);
        Assert.Single(result);
        Assert.Equal(AnnotationType.Text, result[0].Type);
        Assert.Equal("Test text", result[0].Text);
        Assert.Equal(0, result[0].PageIndex);
        Assert.Equal("#FF0000", result[0].Color);
    }

    [Fact]
    public void ImportFromXfdf_MultipleAnnotations_ImportsAll()
    {
        var xfdfContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<xfdf xmlns=""http://ns.adobe.com/xfdf/"" xml:space=""preserve"">
  <annots>
    <highlight page=""0"" color=""#FFFF00"" opacity=""0.30"" rect=""0,0,100,20"" name=""h1"" />
    <square page=""1"" color=""#0000FF"" opacity=""1.00"" rect=""10,10,200,200"" name=""s1"" interior-color=""#00FF00"" width=""2.0"" />
  </annots>
</xfdf>";

        var result = _sut.ImportFromXfdf(xfdfContent);
        Assert.Equal(2, result.Count);
        Assert.Equal(AnnotationType.Highlight, result[0].Type);
        Assert.Equal(AnnotationType.Rectangle, result[1].Type);
    }

    [Fact]
    public void ImportFromXfdf_EmptyAnnots_ReturnsEmpty()
    {
        var xfdfContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<xfdf xmlns=""http://ns.adobe.com/xfdf/"" xml:space=""preserve"">
  <annots />
</xfdf>";

        var result = _sut.ImportFromXfdf(xfdfContent);
        Assert.Empty(result);
    }

    [Fact]
    public void ImportFromXfdf_InvalidXml_ReturnsEmpty()
    {
        var result = _sut.ImportFromXfdf("this is not xml");
        Assert.Empty(result);
    }

    [Fact]
    public void ImportFromXfdf_StampAnnotation_ParsesIconAndContent()
    {
        var xfdfContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<xfdf xmlns=""http://ns.adobe.com/xfdf/"" xml:space=""preserve"">
  <annots>
    <stamp page=""2"" color=""#FF0000"" opacity=""0.50"" rect=""0,0,200,50"" name=""s1"" icon=""Approved"">
      <contents>APPROVED</contents>
    </stamp>
  </annots>
</xfdf>";

        var result = _sut.ImportFromXfdf(xfdfContent);
        Assert.Single(result);
        Assert.Equal(AnnotationType.Stamp, result[0].Type);
        Assert.Equal(StampType.Approved, result[0].StampPreset);
        Assert.Equal("APPROVED", result[0].StampText);
    }

    #endregion

    #region Roundtrip

    [Fact]
    public void ExportThenImport_RoundTrips_TextAnnotation()
    {
        var original = new PdfAnnotation
        {
            Type = AnnotationType.Text,
            PageIndex = 2,
            X = 0.1, Y = 0.2,
            Width = 0.5, Height = 0.1,
            Text = "Round trip test",
            FontSize = 18f,
            Color = "#0000FF"
        };

        var xfdf = _sut.ExportToXfdf(new List<PdfAnnotation> { original });
        var imported = _sut.ImportFromXfdf(xfdf);

        Assert.Single(imported);
        Assert.Equal(original.Type, imported[0].Type);
        Assert.Equal(original.PageIndex, imported[0].PageIndex);
        Assert.Equal(original.Text, imported[0].Text);
        Assert.Equal(original.Color, imported[0].Color);
    }

    [Fact]
    public void ExportThenImport_RoundTrips_HighlightAnnotation()
    {
        var original = new PdfAnnotation
        {
            Type = AnnotationType.Highlight,
            PageIndex = 0,
            FillColor = "#00FF00",
            FillOpacity = 0.5f
        };

        var xfdf = _sut.ExportToXfdf(new List<PdfAnnotation> { original });
        var imported = _sut.ImportFromXfdf(xfdf);

        Assert.Single(imported);
        Assert.Equal(AnnotationType.Highlight, imported[0].Type);
    }

    [Fact]
    public void ExportThenImport_RoundTrips_StickyNote()
    {
        var original = new PdfAnnotation
        {
            Type = AnnotationType.StickyNote,
            NoteContent = "Important note",
            PageIndex = 1
        };

        var xfdf = _sut.ExportToXfdf(new List<PdfAnnotation> { original });
        var imported = _sut.ImportFromXfdf(xfdf);

        Assert.Single(imported);
        Assert.Equal(AnnotationType.StickyNote, imported[0].Type);
        Assert.Equal("Important note", imported[0].NoteContent);
    }

    #endregion
}
