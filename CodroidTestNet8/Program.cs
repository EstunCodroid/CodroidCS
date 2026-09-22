// =============================================================================
// CodroidTestNet8 — 控制台示例程序（net8.0）；net6.0 版见 CodroidTestNet6/Program.cs
// -----------------------------------------------------------------------------
// 【默认：完整套件】无子命令即跑全部 7 段（含 RobotStatus 订阅 10 秒）
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- 192.168.8.10     // 指定控制器 IP 跑全套
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- all 192.168.8.10   // 显式写 all，同上
//   顺序：全局变量 → 正逆解 → IO → 寄存器 → RobotStatus → CRI → S20 运动+CRI
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- … --no-clean     // 仅影响「全局变量」段是否删除 sdk_gv_*
//
// 【仅单项】须带子命令：
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- global [ip]
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- cri [ip]
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- kin [ip]
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- io [ip]           // 或 iomanager
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- register [ip]     // 或 reg：寄存器读写
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- robotstatus [ip] // 仅订阅 publish/RobotStatus，收 10 秒推送
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- motion [ip]      // 或 s20 / movecri：四组合+矩形路径
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- robotparam [ip] // 机器人设置 19.x（Get/SaveRobotParameter）
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- robotparamfull [ip] // 机器人设置完整测试（19.1~19.7 全部接口）
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- syncmotion [ip] // 阻塞运动 Sync（CRI 新鲜度+到位判定）
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace Program;

internal static class Program
{
    /// <summary>未传 IP 时使用的默认控制器地址（请按现场修改）。</summary>
    private const string DefaultRobotIp = "192.168.1.136";

    /// <summary>程序入口：无子命令时跑完整套件；带子命令时只跑对应单项。</summary>
    private static async Task Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();
        var mode = ParseMode(args, out var robotIp, out var noClean);

