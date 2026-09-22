// =============================================================================
// PublishSubscribeExample — 主题订阅示例
// -----------------------------------------------------------------------------
// 演示如何通过 TCP 订阅控制器推送的主题消息（协议 15.x）。
//
// 可订阅主题：
//   publish/ProjectState  — 工程状态
//   publish/VarUpdate     — 变量数据更新
//   publish/RobotStatus   — 机器人状态
//   publish/RobotPosture  — 机器人姿态
//   publish/RobotCoordinate — 机器人坐标
//   publish/Log            — 日志
//   publish/Error          — 错误/报警
//
// 用法：
//   dotnet run --project examples/PublishSubscribeExample                              // 默认 IP，订阅全部主题
//   dotnet run --project examples/PublishSubscribeExample -- 192.168.1.136            // 指定控制器 IP
//   dotnet run --project examples/PublishSubscribeExample -- 192.168.1.136 status     // 仅订阅 RobotStatus
//   dotnet run --project examples/PublishSubscribeExample -- 192.168.1.136 all 50     // 指定 tc=50ms
//
// 流程：
//   1. TCP 连接控制器
//   2. 订阅选定的主题（发送 {ty, tc} 帧，不含 id）
//   3. 控制器在数据变化时推送消息，回调在线程池执行
//   4. 运行 30 秒后自动取消订阅并断开（Ctrl+C 提前退出）
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace PublishSubscribeExample;

internal static class Program
{
    private const string DefaultRobotIp = "192.168.1.136";
    private const int RunDurationMs = 30_000; // 30 秒后自动退出

    private static async Task<int> Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();

        var (robotIp, topicFilter, tcMs) = ParseArgs(args);
        var topics = SelectTopics(topicFilter);

        PrintBanner($"主题订阅示例  |  控制器: {robotIp}  |  tc={tcMs}ms", ConsoleColor.White);
        Console.WriteLine($"  订阅主题 ({topics.Count} 个):");
        foreach (var t in topics)
            Console.WriteLine($"    • {t}");
        Console.WriteLine($"  运行时长: {RunDurationMs / 1000} 秒（Ctrl+C 提前退出）");

        var robot = new CodroidClient(robotIp);
        var subscriptions = new List<IDisposable>();

        using var cts = new CancellationTokenSource(RunDurationMs);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            PrintStep(1, "TCP 连接控制器");
            await robot.Connect();
            PrintOk("已连接。");

            PrintStep(2, "逐个订阅主题");
            foreach (var topic in topics)
            {
                var sub = await robot.SubscribePublishTopic(
                    topic,
                    notification => OnNotification(topic, notification),
                    tcMilliseconds: tcMs);
                subscriptions.Add(sub);
                PrintOk($"已订阅: {topic}");
            }

            PrintStep(3, "等待推送消息（数据变化时触发）…");
            Console.WriteLine("  ──────────────────────────────────────");

            // 等待取消（超时或 Ctrl+C）
            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (TaskCanceledException) { }

            Console.WriteLine("  ──────────────────────────────────────");

            PrintStep(4, "取消订阅");
            foreach (var sub in subscriptions)
            {
                sub.Dispose();
            }
            PrintOk($"已取消 {subscriptions.Count} 个订阅。");

            PrintBanner("主题订阅示例结束", ConsoleColor.Green);
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
    // 推送回调
    // -------------------------------------------------------------------------
    private static int _messageCount = 0;

    private static void OnNotification(string topic, PublishNotification n)
    {
        Interlocked.Increment(ref _messageCount);
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        var shortTopic = topic.Replace("publish/", "");

        Console.WriteLine($"  [{ts}] #{_messageCount,3} {shortTopic}");

        // 打印 db 内容（截断过长的内容）
        var dbStr = n.Db.ValueKind == JsonValueKind.Undefined ? "(空)" : n.Db.GetRawText();
        if (dbStr.Length > 200)
            dbStr = dbStr[..200] + "…";
        Console.WriteLine($"         db: {dbStr}");
    }

    // -------------------------------------------------------------------------
    // 主题选择
    // -------------------------------------------------------------------------
    private static List<string> SelectTopics(string filter)
    {
        var all = new List<string>
        {
            PublishTopics.ProjectState,
            PublishTopics.RobotStatus,
            PublishTopics.RobotPosture,
            PublishTopics.VarUpdate,
            PublishTopics.Log,
            PublishTopics.Error,
        };

        if (filter == "all" || string.IsNullOrEmpty(filter))
            return all;

        return filter.ToLower() switch
        {
            "status"   => new() { PublishTopics.RobotStatus },
            "state"    => new() { PublishTopics.ProjectState },
            "posture"  => new() { PublishTopics.RobotPosture },
            "var"      => new() { PublishTopics.VarUpdate },
            "log"      => new() { PublishTopics.Log },
            "error"    => new() { PublishTopics.Error },
            _          => all,
        };
    }

    // -------------------------------------------------------------------------
    // 命令行解析
    // -------------------------------------------------------------------------
    private static (string robotIp, string topicFilter, int tcMs) ParseArgs(string[] args)
    {
        string ip = DefaultRobotIp;
        string filter = "all";
        int tc = 100;

        var argv = args ?? Array.Empty<string>();
        if (argv.Length >= 1) ip = argv[0];
        if (argv.Length >= 2) filter = argv[1];
        if (argv.Length >= 3 && int.TryParse(argv[2], out var t)) tc = t;

        return (ip, filter, tc);
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
}
