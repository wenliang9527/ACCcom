namespace ACCcom.Core.Services;

/// <summary>
/// HttpService 的配置对象，封装所有服务依赖和配置参数。
/// 解决原构造函数参数过多（11个）的"Long Parameter List"代码坏味道。
/// </summary>
public class HttpServiceOptions
{
    /// <summary>串口服务</summary>
    public ISerialService? SerialService { get; set; }

    /// <summary>解析器管理器</summary>
    public ParserManager? ParserManager { get; set; }

    /// <summary>Modbus 从站服务</summary>
    public ModbusSlaveService? SlaveService { get; set; }

    /// <summary>多端口服务</summary>
    public MultiPortService? MultiPortService { get; set; }

    /// <summary>Modbus 主站服务</summary>
    public ModbusService? ModbusService { get; set; }

    /// <summary>Modbus 连接管理器</summary>
    public ModbusConnectionManager? ModbusConnections { get; set; }

    /// <summary>自动波特率检测器</summary>
    public AutoBaudDetector? AutoBaudDetector { get; set; }

    /// <summary>会话记录器</summary>
    public SessionRecorder? SessionRecorder { get; set; }

    /// <summary>数据统计服务</summary>
    public DataStatistics? DataStatistics { get; set; }

    /// <summary>HTTP 服务监听地址，默认为 <see cref="HttpService.DefaultUrl"/></summary>
    public string Url { get; set; } = HttpService.DefaultUrl;

    /// <summary>数据缓冲区容量，默认 10000</summary>
    public int BufferCapacity { get; set; } = 10000;
}
