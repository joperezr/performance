// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generator;
using System.Threading;
using System.Threading.Tasks;

namespace System
{
    public static class SourceGeneratorRunner
    {
        internal static MetadataReference[] References { get; } = CreateReferences();

        private static MetadataReference[] CreateReferences()
        {
            // Typically we'd want to use the right reference assemblies, but as we're not persisting any
            // assets and only using this for testing purposes, referencing implementation assemblies is sufficient.
            string corelibPath = typeof(object).Assembly.Location;
            return new[]
            {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(corelibPath), "System.Runtime.dll")),
                    MetadataReference.CreateFromFile(typeof(Unsafe).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location),
                };
        }

        internal static Task<IReadOnlyList<Diagnostic>> RunGenerator(
                    string code, SourceGeneratorType sourceGeneratorType, bool compile = true, LanguageVersion langVersion = LanguageVersion.Preview, MetadataReference[] additionalRefs = null, bool allowUnsafe = false, CancellationToken cancellationToken = default)
            => RunGenerator(new[] { (code, "file.g.cs") }, sourceGeneratorType, compile, langVersion, additionalRefs, allowUnsafe, cancellationToken);

        internal static async Task<IReadOnlyList<Diagnostic>> RunGenerator(
                    IEnumerable<(string code, string fileName)> sourceFiles, SourceGeneratorType sourceGeneratorType, bool compile = true, LanguageVersion langVersion = LanguageVersion.Preview, MetadataReference[] additionalRefs = null, bool allowUnsafe = false, CancellationToken cancellationToken = default)
        {
            var proj = new AdhocWorkspace()
                .AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create()))
                .AddProject("SourceGeneratorTest", "SourceGeneratorTest.dll", "C#")
                .WithMetadataReferences(additionalRefs != null ? References.Concat(additionalRefs) : References)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe)
                .WithNullableContextOptions(NullableContextOptions.Enable))
                .WithParseOptions(new CSharpParseOptions(langVersion));

            foreach((string code, string filename) in sourceFiles)
            {
                proj = proj.AddDocument(filename, SourceText.From(code, Encoding.UTF8)).Project;
            }

            proj.Solution.Workspace.TryApplyChanges(proj.Solution);

            Compilation comp = await proj!.GetCompilationAsync(CancellationToken.None).ConfigureAwait(false);
            Debug.Assert(comp != null);

            IIncrementalGenerator generator = sourceGeneratorType switch
            {
                SourceGeneratorType.Regex => new RegexGenerator(),
                _ => throw new ArgumentException($"Invalid {nameof(sourceGeneratorType)} value.")
            };

            CSharpGeneratorDriver cgd = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(langVersion));
            GeneratorDriver gd = cgd.RunGenerators(comp!, cancellationToken);
            GeneratorDriverRunResult generatorResults = gd.GetRunResult();
            if (!compile)
            {
                return generatorResults.Diagnostics;
            }

            comp = comp.AddSyntaxTrees(generatorResults.GeneratedTrees.ToArray());
            EmitResult results = comp.Emit(Stream.Null, cancellationToken: cancellationToken);
            ImmutableArray<Diagnostic> generatorDiagnostics = generatorResults.Diagnostics.RemoveAll(d => d.Severity <= DiagnosticSeverity.Hidden);
            ImmutableArray<Diagnostic> resultsDiagnostics = results.Diagnostics.RemoveAll(d => d.Severity <= DiagnosticSeverity.Hidden);
            if (!results.Success || resultsDiagnostics.Length != 0 || generatorDiagnostics.Length != 0)
            {
                throw new ArgumentException(
                    string.Join(Environment.NewLine, resultsDiagnostics.Concat(generatorDiagnostics)) + Environment.NewLine +
                    string.Join(Environment.NewLine, generatorResults.GeneratedTrees.Select(t => t.ToString())));
            }

            return generatorResults.Diagnostics.Concat(results.Diagnostics).Where(d => d.Severity != DiagnosticSeverity.Hidden).ToArray();
        }
    }

    public enum SourceGeneratorType
    {
        Regex
    }
}