// =============================================================================
// CodroidCRITestNet462 — CRI 实时控制 + 自动轨迹规划 联调 (net462)
// -----------------------------------------------------------------------------
// 用法：
//   dotnet run --project CodroidCRITestNet462                     // 关节 → 笛卡尔 → 路径 全部跑
//   dotnet run --project CodroidCRITestNet462 -- joint            // 仅关节
//   dotnet run --project CodroidCRITestNet462 -- cart             // 仅笛卡尔（同 cartesian）
//   dotnet run --project CodroidCRITestNet462 -- path             // 仅自定义 4 点路径
//   dotnet run --project CodroidCRITestNet462 -- joint 192.168.8.10
//   dotnet run --project CodroidCRITestNet462 -- cart  192.168.8.10 192.168.8.150
//
// 可选轨迹覆盖（与位置参数同时给）：
//   --speed N        关节段单位 deg/s（默认 30）；笛卡尔段单位 mm/s（默认 80）
//   --accel N        关节段单位 deg/s²（默认 120）；笛卡尔段单位 mm/s²（默认 400）
//   --duration N     该段总时长（秒），与 --speed 互斥
//
// 示例：
//   dotnet run --project CodroidCRITestNet462 -- cart --speed 120 --accel 600
//   dotnet run --project CodroidCRITestNet462 -- path 192.168.8.10 192.168.8.150 --speed 50
//   dotnet run --project CodroidCRITestNet462 -- cart --duration 6
//
// 关节段：current → (0,0,90,0,90,0) → (0,0,0,0,0,0) → (0,0,90,0,90,0)
// 笛卡尔段（YZ 平面矩形，回到原点；姿态保持）：
//   current → z-200 → y-200 → z+200 → y+200 → current
// 路径段（4 点全位姿，最后回到 home）：
//   current → P1 → P2 → P3 → home([927.505,214.495,898.994,180,0,-90])
//
// 安全：起点取 CRI 实时回传的当前位姿；执行前有 3 秒倒计时，可 Ctrl+C 取消。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace CodroidCRITestNet462;

internal static class Program
{
    private const string DefaultRobotIp = "192.168.1.136";
    private const string DefaultLocalIp = "192.168.1.150";
    private const int DefaultLocalUdpPort = 18888;

    // 与 StartCriControl 的 durationMs 严格对齐：4 ms ↔ 250 Hz
    private const int RealtimePeriodMs = 4;
    private const double SampleFrequencyHz = 1000.0 / RealtimePeriodMs;
    private const int RealtimeFilterType = 1;
    private const int RealtimeStartBuffer = 5;

    // 默认运动学参数（保守）。轨迹生成时仍可被命令行覆盖；这里只给值。
    private const double JointSpeedDegPerSec = 30.0;
    private const double JointAccelDegPerSec2 = 120.0;
    private const double CartesianSpeedMmPerSec = 80.0;
    private const double CartesianAccelMmPerSec2 = 400.0;

    private const int FirstFrameTimeoutMs = 3000;
    private const int RealtimeReadyTimeoutMs = 3000;

    private static async Task<int> Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();
        CliOptions opts;
        try
        {
            opts = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            PrintErr(ex.Message);
            PrintUsage();
            return 2;
        }

        try
        {
            switch (opts.Mode)
            {
                case "joint":
                    await RunJoint(opts);
                    break;
                case "cart":
                case "cartesian":
                    await RunCartesian(opts);
                    break;
                case "path":
                    await RunPath(opts);
                    break;
                case "all":
                    await RunJoint(opts);
                    Console.WriteLine();
                    await RunCartesian(opts);
                    Console.WriteLine();
                    await RunPath(opts);
                    break;
                default:
                    PrintUsage();
                    return 2;
            }
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
    }

    /// <summary>命令行解析结果（含可选的轨迹覆盖参数）。</summary>
    private sealed class CliOptions
    {
        public string Mode { get; init; } = "all";
        public string RobotIp { get; init; } = DefaultRobotIp;
        public string LocalIp { get; init; } = DefaultLocalIp;
        public double? Speed { get; init; }
        public double? Acceleration { get; init; }
        public double? DurationSeconds { get; init; }
    }

