using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Diagnostics;

abstract class StreamHandler : IDisposable
{
    public abstract Stream Stream { get; }
    public abstract void Dispose();
}

class MemoryStreamHandler : StreamHandler
{
    private MemoryStream memoryStream;

    public MemoryStreamHandler()
    {
        memoryStream = new MemoryStream();
    }

    public override Stream Stream => memoryStream;

    public override void Dispose()
    {
        memoryStream.Dispose();
    }
}

class FileStreamHandler : StreamHandler
{
    private string filePath;
    private FileStream fileStream;

    public FileStreamHandler(string filePath)
    {
        this.filePath = filePath;
        fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
    }

    public override Stream Stream => fileStream;

    public override void Dispose()
    {
        fileStream.Dispose();
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}

class StreamHandlerFactory : IDisposable
{
    private List<StreamHandler> handlers = new List<StreamHandler>();

    public StreamHandler CreateHandler(string filePath, long memoryThreshold)
    {
        long availableMemory = GetAvailableMemory();
        StreamHandler handler;

        if (availableMemory > memoryThreshold)
        {
            handler = new MemoryStreamHandler();
        }
        else
        {
            handler = new FileStreamHandler(filePath);
        }

        handlers.Add(handler);
        return handler;
    }

    private static long GetAvailableMemory()
    {
        using (PerformanceCounter pc = new PerformanceCounter("Memory", "Available Bytes"))
        {
            return Convert.ToInt64(pc.NextValue());
        }
    }

    public void Dispose()
    {
        foreach (var handler in handlers)
        {
            handler.Dispose();
        }

        handlers.Clear();
    }
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        string inputDirectory = null;
        string outputDirectory = null;
        long partSizeMB = 100; // Default 100 MB per part
        long memoryThresholdMB = 100; // Default 100 MB memory threshold

        // Parse command-line arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input":
                    if (i + 1 < args.Length)
                    {
                        inputDirectory = args[i + 1];
                    }
                    break;
                case "--output":
                    if (i + 1 < args.Length)
                    {
                        outputDirectory = args[i + 1];
                    }
                    break;
                case "--partsize":
                    if (i + 1 < args.Length && long.TryParse(args[i + 1], out long size))
                    {
                        partSizeMB = size;
                    }
                    break;
                case "--threshold":
                    if (i + 1 < args.Length && long.TryParse(args[i + 1], out long threshold))
                    {
                        memoryThresholdMB = threshold;
                    }
                    break;
                case "--cd":
                    memoryThresholdMB = 700; // CD capacity
                    partSizeMB = Math.Min(partSizeMB, 700); // Limit part size to CD capacity
                    break;
                case "--dvd":
                    memoryThresholdMB = 4700; // DVD capacity
                    partSizeMB = Math.Min(partSizeMB, 4700); // Limit part size to DVD capacity
                    break;
                case "--bluray":
                    memoryThresholdMB = 25000; // Blu-ray capacity
                    partSizeMB = Math.Min(partSizeMB, 25000); // Limit part size to Blu-ray capacity
                    break;
            }
        }

        if (string.IsNullOrEmpty(inputDirectory) || string.IsNullOrEmpty(outputDirectory))
        {
            Console.WriteLine("Error: Input and output directories must be specified.");
            PrintUsage();
            return;
        }

        if (!Directory.Exists(inputDirectory))
        {
            Console.WriteLine($"Error: The input directory '{inputDirectory}' does not exist.");
            return;
        }

        if (!Directory.Exists(outputDirectory))
        {
            Console.WriteLine($"Creating output directory '{outputDirectory}'.");
            Directory.CreateDirectory(outputDirectory);
        }

        long partSizeBytes = partSizeMB * 1024 * 1024;
        long memoryThresholdBytes = memoryThresholdMB * 1024 * 1024;

        using (var factory = new StreamHandlerFactory())
        {
            CreateZipParts(factory, inputDirectory, outputDirectory, partSizeBytes, memoryThresholdBytes);
        }

        Console.WriteLine("Zipping complete.");
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  --input <directory>   : Specify the directory to compress.");
        Console.WriteLine("  --output <directory>  : Specify the output directory for ZIP files.");
        Console.WriteLine("  --partsize <sizeMB>   : Specify the maximum size of each ZIP part in MB (default is 100MB).");
        Console.WriteLine("  --threshold <sizeMB>  : Specify the memory threshold in MB for choosing MemoryStream (default is 100MB).");
        Console.WriteLine("  --cd                  : Set size constraints for CD capacity (700MB).");
        Console.WriteLine("  --dvd                 : Set size constraints for DVD capacity (4700MB).");
        Console.WriteLine("  --bluray              : Set size constraints for Blu-ray capacity (25000MB).");
    }

    static void CreateZipParts(StreamHandlerFactory factory, string inputDir, string outputDir, long partSizeBytes, long memoryThresholdBytes)
    {
        var files = Directory.GetFiles(inputDir, "*.*", SearchOption.AllDirectories);
        var currentZipFiles = new List<string>();
        long currentZipSize = 0;
        int zipPartNumber = 0;

        foreach (var filePath in files)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            if (currentZipSize + fileSize > partSizeBytes)
            {
                CreateZip(factory, currentZipFiles, outputDir, zipPartNumber, memoryThresholdBytes);
                currentZipFiles.Clear();
                currentZipSize = 0;
                zipPartNumber++;
            }

            currentZipFiles.Add(filePath);
            currentZipSize += fileSize;
        }

        // Create the last zip if there are remaining files
        if (currentZipFiles.Count > 0)
        {
            CreateZip(factory, currentZipFiles, outputDir, zipPartNumber, memoryThresholdBytes);
        }
    }

    static void CreateZip(StreamHandlerFactory factory, List<string> files, string outputDir, int partNumber, long memoryThresholdBytes)
    {
        string tempZipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string zipFileName = Path.Combine(outputDir, $"archive_part{partNumber:D3}.zip");

        using (StreamHandler streamHandler = factory.CreateHandler(tempZipPath, memoryThresholdBytes))
        {
            using (ZipArchive zip = new ZipArchive(streamHandler.Stream, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    string entryName = Path.GetRelativePath(Path.GetDirectoryName(file), file);
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            using (FileStream outputStream = new FileStream(zipFileName, FileMode.Create, FileAccess.Write))
            {
                streamHandler.Stream.Position = 0;
                streamHandler.Stream.CopyTo(outputStream);
            }
        }

        Console.WriteLine($"Created {zipFileName}");
    }
}
