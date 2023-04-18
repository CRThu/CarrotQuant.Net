using CarrotQuant.DataLib;
using Spectre.Console;

namespace CarrotQuant.DataLib.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var frequency = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("Download Menu: Select frequency: ")
                .AddChoices(new[] { "day", "5m" }));

            AnsiConsole.WriteLine($"Frequency: {frequency}");

            var adjust = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("Download Menu: Select adjust: ")
                .AddChoices(new[] { "none", "post", "pre" }));

            AnsiConsole.WriteLine($"Adjust: {adjust}");

            Console.Write("start time(default: 1990-01-01): ");
            var starttime = Console.ReadLine();
            if (starttime == "")
            {
                starttime = "1990-01-01";
            }
            AnsiConsole.WriteLine($"start time: {starttime}");

            Console.Write("end time(default: now): ");
            var endtime = Console.ReadLine();
            if (endtime == "")
            {
                endtime = "now";
            }
            AnsiConsole.WriteLine($"end time: {endtime}");

            string scriptPath = "D:\\Projects\\CarrotQuant.Net\\CarrotQuant.Data.Python\\main_download.py";

            string funcName = "get_version";
            string[] scriptArgs = new string[] { };
            CallPython.RunPythonScript(scriptPath, funcName, scriptArgs, OutputCallback);

            funcName = "download_ashare";
            scriptArgs = new string[] { starttime, endtime, frequency, adjust };
            CallPython.RunPythonScript(scriptPath, funcName, scriptArgs, OutputCallback);
        }

        static void OutputCallback(string line)
        {
            AnsiConsole.WriteLine(line);
        }
    }
}