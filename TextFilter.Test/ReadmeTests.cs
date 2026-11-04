// Copyright (c) 2023-2026 ktsu-dev contributors

namespace TextFilter.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ktsu.TextFilter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Keeps README.md, which is also the nuget.org package page, in step with the public API (#138).
/// </summary>
[TestClass]
public partial class ReadmeTests
{
	private static readonly Assembly LibraryAssembly = typeof(TextFilterType).Assembly;
	private const string LibraryNamespace = "ktsu.TextFilter";

	[GeneratedRegex(@"```csharp\r?\n(?<code>.*?)```", RegexOptions.Singleline)]
	private static partial Regex CSharpBlockRegex();

	[GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
	private static partial Regex StringLiteralRegex();

	[GeneratedRegex(@"//[^\n]*")]
	private static partial Regex LineCommentRegex();

	[GeneratedRegex(@"\b(?<type>[A-Z]\w*)\.(?<member>[A-Za-z_]\w*)")]
	private static partial Regex MemberAccessRegex();

	[GeneratedRegex(@"^\| `(?<signature>(?<method>\w+)(?:<\w+>)?\((?<parameters>[^`]*)\))` \|", RegexOptions.Multiline)]
	private static partial Regex ApiTableRowRegex();

	[GeneratedRegex(@"(?<name>\w+) = (?<type>\w+)\.(?<value>\w+)")]
	private static partial Regex DocumentedDefaultRegex();

	private static string ReadReadme()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			string candidate = Path.Join(directory.FullName, "README.md");
			if (File.Exists(candidate) && File.Exists(Path.Join(directory.FullName, "TextFilter.sln")))
			{
				return File.ReadAllText(candidate);
			}
		}

		throw new FileNotFoundException("README.md was not found above the test output directory.");
	}

	private static IEnumerable<string> CSharpCode() =>
		CSharpBlockRegex().Matches(ReadReadme())
			.Select(match => StringLiteralRegex().Replace(match.Groups["code"].Value, "\"\""))
			.Select(code => LineCommentRegex().Replace(code, string.Empty));

	[TestMethod]
	public void ReadmeHasCSharpSamples() => Assert.IsNotEmpty(CSharpCode().ToList());

	[TestMethod]
	public void EveryTypeMemberUsedInReadmeSamplesExists()
	{
		List<string> missing = [];
		foreach (Match access in CSharpCode().SelectMany(code => MemberAccessRegex().Matches(code)))
		{
			string typeName = access.Groups["type"].Value;
			string memberName = access.Groups["member"].Value;
			Type? type = LibraryAssembly.GetType($"{LibraryNamespace}.{typeName}");
			if (type is null || type.GetMember(memberName, BindingFlags.Public | BindingFlags.Static).Length == 0)
			{
				missing.Add($"{typeName}.{memberName}");
			}
		}

		Assert.IsEmpty(missing, $"README samples use names the library doesn't define: {string.Join(", ", missing.Distinct())}");
	}

	[TestMethod]
	public void EveryMethodInTheReadmeApiReferenceExistsWithItsDocumentedDefaults()
	{
		List<Match> rows = [.. ApiTableRowRegex().Matches(ReadReadme()).Cast<Match>()];
		Assert.IsNotEmpty(rows);

		MethodInfo[] methods = typeof(ktsu.TextFilter.TextFilter).GetMethods(BindingFlags.Public | BindingFlags.Static);
		List<string> problems = [];
		foreach (Match row in rows)
		{
			string methodName = row.Groups["method"].Value;
			MethodInfo[] overloads = [.. methods.Where(m => m.Name == methodName)];
			if (overloads.Length == 0)
			{
				problems.Add($"{methodName} does not exist");
				continue;
			}

			foreach (Match documented in DocumentedDefaultRegex().Matches(row.Groups["parameters"].Value))
			{
				string parameterName = documented.Groups["name"].Value;
				string expected = $"{documented.Groups["type"].Value}.{documented.Groups["value"].Value}";
				ParameterInfo[] parameters = [.. overloads.SelectMany(m => m.GetParameters()).Where(p => p.Name == parameterName)];
				if (parameters.Length == 0)
				{
					problems.Add($"{methodName} has no parameter {parameterName}");
				}
				else if (parameters.Any(p => !p.HasDefaultValue || $"{p.ParameterType.Name}.{p.DefaultValue}" != expected))
				{
					problems.Add($"{methodName}({parameterName}) is documented as defaulting to {expected}");
				}
			}
		}

		Assert.IsEmpty(problems, string.Join("; ", problems));
	}

	[TestMethod]
	public void ReadmeMatchOptionsSampleGivesTheDocumentedResults()
	{
		const string text = "Hello beautiful world";
		const string pattern = "hello world";

		Assert.IsTrue(ktsu.TextFilter.TextFilter.IsMatch(text, pattern, TextFilterType.Glob, TextFilterMatchOptions.ByWordAny));
		Assert.IsFalse(ktsu.TextFilter.TextFilter.IsMatch(text, pattern, TextFilterType.Glob, TextFilterMatchOptions.ByWordAll));
		Assert.IsFalse(ktsu.TextFilter.TextFilter.IsMatch(text, pattern, TextFilterType.Glob, TextFilterMatchOptions.ByWholeString));
	}

	[TestMethod]
	public void ReadmeBasicAndFilterTypeSamplesMatch()
	{
		const string text = "Hello, World!";

		Assert.IsTrue(ktsu.TextFilter.TextFilter.IsMatch(text, "Hello*"));
		Assert.IsTrue(ktsu.TextFilter.TextFilter.IsMatch(text, "Hello*", filterType: TextFilterType.Glob));
		Assert.IsTrue(ktsu.TextFilter.TextFilter.IsMatch(text, "^Hello", filterType: TextFilterType.Regex));
		Assert.IsTrue(ktsu.TextFilter.TextFilter.IsMatch(text, "Helo", filterType: TextFilterType.Fuzzy));
	}
}
