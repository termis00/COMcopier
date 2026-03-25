using System.IO.Ports;
using System.Text;
using COMcopier.Models;

namespace COMcopier.Services;

/// <summary>
/// 하나의 소스 COM 포트에서 데이터를 수신하여 여러 대상 COM 포트로 복사합니다.
/// 타임아웃 기반으로 하나의 인쇄 작업 단위를 감지합니다.
/// </summary>
public class PortCopier : IDisposable
{
    private readonly MappingConfig _config;
    private readonly ILogger _logger;
    private SerialPort? _sourcePort;
    private readonly List<DestinationEntry> _destinationPorts = new();

    private record DestinationEntry(SerialPort Port, int Copies, List<string> Patterns, Encoding Encoding);

    private readonly MemoryStream _buffer = new();
    private readonly object _bufferLock = new();
    private Timer? _flushTimer;

    // 데이터 수신 후 이 시간(ms) 동안 추가 데이터가 없으면 한 건의 인쇄 작업이 끝난 것으로 판단
    private const int FlushTimeoutMs = 500;

    public string Name => _config.Name;

    public PortCopier(MappingConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Start()
    {
        try
        {
            _sourcePort = CreateSerialPort(_config.Source);
            _sourcePort.DataReceived += OnDataReceived;
            _sourcePort.Open();
            _logger.LogInformation("[{Name}] 소스 포트 {Port} 열림 (BaudRate={BaudRate})",
                Name, _config.Source.Port, _config.Source.BaudRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Name}] 소스 포트 {Port} 열기 실패", Name, _config.Source.Port);
            throw;
        }

        // euc-kr 등 추가 인코딩 지원을 위해 등록
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        foreach (var dest in _config.Destinations)
        {
            try
            {
                var port = CreateSerialPort(dest);
                port.Open();
                var encoding = Encoding.GetEncoding(dest.Encoding);
                var entry = new DestinationEntry(port, dest.Copies, dest.Patterns, encoding);
                _destinationPorts.Add(entry);

                var patternInfo = dest.Patterns.Count > 0
                    ? $"Patterns=[{string.Join(", ", dest.Patterns)}]"
                    : "Patterns=(없음-전체전송)";
                _logger.LogInformation("[{Name}] 대상 포트 {Port} 열림 (BaudRate={BaudRate}, Copies={Copies}, {PatternInfo})",
                    Name, dest.Port, dest.BaudRate, dest.Copies, patternInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Name}] 대상 포트 {Port} 열기 실패 - 건너뜀", Name, dest.Port);
            }
        }

        if (_destinationPorts.Count == 0)
        {
            _logger.LogWarning("[{Name}] 열린 대상 포트가 없습니다. 데이터는 수신되지만 전달되지 않습니다.", Name);
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_sourcePort == null || !_sourcePort.IsOpen) return;

        try
        {
            int bytesToRead = _sourcePort.BytesToRead;
            if (bytesToRead <= 0) return;

            byte[] data = new byte[bytesToRead];
            int bytesRead = _sourcePort.Read(data, 0, bytesToRead);

            lock (_bufferLock)
            {
                _buffer.Write(data, 0, bytesRead);

                // 타이머 리셋: 마지막 데이터 수신 후 FlushTimeoutMs 경과 시 전송
                _flushTimer?.Dispose();
                _flushTimer = new Timer(FlushBuffer, null, FlushTimeoutMs, Timeout.Infinite);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Name}] 소스 포트 데이터 수신 중 오류", Name);
        }
    }

    private void FlushBuffer(object? state)
    {
        byte[] data;
        lock (_bufferLock)
        {
            if (_buffer.Length == 0) return;
            data = _buffer.ToArray();
            _buffer.SetLength(0);
        }

        _logger.LogInformation("[{Name}] 인쇄 작업 감지: {Bytes} 바이트", Name, data.Length);

        foreach (var dest in _destinationPorts)
        {
            // 패턴 필터링: Patterns가 설정되어 있으면 하나라도 매칭될 때만 전송
            if (dest.Patterns.Count > 0)
            {
                var text = dest.Encoding.GetString(data);
                var matched = dest.Patterns.FirstOrDefault(p =>
                    text.Contains(p, StringComparison.OrdinalIgnoreCase));

                if (matched == null)
                {
                    _logger.LogDebug("[{Name}] {Port} 패턴 불일치 - 전송 건너뜀", Name, dest.Port.PortName);
                    continue;
                }

                _logger.LogInformation("[{Name}] {Port} 패턴 매칭: \"{Pattern}\"",
                    Name, dest.Port.PortName, matched);
            }

            for (int i = 0; i < dest.Copies; i++)
            {
                try
                {
                    if (dest.Port.IsOpen)
                    {
                        dest.Port.Write(data, 0, data.Length);
                        _logger.LogDebug("[{Name}] {Port} 전송 완료 (복사 {Copy}/{Total})",
                            Name, dest.Port.PortName, i + 1, dest.Copies);
                    }
                    else
                    {
                        _logger.LogWarning("[{Name}] {Port} 포트가 닫혀있어 전송 실패", Name, dest.Port.PortName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Name}] {Port} 전송 실패 (복사 {Copy}/{Total})",
                        Name, dest.Port.PortName, i + 1, dest.Copies);
                }

                // 같은 포트에 여러 번 전송 시 약간의 딜레이
                if (dest.Copies > 1 && i < dest.Copies - 1)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }

    private static SerialPort CreateSerialPort(SerialPortConfig config)
    {
        return new SerialPort
        {
            PortName = config.Port,
            BaudRate = config.BaudRate,
            DataBits = config.DataBits,
            Parity = config.Parity,
            StopBits = config.StopBits,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();

        if (_sourcePort != null)
        {
            _sourcePort.DataReceived -= OnDataReceived;
            if (_sourcePort.IsOpen)
            {
                try { _sourcePort.Close(); } catch { }
            }
            _sourcePort.Dispose();
        }

        foreach (var dest in _destinationPorts)
        {
            if (dest.Port.IsOpen)
            {
                try { dest.Port.Close(); } catch { }
            }
            dest.Port.Dispose();
        }

        _buffer.Dispose();

        _logger.LogInformation("[{Name}] 포트 복사기 종료됨", Name);
    }
}
