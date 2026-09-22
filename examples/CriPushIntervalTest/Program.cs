// =============================================================================
// CriPushIntervalTest — CRI 数据推送间隔测试
// -----------------------------------------------------------------------------
// 测量控制器 CRI UDP 数据推送的实际帧间隔，找到最小可用周期。
//
// 测试方法：
//   1. 开启 CRI 数据推送
//   2. 通过 CriDataReceived 事件回调记录每帧的时间戳
//   3. 统计帧间隔分布（最小/最大/平均/标准差/P50/P95/P99）
//   4. 采样结束后输出报告
//
// 注意：CRI 推送间隔由控制器固件决定，本测试只测量实际值。
//       控制器可能以固定 4ms 周期推送，也可能因网络抖动产生波动。
//
// 用法：
//   dotnet run --project examples/CriPushIntervalTest                                   // 默认 IP，采样 10 秒
//   dotnet run --project examples/CriPushIntervalTest -- 192.168.1.136                // 指定控制器 IP
//   dotnet run --project examples/CriPushIntervalTest -- 192.168.1.136 192.168.1.150  // 指定控制器 + 本机 IP
//   dotnet run --project examples/CriPushIntervalTest -- 192.168.1.136 192.168.1.150 30 // 采样 30 秒
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace CriPushIntervalTest;

internal static class Program
{
    private const string DefaultRobotIp = "192.168.1.136";
    private const string DefaultLocalIp = "192.168.1.150";
    private const int DefaultLocalUdpPort = 18888;
    private const int DefaultSampleSeconds = 10;

    // 用于记录每帧的高精度时间戳（Stopwatch ticks）
    private static readonly List<long> _frameTicks = new(10_000);
    private static long _firstFrameTick;
    private static int _totalFrames;
    private static long _lastFrameTick;
    private static long _lastControllerTimestamp;
    private static readonly List<double> _intervalsMs = new(10_000);
    private static readonly List<double> _controllerIntervalsMs = new(10_000);

    private static async Task<int> Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();

        var (robotIp, localIp, sampleSeconds) = ParseArgs(args);

        PrintBanner($"CRI 推送间隔测试  |  控制器: {robotIp}  |  本机 UDP: {localIp}:{DefaultLocalUdpPort}", ConsoleColor.White);
        Console.WriteLine($"  采样时长: {sampleSeconds} 秒（Ctrl+C 提前结束）");

