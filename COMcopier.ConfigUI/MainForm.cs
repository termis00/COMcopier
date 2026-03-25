using System.Diagnostics;
using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;
using COMcopier.Models;

namespace COMcopier.ConfigUI;

public class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private string _configPath = string.Empty;
    private CopierSettings _settings = new();

    // 좌측: 매핑 목록
    private readonly ListBox _mappingList = new();
    private readonly Button _btnAddMapping = new() { Text = "추가" };
    private readonly Button _btnRemoveMapping = new() { Text = "삭제" };

    // 우측 상단: 매핑 이름 + 소스 포트
    private readonly TextBox _txtMappingName = new();
    private readonly ComboBox _cboSourcePort = new();
    private readonly ComboBox _cboSourceBaud = new();

    // 우측 중간: 대상 포트 목록
    private readonly DataGridView _dgvDestinations = new();
    private readonly Button _btnAddDest = new() { Text = "대상 추가" };
    private readonly Button _btnRemoveDest = new() { Text = "대상 삭제" };

    // 우측 하단: 선택한 대상 상세
    private readonly ComboBox _cboDestPort = new();
    private readonly ComboBox _cboDestBaud = new();
    private readonly NumericUpDown _nudCopies = new() { Minimum = 1, Maximum = 99, Value = 1 };
    private readonly TextBox _txtEncoding = new() { Text = "euc-kr" };
    private readonly TextBox _txtPatterns = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    // 하단 바
    private readonly Button _btnSave = new() { Text = "저장" };
    private readonly Button _btnRestart = new() { Text = "서비스 재시작" };
    private readonly Label _lblStatus = new() { AutoSize = true };
    private readonly Button _btnBrowse = new() { Text = "설정파일 열기..." };

    private bool _suppressEvents;

    public MainForm()
    {
        Text = "COMcopier 설정";
        Size = new Size(820, 620);
        MinimumSize = new Size(750, 550);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();
        PopulateComPorts();
        LoadDefaultConfig();
    }

    private void BuildLayout()
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // === 좌측 패널: 매핑 목록 ===
        var leftPanel = new Panel { Dock = DockStyle.Fill };
        var lblMappings = new Label { Text = "매핑 목록", Dock = DockStyle.Top, Font = new Font(Font, FontStyle.Bold), Height = 24 };
        _mappingList.Dock = DockStyle.Fill;

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.LeftToRight };
        leftButtons.Controls.AddRange(new Control[] { _btnAddMapping, _btnRemoveMapping });

        leftPanel.Controls.Add(_mappingList);
        leftPanel.Controls.Add(leftButtons);
        leftPanel.Controls.Add(lblMappings);

        // === 우측 패널 ===
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        var rightScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 0;

        // 설정 파일 경로
        var lblFile = CreateLabel("설정 파일:", 0, y); rightScroll.Controls.Add(lblFile);
        _btnBrowse.Location = new Point(80, y - 2); _btnBrowse.Width = 120;
        rightScroll.Controls.Add(_btnBrowse);
        y += 32;

        // 매핑 이름
        var lblName = CreateLabel("매핑 이름:", 0, y); rightScroll.Controls.Add(lblName);
        _txtMappingName.Location = new Point(80, y); _txtMappingName.Width = 200;
        rightScroll.Controls.Add(_txtMappingName);
        y += 32;

        // 소스 포트 그룹
        var lblSource = CreateLabel("── 소스 포트 ──", 0, y);
        lblSource.Font = new Font(Font, FontStyle.Bold); lblSource.AutoSize = true;
        rightScroll.Controls.Add(lblSource);
        y += 24;

        rightScroll.Controls.Add(CreateLabel("포트:", 0, y));
        _cboSourcePort.Location = new Point(80, y); _cboSourcePort.Width = 100; _cboSourcePort.DropDownStyle = ComboBoxStyle.DropDown;
        rightScroll.Controls.Add(_cboSourcePort);

        rightScroll.Controls.Add(CreateLabel("보드레이트:", 200, y));
        _cboSourceBaud.Location = new Point(280, y); _cboSourceBaud.Width = 100; _cboSourceBaud.DropDownStyle = ComboBoxStyle.DropDown;
        rightScroll.Controls.Add(_cboSourceBaud);
        y += 32;

        // 대상 포트 목록 그룹
        var lblDest = CreateLabel("── 대상 포트 목록 ──", 0, y);
        lblDest.Font = new Font(Font, FontStyle.Bold); lblDest.AutoSize = true;
        rightScroll.Controls.Add(lblDest);
        y += 24;

        _dgvDestinations.Location = new Point(0, y);
        _dgvDestinations.Size = new Size(560, 120);
        _dgvDestinations.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _dgvDestinations.AllowUserToAddRows = false;
        _dgvDestinations.AllowUserToDeleteRows = false;
        _dgvDestinations.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dgvDestinations.MultiSelect = false;
        _dgvDestinations.ReadOnly = true;
        _dgvDestinations.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvDestinations.Columns.Add("Port", "포트");
        _dgvDestinations.Columns.Add("BaudRate", "보드레이트");
        _dgvDestinations.Columns.Add("Copies", "매수");
        _dgvDestinations.Columns.Add("Patterns", "패턴");
        rightScroll.Controls.Add(_dgvDestinations);
        y += 125;

        var destButtons = new FlowLayoutPanel { Location = new Point(0, y), Size = new Size(300, 32), FlowDirection = FlowDirection.LeftToRight };
        destButtons.Controls.AddRange(new Control[] { _btnAddDest, _btnRemoveDest });
        rightScroll.Controls.Add(destButtons);
        y += 36;

        // 선택한 대상 상세
        var lblDetail = CreateLabel("── 선택한 대상 상세 ──", 0, y);
        lblDetail.Font = new Font(Font, FontStyle.Bold); lblDetail.AutoSize = true;
        rightScroll.Controls.Add(lblDetail);
        y += 24;

        rightScroll.Controls.Add(CreateLabel("포트:", 0, y));
        _cboDestPort.Location = new Point(80, y); _cboDestPort.Width = 100; _cboDestPort.DropDownStyle = ComboBoxStyle.DropDown;
        rightScroll.Controls.Add(_cboDestPort);

        rightScroll.Controls.Add(CreateLabel("보드레이트:", 200, y));
        _cboDestBaud.Location = new Point(280, y); _cboDestBaud.Width = 100; _cboDestBaud.DropDownStyle = ComboBoxStyle.DropDown;
        rightScroll.Controls.Add(_cboDestBaud);
        y += 28;

        rightScroll.Controls.Add(CreateLabel("복사 매수:", 0, y));
        _nudCopies.Location = new Point(80, y); _nudCopies.Width = 60;
        rightScroll.Controls.Add(_nudCopies);

        rightScroll.Controls.Add(CreateLabel("인코딩:", 200, y));
        _txtEncoding.Location = new Point(280, y); _txtEncoding.Width = 100;
        rightScroll.Controls.Add(_txtEncoding);
        y += 28;

        rightScroll.Controls.Add(CreateLabel("패턴 (줄바꿈 구분, 비어있으면 전체 전송):", 0, y));
        y += 20;
        _txtPatterns.Location = new Point(0, y); _txtPatterns.Size = new Size(400, 70);
        _txtPatterns.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        rightScroll.Controls.Add(_txtPatterns);

        rightPanel.Controls.Add(rightScroll);

        // === 하단 바 ===
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        _btnSave.Location = new Point(8, 8); _btnSave.Width = 80;
        _btnRestart.Location = new Point(96, 8); _btnRestart.Width = 100;
        _lblStatus.Location = new Point(210, 12);
        bottomBar.Controls.AddRange(new Control[] { _btnSave, _btnRestart, _lblStatus });

        mainPanel.Controls.Add(leftPanel, 0, 0);
        mainPanel.Controls.Add(rightPanel, 1, 0);

        Controls.Add(mainPanel);
        Controls.Add(bottomBar);
    }

    private void WireEvents()
    {
        _mappingList.SelectedIndexChanged += (_, _) => OnMappingSelected();
        _btnAddMapping.Click += (_, _) => AddMapping();
        _btnRemoveMapping.Click += (_, _) => RemoveMapping();

        _dgvDestinations.SelectionChanged += (_, _) => OnDestinationSelected();
        _btnAddDest.Click += (_, _) => AddDestination();
        _btnRemoveDest.Click += (_, _) => RemoveDestination();

        _txtMappingName.TextChanged += (_, _) => SaveCurrentMappingToModel();
        _cboSourcePort.TextChanged += (_, _) => SaveCurrentMappingToModel();
        _cboSourceBaud.TextChanged += (_, _) => SaveCurrentMappingToModel();

        _cboDestPort.TextChanged += (_, _) => SaveCurrentDestToModel();
        _cboDestBaud.TextChanged += (_, _) => SaveCurrentDestToModel();
        _nudCopies.ValueChanged += (_, _) => SaveCurrentDestToModel();
        _txtEncoding.TextChanged += (_, _) => SaveCurrentDestToModel();
        _txtPatterns.TextChanged += (_, _) => SaveCurrentDestToModel();

        _btnSave.Click += (_, _) => SaveConfig();
        _btnRestart.Click += (_, _) => RestartService();
        _btnBrowse.Click += (_, _) => BrowseConfigFile();
    }

    private void PopulateComPorts()
    {
        var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        var bauds = new[] { "9600", "19200", "38400", "57600", "115200" };

        _cboSourcePort.Items.AddRange(ports);
        _cboDestPort.Items.AddRange(ports);
        _cboSourceBaud.Items.AddRange(bauds);
        _cboDestBaud.Items.AddRange(bauds);
    }

    private void LoadDefaultConfig()
    {
        // 서비스 프로젝트의 appsettings.json 경로 탐색
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "appsettings.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "publish", "appsettings.json"),
        };

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                _configPath = full;
                LoadConfig();
                return;
            }
        }

        _lblStatus.Text = "설정 파일을 찾을 수 없습니다. '설정파일 열기'를 사용하세요.";
    }

    private void BrowseConfigFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            Title = "appsettings.json 파일을 선택하세요"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _configPath = dlg.FileName;
            LoadConfig();
        }
    }

    private void LoadConfig()
    {
        try
        {
            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("COMcopier", out var section))
            {
                _settings = JsonSerializer.Deserialize<CopierSettings>(section.GetRawText(), JsonOptions) ?? new();
            }
            else
            {
                _settings = new CopierSettings();
            }

            RefreshMappingList();
            _lblStatus.Text = $"로드됨: {_configPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"설정 파일 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveConfig()
    {
        if (string.IsNullOrEmpty(_configPath))
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "JSON 파일 (*.json)|*.json",
                FileName = "appsettings.json"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _configPath = dlg.FileName;
        }

        try
        {
            // 기존 JSON 구조를 유지하면서 COMcopier 섹션만 업데이트
            Dictionary<string, object>? root = null;
            if (File.Exists(_configPath))
            {
                var existing = File.ReadAllText(_configPath);
                root = JsonSerializer.Deserialize<Dictionary<string, object>>(existing, JsonOptions);
            }
            root ??= new Dictionary<string, object>();

            // COMcopier 섹션을 JsonElement로 변환하여 삽입
            var settingsJson = JsonSerializer.Serialize(_settings, JsonOptions);
            var settingsElement = JsonSerializer.Deserialize<JsonElement>(settingsJson);
            root["COMcopier"] = settingsElement;

            var output = JsonSerializer.Serialize(root, JsonOptions);
            File.WriteAllText(_configPath, output);

            _lblStatus.Text = $"저장됨: {_configPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestartService()
    {
        try
        {
            var stopInfo = new ProcessStartInfo("sc", "stop COMcopier")
            {
                CreateNoWindow = true,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(stopInfo)?.WaitForExit(5000);

            var startInfo = new ProcessStartInfo("sc", "start COMcopier")
            {
                CreateNoWindow = true,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo)?.WaitForExit(5000);

            _lblStatus.Text = "서비스 재시작 완료";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"서비스 재시작 실패:\n{ex.Message}\n\n관리자 권한이 필요합니다.", "오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ── 매핑 관리 ──

    private void RefreshMappingList()
    {
        _suppressEvents = true;
        var selectedIdx = _mappingList.SelectedIndex;
        _mappingList.Items.Clear();
        foreach (var m in _settings.Mappings)
            _mappingList.Items.Add(m.Name);

        if (selectedIdx >= 0 && selectedIdx < _mappingList.Items.Count)
            _mappingList.SelectedIndex = selectedIdx;
        else if (_mappingList.Items.Count > 0)
            _mappingList.SelectedIndex = 0;

        _suppressEvents = false;
        OnMappingSelected();
    }

    private MappingConfig? GetSelectedMapping()
    {
        var idx = _mappingList.SelectedIndex;
        return idx >= 0 && idx < _settings.Mappings.Count ? _settings.Mappings[idx] : null;
    }

    private void AddMapping()
    {
        var mapping = new MappingConfig
        {
            Name = $"매핑 {_settings.Mappings.Count + 1}",
            Source = new SerialPortConfig { BaudRate = 9600 }
        };
        _settings.Mappings.Add(mapping);
        RefreshMappingList();
        _mappingList.SelectedIndex = _mappingList.Items.Count - 1;
    }

    private void RemoveMapping()
    {
        var idx = _mappingList.SelectedIndex;
        if (idx < 0) return;
        if (MessageBox.Show($"'{_settings.Mappings[idx].Name}' 매핑을 삭제하시겠습니까?", "삭제 확인",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _settings.Mappings.RemoveAt(idx);
        RefreshMappingList();
    }

    private void OnMappingSelected()
    {
        if (_suppressEvents) return;
        _suppressEvents = true;

        var mapping = GetSelectedMapping();
        if (mapping != null)
        {
            _txtMappingName.Text = mapping.Name;
            _cboSourcePort.Text = mapping.Source.Port;
            _cboSourceBaud.Text = mapping.Source.BaudRate.ToString();
            RefreshDestGrid(mapping);
        }
        else
        {
            _txtMappingName.Text = "";
            _cboSourcePort.Text = "";
            _cboSourceBaud.Text = "";
            _dgvDestinations.Rows.Clear();
            ClearDestDetail();
        }

        _suppressEvents = false;
    }

    private void SaveCurrentMappingToModel()
    {
        if (_suppressEvents) return;
        var mapping = GetSelectedMapping();
        if (mapping == null) return;

        mapping.Name = _txtMappingName.Text;
        mapping.Source.Port = _cboSourcePort.Text;
        if (int.TryParse(_cboSourceBaud.Text, out var baud))
            mapping.Source.BaudRate = baud;

        // 리스트 표시 이름 업데이트
        var idx = _mappingList.SelectedIndex;
        if (idx >= 0)
        {
            _suppressEvents = true;
            _mappingList.Items[idx] = mapping.Name;
            _suppressEvents = false;
        }
    }

    // ── 대상 포트 관리 ──

    private void RefreshDestGrid(MappingConfig mapping)
    {
        _dgvDestinations.Rows.Clear();
        foreach (var dest in mapping.Destinations)
        {
            var patterns = dest.Patterns.Count > 0 ? string.Join(", ", dest.Patterns) : "(전체)";
            _dgvDestinations.Rows.Add(dest.Port, dest.BaudRate, dest.Copies, patterns);
        }

        if (_dgvDestinations.Rows.Count > 0)
            _dgvDestinations.Rows[0].Selected = true;

        OnDestinationSelected();
    }

    private DestinationConfig? GetSelectedDest()
    {
        var mapping = GetSelectedMapping();
        if (mapping == null || _dgvDestinations.SelectedRows.Count == 0) return null;
        var idx = _dgvDestinations.SelectedRows[0].Index;
        return idx >= 0 && idx < mapping.Destinations.Count ? mapping.Destinations[idx] : null;
    }

    private void AddDestination()
    {
        var mapping = GetSelectedMapping();
        if (mapping == null) return;

        mapping.Destinations.Add(new DestinationConfig { BaudRate = 9600 });
        RefreshDestGrid(mapping);
        _dgvDestinations.ClearSelection();
        _dgvDestinations.Rows[_dgvDestinations.Rows.Count - 1].Selected = true;
    }

    private void RemoveDestination()
    {
        var mapping = GetSelectedMapping();
        if (mapping == null || _dgvDestinations.SelectedRows.Count == 0) return;

        var idx = _dgvDestinations.SelectedRows[0].Index;
        mapping.Destinations.RemoveAt(idx);
        RefreshDestGrid(mapping);
    }

    private void OnDestinationSelected()
    {
        if (_suppressEvents) return;
        _suppressEvents = true;

        var dest = GetSelectedDest();
        if (dest != null)
        {
            _cboDestPort.Text = dest.Port;
            _cboDestBaud.Text = dest.BaudRate.ToString();
            _nudCopies.Value = dest.Copies;
            _txtEncoding.Text = dest.Encoding;
            _txtPatterns.Text = string.Join(Environment.NewLine, dest.Patterns);
            SetDestDetailEnabled(true);
        }
        else
        {
            ClearDestDetail();
        }

        _suppressEvents = false;
    }

    private void ClearDestDetail()
    {
        _cboDestPort.Text = "";
        _cboDestBaud.Text = "";
        _nudCopies.Value = 1;
        _txtEncoding.Text = "euc-kr";
        _txtPatterns.Text = "";
        SetDestDetailEnabled(false);
    }

    private void SetDestDetailEnabled(bool enabled)
    {
        _cboDestPort.Enabled = enabled;
        _cboDestBaud.Enabled = enabled;
        _nudCopies.Enabled = enabled;
        _txtEncoding.Enabled = enabled;
        _txtPatterns.Enabled = enabled;
    }

    private void SaveCurrentDestToModel()
    {
        if (_suppressEvents) return;
        var dest = GetSelectedDest();
        if (dest == null) return;

        dest.Port = _cboDestPort.Text;
        if (int.TryParse(_cboDestBaud.Text, out var baud))
            dest.BaudRate = baud;
        dest.Copies = (int)_nudCopies.Value;
        dest.Encoding = _txtEncoding.Text;
        dest.Patterns = _txtPatterns.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        // 그리드 행 업데이트
        if (_dgvDestinations.SelectedRows.Count > 0)
        {
            var row = _dgvDestinations.SelectedRows[0];
            row.Cells["Port"].Value = dest.Port;
            row.Cells["BaudRate"].Value = dest.BaudRate;
            row.Cells["Copies"].Value = dest.Copies;
            row.Cells["Patterns"].Value = dest.Patterns.Count > 0 ? string.Join(", ", dest.Patterns) : "(전체)";
        }
    }

    private static Label CreateLabel(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };
}
