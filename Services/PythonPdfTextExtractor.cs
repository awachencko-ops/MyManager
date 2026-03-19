using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Replica
{
    public static class PythonPdfTextExtractor
    {
        private const string PythonScript = """
import sys
from pathlib import Path

try:
    import fitz
except Exception as ex:
    print(f"PyMuPDF import failed: {ex}", file=sys.stderr)
    sys.exit(2)

pdf = Path(sys.argv[1])
doc = fitz.open(pdf)
try:
    page = doc[0]
    text = page.get_text("text") or ""
    sys.stdout.write(text)
finally:
    doc.close()
""";

        private const string PdfMetadataScript = """
import sys
import json
from pathlib import Path

try:
    import fitz
except Exception as ex:
    print(f"PyMuPDF import failed: {ex}", file=sys.stderr)
    sys.exit(2)

pdf = Path(sys.argv[1])
doc = fitz.open(pdf)
try:
    md = doc.metadata or {}
    payload = {
        "title": md.get("title") or "",
        "author": md.get("author") or "",
        "subject": md.get("subject") or "",
        "keywords": md.get("keywords") or "",
        "creator": md.get("creator") or "",
        "producer": md.get("producer") or "",
        "creationDate": md.get("creationDate") or "",
        "modDate": md.get("modDate") or "",
        "pageCount": doc.page_count,
        "fileSize": pdf.stat().st_size,
    }
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(',', ':')))
finally:
    doc.close()
""";

        public static async Task<(bool success, string text, string? error)> TryExtractFirstPageTextAsync(
            string pdfPath,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                    return (false, string.Empty, "PDF file not found.");

                var pythonExe = ResolvePythonExecutable();
                if (string.IsNullOrWhiteSpace(pythonExe))
                    return (false, string.Empty, "Python executable not found.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("-");
                startInfo.ArgumentList.Add(pdfPath);

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                    return (false, string.Empty, "Unable to start Python process.");

                await process.StandardInput.WriteAsync(PythonScript.AsMemory(), ct);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(ct);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    var error = string.IsNullOrWhiteSpace(stderr)
                        ? $"Python exited with code {process.ExitCode}."
                        : stderr.Trim();
                    return (false, string.Empty, error);
                }

                return (true, stdout ?? string.Empty, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        public static bool TryExtractPdfMetadataSignature(string pdfPath, out string signature, out string? error)
        {
            signature = string.Empty;
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                {
                    error = "PDF file not found.";
                    return false;
                }

                var pythonExe = ResolvePythonExecutable();
                if (string.IsNullOrWhiteSpace(pythonExe))
                {
                    error = "Python executable not found.";
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("-");
                startInfo.ArgumentList.Add(pdfPath);

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    error = "Unable to start Python process.";
                    return false;
                }

                process.StandardInput.Write(PdfMetadataScript);
                process.StandardInput.Close();

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    error = string.IsNullOrWhiteSpace(stderr)
                        ? $"Python exited with code {process.ExitCode}."
                        : stderr.Trim();
                    return false;
                }

                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
                var root = document.RootElement;
                signature = BuildMetadataSignature(
                    root.GetPropertyOrDefault("title"),
                    root.GetPropertyOrDefault("author"),
                    root.GetPropertyOrDefault("subject"),
                    root.GetPropertyOrDefault("keywords"),
                    root.GetPropertyOrDefault("creator"),
                    root.GetPropertyOrDefault("producer"),
                    root.GetPropertyOrDefault("creationDate"),
                    root.GetPropertyOrDefault("modDate"),
                    root.TryGetProperty("pageCount", out var pageCountElement) && pageCountElement.TryGetInt32(out var pageCountValue) ? pageCountValue : 0,
                    root.TryGetProperty("fileSize", out var fileSizeElement) && fileSizeElement.TryGetInt64(out var fileSizeValue) ? fileSizeValue : 0L);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string BuildMetadataSignature(
            string title,
            string author,
            string subject,
            string keywords,
            string creator,
            string producer,
            string creationDate,
            string modDate,
            int pageCount,
            long fileSize)
        {
            static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

            return string.Join("|", new[]
            {
                Clean(title),
                Clean(author),
                Clean(subject),
                Clean(keywords),
                Clean(creator),
                Clean(producer),
                Clean(creationDate),
                Clean(modDate),
                pageCount.ToString(),
                fileSize.ToString()
            });
        }

        private static string? ResolvePythonExecutable()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("REPLICA_PYTHON_EXECUTABLE"),
                Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe"),
                Path.Combine(localAppData, "Programs", "Python", "Python313", "python.exe"),
                Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe"),
                Path.Combine(localAppData, "Programs", "Python", "Python310", "python.exe"),
                Path.Combine(programFiles, "Python312", "python.exe"),
                Path.Combine(programFiles, "Python313", "python.exe"),
                Path.Combine(programFiles, "Python311", "python.exe"),
                Path.Combine(programFilesX86, "Python312", "python.exe"),
                Path.Combine(programFilesX86, "Python313", "python.exe"),
                Path.Combine(programFilesX86, "Python311", "python.exe"),
            };

            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        }
    }
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

}
