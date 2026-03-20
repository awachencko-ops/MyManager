using System;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderFileStageCommandServiceTests
{
    [Fact]
    public void TryPrepareOrderAdd_PrintStageWithOrderNumber_UsesOrderNumberAsTargetFileName()
    {
        var service = new OrderFileStageCommandService();
        var order = new OrderData { Id = "5001" };
        var sourcePath = CreateTempFile("print-source.pdf", "print-data");

        try
        {
            var uniqueNameCalls = 0;
            var prepared = service.TryPrepareOrderAdd(
                order,
                sourcePath,
                OrderStages.Print,
                ensureUniqueStageFileName: (_, fileName) =>
                {
                    uniqueNameCalls++;
                    return fileName;
                },
                out var plan);

            Assert.True(prepared);
            Assert.Equal(sourcePath, plan.CleanSourcePath);
            Assert.Equal("5001.pdf", plan.TargetFileName);
            Assert.True(plan.UsePrintCopy);
            Assert.False(plan.EnsureSourceCopy);
            Assert.Equal(0, uniqueNameCalls);
        }
        finally
        {
            TryDeleteFile(sourcePath);
        }
    }

    [Fact]
    public void TryPrepareOrderAdd_PreparedStage_UsesUniqueNameAndMarksEnsureSourceCopy()
    {
        var service = new OrderFileStageCommandService();
        var order = new OrderData { Id = "5002" };
        var sourcePath = CreateTempFile("prepared-source.pdf", "prepared-data");

        try
        {
            int calledStage = -1;
            string? calledFileName = null;
            var prepared = service.TryPrepareOrderAdd(
                order,
                sourcePath,
                OrderStages.Prepared,
                ensureUniqueStageFileName: (stage, fileName) =>
                {
                    calledStage = stage;
                    calledFileName = fileName;
                    return "unique.pdf";
                },
                out var plan);

            Assert.True(prepared);
            Assert.Equal(OrderStages.Prepared, calledStage);
            Assert.Equal(Path.GetFileName(sourcePath), calledFileName);
            Assert.Equal("unique.pdf", plan.TargetFileName);
            Assert.False(plan.UsePrintCopy);
            Assert.True(plan.EnsureSourceCopy);
        }
        finally
        {
            TryDeleteFile(sourcePath);
        }
    }

    [Fact]
    public void TryPrepareItemAdd_PrintStage_FillsClientLabelAndUsesPrintNamePipeline()
    {
        var service = new OrderFileStageCommandService();
        var order = new OrderData { Id = "5003" };
        var item = new OrderFileItem
        {
            ItemId = "item-1",
            ClientFileLabel = string.Empty
        };
        var sourcePath = CreateTempFile("item-source.pdf", "item-data");

        try
        {
            int calledStage = -1;
            string? buildInput = null;
            var prepared = service.TryPrepareItemAdd(
                order,
                item,
                sourcePath,
                OrderStages.Print,
                ensureUniqueStageFileName: (stage, fileName) =>
                {
                    calledStage = stage;
                    return "item-unique.pdf";
                },
                buildItemPrintFileName: cleanSource =>
                {
                    buildInput = cleanSource;
                    return "built-item-print.pdf";
                },
                out var plan);

            Assert.True(prepared);
            Assert.Equal(Path.GetFileNameWithoutExtension(sourcePath), item.ClientFileLabel);
            Assert.Equal(sourcePath, buildInput);
            Assert.Equal(OrderStages.Print, calledStage);
            Assert.Equal("item-unique.pdf", plan.TargetFileName);
            Assert.True(plan.UsePrintCopy);
            Assert.False(plan.EnsureSourceCopy);
        }
        finally
        {
            TryDeleteFile(sourcePath);
        }
    }

    [Fact]
    public void TryPrepareItemAdd_WhenSourceFileMissing_ReturnsFalse()
    {
        var service = new OrderFileStageCommandService();
        var order = new OrderData { Id = "5004" };
        var item = new OrderFileItem { ItemId = "item-2" };

        var prepared = service.TryPrepareItemAdd(
            order,
            item,
            sourceFile: @"C:\missing\file.pdf",
            stage: OrderStages.Source,
            ensureUniqueStageFileName: (_, fileName) => fileName,
            buildItemPrintFileName: cleanSource => cleanSource,
            out _);

        Assert.False(prepared);
    }

    private static string CreateTempFile(string fileName, string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "replica-file-stage-command-tests");
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, $"{Guid.NewGuid():N}-{fileName}");
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
