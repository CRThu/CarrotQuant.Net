using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using CarrotBacktesting.NET.Abstraction.Strategy;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CarrotBacktesting.NET.Scripting;

/// <summary>
/// Roslyn 动态 C# 源码策略编译器与加载器。
/// 专为量化投研人员提供“修改源码保存即运行”的免构建体验，同时具备 C# 原生 JIT 机器码执行效率。
/// 内置官方 Microsoft.CodeAnalysis.CSharp 引擎，利用 <see cref="AssemblyLoadContext"/>（isCollectible: true）防止多轮优化时的内存泄露。
/// </summary>
public static class RoslynStrategyCompiler
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences;

    static RoslynStrategyCompiler()
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 尝试从 .NET 运行时受信任平台程序集列表（TPA）抓取全量核心引用
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies && !string.IsNullOrEmpty(trustedAssemblies))
        {
            var paths = trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                references.Add(path);
            }
        }

        // 2. 从当前 AppDomain 已加载的程序集中自动补齐框架自身与宿主引用
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
            {
                references.Add(assembly.Location);
            }
        }

        DefaultReferences = references
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }

    /// <summary>
    /// 从 C# 源码字符串编译并实例化策略。
    /// </summary>
    /// <param name="sourceCode">C# 策略源代码字符串。</param>
    /// <param name="typeName">可选策略类全名或类名。缺省时自动查找首个实现 IStrategy 的非抽象公开类。</param>
    /// <param name="constructorArgs">构造函数参数列表（可选）。</param>
    /// <returns>已实例化的策略对象与承载它的程序集加载上下文（可用于卸载）。</returns>
    public static (IStrategy Strategy, AssemblyLoadContext Context) CompileSource(
        string sourceCode,
        string? typeName = null,
        object?[]? constructorArgs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCode);

        // 1. 生成抽象语法树
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // 2. 创建编译配置（Release 优化等级）
        var assemblyName = $"DynamicStrategy_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true)
        );

        // 3. 编译输出至内存流
        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var lineSpan = d.Location.GetLineSpan();
                    var line = lineSpan.StartLinePosition.Line + 1;
                    var col = lineSpan.StartLinePosition.Character + 1;
                    return $"[Line {line}, Col {col}] {d.GetMessage()}";
                });

            throw new InvalidOperationException($"动态策略源码编译失败:\n{string.Join(Environment.NewLine, errors)}");
        }

        peStream.Seek(0, SeekOrigin.Begin);

        // 4. 使用可回收上下文（Collectible ALC）加载，防参数网格寻优时 OOM
        var alc = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = alc.LoadFromStream(peStream);

        // 5. 查找目标类型（支持 public 与 internal 策略脚本，由私有 ALC 完全隔离）
        Type? targetType;
        if (!string.IsNullOrEmpty(typeName))
        {
            targetType = assembly.GetType(typeName, throwOnError: false, ignoreCase: true)
                         ?? assembly.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));

            if (targetType != null && (!typeof(IStrategy).IsAssignableFrom(targetType) || targetType.IsAbstract))
            {
                alc.Unload();
                throw new TypeLoadException($"指定类型 '{targetType.FullName}' 为抽象类或未实现 IStrategy 契约。");
            }
        }
        else
        {
            targetType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IStrategy).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);
        }

        if (targetType == null)
        {
            alc.Unload();
            throw new TypeLoadException($"在编译的程序集中未找到实现 IStrategy 契约的类型 (typeName='{typeName ?? "<Auto>"}')。");
        }

        // 6. 实例化策略
        object? instance;
        try
        {
            instance = constructorArgs is { Length: > 0 }
                ? Activator.CreateInstance(targetType, constructorArgs)
                : Activator.CreateInstance(targetType);
        }
        catch (Exception ex)
        {
            alc.Unload();
            throw new InvalidOperationException($"策略类型 '{targetType.FullName}' 实例化失败: {ex.Message}", ex);
        }

        if (instance is not IStrategy strategy)
        {
            alc.Unload();
            throw new InvalidCastException($"类型 '{targetType.FullName}' 未实现 IStrategy 接口。");
        }

        return (strategy, alc);
    }

    /// <summary>
    /// 从物理 .cs 源码文件编译并实例化策略。
    /// </summary>
    /// <param name="filePath">.cs 源码物理路径。</param>
    /// <param name="typeName">可选策略类全名或类名。</param>
    /// <param name="constructorArgs">构造函数参数列表（可选）。</param>
    public static (IStrategy Strategy, AssemblyLoadContext Context) CompileFile(
        string filePath,
        string? typeName = null,
        object?[]? constructorArgs = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"找不到指定的策略源文件: '{filePath}'", filePath);
        }

        var sourceCode = File.ReadAllText(filePath);
        return CompileSource(sourceCode, typeName, constructorArgs);
    }
}
