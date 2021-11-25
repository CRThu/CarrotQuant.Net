using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model
{
    public static class CodeCompiler
    {
        private static readonly IEnumerable<PortableExecutableReference> Assemblies =
            AppDomain.CurrentDomain.GetAssemblies()
            .Select(x => MetadataReference.CreateFromFile(x.Location));

        public static Type Run(string code, string className)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var type = CompileType(className, syntaxTree);
            return type;
        }

        private static Type CompileType(string originalClassName, SyntaxTree syntaxTree)
        {
            // 指定编译选项
            var assemblyName = $"{originalClassName}.g";

            CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, new[] { syntaxTree },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(
                // 引用
                Assemblies
                );

            // 编译到内存流
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                if (result.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    var assembly = Assembly.Load(ms.ToArray());
                    return assembly.GetTypes().First(x => x.Name == originalClassName);
                }
                throw new Exception(string.Join("\n", result.Diagnostics));
            }
        }
    }
}
