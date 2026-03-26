using COMcopier;

var builder = Host.CreateApplicationBuilder(args);

// Windows 서비스는 작업 디렉토리가 System32이므로, exe 경로를 기준으로 설정 파일을 찾도록 지정
builder.Configuration.SetBasePath(AppContext.BaseDirectory);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "COMcopier";
});
builder.Services.AddHostedService<COMcopierService>();

var host = builder.Build();
host.Run();
