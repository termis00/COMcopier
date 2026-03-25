using System.IO.Ports;

namespace COMcopier.Models;

public class CopierSettings
{
    public List<MappingConfig> Mappings { get; set; } = new();
}

public class MappingConfig
{
    public string Name { get; set; } = string.Empty;
    public SerialPortConfig Source { get; set; } = new();
    public List<DestinationConfig> Destinations { get; set; } = new();
}

public class SerialPortConfig
{
    public string Port { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
}

public class DestinationConfig : SerialPortConfig
{
    public int Copies { get; set; } = 1;

    /// <summary>
    /// 이 문자열이 인쇄 데이터에 포함되어 있을 때만 전송합니다.
    /// 비어있으면 모든 데이터를 전송합니다.
    /// 여러 패턴을 설정하면 하나라도 매칭되면 전송합니다 (OR 조건).
    /// </summary>
    public List<string> Patterns { get; set; } = new();

    /// <summary>
    /// 패턴 매칭 시 사용할 인코딩. 기본값은 "euc-kr" (한글 영수증 프린터 일반적).
    /// </summary>
    public string Encoding { get; set; } = "euc-kr";
}
