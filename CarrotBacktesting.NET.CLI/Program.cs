using System;

namespace CarrotBacktesting.NET.CLI;

internal static class Program
{
    private static int Main(string[] args)
    {
        return CliRunner.Run(args);
    }
}
