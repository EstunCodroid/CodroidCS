# Quick Start / 快速上手

## Install / 安装

### NuGet

```bash
dotnet add package Codroidsdk
```

### Project Reference / 项目引用

```bash
dotnet add path/to/YourApp.csproj reference path/to/CodroidSDK/CodroidCS.csproj
```

### Supported Targets / 支持的目标框架

```xml
<!-- net8.0 (recommended) -->
<TargetFramework>net8.0</TargetFramework>

<!-- net6.0 -->
<TargetFramework>net6.0</TargetFramework>

<!-- .NET Framework 4.6.2+ (Windows only) -->
<TargetFramework>net462</TargetFramework>
```

---

## Minimal Example / 最小示例

Connect to the controller, read a digital input, write a digital output, and disconnect.

连接控制器，读取数字输入，写入数字输出，然后断开。

```csharp
using Codroid;

var robot = new CodroidClient("192.168.8.136");

try
{
    // Connect, enter remote mode, and power on / 连接、切换远程、上电
    await robot.ConnectRemoteAndSwitchOn();

    // Read DI port 0 / 读取 DI 端口 0
    int di0 = await robot.GetDi(0);
    Console.WriteLine($"DI 0 = {di0}");

    // Write DI value to DO port 10 / 将 DI 值写入 DO 端口 10
    await robot.SetDo(10, di0);
}
finally
{
    // Always disconnect in finally / 始终在 finally 中断开
    robot.Disconnect();
}
```

---

## Standard Connection Pattern / 标准连接写法

**Recommended: Always call `StartCriDataPush` + `WaitForCriData` after `ConnectRemoteAndSwitchOn`.**

**推荐：连接后立即调用 `StartCriDataPush` + `WaitForCriData`。**

```csharp
using Codroid;

var robot = new CodroidClient("192.168.8.136");

try
{
    // Standard connection pattern / 标准连接写法
    await robot.ConnectRemoteAndSwitchOn();
    await robot.StartCriDataPush("192.168.8.150", 18888);
    await robot.WaitForCriData(5.0);

    // Now you can use all APIs / 现在可以使用所有 API
    robot.MovJSync(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), speed: 40, acc: 100);
}
finally
{
    robot.Disconnect();
}
```

**Why this pattern? / 为什么这个写法？**

- `StartCriDataPush` enables real-time state monitoring / 启用实时状态监控
- `WaitForCriData` ensures CRI data is flowing before any motion / 确保 CRI 数据在运动前已就绪
- Required for `*Sync` blocking motion APIs / `*Sync` 阻塞运动 API 必需
- Recommended for all use cases / 推荐所有场景使用

---

## Complete Workflow Example / 完整工作流示例

```csharp
using Codroid;

ConsoleUtf8.InitConsoleUtf8(); // Windows console UTF-8 / Windows 控制台 UTF-8

var robot = new CodroidClient("192.168.8.136");

try
{
    // 1. Standard connection pattern / 标准连接写法
    await robot.ConnectRemoteAndSwitchOn();
    await robot.StartCriDataPush("192.168.8.150", 18888);
    await robot.WaitForCriData(5.0);

    // 2. IO / IO 操作
    int di0 = await robot.GetDi(0);
    await robot.SetDo(10, di0);

    // 3. Register / 寄存器
    RegisterReadValue reg = await robot.GetRegisterValue(49100);
    int value = reg.GetInt32();
    await robot.SetRegisterValue(49100, value + 1);

    // 4. Motion / 运动
    await robot.MovJ(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), speed: 40, acc: 100);

    // 5. Blocking motion / 阻塞运动
    robot.MovLSync(
        CartesianPoint.MmDegWithRef(new[] { 400, 0, 300, 180, 0, 0 }, robot.CriData.JointPosition),
        speed: 150, acc: 500);
}
finally
{
    robot.Disconnect();
}
```

---

## Run Example Projects / 运行示例项目

### Test Projects / 测试项目

```bash
# net8.0 (full suite / 完整套件)
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj

# With controller IP / 指定控制器 IP
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- 192.168.8.10

# Specific demo / 仅运行某一类演示
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- cri 192.168.8.10
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- io 192.168.8.10
dotnet run --project CodroidTestNet8/CodroidTestNet8.csproj -- register 192.168.8.10

# net462 / .NET Framework 4.6.2
dotnet run --project CodroidTestNet462/CodroidTestNet462.csproj -- 192.168.8.10
```

### RunScriptExample / 脚本下发示例

Located in `examples/RunScriptExample/`. Reads a `.lua` script file and sends it to the robot via `RunScript` for immediate execution.

位于 `examples/RunScriptExample/`。读取 `.lua` 脚本文件，通过 `RunScript` 下发给机器人立即执行。

**Prerequisites / 前置条件:**

- Controller in remote mode / 控制器已切换到远程模式
- Script file accessible on local machine / 脚本文件在本地可访问
- Default script path: `D:\C#proj\CodroidCS-main\test.lua`

**Usage / 用法:**

