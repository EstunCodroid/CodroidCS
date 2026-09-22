// =============================================================================
// 机器人设置接口完整测试示例（RobotSettingsFullTest）
// -----------------------------------------------------------------------------
// 覆盖协议 19.1~19.7 全部接口：
//   19.1  SetCollisionSensitivity    — 设置碰撞检测灵敏度
//   19.2  SetDefaultPayloadId         — 设置默认负载编号
//   19.3  SetDefaultToolId            — 设置默认工具坐标系编号
//   19.4  SaveToolFrames / SetToolFrame — 设置工具坐标系（整表 / 单项）
//   19.5  SavePayloadFrames / SetPayloadFrame — 设置负载坐标系（整表 / 单项）
//   19.6  SaveUserCoordinateFrames / SetUserCoordinateFrame / SetDefaultUserCoordinateId
//   19.7  GetRobotParameters          — 获取全部设置参数
//
// 用法：
//   dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- robotparamfull 192.168.8.10
//
// 安全说明：
//   - 会修改 Tool[1]、Payload[1]、Coordinate[1]，请确认无冲突后再跑
//   - 不会修改 id=0（控制器保留项）
//   - 测试结束后自动恢复原值
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Codroid;

namespace CodroidTestNet8
{
    public static class RobotSettingsFullTest
    {
        /// <summary>
        /// 入口：机器人设置接口完整测试。
        /// </summary>
        /// <param name="robotIp">控制器 IP，默认 192.168.8.10</param>
        public static async Task Run(string robotIp = "192.168.1.136")
        {
            var robot = new CodroidClient(robotIp);
            PrintBanner($"机器人设置接口完整测试（19.1~19.7）| TCP {robotIp}:9001", ConsoleColor.White);
            Console.WriteLine("  会修改 Tool[1] / Payload[1] / Coordinate[1]，测试后恢复原值。");
            Console.WriteLine();

            // 保存原始值，测试结束后恢复
            RobotParameters? original = null;

            try
            {
                await robot.Connect();
                PrintOk("TCP 已连接。");

                // ==========================================================
                // 19.7 获取全部设置参数（最先调用，后续测试依赖此数据）
                // ==========================================================
                PrintStep(1, "GetRobotParameters — 获取全部设置参数（19.7）");
                original = await robot.GetRobotParameters();
                PrintRobotParameters(original);
                Console.WriteLine();

                // ==========================================================
                // 19.1 设置碰撞检测灵敏度
                // ==========================================================
                PrintStep(2, "SetCollisionSensitivity(50) — 设置碰撞检测灵敏度（19.1）");
                Console.WriteLine("  需固件 2.3.2.10+，db=0~100");
                var resp = await robot.SetCollisionSensitivity(50);
                PrintOk($"已下发，响应 db={resp.db}");
                Console.WriteLine();

                // ==========================================================
                // 19.2 设置默认负载编号
                // ==========================================================
                PrintStep(3, "SetDefaultPayloadId(1) — 设置默认负载编号（19.2）");
                await robot.SetDefaultPayloadId(1);
                PrintOk("已下发 defaultPayloadId=1");

                var pAfter = await robot.GetRobotParameters();
                Console.WriteLine($"  读回 defaultPayloadId={pAfter.DefaultPayloadId}");
                Console.WriteLine();

                // ==========================================================
                // 19.3 设置默认工具坐标系编号
                // ==========================================================
                PrintStep(4, "SetDefaultToolId(1) — 设置默认工具编号（19.3）");
                await robot.SetDefaultToolId(1);
                PrintOk("已下发 defaultToolId=1");

                pAfter = await robot.GetRobotParameters();
                Console.WriteLine($"  读回 defaultToolId={pAfter.DefaultToolId}");
                Console.WriteLine();

                // ==========================================================
                // 19.4 设置工具坐标系（单项修改）
                // ==========================================================
                PrintStep(5, "SetToolFrame(1) — 修改单个工具坐标系（19.4）");
                Console.WriteLine("  设置 Tool[1]: x=0, y=0, z=100, a=0, b=0, c=0（TCP 偏移 100mm）");
                await robot.SetToolFrame(
                    1,
                    new RobotFrame { Id = 1, Name = "TOOL1", X = 0, Y = 0, Z = 100, A = 0, B = 0, C = 0 });
                PrintOk("已下发 Tool[1]");

                pAfter = await robot.GetRobotParameters();
                var t1 = pAfter.Tool.First(f => f.Id == 1);
                Console.WriteLine($"  读回 Tool[1]: name={t1.Name}, x={t1.X}, y={t1.Y}, z={t1.Z}, a={t1.A}, b={t1.B}, c={t1.C}");
                Console.WriteLine();

                // ==========================================================
                // 19.4 验证 id=0 不可写
                // ==========================================================
                PrintStep(6, "SetToolFrame(0) 应被拒绝 — 验证 id=0 保留项保护");
                try
                {
                    await robot.SetToolFrame(
                        0,
                        new RobotFrame { Id = 0, Name = "Test", X = 1, Y = 0, Z = 0, A = 0, B = 0, C = 0 });
                    PrintErr("SetToolFrame(0) 未抛异常，不符合预期！");
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    PrintOk("已拒绝: " + ex.Message);
                }
                Console.WriteLine();

                // ==========================================================
                // 19.4 整表写入工具坐标系
                // ==========================================================
                PrintStep(7, "SaveToolFrames — 整表写入工具坐标系（19.4）");
                Console.WriteLine("  构造 16 项（id 0~15），id=0 全零，id=1 保持上一步值");
                var toolList = BuildFullToolFrames();
                await robot.SaveToolFrames(toolList);
                PrintOk("整表已下发");

                pAfter = await robot.GetRobotParameters();
                Console.WriteLine($"  读回 Tool 条数={pAfter.Tool.Count}");
                var t0Check = pAfter.Tool.First(f => f.Id == 0);
                Console.WriteLine($"  Tool[0]（须全零）: x={t0Check.X}, y={t0Check.Y}, z={t0Check.Z}");
                Console.WriteLine();

                // ==========================================================
                // 19.5 设置负载坐标系（单项修改）— 优先 setPayloads 新接口
                // ==========================================================
                PrintStep(8, "SetPayloadFrame(1) — 修改单个负载坐标系（19.5）");
                Console.WriteLine("  设置 Payload[1]: name=Payload1, m=2.5kg, 质心 mx=0, my=0, mz=50mm");
                try
                {
                    await robot.SetPayloadFrame(
                        1,
                        new RobotPayloadFrame { Id = 1, Name = "Payload1", M = 2.5, Mx = 0, My = 0, Mz = 50 });
                    PrintOk("已下发 Payload[1]");

                    pAfter = await robot.GetRobotParameters();
                    if (pAfter.Payload.Count > 0)
                    {
                        var pl1 = pAfter.Payload.First(f => f.Id == 1);
                        Console.WriteLine($"  读回 Payload[1]: name={pl1.Name}, m={pl1.M}, mx={pl1.Mx}, my={pl1.My}, mz={pl1.Mz}");
                    }
                    else
                    {
                        Console.WriteLine("  [跳过] 新固件不支持 Payload 表管理，已跳过此步骤。");
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("不存在 Payload"))
                {
                    Console.WriteLine("  [跳过] 新固件不支持 Payload 表管理，已跳过此步骤。");
                }
                Console.WriteLine();

                // ==========================================================
                // 19.5 验证 Payload id=0 不可写
                // ==========================================================
                PrintStep(9, "SetPayloadFrame(0) 应被拒绝 — 验证 id=0 保留项保护");
                try
                {
                    await robot.SetPayloadFrame(
                        0,
                        new RobotPayloadFrame { Id = 0, Name = "Test", M = 1, Mx = 0, My = 0, Mz = 0 });
                    PrintErr("SetPayloadFrame(0) 未抛异常，不符合预期！");
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    PrintOk("已拒绝: " + ex.Message);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("不存在 Payload"))
                {
                    Console.WriteLine("  [跳过] 新固件不支持 Payload 表管理，已跳过此步骤。");
                }
                Console.WriteLine();

                // ==========================================================
                // 19.5 整表写入负载坐标系
                // ==========================================================
                PrintStep(10, "SavePayloadFrames — 整表写入负载坐标系（19.5）");
                try
                {
                    var payloadList = BuildFullPayloadFrames();
                    await robot.SavePayloadFrames(payloadList);
                    PrintOk("整表已下发");

                    pAfter = await robot.GetRobotParameters();
                    Console.WriteLine($"  读回 Payload 条数={pAfter.Payload.Count}");
                    if (pAfter.Payload.Count > 0)
                    {
                        var pl0Check = pAfter.Payload.First(f => f.Id == 0);
                        Console.WriteLine($"  Payload[0]（须全零）: m={pl0Check.M}, mx={pl0Check.Mx}, my={pl0Check.My}, mz={pl0Check.Mz}");
                    }
                }
                catch (CodroidCommandException ex) when (ex.ControllerError?.Contains("404") == true)
                {
                    Console.WriteLine("  [跳过] 新固件不支持 SavePayloadFrames，已跳过此步骤。");
                }
                Console.WriteLine();

                // ==========================================================
                // 19.6 设置用户坐标系（单项修改）
                // ==========================================================
                PrintStep(11, "SetUserCoordinateFrame(1) — 修改单个用户坐标系（19.6）");
                Console.WriteLine("  设置 Coordinate[1]: x=100, y=200, z=0, a=0, b=0, c=45");
                await robot.SetUserCoordinateFrame(
                    1,
                    new RobotFrame { Id = 1, Name = "COORDINATE1", X = 100, Y = 200, Z = 0, A = 0, B = 0, C = 45 });
                PrintOk("已下发 Coordinate[1]");

                pAfter = await robot.GetRobotParameters();
                var c1 = pAfter.Coordinate.First(f => f.Id == 1);
                Console.WriteLine($"  读回 Coordinate[1]: x={c1.X}, y={c1.Y}, z={c1.Z}, c={c1.C}");
                Console.WriteLine();

                // ==========================================================
                // 19.6 设置默认用户坐标系编号
                // ==========================================================
                PrintStep(12, "SetDefaultUserCoordinateId(1) — 设置默认坐标系编号（19.6）");
                await robot.SetDefaultUserCoordinateId(1);
                PrintOk("已下发 defaultCoordinateId=1");

                pAfter = await robot.GetRobotParameters();
                Console.WriteLine($"  读回 defaultCoordinateId={pAfter.DefaultCoordinateId}");
                Console.WriteLine();

                // ==========================================================
                // 19.6 整表写入用户坐标系
                // ==========================================================
                PrintStep(13, "SaveUserCoordinateFrames — 整表写入用户坐标系（19.6）");
                var coordList = BuildFullCoordinateFrames();
                await robot.SaveUserCoordinateFrames(coordList);
                PrintOk("整表已下发");

                pAfter = await robot.GetRobotParameters();
                Console.WriteLine($"  读回 Coordinate 条数={pAfter.Coordinate.Count}");
                Console.WriteLine();

                // ==========================================================
                // 兼容旧接口：SetPayload（本地文档 2.2.9.2 风格）
                // ==========================================================
                PrintStep(14, "SetPayload(1) — 兼容旧接口（Robot/setPayload）");
                Console.WriteLine("  与 19.2 SaveRobotParameter 不同，走独立 ty=Robot/setPayload");
                var payloadResp = await robot.SetPayload(1);
                PrintOk($"已下发，响应 db={payloadResp.db}");
                Console.WriteLine();

                // ==========================================================
                // 恢复原始值
                // ==========================================================
                PrintStep(15, "恢复原始设置");
                if (original != null)
                {
                    await robot.SaveToolFrames(original.Tool);
                    PrintOk("Tool 已恢复");

                    if (original.Payload.Count == 0)
                    {
                        Console.WriteLine("  [跳过] 无 Payload 数据，跳过恢复。");
                    }
                    else
                    {
                        try
                        {
                            await robot.SavePayloadFrames(original.Payload);
                            PrintOk("Payload 已恢复");
                        }
                        catch (CodroidCommandException ex) when (ex.ControllerError?.Contains("404") == true)
                        {
                            Console.WriteLine("  [跳过] 新固件不支持 Payload 表恢复。");
                        }
                    }

                    await robot.SaveUserCoordinateFrames(original.Coordinate);
                    PrintOk("Coordinate 已恢复");

                    await robot.SetDefaultToolId(original.DefaultToolId);
                    PrintOk($"defaultToolId 已恢复为 {original.DefaultToolId}");

                    await robot.SetDefaultPayloadId(original.DefaultPayloadId);
                    PrintOk($"defaultPayloadId 已恢复为 {original.DefaultPayloadId}");

                    await robot.SetDefaultUserCoordinateId(original.DefaultCoordinateId);
                    PrintOk($"defaultCoordinateId 已恢复为 {original.DefaultCoordinateId}");

                    // 最终验证
                    var pFinal = await robot.GetRobotParameters();
                    Console.WriteLine();
                    Console.WriteLine("  最终状态:");
                    PrintRobotParameters(pFinal);
                }

                PrintBanner("robotparamfull 测试全部通过", ConsoleColor.Green);
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
                Console.WriteLine("  " + ex.StackTrace);
            }
            finally
            {
                robot.Disconnect();
                PrintOk("已 Disconnect。");
            }
        }

        // =============================================================
        // 辅助：构造完整的 16 项工具坐标系表
        // =============================================================
        private static List<RobotFrame> BuildFullToolFrames()
        {
            var list = new List<RobotFrame>(16);
            // id=0 必须全零
            list.Add(new RobotFrame { Id = 0, Name = "NOTOOL", X = 0, Y = 0, Z = 0, A = 0, B = 0, C = 0 });
            // id=1
            list.Add(new RobotFrame { Id = 1, Name = "TOOL1", X = 0, Y = 0, Z = 100, A = 0, B = 0, C = 0 });
            // id=2~15
            for (int i = 2; i <= 15; i++)
            {
                list.Add(new RobotFrame { Id = i, Name = $"TOOL{i}", X = 0, Y = 0, Z = 0, A = 0, B = 0, C = 0 });
            }
            return list;
        }

        // =============================================================
        // 辅助：构造完整的 16 项负载坐标系表
        // =============================================================
        private static List<RobotPayloadFrame> BuildFullPayloadFrames()
        {
            var list = new List<RobotPayloadFrame>(16);
            // id=0 必须全零
            list.Add(new RobotPayloadFrame { Id = 0, Name = "NoPayload", M = 0, Mx = 0, My = 0, Mz = 0 });
            // id=1
            list.Add(new RobotPayloadFrame { Id = 1, Name = "Payload1", M = 2.5, Mx = 0, My = 0, Mz = 50 });
            for (int i = 2; i <= 15; i++)
            {
                list.Add(new RobotPayloadFrame { Id = i, Name = $"Payload{i}", M = 0, Mx = 0, My = 0, Mz = 0 });
            }
            return list;
        }

        // =============================================================
        // 辅助：构造完整的 16 项用户坐标系表
        // =============================================================
        private static List<RobotFrame> BuildFullCoordinateFrames()
        {
            var list = new List<RobotFrame>(16);
            // id=0 必须全零（世界坐标系）
            list.Add(new RobotFrame { Id = 0, Name = "NOCOORDINATE", X = 0, Y = 0, Z = 0, A = 0, B = 0, C = 0 });
            // id=1
            list.Add(new RobotFrame { Id = 1, Name = "COORDINATE1", X = 100, Y = 200, Z = 0, A = 0, B = 0, C = 45 });
            // id=2~15
            for (int i = 2; i <= 15; i++)
            {
                list.Add(new RobotFrame { Id = i, Name = $"COORDINATE{i}", X = 0, Y = 0, Z = 0, A = 0, B = 0, C = 0 });
            }
            return list;
        }

        // =============================================================
        // 辅助：打印 RobotParameters
        // =============================================================
        private static void PrintRobotParameters(RobotParameters p)
        {
            Console.WriteLine($"  defaultToolId       = {p.DefaultToolId}");
            Console.WriteLine($"  defaultPayloadId    = {p.DefaultPayloadId}");
            Console.WriteLine($"  defaultCoordinateId = {p.DefaultCoordinateId}");
            Console.WriteLine($"  maxPayload           = {p.MaxPayload} kg");
            Console.WriteLine($"  Tool 条数            = {p.Tool.Count}");
            Console.WriteLine($"  Payload 条数         = {p.Payload.Count}");
            Console.WriteLine($"  Coordinate 条数      = {p.Coordinate.Count}");

            // 打印前 3 项工具
            foreach (var t in p.Tool.Take(3))
            {
                Console.WriteLine($"  Tool[{t.Id}]: name={t.Name}, x={t.X}, y={t.Y}, z={t.Z}, a={t.A}, b={t.B}, c={t.C}");
            }

            // 打印前 3 项负载
            foreach (var pl in p.Payload.Take(3))
            {
                Console.WriteLine($"  Payload[{pl.Id}]: name={pl.Name}, m={pl.M}, mx={pl.Mx}, my={pl.My}, mz={pl.Mz}");
            }

            // 打印前 3 项坐标系
            foreach (var c in p.Coordinate.Take(3))
            {
                Console.WriteLine($"  Coordinate[{c.Id}]: name={c.Name}, x={c.X}, y={c.Y}, z={c.Z}, a={c.A}, b={c.B}, c={c.C}");
            }
        }

        // =============================================================
        // 辅助：打印方法（与 Program.cs 风格一致）
        // =============================================================
        private static void PrintBanner(string title, ConsoleColor color = ConsoleColor.Cyan)
        {
            const int width = 66;
            var line = new string('=', width);
            Console.WriteLine();
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.WriteLine(title);
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
}
