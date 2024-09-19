using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _processName = "FactoryGameEGS-Win64-Shipping";
    private bool _processIsRunning = false;
    private readonly Dictionary<string, string> _config;
    private readonly string _configPath;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        var exePath = AppContext.BaseDirectory;
        _logger.LogInformation($"ExePath: {exePath}");
        _configPath = Path.Combine(exePath, "config.cfg");
        _logger.LogInformation($"ConfigPath: {_configPath}");
        _config = ReadConfig(_configPath);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Monitoring for process start and stop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            Process[] processes = Process.GetProcessesByName(_processName);

            if (processes.Length > 0 && !_processIsRunning)
            {
                _logger.LogInformation("Process started. Running 'download' operation.");
                DownloadFiles(_config);
                _processIsRunning = true;
            }
            else if (processes.Length == 0 && _processIsRunning)
            {
                _logger.LogInformation("Process stopped. Running 'upload' operation.");
                UploadFiles(_config);
                _processIsRunning = false;
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    static Dictionary<string, string> ReadConfig(string configPath)
    {
        var config = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(configPath))
        {
            var parts = line.Split('=');
            if (parts.Length == 2)
            {
                config[parts[0].Trim()] = parts[1].Trim();
            }
        }
        return config;
    }

    static void UploadFiles(Dictionary<string, string> config)
    {
        var directory = new DirectoryInfo(config["LocalDirectory"]);
        var recentFiles = directory.GetFiles()
            .OrderByDescending(f => f.LastWriteTime)
            .Take(3)
            .ToList();

        foreach (var file in recentFiles)
        {
            UploadFile(file.FullName, file.Name, config);
        }
    }

    static void DownloadFiles(Dictionary<string, string> config)
    {
        var ftpFiles = ListFtpFiles(config);
        var recentFiles = ftpFiles
            .Where(f => !string.IsNullOrEmpty(f.Name)) // Filter out null or empty names
            .OrderByDescending(f => f.Date)
            .Take(3)
            .ToList();

        foreach (var file in recentFiles)
        {
            DownloadFile(file.Name, config);
        }
    }

    static void UploadFile(string localFilePath, string fileName, Dictionary<string, string> config)
    {
        string ftpUrl = $"{config["FtpUrl"]}/{fileName}";
        string username = config["Username"];
        string password = config["Password"];

        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUrl);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(username, password);

            byte[] fileContents = File.ReadAllBytes(localFilePath);

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(fileContents, 0, fileContents.Length);
            }

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                Console.WriteLine($"Upload of {fileName} complete. Status: {response.StatusDescription}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred while uploading {fileName}: {e.Message}");
        }
    }

    static void DownloadFile(string fileName, Dictionary<string, string> config)
    {
        string ftpUrl = $"{config["FtpUrl"]}/{fileName}";
        string localFilePath = Path.Combine(config["LocalDirectory"], fileName);
        string username = config["Username"];
        string password = config["Password"];

        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUrl);
            request.Method = WebRequestMethods.Ftp.DownloadFile;
            request.Credentials = new NetworkCredential(username, password);

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (FileStream fileStream = File.Create(localFilePath))
            {
                responseStream.CopyTo(fileStream);
            }

            Console.WriteLine($"Download of {fileName} complete.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred while downloading {fileName}: {e.Message}");
        }
    }

    static List<FtpFile> ListFtpFiles(Dictionary<string, string> config)
    {
        var files = new List<FtpFile>();
        string ftpUrl = config["FtpUrl"];
        string username = config["Username"];
        string password = config["Password"];

        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUrl);
            request.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
            request.Credentials = new NetworkCredential(username, password);

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(responseStream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var file = ParseFtpLine(line);
                    if (file != null && file.Name != "." && file.Name != "..")
                    {
                        files.Add(file);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred while listing FTP files: {e.Message}");
        }

        return files;
    }

    static FtpFile ParseFtpLine(string line)
    {
        string pattern = @"^([\w-]+)\s+(\d+)\s+(\w+)\s+(\w+)\s+(\d+)\s+(\w+\s+\d+\s+[\w:]+)\s+(.+)$";
        Match match = Regex.Match(line, pattern);

        if (match.Success)
        {
            return new FtpFile
            {
                Name = match.Groups[7].Value,
                Date = DateTime.Parse(match.Groups[6].Value)
            };
        }

        Console.WriteLine($"Failed to parse FTP line: {line}");
        return null;
    }
}

class FtpFile
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
}
