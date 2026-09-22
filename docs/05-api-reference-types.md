# API Reference: Data Types / API 参考：数据类型

This document provides a comprehensive reference for all data types, enums, and exceptions in the Codroid CRI SDK.

本文档提供 Codroid CRI SDK 中所有数据类型、枚举和异常的完整参考。

---

## Table of Contents / 目录

1. [CommonResponse](#1-commonresponse)
2. [CriRealTimeData](#2-crirealtimedata)
3. [RobotFrame](#3-robotframe)
4. [RobotPayloadFrame](#4-robotpayloadframe)
5. [RobotParameters](#5-robotparameters)
6. [RegisterReadValue](#6-registervalue)
7. [RegisterExtendArrayValueType](#7-registerextendarrayvaluetype)
8. [IoPortKind](#8-ioportkind)
9. [RelativePoseCoorType](#9-relativeposecoortype)
10. [CodroidCommandException](#10-codroidcommandexception)
11. [GlobalVarSaveItem](#11-globalvarsaveitem)
12. [GlobalVarRawJson](#12-globalvarrawjson)
13. [GlobalVarCatalogEntry](#13-globalvarcatalogentry)

---

## 1. CommonResponse

**Common Response / 通用响应**

A general-purpose response class returned by most CRI SDK methods. Contains the request ID, a type identifier, a JSON data payload, and an optional error message.

大多数 CRI SDK 方法返回的通用响应类。包含请求 ID、类型标识符、JSON 数据载荷和可选的错误消息。

### Properties / 属性

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `id` | `object?` | The request identifier / 请求标识符 | 原始请求的标识符，可用于匹配请求与响应 |
| `ty` | `string?` | The response type identifier / 响应类型标识符 | 标识响应的类型，用于区分不同类型的返回数据 |
| `db` | `JsonElement` | The response data payload / 响应数据载荷 | 包含实际返回数据的 JSON 元素，需根据 `ty` 解析 |
| `err` | `string?` | Error message, if any / 错误信息（如有） | 当请求失败时包含错误描述，成功时为 `null` |

### Example: Reading the `db` field / 示例：读取 db 字段

Reading and parsing the `db` field from a `CommonResponse` to extract the data payload.

从 `CommonResponse` 中读取并解析 `db` 字段以提取数据载荷。

```csharp
CommonResponse response = await client.SomeMethodAsync();

// Check for errors first / 先检查错误
if (response.err != null)
{
    Console.WriteLine($"Error / 错误: {response.err}");
    return;
}

// Read db as a specific type / 将 db 读取为特定类型
int value = response.db.GetInt32();
Console.WriteLine($"Value / 值: {value}");

// Or read as JSON string / 或者读取为 JSON 字符串
string json = response.db.GetRawText();
Console.WriteLine($"Raw JSON / 原始 JSON: {json}");
```

---

## 2. CriRealTimeData

**CRI Real-Time Data / CRI 实时数据**

Contains all real-time data fields from the robot controller, including joint positions, TCP poses, status flags, and more. Updated continuously via the CRI connection.

包含来自机器人控制器的所有实时数据字段，包括关节位置、TCP 位姿、状态标志等。通过 CRI 连接持续更新。

### Properties / 属性

#### Timestamp / 时间戳

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `TimestampMs` | `long` | Timestamp in milliseconds / 毫秒级时间戳 | 控制器端的时间戳，单位为毫秒 |

#### Status Flags / 状态标志

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `Status1Raw` | `ushort` | Raw status word 1 / 原始状态字 1 | 控制器原始状态寄存器 1 |
| `Status2Raw` | `ushort` | Raw status word 2 / 原始状态字 2 | 控制器原始状态寄存器 2 |
| `ProjectRunning` | `bool` | Whether a project is currently running / 项目是否正在运行 | 表示当前是否有程序在运行 |
| `ProjectStopped` | `bool` | Whether the project is stopped / 项目是否已停止 | 表示程序是否已停止 |
| `ProjectPaused` | `bool` | Whether the project is paused / 项目是否已暂停 | 表示程序是否处于暂停状态 |
| `Enabling` | `bool` | Whether the enabling switch is active / 使能开关是否激活 | 表示使能开关是否处于激活状态 |
| `NotEnabled` | `bool` | Whether the robot is not enabled / 机器人是否未使能 | 表示机器人未处于使能状态 |
| `ManualMode` | `bool` | Whether in manual mode / 是否处于手动模式 | 表示当前是否为手动操作模式 |
| `Dragging` | `bool` | Whether the robot is being dragged / 机器人是否正在被拖动 | 表示机器人是否处于拖动示教状态 |
| `InMotion` | `bool` | Whether the robot is in motion / 机器人是否正在运动 | 表示机器人当前是否有轴在运动 |
| `CollisionStopped` | `bool` | Whether stopped due to collision / 是否因碰撞而停止 | 表示机器人是否因检测到碰撞而停止 |
| `InSafetyPosition` | `bool` | Whether in the safety position / 是否在安全位置 | 表示机器人是否已到达安全位置 |
| `HasAlarm` | `bool` | Whether an alarm is active / 是否有报警 | 表示控制器是否有活跃的报警信息 |
| `SimulationMode` | `bool` | Whether in simulation mode / 是否处于仿真模式 | 表示控制器是否运行在仿真模式下 |
| `EmergencyStopPressed` | `bool` | Whether the E-stop is pressed / 急停按钮是否按下 | 表示急停按钮是否被按下 |
| `RescueMode` | `bool` | Whether in rescue mode / 是否处于救援模式 | 表示机器人是否处于碰撞救援模式 |
| `AutoMode` | `bool` | Whether in auto mode / 是否处于自动模式 | 表示控制器是否处于自动运行模式 |
| `RemoteMode` | `bool` | Whether in remote mode / 是否处于远程模式 | 表示控制器是否处于远程控制模式 |
| `RealTimeControlMode` | `bool` | Whether in real-time control mode / 是否处于实时控制模式 | 表示控制器是否处于实时控制模式 |
| `CriErrorCode` | `byte` | CRI error code / CRI 错误码 | CRI 协议层的错误代码 |

#### Joint Data / 关节数据

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `JointPosition` | `double[6]` | Joint positions in degrees / 关节位置（度） | 各关节的当前角度，单位为度 |
| `JointVelocity` | `double[6]` | Joint velocities / 关节速度 | 各关节的当前角速度 |
| `JointOutputTorque` | `double[6]` | Joint output torques / 关节输出力矩 | 各关节的当前输出力矩百分比 |
| `JointExternalForce` | `double[6]` | Joint external forces / 关节外部力 | 各关节检测到的外部力 |

#### TCP Data / TCP 数据

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `TcpPose` | `double[6]` | TCP pose (mm + deg) / TCP 位姿（毫米+度） | 工具中心点位姿，XYZ 单位 mm，ABC 单位度 |
| `TcpVelocity` | `double[6]` | TCP velocity / TCP 速度 | 工具中心点的六维速度 |
| `TcpLinearVelocity` | `double` | TCP linear velocity magnitude / TCP 线速度大小 | 工具中心点的线速度标量值 |

#### External / 外部轴

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `ExternalAxisPosition` | `double[]` | External axis positions / 外部轴位置 | 外部轴（如导轨、转台）的位置数组 |

### Methods / 方法

| Method | Return Type | Description (EN) | Description (CN) |
|--------|-------------|-------------------|-------------------|
| `UpdateFrom(CriRealTimeData)` | `void` | Copies all fields from another instance / 从另一个实例复制所有字段 | 用另一个 `CriRealTimeData` 实例的值更新当前实例的所有字段 |
| `Clone()` | `CriRealTimeData` | Creates a deep copy / 创建深拷贝 | 创建当前实例的深拷贝副本 |

### Example: Subscribing to CRI data / 示例：订阅 CRI 数据并读取关节位置

Subscribing to real-time data updates and reading joint positions.

订阅实时数据更新并读取关节位置。

```csharp
// Subscribe to real-time data / 订阅实时数据
client.OnCriDataReceived += (sender, data) =>
{
    // Read timestamp / 读取时间戳
    long timestamp = data.TimestampMs;

    // Read joint positions (in degrees) / 读取关节位置（单位：度）
    double joint1 = data.JointPosition[0];
    double joint2 = data.JointPosition[1];
    double joint3 = data.JointPosition[2];

    Console.WriteLine($"J1={joint1:F2}, J2={joint2:F2}, J3={joint3:F2}");

    // Read TCP pose / 读取 TCP 位姿
    double tcpX = data.TcpPose[0]; // mm
    double tcpY = data.TcpPose[1]; // mm
    double tcpZ = data.TcpPose[2]; // mm

    Console.WriteLine($"TCP X={tcpX:F2} Y={tcpY:F2} Z={tcpZ:F2}");

    // Check status / 检查状态
    if (data.HasAlarm)
    {
        Console.WriteLine("Robot has an alarm! / 机器人有报警!");
    }
};

// Or clone for thread-safe access / 或者克隆以实现线程安全访问
CriRealTimeData snapshot = data.Clone();
```

---

## 3. RobotFrame

**Robot Frame / 机器人坐标系**

A sealed class representing a coordinate frame definition, used for both tool frames and user coordinate frames. Contains an ID, a 6-axis pose (position + orientation), and optional tool-specific fields (name, offsets).

一个密封类，表示坐标系定义，用于工具坐标系和用户坐标系。包含 ID、六轴位姿（位置+姿态），以及工具帧可选字段（名称、偏置）。

### Properties / 属性

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `Id` | `int` | Frame identifier / 坐标系标识符 | 坐标系的唯一编号 |
| `X` | `double` | X position (mm) / X 位置（毫米） | X 轴方向的偏移量 |
| `Y` | `double` | Y position (mm) / Y 位置（毫米） | Y 轴方向的偏移量 |
| `Z` | `double` | Z position (mm) / Z 位置（毫米） | Z 轴方向的偏移量 |
| `A` | `double` | Rotation around X axis (deg) / 绕 X 轴旋转（度） | 绕 X 轴的旋转角度 |
| `B` | `double` | Rotation around Y axis (deg) / 绕 Y 轴旋转（度） | 绕 Y 轴的旋转角度 |
| `C` | `double` | Rotation around Z axis (deg) / 绕 Z 轴旋转（度） | 绕 Z 轴的旋转角度 |
| `Name` | `string?` | Tool/coordinate name (tool frames only, optional) / 工具/坐标系名称（仅工具帧，可选） | 工具帧名称，用户坐标系帧可留空 |
| `XOffset` | `double` | Tool offset X (mm, optional, tool frames only) / 工具偏置 X（毫米，可选，仅工具帧） | 沿工具坐标系的偏置 |
| `YOffset` | `double` | Tool offset Y (mm, optional, tool frames only) / 工具偏置 Y（毫米，可选，仅工具帧） | 沿工具坐标系的偏置 |
| `ZOffset` | `double` | Tool offset Z (mm, optional, tool frames only) / 工具偏置 Z（毫米，可选，仅工具帧） | 沿工具坐标系的偏置 |

### Example / 示例

```csharp
// Access a tool frame / 访问工具坐标系
RobotFrame tool = robotParams.Tool[0];
Console.WriteLine($"Tool {tool.Id}: X={tool.X}, Y={tool.Y}, Z={tool.Z}");
Console.WriteLine($"  A={tool.A}, B={tool.B}, C={tool.C}");
```

---

## 4. RobotPayloadFrame

**Robot Payload Frame / 机器人负载参数**

A sealed class representing a payload definition, including mass and center of mass coordinates.

一个密封类，表示负载定义，包括质量和质心坐标。

### Properties / 属性

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `Id` | `int` | Payload identifier / 负载标识符 | 负载配置的唯一编号 |
| `M` | `double` | Mass (kg) / 质量（千克） | 负载的质量 |
| `Mx` | `double` | Center of mass X (mm) / 质心 X（毫米） | 质心在 X 方向的偏移 |
| `My` | `double` | Center of mass Y (mm) / 质心 Y（毫米） | 质心在 Y 方向的偏移 |
| `Mz` | `double` | Center of mass Z (mm) / 质心 Z（毫米） | 质心在 Z 方向的偏移 |

### Example / 示例

```csharp
// Access a payload configuration / 访问负载配置
RobotPayloadFrame payload = robotParams.Payload[0];
Console.WriteLine($"Payload {payload.Id}: Mass={payload.M}kg");
Console.WriteLine($"  CoM: ({payload.Mx}, {payload.My}, {payload.Mz})");
```

---

## 5. RobotParameters

**Robot Parameters / 机器人参数**

A sealed class containing the complete set of robot parameters, including default IDs for tool, payload, and coordinate frames, as well as the full lists of configured frames.

一个密封类，包含机器人的完整参数集，包括工具、负载和坐标系的默认 ID，以及所有已配置的坐标系列表。

### Properties / 属性

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `DefaultToolId` | `int` | Default tool frame ID / 默认工具坐标系 ID | 当前激活的工具坐标系编号 |
| `DefaultPayloadId` | `int` | Default payload ID / 默认负载 ID | 当前激活的负载配置编号 |
| `DefaultCoordinateId` | `int` | Default coordinate frame ID / 默认坐标系 ID | 当前激活的用户坐标系编号 |
| `MaxPayload` | `double` | Maximum payload (kg) / 最大负载（千克） | 机器人允许的最大负载质量 |
| `Tool` | `List<RobotFrame>` | Tool frames list / 工具坐标系列表 | 所有已配置的工具坐标系 |
| `Payload` | `List<RobotPayloadFrame>` | Payload configurations list / 负载配置列表 | 所有已配置的负载参数 |
| `Coordinate` | `List<RobotFrame>` | User coordinate frames list / 用户坐标系列表 | 所有已配置的用户坐标系 |

### Example: Reading robot parameters / 示例：读取机器人参数

Reading and inspecting robot parameters including tools, payloads, and coordinate frames.

读并检查机器人参数，包括工具、负载和坐标系。

```csharp
RobotParameters parameters = await client.GetRobotParametersAsync();

// Read defaults / 读取默认值
Console.WriteLine($"Default Tool ID / 默认工具 ID: {parameters.DefaultToolId}");
Console.WriteLine($"Default Payload ID / 默认负载 ID: {parameters.DefaultPayloadId}");
Console.WriteLine($"Default Coordinate ID / 默认坐标系 ID: {parameters.DefaultCoordinateId}");
Console.WriteLine($"Max Payload / 最大负载: {parameters.MaxPayload} kg");

// Iterate tool frames / 遍历工具坐标系
Console.WriteLine("Tool Frames / 工具坐标系:");
foreach (RobotFrame tool in parameters.Tool)
{
    Console.WriteLine($"  [{tool.Id}] X={tool.X}, Y={tool.Y}, Z={tool.Z}, "
                    + $"A={tool.A}, B={tool.B}, C={tool.C}");
}

// Iterate payloads / 遍历负载配置
Console.WriteLine("Payloads / 负载配置:");
foreach (RobotPayloadFrame payload in parameters.Payload)
{
    Console.WriteLine($"  [{payload.Id}] M={payload.M}kg, "
                    + $"CoM=({payload.Mx}, {payload.My}, {payload.Mz})");
}

// Iterate coordinate frames / 遍历坐标系
Console.WriteLine("Coordinate Frames / 用户坐标系:");
foreach (RobotFrame coord in parameters.Coordinate)
{
    Console.WriteLine($"  [{coord.Id}] X={coord.X}, Y={coord.Y}, Z={coord.Z}, "
                    + $"A={coord.A}, B={coord.B}, C={coord.C}");
}
```

---

## 6. RegisterReadValue

**Register Read Value / 寄存器读取值**

A readonly struct representing a value read from a controller register, with helpers to convert the value to common types.

一个只读结构体，表示从控制器寄存器读取的值，提供将值转换为常见类型的辅助方法。

### Properties / 属性

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `Address` | `int` | Register address / 寄存器地址 | 读取的寄存器地址编号 |
| `Value` | `JsonElement` | Raw value as JSON / 原始 JSON 值 | 寄存器的原始值，以 JSON 元素形式存储 |

### Methods / 方法

| Method | Return Type | Description (EN) | Description (CN) |
|--------|-------------|-------------------|-------------------|
| `GetInt32()` | `int` | Converts value to Int32 / 将值转换为 Int32 | 直接将寄存器值转换为 32 位整数，转换失败时抛出异常 |
| `GetDouble()` | `double` | Converts value to Double / 将值转换为 Double | 直接将寄存器值转换为双精度浮点数 |
| `TryGetInt32(out int)` | `bool` | Safely tries to convert to Int32 / 安全尝试转换为 Int32 | 安全尝试转换，失败时返回 `false` 而不抛出异常 |

### Example: Reading and converting register values / 示例：读取和转换寄存器值

```csharp
// Read registers / 读取寄存器
List<RegisterReadValue> values = await client.ReadRegistersAsync(address: 0, count: 5);

foreach (RegisterReadValue reg in values)
{
    Console.WriteLine($"Register [{reg.Address}] raw: {reg.Value}");

    // Direct conversion (throws on failure) / 直接转换（失败时抛出异常）
    int intVal = reg.GetInt32();
    Console.WriteLine($"  As Int32: {intVal}");

    // Safe conversion / 安全转换
    if (reg.TryGetInt32(out int safeVal))
    {
        Console.WriteLine($"  Safe Int32: {safeVal}");
    }
    else
    {
        Console.WriteLine("  Cannot convert to Int32 / 无法转换为 Int32");
    }

    // As double / 转换为 double
    double dblVal = reg.GetDouble();
    Console.WriteLine($"  As Double: {dblVal}");
}
```

---

## 7. RegisterExtendArrayValueType

**Register Extended Array Value Type / 寄存器扩展数组值类型**

A static class defining constants for the data types used in extended register arrays.

一个静态类，定义扩展寄存器数组中使用的数据类型常量。

### Constants / 常量

| Constant | Value | Description (EN) | Description (CN) |
|----------|-------|-------------------|-------------------|
| `Bool` | `0` | Boolean type / 布尔类型 | 布尔值，`true` 或 `false` |
| `UInt8` | `1` | Unsigned 8-bit integer / 无符号 8 位整数 | 范围 0 ~ 255 |
| `Int8` | `2` | Signed 8-bit integer / 有符号 8 位整数 | 范围 -128 ~ 127 |
| `UInt16` | `3` | Unsigned 16-bit integer / 无符号 16 位整数 | 范围 0 ~ 65535 |
| `Int16` | `4` | Signed 16-bit integer / 有符号 16 位整数 | 范围 -32768 ~ 32767 |
| `UInt32` | `5` | Unsigned 32-bit integer / 无符号 32 位整数 | 范围 0 ~ 4294967295 |
| `Int32` | `6` | Signed 32-bit integer / 有符号 32 位整数 | 范围 -2147483648 ~ 2147483647 |
| `Float32` | `7` | 32-bit floating point / 32 位浮点数 | 单精度浮点数 |

### Example / 示例

```csharp
// Specify value type when writing extended registers / 写入扩展寄存器时指定值类型
await client.WriteExtendRegisterAsync(
    address: 0,
    values: new[] { 3.14f },
    valueType: RegisterExtendArrayValueType.Float32
);
```

---

## 8. IoPortKind

**I/O Port Kind / I/O 端口类型**

A static class defining constants for the different kinds of I/O ports available on the controller.

一个静态类，定义控制器上可用的不同 I/O 端口类型常量。

### Constants / 常量

| Constant | Value | Description (EN) | Description (CN) |
|----------|-------|-------------------|-------------------|
| `Di` | `"DI"` | Digital Input / 数字输入 | 数字输入端口，用于读取开关量信号 |
| `Do` | `"DO"` | Digital Output / 数字输出 | 数字输出端口，用于控制开关量信号 |
| `Ai` | `"AI"` | Analog Input / 模拟输入 | 模拟输入端口，用于读取连续量信号 |
| `Ao` | `"AO"` | Analog Output / 模拟输出 | 模拟输出端口，用于输出连续量信号 |

### Example / 示例

```csharp
// Read a digital input / 读取数字输入
bool diValue = await client.ReadDigitalInputAsync(port: IoPortKind.Di, address: 0);

// Write a digital output / 写入数字输出
await client.WriteDigitalOutputAsync(port: IoPortKind.Do, address: 0, value: true);

// Read an analog input / 读取模拟输入
double aiValue = await client.ReadAnalogInputAsync(port: IoPortKind.Ai, address: 0);
```

---

## 9. RelativePoseCoorType

**Relative Pose Coordinate Type / 相对位姿坐标类型**

An enum specifying the coordinate system in which a relative pose is expressed.

一个枚举，指定相对位姿所表达的坐标系。

### Values / 值

| Name | Value | Description (EN) | Description (CN) |
|------|-------|-------------------|-------------------|
| `User` | `0` | User (world) coordinate system / 用户（世界）坐标系 | 相对位姿在用户坐标系下表达 |
| `Tool` | `1` | Tool coordinate system / 工具坐标系 | 相对位姿在当前工具坐标系下表达 |

### Example / 示例

```csharp
// Move relative in tool coordinate system / 在工具坐标系下做相对运动
await client.MoveRelativeAsync(
    pose: new[] { 0, 0, 10, 0, 0, 0 }, // Move 10mm in Z / 沿 Z 移动 10mm
    coorType: RelativePoseCoorType.Tool
);

// Move relative in user coordinate system / 在用户坐标系下做相对运动
await client.MoveRelativeAsync(
    pose: new[] { 10, 0, 0, 0, 0, 0 }, // Move 10mm in X / 沿 X 移动 10mm
    coorType: RelativePoseCoorType.User
);
```

---

## 10. CodroidCommandException

**Codroid Command Exception / Codroid 命令异常**

A sealed exception class thrown when a CRI command fails. Provides detailed context about the failure including the request ID, command type, controller error message, and the full response.

当 CRI 命令失败时抛出的密封异常类。提供关于失败的详细上下文，包括请求 ID、命令类型、控制器错误消息和完整响应。

### Inheritance / 继承

```
System.Exception
  └── CodroidCommandException
```

### Properties / 属性

| Property | Type | Description (EN) | Description (CN) |
|----------|------|-------------------|-------------------|
| `RequestId` | `int` | The ID of the failed request / 失败请求的 ID | 用于匹配请求与响应的标识符 |
| `CommandType` | `string` | The type of command that failed / 失败的命令类型 | 字符串标识符，表示哪个 CRI 命令失败 |
| `ControllerError` | `string?` | Error from the controller / 来自控制器的错误 | 控制器返回的错误描述，可能为 `null` |
| `Response` | `CommonResponse?` | The full response object / 完整的响应对象 | 包含原始响应数据，可用于进一步诊断 |

### Constructor / 构造函数

```csharp
public CodroidCommandException(
    int requestId,
    string commandType,
    string? controllerError,
    CommonResponse? response
)
```

### Example: Catching and inspecting / 示例：捕获和检查异常

Catching a `CodroidCommandException` and inspecting its properties for diagnostics.

捕获 `CodroidCommandException` 并检查其属性以进行诊断。

```csharp
try
{
    await client.MoveJointAsync(target: new[] { 0, 0, 90, 0, 90, 0 });
}
catch (CodroidCommandException ex)
{
    Console.WriteLine("Command failed! / 命令失败!");
    Console.WriteLine($"  Request ID / 请求 ID: {ex.RequestId}");
    Console.WriteLine($"  Command Type / 命令类型: {ex.CommandType}");
    Console.WriteLine($"  Controller Error / 控制器错误: {ex.ControllerError}");

    if (ex.Response != null)
    {
        Console.WriteLine($"  Response error / 响应错误: {ex.Response.err}");
        Console.WriteLine($"  Response data / 响应数据: {ex.Response.db.GetRawText()}");
    }

    // Re-throw or handle / 重新抛出或处理
    throw;
}
```

---

## 11. GlobalVarSaveItem

**Global Variable Save Item / 全局变量保存项**

A readonly record struct used to specify a global variable to be saved or written to the controller, including its name, value, and an optional remark.

一个只读记录结构体，用于指定要保存或写入控制器的全局变量，包括其名称、值和可选备注。

### Constructor / 构造函数

```csharp
public GlobalVarSaveItem(string Name, object Value, string? Remark = null)
```

| Parameter | Type | Required | Description (EN) | Description (CN) |
|-----------|------|----------|-------------------|-------------------|
| `Name` | `string` | Yes | Variable name / 变量名称 | 全局变量的标识名称 |
| `Value` | `object` | Yes | Variable value / 变量值 | 变量的值，支持多种类型 |
| `Remark` | `string?` | No (default: `null`) | Optional remark / 可选备注 | 变量的描述或注释信息 |

### Example / 示例

```csharp
// Create items to save / 创建要保存的项
var items = new List<GlobalVarSaveItem>
{
    new GlobalVarSaveItem("Counter", 42, "Production count / 生产计数"),
    new GlobalVarSaveItem("Speed", 50.5, "Motion speed / 运动速度"),
    new GlobalVarSaveItem("Flag", true)
};

await client.SaveGlobalVarsAsync(items);
```

---

## 12. GlobalVarRawJson

**Global Variable Raw JSON / 全局变量原始 JSON**

A readonly record struct that wraps a raw JSON string literal for writing global variables with complex or custom JSON structures.

一个只读记录结构体，封装原始 JSON 字符串字面量，用于写入具有复杂或自定义 JSON 结构的全局变量。

### Constructor / 构造函数

```csharp
public GlobalVarRawJson(string Literal)
```

| Parameter | Type | Required | Description (EN) | Description (CN) |
|-----------|------|----------|-------------------|-------------------|
| `Literal` | `string` | Yes | Raw JSON string / 原始 JSON 字符串 | 直接传递给控制器的 JSON 字面量 |

### Example / 示例

```csharp
// Write a complex variable using raw JSON / 使用原始 JSON 写入复杂变量
var rawJson = new GlobalVarRawJson(
    """
    {"positions": [1.0, 2.0, 3.0], "enabled": true}
    """
);

await client.WriteGlobalVarRawAsync("MyConfig", rawJson);
```

---

## 13. GlobalVarCatalogEntry

**Global Variable Catalog Entry / 全局变量目录项**

A sealed class representing a single entry in the global variable catalog, containing the variable's current value and optional remark.

一个密封类，表示全局变量目录中的单个条目，包含变量的当前值和可选备注。

### Properties / 属性

| Property | Type | Default | Description (EN) | Description (CN) |
|----------|------|---------|-------------------|-------------------|
| `Value` | `JsonElement` | -- | The variable's current value / 变量的当前值 | 变量当前存储的值，以 JSON 元素形式表示 |
| `Remark` | `string` | `""` | Remark or description / 备注或描述 | 变量的描述信息，默认为空字符串 |

### Example / 示例

```csharp
// Read the global variable catalog / 读取全局变量目录
Dictionary<string, GlobalVarCatalogEntry> catalog =
    await client.GetGlobalVarCatalogAsync();

foreach (var (name, entry) in catalog)
{
    Console.WriteLine($"Variable / 变量: {name}");
    Console.WriteLine($"  Value / 值: {entry.Value.GetRawText()}");
    Console.WriteLine($"  Remark / 备注: {entry.Remark}");
}
```