        switch (mode)
        {
            case RunMode.AllTests:
                await RunAllTests(robotIp, noClean);
                return;
            case RunMode.CriDemo:
                await RunCriDemo(robotIp);
                return;
            case RunMode.KinematicsTest:
                await RunKinematicsTest(robotIp);
                return;
            case RunMode.S20MotionCriTest:
                await RunS20MotionCriCombo(robotIp);
                return;
            case RunMode.IoTest:
                await RunIoTest(robotIp);
                return;
            case RunMode.RegisterTest:
                await RunRegisterTest(robotIp);
                return;
            case RunMode.RobotStatusPublishDemo:
                await RunRobotStatusPublishDemo(robotIp);
                return;
            case RunMode.RobotParameterTest:
                await RunRobotParameterTest(robotIp);
                return;
            case RunMode.RobotParameterFullTest:
                await CodroidTestNet8.RobotSettingsFullTest.Run(robotIp);
                return;
            case RunMode.SyncMotionTest:
                await RunSyncMotionTest(robotIp);
                return;
            default:
                // 枚举齐全时不可达；若将来新增 RunMode 未补 switch，宁可失败也不要静默只跑全局变量。
                throw new InvalidOperationException($"未处理的运行模式: {mode}");
        }
    }

    private enum RunMode
    {
        AllTests,
        GlobalVarTest,
        CriDemo,
        KinematicsTest,
        S20MotionCriTest,
        IoTest,
        RegisterTest,
        RobotStatusPublishDemo,
        RobotParameterTest,
        RobotParameterFullTest,
        SyncMotionTest
    }

    /// <summary>
    /// 按顺序执行全部测试（每项内部会 Connect/Disconnect）。
    /// </summary>
    private static async Task RunAllTests(string robotIp, bool noClean)
    {
        PrintBanner("CodroidTest 完整测试套件", ConsoleColor.White);
        Console.WriteLine($"  控制器 IP: {robotIp}");
        Console.WriteLine("  顺序：1 全局变量  2 正逆解  3 IO  4 寄存器  5 RobotStatus  6 CRI  7 S20 运动 + CRI");
        if (noClean)
        {
            PrintWarn("已指定 --no-clean：全局变量段结束后将保留 sdk_gv_*。");
        }

        Console.WriteLine();
        PrintBanner("【1/7】全局变量", ConsoleColor.DarkYellow);
        await RunGlobalVarTest(robotIp, cleanUp: !noClean);

        PrintBanner("【2/7】正逆解 / 相对位姿", ConsoleColor.DarkYellow);
        await RunKinematicsTest(robotIp);

        PrintBanner("【3/7】IO（GetIOValue / SetIOValue）", ConsoleColor.DarkYellow);
        await RunIoTest(robotIp);

        PrintBanner("【4/7】寄存器（GetRegisterValue / SetRegisterValue）", ConsoleColor.DarkYellow);
        await RunRegisterTest(robotIp);

        PrintBanner("【5/7】TCP 主题 publish/RobotStatus（订阅 10 秒）", ConsoleColor.DarkYellow);
        await RunRobotStatusPublishDemo(robotIp, partOfFullSuite: true);

        PrintBanner("【6/7】CRI 实时数据演示", ConsoleColor.DarkYellow);
        await RunCriDemo(robotIp);

        PrintBanner("【7/7】S20-180-ECO_V2 运动 + CRI", ConsoleColor.DarkYellow);
        await RunS20MotionCriCombo(robotIp);

        PrintBanner("CodroidTest 全部测试已执行完毕", ConsoleColor.Green);
    }

    /// <summary>
    /// 解析命令行：去掉 --no-clean；无参数或 all 或单独 IP → <see cref="RunMode.AllTests"/>；否则匹配单项子命令。
    /// </summary>
    private static RunMode ParseMode(string[] args, out string robotIp, out bool noClean)
    {
        robotIp = DefaultRobotIp;
        noClean = false;

        var list = args.ToList();
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], "--no-clean", StringComparison.OrdinalIgnoreCase))
            {
                noClean = true;
                list.RemoveAt(i);
            }
        }

        if (list.Count == 0)
        {
            return RunMode.AllTests;
        }

        if (string.Equals(list[0], "all", StringComparison.OrdinalIgnoreCase))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.AllTests;
        }

        if (list.Count > 0 && string.Equals(list[0], "cri", StringComparison.OrdinalIgnoreCase))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.CriDemo;
        }

        if (list.Count > 0 && IsKinematicsCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.KinematicsTest;
        }

        if (list.Count > 0 && IsS20MotionCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.S20MotionCriTest;
        }

        if (list.Count > 0 && IsIoCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.IoTest;
        }

        if (list.Count > 0 && IsRegisterCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.RegisterTest;
        }

        if (list.Count > 0 && IsRobotStatusPublishCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.RobotStatusPublishDemo;
        }

        if (list.Count > 0 && IsRobotParameterCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.RobotParameterTest;
        }

        if (list.Count > 0 && IsRobotParameterFullCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.RobotParameterFullTest;
        }

        if (list.Count > 0 && IsSyncMotionCommand(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.SyncMotionTest;
        }

        // 「global」可写可不写；写了则跳过该词再读 IP
        if (list.Count > 0 && string.Equals(list[0], "global", StringComparison.OrdinalIgnoreCase))
        {
            list.RemoveAt(0);
            if (list.Count > 0)
            {
                robotIp = list[0];
            }

            return RunMode.GlobalVarTest;
        }

        if (list.Count > 0)
        {
            robotIp = list[0];
        }

        return RunMode.AllTests;
    }

    private static bool IsKinematicsCommand(string token) =>
        string.Equals(token, "kin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "kinematics", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "fk", StringComparison.OrdinalIgnoreCase);

    private static bool IsS20MotionCommand(string token) =>
        string.Equals(token, "motion", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "s20", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "movecri", StringComparison.OrdinalIgnoreCase);

    private static bool IsIoCommand(string token) =>
        string.Equals(token, "io", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "iomanager", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegisterCommand(string token) =>
        string.Equals(token, "register", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "reg", StringComparison.OrdinalIgnoreCase);

    private static bool IsRobotStatusPublishCommand(string token) =>
        string.Equals(token, "robotstatus", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "pubstatus", StringComparison.OrdinalIgnoreCase);

    private static bool IsRobotParameterCommand(string token) =>
        string.Equals(token, "robotparam", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "robotsettings", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "settings", StringComparison.OrdinalIgnoreCase);

    private static bool IsRobotParameterFullCommand(string token) =>
        string.Equals(token, "robotparamfull", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "robotsettingsfull", StringComparison.OrdinalIgnoreCase);

    private static bool IsSyncMotionCommand(string token) =>
        string.Equals(token, "syncmotion", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "motionsync", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "andwait", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // 控制台输出：分节横幅 + 颜色，便于在日志里一眼看到阶段
    // -------------------------------------------------------------------------

    private static void PrintBanner(string title, ConsoleColor color = ConsoleColor.Cyan)
    {
        const int width = 66;
        var line = new string('=', width);
        Console.WriteLine();
        Console.ForegroundColor = color;
        Console.WriteLine(line);
        Console.WriteLine($"  {title}");
        Console.WriteLine(line);
        Console.ResetColor();
    }

    private static void PrintStep(int step, string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($">>> 步骤 {step}：{text}");
        Console.ResetColor();
    }

    private static void PrintOk(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] {text}");
        Console.ResetColor();
    }

    private static void PrintWarn(string text)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[!!] {text}");
        Console.ResetColor();
    }

    private static void PrintErr(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[×] {text}");
        Console.ResetColor();
    }

    private static void PrintVector6(string label, IReadOnlyList<double> v, string unitHint)
    {
        Console.WriteLine($"  {label} ({unitHint}): [{string.Join(", ", v.Select(x => x.ToString("G9")))}]");
    }

    /// <summary>
    /// 彩色阶段标题（运动测试用）。
    /// </summary>
    private static void PrintMotionPhase(string title, ConsoleColor fg, ConsoleColor? bg = null)
    {
        Console.WriteLine();
        if (bg.HasValue)
        {
            Console.BackgroundColor = bg.Value;
        }

        Console.ForegroundColor = fg;
        var line = new string('█', 64);
        Console.WriteLine(line);
        Console.WriteLine($"  {title}");
        Console.WriteLine(line);
        Console.ResetColor();
    }

    private static void PrintCriLine(CodroidClient robot, string tag)
    {
        var d = robot.Data;
        var t = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write($"[{t}] [{tag}] ");
        Console.ResetColor();
        Console.WriteLine(
            $"InMotion={d.InMotion,-5}  |  jp(deg)=[{string.Join(", ", d.JointPosition.Select(x => x.ToString("F2")))}]");
        Console.WriteLine(
            $"           TcpPose(mm,deg)=[{string.Join(", ", d.TcpPose.Select(x => x.ToString("F3")))}]");
    }

    /// <summary>
    /// 测试机型 S20-180-ECO_V2：CRI + movJ 三段 + 四组合 API（MovJ/MovL 关节与 TCP）+ movL 矩形路径；全程每 1s 打印状态。
    /// </summary>
    private static async Task RunS20MotionCriCombo(string robotIp)
    {
        const string model = "S20-180-ECO_V2";
        var robot = new CodroidClient(robotIp);

        const string localUdpIp = "192.168.1.150";
        const int localUdpPort = 18888;

        using var printCts = new CancellationTokenSource();
        Task? printerTask = null;

        PrintBanner($"运动 + CRI 联调  |  机型: {model}  |  TCP {robotIp}:9001", ConsoleColor.White);
        Console.WriteLine();
        Console.WriteLine("  警告：请在确认安全空间、急停可用后再运行；本程序会下发真实运动指令。");
        Console.WriteLine($"  CRI UDP 本机: {localUdpIp}:{localUdpPort}（请按现场修改 Program.cs 常量）。");
        Console.WriteLine("  运动中每 1 秒打印：InMotion、关节角(deg)、TCP(mm,deg)。");

        try
        {
            await robot.ConnectRemoteAndSwitchOn();
            PrintOk("TCP 已连接，已切远程并上电。");

            await robot.StartCriDataPush(localUdpIp, localUdpPort);
            PrintOk("CRI StartDataPush 已开启。");

            await Task.Delay(500);
            PrintCriLine(robot, "CRI 初始");

            printerTask = Task.Run(async () =>
            {
                try
                {
                    while (!printCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(1000, printCts.Token).ConfigureAwait(false);
                        PrintCriLine(robot, "周期");
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常结束
                }
            }, printCts.Token);

            double[] RefJoints() => (double[])robot.Data.JointPosition.Clone();

            async Task WaitMotionSettled(TimeSpan maxWait)
            {
                var sw = Stopwatch.StartNew();
                await Task.Delay(300);
                while (sw.Elapsed < maxWait)
                {
                    if (!robot.Data.InMotion)
                    {
                        await Task.Delay(250);
                        if (!robot.Data.InMotion)
                        {
                            return;
                        }
                    }

                    await Task.Delay(100);
                }

                PrintWarn("等待 InMotion=false 超时，仍继续下一步。");
            }

            // §5.1 常量（S20-180-ECO_V2，与 AGENTS.md / update1 一致）
            var jHome = new[] { 0.0, 0, 90, 0, 90, 0 };
            var jZero = new[] { 0.0, 0, 0, 0, 0, 0 };
            var cpDocHome = new[] { 927.504, 214.495, 898.998, 179.999, 0.0, -90.0 };
            var cpP1 = new[] { 927.511, 214.489, 486.524, 179.999, 0.0, -89.999 };
            var cpP2 = new[] { 927.516, -160.239, 486.534, 180.0, 0.0, -89.999 };
            var cpP3 = new[] { 927.515, -160.238, 1111.244, -179.999, 0.0, -89.999 };
            var cpP4 = new[] { 927.512, 351.971, 1111.249, -179.998, 0.0, -89.999 };

            const double movJSpeed = 40;
            const double movJAcc = 100;
            const double movLSpeed = 150;
            const double movLAcc = 500;

            // --- 1) movJ 三段关节 ---
            PrintMotionPhase(
                ">>> [1/4] movJ×3 — JointPoint（0,0,90,0,90,0）→ 全零 → 回到 home",
                ConsoleColor.Black,
                ConsoleColor.Green);

            await robot.MovJ(JointPoint.Degrees(jHome), movJSpeed, movJAcc);
            PrintOk("MovJ(JointPoint) → home");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            await robot.MovJ(JointPoint.Degrees(jZero), movJSpeed, movJAcc);
            PrintOk("MovJ(JointPoint) → 全零");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            await robot.MovJ(JointPoint.Degrees(jHome), movJSpeed, movJAcc);
            PrintOk("MovJ(JointPoint) → 回到 home");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            // --- 2) 四组合 API（单段门面 + 一条 Move 多段）---
            PrintMotionPhase(
                ">>> [2/4] 四组合 API — movJ(jp) / movJ(cp) / movL(cp) / movL(jp)",
                ConsoleColor.Black,
                ConsoleColor.Yellow);
            Console.WriteLine("  每步前刷新 CRI 关节作逆解参考；TCP 段用 MmDegWithRef。");

            var refA = RefJoints();
            PrintVector6("  当前参考关节 rj", refA, "deg");
            await robot.MovJ(JointPoint.Degrees(jZero), movJSpeed, movJAcc);
            PrintOk("[单段] MovJ(JointPoint) → 全零");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            var refB = RefJoints();
            await robot.MovJ(CartesianPoint.MmDegWithRef(cpP1, refB), movJSpeed, movJAcc);
            PrintOk("[单段] MovJ(CartesianPoint) → P1 TCP（关节运动到笛卡尔）");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            var refC = RefJoints();
            await robot.MovL(CartesianPoint.MmDegWithRef(cpP2, refC), movLSpeed, movLAcc);
            PrintOk("[单段] MovL(CartesianPoint) → P2 TCP");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            var refD = RefJoints();
            await robot.MovL(JointPoint.Degrees(jHome), movLSpeed, movLAcc);
            PrintOk("[单段] MovL(JointPoint) → home 关节（直线到关节目标）");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            PrintMotionPhase(
                ">>> [2/4] 四组合 — 一条 Move(path) 连续四段",
                ConsoleColor.DarkYellow,
                ConsoleColor.Black);
            var refPath = RefJoints();
            await robot.Move(new[]
            {
                MoveInstruction.MovJ(JointPoint.Degrees(jZero), movJSpeed, movJAcc),
                MoveInstruction.MovJ(CartesianPoint.MmDegWithRef(cpP1, refPath), movJSpeed, movJAcc),
                MoveInstruction.MovL(CartesianPoint.MmDegWithRef(cpP2, refPath), movLSpeed, movLAcc),
                MoveInstruction.MovL(JointPoint.Degrees(jHome), movLSpeed, movLAcc),
            });
            PrintOk("Move(path): movJ(jp) → movJ(cp) → movL(cp) → movL(jp)");
            await WaitMotionSettled(TimeSpan.FromMinutes(6));

            // --- 3) movL 矩形：P1 单段 + P2→P3→P4 多段 ---
            PrintMotionPhase(
                ">>> [3/4] movL 矩形 — 单段到 P1，再 Move 连续 P2→P3→P4",
                ConsoleColor.Black,
                ConsoleColor.Cyan);
            PrintVector6("文档参考起点 cp（不单独下发）", cpDocHome, "mm, deg");

            var refP1 = RefJoints();
            await robot.MovL(CartesianPoint.MmDegWithRef(cpP1, refP1), movLSpeed, movLAcc);
            PrintOk("MovL(CartesianPoint) → P1");
            await WaitMotionSettled(TimeSpan.FromMinutes(3));

            PrintMotionPhase(
                ">>> [4/4] Move(path) — movL×3（P2→P3→P4）",
                ConsoleColor.Black,
                ConsoleColor.Magenta);
            var refRect = RefJoints();
            await robot.Move(new[]
            {
                MoveInstruction.MovL(CartesianPoint.MmDegWithRef(cpP2, refRect), movLSpeed, movLAcc),
                MoveInstruction.MovL(CartesianPoint.MmDegWithRef(cpP3, refRect), movLSpeed, movLAcc),
                MoveInstruction.MovL(CartesianPoint.MmDegWithRef(cpP4, refRect), movLSpeed, movLAcc),
            });
            PrintOk("Move(path): movL → P2 → P3 → P4");
            await WaitMotionSettled(TimeSpan.FromMinutes(5));

            printCts.Cancel();
            if (printerTask != null)
            {
                try
                {
                    await printerTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            PrintCriLine(robot, "结束快照");
            PrintBanner($"{model} 运动 + CRI 测试流程结束", ConsoleColor.Green);

            await robot.StopCriDataPush(localUdpIp, localUdpPort);
            PrintOk("CRI 已 StopDataPush。");
        }
        catch (CodroidCommandException ex)
        {
            printCts.Cancel();
            PrintBanner("控制器错误", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (Exception ex)
        {
            printCts.Cancel();
            PrintBanner("测试异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            printCts.Cancel();
            robot.Disconnect();
            Console.WriteLine();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// 正解 / 逆解 / 相对位姿：使用文档示例数据调用 SDK，打印返回的六维向量。
    /// </summary>
    private static async Task RunKinematicsTest(string robotIp)
    {
        var client = new CodroidClient(robotIp);

        PrintBanner($"机器人计算接口测试  |  TCP: {robotIp}:9001", ConsoleColor.White);
        Console.WriteLine();
        Console.WriteLine("说明：数据与接口文档示例一致；实际数值依赖控制器模型，可能与文档印刷值略有差异。");
        Console.WriteLine("子命令：kin | kinematics | fk（任选其一）+ 可选 IP。");

        try
        {
            PrintStep(1, "TCP 连接 → 远程模式 → 上电（ConnectRemoteAndSwitchOn）");
            await client.ConnectRemoteAndSwitchOn();
            PrintOk($"已连接 {robotIp}:9001，已切远程并上电。");

            // ----- 10.1 正解 Robot/apostocpos -----
            PrintStep(2, "正解 AposToCposPose（Robot/apostocpos）");
            var jp = new[] { 0.0, 0.0, 90.0, 0.0, 90.0, 0.0 };
            var coor = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            var tool = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            PrintVector6("输入 jp (deg)", jp, "度");
            PrintVector6("输入 coor", coor, "mm, deg");
            PrintVector6("输入 tool", tool, "mm, deg");

            var cpos = await client.AposToCposPose(jp, coor, tool, Array.Empty<double>());
            PrintVector6("输出 TCP 位姿 db", cpos, "mm, deg（文档示例为 [100,200,300,10,20,30]）");
            PrintOk("正解完成。");

            // ----- 10.2 逆解 Robot/cpostoapos -----
            PrintStep(3, "逆解 CposToAposJoints（Robot/cpostoapos）");
            var cp = new[] { 927.503, 214.5, 898.998, 179.999, 0.0, -90.0 };
            var rj = new[] { 10.0, 20, 30, 40, 50, 60 };
            PrintVector6("输入 cp (末端)", cp, "mm, deg");
            PrintVector6("输入 rj (参考角)", rj, "度；若无解可改为例如全 20 再试");

            try
            {
                var apos = await client.CposToAposJoints(cp, rj, Array.Empty<double>());
                PrintVector6("输出关节角 db", apos, "度");
                PrintOk("逆解完成。");
            }
            catch (InvalidOperationException ex)
            {
                PrintWarn("逆解返回无法解析（常见：db 为空数组）。可调整参考角 rj 后重试。");
                PrintErr(ex.Message);
            }

            // ----- 10.3 相对位姿 Robot/calculateRelativePose -----
            PrintStep(4, "相对位姿 CalculateRelativePoseResult（Robot/calculateRelativePose）");
            var pos = new[] { 927.503, 214.5, 898.998, 179.999, 0.0, -90.0 };
            var posCoor = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            var offset = new[] { 0, 0, -300.0, 0, 0, 0 };
            PrintVector6("输入 pos (世界系 TCP)", pos, "mm, deg");
            PrintVector6("输入 posCoor (可选)", posCoor, "mm, deg");
            PrintVector6("输入 offset", offset, "mm, deg");
            Console.WriteLine("  coorType: tool");  

            var rel = await client.CalculateRelativePoseResult(
                pos,
                offset,
                RelativePoseCoorType.User,
                tcpPoseInPosCoorFrame: null,
                userCoorFrame: null);
            PrintVector6("输出偏移后位姿 db", rel, "mm, deg（文档示例为 posCoor 系下同一组数）");
            PrintOk("相对位姿计算完成。");

            PrintBanner("机器人计算接口测试结束", ConsoleColor.Green);
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (ArgumentException ex)
        {
            PrintBanner("参数错误（ArgumentException）", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        catch (Exception ex)
        {
            PrintBanner("测试异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            client.Disconnect();
            Console.WriteLine();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// 机器人设置 19.x：GetRobotParameters、SetToolFrame(1~15)、SetDefault*Id(0~15)；验证 id=0 不可写。
    /// </summary>
    private static async Task RunRobotParameterTest(string robotIp)
    {
        var robot = new CodroidClient(robotIp);
        PrintBanner($"机器人设置参数（19.x）| TCP {robotIp}:9001", ConsoleColor.White);
        Console.WriteLine("  会修改 Tool[2]（x=15,y=20）并 SetDefaultToolId(2)；请确认无冲突后再跑。");
        Console.WriteLine();

        try
        {
            await robot.Connect();
            PrintOk("TCP 已连接。");

            PrintStep(1, "GetRobotParameters");
            var p = await robot.GetRobotParameters();
            Console.WriteLine(
                $"  defaultToolId={p.DefaultToolId}, defaultPayloadId={p.DefaultPayloadId}, " +
                $"defaultCoordinateId={p.DefaultCoordinateId}, maxPayload={p.MaxPayload}");
            Console.WriteLine($"  Tool 条数={p.Tool.Count}, Payload={p.Payload.Count}, Coordinate={p.Coordinate.Count}");

            var t0 = p.Tool.First(f => f.Id == 0);
            Console.WriteLine($"  Tool[0]（只读）: x={t0.X}, y={t0.Y}, z={t0.Z}");

            PrintStep(2, "SetToolFrame(0) 应被拒绝");
            try
            {
                await robot.SetToolFrame(
                    0,
                    new RobotFrame { Id = 0, X = 1, Y = 0, Z = 0, A = 0, B = 0, C = 0 });
                PrintErr("SetToolFrame(0) 未抛异常，不符合预期。");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                PrintOk("已拒绝: " + ex.Message);
            }

            PrintStep(3, "SetToolFrame(2) — 与协议文档示例一致");
            await robot.SetToolFrame(
                2,
                new RobotFrame { Id = 2, X = 15, Y = 20, Z = 0, A = 0, B = 0, C = 0 });
            PrintOk("已下发 Tool[2] x=15 y=20");

            var pAfter = await robot.GetRobotParameters();
            var t2 = pAfter.Tool.First(f => f.Id == 2);
            Console.WriteLine($"  读回 Tool[2]: x={t2.X}, y={t2.Y}, z={t2.Z}");
            var t0After = pAfter.Tool.First(f => f.Id == 0);
            Console.WriteLine($"  Tool[0] 仍为: x={t0After.X}, y={t0After.Y}（须全零）");

            PrintStep(4, "SetDefaultToolId(2)（允许 0~15）");
            await robot.SetDefaultToolId(2);
            PrintOk("SetDefaultToolId(2) 已下发");

            var pDef = await robot.GetRobotParameters();
            Console.WriteLine($"  读回 defaultToolId={pDef.DefaultToolId}");

            PrintWarn("未调用 SetCollisionSensitivity / SetPayload，避免改变现场运行状态。");
            PrintBanner("robotparam 测试结束", ConsoleColor.Green);
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器错误", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (Exception ex)
        {
            PrintBanner("测试异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            robot.Disconnect();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// 阻塞运动演示：使用 *Sync API（CRI 新鲜度 + InMotion + 目标到位）判定完成。
    /// </summary>
    private static async Task RunSyncMotionTest(string robotIp)
    {
        const string localUdpIp = "192.168.1.150";
        const int localUdpPort = 18888;

        var robot = new CodroidClient(robotIp);
        PrintBanner($"阻塞运动测试（Sync）| {robotIp}", ConsoleColor.White);
        Console.WriteLine($"  将启动 CRI: {localUdpIp}:{localUdpPort}；请按现场修改 Program.cs 常量。");
        Console.WriteLine("  流程：MovJSync(joint) → MovJSync(cart) → MovLSync(joint) → MoveSync(path)");
        Console.WriteLine();

        try
        {
            await robot.ConnectRemoteAndSwitchOn();
            PrintOk("已连接并远程 + 上电。");

            await robot.StartCriDataPush(localUdpIp, localUdpPort);
            PrintOk("CRI StartDataPush 已开启。");
            await Task.Delay(600);

            var wait = new MotionWaitOptions
            {
                Timeout = TimeSpan.FromSeconds(90),
                CriStaleTimeout = TimeSpan.FromMilliseconds(700),
                PollInterval = TimeSpan.FromMilliseconds(50),
                SettledSamples = 3,
            };

            var homeJ = JointPoint.Degrees(new[] { 0.0, 0, 90, 0, 90, 0 });
            var zeroJ = JointPoint.Degrees(new[] { 0.0, 0, 0, 0, 0, 0 });
            var p1 = new[] { 927.511, 214.489, 486.524, 179.999, 0.0, -89.999 };
            var refJ = robot.CriData.JointPosition;
            var p1Cart = CartesianPoint.MmDegWithRef(p1, refJ);

            PrintStep(1, "MovJSync(JointPoint) -> home");
            robot.MovJSync(homeJ, speed: 40, acc: 100, wait: wait);
            PrintOk("完成：MovJSync(joint) 到位");

            PrintStep(2, "MovJSync(CartesianPoint) -> P1");
            robot.MovJSync(p1Cart, speed: 40, acc: 100, wait: wait);
            PrintOk("完成：MovJSync(cart) 到位");

            PrintStep(3, "MovLSync(JointPoint) -> zero");
            robot.MovLSync(zeroJ, speed: 150, acc: 500, wait: wait);
            PrintOk("完成：MovLSync(joint) 到位");

            PrintStep(4, "MoveSync(path) -> [movJ(joint), movL(cart)]");
            var refPath = robot.CriData.JointPosition;
            bool ok = robot.MoveSync(
                new[]
                {
                    MoveInstruction.MovJ(homeJ, 40, 100),
                    MoveInstruction.MovL(CartesianPoint.MmDegWithRef(p1, refPath), 150, 500)
                },
                wait);
            PrintOk($"完成：MoveSync(path) 返回 {ok}");

            PrintStep(5, "MovLSync + blend=10 (平滑过渡)");
            robot.MovLSync(p1Cart, speed: 150, acc: 500, blend: 10, wait: wait);
            PrintOk("完成：blend 平滑过渡");

            PrintStep(6, "MovJSync + relativeBlend=0.5 (相对平滑)");
            robot.MovJSync(homeJ, speed: 40, acc: 100, relativeBlend: 0.5, wait: wait);
            PrintOk("完成：relativeBlend 相对平滑");

            PrintBanner("syncmotion 测试完成", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            PrintBanner("syncmotion 异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            try
            {
                await robot.StopCriDataPush(localUdpIp, localUdpPort);
            }
            catch
            {
                // ignore
            }

            robot.Disconnect();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// IO 联调：批量 GetIoValues、单点 GetDi/Do/Ai/Ao；DO/AO 先读后写当前值，避免误改现场。
    /// </summary>
    private static async Task RunIoTest(string robotIp)
    {
        var client = new CodroidClient(robotIp);
        PrintBanner($"IO 测试（IOManager）| {robotIp}", ConsoleColor.White);
        Console.WriteLine("  端口与文档 13.1 示例一致：DI0、DO10、AI1、AO2。");
        Console.WriteLine("  DO/AO 写入：先读当前值再 Set 相同值，降低对现场输出的影响。");
        Console.WriteLine();

        try
        {
            await client.ConnectRemoteAndSwitchOn();
            PrintOk("已连接并远程 + 上电。");

            PrintStep(1, "批量 GetIoValues（四个点）");
            var batch = await client.GetIoValues(new List<(string Type, int Port)>
            {
                (IoPortKind.Di, 0),
                (IoPortKind.Do, 10),
                (IoPortKind.Ai, 1),
                (IoPortKind.Ao, 2),
            });
            Console.WriteLine("  db: " + batch.db.GetRawText());

            PrintStep(2, "单点 GetDi(0) / GetDo(10) / GetAi(1) / GetAo(2)");
            try
            {
                var di0 = await client.GetDi(0);
                Console.WriteLine($"  DI0 = {di0}");
            }
            catch (Exception ex)
            {
                PrintWarn("  DI0: " + ex.Message);
            }

            try
            {
                var do10 = await client.GetDo(10);
                Console.WriteLine($"  DO10 = {do10}");
            }
            catch (Exception ex)
            {
                PrintWarn("  DO10: " + ex.Message);
            }

            try
            {
                var ai1 = await client.GetAi(1);
                Console.WriteLine($"  AI1 = {ai1}");
            }
            catch (Exception ex)
            {
                PrintWarn("  AI1: " + ex.Message);
            }

            try
            {
                var ao2 = await client.GetAo(2);
                Console.WriteLine($"  AO2 = {ao2}");
            }
            catch (Exception ex)
            {
                PrintWarn("  AO2: " + ex.Message);
            }

            PrintStep(3, "SetDo / SetAo（写回当前读值）");
            try
            {
                var curDo = await client.GetDo(10);
                await client.SetDo(10, curDo);
                PrintOk($"SetDo(10, {curDo}) 已执行。");
            }
            catch (Exception ex)
            {
                PrintWarn("DO 写回跳过: " + ex.Message);
            }

            try
            {
                var curAo = await client.GetAo(2);
                await client.SetAo(2, curAo);
                PrintOk($"SetAo(2, {curAo}) 已执行。");
            }
            catch (Exception ex)
            {
                PrintWarn("AO 写回跳过: " + ex.Message);
            }

            PrintBanner("IO 测试结束", ConsoleColor.Green);
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (Exception ex)
        {
            PrintBanner("IO 测试异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            client.Disconnect();
            Console.WriteLine();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// 寄存器联调：9032~9035 写 1/0；49100/49102/49104 整型 520；49300/49302/49304 浮点 520.52（中间地址按协议应为 49302）。
    /// </summary>
    private static async Task RunRegisterTest(string robotIp)
    {
        var client = new CodroidClient(robotIp);
        PrintBanner($"寄存器测试（RegisterManager）| {robotIp}", ConsoleColor.White);
        Console.WriteLine("  9032~9035：批量读 → 写 1 → 逐个读 → 写 0。");
        Console.WriteLine("  49100 / 49102 / 49104：整型寄存器，批量读 → 写 520 → 逐个读 → 写 0。");
        Console.WriteLine("  49300 / 49302 / 49304：浮点寄存器，批量读 → 写 520.52 → 逐个读 → 写 0。");
        Console.WriteLine();

        try
        {
            await client.ConnectRemoteAndSwitchOn();
            PrintOk("已连接并远程 + 上电。");

            var range9032 = new[] { 9032, 9033, 9034, 9035 };
            var intRegs = new[] { 49100, 49102, 49104 };
            var floatRegs = new[] { 49300, 49302, 49304 };

            PrintBanner("区段 A：9032 ~ 9035", ConsoleColor.DarkCyan);
            await RunRegisterSequence(
                client,
                range9032,
                async () =>
                {
                    foreach (var a in range9032)
                    {
                        await client.SetRegisterValue(a, 1);
                    }
                },
                async () =>
                {
                    foreach (var a in range9032)
                    {
                        await client.SetRegisterValue(a, 0);
                    }
                });

            PrintBanner("区段 B：49100 / 49102 / 49104（整型 520）", ConsoleColor.DarkCyan);
            await RunRegisterSequence(
                client,
                intRegs,
                async () =>
                {
                    foreach (var a in intRegs)
                    {
                        await client.SetRegisterValue(a, 520);
                    }
                },
                async () =>
                {
                    foreach (var a in intRegs)
                    {
                        await client.SetRegisterValue(a, 0);
                    }
                });

            PrintBanner("区段 C：49300 / 49302 / 49304（浮点 520.52）", ConsoleColor.DarkCyan);
            await RunRegisterSequence(
                client,
                floatRegs,
                async () =>
                {
                    foreach (var a in floatRegs)
                    {
                        await client.SetRegisterValue(a, 520.52);
                    }
                },
                async () =>
                {
                    foreach (var a in floatRegs)
                    {
                        await client.SetRegisterValue(a, 0.0);
                    }
                });

            PrintBanner("寄存器测试结束", ConsoleColor.Green);
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (Exception ex)
        {
            PrintBanner("寄存器测试异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            client.Disconnect();
            Console.WriteLine();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// 单组寄存器：批量读 → 写入 → 逐个读 → 清零。
    /// </summary>
    private static async Task RunRegisterSequence(
        CodroidClient client,
        IReadOnlyList<int> addresses,
        Func<Task> writePayload,
        Func<Task> writeClear)
    {
        PrintStep(1, $"批量 GetRegisterValues：{string.Join(", ", addresses)}");
        var batch = await client.GetRegisterValues(addresses);
        foreach (var x in batch)
        {
            Console.WriteLine($"  地址 {x.Address}: {FormatRegisterValue(x)}");
        }

        PrintStep(2, "SetRegisterValue（写入目标值）");
        await writePayload();
        PrintOk("写入完成。");

        PrintStep(3, "逐个 GetRegisterValue 回读");
        foreach (var a in addresses)
        {
            var r = await client.GetRegisterValue(a);
            Console.WriteLine($"  地址 {r.Address}: {FormatRegisterValue(r)}");
        }

        PrintStep(4, "SetRegisterValue（写回 0 / 0.0）");
        await writeClear();
        PrintOk("清零完成。");
    }

    private static string FormatRegisterValue(RegisterReadValue r)
    {
        if (r.TryGetInt32(out var i))
        {
            return $"{i}（整型）";
        }

        return $"{r.GetDouble():G}（浮点）";
    }

    /// <summary>
    /// 仅订阅 <see cref="PublishTopics.RobotStatus"/>：TCP 连接 → 下发订阅帧 → 回调打印推送，10 秒后退出。
    /// </summary>
    /// <param name="partOfFullSuite">为 true 时不重复打印本段大横幅（由 <see cref="RunAllTests"/> 已打印【n/7】）。</param>
    /// <remarks>推送仅在状态变化或首次订阅时出现；静止时可能整条数为 0。</remarks>
    private static async Task RunRobotStatusPublishDemo(string robotIp, bool partOfFullSuite = false)
    {
        var client = new CodroidClient(robotIp);
        int pushCount = 0;

        if (!partOfFullSuite)
        {
            PrintBanner($"订阅 RobotStatus（{PublishTopics.RobotStatus}）10 秒 | {robotIp}", ConsoleColor.White);
            Console.WriteLine("  仅需 TCP；未远程/上电亦可订阅。数据不变则可能无推送。");
            Console.WriteLine();
        }

        try
        {
            await client.Connect();
            PrintOk($"TCP 已连接 {robotIp}:9001。");

            using var subscription = await client.SubscribePublishTopic(
                PublishTopics.RobotStatus,
                msg =>
                {
                    int n = Interlocked.Increment(ref pushCount);
                    string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[{ts}] 第 {n} 条推送  ty={msg.Ty}");
                    Console.ResetColor();
                    if (msg.Db.ValueKind == JsonValueKind.Undefined)
                    {
                        Console.WriteLine("  db: (无)");
                    }
                    else
                    {
                        Console.WriteLine("  db: " + msg.Db.GetRawText());
                    }
                });

            PrintOk("已发送订阅帧（ty+tc，默认 tc=100）。等待 10 秒…");
            await Task.Delay(TimeSpan.FromSeconds(10));

            Console.WriteLine();
            PrintOk($"结束：共收到 {Volatile.Read(ref pushCount)} 条推送（Dispose 取消回调）。");
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (Exception ex)
        {
            PrintBanner("RobotStatus 订阅示例异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            client.Disconnect();
            Console.WriteLine();
            PrintOk("已 Disconnect。");
        }
    }

    /// <summary>
    /// 全局变量接口联调：getVars 快照 → saveVars 批量 → 单条覆盖 → removeVars 清理（可选）。
    /// </summary>
    private static async Task RunGlobalVarTest(string robotIp, bool cleanUp)
    {
        var client = new CodroidClient(robotIp);

        // 变量名须符合 SDK 内 GlobalVarNaming（字母/下划线开头等）；统一前缀避免覆盖现场重要变量
        const string prefix = "sdk_gv_";
        var testNames = new[]
        {
            prefix + "int",
            prefix + "float",
            prefix + "str",
            prefix + "arr",
            prefix + "map",
            prefix + "raw",
        };

        PrintBanner($"Codroid 全局变量测试  |  控制器 TCP: {robotIp}:9001", ConsoleColor.White);
        Console.WriteLine();
        Console.WriteLine("说明：");
        Console.WriteLine($"  · 将使用协议 globalVar/getVars、globalVar/saveVars、globalVar/removeVars。");
        Console.WriteLine($"  · 测试变量名均带前缀 [{prefix}]，默认测试结束会删除这些名字（可用 --no-clean 保留）。");
        Console.WriteLine($"  · 若控制器上已有同名变量，会被本次写入覆盖，请注意。");

        try
        {
            // ----- 连接 -----
            PrintStep(1, "TCP → 远程 → 上电（ConnectRemoteAndSwitchOn）");
            await client.ConnectRemoteAndSwitchOn();
            PrintOk($"已连接 {robotIp}:9001，已切远程并上电。");

            // 局部函数：拉取并打印「解析后的」全局变量目录（GetGlobalVarsCatalog）
            async Task DumpCatalog(string bannerTitle, int stepNo)
            {
                PrintStep(stepNo, bannerTitle);
                PrintBanner("当前全局变量列表（GetGlobalVarsCatalog）", ConsoleColor.DarkCyan);

                var map = await client.GetGlobalVarsCatalog();
                if (map.Count == 0)
                {
                    PrintWarn("字典为空：控制器未返回任何变量，或 db 不是对象。");
                    Console.WriteLine();
                    return;
                }

                Console.WriteLine($"共 {map.Count} 个变量（按名称排序）：");
                Console.WriteLine();

                foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var remark = string.IsNullOrEmpty(kv.Value.Remark) ? "（无备注）" : kv.Value.Remark;
                    Console.WriteLine($"  ┌─ 名称: {kv.Key}");
                    Console.WriteLine($"  │  val: {kv.Value.Value.GetRawText()}");
                    Console.WriteLine($"  └─  nm:  {remark}");
                    Console.WriteLine();
                }
            }

            await DumpCatalog("写入前：读取并打印全部全局变量", stepNo: 2);

            // ----- 批量保存 -----
            PrintStep(3, "批量增量保存（SaveGlobalVars → globalVar/saveVars）");
            Console.WriteLine("将写入以下类型示例：整数、浮点、字符串、数组、字典、GlobalVarRawJson。");
            await client.SaveGlobalVars(new[]
            {
                new GlobalVarSaveItem(testNames[0], 100, "SDK 测试：整数"),
                new GlobalVarSaveItem(testNames[1], 90.4, "SDK 测试：浮点"),
                new GlobalVarSaveItem(testNames[2], "hello_codroid", "SDK 测试：字符串"),
                new GlobalVarSaveItem(testNames[3], new[] { 1, 2, 3, 4, 5 }, "SDK 测试：数组"),
                new GlobalVarSaveItem(testNames[4], new Dictionary<string, int> { ["aaa"] = 100 }, "SDK 测试：表/Map"),
                new GlobalVarSaveItem(testNames[5], new GlobalVarRawJson("{\"jp\":[1,2,3,5,6,8]}"), "SDK 测试：原始 JSON 字面量"),
            });
            PrintOk("saveVars 请求已成功完成（无异常即表示 TCP/协议层成功）。");

            await DumpCatalog("写入后：再次读取目录，确认出现 sdk_gv_* 项", stepNo: 4);

            // ----- 单条覆盖 -----
            PrintStep(5, "单条保存覆盖（SaveGlobalVar → 把 sdk_gv_int 改为 200）");
            await client.SaveGlobalVar(testNames[0], 200, "SDK 测试：整数（已覆盖）");
            PrintOk("覆盖写入完成。");

            await DumpCatalog("覆盖后：确认 sdk_gv_int 的 val 已变化", stepNo: 6);

            // ----- 可选删除 -----
            if (cleanUp)
            {
                PrintStep(7, "删除测试变量（RemoveGlobalVars → globalVar/removeVars）");
                Console.WriteLine("将删除: " + string.Join(", ", testNames));
                await client.RemoveGlobalVars(testNames);
                PrintOk("removeVars 已发送（删除不存在的名字也不会报错）。");

                await DumpCatalog("删除后：sdk_gv_* 应不再出现（若仍见，请核对控制器）", stepNo: 8);
            }
            else
            {
                PrintWarn("已指定 --no-clean：跳过删除。请稍后手动删除或使用示教器清理 sdk_gv_* 变量。");
            }

            PrintBanner("全局变量测试流程正常结束", ConsoleColor.Green);
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine();
                Console.WriteLine("  协议 err 字段原文：");
                Console.WriteLine("  " + ex.ControllerError);
            }
        }
        catch (ArgumentException ex)
        {
            PrintBanner("参数 / 变量名校验失败（ArgumentException）", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        catch (Exception ex)
        {
            PrintBanner("未捕获异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            client.Disconnect();
            Console.WriteLine();
            PrintOk("已调用 Disconnect()，TCP 与 CRI UDP（若曾开启）已释放。");
        }
    }

    /// <summary>
    /// CRI：StartDataPush + 本机 UDP 收包，打印少量实时字段。
    /// </summary>
    private static async Task RunCriDemo(string robotIp)
    {
        var robot = new CodroidClient(robotIp);

        // 本机网卡 IP：控制器会把 CRI 实时包推到这个地址；请改成你 PC 在机器人网段上的地址
        const string localUdpIp = "192.168.1.150";
        const int localUdpPort = 18888;

        var data = robot.Data;

        PrintBanner($"CRI 实时数据演示  |  控制器: {robotIp}  |  本机 UDP: {localUdpIp}:{localUdpPort}", ConsoleColor.Magenta);
        Console.WriteLine("说明：先 TCP 连接，再 StartCriDataPush；控制器向本机 UDP 端口推送二进制包，SDK 解析后写入 robot.Data。");

        try
        {
            PrintStep(1, "TCP 连接");
            await robot.ConnectRemoteAndSwitchOn();
            PrintOk("TCP 已连接，已切远程并上电。");

            PrintStep(2, $"请求 CRI 推送至本机 {localUdpIp}:{localUdpPort}");
            await robot.StartCriDataPush(localUdpIp, localUdpPort);
            PrintOk("StartDataPush 成功，UDP 监听已启动。");

            PrintStep(3, "等待约 5 秒，读取 robot.Data 中的实时字段");
            Console.WriteLine("（后台线程会持续更新 data，以下为某一时刻快照）");
            await Task.Delay(5000);

            PrintBanner("CRI 快照（5 秒后）", ConsoleColor.DarkCyan);
            Console.WriteLine($"  ProjectRunning     : {data.ProjectRunning}");
            Console.WriteLine($"  JointPosition (deg): {string.Join(", ", data.JointPosition.Select(x => x.ToString("F3")))}");
            Console.WriteLine($"  TcpPose (mm/deg)   : {string.Join(", ", data.TcpPose.Select(x => x.ToString("F3")))}");

            PrintStep(4, "再等待 5 秒后停止推送");
            await Task.Delay(5000);

            await robot.StopCriDataPush(localUdpIp, localUdpPort);
            PrintOk("StopCriDataPush 完成，本地 UDP 已关闭。");

            PrintBanner("CRI 演示结束", ConsoleColor.Green);
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器返回错误（CodroidCommandException）", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
            {
                Console.WriteLine("  err: " + ex.ControllerError);
            }
        }
        catch (Exception ex)
        {
            PrintBanner("CRI 演示异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            robot.Disconnect();
            Console.WriteLine();
            PrintOk("已 Disconnect。");
        }
    }
}