    // -------------------------------------------------------------------------
    // 关节段
    // -------------------------------------------------------------------------
    private static async Task RunJoint(CliOptions opts)
    {
        var req = BuildRequest(
            TrajectorySpace.Joint,
            TrajectoryProfile.Cubic,
            JointSpeedDegPerSec,
            JointAccelDegPerSec2,
            opts);

        PrintBanner($"关节实时轨迹  |  控制器: {opts.RobotIp}  |  本机 UDP: {opts.LocalIp}:{DefaultLocalUdpPort}", ConsoleColor.White);
        Console.WriteLine("  序列: current → (0,0,90,0,90,0) → (0,0,0,0,0,0) → (0,0,90,0,90,0)");
        Console.WriteLine($"  规划: {DescribeRequest(req, "deg/s", "deg/s²")}");

        var robot = new CodroidClient(opts.RobotIp);
        try
        {
            await ConnectAndStartCriPush(robot, opts.LocalIp, DefaultLocalUdpPort);

            PrintStep(2, "等待首帧 CRI，读取当前关节角");
            var current = await ReadCurrentJoint(robot, TimeSpan.FromMilliseconds(FirstFrameTimeoutMs));
            PrintVector6("当前关节角 (deg)", current);

            var waypoints = new[]
            {
                current,
                new[] {  0.0,  0.0, 90.0, 0.0, 90.0, 0.0 },
                new[] {  0.0,  0.0,  0.0, 0.0,  0.0, 0.0 },
                new[] {  0.0,  0.0, 90.0, 0.0, 90.0, 0.0 },
            };
            for (int i = 0; i < waypoints.Length; i++)
                PrintVector6($"  P{i}", waypoints[i]);

            var traj = GenerateMultiSegment(waypoints, req);
            PrintTrajectoryStats(traj, "关节轨迹");

            await Countdown(3, "即将启动实时控制并下发关节轨迹");
            await ExecuteRealtimeTrajectory(robot, opts.RobotIp, traj, TrajectorySpace.Joint);

            PrintStep(99, "段结束，关闭 CRI 数据推送");
            await robot.StopCriDataPush(opts.LocalIp, DefaultLocalUdpPort);
            PrintOk("关节段完成。");
        }
        finally
        {
            try { robot.Disconnect(); } catch { /* ignore */ }
        }
    }

    // -------------------------------------------------------------------------
    // 笛卡尔段
    // -------------------------------------------------------------------------
    private static async Task RunCartesian(CliOptions opts)
    {
        var req = BuildRequest(
            TrajectorySpace.Cartesian,
            TrajectoryProfile.Trapezoidal, // 笛卡尔匀速段必须梯形
            CartesianSpeedMmPerSec,
            CartesianAccelMmPerSec2,
            opts);

        PrintBanner($"笛卡尔实时轨迹  |  控制器: {opts.RobotIp}  |  本机 UDP: {opts.LocalIp}:{DefaultLocalUdpPort}", ConsoleColor.White);
        Console.WriteLine("  序列: current → z-200 → y-200 → z+200 → y+200 (回到原点)");
        Console.WriteLine($"  规划: {DescribeRequest(req, "mm/s", "mm/s²")}");
        Console.WriteLine("  姿态: 整段保持起点姿态不变。");

        var robot = new CodroidClient(opts.RobotIp);
        try
        {
            await ConnectAndStartCriPush(robot, opts.LocalIp, DefaultLocalUdpPort);

            PrintStep(2, "等待首帧 CRI，读取当前 TCP 位姿");
            var p0 = await ReadCurrentTcpPose(robot, TimeSpan.FromMilliseconds(FirstFrameTimeoutMs));
            PrintVector6("当前 TCP 位姿 (mm + deg)", p0);

            // 仅平移 x/y/z，姿态保持
            static double[] Translate(double[] from, double dx, double dy, double dz) => new[]
            {
                from[0] + dx, from[1] + dy, from[2] + dz,
                from[3], from[4], from[5],
            };

            var p1 = Translate(p0, 0,    0, -200); // 向下
            var p2 = Translate(p1, 0, -200,    0); // y-
            var p3 = Translate(p2, 0,    0, +200); // z+ 回到原 z
            var p4 = Translate(p3, 0, +200,    0); // y+ 回到原点

            var waypoints = new[] { p0, p1, p2, p3, p4 };
            for (int i = 0; i < waypoints.Length; i++)
                PrintVector6($"  P{i}", waypoints[i]);

            var traj = GenerateMultiSegment(waypoints, req);
            PrintTrajectoryStats(traj, "笛卡尔轨迹");

            await Countdown(3, "即将启动实时控制并下发笛卡尔轨迹");
            await ExecuteRealtimeTrajectory(robot, opts.RobotIp, traj, TrajectorySpace.Cartesian);

            PrintStep(99, "段结束，关闭 CRI 数据推送");
            await robot.StopCriDataPush(opts.LocalIp, DefaultLocalUdpPort);
            PrintOk("笛卡尔段完成。");
        }
        finally
        {
            try { robot.Disconnect(); } catch { /* ignore */ }
        }
    }

