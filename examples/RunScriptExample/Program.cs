// =============================================================================
// RunScriptExample — 使用 project/runScript 下发脚本文件
// -----------------------------------------------------------------------------
// 用法：
//   dotnet run --project examples/RunScriptExample                              // 使用默认 IP 和默认脚本路径
//   dotnet run --project examples/RunScriptExample -- 192.168.1.136            // 指定控制器 IP
//   dotnet run --project examples/RunScriptExample -- 192.168.1.136 192.168.1.24  // 指定控制器 IP + 本机 IP (用于 CRI)
//   dotnet run --project examples/RunScriptExample -- "D:\path\to\script.lua"  // 指定脚本文件路径
//
// 默认脚本路径: D:\C#proj\CodroidCS-main\test.lua
// 默认控制器 IP: 192.168.1.136
// 默认本机 UDP IP (CRI): 192.168.1.24
//
// 流程:
//   1. TCP 连接 → 切自动 → 切远程 → 上电
//   2. (可选) 开启 CRI 数据推送，用于等待运动完成
//   3. 读取 test.lua 脚本文件内容
//   4. 停止当前工程 + 等待 1s
//   5. 调用 RunScript(mainScript, subThreads, subPrograms, interrupts, vars)
//   6. (可选) 等待 InMotion 停稳，确认运动结束
//   7. 清理：关闭 CRI 推送、断开 TCP
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace RunScriptExample;

internal static class Program
{
    private const string DefaultRobotIp = "192.168.1.136";
    private const string DefaultLocalIp = "192.168.1.150";
    private const int DefaultLocalUdpPort = 18888;
    private const string DefaultScriptPath = @"D:\C#proj\CodroidCS-main\test.lua";

    private const int FirstFrameTimeoutMs = 3000;
    private const int MotionSettleTimeoutMs = 600_000; // 10 分钟，足够长的运动

    private static async Task<int> Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();

        var opts = ParseArgs(args);
        PrintBanner($"RunScript 脚本下发示例  |  控制器: {opts.RobotIp}  |  本机 UDP: {opts.LocalIp}:{DefaultLocalUdpPort}", ConsoleColor.White);
        Console.WriteLine($"  脚本文件: {opts.ScriptPath}");
        Console.WriteLine($"  等待运动: {(opts.WaitMotion ? "启用（使用 CRI InMotion 判定）" : "跳过（仅下发不等待）")}");

        // 检查脚本文件是否存在
        if (!File.Exists(opts.ScriptPath))
        {
            PrintErr($"找不到脚本文件: {opts.ScriptPath}");
            PrintErr("请将脚本文件放在上述路径，或通过命令行指定路径。");
            return 2;
        }

