using System;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace Codroid
{

/// <summary>
/// 控制器通过 TCP 返回的通用 JSON 响应结构（字段名与协议一致：id、ty、db、err）。
/// </summary>
public class CommonResponse
{
    /// <summary>
    /// 与请求对应的报文序号（协议字段 id）。
    /// </summary>
    public object? id { get; set; }
    /// <summary>
    /// 响应类型或路由标识（协议字段 ty）。
    /// </summary>
    public string? ty { get; set; }
    /// <summary>
    /// 业务数据载荷；结构随接口变化，使用 <see cref="JsonElement"/> 便于按需读取。
    /// </summary>
    public JsonElement db { get; set; }
    /// <summary>
    /// 非空时表示控制器报告的错误信息；此时调用方应将本次调用视为失败。
    /// </summary>
    public string? err { get; set; }
}

/// <summary>力控算法枚举；当前 InitForceControl 固定使用 Admittance。</summary>
public enum ForceControlAlgo
{
    /// <summary>阻抗控制。</summary>
    Impedance = 0,
    /// <summary>导纳控制；当前 InitForceControl 固定使用该值。</summary>
    Admittance = 1,
    /// <summary>PD 力控。</summary>
    PdForce = 2
}

/// <summary>力控坐标系。</summary>
public enum ForceFrame
{
    /// <summary>TCP 坐标系。</summary>
    Tcp = 0,
    /// <summary>用户坐标系。</summary>
    User = 1,
    /// <summary>世界坐标系。</summary>
    World = 2
}

/// <summary>力控逐轴模式。</summary>
public enum ForceAxisMode
{
    /// <summary>位置模式。</summary>
    Position = 0,
    /// <summary>力模式。</summary>
    Force = 1,
    /// <summary>柔顺模式。</summary>
    Compliant = 2
}

/// <summary>力控数据健康状态。</summary>
public enum ForceHealth
{
    /// <summary>数据正常。</summary>
    Ok = 0,
    /// <summary>数据无效。</summary>
    Invalid = 1,
    /// <summary>数据超时。</summary>
    Timeout = 2,
    /// <summary>力或力矩饱和。</summary>
    Saturated = 3,
    /// <summary>数据包丢失超限。</summary>
    PacketLoss = 4
}

/// <summary>Robot/getForceState 返回的力控状态快照。</summary>
public class ForceControlState
{
    /// <summary>力控是否已启用。</summary>
    public bool Enabled { get; set; }
    /// <summary>力控是否处于待完成状态。</summary>
    public bool Pending { get; set; }
    /// <summary>当前力控算法编号。</summary>
    public int Algo { get; set; }
    /// <summary>力数据是否有效。</summary>
    public bool Valid { get; set; }
    /// <summary>是否检测到接触。</summary>
    public bool IsContact { get; set; }
    /// <summary>是否触发过力保护。</summary>
    public bool IsOverforce { get; set; }
    /// <summary>力数据健康状态编号。</summary>
    public int Health { get; set; }
    /// <summary>TCP 坐标系下的六维力/力矩。</summary>
    public double[] WrenchTcp { get; set; } = new double[6];
    /// <summary>基坐标系下的六维力/力矩。</summary>
    public double[] WrenchBase { get; set; } = new double[6];
    /// <summary>期望六维力/力矩。</summary>
    public double[] DesiredWrench { get; set; } = new double[6];
    /// <summary>力跟踪误差。</summary>
    public double[] TrackError { get; set; } = new double[6];
    /// <summary>六个轴的力控模式。</summary>
    public int[] AxisMode { get; set; } = new int[6];
}


/// <summary>
/// CRI 实时数据快照：由 UDP 二进制包经 <see cref="CriRealtimePacketParser"/> 解析得到；
/// 控制器线上数据为弧度与米，解析后本类型对外字段为关节类量度、线位移毫米等（详见各属性说明）。
/// </summary>
public class CriRealTimeData
{
    /// <summary>
    /// 时间戳（毫秒，含义以控制器协议为准）。
    /// </summary>
    public long TimestampMs { get; set; }
    /// <summary>
    /// 状态字 1 的原始 16 位值（解析后见各类布尔状态属性）。
    /// </summary>
    public ushort Status1Raw { get; set; }
    /// <summary>
    /// 状态字 2 的原始 16 位值（含实时控制模式位与错误码高字节）。
    /// </summary>
    public ushort Status2Raw { get; set; }

    /// <summary>工程正在运行。</summary>
    public bool ProjectRunning { get; set; }
    /// <summary>工程已停止。</summary>
    public bool ProjectStopped { get; set; }
    /// <summary>工程已暂停。</summary>
    public bool ProjectPaused { get; set; }
    /// <summary>正在使能过程中。</summary>
    public bool Enabling { get; set; }
    /// <summary>未处于使能就绪状态。</summary>
    public bool NotEnabled { get; set; }
    /// <summary>手动模式。</summary>
    public bool ManualMode { get; set; }
    /// <summary>拖拽（示教）相关状态有效。</summary>
    public bool Dragging { get; set; }
    /// <summary>机构运动中。</summary>
    public bool InMotion { get; set; }

    /// <summary>碰撞检测导致停止。</summary>
    public bool CollisionStopped { get; set; }
    /// <summary>处于安全位置。</summary>
    public bool InSafetyPosition { get; set; }
    /// <summary>存在报警。</summary>
    public bool HasAlarm { get; set; }
    /// <summary>仿真模式。</summary>
    public bool SimulationMode { get; set; }
    /// <summary>急停已按下。</summary>
    public bool EmergencyStopPressed { get; set; }
    /// <summary>救援模式。</summary>
    public bool RescueMode { get; set; }
    /// <summary>自动模式。</summary>
    public bool AutoMode { get; set; }
    /// <summary>远程模式。</summary>
    public bool RemoteMode { get; set; }

    /// <summary>CRI 错误码（取自状态字 2 的高 8 位）。</summary>
    public byte CriErrorCode { get; set; }
    /// <summary>实时控制模式有效。</summary>
    public bool RealTimeControlMode { get; set; }

    /// <summary>六轴关节位置（度）。</summary>
    public double[] JointPosition { get; set; } = new double[6];
    /// <summary>六轴关节角速度（度/秒）。</summary>
    public double[] JointVelocity { get; set; } = new double[6];
    /// <summary>TCP 位姿：前三维 x,y,z 为毫米；后三维 rx,ry,rz 为度。</summary>
    public double[] TcpPose { get; set; } = new double[6];
    /// <summary>TCP 速度：线速度分量为毫米/秒；角速度分量为度/秒。</summary>
    public double[] TcpVelocity { get; set; } = new double[6];
    /// <summary>TCP 线速度标量（毫米/秒）。</summary>
    public double TcpLinearVelocity { get; set; }
    /// <summary>关节输出力矩（控制器原始单位，由协议定义）。</summary>
    public double[] JointOutputTorque { get; set; } = new double[6];
    /// <summary>关节外力（控制器原始单位，由协议定义）。</summary>
    public double[] JointExternalForce { get; set; } = new double[6];
    /// <summary>附加轴位置；当前解析配置为六轴无附加轴时通常为空数组。</summary>
    public double[] ExternalAxisPosition { get; set; } = Array.Empty<double>();

    /// <summary>
    /// 用另一快照覆盖当前实例的所有字段（数组为深拷贝引用新数组）。
    /// </summary>
    /// <param name="source">作为数据源的实时数据对象，不可为 null。</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 null。</exception>
    public void UpdateFrom(CriRealTimeData source)
    {
        Polyfills.ThrowIfNull(source);
        TimestampMs = source.TimestampMs;
        Status1Raw = source.Status1Raw;
        Status2Raw = source.Status2Raw;
        ProjectRunning = source.ProjectRunning;
        ProjectStopped = source.ProjectStopped;
        ProjectPaused = source.ProjectPaused;
        Enabling = source.Enabling;
        NotEnabled = source.NotEnabled;
        ManualMode = source.ManualMode;
        Dragging = source.Dragging;
        InMotion = source.InMotion;
        CollisionStopped = source.CollisionStopped;
        InSafetyPosition = source.InSafetyPosition;
        HasAlarm = source.HasAlarm;
        SimulationMode = source.SimulationMode;
        EmergencyStopPressed = source.EmergencyStopPressed;
        RescueMode = source.RescueMode;
        AutoMode = source.AutoMode;
        RemoteMode = source.RemoteMode;
        CriErrorCode = source.CriErrorCode;
        RealTimeControlMode = source.RealTimeControlMode;
        JointPosition = (double[])source.JointPosition.Clone();
        JointVelocity = (double[])source.JointVelocity.Clone();
        TcpPose = (double[])source.TcpPose.Clone();
        TcpVelocity = (double[])source.TcpVelocity.Clone();
        TcpLinearVelocity = source.TcpLinearVelocity;
        JointOutputTorque = (double[])source.JointOutputTorque.Clone();
        JointExternalForce = (double[])source.JointExternalForce.Clone();
        ExternalAxisPosition = (double[])source.ExternalAxisPosition.Clone();
    }

    /// <summary>
    /// 创建当前数据的深拷贝，便于在回调或跨线程传递时避免共享可变数组。
    /// </summary>
    /// <returns>与当前字段值一致的新 <see cref="CriRealTimeData"/> 实例。</returns>
    public CriRealTimeData Clone()
    {
        return new CriRealTimeData
        {
            TimestampMs = TimestampMs,
            Status1Raw = Status1Raw,
            Status2Raw = Status2Raw,
            ProjectRunning = ProjectRunning,
            ProjectStopped = ProjectStopped,
            ProjectPaused = ProjectPaused,
            Enabling = Enabling,
            NotEnabled = NotEnabled,
            ManualMode = ManualMode,
            Dragging = Dragging,
            InMotion = InMotion,
            CollisionStopped = CollisionStopped,
            InSafetyPosition = InSafetyPosition,
            HasAlarm = HasAlarm,
            SimulationMode = SimulationMode,
            EmergencyStopPressed = EmergencyStopPressed,
            RescueMode = RescueMode,
            AutoMode = AutoMode,
            RemoteMode = RemoteMode,
            CriErrorCode = CriErrorCode,
            RealTimeControlMode = RealTimeControlMode,
            JointPosition = (double[])JointPosition.Clone(),
            JointVelocity = (double[])JointVelocity.Clone(),
            TcpPose = (double[])TcpPose.Clone(),
            TcpVelocity = (double[])TcpVelocity.Clone(),
            TcpLinearVelocity = TcpLinearVelocity,
            JointOutputTorque = (double[])JointOutputTorque.Clone(),
            JointExternalForce = (double[])JointExternalForce.Clone(),
            ExternalAxisPosition = (double[])ExternalAxisPosition.Clone()
        };
    }

}
}
