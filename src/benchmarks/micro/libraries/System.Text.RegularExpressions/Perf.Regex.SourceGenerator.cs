// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using BenchmarkDotNet.Attributes;
using MicroBenchmarks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace System.Text.RegularExpressions.Tests
{
    [BenchmarkCategory(Categories.Libraries, Categories.Regex, Categories.NoWASM)]
    public class Perf_Regex_SourceGenerator
    {
        public enum BenchmarkType
        {
            UsesGenerator,
            DoesNotUseGenerator
        }

        [Params(BenchmarkType.UsesGenerator, BenchmarkType.DoesNotUseGenerator)]
        public BenchmarkType TestType { get; set; }

        private List<(string, string)> _sourceFiles;

        private const string UsesGeneratorCodeTemplate = @"using System.Text.RegularExpressions;
public partial class Class#i#
{
    [RegexGenerator(""(a|b)#i#"", RegexOptions.IgnoreCase)]
    private static partial Regex MyRegex();
}";

        private const string DoesNotUseGeneratorCodeTemplate = @"using System.Text.RegularExpressions;
public partial class Class#i#
{
    [MyCustom(""(a|b)#i#"", RegexOptions.IgnoreCase)]
    private static Regex MyRegex() => throw null;
}";

        [GlobalSetup]
        public void Setup()
        {
            _sourceFiles = new List<(string, string)>();

            if (TestType == BenchmarkType.UsesGenerator)
            {
                for (int i = 0; i < 100; i++)
                {
                    _sourceFiles.Add((UsesGeneratorCodeTemplate.Replace("#i#", i.ToString()), $"Class{i}.g.cs"));
                }
            }
            else
            {
                _sourceFiles.Add((@"using System.Text.RegularExpressions;
using System;
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MyCustomAttribute : Attribute
{
    public MyCustomAttribute(string pattern, RegexOptions options)
    { }
}", $"MyCustomAttribute.cs"));

                for (int i = 0; i < 100; i++)
                {
                    _sourceFiles.Add((DoesNotUseGeneratorCodeTemplate.Replace("#i#", i.ToString()), $"Class{i}.g.cs"));
                }
            }
        }

        [Benchmark]
        public async Task RunRegexGenerator()
            => await SourceGeneratorRunner.RunGenerator(_sourceFiles, SourceGeneratorType.Regex);
    }
}
