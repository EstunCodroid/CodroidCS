// =============================================================================
// CriDataPushExample — CRI 实时数据推送示例
// -----------------------------------------------------------------------------
// 演示如何开启 CRI 数据推送，实时读取机器人状态（TCP 位姿、关节角、速度等）。
//
// CRI 数据推送基于 UDP 9030 端口，控制器以固定周期向本机推送二进制数据包，
// SDK 解析后写入 CriRealTimeData 对象，单位已换算为 mm/deg。
//
// 用法：
//   dotnet run --project examples/CriDataPushExample                              // 默认 IP，运行 10 秒
//   dotnet run --project examples/CriDataPushExample -- 192.168.1.136            // 指定控制器 IP
//   dotnet run --project examples/CriDataPushExample -- 192.168.1.136 192.168.1.150  // 指定控制器 IP + 本机 IP
//   dotnet run --project examples/CriDataPushExample -- 192.168.1.136 192.168.1.150 30  // 运行 30 秒
//
// 数据字段说明：
//   TcpPose[6]         — TCP 位姿 [x,y,z,rx,ry,rz]，mm + deg
//   JointPosition[6]   — 六轴关节角，deg
//   JointVelocity[6]   — 六轴关节角速度，deg/s
//   TcpVelocity[6]     — TCP 速度 [vx,vy,vz,wx,wy,wz]，mm/s + deg/s
//   TcpLinearVelocity  — TCP 线速度标量，mm/s
//   InMotion           — 是否在运动中
//   ProjectRunning     — 工程是否在运行
//   AutoMode           — 是否自动模式
//   RemoteMode         — 是否远程模式
//   SimulationMode     — 是否仿真模式
//   EmergencyStopPressed — 急停是否按下
//   HasAlarm            — 是否存在报警
//   CriErrorCode        — CRI 错误码（0=正常）
//   RealTimeControlMode — 实时控制模式是否激活
//
// 流程：
//   1. TCP 连接控制器
//   2. 开启 CRI 数据推送（StartCriDataPush）
//   3. 等待首帧数据
//   4. 注册事件回调（CriDataReceived）或轮询 robot.CriData
//   5. 持续打印实时数据
//   6. 关闭推送并断开
// =============================================================================

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace CriDataPushExample;

internal static class Program
{
    private const string DefaultRobotIp = "192.168.1.136";
    private const string DefaultLocalIp = "192.168.1.150";
    private const int DefaultLocalUdpPort = 18888;
    private const int DefaultRunSeconds = 10;

    private static async Task<int> Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();

        var (robotIp, localIp, runSeconds) = ParseArgs(args);

        PrintBanner($"CRI 实时数据推送示例  |  控制器: {robotIp}  |  本机 UDP: {localIp}:{DefaultLocalUdpPort}", ConsoleColor.White);
        Console.WriteLine($"  运行时长: {runSeconds} 秒（Ctrl+C 提前退出）");