```bash
# Default IP (192.168.1.136) + default script path
# 默认 IP + 默认脚本路径
dotnet run --project examples/RunScriptExample

# Specify controller IP
# 指定控制器 IP
dotnet run --project examples/RunScriptExample -- 192.168.1.136

# Specify controller IP + local IP (for CRI motion tracking)
# 指定控制器 IP + 本机 IP（用于 CRI 运动跟踪）
dotnet run --project examples/RunScriptExample -- 192.168.1.136 192.168.1.150

# Specify script file path
# 指定脚本文件路径
dotnet run --project examples/RunScriptExample -- "D:\path\to\script.lua"

# Disable motion wait (fire and forget)
# 不等待运动完成（下发即退出）
dotnet run --project examples/RunScriptExample -- --no-wait
```

**Execution Flow / 执行流程:**

| Step / 步骤 | Action / 操作 |
|-------------|---------------|
| 1 | TCP connect → switch to auto → switch to remote → power on / TCP 连接 → 切自动 → 切远程 → 上电 |
| 2 | (Optional) Start CRI data push for motion tracking /（可选）开启 CRI 数据推送，用于等待运动完成 |
| 3 | Read `.lua` script file content / 读取 `.lua` 脚本文件内容 |
| 4 | Stop current project (avoid conflict) / 停止当前工程（避免冲突） |
| 5 | Call `RunScript(mainScript, subThreads, subPrograms, interrupts, vars)` / 下发脚本 |
| 6 | (Optional) Wait for `InMotion = false` (settled) /（可选）等待运动完成 |
| 7 | Cleanup: stop CRI push, disconnect TCP / 清理：关闭 CRI 推送、断开 TCP |

**Script File Format / 脚本文件格式:**

The script file is a plain text file containing robot script commands (Lua syntax):

脚本文件为纯文本，包含机器人脚本指令（Lua 语法）：

```lua
-- test.lua 示例
p1={cp={927.5,231.57,899.875,-180,5.867,-90}}
p2={cp={1053.27,89.41,734.12,-180,5.867,-90}}
p3={cp={715.63,402.85,1120.44,-180,5.867,-90}}

movL(p1)
movL(p2)
movL(p3)
movL(p1)
```

**Passing Variables / 传入变量:**

Uncomment and modify the `vars` dictionary in `Program.cs` to inject variables:

取消 `Program.cs` 中 `vars` 字典的注释并修改，可注入变量：

```csharp
var vars = new Dictionary<string, object>
{
    ["p1"] = new[] { 927.5, 214.5, 899.0, 180.0, 0.0, -90.0 },
    ["speed"] = 1000,
};
```

**CLI Arguments / 命令行参数:**

| Argument / 参数 | Description / 说明 | Default / 默认值 |
|-----------------|-------------------|-------------------|
| Position 1 | Controller IP or script path / 控制器 IP 或脚本路径 | `192.168.1.136` |
| Position 2 | Local UDP IP or script path / 本机 UDP IP 或脚本路径 | `192.168.1.150` |
| Position 3 | Script file path / 脚本文件路径 | `D:\C#proj\CodroidCS-main\test.lua` |
| `--no-wait` | Skip motion wait / 跳过运动等待 | (enabled by default / 默认启用等待) |
| `--wait` | Enable motion wait / 启用运动等待 | |
| `-s` / `--script` | Specify script path / 指定脚本路径 | |

### PublishSubscribeExample / 主题订阅示例

Located in `examples/PublishSubscribeExample/`. Demonstrates TCP topic subscription (protocol 15.x). The controller pushes messages on data change; the SDK dispatches to registered callbacks.

位于 `examples/PublishSubscribeExample/`。演示 TCP 主题订阅（协议 15.x）。控制器在数据变化时推送消息，SDK 分发到已注册的回调。

**Available Topics / 可订阅主题:**

| Topic / 主题 | Description / 说明 |
|---|---|
| `publish/ProjectState` | Project state / 工程状态 |
| `publish/RobotStatus` | Robot status / 机器人状态 |
| `publish/RobotPosture` | Robot posture / 机器人姿态 |
| `publish/VarUpdate` | Variable update / 变量数据更新 |
| `publish/Log` | Log / 日志 |
| `publish/Error` | Error/Alarm / 错误报警 |

**Usage / 用法:**

```bash
# Subscribe all topics, default IP, run 30s
# 订阅全部主题，默认 IP，运行 30 秒
dotnet run --project examples/PublishSubscribeExample

# Specify controller IP
# 指定控制器 IP
dotnet run --project examples/PublishSubscribeExample -- 192.168.1.136

# Subscribe only RobotStatus
# 仅订阅 RobotStatus
dotnet run --project examples/PublishSubscribeExample -- 192.168.1.136 status

# Specify tc (push interval) = 50ms
# 指定 tc（推送间隔）= 50ms
dotnet run --project examples/PublishSubscribeExample -- 192.168.1.136 all 50
```

**Core API / 核心 API:**

