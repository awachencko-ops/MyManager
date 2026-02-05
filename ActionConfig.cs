// ActionConfig.cs
namespace MyManager
{
    public class ActionConfig
    {
        public string Name { get; set; } = "";
        public string BaseFolder { get; set; } = "";
        public string InputFolder { get; set; } = "";
        public string ReportSuccess { get; set; } = "";
        public string ReportError { get; set; } = "";
        public string OriginalSuccess { get; set; } = "";
        public string OriginalError { get; set; } = "";
        public string ProcessedSuccess { get; set; } = "";
        public string ProcessedError { get; set; } = "";
        public string NonPdfLogs { get; set; } = "";
        public string NonPdfFiles { get; set; } = "";
    }
}