        var robot = new CodroidClient(robotIp);
        int frameCount = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(runSeconds));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            PrintStep(1, "TCP 连接控制器");
            await robot.Connect();
            PrintOk("已连接。");

            PrintStep(2, "注册 CriDataReceived 事件回调");
            robot.CriDataReceived += data =>
            {
                // 每帧回调（在线程池执行），请勿在此长时间阻塞
                Interlocked.Increment(ref frameCount);
            };
            PrintOk("事件回调已注册。");

            PrintStep(3, $"开启 CRI 数据推送 → 本机 {localIp}:{DefaultLocalUdpPort}");
            await robot.StartCriDataPush(localIp, DefaultLocalUdpPort);
            PrintOk("CRI 数据推送已开启。");

            PrintStep(4, "等待首帧 CRI 数据…");
            var firstData = await WaitForFirstFrame(robot, TimeSpan.FromSeconds(5));
            PrintOk("首帧已收到。");
            PrintSeparator();

            // 打印首帧完整状态
            PrintFullStatus(firstData);
            PrintSeparator();

            PrintStep(5, "持续打印实时数据（每 500ms 采样一次）…");
            Console.WriteLine();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cts.Token.IsCancellationRequested)
            {
                var snap = robot.CriData;
                var elapsed = sw.Elapsed.TotalSeconds;

                Console.WriteLine(
                    $"  [{elapsed,6:F1}s] #{frameCount,7}  " +
                    $"TCP=[{F3(snap.TcpPose[0])},{F3(snap.TcpPose[1])},{F3(snap.TcpPose[2])}]  " +
                    $"J=[{F2(snap.JointPosition[0])},{F2(snap.JointPosition[1])},{F2(snap.JointPosition[2])},{F2(snap.JointPosition[3])},{F2(snap.JointPosition[4])},{F2(snap.JointPosition[5])}]  " +
                    $"v={snap.TcpLinearVelocity,6:F1}mm/s  " +
                    $"Motion={snap.InMotion}  " +
                    $"Err={snap.CriErrorCode}");

                try { await Task.Delay(500, cts.Token); }
                catch (TaskCanceledException) { break; }
            }

            PrintSeparator();
            PrintOk($"运行结束，共收到 {frameCount} 帧 CRI 数据。");

            PrintStep(6, "关闭 CRI 数据推送");
            await robot.StopCriDataPush(localIp, DefaultLocalUdpPort);
            PrintOk("CRI 数据推送已关闭。");

            PrintBanner("CRI 数据推送示例结束", ConsoleColor.Green);
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
    // 等待首帧
    // -------------------------------------------------------------------------
    private static async Task<CriRealTimeData> WaitForFirstFrame(CodroidClient robot, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var snap = robot.CriData;
            if (snap.TimestampMs > 0)
                return snap;
            await Task.Delay(50);
        }
        throw new TimeoutException("等待首帧 CRI 数据超时。请检查控制器 CRI 推送目标 IP 配置。");
    }

    // -------------------------------------------------------------------------
    // 打印完整状态（首帧）
    // -------------------------------------------------------------------------
    private static void PrintFullStatus(CriRealTimeData d)
    {
        Console.WriteLine("  === 完整 CRI 状态 ===");
        Console.WriteLine($"  时间戳        : {d.TimestampMs} ms");
        Console.WriteLine();
        Console.WriteLine("  --- 位姿 ---");
        Console.WriteLine($"  TCP 位姿 (mm/deg): [{string.Join(", ", F3(d.TcpPose[0]), F3(d.TcpPose[1]), F3(d.TcpPose[2]), F3(d.TcpPose[3]), F3(d.TcpPose[4]), F3(d.TcpPose[5]))}]");
        Console.WriteLine($"  关节角 (deg)      : [{string.Join(", ", F3(d.JointPosition[0]), F3(d.JointPosition[1]), F3(d.JointPosition[2]), F3(d.JointPosition[3]), F3(d.JointPosition[4]), F3(d.JointPosition[5]))}]");
        Console.WriteLine();
        Console.WriteLine("  --- 速度 ---");
        Console.WriteLine($"  TCP 线速度       : {F2(d.TcpLinearVelocity)} mm/s");
        Console.WriteLine($"  关节角速度 (deg/s): [{string.Join(", ", F2(d.JointVelocity[0]), F2(d.JointVelocity[1]), F2(d.JointVelocity[2]), F2(d.JointVelocity[3]), F2(d.JointVelocity[4]), F2(d.JointVelocity[5]))}]");
        Console.WriteLine();
        Console.WriteLine("  --- 状态字 ---");
        Console.WriteLine($"  工程运行    : {d.ProjectRunning}    工程停止: {d.ProjectStopped}    工程暂停: {d.ProjectPaused}");
        Console.WriteLine($"  自动模式    : {d.AutoMode}          远程模式: {d.RemoteMode}          手动模式: {d.ManualMode}");
        Console.WriteLine($"  仿真模式    : {d.SimulationMode}    急停按下: {d.EmergencyStopPressed}    有报警: {d.HasAlarm}");
        Console.WriteLine($"  运动中      : {d.InMotion}          碰撞停止: {d.CollisionStopped}        安全位置: {d.InSafetyPosition}");
        Console.WriteLine($"  实时控制模式: {d.RealTimeControlMode}   CRI错误码: {d.CriErrorCode}");
    }

    // -------------------------------------------------------------------------
    // 命令行解析
    // -------------------------------------------------------------------------
    private static (string robotIp, string localIp, int runSeconds) ParseArgs(string[] args)
    {
        string ip = DefaultRobotIp;
        string localIp = DefaultLocalIp;
        int seconds = DefaultRunSeconds;

        var argv = args ?? Array.Empty<string>();
        if (argv.Length >= 1) ip = argv[0];
        if (argv.Length >= 2) localIp = argv[1];
        if (argv.Length >= 3 && int.TryParse(argv[2], out var s)) seconds = s;

        return (ip, localIp, seconds);
    }

    // -------------------------------------------------------------------------
    // 格式化辅助
    // -------------------------------------------------------------------------
    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

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
