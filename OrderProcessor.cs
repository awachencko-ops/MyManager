using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyManager
{
    public class OrderProcessor
    {
        public event Action<string, string, string> OnStatusChanged;
        public event Action<string> OnLog;

        private readonly string _rootPath;
        private const string TempInFolder = "in";
        private const string TempPrepressFolder = "prepress";
        private const string TempPrintFolder = "print";

        public OrderProcessor(string rootPath)
        {
            _rootPath = rootPath;
        }

        public async Task RunAsync(OrderData order, CancellationToken ct)
        {
            var settings = AppSettings.Load();
            var timeout = TimeSpan.FromMinutes(settings.RunTimeoutMinutes);
            bool useExtendedMode = order.StartMode == OrderStartMode.Unknown
                ? settings.UseExtendedMode
                : order.StartMode == OrderStartMode.Extended;
            string tempRoot = string.IsNullOrWhiteSpace(settings.TempFolderPath)
                ? Path.Combine(_rootPath, settings.TempFolderName)
                : settings.TempFolderPath;
            EnsureTempFolders(tempRoot);

            try
            {
                Logger.Info($">>> СТАРТ: Заказ {order.Id}");
                Notify(order, "🟡 Запуск…", "Поиск конфигов...");

                var pitCfg = ConfigService.GetPitStopConfigByName(order.PitStopAction);
                var impCfg = ConfigService.GetImposingConfigByName(order.ImposingAction);

                if (pitCfg == null && impCfg == null)
                {
                    Notify(order, "⚪ Ожидание", "Сценарии не выбраны.");
                    return;
                }

                // --- PITSTOP ---
                if (pitCfg != null)
                {
                    if (!File.Exists(order.PreparedPath)) throw new Exception("Файл для PitStop не найден.");

                    string fileName = Path.GetFileName(order.PreparedPath);
                    string targetIn = Path.Combine(pitCfg.InputFolder, fileName);

                    Notify(order, "🟡 PitStop: копирование", "Копирую в Hotfolder PitStop...");
                    Logger.Info($"Копирование: {order.PreparedPath} -> {targetIn}");
                    File.Copy(order.PreparedPath, targetIn, true);

                    if (impCfg != null)
                    {
                        var places = new (string folder, string label)[] {
                            (pitCfg.ProcessedSuccess, "PitStop Success"),
                            (pitCfg.ProcessedError,   "PitStop Error"),
                            (impCfg.In,               "Imposing In"),
                            (impCfg.Out,              "Imposing Out")
                        };
                        var (foundPath, where) = await WaitForFileInAnyAsync(places, fileName, timeout, ct);
                        if (foundPath == null) throw new Exception("Таймаут PitStop.");
                        if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                        Notify(order, "🟡 PitStop OK", $"PitStop завершен ({where})");
                    }
                    else
                    {
                        string okFile = await WaitForFileAsync(pitCfg.ProcessedSuccess, fileName, timeout, ct);
                        if (okFile == null) throw new Exception("Таймаут PitStop.");
                        string newName = $"{Path.GetFileNameWithoutExtension(fileName)}_pitstop{Path.GetExtension(fileName)}";
                        order.PreparedPath = CopyIntoStage(order, 2, okFile, newName, tempRoot);
                        Notify(order, "🟡 PitStop готово", "Версия сохранена.");
                    }
                }

                // --- IMPOSING ---
                if (impCfg != null)
                {
                    string fileName = Path.GetFileName(order.PreparedPath);
                    string targetIn = Path.Combine(impCfg.In, fileName);

                    if (!File.Exists(targetIn))
                    {
                        Notify(order, "🟡 Imposing: старт", "Копирую в Hotfolder Imposing...");
                        File.Copy(order.PreparedPath, targetIn, true);
                    }

                    string outFile = await WaitForFileAsync(impCfg.Out, fileName, timeout, ct);
                    if (outFile == null) throw new Exception("Таймаут Imposing.");

                    string printName = $"{order.Id}.pdf";
                    if (useExtendedMode)
                    {
                        order.PrintPath = CopyIntoStage(order, 3, outFile, printName, tempRoot);
                        try { File.Delete(outFile); } catch { }
                    }
                    else
                    {
                        order.PrintPath = CopyToGrandpa(outFile, printName, settings.GrandpaPath);
                        try { File.Delete(outFile); } catch { }
                    }
                }

                if (!string.IsNullOrEmpty(order.PrintPath) && File.Exists(order.PrintPath))
                {
                    if (useExtendedMode)
                        MoveTempToOrderFolder(order, tempRoot);
                    else if (!IsInGrandpa(order.PrintPath, settings.GrandpaPath))
                        MovePrintToGrandpa(order, settings.GrandpaPath);
                }

                Notify(order, "✅ Готово", "Заказ успешно выполнен.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка в {order.Id}: {ex.Message}");
                Notify(order, "🔴 Ошибка", ex.Message);
            }
        }

        private void Notify(OrderData o, string s, string l)
        {
            OnStatusChanged?.Invoke(o.Id, s, l);
            OnLog?.Invoke(l);
            Logger.Info($"STATUS | order={o.Id} | source=processor | status={s} | reason={l}");
        }

        private string CopyIntoStage(OrderData o, int stage, string src, string name, string rootPath)
        {
            string sub = stage switch { 1 => TempInFolder, 2 => TempPrepressFolder, 3 => TempPrintFolder, _ => "" };
            string path = Path.Combine(rootPath, sub);
            Directory.CreateDirectory(path);
            string dest = Path.Combine(path, name);
            File.Copy(src, dest, true);
            return dest;
        }

        private void MoveTempToOrderFolder(OrderData order, string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(order.FolderName)) return;

            string sourcePath = MoveFileIfExists(order.SourcePath, GetOrderStagePath(order, 1));
            string preparedPath = MoveFileIfExists(order.PreparedPath, GetOrderStagePath(order, 2));
            string printPath = MoveFileIfExists(order.PrintPath, GetOrderStagePath(order, 3));

            if (!string.IsNullOrEmpty(sourcePath)) order.SourcePath = sourcePath;
            if (!string.IsNullOrEmpty(preparedPath)) order.PreparedPath = preparedPath;
            if (!string.IsNullOrEmpty(printPath)) order.PrintPath = printPath;

            TryDeleteEmptyFolders(tempRoot);
        }

        private void MovePrintToGrandpa(OrderData order, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(grandpaPath)) return;
            Directory.CreateDirectory(grandpaPath);

            string target = Path.Combine(grandpaPath, Path.GetFileName(order.PrintPath));
            MoveFileWithOverwrite(order.PrintPath, target);
            order.PrintPath = target;
            TryCopyPathToClipboard(target);

            if (!string.IsNullOrEmpty(order.SourcePath) && File.Exists(order.SourcePath))
            {
                try { File.Delete(order.SourcePath); } catch { }
            }
        }

        private string CopyToGrandpa(string sourcePath, string fileName, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(grandpaPath)) return sourcePath;
            Directory.CreateDirectory(grandpaPath);
            string target = Path.Combine(grandpaPath, fileName);
            File.Copy(sourcePath, target, true);
            TryCopyPathToClipboard(target);
            return target;
        }

        private void TryCopyPathToClipboard(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                    Clipboard.SetText(path);
            }
            catch
            {
            }
        }

        private bool IsInGrandpa(string path, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(grandpaPath)) return false;
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string fullGrandpa = Path.GetFullPath(grandpaPath).TrimEnd(Path.DirectorySeparatorChar);
            return fullPath.StartsWith(fullGrandpa + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, fullGrandpa, StringComparison.OrdinalIgnoreCase);
        }

        private string GetOrderStagePath(OrderData order, int stage)
        {
            string sub = stage switch { 1 => "1. исходные", 2 => "2. подготовка", 3 => "3. печать", _ => "" };
            string path = Path.Combine(_rootPath, order.FolderName, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private string MoveFileIfExists(string sourcePath, string targetFolder)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return "";
            string targetPath = Path.Combine(targetFolder, Path.GetFileName(sourcePath));
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                return sourcePath;
            MoveFileWithOverwrite(sourcePath, targetPath);
            return targetPath;
        }

        private void MoveFileWithOverwrite(string sourcePath, string targetPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;
            if (File.Exists(targetPath))
            {
                try { File.Delete(targetPath); } catch { }
            }
            File.Move(sourcePath, targetPath);
        }

        private void EnsureTempFolders(string tempRoot)
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, TempInFolder));
            Directory.CreateDirectory(Path.Combine(tempRoot, TempPrepressFolder));
            Directory.CreateDirectory(Path.Combine(tempRoot, TempPrintFolder));
        }

        private void TryDeleteEmptyFolders(string tempRoot)
        {
            try
            {
                foreach (var folder in new[] { TempInFolder, TempPrepressFolder, TempPrintFolder })
                {
                    string path = Path.Combine(tempRoot, folder);
                    if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
                        Directory.Delete(path, true);
                }
            }
            catch { }
        }

        private async Task<string> WaitForFileAsync(string folder, string fileName, TimeSpan timeout, CancellationToken ct)
        {
            string full = Path.Combine(folder, fileName);
            var start = DateTime.Now;
            long lastSize = -1;
            while (DateTime.Now - start < timeout)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(full))
                {
                    FileInfo fi = new FileInfo(full);
                    if (fi.Length > 0 && fi.Length == lastSize && IsFileReady(full)) return full;
                    lastSize = fi.Length;
                }
                await Task.Delay(1000, ct);
            }
            return null;
        }

        private async Task<(string path, string where)> WaitForFileInAnyAsync((string folder, string label)[] places, string fileName, TimeSpan timeout, CancellationToken ct)
        {
            var start = DateTime.Now;
            while (DateTime.Now - start < timeout)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var p in places)
                {
                    if (string.IsNullOrEmpty(p.folder)) continue;
                    string full = Path.Combine(p.folder, fileName);
                    if (File.Exists(full) && IsFileReady(full)) return (full, p.label);
                }
                await Task.Delay(1000, ct);
            }
            return (null, null);
        }

        private bool IsFileReady(string path)
        {
            try { using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
            catch { return false; }
        }
    }
}
