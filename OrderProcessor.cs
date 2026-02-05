using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MyManager
{
    public class OrderProcessor
    {
        public event Action<string, string> OnStatusChanged;
        public event Action<string> OnLog;

        private readonly string _rootPath;

        public OrderProcessor(string rootPath)
        {
            _rootPath = rootPath;
        }

        public async Task RunAsync(OrderData order, CancellationToken ct)
        {
            var settings = AppSettings.Load();
            var timeout = TimeSpan.FromMinutes(settings.RunTimeoutMinutes);

            try
            {
                Logger.Info($">>> СТАРТ: Заказ {order.Id}");
                Notify(order, "🟡 Запуск…", "Поиск конфигов...");

                var pitCfg = ConfigService.GetPitStopConfigByName(order.PitStopAction);
                var impCfg = ConfigService.GetImposingConfigByName(order.ImposingAction);

                if (pitCfg == null && impCfg == null)
                {
                    Notify(order, "Новый", "Сценарии не выбраны.");
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
                        order.PreparedPath = CopyIntoStage(order, 2, okFile, newName);
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

                    order.PrintPath = CopyIntoStage(order, 3, outFile, $"{order.Id}.pdf");
                    try { File.Delete(outFile); } catch { }
                }

                Notify(order, "✅ Готово", "Заказ успешно выполнен.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка в {order.Id}: {ex.Message}");
                Notify(order, "🔴 Ошибка", ex.Message);
            }
        }

        private void Notify(OrderData o, string s, string l) { OnStatusChanged?.Invoke(o.Id, s); OnLog?.Invoke(l); }

        private string CopyIntoStage(OrderData o, int stage, string src, string name)
        {
            string sub = stage switch { 1 => "1. исходные", 2 => "2. подготовка", 3 => "3. печать", _ => "" };
            string path = Path.Combine(_rootPath, o.FolderName, sub);
            Directory.CreateDirectory(path);
            string dest = Path.Combine(path, name);
            File.Copy(src, dest, true);
            return dest;
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