        string mainScript;
        try
        {
            mainScript = File.ReadAllText(opts.ScriptPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            PrintErr($"读取脚本文件失败: {ex.Message}");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(mainScript))
        {
            PrintErr("脚本文件内容为空。");
            return 2;
        }

        PrintOk($"已读取脚本，{mainScript.Length} 字符，{mainScript.Split('\n').Length} 行");
        Console.WriteLine("  脚本预览（前 5 行）:");
        var previewLines = mainScript.Split('\n').Take(5);
        foreach (var line in previewLines)
        {
            Console.WriteLine($"    {line}");
        }

        // 可选：如果需要传递变量 (vars)、子线程、子程序、中断脚本，在这里构建
        // 示例：
        //   var vars = new Dictionary<string, object>
        //   {
        //       ["p1"] = new[] { 927.5, 214.5, 899.0, 180.0, 0.0, -90.0 },
        //       ["p2"] = new[] { 1140.0, 214.5, 899.0, -91.5, 0.0, -90.0 },
        //       ["speed"] = 1000,
        //   };
        //
        //   var subThreads = new Dictionary<string, string>
        //   {
        //       ["thread1"] = "while true do\n  sleep(100)\nend",
        //   };
        Dictionary<string, object>? vars = null;
        Dictionary<string, string>? subThreads = null;
        Dictionary<string, string>? subPrograms = null;
        Dictionary<string, string>? interrupts = null;

        var robot = new CodroidClient(opts.RobotIp);
        try
        {
            PrintStep(1, "TCP 连接 → 切自动 → 切远程 → 上电");
            await robot.ConnectRemoteAndSwitchOn();
            PrintOk("已连接，已切远程并上电。");

            CriRealTimeData? initialCri = null;
            if (opts.WaitMotion)
            {
                PrintStep(2, $"开启 CRI 数据推送 → 本机 {opts.LocalIp}:{DefaultLocalUdpPort}");
                await robot.StartCriDataPush(opts.LocalIp, DefaultLocalUdpPort);
                PrintOk("CRI 数据推送已开启。");

                PrintStep(3, "等待首帧 CRI，读取当前 TCP 位姿（用于记录起点）");
                initialCri = await ReadFirstCriData(robot, TimeSpan.FromMilliseconds(FirstFrameTimeoutMs));
                PrintVector6("当前 TCP 位姿 (mm + deg)", initialCri.TcpPose);
                PrintVector6("当前关节角 (deg)", initialCri.JointPosition);
            }

            await Countdown(3, "即将下发脚本，请确认机器人周围安全");

            PrintStep(4, "停止当前工程（避免与脚本冲突）");
            try
            {
                await robot.StopProject();
                PrintOk("StopProject 已执行");
            }
            catch (CodroidCommandException ex)
            {
                PrintWarn($"StopProject 警告: {ex.Message}（可忽略）");
            }
            await Task.Delay(1000);

            PrintStep(5, $"调用 RunScript 下发脚本 (vars={(vars?.Count ?? 0)}, subThreads={(subThreads?.Count ?? 0)}, subPrograms={(subPrograms?.Count ?? 0)}, interrupts={(interrupts?.Count ?? 0)})");
            var sw = Stopwatch.StartNew();
            await robot.RunScript(
                mainScript: mainScript,
                subThreads: subThreads,
                subPrograms: subPrograms,
                interrupts: interrupts,
                vars: vars
            );
            sw.Stop();
            PrintOk($"脚本已下发成功，耗时 {sw.Elapsed.TotalSeconds:F2}s");

            if (opts.WaitMotion)
            {
                PrintStep(6, "等待机器人运动完成（InMotion 连续 5 次为 false）…");
                await WaitForMotionSettled(robot, TimeSpan.FromMilliseconds(MotionSettleTimeoutMs));
                PrintOk("运动已完成（InMotion 停稳）。");

                PrintStep(7, "读取最终位姿");
                var finalPose = robot.CriData;
                PrintVector6("最终 TCP 位姿 (mm + deg)", finalPose.TcpPose);
                PrintVector6("最终关节角 (deg)", finalPose.JointPosition);
            }
            else
            {
                PrintWarn("已跳过运动等待（--no-wait）；如需确认到位请使用 CRI 判定。");
            }

            PrintBanner("脚本下发流程结束", ConsoleColor.Green);
            return 0;
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
                Console.WriteLine("  err: " + ex.ControllerError);
            return 1;
        }
        catch (Exception ex)
        {
            PrintBanner("运行异常", ConsoleColor.Red);
            PrintErr(ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            try
            {
                if (opts.WaitMotion)
                {
                    await robot.StopCriDataPush(opts.LocalIp, DefaultLocalUdpPort);
                    PrintOk("CRI 数据推送已关闭。");
                }
            }
            catch { /* ignore */ }

            try { robot.Disconnect(); } catch { /* ignore */ }
            PrintOk("TCP 已断开。");
        }
    }

    // -------------------------------------------------------------------------
    // 命令行解析
    // -------------------------------------------------------------------------
    private sealed class CliOptions
    {
        public string RobotIp { get; init; } = DefaultRobotIp;
        public string LocalIp { get; init; } = DefaultLocalIp;
        public string ScriptPath { get; init; } = DefaultScriptPath;
        public bool WaitMotion { get; init; } = true;
    }

    private static CliOptions ParseArgs(string[] args)
    {
        string robotIp = DefaultRobotIp;
        string localIp = DefaultLocalIp;
        string scriptPath = DefaultScriptPath;
        bool waitMotion = true;

        var positional = new List<string>();
        var argv = args ?? Array.Empty<string>();

        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "--no-wait":
                    waitMotion = false;
                    break;
                case "--wait":
                    waitMotion = true;
                    break;
                case "--script":
                case "-s":
                    if (i + 1 >= argv.Length)
                        throw new ArgumentException($"{a} 缺少脚本路径参数。");
                    scriptPath = argv[++i];
                    break;
                default:
                    positional.Add(a);
                    break;
            }
        }

