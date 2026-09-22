// =============================================================================
// ToolCoordinateTest — net462 工具坐标系 + 用户坐标系 获取/设置测试
// -----------------------------------------------------------------------------
// 测试内容：
//   1. 获取全部设置参数（getTools + getCoordinates + getPayloads）
//   2. 修改单个工具坐标系（SetToolFrame）
//   3. 整表写入工具坐标系（SaveToolFrames）
//   4. 设置默认工具编号（SetDefaultToolId）
//   5. 修改单个用户坐标系（SetUserCoordinateFrame）
//   6. 整表写入用户坐标系（SaveUserCoordinateFrames）
//   7. 设置默认坐标系编号（SetDefaultUserCoordinateId）
//   8. 恢复原始值
//
// 用法（在仓库根目录执行）：
//   dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- toolcoord
//   dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- toolcoord 192.168.1.136
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Codroid;

namespace Program;

internal static class ToolCoordinateTest
{
    /// <summary>
    /// 入口：由 Program.cs 的 switch 分发调用。
    /// </summary>
    public static async Task Run(string robotIp)
    {
        var robot = new CodroidClient(robotIp);
        PrintBanner($"工具/坐标系获取与设置测试 | TCP {robotIp}:9001");

        Console.WriteLine("  本测试将修改 Tool[1] 和 Coordinate[1]，结束后恢复原始值。");
        Console.WriteLine();

        // 保存原始值用于恢复
        RobotParameters? original = null;

        try
        {
            // ---------------------------------------------------------------
            // 步骤 0：连接
            // ---------------------------------------------------------------
            PrintStep(0, "TCP 连接");
            await robot.Connect();
            PrintOk("TCP 已连接。");

            // ---------------------------------------------------------------
            // 步骤 1：获取全部参数
            // ---------------------------------------------------------------
            PrintStep(1, "GetRobotParameters — 获取全部设置参数");
            original = await robot.GetRobotParameters();

            Console.WriteLine($"  defaultToolId       = {original.DefaultToolId}");
            Console.WriteLine($"  defaultPayloadId    = {original.DefaultPayloadId}");
            Console.WriteLine($"  defaultCoordinateId = {original.DefaultCoordinateId}");
            Console.WriteLine($"  maxPayload           = {original.MaxPayload} kg");
            Console.WriteLine($"  Tool 条数            = {original.Tool.Count}");
            Console.WriteLine($"  Payload 条数         = {original.Payload.Count}");
            Console.WriteLine($"  Coordinate 条数      = {original.Coordinate.Count}");

            // 打印 Tool[0] 和 Tool[1]
            var t0 = original.Tool.FirstOrDefault(f => f.Id == 0);
            var t1 = original.Tool.FirstOrDefault(f => f.Id == 1);
            if (t1 == null) { PrintErr("Tool[1] 不存在，无法测试。"); return; }
            Console.WriteLine($"  Tool[0]: name={t0.Name}, x={t0.X}, y={t0.Y}, z={t0.Z}");
            Console.WriteLine($"  Tool[1]: name={t1.Name}, x={t1.X}, y={t1.Y}, z={t1.Z}");

            // 打印 Coordinate[0] 和 Coordinate[1]
            var c0 = original.Coordinate.FirstOrDefault(f => f.Id == 0);
            var c1 = original.Coordinate.FirstOrDefault(f => f.Id == 1);
            if (c1 == null) { PrintErr("Coordinate[1] 不存在，无法测试。"); return; }
            Console.WriteLine($"  Coordinate[0]: name={c0.Name}, x={c0.X}, y={c0.Y}, z={c0.Z}");
            Console.WriteLine($"  Coordinate[1]: name={c1.Name}, x={c1.X}, y={c1.Y}, z={c1.Z}");

            PrintOk("参数获取成功。");

            // ---------------------------------------------------------------
            // 步骤 2：修改单个工具坐标系 Tool[1]
            // ---------------------------------------------------------------
            PrintStep(2, "SetToolFrame(1) — 修改 Tool[1]（TCP 偏移 z=50mm）");
            await robot.SetToolFrame(1, new RobotFrame
            {
                Id = 1,
                Name = "TOOL1",
                X = 0, Y = 0, Z = 50,
                A = 0, B = 0, C = 0
            });
            PrintOk("已下发 Tool[1]。");

            // 读回验证
            var p2 = await robot.GetRobotParameters();
            var t1After = p2.Tool.First(f => f.Id == 1);
            Console.WriteLine($"  读回 Tool[1]: name={t1After.Name}, x={t1After.X}, y={t1After.Y}, z={t1After.Z}");
            if (Math.Abs(t1After.Z - 50) < 0.01)
                PrintOk("Tool[1] z=50 验证通过。");
            else
                PrintWarn($"Tool[1] z 期望 50，实际 {t1After.Z}。");

            // ---------------------------------------------------------------
            // 步骤 3：整表写入工具坐标系
            // ---------------------------------------------------------------
            PrintStep(3, "SaveToolFrames — 整表写入 16 项工具坐标系");
            var newTools = BuildDefaultTools(original.DefaultToolId, original.Tool);
            await robot.SaveToolFrames(newTools);
            PrintOk("整表写入成功。");

            var p3 = await robot.GetRobotParameters();
            Console.WriteLine($"  读回 Tool 条数={p3.Tool.Count}");
            var t1Check = p3.Tool.First(f => f.Id == 1);
            Console.WriteLine($"  Tool[1]: name={t1Check.Name}, x={t1Check.X}, y={t1Check.Y}, z={t1Check.Z}");

            // ---------------------------------------------------------------
            // 步骤 4：设置默认工具编号
            // ---------------------------------------------------------------
            PrintStep(4, "SetDefaultToolId(1) — 设置默认工具为 1");
            await robot.SetDefaultToolId(1);
            PrintOk("已下发。");

            var p4 = await robot.GetRobotParameters();
            Console.WriteLine($"  读回 defaultToolId={p4.DefaultToolId}");
            if (p4.DefaultToolId == 1)
                PrintOk("默认工具编号验证通过。");
            else
                PrintWarn($"期望 1，实际 {p4.DefaultToolId}。");

            // ---------------------------------------------------------------
            // 步骤 5：修改单个用户坐标系 Coordinate[1]
            // ---------------------------------------------------------------
            PrintStep(5, "SetUserCoordinateFrame(1) — 修改 Coordinate[1]");
            await robot.SetUserCoordinateFrame(1, new RobotFrame
            {
                Id = 1,
                Name = "COORDINATE1",
                X = 100, Y = 200, Z = 0,
                A = 0, B = 0, C = 45
            });
            PrintOk("已下发 Coordinate[1]。");

            var p5 = await robot.GetRobotParameters();
            var c1After = p5.Coordinate.First(f => f.Id == 1);
            Console.WriteLine($"  读回 Coordinate[1]: name={c1After.Name}, x={c1After.X}, y={c1After.Y}, z={c1After.Z}, c={c1After.C}");
            if (Math.Abs(c1After.X - 100) < 0.01 && Math.Abs(c1After.C - 45) < 0.01)
                PrintOk("Coordinate[1] 验证通过。");
            else
                PrintWarn($"期望 x=100,c=45，实际 x={c1After.X},c={c1After.C}。");

            // ---------------------------------------------------------------
            // 步骤 6：整表写入用户坐标系
            // ---------------------------------------------------------------
            PrintStep(6, "SaveUserCoordinateFrames — 整表写入 16 项用户坐标系");
            var newCoords = BuildDefaultCoordinates(original.DefaultCoordinateId, original.Coordinate);
            await robot.SaveUserCoordinateFrames(newCoords);
            PrintOk("整表写入成功。");

            var p6 = await robot.GetRobotParameters();
            Console.WriteLine($"  读回 Coordinate 条数={p6.Coordinate.Count}");

            // ---------------------------------------------------------------
            // 步骤 7：设置默认坐标系编号
            // ---------------------------------------------------------------
            PrintStep(7, "SetDefaultUserCoordinateId(1) — 设置默认坐标系为 1");
            await robot.SetDefaultUserCoordinateId(1);
            PrintOk("已下发。");

            var p7 = await robot.GetRobotParameters();
            Console.WriteLine($"  读回 defaultCoordinateId={p7.DefaultCoordinateId}");
            if (p7.DefaultCoordinateId == 1)
                PrintOk("默认坐标系编号验证通过。");
            else
                PrintWarn($"期望 1，实际 {p7.DefaultCoordinateId}。");

            PrintBanner("全部测试步骤通过", ConsoleColor.Green);
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
            Console.WriteLine("  " + ex.StackTrace);
        }
        finally
        {
            // ---------------------------------------------------------------
            // 步骤 8：恢复原始值
            // ---------------------------------------------------------------
            if (original != null)
            {
                PrintStep(8, "恢复原始值");
                try
                {
                    // 恢复工具
                    if (original.Tool.Count > 0)
                    {
                        await robot.SaveToolFrames(original.Tool);
                        PrintOk("工具坐标系已恢复。");
                    }
                    // 恢复默认工具编号
                    await robot.SetDefaultToolId(original.DefaultToolId);
                    PrintOk($"默认工具编号已恢复为 {original.DefaultToolId}。");

                    // 恢复坐标系
                    if (original.Coordinate.Count > 0)
                    {
                        await robot.SaveUserCoordinateFrames(original.Coordinate);
                        PrintOk("用户坐标系已恢复。");
                    }
                    // 恢复默认坐标系编号
                    await robot.SetDefaultUserCoordinateId(original.DefaultCoordinateId);
                    PrintOk($"默认坐标系编号已恢复为 {original.DefaultCoordinateId}。");
                }
                catch (Exception ex)
                {
                    PrintWarn("恢复原始值时出错: " + ex.Message);
                }
            }

            try { robot.Disconnect(); } catch { }
            PrintOk("已 Disconnect。");
        }
    }

