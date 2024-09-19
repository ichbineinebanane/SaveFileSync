using System;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.WindowsServices;

public class Program
{
    public static void Main(string[] args)
    {
        // Set the content root path to the executable's directory
        string contentRoot = AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(contentRoot);

        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .UseContentRoot(contentRoot) 
            .ConfigureServices(services =>
            {
                services.AddHostedService<Worker>();
            })
            .Build()
            .Run();
    }
}