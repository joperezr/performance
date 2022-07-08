// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using BenchmarkDotNet.Attributes;
using MicroBenchmarks;
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

        private string _code;

        private const string UsesGeneratorCodeTemplate = @"
public partial class Class#i#
{
    [RegexGenerator(""(a|b)#i#"", RegexOptions.IgnoreCase)]
    private static partial Regex MyRegex();
}";

        private const string DoesNotUseGeneratorCodeTemplate = @"
public partial class Class#i#
{
    [MyCustom(""(a|b)#i#"", RegexOptions.IgnoreCase)]
    private static Regex MyRegex() => throw null;
}";

        [GlobalSetup]
        public void Setup()
        {
            if (TestType == BenchmarkType.UsesGenerator)
            {
                StringBuilder sb = new StringBuilder("using System.Text.RegularExpressions;");
                for (int i = 0; i < 100; i++)
                {
                    sb.Append(UsesGeneratorCodeTemplate.Replace("#i#", i.ToString()));
                }
                _code = sb.ToString();
            }
            else
            {
                StringBuilder sb = new StringBuilder(@"using System.Text.RegularExpressions;
using System;
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MyCustomAttribute : Attribute
{
    public MyCustomAttribute(string pattern, RegexOptions options)
    { }
}");
                for (int i = 0; i < 100; i++)
                {
                    sb.Append(DoesNotUseGeneratorCodeTemplate.Replace("#i#", i.ToString()));
                }
                _code = sb.ToString();
            }
        }

        [Benchmark]
        public async Task RunRegexGenerator()
            => await SourceGeneratorRunner.RunGenerator(_code, SourceGeneratorType.Regex);
    }
}
