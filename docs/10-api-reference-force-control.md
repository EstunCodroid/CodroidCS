# C# 力控接口说明

当前 C# SDK 与 Python 力控接口对齐，目标框架覆盖 `net462`、`net6.0`、`net8.0`。

## 初始化与校准

- `ZeroForceCalibration(int calibrationTimeMs = 1000)`：零力校准 / 带载去皮。
- `InitForceControl(...)`：初始化力控。SDK 内部固定 `algo=1`（导纳），当前不允许调用方传入算法参数。
- `StartForceControl()` / `StopForceControl(int smoothTimeMs = 500)`：启动 / 停止力控。

`FTSensorDriftCalibration` 已废弃并移除。

## 在线参数与安全

- `TuneForceParams(...)`：在线更新刚度、阻尼、质量、期望力、力限制、坐标系和 `rampTime`。
- `StartContactDetection(...)`：接触检测。
- `SetOverforceProtection(...)`：过力保护。
- `SetForceDataHealth(...)`：力数据健康监控。

## 状态读取

`GetForceState()` 返回 `ForceControlState`，字段包括：

- `Enabled`、`Pending`、`Valid`、`IsContact`、`IsOverforce`：`bool`
- `Algo`、`Health`：`int`
- `WrenchTcp`、`WrenchBase`、`DesiredWrench`、`TrackError`：`double[]`
- `AxisMode`：`int[]`

也可以使用单字段 getter，例如：

- `GetForceStateEnabled()` 返回 `bool`
- `GetForceStateWrenchTcp()` 返回 `double[]`
- `GetForceStateAxisMode()` 返回 `int[]`

## 测试示例

见 `examples/ForceControlTest/`：

```bash
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net8.0 -- 192.168.1.136 state
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net6.0 -- 192.168.1.136 constant
```

`net462` 目标用于 Windows .NET Framework 4.6.2+。
