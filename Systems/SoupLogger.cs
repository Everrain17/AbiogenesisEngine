using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AbiogenesisEngine.Systems;

public static class SoupLogger
{
    private static readonly ConcurrentQueue<string> _logQueue = new();
    private static readonly string _logFilePath;
    private static readonly CancellationTokenSource _cts = new();
    private static bool _isInitialized = false;

    static SoupLogger()
    {
        string localDataFolder = @"D:\repos\EvolveGenEngineAgentBM\AbiogenesisEngine\Data";
        Directory.CreateDirectory(localDataFolder);
        _logFilePath = Path.Combine(localDataFolder, "soup_evolution.log");

        try
        {
            // ИСПРАВЛЕНО: Вместо File.WriteAllText используем File.AppendAllText! 
            // Теперь лог никогда не затирается, а новые сессии дописываются вниз с красивым разделителем!
            File.AppendAllText(_logFilePath, $"\n\n--- [CONTINUE BIOGENESIS LOCAL LOG - {DateTime.Now}] ---\n");
        }
        catch { }
    }

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                if (!_logQueue.IsEmpty)
                {
                    using var writer = new StreamWriter(_logFilePath, append: true);
                    while (_logQueue.TryDequeue(out string logMessage))
                    {
                        await writer.WriteLineAsync(logMessage);
                    }
                }
                await Task.Delay(30);
            }
        });
    }

    public static void Log(string message)
    {
        string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        _logQueue.Enqueue(timestampedMessage);
    }

    public static void Shutdown()
    {
        _cts.Cancel();
        if (!_logQueue.IsEmpty)
        {
            using var writer = new StreamWriter(_logFilePath, append: true);
            while (_logQueue.TryDequeue(out string logMessage)) writer.WriteLine(logMessage);
        }
    }
}
