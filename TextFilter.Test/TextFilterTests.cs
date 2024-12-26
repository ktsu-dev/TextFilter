namespace TextFilter.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using ktsu.TextFilter;

[TestClass]
public class TextFilterTests
{
	[TestMethod]
	public void GetHintGlobReturnsCorrectHint()
	{
		string hint = TextFilter.GetHint(TextFilterType.Glob);
		Assert.AreEqual("glob pattern, 'optional1* opti?nal2 +required -excluded' etc, text must contain one of the optional tokens, all of the required tokens, and none of the excluded tokens", hint);
	}

	[TestMethod]
	public void GetHintRegexReturnsCorrectHint()
	{
		string hint = TextFilter.GetHint(TextFilterType.Regex);
		Assert.AreEqual("regex pattern, text must match the regex pattern", hint);
	}

	[TestMethod]
	public void GetHintFuzzyReturnsCorrectHint()
	{
		string hint = TextFilter.GetHint(TextFilterType.Fuzzy);
		Assert.AreEqual("fuzzy pattern, text is ranked by how well it matches the pattern", hint);
	}

	[TestMethod]
	public void FilterGlobByWordAnyReturnsCorrectResults()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		var result = TextFilter.Filter(strings, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList();
		CollectionAssert.AreEqual(new List<string> { "hello world", "hello" }, result);
	}