```csharp
// Subscribe a topic; returns a disposable subscription
// 订阅主题；返回可释放的订阅句柄
using var sub = await robot.SubscribePublishTopic(
    PublishTopics.RobotStatus,
    notification =>
    {
        Console.WriteLine($"Topic: {notification.Ty}");
        Console.WriteLine($"Data: {notification.Db}");
        Console.WriteLine($"Raw JSON: {notification.RawJson}");
    },
    tcMilliseconds: 100);

// Dispose to unregister callback (does NOT send unsubscribe to controller)
// Dispose 取消回调（不会向控制器发退订帧）
// TCP 断开后订阅失效，重连后需再次调用
```

### CriDataPushExample / CRI 实时数据推送示例

Located in `examples/CriDataPushExample/`. Demonstrates CRI real-time data push via UDP. The controller sends binary packets at fixed intervals; the SDK parses them into `CriRealTimeData` (units converted to mm/deg).

位于 `examples/CriDataPushExample/`。演示通过 UDP 接收 CRI 实时数据推送。控制器以固定周期发送二进制数据包，SDK 解析为 `CriRealTimeData`（单位已换算为 mm/deg）。

**Usage / 用法:**

```bash
# Default IP, run 10 seconds
# 默认 IP，运行 10 秒
dotnet run --project examples/CriDataPushExample

# Specify controller IP
# 指定控制器 IP
dotnet run --project examples/CriDataPushExample -- 192.168.1.136

# Specify controller IP + local UDP IP
# 指定控制器 IP + 本机 UDP IP
dotnet run --project examples/CriDataPushExample -- 192.168.1.136 192.168.1.150

# Run for 30 seconds
# 运行 30 秒
dotnet run --project examples/CriDataPushExample -- 192.168.1.136 192.168.1.150 30
```

**Core API / 核心 API:**

```csharp
// 1. Start CRI data push (controller sends UDP to local IP:port)
//    开启 CRI 数据推送（控制器向本机 IP:端口推送 UDP 数据）
await robot.StartCriDataPush("192.168.1.150", 18888);

// 2. Option A: Event callback (fires on every frame, on thread pool)
//    方式 A：事件回调（每帧触发，在线程池执行）
robot.CriDataReceived += data =>
{
    // data.TcpPose[6]         — TCP pose [x,y,z,rx,ry,rz], mm + deg
    // data.JointPosition[6]   — joint angles, deg
    // data.TcpLinearVelocity  — TCP linear speed, mm/s
    // data.InMotion           — is moving
    // data.CriErrorCode       — 0 = OK
    // data.RealTimeControlMode — real-time control active
};

// 2. Option B: Poll latest snapshot (thread-safe deep copy)
//    方式 B：轮询最新快照（线程安全深拷贝）
CriRealTimeData snap = robot.CriData;
Console.WriteLine($"TCP: [{string.Join(", ", snap.TcpPose)}]");
Console.WriteLine($"Joints: [{string.Join(", ", snap.JointPosition)}]");

// 3. Stop CRI data push
//    关闭 CRI 数据推送
await robot.StopCriDataPush("192.168.1.150", 18888);
```

**CriRealTimeData Key Fields / 关键字段:**

| Field / 字段 | Type | Unit / 单位 | Description / 说明 |
|---|---|---|---|
| `TcpPose[6]` | `double[]` | mm + deg | TCP pose [x,y,z,rx,ry,rz] |
| `JointPosition[6]` | `double[]` | deg | Joint angles / 关节角 |
| `JointVelocity[6]` | `double[]` | deg/s | Joint angular velocity / 关节角速度 |
| `TcpVelocity[6]` | `double[]` | mm/s + deg/s | TCP velocity / TCP 速度 |
| `TcpLinearVelocity` | `double` | mm/s | TCP linear speed scalar / TCP 线速度标量 |
| `InMotion` | `bool` | — | Is moving / 是否运动中 |
| `ProjectRunning` | `bool` | — | Project running / 工程运行中 |
| `AutoMode` | `bool` | — | Auto mode / 自动模式 |
| `RemoteMode` | `bool` | — | Remote mode / 远程模式 |
| `SimulationMode` | `bool` | — | Simulation mode / 仿真模式 |
| `EmergencyStopPressed` | `bool` | — | E-stop pressed / 急停按下 |
| `HasAlarm` | `bool` | — | Has alarm / 存在报警 |
| `CriErrorCode` | `byte` | — | CRI error code (0=OK) / CRI 错误码 |
| `RealTimeControlMode` | `bool` | — | Real-time control active / 实时控制模式 |

---

## Error Handling / 错误处理

All TCP commands throw on failure:

所有 TCP 指令在失败时抛出异常：

| Exception / 异常 | Condition / 条件 |
|-----------------|-----------------|
| `CodroidCommandException` | Controller returns `err` / 控制器返回 `err` |
| `TimeoutException` | No response within 10 seconds / 10 秒内未收到响应 |
| `ArgumentException` | Invalid parameter (SDK-side validation) / 参数无效（SDK 侧校验） |

```csharp
try
{
    await robot.SetDo(999, 1); // Invalid port / 无效端口
}
catch (CodroidCommandException ex)
{
    Console.WriteLine($"Controller error: {ex.ControllerError}");
}
catch (TimeoutException)
{
    Console.WriteLine("Request timed out / 请求超时");
}
```
