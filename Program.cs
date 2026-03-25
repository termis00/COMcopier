using COMcopier;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "COMcopier";
});
builder.Services.AddHostedService<COMcopierService>();

var host = builder.Build();
host.Run();
