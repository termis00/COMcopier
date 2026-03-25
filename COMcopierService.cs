using COMcopier.Models;
using COMcopier.Services;

namespace COMcopier;

public class COMcopierService : BackgroundService
{
    private readonly ILogger<COMcopierService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<PortCopier> _copiers = new();

    public COMcopierService(
        ILogger<COMcopierService> logger,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("COMcopier 서비스 시작 중...");

        // 사용 가능한 COM 포트 목록 출력
        var availablePorts = System.IO.Ports.SerialPort.GetPortNames();
        _logger.LogInformation("사용 가능한 COM 포트: {Ports}",
            availablePorts.Length > 0 ? string.Join(", ", availablePorts) : "(없음)");

        // 설정 로드
        var settings = new CopierSettings();
        _configuration.GetSection("COMcopier").Bind(settings);

        if (settings.Mappings.Count == 0)
        {
            _logger.LogWarning("설정된 포트 매핑이 없습니다. appsettings.json을 확인하세요.");
            return base.StartAsync(cancellationToken);
        }

        // 각 매핑별로 PortCopier 인스턴스 생성 및 시작
        foreach (var mapping in settings.Mappings)
        {
            try
            {
                var copier = new PortCopier(mapping, _loggerFactory.CreateLogger($"PortCopier.{mapping.Name}"));
                copier.Start();
                _copiers.Add(copier);
                _logger.LogInformation("매핑 '{Name}' 시작됨: {Source} → {Destinations}",
                    mapping.Name,
                    mapping.Source.Port,
                    string.Join(", ", mapping.Destinations.Select(d => $"{d.Port}(x{d.Copies})")));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "매핑 '{Name}' 시작 실패", mapping.Name);
            }
        }

        _logger.LogInformation("COMcopier 서비스 시작 완료. 활성 매핑: {Count}/{Total}",
            _copiers.Count, settings.Mappings.Count);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 서비스가 중지될 때까지 대기 (실제 작업은 이벤트 기반으로 PortCopier에서 처리)
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("COMcopier 서비스 종료 중...");

        foreach (var copier in _copiers)
        {
            try
            {
                copier.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "포트 복사기 '{Name}' 종료 중 오류", copier.Name);
            }
        }
        _copiers.Clear();

        _logger.LogInformation("COMcopier 서비스 종료 완료.");
        return base.StopAsync(cancellationToken);
    }
}
