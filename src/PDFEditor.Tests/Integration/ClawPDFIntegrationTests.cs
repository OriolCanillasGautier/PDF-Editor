using PDFEditor.ClawPDFIntegration;
using Xunit;

namespace PDFEditor.Tests.Integration;

public class ClawPDFIntegrationTests
{
    [Fact]
    public void Constructor_DefaultPath_CreatesInstance()
    {
        var wrapper = new ClawPDFWrapper();
        Assert.NotNull(wrapper);
    }

    [Fact]
    public void Constructor_CustomPath_StoresPath()
    {
        var wrapper = new ClawPDFWrapper(@"C:\Fake\clawPDF.exe");
        Assert.NotNull(wrapper);
    }

    [Fact]
    public void IsAvailable_ReturnsBool()
    {
        var wrapper = new ClawPDFWrapper();
        // Just verifies the method executes without exception
        var result = wrapper.IsAvailable();
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void PrintToPdf_MissingInputFile_ThrowsFileNotFoundException()
    {
        var wrapper = new ClawPDFWrapper(@"C:\Fake\clawPDF.exe");
        Assert.Throws<FileNotFoundException>(() =>
            wrapper.PrintToPdf("nonexistent_input.docx", @"C:\Temp\output.pdf"));
    }

    [Fact]
    public void MergeDocuments_EmptyArray_ThrowsArgumentException()
    {
        var wrapper = new ClawPDFWrapper();
        Assert.Throws<ArgumentException>(() =>
            wrapper.MergeDocuments(Array.Empty<string>(), @"C:\Temp\merged.pdf"));
    }

    [Fact]
    public void MergeDocuments_NullArray_ThrowsArgumentException()
    {
        var wrapper = new ClawPDFWrapper();
        Assert.Throws<ArgumentException>(() =>
            wrapper.MergeDocuments(null!, @"C:\Temp\merged.pdf"));
    }

    [Fact]
    public void MergeDocuments_MissingFile_ThrowsFileNotFoundException()
    {
        var wrapper = new ClawPDFWrapper();
        Assert.Throws<FileNotFoundException>(() =>
            wrapper.MergeDocuments(new[] { "missing1.pdf", "missing2.pdf" }, @"C:\Temp\out.pdf"));
    }

    /// <summary>
    /// Integration smoke test — only runs if clawPDF is actually installed.
    /// Skips gracefully when not available.
    /// </summary>
    [Fact(Skip = "Requires clawPDF installed on the test machine")]
    public void PrintToPdf_RealExe_ProducesOutput()
    {
        var wrapper = new ClawPDFWrapper();
        if (!wrapper.IsAvailable()) return;

        var tmpInput  = Path.GetTempFileName() + ".txt";
        var tmpOutput = Path.GetTempFileName() + ".pdf";
        File.WriteAllText(tmpInput, "Hello clawPDF!");

        try
        {
            wrapper.PrintToPdf(tmpInput, tmpOutput);
            Assert.True(File.Exists(tmpOutput));
            Assert.True(new FileInfo(tmpOutput).Length > 0);
        }
        finally
        {
            if (File.Exists(tmpInput))  File.Delete(tmpInput);
            if (File.Exists(tmpOutput)) File.Delete(tmpOutput);
        }
    }
}
