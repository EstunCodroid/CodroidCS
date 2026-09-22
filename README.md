# CodroidCS

Codroid 机器人控制器的 **C# SDK**：通过 TCP/UDP 与 JSON 协议与控制器通信，支持寄存器、IO、运动、CRI 实时数据等能力。

## 单位说明

现场与上层软件一般按 **毫米**、**度** 使用。TCP JSON 侧与 SDK 对外 API（含 **`CriRealTimeData`**）与此一致。**CRI UDP** 二进制载荷在线上为 **米**、**弧度**；`CriRealtimePacketParser` 已换算为毫米与度后再写入模型，请勿把原始 UDP 浮点当作毫米/度。多语言 SDK 应对齐同一换算，详见根目录 **`AGENTS.md`**。

## 环境要求

- [.NET SDK](https://dotnet.microsoft.com/download)：**推荐安装 8.x**（示例程序 `CodroidTest` 面向 `net8.0`）。
- 类库 `CodroidSDK` 同时编译 **net462**、**net6.0** 与 **net8.0**，可按需在项目中引用对应目标框架。
- `.NET Framework 4.6.2+` 版本仅支持 **Windows**；Linux / macOS 请使用 `net6.0` / `net8.0`。

本仓库为托管代码，**可在 Linux、Windows、macOS 上开发与运行**，无需单独做平台分支；仅需安装对应系统的 .NET SDK。

## 仓库结构

| 目录 / 项目 | 说明 |
|-------------|------|
| `CodroidSDK/` | SDK 类库（NuGet 包 id：`Codroidsdk`，构建时可生成 `.nupkg`） |
| `CodroidTestNet8/` | 控制台示例程序（net8.0），演示各类 API 用法 |
| `CodroidTestNet6/` | 控制台示例程序（net6.0），复用同一 `Program.cs` |
| `CodroidTestNet462/` | 控制台示例程序（net462），面向 .NET Framework 4.6.2+ |
| `CodroidCRITest/` | CRI 实时控制示例程序，演示轨迹规划与 UDP CommandData 周期下发 |
| `CodroidCRITestNet462/` | CRI 实时控制示例程序（net462），面向 .NET Framework 4.6.2+ |
| `examples/ForceControlTest/` | 力控接口测试程序（net462 / net6.0 / net8.0） |

## 构建 SDK

```bash
dotnet build CodroidSDK/CodroidCS.csproj -c Release
```

生成的程序集在 `CodroidSDK/bin/Release/net462/`、`net6.0/` 与 `net8.0/`（若单独指定 `-f net8.0` 则仅该框架）。若启用 `GeneratePackageOnBuild`，会在输出目录生成 NuGet 包。

## 运行示例程序

示例默认连接的控制器 IP 在 `CodroidTestNet8/Program.cs` 中的 `DefaultRobotIp`；也可通过命令行传入。

```bash
# 完整套件（默认）
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj

# 指定控制器 IP
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- 192.168.8.10

# 仅运行某一类演示（如 CRI、IO、寄存器等）
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- cri 192.168.8.10
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- io 192.168.8.10
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- register 192.168.8.10
```

更多子命令与说明见 `CodroidTestNet8/Program.cs` 文件顶部注释。

## 力控测试

力控接口测试工程同时面向 **net462**、**net6.0** 与 **net8.0**。Linux / macOS 上运行 `net6.0` 或 `net8.0`；`net462` 仅用于 Windows .NET Framework 4.6.2+。

```bash
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net8.0 -- 192.168.1.136 state
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net6.0 -- 192.168.1.136 state
```

常用模式：

- `state`：读取 `GetForceState()` 与单字段 getter，例如 `GetForceStateEnabled()`
- `calibration`：执行 `ZeroForceCalibration(calibrationTimeMs)`
- `safety`：设置过力保护与力数据健康监控
- `compliance` / `constant`：进入柔顺或恒力模式，测试 `TuneForceParams`
- `contact --allow-motion`：执行接触检测，涉及运动，必须显式追加 `--allow-motion`

当前 `InitForceControl()` 固定下发导纳算法 `algo=1`，不开放算法参数；旧 `FTSensorDriftCalibration` 已移除。

## 在自己的项目中引用

**方式一：项目引用（开发调试）**

```bash
dotnet add path/to/YourApp.csproj reference path/to/CodroidSDK/CodroidCS.csproj
```

**方式二：NuGet**

对打包生成的 `Codroidsdk.*.nupkg` 配置本地源或使用内部 NuGet 源后：

```bash
dotnet add package Codroidsdk
```

在代码中加入 `using Codroid;` 即可使用 SDK 类型。

## API 命名约定

SDK 公共函数名不加 `Async` 后缀，即使返回类型是 `Task` / `Task<T>`。例如：

- `ConnectRemoteAndSwitchOn()`
- `GetIoValues(...)`
- `SetRegisterValue(...)`
- `MovJ(...)` / `MovL(...)` / `MovC(...)` / `MovCircle(...)` / `Move(...)`
- `GetRobotParameters` / `SetDefaultToolId` / `SetToolFrame` / …（机器人设置 19.x）
- `CposToCposPose` / `CposToCposDouble`（笛卡尔坐标系/工具系换算）
- `StartCriControl(...)`
- `CriRealtimeDispatcher.SendTrajectory(...)`

这样做是为了让 C# / Python / C++ 三套 SDK 使用同一套公开函数名。调用方式仍然是 C# 标准异步调用：

```csharp
await robot.ConnectRemoteAndSwitchOn();
```

## 快速上手

下面示例展示最常见流程：连接控制器、切远程、上电、读写 IO / 寄存器，最后断开。

```csharp
using Codroid;

var robot = new CodroidClient("192.168.8.136");

try
{
    await robot.ConnectRemoteAndSwitchOn();

    int di0 = await robot.GetDi(0);
    await robot.SetDo(10, di0);

    RegisterReadValue reg = await robot.GetRegisterValue(49100);
    int value = reg.GetInt32();
    await robot.SetRegisterValue(49100, value + 1);
}
finally
{
    robot.Disconnect();
}
```

所有 TCP 指令都会在控制器返回 `err` 时抛出 `CodroidCommandException`；10 秒内未收到对应 `id` 响应时抛出 `TimeoutException`。参数范围错误（例如 DO 只能写 0/1、CRI 控制周期必须整除 1000）会在 SDK 侧先抛 `ArgumentException` 或 `ArgumentOutOfRangeException`。

### Windows 控制台中文

示例程序入口首行调用（Linux/macOS 无操作）：

```csharp
ConsoleUtf8.InitConsoleUtf8();
```

## 运动指令（2.1.1+）

运动目标使用强类型，避免关节角与 TCP 位姿混用：

| 类型 | 含义 | 工厂 |
|------|------|------|
| `JointPoint` | 六轴关节角（度） | `JointPoint.Degrees(...)` |
| `CartesianPoint` | TCP `[x,y,z,rx,ry,rz]`（mm + 度） | `MmDeg(...)` / `MmDegWithRef(pose, refJoints)` |

单段下发：

```csharp
await robot.MovJ(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), speed: 40, acc: 100);
await robot.MovL(CartesianPoint.MmDegWithRef(pose, robot.CriData.JointPosition), speed: 150, acc: 500);
```

多段路径仍用 `Move`，由 `MoveInstruction` 工厂组段：

```csharp
await robot.Move(new[]
{
    MoveInstruction.MovJ(JointPoint.Degrees(j1), 40, 100),
    MoveInstruction.MovL(CartesianPoint.MmDegWithRef(p2, refJ), 150, 500),
});
```

`MovJ` 可接受 `CartesianPoint`（关节运动到 TCP）；`MovL` 可接受 `JointPoint`（直线到关节目标）。`MovC` / `MovCircle` 仅支持 `CartesianPoint`。笛卡尔点未设 `rj` 时，下发自动补默认 `[20,20,20,20,20,20]`。

### 阻塞与非阻塞

- 非阻塞（返回 `Task`）：`MovJ` / `MovL` / `MovC` / `MovCircle` / `Move`
- 阻塞到“运动停稳（CRI `InMotion` 判定，成功返回 `true`，异常直接抛出）”：`MovJSync` / `MovLSync` / `MovCSync` / `MovCircleSync` / `MoveSync`

`*Sync` 系列须先 `StartCriDataPush`，使用 `MotionWaitOptions` 判定完成：CRI 数据新鲜度、曾 `InMotion=true` 后连续 `SettledSamples` 次 `InMotion=false`。**不比对**关节角或 TCP 与目标点的位置误差。

```csharp
var wait = new MotionWaitOptions
{
    Timeout = TimeSpan.FromSeconds(90),
    CriStaleTimeout = TimeSpan.FromMilliseconds(700),
    SettledSamples = 3
};

robot.MovJSync(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), 40, 100, wait);
robot.MovJSync(CartesianPoint.MmDegWithRef(pose, robot.CriData.JointPosition), 40, 100, wait);
```

## 运动学换算

```csharp
var cp = CartesianPoint.MmDeg([494, 191, 444, -180, 0, -90]);
var coor1 = new[] { 0.0, 0, 10, 0, 0, 0 };
var tool1 = new[] { 0.0, 0, 10, 0, 0, 0 };
var coor2 = new[] { 0.0, 0, 10, 0, 0, 0 };
var tool2 = new[] { 0.0, 0, 10, 0, 0, 0 };

CartesianPoint converted = await robot.CposToCposPose(cp, coor1, tool1, coor2, tool2);
double[] arr = await robot.CposToCposDouble(cp, coor1, tool1, coor2, tool2);
```

## CRI 实时数据与实时控制

实时数据推送：

```csharp
robot.CriDataReceived += data =>
{
    Console.WriteLine(string.Join(", ", data.JointPosition)); // 单位：度
    Console.WriteLine(string.Join(", ", data.TcpPose));       // 单位：mm + 度
};

await robot.StartCriDataPush("192.168.8.150", 18888);
```

实时控制推荐流程：

```csharp
double[] start = robot.CriData.JointPosition;
double[] target = [0, 0, 90, 0, 90, 0];

var request = new TrajectoryRequest
{
    Space = TrajectorySpace.Joint,
    Profile = TrajectoryProfile.Cubic,
    FrequencyHz = 250,
    Speed = 30
};

var trajectory = TrajectoryGenerator.Generate(start, target, request).ToList();

// 多段轨迹拼接（v2.1.8+）
var waypoints = new[] { start, target, new[] { 0.0, 0, 90, 0, 90, 0 } };
var multiTraj = TrajectoryGenerator.GenerateMultiSegment(waypoints, request);

await robot.StartCriControl(filterType: 1, durationMs: 4, startBuffer: 5);
try
{
    using var dispatcher = new CriRealtimeDispatcher("192.168.8.136");
    await dispatcher.SendTrajectory(trajectory, TrajectorySpace.Joint, periodMs: 4);
}
finally
{
    await robot.StopCriControl();
    await robot.StopCriDataPush("192.168.8.150", 18888);
}
```

CRI UDP 线上单位是 m/rad，SDK 对外统一使用 mm/deg；`CriRealtimeDispatcher` 默认会在发送前把 mm/deg 转回 m/rad。

## .NET Framework 4.6.2+ 支持

从 2.1.2 版本开始，SDK 新增 `.NET Framework 4.6.2+` 目标，适用于 WinForms / WPF / 老 .NET Framework 项目。

```xml
<PackageReference Include="Codroidsdk" Version="2.1.11" />
```

注意事项：

- `.NET Framework` 版本仅支持 **Windows**。
- 推荐 `.NET Framework 4.7.2` 或 `4.8`；最低支持 `4.6.2`，不承诺 `4.6.0` / `4.6.1`。
- `net462` 下 CRI 实时控制默认频率为 **250Hz**（`periodMs=4`），可满足常规应用场景。
- `periodMs != 4`、500Hz、1000Hz 不作为 `net462` 默认 SLA，需要现场压测验证。

## 文档目录

`docs/` 目录下包含完整的 API 参考和开发指南：

| 文档 | 说明 |
|---|---|
| [01-quick-start.md](docs/01-quick-start.md) | 快速上手：安装、连接、第一个程序、运行示例项目 |
| [02-concepts.md](docs/02-concepts.md) | 核心概念：TCP/UDP 通信、JSON 协议、单位换算、线程安全 |
| [03-api-reference-codroidclient.md](docs/03-api-reference-codroidclient.md) | CodroidClient 完整 API：连接、工程、脚本、IO、寄存器、机器人设置等 |
| [04-api-reference-motion.md](docs/04-api-reference-motion.md) | 运动控制 API：MovJ/MovL/MovC/Move、Sync 系列、运动学换算 |
| [05-api-reference-types.md](docs/05-api-reference-types.md) | 数据类型：RobotFrame、RobotPayloadFrame、RobotParameters 等 |
| [06-api-reference-cri.md](docs/06-api-reference-cri.md) | CRI 实时接口：数据推送、实时控制、CriRealtimeDispatcher、轨迹生成 |
| [07-api-reference-io-register.md](docs/07-api-reference-io-register.md) | IO 与寄存器 API：DI/DO/AI/AO/Group IO、寄存器读写 |
| [08-api-reference-utilities.md](docs/08-api-reference-utilities.md) | 工具类：ConsoleUtf8、CRC 校验等辅助功能 |
| [09-net462-notes.md](docs/09-net462-notes.md) | .NET Framework 4.6.2+ 注意事项与兼容性说明 |
| [10-api-reference-force-control.md](docs/10-api-reference-force-control.md) | 力控接口 API：零力标定、柔顺控制、恒力控制、接触检测 |
| [11-deployment-guide.md](docs/11-deployment-guide.md) | SDK 部署指南：环境要求、编译输出、DLL 复制与 NuGet 部署 |

另有中英文 SDK 完整手册：

- [CodroidCS-SDK-Manual-v2.1.11-zh.md](docs/CodroidCS-SDK-Manual-v2.1.11-zh.md) — 中文手册
- [CodroidCS-SDK-Manual-v2.1.11-en.md](docs/CodroidCS-SDK-Manual-v2.1.11-en.md) — English manual

## 仓库地址

<https://github.com/Guybod/CodroidCS>

## 许可证

本项目采用 [MIT License](LICENSE)。

---

运行示例前请确认本机网络可达机器人控制器，并根据现场修改 IP 与安全策略。
