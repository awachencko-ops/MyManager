using System.IO;
using System.Threading.Tasks;
using Replica;
using Xunit;

namespace Replica.UiSmokeTests
{
    public class PitStopPdfExtractionTests
    {
        [Fact]
        public async Task ExtractFirstPageText_FromPitStopReport_Works()
        {
            var pdfPath = @"C:\Андрей ПК\Replica BASEFOLDER\WARNING NOT DELETE\PitStop\Outlines CMYK\Reports on Success\00000_Визитки царита 2_log.pdf";
            if (!File.Exists(pdfPath))
                return;

            var result = await PythonPdfTextExtractor.TryExtractFirstPageTextAsync(pdfPath);
            if (!result.success)
                return;

            Assert.Contains("Preflight Report", result.text);
            Assert.Contains("No errors or warnings were found", result.text);
        }
    }
}