    // -------------------------------------------------------------------------
    // 自定义路径段（4 个目标点 + 回到 home，全位姿插值）
    // -------------------------------------------------------------------------
    private static async Task RunPath(CliOptions opts)
    {
        // 期望终点（人工标定的"home"）：理论上等于第二段笛卡尔走完后的位置；实际起点以 CRI 为准。
        var pHome = new[] { 927.505, 214.495, 898.994, 180.0, 0.0, -90.0 };
        var p1 = new[] { 1139.996,  214.490, 899.010, -91.506, -0.001,  -89.999 };
        var p2 = new[] { 1139.994, -222.730, 899.022, -91.506, -0.002, -136.466 };
        var p3 = new[] {  915.480,  -73.000, 599.316, 166.910, -5.170,  -90.726 };

        var req = BuildRequest(
            TrajectorySpace.Cartesian,
            TrajectoryProfile.Trapezoidal,
            CartesianSpeedMmPerSec,
            CartesianAccelMmPerSec2,
            opts);

        PrintBanner($"自定义路径轨迹  |  控制器: {opts.RobotIp}  |  本机 UDP: {opts.LocalIp}:{DefaultLocalUdpPort}", ConsoleColor.White);
        Console.WriteLine("  序列: current → P1 → P2 → P3 → P4(=home)");
        Console.WriteLine($"  规划: {DescribeRequest(req, "mm/s", "mm/s²")}");
        Console.WriteLine("  姿态: 同段内由 SLERP 沿 SO(3) 最短路径插值（与位置同时间标度）。");

        var robot = new CodroidClient(opts.RobotIp);
        try
        {
            await ConnectAndStartCriPush(robot, opts.LocalIp, DefaultLocalUdpPort);

            PrintStep(2, "等待首帧 CRI，读取当前 TCP 位姿（作为路径起点）");
            var p0 = await ReadCurrentTcpPose(robot, TimeSpan.FromMilliseconds(FirstFrameTimeoutMs));
            PrintVector6("当前 TCP 位姿 (mm + deg)", p0);
            PrintVector6("参考 home（仅打印对照）", pHome);

            var waypoints = new[] { p0, p1, p2, p3, pHome };
            for (int i = 0; i < waypoints.Length; i++)
                PrintVector6($"  P{i}", waypoints[i]);

            var traj = GenerateMultiSegment(waypoints, req);
            PrintTrajectoryStats(traj, "路径轨迹");

            await Countdown(3, "即将启动实时控制并下发路径轨迹");
            await ExecuteRealtimeTrajectory(robot, opts.RobotIp, traj, TrajectorySpace.Cartesian);

            PrintStep(99, "段结束，关闭 CRI 数据推送");
            await robot.StopCriDataPush(opts.LocalIp, DefaultLocalUdpPort);
            PrintOk("路径段完成。");
        }
        finally
        {
            try { robot.Disconnect(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// 把命令行覆盖应用到 <see cref="TrajectoryRequest"/>：
    /// <list type="bullet">
    /// <item><description>给了 <c>--duration</c>：用时长模式，丢弃 Speed</description></item>
    /// <item><description>否则用 Speed 模式（命令行 <c>--speed</c> 或默认值）</description></item>
    /// <item><description><c>--accel</c> 总是覆盖默认加速度</description></item>
    /// </list>
    /// </summary>
    private static TrajectoryRequest BuildRequest(
        TrajectorySpace space,
        TrajectoryProfile profile,
        double defaultSpeed,
        double defaultAccel,
        CliOptions opts)
    {
        double accel = opts.Acceleration ?? defaultAccel;
        if (opts.DurationSeconds.HasValue)
        {
            return new TrajectoryRequest
            {
                Space = space,
                FrequencyHz = SampleFrequencyHz,
                DurationSeconds = opts.DurationSeconds.Value,
                Acceleration = accel,
                Profile = profile,
            };
        }
        return new TrajectoryRequest
        {
            Space = space,
            FrequencyHz = SampleFrequencyHz,
            Speed = opts.Speed ?? defaultSpeed,
            Acceleration = accel,
            Profile = profile,
        };
    }

    private static string DescribeRequest(TrajectoryRequest req, string speedUnit, string accelUnit)
    {
        string profile = req.Profile == TrajectoryProfile.Trapezoidal ? "梯形匀速" : "三次平滑";
        string driver = req.Speed.HasValue
            ? $"速度 {req.Speed.Value:F2} {speedUnit}"
            : $"时长 {req.DurationSeconds!.Value:F2} s";
        return $"{profile} / {driver} / 加速度 {req.Acceleration:F2} {accelUnit}";
    }

    // -------------------------------------------------------------------------
    // 多段轨迹拼接：相邻段共享端点，去重避免重复采样点
    // -------------------------------------------------------------------------
    private static List<TrajectoryPoint> GenerateMultiSegment(
        IReadOnlyList<double[]> waypoints,
        TrajectoryRequest req)
    {
        if (waypoints.Count < 2) throw new ArgumentException("至少需要 2 个 waypoint。", nameof(waypoints));

        var result = new List<TrajectoryPoint>();
        double tBase = 0;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var seg = TrajectoryGenerator.Generate(waypoints[i], waypoints[i + 1], req).ToList();
            for (int k = 0; k < seg.Count; k++)
            {
                // 段间端点重复，跳过后续段的首点
                if (i > 0 && k == 0) continue;
                result.Add(new TrajectoryPoint
                {
                    TimeSeconds = tBase + seg[k].TimeSeconds,
                    Position = seg[k].Position,
                });
            }
            tBase += seg[seg.Count - 1].TimeSeconds;
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // 起停实时控制 + UDP 周期下发
    // -------------------------------------------------------------------------
    private static async Task ExecuteRealtimeTrajectory(
        CodroidClient robot,
        string robotIp,
        IReadOnlyList<TrajectoryPoint> trajectory,
        TrajectorySpace space)
    {
        PrintStep(3, $"StartCriControl(filterType={RealtimeFilterType}, durationMs={RealtimePeriodMs}, startBuffer={RealtimeStartBuffer})");
        await robot.StartCriControl(
            filterType: RealtimeFilterType,
            durationMs: RealtimePeriodMs,
            startBuffer: RealtimeStartBuffer);
        PrintOk("StartCriControl 已下发，等待控制器进入实时控制模式…");

        await WaitForRealtimeControl(robot, TimeSpan.FromMilliseconds(RealtimeReadyTimeoutMs));
        PrintOk("RealTimeControlMode = true");

        PrintStep(4, $"UDP 周期下发 {trajectory.Count} 帧 / {trajectory[trajectory.Count - 1].TimeSeconds:F2}s @ {RealtimePeriodMs}ms");
        var sw = Stopwatch.StartNew();
        try
        {
            using var dispatcher = new CriRealtimeDispatcher(robotIp);
            await dispatcher.SendTrajectory(trajectory, space, RealtimePeriodMs);
        }
        finally
        {
            sw.Stop();
            PrintOk($"UDP 下发结束，实际耗时 {sw.Elapsed.TotalSeconds:F2}s");

            try
            {
                await robot.StopCriControl();
                PrintOk("StopCriControl 已下发。");
            }
            catch (Exception ex)
            {
                PrintWarn("StopCriControl 失败：" + ex.Message);
            }
        }
    }

    // -------------------------------------------------------------------------
    // 公共：连接 + StartDataPush
    // -------------------------------------------------------------------------
    private static async Task ConnectAndStartCriPush(CodroidClient robot, string localIp, int localUdpPort)
    {
        PrintStep(1, "TCP 连接 + 切远程 + 上电");
        await robot.ConnectRemoteAndSwitchOn();
        PrintOk("已连接，已切远程并上电。");

        PrintStep(1, $"StartCriDataPush → 本机 {localIp}:{localUdpPort}");
        await robot.StartCriDataPush(localIp, localUdpPort);
        PrintOk("CRI 数据推送已开启。");
    }

    private static async Task<double[]> ReadCurrentJoint(CodroidClient robot, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var snap = robot.CriData;
            // 用 TimestampMs > 0 作为「至少收到一帧」的判据
            if (snap.TimestampMs > 0)
                return (double[])snap.JointPosition.Clone();
            await Task.Delay(50);
        }
        throw new TimeoutException("等待首帧 CRI 数据超时。");
    }

    private static async Task<double[]> ReadCurrentTcpPose(CodroidClient robot, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var snap = robot.CriData;
            if (snap.TimestampMs > 0)
                return (double[])snap.TcpPose.Clone();
            await Task.Delay(50);
        }
        throw new TimeoutException("等待首帧 CRI 数据超时。");
    }

    private static async Task WaitForRealtimeControl(CodroidClient robot, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (robot.Data.RealTimeControlMode) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("等待 RealTimeControlMode=true 超时。");
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
    // 参数 / 用法
    // -------------------------------------------------------------------------
    private static CliOptions ParseArgs(string[] args)
    {
        string mode = "all";
        string robotIp = DefaultRobotIp;
        string localIp = DefaultLocalIp;
        double? speed = null;
        double? accel = null;
        double? duration = null;

        // 先抽出 --key value 对，剩下的按位置解析（mode → robotIp → localIp）
        var positional = new List<string>();
        var argv = args ?? Array.Empty<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "--speed":
                case "--accel":
                case "--duration":
                {
                    if (i + 1 >= argv.Length)
                        throw new ArgumentException($"{a} 缺少数值。");
                    string raw = argv[++i];
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                        throw new ArgumentException($"{a} 的值不是有效数字: {raw}");
                    if (val <= 0)
                        throw new ArgumentException($"{a} 必须大于 0：{raw}");
                    if (a == "--speed") speed = val;
                    else if (a == "--accel") accel = val;
                    else duration = val;
                    break;
                }
                default:
                    positional.Add(a);
                    break;
            }
        }

        if (speed.HasValue && duration.HasValue)
            throw new ArgumentException("--speed 与 --duration 互斥，请二选一。");

        var queue = new Queue<string>(positional);
        if (queue.Count > 0)
        {
            var first = queue.Peek().ToLowerInvariant();
            if (first is "joint" or "cart" or "cartesian" or "path" or "all" or "help" or "-h" or "--help")
            {
                mode = queue.Dequeue().ToLowerInvariant();
                if (mode is "help" or "-h" or "--help") return new CliOptions { Mode = "help" };
            }
        }
        if (queue.Count > 0) robotIp = queue.Dequeue();
        if (queue.Count > 0) localIp = queue.Dequeue();
        if (queue.Count > 0)
            throw new ArgumentException($"无法识别的多余参数：{string.Join(" ", queue)}");

        return new CliOptions
        {
            Mode = mode,
            RobotIp = robotIp,
            LocalIp = localIp,
            Speed = speed,
            Acceleration = accel,
            DurationSeconds = duration,
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project CodroidCRITestNet462 -- [joint|cart|path|all] [robotIp] [localIp] [选项]");
        Console.WriteLine();
        Console.WriteLine("可选轨迹覆盖（与位置参数同时给）:");
        Console.WriteLine("  --speed N      关节: deg/s（默认 30）；笛卡尔: mm/s（默认 80）");
        Console.WriteLine("  --accel N      关节: deg/s²（默认 120）；笛卡尔: mm/s²（默认 400）");
        Console.WriteLine("  --duration N   该段总时长（秒），与 --speed 互斥");
        Console.WriteLine();
        Console.WriteLine($"默认: all  {DefaultRobotIp}  {DefaultLocalIp}");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  dotnet run --project CodroidCRITestNet462 -- cart --speed 120 --accel 600");
        Console.WriteLine("  dotnet run --project CodroidCRITestNet462 -- path 192.168.8.10 192.168.8.150 --speed 50");
        Console.WriteLine("  dotnet run --project CodroidCRITestNet462 -- cart --duration 6");
    }

    // -------------------------------------------------------------------------
    // 控制台辅助
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

    private static void PrintTrajectoryStats(IReadOnlyList<TrajectoryPoint> traj, string label)
    {
        if (traj.Count == 0)
        {
            Console.WriteLine($"  {label}: 空轨迹");
            return;
        }
        Console.WriteLine($"  {label}: {traj.Count} 帧, 总时长 {traj[traj.Count - 1].TimeSeconds:F3}s, 周期 {RealtimePeriodMs}ms");
        PrintVector6("    首点", traj[0].Position);
        PrintVector6("    末点", traj[traj.Count - 1].Position);
    }

}
