using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Strategy;

/// <summary>
/// 策略管线装配工厂类（纯粹的策略对象装配，与 YAML 解析彻底解耦）。
/// 接收强类型 <see cref="PipelineConfiguration"/> 配置模型，递归装配嵌套子管线、源码策略与属性注入。
/// YAML 文件的反序列化统一由 <see cref="PipelineConfigLoader"/> 权威负责。
/// </summary>
public static class StrategyPipelineFactory
{
    /// <summary>
    /// 根据强类型策略管线配置模型装配策略管线实例。
    /// </summary>
    /// <param name="config">策略管线强类型配置模型。</param>
    /// <param name="baseDirectory">相对路径解析基准目录（可选，缺省为当前工作目录）。</param>
    /// <param name="autoCompile">装配完成后是否立即执行 Compile 密封优化（默认为 true）。</param>
    /// <returns>已装配完成的策略管线实例。</returns>
    public static StrategyPipeline Create(PipelineConfiguration config, string? baseDirectory = null, bool autoCompile = true)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Pipeline == null || config.Pipeline.Count == 0)
        {
            throw new InvalidOperationException("策略管线配置未包含有效的 'pipeline' 节点列表。");
        }

        baseDirectory ??= Directory.GetCurrentDirectory();
        var pipeline = CreatePipelineInstance(config.Mode, config.Name);

        if (pipeline is ParallelPipeline parallel)
        {
            parallel.Concurrent = config.Concurrent;
        }

        foreach (var node in config.Pipeline)
        {
            var strategy = CreateStrategyFromNode(node, baseDirectory, pipeline);
            pipeline.Add(strategy);
        }

        if (autoCompile)
        {
            pipeline.Compile();
        }

        return pipeline;
    }

    /// <summary>
    /// 便捷方法：从 YAML 配置文件物理路径加载并装配策略管线。
    /// 内部由 <see cref="PipelineConfigLoader.Load"/> 负责 YAML 反序列化，本工厂负责管线装配。
    /// </summary>
    /// <param name="yamlFilePath">YAML 配置文件物理路径。</param>
    /// <param name="autoCompile">装配完成后是否立即执行 Compile 密封优化（默认为 true）。</param>
    public static StrategyPipeline Create(string yamlFilePath, bool autoCompile = true)
    {
        var config = PipelineConfigLoader.Load(yamlFilePath);
        string? baseDir = Path.GetDirectoryName(Path.GetFullPath(yamlFilePath));
        return Create(config, baseDir, autoCompile);
    }

    private static StrategyPipeline CreatePipelineInstance(PipelineMode mode, string? name)
    {
        StrategyPipeline pipeline = mode switch
        {
            PipelineMode.Sequential => new SequentialPipeline(),
            PipelineMode.Parallel => new ParallelPipeline(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"未知的管线执行模式: {mode}")
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            pipeline.Name = name;
        }

        return pipeline;
    }

    private static Type ResolveStrategyType(string typeName)
    {
        var targetType = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (targetType == null)
        {
            // 在当前 AppDomain 中已加载的程序集中查找
            targetType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetExportedTypes(); } catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
        }

        if (targetType == null || !typeof(IStrategy).IsAssignableFrom(targetType) || targetType.IsAbstract)
        {
            throw new TypeLoadException($"无法解析或实例化策略类型: '{typeName}'。");
        }

        return targetType;
    }

    private static IStrategy CreateStrategyFromNode(StrategyNodeConfiguration node, string baseDirectory, StrategyPipeline parentPipeline)
    {
        IStrategy strategy;

        // 1. 嵌套子管线支持 (Composite Pipeline)
        if (node.Pipeline != null)
        {
            var subPipeline = CreatePipelineInstance(node.Mode, node.Name);
            if (subPipeline is ParallelPipeline parallelSub)
            {
                parallelSub.Concurrent = node.Concurrent;
            }
            foreach (var childNode in node.Pipeline)
            {
                subPipeline.Add(CreateStrategyFromNode(childNode, baseDirectory, subPipeline));
            }
            strategy = subPipeline;
        }
        // 2. 指定了 source (.cs 源码文件，Roslyn 动态内存编译)
        else if (!string.IsNullOrWhiteSpace(node.Source))
        {
            var sourcePath = Path.IsPathRooted(node.Source)
                ? node.Source
                : Path.Combine(baseDirectory, node.Source);

            var (compiledStrategy, alc) = CarrotBacktesting.NET.Scripting.RoslynStrategyCompiler.CompileFile(sourcePath, node.Type);
            parentPipeline.RegisterOnDispose(() =>
            {
                try { alc.Unload(); } catch (InvalidOperationException) { }
            });
            strategy = compiledStrategy;
        }
        // 3. 指定了已存在/已加载的策略类型名称 (type)
        else if (!string.IsNullOrWhiteSpace(node.Type))
        {
            var targetType = ResolveStrategyType(node.Type);
            strategy = (IStrategy)Activator.CreateInstance(targetType)!;
        }
        else
        {
            throw new InvalidOperationException("策略节点配置必须指定 'pipeline' (子管线)、'source' (源码) 或 'type' (类型)。");
        }

        // 4. 属性参数注入 (Params Injection)
        if (node.Params != null && node.Params.Count > 0)
        {
            ConfigPropertyInjector.Inject(strategy, node.Params, strategy.Name);
        }

        return strategy;
    }
}