        // 位置参数优先级：
        //   1 个参数 → 如果是 .lua 结尾或路径存在 → scriptPath；否则 → robotIp
        //   2 个参数 → robotIp + localIp 或 robotIp + scriptPath
        //   3 个参数 → robotIp + localIp + scriptPath
        var queue = new Queue<string>(positional);

        if (queue.Count >= 1)
        {
            var first = queue.Dequeue();
            if (first.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) || File.Exists(first))
            {
                scriptPath = first;
            }
            else
            {
                robotIp = first;
            }
        }

        if (queue.Count >= 1)
        {
            var second = queue.Dequeue();
            if (second.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) || File.Exists(second))
            {
                scriptPath = second;
            }
            else
            {
                localIp = second;
            }
        }

        if (queue.Count >= 1)
        {
            scriptPath = queue.Dequeue();
        }

        if (queue.Count > 0)
            throw new ArgumentException($"无法识别的多余参数：{string.Join(' ', queue)}");

        return new CliOptions
        {
            RobotIp = robotIp,
            LocalIp = localIp,
            ScriptPath = scriptPath,
            WaitMotion = waitMotion,
        };
    }

    // -------------------------------------------------------------------------
    // CRI 辅助：首帧等待 + 运动停稳判定
    // -------------------------------------------------------------------------
    private static async Task<CriRealTimeData> ReadFirstCriData(CodroidClient robot, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var snap = robot.CriData;
            if (snap.TimestampMs > 0)
                return snap;
            await Task.Delay(50);
        }
        throw new TimeoutException("等待首帧 CRI 数据超时。请检查控制器 CRI 推送目标 IP 配置。");
    }

    private static async Task WaitForMotionSettled(CodroidClient robot, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        int settledCount = 0;
        while (sw.Elapsed < timeout)
        {
            var snap = robot.CriData;
            if (snap.TimestampMs == 0)
            {
                // CRI 数据丢失，记录警告但继续等
                PrintWarn("CRI 数据陈旧（TimestampMs=0），继续等待…");
            }

            if (!snap.InMotion)
            {
                settledCount++;
                if (settledCount >= 5)
                    return;
            }
            else
            {
                settledCount = 0;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"等待运动完成超时（{timeout.TotalMinutes:F1} 分钟）。可通过增大 MotionSettleTimeoutMs 调整。");
    }

    private static async Task Countdown(int seconds, string warning)
    {
        PrintWarn(warning + $"，{seconds} 秒后开始（Ctrl+C 取消）。");
        for (int s = seconds; s > 0; s--)
        {
            Console.Write($"\r  倒计时 {s}…  ");
            await Task.Delay(1000);
        }
        Console.WriteLine();
    }

    // -------------------------------------------------------------------------
    // 控制台输出辅助
    // -------------------------------------------------------------------------
    private static void PrintBanner(string title, ConsoleColor color = ConsoleColor.Cyan)
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

    private static void PrintWarn(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ! " + text);
        Console.ForegroundColor = prev;
    }

    private static void PrintErr(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  ✘ " + text);
        Console.ForegroundColor = prev;
    }

    private static void PrintVector6(string label, IReadOnlyList<double> v)
    {
        Console.WriteLine($"  {label,-32}: [{string.Join(", ", v.Select(x => x.ToString("F3")))}]");
    }
}
