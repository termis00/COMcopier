using System.IO.Ports;
using System.Text;
using COMcopier.Models;

namespace COMcopier.Services;

/// <summary>
/// 하나의 소스 COM 포트에서 데이터를 수신하여 여러 대상 COM 포트로 복사합니다.
/// 패턴 미지정 대상: 데이터를 수신 즉시 전달하여 ESC/POS 명령(컷팅 등)을 보존합니다.
/// 패턴 지정 대상: 타임아웃 기반으로 인쇄 작업을 감지한 뒤 패턴 매칭 후 전송합니다.
/// 대상 포트는 쓰기 순간에만 열고 즉시 닫아 다른 프로그램과의 충돌을 방지합니다.
/// </summary>
public class PortCopier : IDisposable
{
    private readonly MappingConfig _config;
    private readonly ILogger _logger;
    private SerialPort? _sourcePort;

    private readonly List<DestinationEntry> _destinations = new();

    private readonly MemoryStream _buffer = new();
    private readonly object _bufferLock = new();
    private readonly object _writeLock = new();
    private Timer? _flushTimer;
    private Timer? _retryTimer;
    private CancellationTokenSource? _cts;

    // 데이터 수신 후 이 시간(ms) 동안 추가 데이터가 없으면 한 건의 인쇄 작업이 끝난 것으로 판단
    private const int FlushTimeoutMs = 500;

    // 소스 포트 연결 재시도 간격 (ms)
    private const int RetryIntervalMs = 5000;

    public string Name => _config.Name;

    private record DestinationEntry(DestinationConfig Config, Encoding Encoding, bool HasPatterns);

    public PortCopier(MappingConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        // euc-kr 등 추가 인코딩 지원을 위해 등록
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        foreach (var dest in _config.Destinations)
        {
            try
            {
                var encoding = Encoding.GetEncoding(dest.Encoding);
                var hasPatterns = dest.Patterns.Count > 0;
                _destinations.Add(new DestinationEntry(dest, encoding, hasPatterns));

                var patternInfo = hasPatterns
                    ? $"Patterns=[{string.Join(", ", dest.Patterns)}]"
                    : "Patterns=(없음-전체전송)";
                var mode = hasPatterns ? "필터모드" : "직접전달";
                _logger.LogInformation("[{Name}] 대상 포트 {Port} 등록됨 ({Mode}, BaudRate={BaudRate}, Copies={Copies}, {PatternInfo})",
                    Name, dest.Port, mode, dest.BaudRate, dest.Copies, patternInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Name}] 대상 포트 {Port} 인코딩 설정 실패 - 건너뜀", Name, dest.Port);
            }
        }

        if (_destinations.Count == 0)
        {
            _logger.LogWarning("[{Name}] 등록된 대상 포트가 없습니다. 데이터는 수신되지만 전달되지 않습니다.", Name);
        }

        TryOpenSourcePort();
    }

    private void TryOpenSourcePort()
    {
        if (_cts?.IsCancellationRequested == true) return;

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
            _logger.LogWarning("[{Name}] 소스 포트 {Port} 열기 실패 - {Seconds}초 후 재시도: {Message}",
                Name, _config.Source.Port, RetryIntervalMs / 1000, ex.Message);

            if (_sourcePort != null)
            {
                _sourcePort.DataReceived -= OnDataReceived;
                try { _sourcePort.Dispose(); } catch { }
                _sourcePort = null;
            }

            _retryTimer?.Dispose();
            _retryTimer = new Timer(_ => TryOpenSourcePort(), null, RetryIntervalMs, Timeout.Infinite);
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

            // 패턴 없는 대상: 수신 즉시 포트를 열고 쓰고 닫음 (ESC/POS 명령 보존)
            lock (_writeLock)
            {
                foreach (var dest in _destinations)
                {
                    if (dest.HasPatterns) continue;

                    try
                    {
                        WriteToPort(dest.Config, data, bytesRead);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{Name}] {Port} 즉시 전송 실패", Name, dest.Config.Port);
                    }
                }
            }

            // 버퍼에 축적 (패턴 대상 전송 + 추가 복사용)
            bool needsBuffering = _destinations.Any(d => d.HasPatterns || d.Config.Copies > 1);

            if (needsBuffering)
            {
                lock (_bufferLock)
                {
                    _buffer.Write(data, 0, bytesRead);
                    _flushTimer?.Dispose();
                    _flushTimer = new Timer(FlushBuffer, null, FlushTimeoutMs, Timeout.Infinite);
                }
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

        _logger.LogInformation("[{Name}] 인쇄 작업 완료 감지: {Bytes} 바이트", Name, data.Length);

        lock (_writeLock)
        {
            foreach (var dest in _destinations)
            {
                if (dest.HasPatterns)
                {
                    // 패턴 지정 대상: 패턴 매칭 후 전송
                    var text = dest.Encoding.GetString(data);
                    var matched = dest.Config.Patterns.FirstOrDefault(p =>
                        text.Contains(p, StringComparison.OrdinalIgnoreCase));

                    if (matched == null)
                    {
                        _logger.LogDebug("[{Name}] {Port} 패턴 불일치 - 전송 건너뜀", Name, dest.Config.Port);
                        continue;
                    }

                    _logger.LogInformation("[{Name}] {Port} 패턴 매칭: \"{Pattern}\"",
                        Name, dest.Config.Port, matched);

                    try
                    {
                        for (int i = 0; i < dest.Config.Copies; i++)
                        {
                            WriteToPort(dest.Config, data, data.Length);

                            if (dest.Config.Copies > 1 && i < dest.Config.Copies - 1)
                                Thread.Sleep(200);
                        }

                        _logger.LogDebug("[{Name}] {Port} 전송 완료 (x{Copies})",
                            Name, dest.Config.Port, dest.Config.Copies);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{Name}] {Port} 전송 실패", Name, dest.Config.Port);
                    }
                }
                else if (dest.Config.Copies > 1)
                {
                    // 직접전달 대상 중 추가 복사가 필요한 경우: 1부는 이미 즉시 전달됨, 나머지분만 전송
                    try
                    {
                        for (int i = 1; i < dest.Config.Copies; i++)
                        {
                            Thread.Sleep(200);
                            WriteToPort(dest.Config, data, data.Length);
                        }

                        _logger.LogDebug("[{Name}] {Port} 추가 복사 전송 완료 (+{Extra}부)",
                            Name, dest.Config.Port, dest.Config.Copies - 1);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{Name}] {Port} 추가 복사 전송 실패", Name, dest.Config.Port);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 포트를 열고, 데이터를 쓰고, 즉시 닫습니다.
    /// 포트 점유 시간을 최소화하여 다른 프로그램과의 충돌을 방지합니다.
    /// </summary>
    private void WriteToPort(SerialPortConfig config, byte[] data, int length)
    {
        using var port = CreateSerialPort(config);
        port.Open();
        port.Write(data, 0, length);
        port.Close();
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
        _cts?.Cancel();
        _retryTimer?.Dispose();
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

        _buffer.Dispose();
        _cts?.Dispose();

        _logger.LogInformation("[{Name}] 포트 복사기 종료됨", Name);
    }
}
