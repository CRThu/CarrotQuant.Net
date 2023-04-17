using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.DataLib
{
    public class CallPython
    {
        // Create new process start info
        public static string RunPythonScript(string scriptPath, string funcName, string[] args)
        {
            ProcessStartInfo start = new ProcessStartInfo();

            // Set the python interpreter path
            string pythonPath = "";
            // Find python interpreter path in environment variables
            string[] possiblePythonPaths = Environment.GetEnvironmentVariable("PATH").Split(';');
            foreach (string path in possiblePythonPaths)
            {
                string pythonExePath = Path.Combine(path, "python.exe");
                if (File.Exists(pythonExePath))
                {
                    pythonPath = pythonExePath;
                    break;
                }
            }
            if (pythonPath == "")
            {
                // If python interpreter not found in environment variables, search in common python and anaconda paths
                string[] commonPythonPaths = { @"C:\Python27\python.exe", @"C:\Python36\python.exe", @"C:\Program Files\Python38\python.exe", @"C:\Program Files\Anaconda3\python.exe", $@"C:\Users\{Environment.UserName}\anaconda3\python.exe" };
                foreach (string path in commonPythonPaths)
                {
                    if (File.Exists(path))
                    {
                        pythonPath = path;
                        break;
                    }
                }
                if (pythonPath == "")
                {
                    throw new Exception("Python interpreter not found.");
                }
            }
            start.FileName = pythonPath;

            // Set the path of the python script to be executed and the arguments to be passed
            start.Arguments = scriptPath + " " + funcName + " " + string.Join(" ", args);
            // Redirect standard output to the console
            start.RedirectStandardOutput = true;
            // Set the output encoding
            start.StandardOutputEncoding = Encoding.UTF8;
            // UseShellExecute must be false in order to redirect output
            start.UseShellExecute = false;

            // Create new process
            Process process = new Process();
            // Assign the start info to the process
            process.StartInfo = start;
            // Start the process
            process.Start();

            // Read the output from the process
            StringBuilder outputBuilder = new StringBuilder();
            while (!process.StandardOutput.EndOfStream)
            {
                string line = process.StandardOutput.ReadLine();
                // Do something with the line of output
                outputBuilder.AppendLine(line);
            }

            string output = outputBuilder.ToString();

            // Wait for the process to exit
            process.WaitForExit();

            // Process the output
            // Do something with the output
            return output;
        }

    }
}
