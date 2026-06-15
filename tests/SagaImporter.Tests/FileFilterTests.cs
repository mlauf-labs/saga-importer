using SagaImporter.Services;
using Xunit;

namespace SagaImporter.Tests;

public class FileFilterTests
{
    [Fact]
    public void EmptyLists_AllowsEverything()
    {
        Assert.True(FileFilter.ShouldImport("report.pdf", string.Empty, string.Empty));
    }

    [Fact]
    public void Exclude_BlocksMatchingExtension()
    {
        Assert.False(FileFilter.ShouldImport("download.part", string.Empty, ".tmp,.part"));
    }

    [Fact]
    public void Include_AllowsOnlyListedExtensions()
    {
        Assert.True(FileFilter.ShouldImport("a.pdf", ".pdf,.docx", string.Empty));
        Assert.False(FileFilter.ShouldImport("a.png", ".pdf,.docx", string.Empty));
    }

    [Fact]
    public void Exclude_TakesPrecedenceOverInclude()
    {
        Assert.False(FileFilter.ShouldImport("a.pdf", ".pdf", ".pdf"));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData(".pdf")]
    [InlineData(" .PDF ")]
    public void ExtensionParsing_IsLenient(string include)
    {
        Assert.True(FileFilter.ShouldImport("file.pdf", include, string.Empty));
    }
}
