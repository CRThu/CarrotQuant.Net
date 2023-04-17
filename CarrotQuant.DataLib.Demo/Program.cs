using CarrotQuant.DataLib;

namespace CarrotQuant.DataLib.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            Console.WriteLine("Call Python Script Demo");

            string scriptPath = "D:\\Projects\\CarrotQuant.Net\\CarrotQuant.Data.Python\\testfunc.py";
            string funcName = "func0";
            string[] scriptArgs = new string[] { };
            string log = CallPython.RunPythonScript(scriptPath, funcName, scriptArgs);
            Console.WriteLine(log);

            funcName = "func1";
            scriptArgs = new string[] { "arg1" };
            log = CallPython.RunPythonScript(scriptPath, funcName, scriptArgs);
            Console.WriteLine(log);

            funcName = "func2";
            scriptArgs = new string[] { "arg1", "arg2" };
            log = CallPython.RunPythonScript(scriptPath, funcName, scriptArgs);
            Console.WriteLine(log);

            funcName = "loop0";
            scriptArgs = new string[] { };
            log = CallPython.RunPythonScript(scriptPath, funcName, scriptArgs);
            Console.WriteLine(log);
        }
    }
}