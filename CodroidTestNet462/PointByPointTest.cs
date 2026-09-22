// =============================================================================
// PointByPointTest — 逐点下发机器人位置测试（net462）
// -----------------------------------------------------------------------------
// 从当前位姿出发，生成 ±300mm 范围内的随机点位，逐点 MovL 下发并等待到位。
// 使用 MovLSync（阻塞式），每点运动完成后才下发下一个点。
//
// 用法（在仓库根目录执行）：
//   dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- points
//   dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- points 192.168.1.136
//   dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- points 192.168.1.136 20
//   dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- points 192.168.1.136 20 100
//                                                       点数              速度(mm/s)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Codroid;

namespace Program;

internal static class PointByPointTest
{
    private const string LocalUdpIp = "192.168.1.150";
    private const int LocalUdpPort = 18888;
    private const int DefaultPointCount = 10;
    private const double DefaultSpeed = 100;       // mm/s
    private const double DefaultAcc = 200;          // mm/s²

    public static async Task Run(string robotIp, string[] args)
    {
        // 解析参数
        int pointCount = DefaultPointCount;
        double speed = DefaultSpeed;
        var argv = args ?? Array.Empty<string>();
        // 去掉子命令名
        var rest = new List<string>();
        bool skipFirst = true;
        foreach (var a in argv)
        {
            if (skipFirst && (a == "points" || a == "pointbypoint"))
            { skipFirst = false; continue; }
            rest.Add(a);
        }
        if (rest.Count >= 1 && int.TryParse(rest[0], out var pc)) pointCount = pc;
        if (rest.Count >= 2 && double.TryParse(rest[1], out var sp)) speed = sp;

        var robot = new CodroidClient(robotIp);
        PrintBanner($"逐点下发测试 | {robotIp} | {pointCount} 点 | 速度 {speed} mm/s");

        try
        {
            // ---------------------------------------------------------------
            // 1. 连接 + 切远程 + 上电
            // ---------------------------------------------------------------
            PrintStep(1, "连接 + 切远程 + 上电");
            await robot.ConnectRemoteAndSwitchOn();
            PrintOk("已连接，已切远程并上电。");

            // ---------------------------------------------------------------
            // 2. 开启 CRI 数据推送（MovLSync 需要）
            // ---------------------------------------------------------------
            PrintStep(2, $"开启 CRI 数据推送 → {LocalUdpIp}:{LocalUdpPort}");
            await robot.StartCriDataPush(LocalUdpIp, LocalUdpPort);
            PrintOk("CRI 数据推送已开启。");
            await Task.Delay(600);

            // ---------------------------------------------------------------
            // 3. 读取当前位姿
            // ---------------------------------------------------------------
            PrintStep(3, "读取当前 TCP 位姿");
            var cri = robot.CriData;
            var currentPose = cri.TcpPose;
            if (currentPose == null || currentPose.Length < 6)
            {
                PrintErr("无法读取当前位姿，CRI 数据未就绪。");
                return;
            }
            PrintOk($"当前位姿: [{currentPose[0]:F3}, {currentPose[1]:F3}, {currentPose[2]:F3}, {currentPose[3]:F3}, {currentPose[4]:F3}, {currentPose[5]:F3}]");

            // ---------------------------------------------------------------
            // 4. 生成随机点位（±300mm 偏移，姿态保持不变）
            // ---------------------------------------------------------------
            PrintStep(4, $"生成 {pointCount} 个随机点位（±300mm 偏移）");
            var rand = new Random(42); // 固定种子，可复现
            var points = new double[pointCount][];
            for (int i = 0; i < pointCount; i++)
            {
                points[i] = new double[6];
                points[i][0] = currentPose[0] + rand.NextDouble() * 600 - 300;
                points[i][1] = currentPose[1] + rand.NextDouble() * 600 - 300;
                points[i][2] = currentPose[2] + rand.NextDouble() * 600 - 300;
                points[i][3] = currentPose[3];
                points[i][4] = currentPose[4];
                points[i][5] = currentPose[5];
            }
            // 第一个点为当前位姿（原地不动，热身）
            points[0] = (double[])currentPose.Clone();

            for (int i = 0; i < pointCount; i++)
            {
                Console.WriteLine($"  P{i + 1} = [{points[i][0]:F3}, {points[i][1]:F3}, {points[i][2]:F3}, {points[i][3]:F3}, {points[i][4]:F3}, {points[i][5]:F3}]");
            }

            // ---------------------------------------------------------------
            // 5. 逐点 MovL 下发
            // ---------------------------------------------------------------
            PrintStep(5, "开始逐点下发 MovL");
            Console.WriteLine();

            var waitOpts = new MotionWaitOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                CriStaleTimeout = TimeSpan.FromMilliseconds(500),
                SettledSamples = 3,
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int successCount = 0;

            for (int i = 0; i < pointCount; i++)
            {
                var pt = CartesianPoint.MmDeg(points[i]);
                Console.Write($"  [{i + 1}/{pointCount}] MovL → P{i + 1} ... ");

                var stepSw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    robot.MovLSync(pt, speed, DefaultAcc, waitOpts);
                    stepSw.Stop();
                    Console.WriteLine($"✔ 到位 ({stepSw.Elapsed.TotalSeconds:F2}s)");
                    successCount++;
                }
                catch (TimeoutException ex)
                {
                    stepSw.Stop();
                    Console.WriteLine($"✘ 超时 ({stepSw.Elapsed.TotalSeconds:F2}s): {ex.Message}");
                }
                catch (CodroidCommandException ex)
                {
                    stepSw.Stop();
                    Console.WriteLine($"✘ 控制器错误: {ex.Message}");
                    if (ex.ControllerError != null)
                        Console.WriteLine($"       err: {ex.ControllerError}");
                    break;
                }
                catch (Exception ex)
                {
                    stepSw.Stop();
                    Console.WriteLine($"✘ 异常: {ex.Message}");
                    break;
                }
            }

            sw.Stop();

            // ---------------------------------------------------------------
            // 6. 结果统计
            // ---------------------------------------------------------------
            Console.WriteLine();
            PrintBanner($"逐点下发完成 | {successCount}/{pointCount} 成功 | 总耗时 {sw.Elapsed.TotalSeconds:F1}s", ConsoleColor.Green);

            // ---------------------------------------------------------------
            // 7. 回到起点
            // ---------------------------------------------------------------
            PrintStep(7, "返回起点");
            try
            {
                var startPt = CartesianPoint.MmDeg(currentPose);
                robot.MovLSync(startPt, speed, DefaultAcc, waitOpts);
                PrintOk("已回到起点。");
            }
            catch (Exception ex)
            {
                PrintWarn("回到起点失败: " + ex.Message);
            }
        }
        catch (CodroidCommandException ex)
        {
            PrintBanner("控制器错误", ConsoleColor.Red);
            PrintErr(ex.Message);
            if (ex.ControllerError != null)
                Console.WriteLine("  err: " + ex.ControllerError);
        }
        catch (Exception ex)
        {
            PrintBanner("测试异常", ConsoleColor.Red);
            PrintErr(ex.Message);
        }
        finally
        {
            try { await robot.StopCriDataPush(LocalUdpIp, LocalUdpPort); } catch { }
            try { robot.Disconnect(); } catch { }
            PrintOk("已断开连接。");
        }
    }

    // -------------------------------------------------------------------------
    // 控制台辅助
    // -------------------------------------------------------------------------
    private static void PrintBanner(string title, ConsoleColor color = ConsoleColor.Cyan)
    {
        var line = new string('=', 66);
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
}