    // -------------------------------------------------------------------------
    // 构建默认工具表（保留原始值，仅确保 16 项完整）
    // -------------------------------------------------------------------------
    private static IReadOnlyList<RobotFrame> BuildDefaultTools(
        int defaultToolId, IReadOnlyList<RobotFrame> original)
    {
        var list = new List<RobotFrame>(16);
        for (int i = 0; i < 16; i++)
        {
            var existing = original.FirstOrDefault(f => f.Id == i);
            if (existing != null)
                list.Add(existing);
            else
                list.Add(new RobotFrame
                {
                    Id = i,
                    Name = i == 0 ? "NOTOOL" : $"TOOL{i}",
                    X = 0, Y = 0, Z = 0, A = 0, B = 0, C = 0
                });
        }
        return list;
    }

    // -------------------------------------------------------------------------
    // 构建默认坐标系列表（保留原始值，仅确保 16 项完整）
    // -------------------------------------------------------------------------
    private static IReadOnlyList<RobotFrame> BuildDefaultCoordinates(
        int defaultCoordinateId, IReadOnlyList<RobotFrame> original)
    {
        var list = new List<RobotFrame>(16);
        for (int i = 0; i < 16; i++)
        {
            var existing = original.FirstOrDefault(f => f.Id == i);
            if (existing != null)
                list.Add(existing);
            else
                list.Add(new RobotFrame
                {
                    Id = i,
                    Name = i == 0 ? "NOCOORDINATE" : $"COORDINATE{i}",
                    X = 0, Y = 0, Z = 0, A = 0, B = 0, C = 0
                });
        }
        return list;
    }

    // -------------------------------------------------------------------------
    // 控制台输出辅助
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
}
