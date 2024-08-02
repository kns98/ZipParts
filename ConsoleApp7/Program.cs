using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Diagnostics;

abstract class StreamHandler
{
    protected Stream stream;

    public abstract void WriteData(byte[] data);
    public abstract byte[] ReadData();
    public abstract void Dispose();
}

class MemoryStreamHandler : StreamHandler
{
    public MemoryStreamHandler()
    {
        stream = new MemoryStream();
    }

    public override void WriteData(byte[] data)
    {
        stream.Write(data, 0, data.Length);
    }

    public override byte[] ReadData()
    {
        stream.Position = 0;
        byte[] data = new byte[stream.Length];
        stream.Read(data, 0, data.Length);
        return data;
    }

    public override void Dispose()
    {
        stream.Dispose();
    }
}

class FileStreamHandler : StreamHandler
{
    private string filePath;

    public FileStreamHandler()
    {
        filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
    }

    public override void WriteData(byte[] data)
    {
        stream.Write(data, 0, data.Length);
    }

    public override byte[] ReadData()
    {
        stream.Position = 0;
        byte[] data = new byte[stream.Length];
        stream.Read(data, 0, data.Length);
        return data;
    }

    public override void Dispose()
    {
        stream.Dispose();
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}

class StreamFactory
{
    public static StreamHandler CreateStreamHandler(long memoryThreshold)
    {
        long availableMemory = GetAvailableMemory();
        if (availableMemory > memoryThreshold)
        {
            return new MemoryStreamHandler();
        }
        else
        {
            return new FileStreamHandler();
        }
    }

    private static long GetAvailableMemory()
    {
        // Use PerformanceCounter to get available physical memory
        using (PerformanceCounter pc = new PerformanceCounter("Memory", "Available Bytes"))
        {
            return Convert.ToInt64(pc.NextValue());
        }
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

        CreateZipParts(inputDirectory, outputDirectory, partSizeBytes, memoryThresholdBytes);

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

    static void CreateZipParts(string inputDir, string outputDir, long partSizeBytes, long memoryThresholdBytes)
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
                CreateZip(currentZipFiles, outputDir, zipPartNumber, memoryThresholdBytes);
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
            CreateZip(currentZipFiles, outputDir, zipPartNumber, memoryThresholdBytes);
        }
    }

    static void CreateZip(List<string> files, string outputDir, int partNumber, long memoryThresholdBytes)
    {
        string zipFileName = Path.Combine(outputDir, $"archive_part{partNumber:D3}.zip");

        StreamHandler streamHandler = StreamFactory.CreateStreamHandler(memoryThresholdBytes);
        try
        {
            using (ZipArchive zip = new ZipArchive(streamHandler.stream, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    string entryName = Path.GetRelativePath(Path.GetDirectoryName(file), file);
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            using (FileStream outputStream = new FileStream(zipFileName, FileMode.Create, FileAccess.Write))
            {
                streamHandler.stream.Position = 0;
                streamHandler.stream.CopyTo(outputStream);
            }
        }
        finally
        {
            streamHandler.Dispose();
        }

        Console.WriteLine($"Created {zipFileName}");
    }
}