        var robot = new CodroidClient(robotIp);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(sampleSeconds));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            PrintStep(1, "TCP 连接 + 切远程 + 上电");
            await robot.ConnectRemoteAndSwitchOn();
            PrintOk("已连接，已切远程并上电。");

            PrintStep(2, "注册 CriDataReceived 事件回调");
            robot.CriDataReceived += OnCriDataReceived;
            PrintOk("事件回调已注册。");

            PrintStep(3, $"开启 CRI 数据推送 → 本机 {localIp}:{DefaultLocalUdpPort}");
            await robot.StartCriDataPush(localIp, DefaultLocalUdpPort);
            PrintOk("CRI 数据推送已开启。");

            PrintStep(4, $"采样中…（{sampleSeconds} 秒）");
            Console.WriteLine();

            // 等待采样结束
            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (TaskCanceledException) { }

            Console.WriteLine();

            PrintStep(5, "关闭 CRI 数据推送");
            await robot.StopCriDataPush(localIp, DefaultLocalUdpPort);
            PrintOk("CRI 数据推送已关闭。");

            PrintSeparator();
            PrintReport();

            PrintBanner("CRI 推送间隔测试结束", ConsoleColor.Green);
            return 0;
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器错误", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
                Console.WriteLine("  err: " + ex.ControllerError);
            return 1;
        }
        catch (Exception ex)
        {
            PrintBanner("运行异常", ConsoleColor.Red);
            PrintErr(ex.Message);
            return 1;
        }
        finally
        {
            try { robot.Disconnect(); } catch { }
            PrintOk("TCP 已断开。");
        }
    }

    // -------------------------------------------------------------------------
    // 每帧回调
    // -------------------------------------------------------------------------
    private static void OnCriDataReceived(CriRealTimeData data)
    {
        var tick = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_totalFrames == 0)
        {
            _firstFrameTick = tick;
            _lastFrameTick = tick;
            _lastControllerTimestamp = data.TimestampMs;
        }
        else
        {
            double intervalMs = (tick - _lastFrameTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _intervalsMs.Add(intervalMs);

            // 控制器时间戳间隔
            if (data.TimestampMs > 0 && _lastControllerTimestamp > 0)
            {
                double ctrlIntervalMs = data.TimestampMs - _lastControllerTimestamp;
                if (ctrlIntervalMs > 0 && ctrlIntervalMs < 10000)
                    _controllerIntervalsMs.Add(ctrlIntervalMs);
            }
            _lastControllerTimestamp = data.TimestampMs;
        }

        _lastFrameTick = tick;
        _totalFrames++;
    }

    // -------------------------------------------------------------------------
    // 统计报告
    // -------------------------------------------------------------------------
    private static void PrintReport()
    {
        Console.WriteLine("  ╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║           CRI 数据推送间隔统计报告                    ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var totalMs = (_lastFrameTick - _firstFrameTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"  采样总时长:  {totalMs / 1000:F2} 秒");
        Console.WriteLine($"  总帧数:      {_totalFrames}");
        Console.WriteLine($"  平均帧率:    {_totalFrames / (totalMs / 1000):F1} fps");
        Console.WriteLine($"  整体平均间隔: {totalMs / Math.Max(1, _totalFrames):F3} ms");
        Console.WriteLine();

        if (_intervalsMs.Count == 0)
        {
            Console.WriteLine("  [!] 采样帧数不足（< 2 帧），无法统计间隔。");
            return;
        }

        // --- 本机测量间隔 ---
        Console.WriteLine("  ─── 本机 Stopwatch 测量间隔 (ms) ───");
        PrintStats(_intervalsMs);

        // --- 控制器时间戳间隔 ---
        if (_controllerIntervalsMs.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ─── 控制器时间戳间隔 (ms) ───");
            PrintStats(_controllerIntervalsMs);
        }

        // --- 直方图 ---
        Console.WriteLine();
        PrintHistogram(_intervalsMs, "本机测量间隔分布");

        // --- 结论 ---
        Console.WriteLine();
        var p95 = Percentile(_intervalsMs, 95);
        var min = _intervalsMs.Min();
        var avg = _intervalsMs.Average();
        Console.WriteLine("  ─── 结论 ───");
        Console.WriteLine($"  最小间隔: {min:F3} ms");
        Console.WriteLine($"  平均间隔: {avg:F3} ms");
        Console.WriteLine($"  P95 间隔: {p95:F3} ms");
        Console.WriteLine($"  最大间隔: {_intervalsMs.Max():F3} ms");
        Console.WriteLine($"  抖动范围: {_intervalsMs.Max() - min:F3} ms (Max - Min)");
        Console.WriteLine($"  标准差:   {StdDev(_intervalsMs):F3} ms");

        Console.WriteLine();
        var recommendedCycle = Math.Ceiling(p95);
        Console.WriteLine($"  建议最小下发周期: ≥ {recommendedCycle:F0} ms (基于 P95)");
    }

    private static void PrintStats(List<double> data)
    {
        var sorted = data.OrderBy(x => x).ToList();
        Console.WriteLine($"    最小:  {sorted[0]:F3}");
        Console.WriteLine($"    P25:   {Percentile(sorted, 25):F3}");
        Console.WriteLine($"    P50:   {Percentile(sorted, 50):F3}");
        Console.WriteLine($"    平均:  {data.Average():F3}");
        Console.WriteLine($"    P75:   {Percentile(sorted, 75):F3}");
        Console.WriteLine($"    P90:   {Percentile(sorted, 90):F3}");
        Console.WriteLine($"    P95:   {Percentile(sorted, 95):F3}");
        Console.WriteLine($"    P99:   {Percentile(sorted, 99):F3}");
        Console.WriteLine($"    最大:  {sorted[^1]:F3}");
        Console.WriteLine($"    标准差: {StdDev(data):F3}");
    }

    private static void PrintHistogram(List<double> data, string title)
    {
        Console.WriteLine($"  ─── {title} ───");
        var min = data.Min();
        var max = data.Max();
        if (max - min < 0.001)
        {
            Console.WriteLine($"    所有帧间隔集中在 {min:F3} ms（无分布）");
            return;
        }

        const int bucketCount = 20;
        var bucketSize = (max - min) / bucketCount;
        var buckets = new int[bucketCount];
        foreach (var v in data)
        {
            var idx = (int)((v - min) / bucketSize);
            if (idx >= bucketCount) idx = bucketCount - 1;
            buckets[idx]++;
        }

        var maxCount = buckets.Max();
        for (int i = 0; i < bucketCount; i++)
        {
            var lo = min + i * bucketSize;
            var hi = lo + bucketSize;
            var barLen = (int)(buckets[i] * 40.0 / Math.Max(1, maxCount));
            var bar = new string('█', barLen);
            Console.WriteLine($"    [{lo,7:F2} - {hi,7:F2}] {buckets[i],6}  {bar}");
        }
    }

    // -------------------------------------------------------------------------
    // 统计辅助
    // -------------------------------------------------------------------------
    private static double Percentile(List<double> sortedData, double percentile)
    {
        if (sortedData.Count == 0) return 0;
        if (sortedData.Count == 1) return sortedData[0];
        var sorted = sortedData.OrderBy(x => x).ToList();
        var idx = (percentile / 100.0) * (sorted.Count - 1);
        var lo = (int)Math.Floor(idx);
        var hi = (int)Math.Ceiling(idx);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    private static double StdDev(List<double> data)
    {
        if (data.Count < 2) return 0;
        var avg = data.Average();
        var sumSq = data.Sum(x => (x - avg) * (x - avg));
        return Math.Sqrt(sumSq / (data.Count - 1));
    }

    // -------------------------------------------------------------------------
    // 命令行解析
    // -------------------------------------------------------------------------
    private static (string robotIp, string localIp, int sampleSeconds) ParseArgs(string[] args)
    {
        string ip = DefaultRobotIp;
        string localIp = DefaultLocalIp;
        int seconds = DefaultSampleSeconds;

        var argv = args ?? Array.Empty<string>();
        if (argv.Length >= 1) ip = argv[0];
        if (argv.Length >= 2) localIp = argv[1];
        if (argv.Length >= 3 && int.TryParse(argv[2], out var s)) seconds = s;

        return (ip, localIp, seconds);
    }

    // -------------------------------------------------------------------------
    // 控制台输出辅助
    // -------------------------------------------------------------------------
    private static void PrintBanner(string title, ConsoleColor color)
    {
        var bar = new string('=', Math.Max(20, title.Length + 4));
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine("  " + title);
        Console.WriteLine(bar);
        Console.ForegroundColor = prev;
    }

    private static void PrintStep(int step, string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"[{step:D2}] {text}");
        Console.ForegroundColor = prev;
    }

    private static void PrintOk(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ✔ " + text);
        Console.ForegroundColor = prev;
    }

    private static void PrintErr(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  ✘ " + text);
        Console.ForegroundColor = prev;
    }

    private static void PrintSeparator()
    {
        Console.WriteLine("  " + new string('-', 60));
    }
}
