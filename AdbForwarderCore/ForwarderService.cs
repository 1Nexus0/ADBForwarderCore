using System.Net;
using AdvancedSharpAdbClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnixFileMode = System.IO.UnixFileMode;

namespace AdbForwarderCore;

public class ForwarderService : BackgroundService
{
    private readonly AdbClient _client;
    private readonly ILogger<ForwarderService> _logger;
    private readonly DevicesOptions _devicesOptions;
    
    public ForwarderService(ILogger<ForwarderService> logger, IOptions<DevicesOptions> settings)
    {
        
        _logger = logger ?? 
                  throw new ArgumentException(null, nameof(logger));

        if (settings == null)
        {
            throw new ArgumentException(null, nameof(settings));
        }

        _devicesOptions = settings.Value;
        
        _client = new AdbClient();
        
        if (_devicesOptions.ProductsAllowed.Length == 0)
        {
            throw new IndexOutOfRangeException("No devices allowed to forward");
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)  
        => await MonitorAsync(stoppingToken);

    private async ValueTask MonitorAsync(CancellationToken cancellationToken)
    {
        using (_logger.BeginScope("Starting device monitoring..."))
        {
            await StartAdbServerAsync(cancellationToken);

            IPEndPoint endPoint = new(IPAddress.Loopback, AdbClient.AdbServerPort);
          
            await _client.ConnectAsync(endPoint, cancellationToken: cancellationToken);
                             
            await using var monitor = new DeviceMonitor(new AdbSocket(endPoint));

            monitor.DeviceConnected += Monitor_DeviceConnected;
            monitor.DeviceDisconnected += OnMonitorOnDeviceDisconnected;

            await  monitor.StartAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested && monitor.IsRunning)
            {
                try
                {
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Monitoring shutdown");
                }
            }
            
        }
    }

    private async Task StartAdbServerAsync(CancellationToken cancellationToken)
    {
          var adbPath = await GetAdbPath();

            if (string.IsNullOrEmpty(adbPath))
            {
                _logger.LogCritical("ADB not found.");

                return;
            }

          if (!(await AdbServer.Instance.GetStatusAsync(cancellationToken)).IsRunning)
            {
                _logger.LogInformation("Starting ADB Server...");

                var server = new AdbServer();

                var result = await server.StartServerAsync(adbPath, false, cancellationToken);

                if (result != StartServerResult.Started)
                {
                    _logger.LogCritical("ADB server cannot be started");
                }
            }
            else
            {
                _logger.LogInformation("ADB Server is already started");
            }

    }

    private void OnMonitorOnDeviceDisconnected(object? o, DeviceDataConnectEventArgs args)
    {
        Monitor_DeviceDisconnected(o, args);
    }
 
    private static OsType GetOsType () 
         =>  OperatingSystem.IsLinux()
            ? OsType.Linux
        : OperatingSystem.IsWindows()
            ? OsType.Windows
        : OsType.Unknown;
    private  async Task DownloadAdb(string downloadUri,string path = "")
        {
            using (var web = new HttpClient())
            {
                await using (var stream = await web.GetStreamAsync(downloadUri))
                {
                    await using ( var fs = new FileStream("adb.zip",FileMode.Create))
                    {
                        await stream.CopyToAsync(fs);
                    }
                    
                }
            }
            
            _logger.LogInformation ("Download successful");
            
            System.IO.Compression.ZipFile.ExtractToDirectory("adb.zip", path);
            
            _logger.LogInformation ("Extraction successful");
            
            File.Delete("adb.zip");
        }

    private  async Task CheckAdb(string downloadUri,string adbPathOrig,string adbPath, OsType osType)
        {
            if(!Directory.Exists(adbPathOrig))
            {
                Directory.CreateDirectory(adbPathOrig);
            }

            if (!File.Exists(adbPath))
            {
                _logger.LogInformation ("ADB not found, downloading in the background...");
                await DownloadAdb(downloadUri,adbPathOrig);

                if (osType == OsType.Linux)
                {
                    SetAsLinuxExecutable(adbPath);
                }
            }
        }
        
    private void SetAsLinuxExecutable(string fileName)
        {
            _logger.LogInformation ("Giving adb executable permissions");
            
            const UnixFileMode userAccess = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite;
            const UnixFileMode groupAccess = UnixFileMode.GroupExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;
            const UnixFileMode othersAccess = UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        
            File.SetUnixFileMode(fileName,userAccess | groupAccess | othersAccess );
        }
        
    private void Monitor_DeviceConnected(object? sender, DeviceDataEventArgs e) 
        {
            if(e.Device.Serial.Contains("127.0.0.1"))
            {
                return;
            }
            
            _logger.LogInformation("Connected device: {serial}",e.Device.Serial);
            
            Forward(e.Device);
        }

    private  void Monitor_DeviceDisconnected(object? sender, DeviceDataEventArgs e)
        {
            //await _client.RemoveForwardAsync(e.Device, 9943);
            //await _client.RemoveForwardAsync(e.Device, 9944);

            _logger.LogWarning("Disconnected device: {serial}", e.Device.Serial );
            
        }
        
    private  async void Forward(DeviceData device)
        {
            // DeviceConnected calls without product set yet

            await Task.Delay(2000);

            var devices = await _client.GetDevicesAsync();
            var deviceData = devices
                            .Single(data => device.Serial == data.Serial);

                   
            if (!_devicesOptions.ProductsAllowed.Contains(deviceData.Product))
            {
                _logger.LogWarning("Device forwarding was skipped: Product '{product}', Serial '{serial}'", deviceData.Product,deviceData.Serial);
                return;
            }
            
            await _client.CreateForwardAsync(deviceData, 9943, 9943);
            await _client.CreateForwardAsync(deviceData, 9944, 9944);
            
            _logger.LogInformation("Successfully forwarded device: {serial} [{product}]",deviceData.Serial,deviceData.Product);
        }

     

    private  async Task<string> GetAdbPath()
        {
            const string adbPathOrig = "adb";
            var adbPath = "adb/platform-tools/{0}";
            var downloadUri = "https://dl.google.com/android/repository/platform-tools-latest-{0}.zip";

            var osType = GetOsType();
            
            switch (osType)
            {
                case OsType.Linux:
                    _logger.LogInformation ("Platform: Linux");
                
                    adbPath = string.Format(adbPath, "adb");
                    downloadUri = string.Format(downloadUri, "linux");
                    break;
                case OsType.Windows:
                    _logger.LogInformation ("Platform: Windows");
                
                    adbPath = string.Format(adbPath, "adb.exe");
                    downloadUri = string.Format(downloadUri, "windows");
                    break;
                
                case OsType.Unknown:
                    _logger.LogInformation ("Unsupported platform!");
                    return string.Empty;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await CheckAdb(downloadUri,adbPathOrig, adbPath,osType);

            return adbPath;
        }  
}

internal enum  OsType : byte
{
    Unknown,
    Linux,
    Windows
}