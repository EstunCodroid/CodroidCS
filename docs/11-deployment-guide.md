# SDK 部署指南 / Deployment Guide

## 概述

本文档介绍如何将 CodroidCS SDK 部署到其他电脑上使用。

---

## 一、环境要求

### 目标电脑运行时要求

| 目标框架 | 需安装的运行时 | 适用场景 |
|---|---|---|
| net8.0 | [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) | 新项目，Windows/Linux 均可 |
| net6.0 | [.NET 6 Runtime](https://dotnet.microsoft.com/download/dotnet/6.0) | 已有 .NET 6 项目兼容 |
| net462 | .NET Framework 4.6.2+（Windows 自带） | 老旧工控机、上位机 |

### 检查目标电脑已安装的 .NET 版本

```bash
# 现代版 .NET（6/8）
dotnet --list-runtimes

# .NET Framework（老框架）
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
```

Release 值对照：

| Release 值 | .NET Framework 版本 |
|---|---|
| 528040 | 4.8 |
| 533320 | 4.8.1 |

---

## 二、编译 SDK

在开发机上编译全部目标框架的 DLL：

```bash
cd D:\C#proj\CodroidCS-main

# 编译 SDK（同时输出 net6.0 / net8.0 / net462 + NuGet 包）
dotnet build CodroidSDK\CodroidCS.csproj -c Release
```

### 编译输出

| 目标框架 | DLL 路径 | 依赖文件 |
|---|---|---|
| net8.0 | `CodroidSDK\bin\Release\net8.0\CodroidCS.dll` | 无（框架内置） |
| net6.0 | `CodroidSDK\bin\Release\net6.0\CodroidCS.dll` | 无（框架内置） |
| net462 | `CodroidSDK\bin\Release\net462\CodroidCS.dll` | 需附带 System.*.dll（见下表） |
| NuGet 包 | `CodroidSDK\bin\Release\Codroidsdk.2.1.11.nupkg` | — |

### net462 依赖文件清单

net462 目标需要额外复制以下 DLL（位于 `CodroidSDK\bin\Release\net462\`）：

| DLL | 用途 |
|---|---|
| System.Text.Json.dll | JSON 序列化/反序列化 |
| System.Text.Encodings.Web.dll | URL/HTML 编码（System.Text.Json 依赖） |
| System.Memory.dll | Span\<T\> / Memory\<T\> 支持 |
| System.Buffers.dll | ArrayPool\<T\> 支持 |
| System.Runtime.CompilerServices.Unsafe.dll | 不安全内存操作 |
| System.Threading.Tasks.Extensions.dll | ValueTask\<T\> 支持 |
| System.ValueTuple.dll | C# 元组语法支持 |
| System.Numerics.Vectors.dll | 矩阵/向量运算 |
| Microsoft.Bcl.AsyncInterfaces.dll | IAsyncEnumerable\<T\> 支持 |

> net8.0 / net6.0 不需要这些文件，框架已内置。

---

## 三、部署方式

### 方式 1：直接复制 DLL

最简单，适合快速测试和临时使用。

**步骤：**

1. 根据目标电脑的 .NET 版本，复制对应文件夹下的 `CodroidCS.dll`
2. net462 额外复制上述 System.*.dll 依赖文件
3. 在目标项目中添加 DLL 引用

**目标项目 csproj 配置：**

```xml
<ItemGroup>
  <!-- net8.0 / net6.0 -->
  <Reference Include="CodroidCS">
    <HintPath>libs\CodroidCS.dll</HintPath>
  </Reference>
</ItemGroup>
```

```xml
<ItemGroup>
  <!-- net462：需逐个引用所有 DLL -->
  <Reference Include="CodroidCS">
    <HintPath>libs\CodroidCS.dll</HintPath>
  </Reference>
  <Reference Include="System.Text.Json">
    <HintPath>libs\System.Text.Json.dll</HintPath>
  </Reference>
  <!-- ... 其他 System.*.dll 同理 ... -->
</ItemGroup>
```

### 方式 2：通过 NuGet 包（推荐）

适合正式项目和多人协作，便于版本管理。

**步骤：**

1. 复制 `Codroidsdk.2.1.11.nupkg` 到目标电脑
2. 在目标项目中安装包

**安装方式 2a：本地 NuGet 源**

```bash
# 创建本地 NuGet 源目录并复制 .nupkg
mkdir D:\local-nuget
copy Codroidsdk.2.1.11.nupkg D:\local-nuget\

# 添加本地源
dotnet nuget add source D:\local-nuget -n LocalSource

# 安装包
dotnet add package Codroidsdk --version 2.1.11
```

**安装方式 2b：csproj 中直接引用**

```xml
<ItemGroup>
  <PackageReference Include="Codroidsdk" Version="2.1.11" />
</ItemGroup>
```

然后还原：

```bash
dotnet restore --source D:\local-nuget
```

**安装方式 2c：VS 中操作**

1. 工具 → NuGet 包管理器 → 包管理器设置 → 包源 → 添加本地源
2. 右键项目 → 管理 NuGet 包 → 源选择本地 → 搜索 Codroidsdk → 安装

---

## 四、场景选择

| 场景 | 推荐方式 | 目标框架 |
|---|---|---|
| 新项目开发 | NuGet 包 | net8.0 |
| 正式项目部署 | NuGet 包 | net8.0 |
| 快速测试 | 复制 DLL | net8.0 / net6.0 |
| 老旧工控机（无 .NET 8） | 复制 DLL | net462 |
| WinForm / WPF 老项目 | 复制 DLL 或 NuGet | net462 |
| Linux 部署 | NuGet 包 | net8.0 |

---

## 五、验证部署

在目标电脑上创建一个简单控制台项目验证：

```bash
# 创建项目
dotnet new console -n CodroidTest
cd CodroidTest

# 安装 SDK（方式 2a）
dotnet add package Codroidsdk --version 2.1.11
```

编辑 `Program.cs`：

```csharp
using Codroid;

var robot = new CodroidClient("192.168.1.136");
await robot.Connect();
Console.WriteLine("连接成功");

// 读取当前 TCP 位姿
await robot.StartCriDataPush("192.168.1.150", 18888);
await Task.Delay(500); // 等待首帧
var data = robot.CriData;
Console.WriteLine($"TCP: [{string.Join(", ", data.TcpPose)}]");
Console.WriteLine($"关节: [{string.Join(", ", data.JointPosition)}]");
await robot.StopCriDataPush("192.168.1.150", 18888);

robot.Disconnect();
Console.WriteLine("断开连接");
```

运行验证：

```bash
dotnet run
```

如果输出位姿数据，说明部署成功。

---

## 六、常见问题

### Q: 运行时报 "找不到 CodroidCS"？

- net462：检查是否遗漏了 System.*.dll 依赖文件
- net8.0/net6.0：确认 `CodroidCS.dll` 与项目在同一目录或 HintPath 正确

### Q: net462 运行时报 "找不到 System.Text.Json"？

需将 `System.Text.Json.dll` 及其依赖 DLL 全部复制到输出目录。

### Q: 如何更新 SDK 版本？

1. 在开发机修改代码并重新编译
2. 复制新的 `CodroidCS.dll`（或新的 `.nupkg`）覆盖旧文件
3. 重新编译目标项目

### Q: 如何确认目标电脑是否支持 net8.0？

```bash
dotnet --list-runtimes
```

如果列表中包含 `Microsoft.NETCore.App 8.x.x`，则支持 net8.0。如果不支持，安装 [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 或改用 net462。
