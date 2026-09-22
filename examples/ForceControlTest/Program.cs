using System;
using System.Linq;
using System.Threading.Tasks;
using Codroid;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ConsoleUtf8.InitConsoleUtf8();

        string ip = args.Length > 0 ? args[0] : "192.168.1.136";
        string mode = args.Length > 1 ? args[1] : "state";
        bool allowMotion = args.Any(x => x == "--allow-motion");

        var robot = new CodroidClient(ip);
        await robot.Connect();

        try
        {
            if (mode == "state")
            {
                await PrintState(robot);
                return 0;
            }

            await robot.ClearSystemError();
            await robot.EnterRemoteModeViaAuto();
            await robot.SwitchOn();
            await Task.Delay(2000);

            switch (mode)
            {
                case "calibration":
                    PrintResponse("ZeroForceCalibration", await robot.ZeroForceCalibration());
                    break;
                case "safety":
                    PrintResponse("SetOverforceProtection",
                        await robot.SetOverforceProtection(
                            enable: true,
                            forceThreshold: new[] { 150.0, 150.0, 20.0, 40.0, 40.0, 40.0 },
                            holdMs: 20));
                    PrintResponse("SetForceDataHealth",
                        await robot.SetForceDataHealth(enable: true, timeoutMs: 200, maxPacketLossRatio: 0.9));
                    break;
                case "compliance":
                    await RunCompliance(robot);
                    break;
                case "constant":
                    await RunConstantForce(robot);
                    break;
                case "contact":
                    if (!allowMotion)
                        throw new InvalidOperationException("contact mode requires --allow-motion");
                    await RunContactDetection(robot);
                    break;
                default:
                    throw new ArgumentException($"unknown mode: {mode}");
            }

            await robot.ToAuto();
            await robot.ToManual();
            await robot.SwitchOff();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try { await robot.StopForceControl(300); } catch { }
            try { await robot.SwitchOff(); } catch { }
            return 2;
        }
        finally
        {
            robot.Disconnect();
        }
    }

    private static async Task RunCompliance(CodroidClient robot)
    {
        PrintResponse("InitForceControl", await robot.InitForceControl(
            ForceFrame.Tcp,
            new[]
            {
                ForceAxisMode.Position, ForceAxisMode.Position, ForceAxisMode.Compliant,
                ForceAxisMode.Position, ForceAxisMode.Position, ForceAxisMode.Position
            },
            compliance: new
            {
                stiffness = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
                damping = new[] { 250.0, 250.0, 50.0, 7.5, 7.5, 7.5 },
                mass = new[] { 2.5, 2.5, 1.5, 0.15, 0.15, 0.15 }
            }));
        PrintResponse("StartForceControl", await robot.StartForceControl());
        await PollState(robot, 5);
        PrintResponse("StopForceControl", await robot.StopForceControl());
    }

    private static async Task RunConstantForce(CodroidClient robot)
    {
        PrintResponse("InitForceControl", await robot.InitForceControl(
            ForceFrame.Tcp,
            new[]
            {
                ForceAxisMode.Position, ForceAxisMode.Position, ForceAxisMode.Force,
                ForceAxisMode.Position, ForceAxisMode.Position, ForceAxisMode.Position
            },
            constantForce: new
            {
                desiredForce = new[] { 0.0, 0.0, 2.0, 0.0, 0.0, 0.0 },
                damping = new[] { 250.0, 250.0, 250.0, 7.5, 7.5, 7.5 },
                mass = new[] { 2.5, 2.5, 2.5, 0.15, 0.15, 0.15 },
                rampTimeMs = 500
            }));
        PrintResponse("StartForceControl", await robot.StartForceControl());
        await PollState(robot, 3);
        PrintResponse("TuneForceParams",
            await robot.TuneForceParams(desiredForce: new[] { 0.0, 0.0, 5.0, 0.0, 0.0, 0.0 }, rampTime: 500));
        await PollState(robot, 3);
        PrintResponse("StopForceControl", await robot.StopForceControl());
    }

    private static async Task RunContactDetection(CodroidClient robot)
    {
        PrintResponse("InitForceControl", await robot.InitForceControl(
            ForceFrame.Tcp,
            new[]
            {
                ForceAxisMode.Compliant, ForceAxisMode.Compliant, ForceAxisMode.Force,
                ForceAxisMode.Compliant, ForceAxisMode.Compliant, ForceAxisMode.Compliant
            },
            compliance: new
            {
                stiffness = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
                damping = new[] { 250.0, 250.0, 50.0, 7.5, 7.5, 7.5 },
                mass = new[] { 2.5, 2.5, 1.5, 0.15, 0.15, 0.15 }
            },
            constantForce: new
            {
                desiredForce = new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
                damping = new[] { 250.0, 250.0, 250.0, 7.5, 7.5, 7.5 },
                mass = new[] { 2.5, 2.5, 2.5, 0.15, 0.15, 0.15 }
            }));
        PrintResponse("StartForceControl", await robot.StartForceControl());
        PrintResponse("StartContactDetection",
            await robot.StartContactDetection(
                direction: new[] { 0.0, 0.0, -1.0, 0.0, 0.0, 0.0 },
                feedVelocity: 0.002,
                contactForceThreshold: 3,
                velDropRatio: 0,
                maxTravel: 0.01,
                timeoutMs: 5000));
        await PollState(robot, 5);
        PrintResponse("StopForceControl", await robot.StopForceControl());
    }

    private static async Task PollState(CodroidClient robot, int seconds)
    {
        for (int i = 0; i < seconds * 2; ++i)
        {
            await PrintState(robot);
            await Task.Delay(500);
        }
    }

    private static async Task PrintState(CodroidClient robot)
    {
        ForceControlState s = await robot.GetForceState();
        Console.WriteLine($"enabled={s.Enabled} pending={s.Pending} algo={s.Algo} valid={s.Valid} contact={s.IsContact} overforce={s.IsOverforce} health={s.Health}");
        Console.WriteLine($"wrenchTcp=[{string.Join(", ", s.WrenchTcp.Select(x => x.ToString("F3")))}]");
        Console.WriteLine($"desiredWrench=[{string.Join(", ", s.DesiredWrench.Select(x => x.ToString("F3")))}]");
        Console.WriteLine($"GetForceStateEnabled()={await robot.GetForceStateEnabled()}");
    }

    private static void PrintResponse(string label, CommonResponse response)
    {
        Console.WriteLine($"{label}: err={response.err ?? "<none>"}, db={response.db}");
    }
}
