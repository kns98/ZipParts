using System;
using System.IO;
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
        // Default configuration
        long dataSizeMB = 50; // Default 50 MB
        long memoryThresholdMB = 100; // Default 100 MB

        // Parse command-line arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--size":
                    if (i + 1 < args.Length && long.TryParse(args[i + 1], out long size))
                    {
                        dataSizeMB = size;
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
                    dataSizeMB = Math.Min(dataSizeMB, 700); // Limit data size to CD capacity
                    break;
                case "--dvd":
                    memoryThresholdMB = 4700; // DVD capacity
                    dataSizeMB = Math.Min(dataSizeMB, 4700); // Limit data size to DVD capacity
                    break;
                case "--bluray":
                    memoryThresholdMB = 25000; // Blu-ray capacity
                    dataSizeMB = Math.Min(dataSizeMB, 25000); // Limit data size to Blu-ray capacity
                    break;
            }
        }

        long dataSizeBytes = dataSizeMB * 1024 * 1024;
        long memoryThresholdBytes = memoryThresholdMB * 1024 * 1024;

        // Use the factory to create the appropriate stream handler
        StreamHandler streamHandler = StreamFactory.CreateStreamHandler(memoryThresholdBytes);

        try
        {
            byte[] dataToWrite = new byte[dataSizeBytes];
            new Random().NextBytes(dataToWrite);

            streamHandler.WriteData(dataToWrite);
            byte[] dataRead = streamHandler.ReadData();

            Console.WriteLine($"Data size read: {dataRead.Length} bytes");
        }
        finally
        {
            streamHandler.Dispose();
        }
    }
}
