using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Sftp;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _processName = "FactoryGameEGS-Win64-Shipping";
    private bool _processIsRunning = false;
    private readonly Dictionary<string, string> config;
    private readonly string _configPath;
    private const string saveFileExtension = ".sav";
    private const int maxFiles = 3;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        var exePath = AppContext.BaseDirectory;
        _logger.LogInformation($"ExePath: {exePath}");
        _configPath = Path.Combine(exePath, "config.cfg");
        _logger.LogInformation($"ConfigPath: {_configPath}");
        config = ReadConfig(_configPath);
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
                DownloadFiles();
                _processIsRunning = true;
            }
            else if (processes.Length == 0 && _processIsRunning)
            {
                _logger.LogInformation("Process stopped. Running 'upload' operation.");
                UploadFiles();
                _processIsRunning = false;
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private Dictionary<string, string> ReadConfig(string configPath)
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
    private void DownloadFiles()
    {
        PrivateKeyFile pkfile = new PrivateKeyFile(config["Keyfile"]);
        try
        {
            // get the last write time of the newest local files (maybe throw out files that are not of the correct type)
            var directory = new DirectoryInfo(config["LocalDirectory"]);
            var localrecentFiles = directory.GetFiles("*" + saveFileExtension)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(maxFiles)
                .ToList();


            using (var sshclient = new SshClient(config["Hostname"], config["Username"], pkfile))
            using (var ftpclient = new SftpClient(config["Hostname"], config["Username"], pkfile))
            {
                // get last write times of the newest {maxFiles} remote files
                sshclient.Connect();
                ftpclient.Connect();

                var directorylist = ftpclient.ListDirectory(".");

                var remoterecentFiles = new List<ISftpFile>();
                var dirlist = directorylist.ToList();
                foreach (var x in dirlist)
                {
                    string ext = Path.GetExtension(x.Name);
                    if (x.IsRegularFile && string.Compare(Path.GetExtension(x.Name), saveFileExtension) == 0)
                    {
                        remoterecentFiles.Add(x);
                    }
                }

                remoterecentFiles.OrderByDescending(f => f.LastWriteTimeUtc);

                var replacelist = new List<FileInfo>();

                // compare files
                foreach (var file in remoterecentFiles)
                {
                    var found = localrecentFiles.Find(f => string.Compare(f.Name, file.Name) == 0);
                    if (localrecentFiles.Count == 0)
                    {
                        replacelist.Add(new FileInfo(directory.FullName + "\\" + file.Name));
                    }
                    else if (found != null && found.LastWriteTimeUtc < file.LastWriteTimeUtc)
                    {
                        // download if file has the same name and is newer on remote
                        replacelist.Add(new FileInfo(directory.FullName + "\\" + file.Name));
                    }
                    else if (found == null && localrecentFiles[0].LastWriteTimeUtc < file.LastWriteTimeUtc)
                    {
                        // also download file if they don't have the same name but it is newer than newest local file
                        replacelist.Add(new FileInfo(directory.FullName + "\\" + file.Name));
                    }
                }

                // download all files that are newer on the server
                foreach (var replace in replacelist)
                {
                    DownloadFile(replace, sshclient, ftpclient);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogInformation("DownloadFiles failed with: " + e.Message);
        }
    }

    private void DownloadFile(FileInfo file, SshClient ssh, SftpClient ftp)
    {
        string md5hash = "";
        string tempfile = "tmp.sav";
        try
        {
            using SshCommand cmd = ssh.RunCommand("md5sum " + file.Name);
            md5hash = cmd.Result.Substring(0, cmd.Result.IndexOf(' '));

            using (FileStream fs = File.Create(tempfile))
            {
                ftp.DownloadFile(file.Name, fs);
            }
            if (ftp.Exists(file.Name) && string.Compare(md5hash, CalculateMD5(tempfile)) != 0)
            {
                // md5hashes do not match
                File.Delete(tempfile);
            }
            else
            {
                // md5hashes match, Rename files
                if (File.Exists(file.FullName))
                {
                    File.Delete(file.FullName);
                }
                File.Move(tempfile, file.FullName);
                // client should get the file in question and overwrite the attribute
                // to the write time of the original file
                File.SetLastWriteTimeUtc(file.FullName, ftp.Get(file.Name).LastWriteTimeUtc);
            }
        }
        catch (Exception e)
        {
            _logger.LogError("DownloadFile failed with " + e.Message);
            // cleanup tempfile if something went wrong
            if (File.Exists(tempfile))
            {
                File.Delete(tempfile);
            }
        }
    }
    private void UploadFiles()
    {
        PrivateKeyFile pkfile = new PrivateKeyFile(config["Keyfile"]);
        try
        {
            // get the last write time of the newest local files (maybe throw out files that are not of the correct type)
            var directory = new DirectoryInfo(config["LocalDirectory"]);
            var localrecentFiles = directory.GetFiles("*" + saveFileExtension)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(maxFiles)
                .ToList();


            using (var sshclient = new SshClient(config["Hostname"], config["Username"], pkfile))
            using (var ftpclient = new SftpClient(config["Hostname"], config["Username"], pkfile))
            {
                // get last write times of the newest {maxFiles} remote files
                sshclient.Connect();
                ftpclient.Connect();

                var directorylist = ftpclient.ListDirectory(".");

                var remoterecentFiles = new List<ISftpFile>();
                var dirlist = directorylist.ToList();
                foreach (var x in dirlist)
                {
                    string ext = Path.GetExtension(x.Name);
                    if (x.IsRegularFile && string.Compare(Path.GetExtension(x.Name), saveFileExtension) == 0)
                    {
                        remoterecentFiles.Add(x);
                    }
                }

                remoterecentFiles.OrderByDescending(f => f.LastWriteTimeUtc);

                var replacelist = new List<FileInfo>();


                // compare files
                foreach (var file in localrecentFiles)
                {
                    var found = remoterecentFiles.Find(f => string.Compare(f.Name, file.Name) == 0);
                    if (remoterecentFiles.Count == 0)
                    {
                        replacelist.Add(file);
                    }
                    else if (found != null && found.LastWriteTimeUtc < file.LastWriteTimeUtc)
                    {
                        // upload if file has the same name and is newer on local
                        replacelist.Add(file);
                    }
                    else if (found == null && remoterecentFiles[0].LastWriteTimeUtc < file.LastWriteTimeUtc)
                    {
                        // also upload file if they don't have the same name but it is newer than newest remote file
                        replacelist.Add(file);
                    }
                }

                // download all files that are newer on the server
                foreach (var replace in replacelist)
                {
                    UploadFile(replace, sshclient, ftpclient);
                }

            }
        }
        catch (Exception e)
        {
            _logger.LogError("UploadFiles failed with: " + e.Message);
        }
    }

    private void UploadFile(FileInfo file, SshClient ssh, SftpClient ftp)
    {
        string md5hash;
        string tempfile = "tmp" + saveFileExtension;
        try
        {
            using (FileStream fs = File.OpenRead(file.FullName))
            {
                ftp.UploadFile(fs, tempfile);
            }

            using SshCommand cmd = ssh.RunCommand("md5sum " + tempfile);
            md5hash = cmd.Result.Substring(0, cmd.Result.IndexOf(' '));

            if (string.Compare(md5hash, CalculateMD5(file.FullName)) != 0)
            {
                // md5hashes do not match
                ftp.Delete(tempfile);
            }
            else
            {
                // md5hashes match, Rename files
                if (ftp.Exists(file.Name))
                {
                    ftp.Delete(file.Name);
                }
                ftp.RenameFile(tempfile, file.Name);
                ftp.SetLastWriteTimeUtc(file.Name, file.LastWriteTimeUtc);
            }
        }
        catch (Exception e)
        {
            _logger.LogError("UploadFile failed with: " + e.Message);
            // cleanup tempfile if something went wrong
            if (ftp.Exists(tempfile))
            {
                ftp.Delete(tempfile);
            }
        }
    }

    static string CalculateMD5(string fileName)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(fileName))
        {
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}