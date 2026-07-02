using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A centralized, thread-safe logging system that outputs to both the Godot console
/// and a persistent log file (user://logs/latest.log). Designed to never stall the
/// main thread or chunk generation threads on disk I/O.
/// </summary>
public static class Logger
{
    private static readonly ConcurrentQueue<string> _logQueue = new();
    private static readonly ManualResetEventSlim _flushSignal = new(false);
    private static readonly CancellationTokenSource _cts = new();
    private static readonly string _logDir;
    private static readonly string _logPath;

    static Logger()
    {
        _logDir = ProjectSettings.GlobalizePath("user://logs");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _logPath = Path.Combine(_logDir, $"tues_log_{timestamp}.log");

        try
        {
            if (!Directory.Exists(_logDir))
            {
                Directory.CreateDirectory(_logDir);
            }

            // Start new log
            File.WriteAllText(_logPath, $"=== TUES Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch (Exception e)
        {
            GD.PushError($"[Logger] Failed to initialize log file at {_logPath}: {e}");
        }

        // Start the dedicated background writer thread
        Task.Factory.StartNew(() => WriteLoop(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// Forces the logger to flush remaining logs and stop. Should only be called on application exit.
    /// </summary>
    public static void Shutdown()
    {
        _cts.Cancel();
        _flushSignal.Set();
    }

    private static void WriteLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                _flushSignal.Wait(token);
                _flushSignal.Reset();
                FlushQueue();
            }
        }
        catch (OperationCanceledException)
        {
            // Final flush on shutdown
            FlushQueue();
        }
    }

    private static void FlushQueue()
    {
        if (_logQueue.IsEmpty) return;

        try
        {
            using var writer = new StreamWriter(_logPath, append: true);
            while (_logQueue.TryDequeue(out string? msg))
            {
                writer.WriteLine(msg);
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Logger] Failed to write to log file: {e}");
        }
    }

    private static void LogInternal(string level, string message, Action<string> godotPrintFunc)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        int threadId = System.Environment.CurrentManagedThreadId;
        string formattedMessage = $"[{timestamp}] [{level}] [T{threadId}] {message}";

        // 1. Output to Godot console immediately
        godotPrintFunc(formattedMessage);

        // 2. Queue for disk write
        _logQueue.Enqueue(formattedMessage);
        _flushSignal.Set();
    }

    public static void Debug(string message)
    {
        LogInternal("DEBUG", message, GD.Print);
    }

    public static void Info(string message)
    {
        LogInternal("INFO ", message, GD.Print);
    }

    public static void Warning(string message)
    {
        LogInternal("WARN ", message, GD.PushWarning);
    }

    public static void Error(string message)
    {
        LogInternal("ERROR", message, GD.PushError);
    }
}
