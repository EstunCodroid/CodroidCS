using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace Codroid
{
    /// <summary>
    /// Codroid 机器人控制器客户端：通过 TCP（默认端口 9001）发送 JSON 指令，并可接收 CRI 实时数据 UDP 推送。
    /// </summary>
    public class CodroidClient
    {
        private string _ip;
        private int _port;
        private FutureTcpClient _TcpClient;
        private int _id;
        private readonly object _criDataLock = new();
        private readonly CriRealTimeData _criData = new();
        private long _lastCriReceivedUtcTicks;
        private UdpClient? _criUdpClient;
        private CancellationTokenSource? _criUdpCts;
        private Task? _criUdpTask;

        private const ushort CriMaskFixed = 0xFFFF;
        private const bool CriHighPrecisionFixed = true;
        private const int CriDurationMsFixed = 100;
        private const int CriControlDurationMinMs = 1;
        private const int CriControlDurationMaxMs = 16;
        private const int CriControlStartBufferMin = 1;
        private const int CriControlStartBufferMax = 100;
        private const int CriControlFilterTypeRecommended = 1;
        private const int CriControlDurationRecommendedMs = 4;
        private const int CriControlStartBufferRecommended = 5;

        /// <summary>
        /// 每收到一帧合法长度的 CRI UDP 包并完成解析后触发；参数为当前数据的线程安全快照（克隆）。
        /// </summary>
        public event Action<CriRealTimeData>? CriDataReceived;

        /// <summary>
        /// 使用指定控制器地址构造客户端；TCP 端口固定为 9001。
        /// </summary>
        /// <param name="ip">控制器的 IPv4 地址字符串（也用于 CRI UDP 本地绑定解析）。</param>
        public CodroidClient(string ip)
        {
            _ip = ip;
            _port = 9001;
            _TcpClient = new FutureTcpClient();
            _id = 0;
        }

        /// <summary>
        /// 生成单调递增的请求序号，用于匹配 JSON 响应。
        /// </summary>
        /// <returns>新的正整数 id。</returns>
        private int NextId() => Interlocked.Increment(ref _id);

        /// <summary>
        /// 线程安全地读取当前 CRI 实时数据的克隆副本；适合短时取用，避免长期持有内部缓冲区引用。
        /// </summary>
        /// <value>当前实时数据的深拷贝。</value>
        public CriRealTimeData CriData
        {
            get
            {
                lock (_criDataLock)
                {
                    return _criData.Clone();
                }
            }
        }

        /// <summary>
        /// 后台 UDP 接收线程持续更新的同一实例；直接读取需注意并发，必要时结合锁或仅用只读快照属性 <see cref="CriData"/>。
        /// </summary>
        /// <value>内部实时数据对象的直接引用。</value>
        [Obsolete("不保证线程安全，可能读到半更新数据。请使用 CriData（返回深拷贝）。", false)]
        public CriRealTimeData Data => _criData;

        /// <summary>
        /// 与控制器建立 TCP 连接（地址为构造时传入的 IP，端口 9001）。
        /// </summary>
        /// <returns>表示异步连接操作的任务。</returns>
        /// <exception cref="SocketException">TCP 连接失败，例如 IP 不可达、端口未开放或网络异常。</exception>
        /// <exception cref="ObjectDisposedException">底层 TCP 客户端已释放。</exception>
        public async Task Connect()
        {
            await _TcpClient.Connect(_ip, _port);
        }

        /// <summary>
        /// 建立 TCP 后：先 <see cref="EnterRemoteModeViaAuto"/>（自动→远程），再 <see cref="SwitchOn"/> 上电/使能。
        /// 仅建立连接请用 <see cref="Connect"/>。
        /// </summary>
        /// <returns>表示完整连接、切远程与上电流程完成的任务。</returns>
        /// <exception cref="SocketException">TCP 连接失败。</exception>
        /// <exception cref="InvalidOperationException">响应无法反序列化，或 TCP 状态异常。</exception>
        /// <exception cref="TimeoutException">某步指令等待超时。</exception>
        /// <exception cref="CodroidCommandException">控制器返回错误。</exception>
        public async Task ConnectRemoteAndSwitchOn()
        {
            await Connect();
            await EnterRemoteModeViaAuto();
            await SwitchOn();
        }

        /// <summary>
        /// 订阅 TCP 主题推送（协议 15.x）：<paramref name="topicTy"/> 与下行报文 <c>ty</c> 一致（如 <see cref="PublishTopics.RobotStatus"/>）。
        /// 首次在本连接上订阅该主题时发送 <c>ty</c>、<c>tc</c> 帧（<b>不含</b> <c>id</c>），<b>不等待</b>控制器响应；之后凡<b>无整数</b> <c>id</c> 的 JSON 将按 <c>ty</c> 分发给 <paramref name="handler"/>（在线程池执行）。
        /// </summary>
        /// <param name="topicTy">主题名，例如 <c>publish/VarUpdate</c>。</param>
        /// <param name="handler">自行解析 <see cref="PublishNotification.Db"/> 或 <see cref="PublishNotification.RawJson"/>；请勿长时间阻塞。</param>
        /// <param name="tcMilliseconds">协议字段 <c>tc</c>（毫秒）；默认 <c>100</c>。实际推送仍取决于数据是否变化。</param>
        /// <returns>释放以取消回调；<b>不会</b>向控制器发「退订」报文。TCP 断开后订阅与注册均失效，须重连后再次调用本方法。</returns>
        public async Task<PublishTopicSubscription> SubscribePublishTopic(
            string topicTy,
            Action<PublishNotification> handler,
            int tcMilliseconds = PublishSubscribeDefaults.TcMilliseconds)
        {
            if (string.IsNullOrEmpty(topicTy))
            {
                throw new ArgumentException("主题 ty 不能为空。", nameof(topicTy));
            }

            Polyfills.ThrowIfNull(handler);
            await _TcpClient.RegisterPublishHandlerAndSubscribe(topicTy, handler, tcMilliseconds);
            return new PublishTopicSubscription(_TcpClient, topicTy, handler);
        }

        /// <summary>
        /// 请求进入远程脚本模式（指令：<c>project/enterRemoteScriptMode</c>）。
        /// </summary>
        /// <returns>控制器返回的 <see cref="CommonResponse"/>（业务数据在 <see cref="CommonResponse.db"/>）。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器在 <c>err</c> 中报告错误，或其它执行失败（见 <see cref="Exception.Message"/> 与 <see cref="CodroidCommandException.ControllerError"/>）。</exception>
        public async Task<CommonResponse> EnterRemoteScriptMode()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "project/enterRemoteScriptMode", new { });
        }

        /// <summary>
        /// 直接下发脚本运行（指令：<c>project/runScript</c>）。
        /// </summary>
        /// <param name="mainScript">主程序脚本文本（scripts.main）。</param>
        /// <param name="subThreads">可选：线程脚本映射（scripts.subThreads）。</param>
        /// <param name="subPrograms">可选：子程序脚本映射（scripts.subPrograms）。</param>
        /// <param name="interrupts">可选：中断脚本映射（scripts.interrupts）。</param>
        /// <param name="vars">可选：脚本共享变量映射（db.vars）。</param>
        /// <returns>控制器返回的响应对象；成功时通常仅包含 <c>id</c> 与 <c>ty</c>。</returns>
        /// <exception cref="ArgumentException"><paramref name="mainScript"/> 为空或只包含空白字符。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> RunScript(
            string mainScript,
            IReadOnlyDictionary<string, string>? subThreads = null,
            IReadOnlyDictionary<string, string>? subPrograms = null,
            IReadOnlyDictionary<string, string>? interrupts = null,
            IReadOnlyDictionary<string, object>? vars = null)
        {
            if (string.IsNullOrWhiteSpace(mainScript))
            {
                throw new ArgumentException("mainScript 不能为空。", nameof(mainScript));
            }

            var scripts = new Dictionary<string, object>
            {
                ["main"] = mainScript
            };

            if (subThreads is { Count: > 0 })
            {
                scripts["subThreads"] = subThreads;
            }

            if (subPrograms is { Count: > 0 })
            {
                scripts["subPrograms"] = subPrograms;
            }

            if (interrupts is { Count: > 0 })
            {
                scripts["interrupts"] = interrupts;
            }

            var db = new Dictionary<string, object>
            {
                ["scripts"] = scripts
            };

            if (vars is { Count: > 0 })
            {
                db["vars"] = vars;
            }

            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "project/runScript", db);
        }

        /// <summary>
        /// 按工程 ID 启动运行（指令：<c>project/run</c>）。
        /// </summary>
        /// <param name="projectID">工程标识字符串。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> Run(string projectID)
        {
            int currentId = NextId();
            var data = new { id = projectID };
            return await _TcpClient.SendCommand(currentId, "project/run", data);
        }

        /// <summary>
        /// 按工程列表索引启动运行（指令：<c>project/runByIndex</c>）。
        /// </summary>
        /// <param name="index">工程在非负整数索引。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> RunByIndex(int index)
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "project/runByIndex", index);
        }

        /// <summary>
        /// 按工程 ID 单步运行（指令：<c>project/runStep</c>）。
        /// </summary>
        /// <param name="projectID">工程标识字符串。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> RunStep(string projectID)
        {
            int currentId = NextId();
            var data = new { id = projectID };
            return await _TcpClient.SendCommand(currentId, "project/runStep", data);
        }

        /// <summary>
        /// 暂停当前工程（指令：<c>project/pause</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> PauseProject()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "project/pause", new { });
        }

        /// <summary>
        /// 恢复已暂停的工程（指令：<c>project/resume</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> ResumeProject()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "project/resume", new { });
        }

        /// <summary>
        /// 停止当前工程（指令：<c>project/stop</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StopProject()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "project/stop", new { });
        }

        /// <summary>
        /// 读取全局变量列表（指令：<c>globalVar/getVars</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象（具体结构见 <see cref="CommonResponse.db"/>）。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> GetGlobalVars()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "globalVar/getVars", new { });
        }

        /// <summary>
        /// 获取所有全局变量并解析为字典（指令：<c>globalVar/getVars</c>），等价于 <see cref="GetGlobalVars"/> 后对 <see cref="GlobalVarCatalogParser.Parse"/> 的封装。
        /// </summary>
        /// <returns>变量名到条目（值、备注）的只读映射。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<IReadOnlyDictionary<string, GlobalVarCatalogEntry>> GetGlobalVarsCatalog()
        {
            var resp = await GetGlobalVars();
            return GlobalVarCatalogParser.Parse(resp);
        }

        /// <summary>
        /// 增量保存全局变量（指令：<c>globalVar/saveVars</c>）；同名变量会更新值与备注（若本次提供了备注）。
        /// </summary>
        /// <param name="name">变量名，须符合 <see cref="GlobalVarNaming.Validate"/>。</param>
        /// <param name="value">变量值：数值、字符串、数组、字典等会经 JSON 序列化写入 <c>val</c>；已拼好的 JSON 文本请用 <see cref="GlobalVarRawJson"/> 包装。</param>
        /// <param name="remark">备注，可为中文；null 或空白则不发送 <c>nm</c>。</param>
        /// <returns>控制器响应。</returns>
        /// <exception cref="ArgumentException">变量名非法。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> SaveGlobalVar(string name, object value, string? remark = null)
        {
            return SaveGlobalVars(new[] { new GlobalVarSaveItem(name, value, remark) });
        }

        /// <summary>
        /// 批量增量保存全局变量（指令：<c>globalVar/saveVars</c>）。
        /// </summary>
        /// <param name="items">一项或多条保存说明；批次内变量名不得重复。</param>
        /// <returns>控制器响应。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> 为 null。</exception>
        /// <exception cref="ArgumentException">项为空、变量名非法或批次内重名。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SaveGlobalVars(IReadOnlyCollection<GlobalVarSaveItem> items)
        {
            Polyfills.ThrowIfNull(items);
            if (items.Count == 0)
            {
                throw new ArgumentException("至少需要提供一项变量。", nameof(items));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var db = new Dictionary<string, Dictionary<string, object>>();

            foreach (var item in items)
            {
                GlobalVarNaming.Validate(item.Name);
                if (!seen.Add(item.Name))
                {
                    throw new ArgumentException($"批次中存在重复的变量名: {item.Name}", nameof(items));
                }

                var wireVal = GlobalVarValueFormatter.ToWireString(item.Value);
                var body = new Dictionary<string, object> { ["val"] = wireVal };
                if (!string.IsNullOrEmpty(item.Remark))
                {
                    body["nm"] = item.Remark!;
                }

                db[item.Name] = body;
            }

            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "globalVar/saveVars", db);
        }

        /// <summary>
        /// 删除指定全局变量（指令：<c>globalVar/removeVars</c>）；删除不存在的变量不会报错。
        /// </summary>
        /// <param name="names">要删除的变量名列表，每个名字须符合 <see cref="GlobalVarNaming.Validate"/>。</param>
        /// <returns>控制器响应。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> 为 null。</exception>
        /// <exception cref="ArgumentException">列表为空或某变量名非法。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> RemoveGlobalVars(IEnumerable<string> names)
        {
            Polyfills.ThrowIfNull(names);
            var arr = names as string[] ?? names.ToArray();
            if (arr.Length == 0)
            {
                throw new ArgumentException("至少提供一个变量名。", nameof(names));
            }

            foreach (var n in arr)
            {
                GlobalVarNaming.Validate(n);
            }

            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "globalVar/removeVars", arr);
        }

        /// <summary>
        /// 机器人上电 / 使能打开（指令：<c>Robot/switchOn</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SwitchOn()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/switchOn", new { });
        }

        /// <summary>
        /// 机器人下电 / 使能关闭（指令：<c>Robot/switchOff</c>，<c>db</c> 为空字符串）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SwitchOff()
        {
            return await SendCommandEmptyDb("Robot/switchOff");
        }

        /// <summary>
        /// 进入手动模式（指令：<c>Robot/toManual</c>）。需固件 2.3.2.6+；不能从远程模式直接跳入，须先经自动模式（见 <see cref="EnterManualModeViaAuto"/>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> ToManual() => SendCommandEmptyDb("Robot/toManual");

        /// <summary>
        /// 进入自动模式（指令：<c>Robot/toAuto</c>）。需固件 2.3.2.6+。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> ToAuto() => SendCommandEmptyDb("Robot/toAuto");

        /// <summary>
        /// 进入远程模式（指令：<c>Robot/toRemote</c>）。需固件 2.3.2.6+；不能从手动模式直接跳入，须先经自动模式（见 <see cref="EnterRemoteModeViaAuto"/>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> ToRemote() => SendCommandEmptyDb("Robot/toRemote");

        /// <summary>
        /// 先 <see cref="ToAuto"/> 再 <see cref="ToManual"/>，用于在远程与手动之间切换时满足控制器「必须先切自动」的限制。
        /// </summary>
        /// <returns>最后一次（进入手动）请求的响应。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">任一步等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">任一步控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> EnterManualModeViaAuto()
        {
            await ToAuto();
            return await ToManual();
        }

        /// <summary>
        /// 先 <see cref="ToAuto"/> 再 <see cref="ToRemote"/>，用于在手动与远程之间切换时满足控制器「必须先切自动」的限制。
        /// </summary>
        /// <returns>最后一次（进入远程）请求的响应。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">任一步等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">任一步控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> EnterRemoteModeViaAuto()
        {
            await ToAuto();
            return await ToRemote();
        }

        /// <summary>
        /// 进入仿真模式（指令：<c>Robot/toSimulation</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> ToSimulation() => SendCommandEmptyDb("Robot/toSimulation");

        /// <summary>
        /// 进入实机模式（指令：<c>Robot/toActual</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> ToActual() => SendCommandEmptyDb("Robot/toActual");

        /// <summary>
        /// 进入拖拽模式（指令：<c>Robot/startDrag</c>）。需固件 2.3.2.6+；仅远程或手动模式下可用。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错、当前模式不允许拖拽或其它执行失败。</exception>
        public Task<CommonResponse> StartDrag() => SendCommandEmptyDb("Robot/startDrag");

        /// <summary>
        /// 退出拖拽模式（指令：<c>Robot/stopDrag</c>）。需固件 2.3.2.6+。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> StopDrag() => SendCommandEmptyDb("Robot/stopDrag");

        /// <summary>
        /// 清除错误（指令：<c>System/clearError</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public Task<CommonResponse> ClearSystemError() => SendCommandEmptyDb("System/clearError");

        /// <summary>
        /// 六维力传感器零力校准 / 带载去皮（指令：<c>Robot/ZeroForceCalibration</c>）。
        /// </summary>
        public Task<CommonResponse> ZeroForceCalibration(int calibrationTimeMs = 1000)
        {
            return _TcpClient.SendCommand(NextId(), "Robot/ZeroForceCalibration", new { calibrationTimeMs });
        }

        /// <summary>
        /// 进入力控前一次性配参。当前 C# SDK 固定下发导纳算法 <c>algo=1</c>，不开放 algo 入参。
        /// </summary>
        public Task<CommonResponse> InitForceControl(
            ForceFrame frame,
            IReadOnlyList<ForceAxisMode> axisMode,
            object? compliance = null,
            object? constantForce = null,
            double[]? userFrameRpy = null,
            double[]? desiredWrench = null,
            object? forceLimit = null)
        {
            ValidateLength(axisMode, 6, nameof(axisMode));
            var db = new Dictionary<string, object?>
            {
                ["algo"] = (int)ForceControlAlgo.Admittance,
                ["frame"] = (int)frame,
                ["axisMode"] = axisMode.Select(x => (int)x).ToArray()
            };
            if (compliance != null) db["compliance"] = compliance;
            if (constantForce != null) db["constantForce"] = constantForce;
            if (userFrameRpy != null) db["userFrameRpy"] = userFrameRpy;
            if (desiredWrench != null) db["desiredWrench"] = desiredWrench;
            if (forceLimit != null) db["forceLimit"] = forceLimit;
            return _TcpClient.SendCommand(NextId(), "Robot/initForceControl", db);
        }

        /// <summary>
        /// 启动力控（指令：<c>Robot/startForceControl</c>）。
        /// </summary>
        public Task<CommonResponse> StartForceControl() =>
            _TcpClient.SendCommand(NextId(), "Robot/startForceControl", new { });

        /// <summary>
        /// 平滑停止力控（指令：<c>Robot/stopForceControl</c>）。
        /// </summary>
        public Task<CommonResponse> StopForceControl(int smoothTimeMs = 500) =>
            _TcpClient.SendCommand(NextId(), "Robot/stopForceControl", new { smoothTimeMs });

        /// <summary>
        /// 在线调整力控参数（指令：<c>Robot/tuneForceParams</c>）。
        /// </summary>
        public Task<CommonResponse> TuneForceParams(
            double[]? stiffness = null,
            double[]? damping = null,
            double[]? mass = null,
            double[]? desiredForce = null,
            double[]? kp = null,
            double[]? kd = null,
            double? rampTime = null)
        {
            var db = new Dictionary<string, object?>();
            if (stiffness != null) db["stiffness"] = stiffness;
            if (damping != null) db["damping"] = damping;
            if (mass != null) db["mass"] = mass;
            if (desiredForce != null) db["desiredForce"] = desiredForce;
            if (kp != null) db["kp"] = kp;
            if (kd != null) db["kd"] = kd;
            if (rampTime != null) db["rampTime"] = rampTime.Value;
            return _TcpClient.SendCommand(NextId(), "Robot/tuneForceParams", db);
        }

        /// <summary>
        /// 启动接触检测（指令：<c>Robot/startContactDetection</c>）。
        /// </summary>
        public Task<CommonResponse> StartContactDetection(
            double[] direction,
            double? feedVelocity = null,
            double? contactForceThreshold = null,
            double? velDropRatio = null,
            double? maxTravel = null,
            double? timeoutMs = null)
        {
            ValidateLength(direction, 6, nameof(direction));
            var db = new Dictionary<string, object?> { ["direction"] = direction };
            if (feedVelocity != null) db["feedVelocity"] = feedVelocity.Value;
            if (contactForceThreshold != null) db["contactForceThreshold"] = contactForceThreshold.Value;
            if (velDropRatio != null) db["velDropRatio"] = velDropRatio.Value;
            if (maxTravel != null) db["maxTravel"] = maxTravel.Value;
            if (timeoutMs != null) db["timeoutMs"] = timeoutMs.Value;
            return _TcpClient.SendCommand(NextId(), "Robot/startContactDetection", db);
        }

        /// <summary>
        /// 设置过力保护（指令：<c>Robot/setOverforceProtection</c>）。
        /// </summary>
        public Task<CommonResponse> SetOverforceProtection(
            bool? enable = null,
            double[]? forceThreshold = null,
            double? holdMs = null)
        {
            if (forceThreshold != null) ValidateLength(forceThreshold, 6, nameof(forceThreshold));
            var db = new Dictionary<string, object?>();
            if (enable != null) db["enable"] = enable.Value;
            if (forceThreshold != null) db["forceThreshold"] = forceThreshold;
            if (holdMs != null) db["holdMs"] = holdMs.Value;
            return _TcpClient.SendCommand(NextId(), "Robot/setOverforceProtection", db);
        }

        /// <summary>
        /// 设置力数据健康监控（指令：<c>Robot/setForceDataHealth</c>）。
        /// </summary>
        public Task<CommonResponse> SetForceDataHealth(
            bool? enable = null,
            double? timeoutMs = null,
            double? maxPacketLossRatio = null,
            int? packetLossWindow = null,
            double? forceSaturation = null,
            double? torqueSaturation = null)
        {
            var db = new Dictionary<string, object?>();
            if (enable != null) db["enable"] = enable.Value;
            if (timeoutMs != null) db["timeoutMs"] = timeoutMs.Value;
            if (maxPacketLossRatio != null) db["maxPacketLossRatio"] = maxPacketLossRatio.Value;
            if (packetLossWindow != null) db["packetLossWindow"] = packetLossWindow.Value;
            if (forceSaturation != null) db["forceSaturation"] = forceSaturation.Value;
            if (torqueSaturation != null) db["torqueSaturation"] = torqueSaturation.Value;
            return _TcpClient.SendCommand(NextId(), "Robot/setForceDataHealth", db);
        }

        /// <summary>
        /// 读取力控状态快照（指令：<c>Robot/getForceState</c>）。
        /// </summary>
        public async Task<ForceControlState> GetForceState()
        {
            var response = await _TcpClient.SendCommand(NextId(), "Robot/getForceState", string.Empty)
                .ConfigureAwait(false);
            return ParseForceControlState(response.db);
        }

        /// <summary>读取力控启用状态。</summary>
        public async Task<bool> GetForceStateEnabled() => (await GetForceState().ConfigureAwait(false)).Enabled;
        /// <summary>读取力控 pending 状态。</summary>
        public async Task<bool> GetForceStatePending() => (await GetForceState().ConfigureAwait(false)).Pending;
        /// <summary>读取力控算法编号。</summary>
        public async Task<int> GetForceStateAlgo() => (await GetForceState().ConfigureAwait(false)).Algo;
        /// <summary>读取力数据有效状态。</summary>
        public async Task<bool> GetForceStateValid() => (await GetForceState().ConfigureAwait(false)).Valid;
        /// <summary>读取接触检测状态。</summary>
        public async Task<bool> GetForceStateIsContact() => (await GetForceState().ConfigureAwait(false)).IsContact;
        /// <summary>读取过力保护触发状态。</summary>
        public async Task<bool> GetForceStateIsOverforce() => (await GetForceState().ConfigureAwait(false)).IsOverforce;
        /// <summary>读取力数据健康状态编号。</summary>
        public async Task<int> GetForceStateHealth() => (await GetForceState().ConfigureAwait(false)).Health;
        /// <summary>读取 TCP 坐标系下的六维力/力矩。</summary>
        public async Task<double[]> GetForceStateWrenchTcp() => (await GetForceState().ConfigureAwait(false)).WrenchTcp;
        /// <summary>读取基坐标系下的六维力/力矩。</summary>
        public async Task<double[]> GetForceStateWrenchBase() => (await GetForceState().ConfigureAwait(false)).WrenchBase;
        /// <summary>读取期望六维力/力矩。</summary>
        public async Task<double[]> GetForceStateDesiredWrench() => (await GetForceState().ConfigureAwait(false)).DesiredWrench;
        /// <summary>读取力跟踪误差。</summary>
        public async Task<double[]> GetForceStateTrackError() => (await GetForceState().ConfigureAwait(false)).TrackError;
        /// <summary>读取六个轴的力控模式。</summary>
        public async Task<int[]> GetForceStateAxisMode() => (await GetForceState().ConfigureAwait(false)).AxisMode;

        private static ForceControlState ParseForceControlState(JsonElement db)
        {
            var state = new ForceControlState();
            if (db.ValueKind != JsonValueKind.Object)
            {
                return state;
            }
            state.Enabled = GetBool(db, "enabled");
            state.Pending = GetBool(db, "pending");
            state.Algo = GetInt(db, "algo");
            state.Valid = GetBool(db, "valid");
            state.IsContact = GetBool(db, "isContact");
            state.IsOverforce = GetBool(db, "isOverforce");
            state.Health = GetInt(db, "health");
            state.WrenchTcp = GetDoubleArray(db, "wrenchTcp");
            state.WrenchBase = GetDoubleArray(db, "wrenchBase");
            state.DesiredWrench = GetDoubleArray(db, "desiredWrench");
            state.TrackError = GetDoubleArray(db, "trackError");
            state.AxisMode = GetIntArray(db, "axisMode");
            return state;
        }

        private static bool GetBool(JsonElement db, string name)
        {
            if (!db.TryGetProperty(name, out var value))
                return false;
            return value.ValueKind == JsonValueKind.True;
        }

        private static int GetInt(JsonElement db, string name) =>
            db.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)
                ? n
                : 0;

        private static double[] GetDoubleArray(JsonElement db, string name)
        {
            if (!db.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return new double[6];
            var list = new List<double>();
            foreach (var item in value.EnumerateArray())
            {
                list.Add(item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var v) ? v : 0.0);
            }
            while (list.Count < 6) list.Add(0.0);
            return list.Take(6).ToArray();
        }

        private static int[] GetIntArray(JsonElement db, string name)
        {
            if (!db.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return new int[6];
            var list = new List<int>();
            foreach (var item in value.EnumerateArray())
            {
                list.Add(item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var v) ? v : 0);
            }
            while (list.Count < 6) list.Add(0);
            return list.Take(6).ToArray();
        }

        private static void ValidateLength<T>(IReadOnlyCollection<T> values, int expected, string name)
        {
            if (values.Count != expected)
            {
                throw new ArgumentException($"{name} 必须包含 {expected} 个元素。", name);
            }
        }

        /// <summary>
        /// 发送 <c>db</c> 为空字符串的 JSON 指令（与协议示例一致）。
        /// </summary>
        private Task<CommonResponse> SendCommandEmptyDb(string type) =>
            _TcpClient.SendCommand(NextId(), type, string.Empty);

        /// <summary>
        /// 批量查询 IO 当前值（指令：<c>IOManager/GetIOValue</c>）；结果在 <see cref="CommonResponse.db"/> 数组中。
        /// </summary>
        /// <param name="pins">要读取的 IO 列表，每项包含类型（<see cref="IoPortKind"/>）与端口号。</param>
        /// <returns>控制器原始响应；读取值位于 <see cref="CommonResponse.db"/> 数组中。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="pins"/> 为 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="pins"/> 为空，或包含非法 IO 类型。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> GetIoValues(IReadOnlyList<(string Type, int Port)> pins)
        {
            var db = IoGetResponseParser.BuildGetQuery(pins);
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "IOManager/GetIOValue", db);
        }

        /// <summary>
        /// 读取数字量输入 DI，返回 <c>0</c> 或 <c>1</c>。
        /// </summary>
        /// <param name="port">DI 端口号。</param>
        /// <returns>端口当前值，固定为 <c>0</c> 或 <c>1</c>。</returns>
        /// <exception cref="InvalidOperationException">响应中没有匹配端口，或 value 无法解析为数字量。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<int> GetDi(int port)
        {
            var resp = await GetIoValues(new[] { (IoPortKind.Di, port) });
            return IoGetResponseParser.ParseDigital(resp, IoPortKind.Di, port);
        }

        /// <summary>
        /// 读取数字量输出 DO 当前状态，返回 <c>0</c> 或 <c>1</c>。
        /// </summary>
        /// <param name="port">DO 端口号。</param>
        /// <returns>端口当前值，固定为 <c>0</c> 或 <c>1</c>。</returns>
        /// <exception cref="InvalidOperationException">响应中没有匹配端口，或 value 无法解析为数字量。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<int> GetDo(int port)
        {
            var resp = await GetIoValues(new[] { (IoPortKind.Do, port) });
            return IoGetResponseParser.ParseDigital(resp, IoPortKind.Do, port);
        }

        /// <summary>
        /// 读取模拟量输入 AI，返回浮点值。
        /// </summary>
        /// <param name="port">AI 端口号。</param>
        /// <returns>端口当前模拟量值。</returns>
        /// <exception cref="InvalidOperationException">响应中没有匹配端口，或 value 无法解析为浮点数。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<double> GetAi(int port)
        {
            var resp = await GetIoValues(new[] { (IoPortKind.Ai, port) });
            return IoGetResponseParser.ParseAnalog(resp, IoPortKind.Ai, port);
        }

        /// <summary>
        /// 读取模拟量输出 AO 当前值，返回浮点值。
        /// </summary>
        /// <param name="port">AO 端口号。</param>
        /// <returns>端口当前模拟量值。</returns>
        /// <exception cref="InvalidOperationException">响应中没有匹配端口，或 value 无法解析为浮点数。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<double> GetAo(int port)
        {
            var resp = await GetIoValues(new[] { (IoPortKind.Ao, port) });
            return IoGetResponseParser.ParseAnalog(resp, IoPortKind.Ao, port);
        }

        /// <summary>
        /// 写入数字量输出 DO（指令：<c>IOManager/SetIOValue</c>），<paramref name="value"/> 只能为 <c>0</c> 或 <c>1</c>。
        /// </summary>
        /// <param name="port">DO 端口号。</param>
        /// <param name="value">目标值，只能为 <c>0</c> 或 <c>1</c>。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> 不是 <c>0</c> 或 <c>1</c>。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetDo(int port, int value)
        {
            if (value is not (0 or 1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "DO 只能写入 0 或 1。");
            }

            int currentId = NextId();
            var db = new { type = IoPortKind.Do, port, value };
            return await _TcpClient.SendCommand(currentId, "IOManager/SetIOValue", db);
        }

        /// <summary>
        /// 写入模拟量输出 AO（指令：<c>IOManager/SetIOValue</c>）。
        /// </summary>
        /// <param name="port">AO 端口号。</param>
        /// <param name="value">目标模拟量值。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetAo(int port, double value)
        {
            int currentId = NextId();
            var db = new { type = IoPortKind.Ao, port, value };
            return await _TcpClient.SendCommand(currentId, "IOManager/SetIOValue", db);
        }

        /// <summary>
        /// 读取单个寄存器（指令：<c>RegisterManager/GetRegisterValue</c>）。
        /// 返回值使用 <see cref="RegisterReadValue.GetInt32"/> 或 <see cref="RegisterReadValue.GetDouble"/> 按实际类型读取。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <returns>包含地址与原始 JSON 值的读取结果。</returns>
        /// <exception cref="ArgumentException">地址列表为空（理论上不会由本方法触发）或响应地址与请求不一致。</exception>
        /// <exception cref="InvalidOperationException">响应无法解析为寄存器列表。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<RegisterReadValue> GetRegisterValue(int address)
        {
            IReadOnlyList<RegisterReadValue> batch = await GetRegisterValues(new[] { address });
            return batch[0];
        }

        /// <summary>
        /// 批量读取寄存器（指令：<c>RegisterManager/GetRegisterValue</c>）；返回数组顺序与 <paramref name="addresses"/> 一致。
        /// </summary>
        /// <param name="addresses">要读取的寄存器地址列表。</param>
        /// <returns>与 <paramref name="addresses"/> 顺序一一对应的读取结果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="addresses"/> 为 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="addresses"/> 为空，或响应数量 / 地址与请求不一致。</exception>
        /// <exception cref="InvalidOperationException">响应无法解析为寄存器列表。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<IReadOnlyList<RegisterReadValue>> GetRegisterValues(IReadOnlyList<int> addresses)
        {
            RegisterValidation.ValidateAddresses(addresses);
            int currentId = NextId();
            int[] db = addresses.ToArray();
            CommonResponse resp = await _TcpClient.SendCommand(currentId, "RegisterManager/GetRegisterValue", db);
            return RegisterResponseParser.ParseAligned(resp, addresses);
        }

        /// <summary>
        /// 写入寄存器整型值（指令：<c>RegisterManager/SetRegisterValue</c>）。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">要写入的整型值。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetRegisterValue(int address, int value)
        {
            int currentId = NextId();
            var db = new { address, value };
            return await _TcpClient.SendCommand(currentId, "RegisterManager/SetRegisterValue", db);
        }

        /// <summary>
        /// 写入寄存器浮点值（指令：<c>RegisterManager/SetRegisterValue</c>）。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">要写入的浮点值。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetRegisterValue(int address, double value)
        {
            int currentId = NextId();
            var db = new { address, value };
            return await _TcpClient.SendCommand(currentId, "RegisterManager/SetRegisterValue", db);
        }

        /// <summary>
        /// 设置扩展数组元素数据类型（指令：<c>RegisterManager/setExtendArrayType</c>）；<paramref name="index"/> 为 0~999，<paramref name="type"/> 见 <see cref="RegisterExtendArrayValueType"/>。
        /// </summary>
        /// <param name="index">扩展数组索引，范围 0~999。</param>
        /// <param name="type">元素类型，取值见 <see cref="RegisterExtendArrayValueType"/>。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> 不在 0~999。</exception>
        /// <exception cref="ArgumentException"><paramref name="type"/> 不是支持的扩展数组类型。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetExtendArrayType(int index, string type)
        {
            RegisterValidation.ValidateExtendIndex(index);
            RegisterValidation.ValidateExtendType(type);
            int currentId = NextId();
            var db = new { index, type };
            return await _TcpClient.SendCommand(currentId, "RegisterManager/setExtendArrayType", db);
        }

        /// <summary>
        /// 删除扩展数组指定索引并重置数据（指令：<c>RegisterManager/removeExtendArray</c>）；<paramref name="index"/> 为 0~999。
        /// </summary>
        /// <param name="index">扩展数组索引，范围 0~999。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> 不在 0~999。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> RemoveExtendArray(int index)
        {
            RegisterValidation.ValidateExtendIndex(index);
            int currentId = NextId();
            var db = new { index };
            return await _TcpClient.SendCommand(currentId, "RegisterManager/removeExtendArray", db);
        }

        /// <summary>
        /// 关节空间 → 笛卡尔空间正解（指令：<c>Robot/apostocpos</c>）。
        /// </summary>
        /// <param name="jointDegrees">六轴关节角，单位：度。</param>
        /// <param name="userFrame">用户坐标系 [x,y,z,rx,ry,rz]，单位：毫米、度（与协议一致）。</param>
        /// <param name="toolFrame">工具坐标系，同上。</param>
        /// <param name="externalAxisPositions">附加轴位置 <c>ep</c>；无附加轴时传 null 或空数组。</param>
        /// <returns>控制器响应；笛卡尔结果为 <see cref="CommonResponse.db"/> 中长度为 6 的数组。</returns>
        /// <exception cref="ArgumentException">向量长度不是 6。</exception>
        /// <exception cref="InvalidOperationException">未连接等（见 <see cref="FutureTcpClient.SendCommand"/>）。</exception>
        /// <exception cref="TimeoutException">等待响应超时。</exception>
        /// <exception cref="CodroidCommandException">控制器报错。</exception>
        public async Task<CommonResponse> AposToCpos(
            double[] jointDegrees,
            double[]? userFrame = null,
            double[]? toolFrame = null,
            double[]? externalAxisPositions = null)
        {
            RobotKinematics.RequireVector6(nameof(jointDegrees), jointDegrees);
            if (userFrame != null) RobotKinematics.RequireVector6(nameof(userFrame), userFrame);
            if (toolFrame != null) RobotKinematics.RequireVector6(nameof(toolFrame), toolFrame);
            var ep = externalAxisPositions ?? Array.Empty<double>();

            int currentId = NextId();
            object db = userFrame != null && toolFrame != null
                ? new { jp = jointDegrees, coor = userFrame, tool = toolFrame, ep }
                : userFrame != null
                    ? new { jp = jointDegrees, coor = userFrame, ep }
                    : toolFrame != null
                        ? new { jp = jointDegrees, tool = toolFrame, ep }
                        : new { jp = jointDegrees, ep };
            return await _TcpClient.SendCommand(currentId, "Robot/apostocpos", db);
        }

        /// <summary>
        /// 正解并解析 <c>db</c> 为六维笛卡尔位姿（单位：毫米、度，与控制器约定一致）。
        /// </summary>
        /// <param name="jointDegrees">六轴关节角，单位：度。</param>
        /// <param name="userFrame">用户坐标系 [x,y,z,rx,ry,rz]，单位：毫米、度。</param>
        /// <param name="toolFrame">工具坐标系 [x,y,z,rx,ry,rz]，单位：毫米、度。</param>
        /// <param name="externalAxisPositions">附加轴位置；无附加轴时传 null 或空数组。</param>
        /// <returns>六维笛卡尔位姿 [x,y,z,rx,ry,rz]，单位：毫米、度。</returns>
        /// <exception cref="ArgumentException">任一必需向量长度不是 6。</exception>
        /// <exception cref="InvalidOperationException"><c>db</c> 无法解析为 6 维数组。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<double[]> AposToCposPose(
            double[] jointDegrees,
            double[]? userFrame = null,
            double[]? toolFrame = null,
            double[]? externalAxisPositions = null)
        {
            var resp = await AposToCpos(jointDegrees, userFrame, toolFrame, externalAxisPositions);
            return RobotKinematics.ParseDbAsVector6(resp.db);
        }

        /// <summary>
        /// 笛卡尔空间 → 关节空间逆解（指令：<c>Robot/cpostoapos</c>）。
        /// </summary>
        /// <param name="cartesianMmDeg">末端位姿 [x,y,z,rx,ry,rz]，线位移毫米、姿态度。</param>
        /// <param name="referenceJointDegrees">参考关节角（度）；无解时可尝试修改该组角。</param>
        /// <param name="externalAxisPositions">可选附加轴 <c>ep</c>；无则 null 或空数组。</param>
        /// <returns>控制器响应；关节角在 <see cref="CommonResponse.db"/> 中（度）。</returns>
        /// <exception cref="ArgumentException">向量长度不是 6。</exception>
        /// <exception cref="TimeoutException">等待响应超时。</exception>
        /// <exception cref="CodroidCommandException">控制器报错。</exception>
        public async Task<CommonResponse> CposToApos(
            double[] cartesianMmDeg,
            double[]? referenceJointDegrees = null,
            double[]? externalAxisPositions = null)
        {
            RobotKinematics.RequireVector6(nameof(cartesianMmDeg), cartesianMmDeg);
            var rj = referenceJointDegrees
                ?? CriData?.JointPosition
                ?? new[] { 20.0, 20, 20, 20, 20, 20 };
            RobotKinematics.RequireVector6(nameof(rj), rj);
            var ep = externalAxisPositions ?? Array.Empty<double>();

            int currentId = NextId();
            var db = new
            {
                cp = cartesianMmDeg,
                rj,
                ep
            };
            return await _TcpClient.SendCommand(currentId, "Robot/cpostoapos", db);
        }

        /// <summary>
        /// 逆解并解析 <c>db</c> 为六轴关节角（度）。若控制器返回空数组则抛异常，请调整 <paramref name="referenceJointDegrees"/>。
        /// </summary>
        /// <param name="cartesianMmDeg">末端位姿 [x,y,z,rx,ry,rz]，线位移毫米、姿态度。</param>
        /// <param name="referenceJointDegrees">参考关节角（度）；无解时可尝试修改该组角。</param>
        /// <param name="externalAxisPositions">可选附加轴 <c>ep</c>；无则 null 或空数组。</param>
        /// <returns>六轴关节角，单位：度。</returns>
        /// <exception cref="ArgumentException">任一必需向量长度不是 6。</exception>
        /// <exception cref="InvalidOperationException">返回空数组或无法解析。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<double[]> CposToAposJoints(
            double[] cartesianMmDeg,
            double[]? referenceJointDegrees = null,
            double[]? externalAxisPositions = null)
        {
            var resp = await CposToApos(cartesianMmDeg, referenceJointDegrees, externalAxisPositions);
            return RobotKinematics.ParseDbAsVector6(resp.db);
        }

        /// <summary>
        /// 笛卡尔坐标系/工具系换算（指令：<c>Robot/cpostocpos</c>），返回 <see cref="CartesianPoint"/>。
        /// </summary>
        /// <param name="cp">源点位，<see cref="CartesianPoint.Cp"/> 为 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="coor1">源用户坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="tool1">源工具坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="coor2">目标用户坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="tool2">目标工具坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <returns>换算后的 TCP 位姿。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <exception cref="ArgumentException">位姿向量长度不是 6。</exception>
        /// <exception cref="InvalidOperationException"><c>db</c> 无法解析为 6 维数组。</exception>
        /// <exception cref="TimeoutException">等待响应超时。</exception>
        /// <exception cref="CodroidCommandException">控制器报错。</exception>
        public async Task<CartesianPoint> CposToCposPose(
            CartesianPoint cp,
            double[] coor1,
            double[] tool1,
            double[] coor2,
            double[] tool2)
        {
            var pose = await CposToCposDouble(cp, coor1, tool1, coor2, tool2);
            return CartesianPoint.MmDeg(pose);
        }

        /// <summary>
        /// 笛卡尔坐标系/工具系换算（指令：<c>Robot/cpostocpos</c>），返回六维位姿数组。
        /// </summary>
        /// <param name="cp">源点位，<see cref="CartesianPoint.Cp"/> 为 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="coor1">源用户坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="tool1">源工具坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="coor2">目标用户坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <param name="tool2">目标工具坐标系位姿 [x,y,z,rx,ry,rz]，单位 mm、度。</param>
        /// <returns>换算后的 [x,y,z,rx,ry,rz]，单位 mm、度。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <exception cref="ArgumentException">位姿向量长度不是 6。</exception>
        /// <exception cref="InvalidOperationException"><c>db</c> 无法解析为 6 维数组。</exception>
        /// <exception cref="TimeoutException">等待响应超时。</exception>
        /// <exception cref="CodroidCommandException">控制器报错。</exception>
        public async Task<double[]> CposToCposDouble(
            CartesianPoint cp,
            double[] coor1,
            double[] tool1,
            double[] coor2,
            double[] tool2)
        {
            Polyfills.ThrowIfNull(cp);
            RobotKinematics.RequireVector6(nameof(cp.Cp), cp.Cp);
            RobotKinematics.RequireVector6(nameof(coor1), coor1);
            RobotKinematics.RequireVector6(nameof(tool1), tool1);
            RobotKinematics.RequireVector6(nameof(coor2), coor2);
            RobotKinematics.RequireVector6(nameof(tool2), tool2);

            int currentId = NextId();
            var db = new
            {
                cp = cp.Cp,
                coor1,
                tool1,
                coor2,
                tool2
            };
            var resp = await _TcpClient.SendCommand(currentId, "Robot/cpostocpos", db);
            return RobotKinematics.ParseDbAsVector6(resp.db);
        }

        /// <summary>
        /// 笛卡尔相对位姿/偏移计算（指令：<c>Robot/calculateRelativePose</c>）。
        /// </summary>
        /// <param name="tcpPoseWorld">当前末端 TCP 在世界系下的位姿 [x,y,z,a,b,c]，毫米与度。</param>
        /// <param name="offset">偏移 [x,y,z,a,b,c]，毫米与度。</param>
        /// <param name="coorType">在工具系或用户系下施加偏移。</param>
        /// <param name="tcpPoseInPosCoorFrame">
        /// 可选。当前末端在「posCoor 坐标系」下的位姿；传入后结果也在该坐标系下解释（见控制器文档）。
        /// </param>
        /// <param name="userCoorFrame">
        /// 当 <paramref name="coorType"/> 为 <see cref="RelativePoseCoorType.User"/> 时可选，偏移所用用户坐标系；默认协议为世界系。
        /// </param>
        /// <returns>控制器响应；偏移后位姿在 <see cref="CommonResponse.db"/> 中。</returns>
        /// <exception cref="ArgumentException">向量长度不是 6。</exception>
        /// <exception cref="TimeoutException">等待响应超时。</exception>
        /// <exception cref="CodroidCommandException">控制器报错。</exception>
        public async Task<CommonResponse> CalculateRelativePose(
            double[] tcpPoseWorld,
            double[] offset,
            RelativePoseCoorType coorType,
            double[]? tcpPoseInPosCoorFrame = null,
            double[]? userCoorFrame = null)
        {
            RobotKinematics.RequireVector6(nameof(tcpPoseWorld), tcpPoseWorld);
            RobotKinematics.RequireVector6(nameof(offset), offset);
            if (tcpPoseInPosCoorFrame != null)
            {
                RobotKinematics.RequireVector6(nameof(tcpPoseInPosCoorFrame), tcpPoseInPosCoorFrame);
            }

            if (userCoorFrame != null)
            {
                RobotKinematics.RequireVector6(nameof(userCoorFrame), userCoorFrame);
            }

            var wireType = RobotKinematics.ToWireCoorType(coorType);
            var db = new Dictionary<string, object>
            {
                ["pos"] = tcpPoseWorld,
                ["offset"] = offset,
                ["coorType"] = wireType
            };

            if (tcpPoseInPosCoorFrame != null)
            {
                db["posCoor"] = tcpPoseInPosCoorFrame;
            }

            if (coorType == RelativePoseCoorType.User && userCoorFrame != null)
            {
                db["coor"] = userCoorFrame;
            }

            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/calculateRelativePose", db);
        }

        /// <summary>
        /// 相对位姿计算并解析结果为六维位姿（毫米、度）。
        /// </summary>
        /// <param name="tcpPoseWorld">当前末端 TCP 在世界系下的位姿 [x,y,z,a,b,c]，毫米与度。</param>
        /// <param name="offset">偏移 [x,y,z,a,b,c]，毫米与度。</param>
        /// <param name="coorType">在工具系或用户系下施加偏移。</param>
        /// <param name="tcpPoseInPosCoorFrame">可选。当前末端在目标坐标系下的位姿。</param>
        /// <param name="userCoorFrame">可选。用户坐标系定义；仅 <paramref name="coorType"/> 为 <see cref="RelativePoseCoorType.User"/> 时发送。</param>
        /// <returns>偏移后的六维位姿，单位：毫米、度。</returns>
        /// <exception cref="ArgumentException">任一提供的位姿 / 坐标系向量长度不是 6。</exception>
        /// <exception cref="InvalidOperationException"><c>db</c> 无法解析为 6 维数组。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<double[]> CalculateRelativePoseResult(
            double[] tcpPoseWorld,
            double[] offset,
            RelativePoseCoorType coorType,
            double[]? tcpPoseInPosCoorFrame = null,
            double[]? userCoorFrame = null)
        {
            var resp = await CalculateRelativePose(
                tcpPoseWorld,
                offset,
                coorType,
                tcpPoseInPosCoorFrame,
                userCoorFrame);
            return RobotKinematics.ParseDbAsVector6(resp.db);
        }

        /// <summary>
        /// 启动点动（指令：<c>Robot/jog</c>）。启动后须每约 <see cref="RobotMotionHeartbeat.RecommendedIntervalMilliseconds"/> ms 调用 <see cref="JogHeartbeat"/>。
        /// </summary>
        /// <param name="parameters">点动参数，包含模式、速度、轴/方向索引、坐标系类型与坐标系 ID。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> 为 null。</exception>
        /// <exception cref="ArgumentException">点动速度不在 -1~1，或其它参数不符合协议要求。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StartJog(RobotJogParameters parameters)
        {
            Polyfills.ThrowIfNull(parameters);
            RobotMotionValidation.ValidateJog(parameters);
            int currentId = NextId();
            var db = new
            {
                mode = parameters.Mode,
                speed = parameters.Speed,
                index = parameters.Index,
                coorType = parameters.CoorType,
                coorId = parameters.CoorId
            };
            return await _TcpClient.SendCommand(currentId, "Robot/jog", db);
        }

        /// <summary>
        /// 停止点动（指令：<c>Robot/stopJog</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StopJog()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/stopJog", string.Empty);
        }

        /// <summary>
        /// 点动心跳（指令：<c>Robot/jogHeartbeat</c>），维持点动状态。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> JogHeartbeat()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/jogHeartbeat", string.Empty);
        }

        /// <summary>
        /// 运动到预设/规划位置（指令：<c>Robot/moveTo</c>）。类型为 <see cref="MoveToKind.JointPlanned"/> 或 <see cref="MoveToKind.LinePlanned"/> 时必须提供 <paramref name="target"/>。
        /// 启动后须每约 <see cref="RobotMotionHeartbeat.RecommendedIntervalMilliseconds"/> ms 调用 <see cref="MoveToHeartbeat"/>。
        /// </summary>
        /// <param name="kind">moveTo 类型；不同值代表回零、回安全点、规划关节/直线等控制器内置行为。</param>
        /// <param name="target">规划目标点；当 <paramref name="kind"/> 为 <see cref="MoveToKind.JointPlanned"/> 或 <see cref="MoveToKind.LinePlanned"/> 时必须提供。请用 <see cref="MoveToTarget.Joint"/> 或 <see cref="MoveToTarget.Cartesian"/> 构造。</param>
        /// <returns>控制器返回的响应对象；仅表示请求被接收，运动完成需结合状态/心跳判断。</returns>
        /// <exception cref="ArgumentException">需要目标点但未提供，或目标点不包含任何有效位置字段。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> MoveTo(MoveToKind kind, MoveToTarget? target = null)
        {
            int currentId = NextId();
            object db;
            if (kind is MoveToKind.JointPlanned or MoveToKind.LinePlanned)
            {
                if (target == null)
                {
                    throw new ArgumentException(
                        "type 为 JointPlanned(4) 或 LinePlanned(5) 时必须提供 target。",
                        nameof(target));
                }

                var t = new Dictionary<string, object>();
                if (target.Cp != null)
                {
                    t["cp"] = target.Cp;
                }

                if (target.Jp != null)
                {
                    t["jp"] = target.Jp;
                }

                if (target.Ep != null)
                {
                    t["ep"] = target.Ep;
                }

                if (t.Count == 0)
                {
                    throw new ArgumentException("target 至少包含 cp、jp 或 ep 之一。", nameof(target));
                }

                db = new { type = (int)kind, target = t };
            }
            else
            {
                db = new { type = (int)kind };
            }

            return await _TcpClient.SendCommand(currentId, "Robot/moveTo", db);
        }

        /// <summary>
        /// moveTo 心跳（指令：<c>Robot/moveToHeartbeat</c>），维持 RunTo/moveTo 运动。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> MoveToHeartbeat()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/moveToHeartbeat", string.Empty);
        }

        /// <summary>
        /// 停止 RunTo / moveTo（指令：<c>Robot/moveTo</c>，<c>db.type = -1</c>）。
        /// 与 <see cref="StopRobotMove"/> 区分：本方法仅发送 moveTo 停止信号。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StopMoveTo()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(
                currentId,
                "Robot/moveTo",
                new { type = (int)MoveToKind.Stop });
        }

        /// <summary>
        /// 设置手动运动倍率（指令：<c>Robot/setManualMoveRate</c>），<paramref name="percent"/> 为 1~100。
        /// </summary>
        /// <param name="percent">手动倍率百分比，范围 1~100。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> 不在 1~100。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetManualMoveRate(int percent)
        {
            RobotMotionValidation.ValidateMoveRatePercent(percent);
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/setManualMoveRate", percent);
        }

        /// <summary>
        /// 设置自动运动倍率（指令：<c>Robot/setAutoMoveRate</c>），<paramref name="percent"/> 为 1~100。
        /// </summary>
        /// <param name="percent">自动倍率百分比，范围 1~100。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> 不在 1~100。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetAutoMoveRate(int percent)
        {
            RobotMotionValidation.ValidateMoveRatePercent(percent);
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/setAutoMoveRate", percent);
        }

        /// <summary>
        /// 设置碰撞检测灵敏度（指令：<c>Robot/setCollisionSensitivity</c>）。需固件 2.3.2.10+；<paramref name="sensitivity"/> 为 0~100；成功时响应 <c>db</c> 为布尔。
        /// </summary>
        /// <param name="sensitivity">碰撞检测灵敏度，范围 0~100。</param>
        /// <returns>控制器返回的响应对象；协议成功示例中 <see cref="CommonResponse.db"/> 为布尔值。</returns>
        /// <exception cref="ArgumentException"><paramref name="sensitivity"/> 不在 0~100。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetCollisionSensitivity(int sensitivity)
        {
            RobotMotionValidation.ValidateCollisionSensitivity(sensitivity);
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/setCollisionSensitivity", sensitivity);
        }

        /// <summary>
        /// 设置负载（指令：<c>Robot/setPayload</c>）。需固件 2.3.2.10+；<paramref name="payloadId"/> 为 0~15；成功时响应 <c>db</c> 可能为 null。
        /// </summary>
        /// <param name="payloadId">负载编号，范围 0~15。</param>
        /// <returns>控制器返回的响应对象；协议成功示例中 <see cref="CommonResponse.db"/> 为 null。</returns>
        /// <exception cref="ArgumentException"><paramref name="payloadId"/> 不在 0~15。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> SetPayload(int payloadId)
        {
            RobotMotionValidation.ValidatePayloadId(payloadId);
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/setPayload", payloadId);
        }

        /// <summary>
        /// 获取设置界面参数。优先新接口（<c>Robot/getTools</c> + <c>Robot/getCoordinates</c> + <c>Robot/getPayloads</c>），
        /// 404 时回退旧接口（<c>Robot/GetRobotParameter</c>，协议 19.7）。
        /// </summary>
        public async Task<RobotParameters> GetRobotParameters()
        {
            // 新接口：getTools
            try
            {
                var toolResp = await _TcpClient.SendCommand(NextId(), "Robot/getTools", string.Empty);
                var (defaultToolId, tool) = RobotSettingsSerialization.ParseToolsFromDb(toolResp.db);

                // 新接口：getCoordinates
                int defaultCoordId = 0;
                List<RobotFrame> coordinate = new();
                try
                {
                    var coordResp = await _TcpClient.SendCommand(NextId(), "Robot/getCoordinates", string.Empty);
                    (defaultCoordId, coordinate) = RobotSettingsSerialization.ParseCoordinatesFromDb(coordResp.db);
                }
                catch (CodroidCommandException ex) when (Is404(ex)) { /* getCoordinates 不可用，保持空 */ }

                // 新接口：getPayloads
                int defaultPayloadId = 0;
                double maxPayload = 0;
                List<RobotPayloadFrame> payload = new();
                try
                {
                    var payloadResp = await _TcpClient.SendCommand(NextId(), "Robot/getPayloads", string.Empty);
                    (defaultPayloadId, maxPayload, payload) = RobotSettingsSerialization.ParsePayloadsFromDb(payloadResp.db);
                }
                catch (CodroidCommandException ex) when (Is404(ex)) { /* getPayloads 不可用，保持空 */ }

                return RobotSettingsSerialization.BuildFromNewApi(
                    defaultToolId, tool, defaultCoordId, coordinate,
                    defaultPayloadId, maxPayload, payload);
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                // 回退旧接口
                var response = await _TcpClient.SendCommand(NextId(), "Robot/GetRobotParameter", string.Empty);
                return RobotSettingsSerialization.ParseFromDb(response.db);
            }
        }

        /// <summary>
        /// 设置默认负载编号。<paramref name="payloadId"/> 为 0~15。
        /// 内部转发至 <see cref="SetPayload"/>（<c>Robot/setPayload</c>，新旧接口均有）。
        /// </summary>
        public Task<CommonResponse> SetDefaultPayloadId(int payloadId)
        {
            RobotSettingsValidation.ValidateDefaultSlotId(payloadId, nameof(payloadId));
            return SetPayload(payloadId);
        }

        /// <summary>
        /// 设置默认工具坐标系编号。<paramref name="toolId"/> 为 0~15。
        /// 优先新接口 <c>Robot/setDefaultTool</c>，404 时回退旧接口 <c>Robot/SaveRobotParameter</c>。
        /// </summary>
        public async Task<CommonResponse> SetDefaultToolId(int toolId)
        {
            RobotSettingsValidation.ValidateDefaultSlotId(toolId, nameof(toolId));
            try
            {
                return await _TcpClient.SendCommand(NextId(), "Robot/setDefaultTool", toolId);
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                return await SendSaveRobotParameter(RobotSettingsSerialization.BuildDefaultToolIdDb(toolId));
            }
        }

        /// <summary>
        /// 设置默认用户坐标系编号。<paramref name="coordinateId"/> 为 0~15。
        /// 优先新接口 <c>Robot/setDefaultCoordinate</c>，404 时回退旧接口 <c>Robot/SaveRobotParameter</c>。
        /// </summary>
        public async Task<CommonResponse> SetDefaultUserCoordinateId(int coordinateId)
        {
            RobotSettingsValidation.ValidateDefaultSlotId(coordinateId, nameof(coordinateId));
            try
            {
                return await _TcpClient.SendCommand(NextId(), "Robot/setDefaultCoordinate", coordinateId);
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                return await SendSaveRobotParameter(
                    RobotSettingsSerialization.BuildDefaultCoordinateIdDb(coordinateId));
            }
        }

        /// <summary>
        /// 下发完整工具坐标系表。须包含 id 0~15；<b>id=0 项必须保持全零</b>。
        /// 优先新接口 <c>Robot/setTools</c>（db 含 defaultToolId + Tool），404 时回退旧接口 <c>Robot/SaveRobotParameter</c>。
        /// </summary>
        public async Task<CommonResponse> SaveToolFrames(IReadOnlyList<RobotFrame> frames)
        {
            RobotSettingsValidation.ValidateToolFramesForSave(frames, nameof(frames));
            try
            {
                // 新接口需附带 defaultToolId，从 frames 中取第一个非零 id 或默认 0
                int defaultToolId = frames.FirstOrDefault(f => f.Id != 0)?.Id ?? 0;
                var db = RobotSettingsSerialization.BuildSetToolsDb(defaultToolId, frames);
                Console.WriteLine($"[DEBUG] setTools 发送 JSON: {System.Text.Json.JsonSerializer.Serialize(db)}");
                return await _TcpClient.SendCommand(NextId(), "Robot/setTools", db);
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                return await SendSaveRobotParameter(RobotSettingsSerialization.BuildToolDb(frames));
            }
        }

        /// <summary>
        /// 修改单个工具坐标系（先 <see cref="GetRobotParameters"/> 再保存）。<paramref name="frameId"/> 仅允许 1~15。
        /// </summary>
        public async Task<CommonResponse> SetToolFrame(int frameId, RobotFrame frame)
        {
            RobotSettingsValidation.ValidateWritableFrameId(frameId, nameof(frameId));
            RobotSettingsValidation.ValidateFrameIdMatches(frameId, frame);

            var current = await GetRobotParameters().ConfigureAwait(false);
            var merged = RobotSettingsSerialization.MergeToolFrame(current.Tool, frameId, frame);
            RobotSettingsValidation.ValidateToolFramesForSave(merged, nameof(merged));
            return await SaveToolFrames(merged).ConfigureAwait(false);
        }

        /// <summary>
        /// 下发完整负载坐标系表。须包含 id 0~15；<b>id=0 项必须保持全零</b>。
        /// 优先新接口 <c>Robot/setPayloads</c>（db 含 defaultPayloadId + maxPayload + Payload），404 时回退旧接口。
        /// </summary>
        public async Task<CommonResponse> SavePayloadFrames(IReadOnlyList<RobotPayloadFrame> frames)
        {
            RobotSettingsValidation.ValidatePayloadFramesForSave(frames, nameof(frames));
            try
            {
                // 新接口需附带 defaultPayloadId 和 maxPayload，从当前参数读取
                int defaultPayloadId = frames.FirstOrDefault(f => f.Id != 0)?.Id ?? 0;
                double maxPayload = 0;
                try
                {
                    var current = await GetRobotParameters().ConfigureAwait(false);
                    defaultPayloadId = current.DefaultPayloadId;
                    maxPayload = current.MaxPayload;
                }
                catch { /* 读取失败时使用默认值 */ }

                return await _TcpClient.SendCommand(
                    NextId(), "Robot/setPayloads",
                    RobotSettingsSerialization.BuildSetPayloadsDb(defaultPayloadId, maxPayload, frames));
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                return await SendSaveRobotParameter(RobotSettingsSerialization.BuildPayloadDb(frames));
            }
        }

        /// <summary>
        /// 修改单个负载坐标系（先读后改）。<paramref name="frameId"/> 仅允许 1~15。
        /// 优先新接口 <c>Robot/setPayloads</c>，404 时回退旧接口。
        /// </summary>
        public async Task<CommonResponse> SetPayloadFrame(int frameId, RobotPayloadFrame frame)
        {
            RobotSettingsValidation.ValidateWritableFrameId(frameId, nameof(frameId));
            RobotSettingsValidation.ValidateFrameIdMatches(frameId, frame);

            var current = await GetRobotParameters().ConfigureAwait(false);
            var merged = RobotSettingsSerialization.MergePayloadFrame(current.Payload, frameId, frame);
            RobotSettingsValidation.ValidatePayloadFramesForSave(merged, nameof(merged));

            try
            {
                return await _TcpClient.SendCommand(
                    NextId(), "Robot/setPayloads",
                    RobotSettingsSerialization.BuildSetPayloadsDb(
                        current.DefaultPayloadId, current.MaxPayload, merged));
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                return await SendSaveRobotParameter(RobotSettingsSerialization.BuildPayloadDb(merged))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 下发完整用户坐标系表。须包含 id 0~15；<b>id=0 项必须保持全零</b>。
        /// 优先新接口 <c>Robot/setCoordinates</c>（db 含 defaultCoordinateId + Coordinate），404 时回退旧接口。
        /// </summary>
        public async Task<CommonResponse> SaveUserCoordinateFrames(IReadOnlyList<RobotFrame> frames)
        {
            RobotSettingsValidation.ValidateToolFramesForSave(frames, nameof(frames));
            try
            {
                int defaultCoordId = frames.FirstOrDefault(f => f.Id != 0)?.Id ?? 0;
                var db = RobotSettingsSerialization.BuildSetCoordinatesDb(defaultCoordId, frames);
                Console.WriteLine($"[DEBUG] setCoordinates 发送 JSON: {System.Text.Json.JsonSerializer.Serialize(db)}");
                return await _TcpClient.SendCommand(NextId(), "Robot/setCoordinates", db);
            }
            catch (CodroidCommandException ex) when (Is404(ex))
            {
                return await SendSaveRobotParameter(RobotSettingsSerialization.BuildCoordinateDb(frames));
            }
        }

        /// <summary>
        /// 修改单个用户坐标系（先读后改）。<paramref name="frameId"/> 仅允许 1~15。
        /// </summary>
        public async Task<CommonResponse> SetUserCoordinateFrame(int frameId, RobotFrame frame)
        {
            RobotSettingsValidation.ValidateWritableFrameId(frameId, nameof(frameId));
            RobotSettingsValidation.ValidateFrameIdMatches(frameId, frame);

            var current = await GetRobotParameters().ConfigureAwait(false);
            var merged = RobotSettingsSerialization.MergeCoordinateFrame(current.Coordinate, frameId, frame);
            RobotSettingsValidation.ValidateToolFramesForSave(merged, nameof(merged));
            return await SaveUserCoordinateFrames(merged).ConfigureAwait(false);
        }

        private Task<CommonResponse> SendSaveRobotParameter(object db) =>
            _TcpClient.SendCommand(NextId(), "Robot/SaveRobotParameter", db);

        /// <summary>判断控制器错误是否为 404（接口不存在）。</summary>
        private static bool Is404(CodroidCommandException ex) =>
            ex.ControllerError?.Contains("404") == true
            || ex.Message.Contains("404");

        /// <summary>
        /// 单段关节 <c>movJ</c>（指令：<c>Robot/move</c>，<c>targetPoint.jp</c>）。
        /// </summary>
        public Task<CommonResponse> MovJ(
            JointPoint target,
            double speed,
            double acc,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null) =>
            Move(new[]
            {
                MoveInstruction.MovJ(target, speed, acc, blend, coor, tool, relativeBlend)
            });

        /// <summary>
        /// 单段笛卡尔 <c>movJ</c>（<c>targetPoint.cp</c> + <c>rj</c>；未设 <c>rj</c> 时打包为默认参考关节）。
        /// </summary>
        public Task<CommonResponse> MovJ(
            CartesianPoint target,
            double speed,
            double acc,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null) =>
            Move(new[]
            {
                MoveInstruction.MovJ(target, speed, acc, blend, coor, tool, relativeBlend)
            });

        /// <summary>
        /// 单段笛卡尔 <c>movL</c>（<c>targetPoint.cp</c> + <c>rj</c>）。
        /// </summary>
        public Task<CommonResponse> MovL(
            CartesianPoint target,
            double speed,
            double acc,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null) =>
            Move(new[]
            {
                MoveInstruction.MovL(target, speed, acc, blend, coor, tool, relativeBlend)
            });

        /// <summary>
        /// 单段关节 <c>movL</c>（<c>targetPoint.jp</c>）。
        /// </summary>
        public Task<CommonResponse> MovL(
            JointPoint target,
            double speed,
            double acc,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null) =>
            Move(new[]
            {
                MoveInstruction.MovL(target, speed, acc, blend, coor, tool, relativeBlend)
            });

        /// <summary>
        /// 单段笛卡尔 <c>movC</c>（中间点与目标点均为 TCP）。
        /// </summary>
        public Task<CommonResponse> MovC(
            CartesianPoint middle,
            CartesianPoint target,
            double speed,
            double acc,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null) =>
            Move(new[]
            {
                MoveInstruction.MovC(middle, target, speed, acc, blend, coor, tool, relativeBlend)
            });

        /// <summary>
        /// 单段笛卡尔 <c>movCircle</c>（中间点与目标点均为 TCP）。
        /// </summary>
        public Task<CommonResponse> MovCircle(
            CartesianPoint middle,
            CartesianPoint target,
            int circleNum,
            double speed,
            double acc,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null) =>
            Move(new[]
            {
                MoveInstruction.MovCircle(
                    middle, target, circleNum, speed, acc, blend, coor, tool, relativeBlend)
            });

        /// <summary>
        /// 下发运动指令列表（指令：<c>Robot/move</c>）。不要设置空的 <c>coor</c>/<c>tool</c> 数组。
        /// </summary>
        /// <param name="instructions">一条或多条运动指令；每条至少包含目标点，圆弧类还需中间点。</param>
        /// <returns>控制器返回的响应对象；仅表示控制器接收指令，运动完成需另行观察状态。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="instructions"/> 为 null。</exception>
        /// <exception cref="ArgumentException">指令列表为空、目标点缺失或参数不符合运动类型要求。</exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> Move(IReadOnlyList<MoveInstruction> instructions)
        {
            Polyfills.ThrowIfNull(instructions);
            JsonElement payload = MotionCommandJson.SerializeMoveInstructions(instructions);
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/move", payload);
        }

        /// <summary>
        /// 阻塞下发路径，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；完成条件为曾运动且连续 <see cref="MotionWaitOptions.SettledSamples"/> 次 <c>InMotion=false</c>，不比对关节/TCP 位置。</remarks>
        public bool MoveSync(IReadOnlyList<MoveInstruction> instructions, MotionWaitOptions? wait = null)
        {
            Move(instructions).ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            // 从最后一条指令提取目标用于容差判断
            double[]? targetJp = null;
            double[]? targetCp = null;
            if (instructions.Count > 0)
            {
                var last = instructions[instructions.Count - 1];
                if (last.TargetPoint?.Jp != null && last.TargetPoint.Jp.Length >= 6)
                    targetJp = last.TargetPoint.Jp;
                else if (last.TargetPoint?.Cp != null && last.TargetPoint.Cp.Length >= 6)
                    targetCp = last.TargetPoint.Cp;
            }
            WaitUntilSettledByCri("move(path)", options, targetJp: targetJp, targetCp: targetCp);
            return true;
        }

        /// <summary>
        /// 阻塞下发单段关节 <c>movJ</c>，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；不比对关节/TCP 位置。</remarks>
        public bool MovJSync(
            JointPoint target,
            double speed,
            double acc,
            MotionWaitOptions? wait = null,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null)
        {
            MovJ(target, speed, acc, blend, coor, tool, relativeBlend)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            WaitUntilSettledByCri("movJ(JointPoint)", options, targetJp: target.Jp);
            return true;
        }

        /// <summary>
        /// 阻塞下发单段笛卡尔 <c>movJ</c>，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；支持容差前置判断（UseTolerance=True 时）。</remarks>
        public bool MovJSync(
            CartesianPoint target,
            double speed,
            double acc,
            MotionWaitOptions? wait = null,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null)
        {
            MovJ(target, speed, acc, blend, coor, tool, relativeBlend)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            WaitUntilSettledByCri("movJ(CartesianPoint)", options, targetCp: target.Cp);
            return true;
        }

        /// <summary>
        /// 阻塞下发单段笛卡尔 <c>movL</c>，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；不比对关节/TCP 位置。</remarks>
        public bool MovLSync(
            CartesianPoint target,
            double speed,
            double acc,
            MotionWaitOptions? wait = null,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null)
        {
            MovL(target, speed, acc, blend, coor, tool, relativeBlend)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            WaitUntilSettledByCri("movL(CartesianPoint)", options, targetCp: target.Cp);
            return true;
        }

        /// <summary>
        /// 阻塞下发单段关节 <c>movL</c>，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；支持容差前置判断（UseTolerance=True 时）。</remarks>
        public bool MovLSync(
            JointPoint target,
            double speed,
            double acc,
            MotionWaitOptions? wait = null,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null)
        {
            MovL(target, speed, acc, blend, coor, tool, relativeBlend)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            WaitUntilSettledByCri("movL(JointPoint)", options, targetJp: target.Jp);
            return true;
        }

        /// <summary>
        /// 阻塞下发单段 <c>movC</c>，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；不比对关节/TCP 位置。</remarks>
        public bool MovCSync(
            CartesianPoint middle,
            CartesianPoint target,
            double speed,
            double acc,
            MotionWaitOptions? wait = null,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null)
        {
            MovC(middle, target, speed, acc, blend, coor, tool, relativeBlend)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            WaitUntilSettledByCri("movC", options, targetCp: target.Cp);
            return true;
        }

        /// <summary>
        /// 阻塞下发单段 <c>movCircle</c>，等待 CRI <c>InMotion</c> 停稳后返回 <c>true</c>。
        /// </summary>
        /// <remarks>须先 <see cref="StartCriDataPush"/>；不比对关节/TCP 位置。</remarks>
        public bool MovCircleSync(
            CartesianPoint middle,
            CartesianPoint target,
            int circleNum,
            double speed,
            double acc,
            MotionWaitOptions? wait = null,
            double? blend = null,
            double[]? coor = null,
            double[]? tool = null,
            double? relativeBlend = null)
        {
            MovCircle(middle, target, circleNum, speed, acc, blend, coor, tool, relativeBlend)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var options = GetWait(wait);
            WaitUntilSettledByCri("movCircle", options, targetCp: target.Cp);
            return true;
        }

        /// <summary>
        /// 暂停当前运动（指令：<c>Robot/pause</c>）；与工程级 <c>project/pause</c> 不同。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> PauseRobotMotion()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/pause", string.Empty);
        }

        /// <summary>
        /// 恢复运动（指令：<c>Robot/resume</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> ResumeRobotMotion()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/resume", string.Empty);
        }

        /// <summary>
        /// 停止运动（指令：<c>Robot/stopMove</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StopRobotMove()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "Robot/stopMove", string.Empty);
        }

        /// <summary>
        /// 在本地指定地址与端口启动 UDP 监听，并向控制器请求开始向该端点推送 CRI 实时数据（指令：<c>CRI/StartDataPush</c>）。
        /// 推送参数在本客户端内固定为：周期 100 ms、高精度、mask 0xFFFF，对应 308 字节 UDP 包（六轴、无附加轴）。
        /// </summary>
        /// <param name="udpIp">本机用于绑定与告知控制器的 IPv4 地址（须与控制器路由可达）。</param>
        /// <param name="udpPort">本机 UDP 监听端口。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        /// <remarks>若 TCP 指令失败，会在抛出异常前关闭已启动的本地 UDP 监听。</remarks>
        public async Task<CommonResponse> StartCriDataPush(string udpIp, int udpPort)
        {
            var localIp = IPAddress.Parse(udpIp);
            await StartCriUdpListener(localIp, udpPort);

            int currentId = NextId();
            var data = new
            {
                ip = udpIp,
                port = udpPort,
                duration = CriDurationMsFixed,
                highPercision = CriHighPrecisionFixed,
                mask = CriMaskFixed
            };

            try
            {
                return await _TcpClient.SendCommand(currentId, "CRI/StartDataPush", data);
            }
            catch
            {
                StopCriUdpListener();
                throw;
            }
        }

        /// <summary>
        /// 请求控制器停止 CRI 数据推送（指令：<c>CRI/StopDataPush</c>），并始终关闭本地 UDP 监听。
        /// </summary>
        /// <param name="udpIp">可选；若与 <paramref name="udpPort"/> 同时提供，则作为停止推送的目标地址参数传入协议。</param>
        /// <param name="udpPort">可选；与 <paramref name="udpIp"/> 配对使用。</param>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        /// <remarks>无论 TCP 指令是否成功，本地 UDP 监听都会在方法结束时关闭。</remarks>
        public async Task<CommonResponse> StopCriDataPush(string? udpIp = null, int? udpPort = null)
        {
            int currentId = NextId();
            object data = (string.IsNullOrEmpty(udpIp) || !udpPort.HasValue)
                ? new { }
                : new { ip = udpIp, port = udpPort.Value };

            try
            {
                return await _TcpClient.SendCommand(currentId, "CRI/StopDataPush", data);
            }
            finally
            {
                StopCriUdpListener();
            }
        }

        /// <summary>
        /// 开启 CRI 实时控制（指令：<c>CRI/StartControl</c>）。
        /// </summary>
        /// <param name="filterType">滤波类型：0=关闭，1=平均滤波，2=二阶低通，3=椭圆滤波。推荐 1。</param>
        /// <param name="durationMs">指令间隔（毫秒），范围 1~16，且须能整除 1000（如 1/2/4/5/8/10）。推荐 4。</param>
        /// <param name="startBuffer">启动缓冲点数量，范围 1~100。推荐 5。</param>
        /// <returns>控制器返回的响应对象。启动后应通过 <see cref="CriData"/> 等待 <see cref="CriRealTimeData.RealTimeControlMode"/> 变为 true 再下发 UDP 命令帧。</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="filterType"/> 不在 0~3，或 <paramref name="durationMs"/> 不在 1~16 / 不能整除 1000，或 <paramref name="startBuffer"/> 不在 1~100。
        /// </exception>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StartCriControl(
            int filterType = CriControlFilterTypeRecommended,
            int durationMs = CriControlDurationRecommendedMs,
            int startBuffer = CriControlStartBufferRecommended)
        {
            if (filterType is < 0 or > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(filterType), "filterType 仅支持 0~3。");
            }

            if (durationMs is < CriControlDurationMinMs or > CriControlDurationMaxMs)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationMs),
                    $"durationMs 须在 {CriControlDurationMinMs}~{CriControlDurationMaxMs}（ms）。");
            }
            if (1000 % durationMs != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationMs),
                    "durationMs 必须能整除 1000（建议值：1、2、4、5、8、10）。");
            }

            if (startBuffer is < CriControlStartBufferMin or > CriControlStartBufferMax)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startBuffer),
                    $"startBuffer 须在 {CriControlStartBufferMin}~{CriControlStartBufferMax}。");
            }

            int currentId = NextId();
            var data = new
            {
                filterType,
                duration = durationMs,
                startBuffer
            };
            return await _TcpClient.SendCommand(currentId, "CRI/StartControl", data);
        }

        /// <summary>
        /// 关闭 CRI 实时控制（指令：<c>CRI/StopControl</c>）。
        /// </summary>
        /// <returns>控制器返回的响应对象。</returns>
        /// <exception cref="InvalidOperationException">尚未连接 TCP，或响应无法反序列化。</exception>
        /// <exception cref="TimeoutException">等待响应超时（10 秒）。</exception>
        /// <exception cref="CodroidCommandException">控制器报错或其它执行失败。</exception>
        public async Task<CommonResponse> StopCriControl()
        {
            int currentId = NextId();
            return await _TcpClient.SendCommand(currentId, "CRI/StopControl", string.Empty);
        }

        /// <summary>
        /// 停止已有 UDP 任务后，在指定本地终结点上绑定 <see cref="UdpClient"/> 并启动后台接收循环。
        /// </summary>
        /// <param name="localIp">本机绑定 IP。</param>
        /// <param name="localPort">本机绑定端口。</param>
        /// <returns>表示监听初始化完成的任务。</returns>
        private async Task StartCriUdpListener(IPAddress localIp, int localPort)
        {
            StopCriUdpListener();

            _criUdpCts = new CancellationTokenSource();
            _criUdpClient = new UdpClient(new IPEndPoint(localIp, localPort));
            _criUdpTask = Task.Run(() => CriUdpReceiveLoop(_criUdpCts.Token));
            await Task.CompletedTask;
        }

        private static MotionWaitOptions GetWait(MotionWaitOptions? wait) => wait ?? new MotionWaitOptions();

        private void EnsureCriFresh(MotionWaitOptions options, string opName)
        {
            long ticks = Interlocked.Read(ref _lastCriReceivedUtcTicks);
            if (ticks <= 0)
            {
                throw new InvalidOperationException(
                    $"{opName} 等待完成失败：尚未收到 CRI 数据。请先 StartCriDataPush 并确认回传正常。");
            }

            var last = new DateTime(ticks, DateTimeKind.Utc);
            var age = DateTime.UtcNow - last;
            if (age > options.CriStaleTimeout)
            {
                throw new TimeoutException(
                    $"{opName} 等待完成失败：CRI 数据陈旧 {age.TotalMilliseconds:F0}ms，阈值 {options.CriStaleTimeout.TotalMilliseconds:F0}ms。");
            }
        }

        /// <summary>
        /// 轮询 CRI，直到曾检测到 <c>InMotion</c> 且连续 <see cref="MotionWaitOptions.SettledSamples"/> 次为停止。
        /// 不比对关节角或 TCP 位姿与目标点的误差。
        /// </summary>
        private void WaitUntilSettledByCri(string opName, MotionWaitOptions options, double[]? targetJp = null, double[]? targetCp = null)
        {
            if (options.SettledSamples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "SettledSamples 必须大于 0。");
            }
            if (options.PollInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "PollInterval 必须大于 0。");
            }

            // 容差前置判断：如果目标已在容差范围内，直接返回
            if (options.UseTolerance)
            {
                EnsureCriFresh(options, opName);
                var snapshot = CriData;
                if (IsTargetReached(snapshot, targetJp, targetCp, options))
                {
                    return; // 目标已在容差内，短路返回
                }
            }

            var sw = Stopwatch.StartNew();
            int settled = 0;
            bool hadMotion = false;
            bool motionStarted = false;
            while (sw.Elapsed <= options.Timeout)
            {
                EnsureCriFresh(options, opName);
                var snapshot = CriData;

                if (snapshot.InMotion)
                {
                    hadMotion = true;
                    motionStarted = true;
                }

                if (snapshot.CollisionStopped || snapshot.EmergencyStopPressed || snapshot.HasAlarm)
                {
                    throw new InvalidOperationException(
                        $"{opName} 失败：检测到异常状态（CollisionStopped={snapshot.CollisionStopped}, " +
                        $"EmergencyStopPressed={snapshot.EmergencyStopPressed}, HasAlarm={snapshot.HasAlarm}）。");
                }

                // 启动超时检测：如果 InMotion 在 MotionStartTimeout 内从未变为 true，直接报错
                if (!motionStarted && sw.Elapsed >= options.MotionStartTimeout)
                {
                    throw new InvalidOperationException(
                        $"{opName} 失败：机器人未启动运动（InMotion 在 {options.MotionStartTimeout.TotalSeconds:F1}s 内从未为 True）。" +
                        $"目标可能无法到达或控制器未响应。" +
                        $"最后状态: jp=[{string.Join(", ", snapshot.JointPosition.Select(v => v.ToString("F3")))}]");
                }

                if (hadMotion && !snapshot.InMotion)
                {
                    settled++;
                    if (settled >= options.SettledSamples)
                    {
                        return;
                    }
                }
                else if (snapshot.InMotion)
                {
                    settled = 0;
                }

                Thread.Sleep(options.PollInterval);
            }

            // 整体超时
            var tailData = CriData;
            throw new TimeoutException(
                $"{opName} 等待完成超时（{options.Timeout.TotalSeconds:F1}s）。最后状态: InMotion={tailData.InMotion}, " +
                $"HadMotion={hadMotion}, jp=[{string.Join(", ", tailData.JointPosition.Select(v => v.ToString("F3")))}]");
        }

        /// <summary>检查目标是否在容差范围内。</summary>
        private bool IsTargetReached(CriRealTimeData snapshot, double[]? targetJp, double[]? targetCp, MotionWaitOptions options)
        {
            // 关节目标判断
            if (targetJp != null && targetJp.Length >= 6 && snapshot.JointPosition.Length >= 6)
            {
                double maxDiff = 0;
                for (int i = 0; i < 6; i++)
                {
                    maxDiff = Math.Max(maxDiff, Math.Abs(snapshot.JointPosition[i] - targetJp[i]));
                }
                return maxDiff <= options.JointToleranceDeg;
            }

            // 笛卡尔目标判断
            if (targetCp != null && targetCp.Length >= 6 && snapshot.TcpPose.Length >= 6)
            {
                // 位置误差（欧氏距离，mm）
                double dx = snapshot.TcpPose[0] - targetCp[0];
                double dy = snapshot.TcpPose[1] - targetCp[1];
                double dz = snapshot.TcpPose[2] - targetCp[2];
                double posErr = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                // 姿态误差（最大角度差，度）
                double oriErr = 0;
                for (int i = 3; i < 6; i++)
                {
                    double diff = Math.Abs(snapshot.TcpPose[i] - targetCp[i]);
                    diff = diff % 360;
                    if (diff > 180) diff = 360 - diff;
                    oriErr = Math.Max(oriErr, diff);
                }

                return posErr <= options.CartesianPositionToleranceMm && oriErr <= options.CartesianOrientationToleranceDeg;
            }

            return false;
        }

        /// <summary>
        /// 取消 UDP 接收、释放套接字与相关任务引用。
        /// </summary>
        private void StopCriUdpListener()
        {
            try
            {
                _criUdpCts?.Cancel();
                _criUdpClient?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRI UDP关闭异常] {ex.Message}");
            }
            finally
            {
                _criUdpClient = null;
                Interlocked.Exchange(ref _lastCriReceivedUtcTicks, 0);
                _criUdpCts?.Dispose();
                _criUdpCts = null;
                _criUdpTask = null;
            }
        }

        /// <summary>
        /// 循环接收 UDP 数据报；仅处理长度等于 <see cref="CriRealtimePacketParser.PacketLength"/> 的包，解析后更新内部缓存并触发 <see cref="CriDataReceived"/>。
        /// </summary>
        /// <param name="token">取消令牌；取消时退出循环。</param>
        /// <returns>表示接收循环生命周期的任务。</returns>
        private async Task CriUdpReceiveLoop(CancellationToken token)
        {
            if (_criUdpClient == null)
            {
                return;
            }

#if NET462
            using var reg = token.Register(() =>
            {
                try { _criUdpClient?.Close(); } catch { }
            });
#endif

            while (!token.IsCancellationRequested)
            {
                try
                {
#if NET462
                    UdpReceiveResult received = await _criUdpClient.ReceiveAsync();
#else
                    UdpReceiveResult received = await _criUdpClient.ReceiveAsync(token);
#endif
                    byte[] packet = received.Buffer;
                    if (packet.Length != CriRealtimePacketParser.PacketLength)
                    {
                        continue;
                    }

                    var parsed = CriRealtimePacketParser.Parse(packet);
                    CriRealTimeData snapshot;
                    lock (_criDataLock)
                    {
                        _criData.UpdateFrom(parsed);
                        Interlocked.Exchange(ref _lastCriReceivedUtcTicks, DateTime.UtcNow.Ticks);
                        snapshot = _criData.Clone();
                    }

                    try
                    {
                        CriDataReceived?.Invoke(snapshot);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CRI回调异常] {ex.Message}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRI UDP接收异常] {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 停止 CRI UDP 监听并断开 TCP 连接。
        /// </summary>
        /// <remarks>
        /// 本方法不向控制器发送停止实时控制命令；若已开启 CRI 实时控制，请先显式调用 <see cref="StopCriControl"/>，
        /// 并按需调用 <see cref="StopCriDataPush"/> 后再断开。
        /// </remarks>
        public void Disconnect()
        {
            try
            {
                StopCriDataPush().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略：多客户端或已断开时不阻塞 Disconnect
            }

            StopCriUdpListener();
            _TcpClient.Disconnect();
        }
    }

}
