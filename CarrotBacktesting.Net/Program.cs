// See https://aka.ms/new-console-template for more information
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;

Console.WriteLine("Hello, World!");

IEngine engine = new BackTestingEngine(new BasicStrategy());
engine.Run();