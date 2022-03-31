// See https://aka.ms/new-console-template for more information
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;

Console.WriteLine("Hello, World!");

IEngine engine = new BackTestingEngine(new BasicStrategy());
engine.Run();