	[TestMethod]
	public void FilterRegexByWordAnyReturnsCorrectResults()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		var result = TextFilter.Filter(strings, "^hello", TextFilterType.Regex, TextFilterMatchOptions.ByWordAny).ToList();
		CollectionAssert.AreEqual(new List<string> { "hello world", "hello" }, result);
	}

	[TestMethod]
	public void FilterFuzzyByWordAnyReturnsCorrectResults()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		var result = TextFilter.Filter(strings, "helo", TextFilterType.Fuzzy, TextFilterMatchOptions.ByWordAny).ToList();
		CollectionAssert.AreEqual(new List<string> { "hello", "hello world" }, result);
	}

	[TestMethod]
	public void IsMatchGlobByWordAnyReturnsTrue()
	{
		bool result = TextFilter.IsMatch("hello world", "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsMatchRegexByWordAnyReturnsTrue()
	{
		bool result = TextFilter.IsMatch("hello world", "^hello", TextFilterType.Regex, TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void IsMatchFuzzyByWordAnyReturnsTrue()
	{
		bool result = TextFilter.IsMatch("hello world", "helo", TextFilterType.Fuzzy, TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void DoesMatchGlobByWordAnyReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello*", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void DoesMatchRegexByWordAnyReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^hello", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void AnyTokenMatchesGlobFilterReturnsTrue()
	{
		var textTokens = new HashSet<string> { "hello", "world" };
		bool result = TextFilter.AnyTokenMatchesGlobFilter("hello*", textTokens);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void AllTokensMatchGlobFilterReturnsFalse()
	{
		var textTokens = new HashSet<string> { "hello", "world" };
		bool result = TextFilter.AllTokensMatchGlobFilter("hello*", textTokens);
		Assert.IsFalse(result);
	}

	[TestMethod]
	public void ExtractTextTokensByWholeStringReturnsCorrectTokens()
	{
		var result = TextFilter.ExtractTextTokens("hello world", TextFilterMatchOptions.ByWholeString);
		CollectionAssert.AreEqual(new List<string> { "hello world" }, result.ToList());
	}

	[TestMethod]
	public void ExtractTextTokensByWordAllReturnsCorrectTokens()
	{
		var result = TextFilter.ExtractTextTokens("hello world", TextFilterMatchOptions.ByWordAll);
		CollectionAssert.AreEqual(new List<string> { "hello", "world" }, result.ToList());
	}

	[TestMethod]
	public void ExtractTextTokensByWordAnyReturnsCorrectTokens()
	{
		var result = TextFilter.ExtractTextTokens("hello world", TextFilterMatchOptions.ByWordAny);
		CollectionAssert.AreEqual(new List<string> { "hello", "world" }, result.ToList());
	}

	[TestMethod]
	public void ExtractGlobFilterTokensReturnsCorrectTokens()
	{
		var result = TextFilter.ExtractGlobFilterTokens("hello* +required -excluded");
		CollectionAssert.AreEqual(new List<string> { "hello*" }, result[TextFilterTokenType.Optional].ToList());
		CollectionAssert.AreEqual(new List<string> { "required" }, result[TextFilterTokenType.Required].ToList());
		CollectionAssert.AreEqual(new List<string> { "excluded" }, result[TextFilterTokenType.Excluded].ToList());
	}

	[TestMethod]
	public void DoesMatchGlobWithExcludedTokenReturnsFalse()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* -world", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result);
	}

	[TestMethod]
	public void DoesMatchGlobWithRequiredTokenReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* +world", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void DoesMatchGlobWithOptionalTokenReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* world", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void DoesMatchRegexWithMultipleTokensReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^hello|world$", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result);
	}

	[TestMethod]
	public void DoesMatchRegexWithNoMatchReturnsFalse()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^test", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result);
	}

	[TestMethod]
	public void FilterEmptyStringsReturnsEmpty()
	{
		var strings = new List<string>();
		var result = TextFilter.Filter(strings, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList();
		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void FilterNullStringsThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.Filter(null!, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList());
	}

	[TestMethod]
	public void FilterEmptyFilterReturnsAllStrings()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		var result = TextFilter.Filter(strings, "", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList();
		CollectionAssert.AreEqual(strings, result);
	}

	[TestMethod]
	public void FilterNullFilterThrowsArgumentNullException()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.Filter(strings, null!, TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList());
	}

	[TestMethod]
	public void FilterLargeDatasetPerformance()
	{
		var strings = Enumerable.Range(0, 100000).Select(i => "string" + i).ToList();
		var result = TextFilter.Filter(strings, "string*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList();
		Assert.AreEqual(100000, result.Count);
	}

	[TestMethod]
	public void FilterConcurrentAccess()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		var tasks = new List<Task>();

		for (int i = 0; i < 100; i++)
		{
			tasks.Add(Task.Run(() =>
			{
				var result = TextFilter.Filter(strings, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList();
				CollectionAssert.AreEqual(new List<string> { "hello world", "hello" }, result);
			}));
		}

		Task.WaitAll([.. tasks]);
	}

	[TestMethod]
	public void IsMatchNullTextThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.IsMatch(null!, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void IsMatchNullFilterThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.IsMatch("hello world", null!, TextFilterType.Glob, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void AnyTokenMatchesGlobFilterNullFilterTokenThrowsArgumentNullException()
	{
		var textTokens = new HashSet<string> { "hello", "world" };
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.AnyTokenMatchesGlobFilter(null!, textTokens));
	}

	[TestMethod]
	public void AnyTokenMatchesGlobFilterNullTextTokensThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.AnyTokenMatchesGlobFilter("hello*", null!));
	}

	[TestMethod]
	public void AllTokensMatchGlobFilterNullFilterTokenThrowsArgumentNullException()
	{
		var textTokens = new HashSet<string> { "hello", "world" };
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.AllTokensMatchGlobFilter(null!, textTokens));
	}

	[TestMethod]
	public void AllTokensMatchGlobFilterNullTextTokensThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.AllTokensMatchGlobFilter("hello*", null!));
	}

	[TestMethod]
	public void DoesMatchGlobNullTextThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.DoesMatchGlob(null!, "hello*", TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void DoesMatchGlobNullFilterThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.DoesMatchGlob("hello world", null!, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void DoesMatchRegexNullTextThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.DoesMatchRegex(null!, "^hello", TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void DoesMatchRegexNullFilterThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.DoesMatchRegex("hello world", null!, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void RankFuzzyReturnsCorrectRanking()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		var result = TextFilter.Rank(strings, "helo").ToList();
		CollectionAssert.AreEqual(new List<string> { "hello", "hello world", "world" }, result);
	}

	[TestMethod]
	public void RankEmptyStringsReturnsEmpty()
	{
		var strings = new List<string>();
		var result = TextFilter.Rank(strings, "helo").ToList();
		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void RankNullStringsThrowsArgumentNullException()
	{
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.Rank(null!, "helo").ToList());
	}

	[TestMethod]
	public void RankNullFilterThrowsArgumentNullException()
	{
		var strings = new List<string> { "hello world", "hello", "world" };
		Assert.ThrowsException<ArgumentNullException>(() => TextFilter.Rank(strings, null!).ToList());
	}
}
