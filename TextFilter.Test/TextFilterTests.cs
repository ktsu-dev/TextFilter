// Copyright (c) 2023-2026 ktsu-dev contributors

namespace TextFilter.Test;

using System.Collections.Generic;
using System.Linq;
using ktsu.TextFilter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class TextFilterTests
{
	[TestMethod]
	public void RankWithKeySelectorReturnsCorrectRanking()
	{
		List<(int Id, string Text)> items =
		[
						(1, "hello world"),
						(2, "hello"),
						(3, "world")
					];
		List<(int Id, string Text)> result = [.. TextFilter.Rank(items, item => item.Text, "helo")];
		CollectionAssert.AreEqual(new List<(int, string)> { (2, "hello"), (1, "hello world"), (3, "world") }, result);
	}

	[TestMethod]
	public void RankWithKeySelectorEmptyItemsReturnsEmpty()
	{
		List<(int Id, string Text)> items = [];
		List<(int Id, string Text)> result = [.. TextFilter.Rank(items, item => item.Text, "helo")];
		Assert.IsEmpty(result);
	}

	[TestMethod]
	public void RankWithKeySelectorNullItemsThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Rank<(int, string)>(null!, item => item.Item2, "helo").ToList());
	}

	[TestMethod]
	public void RankWithKeySelectorNullKeySelectorThrowsArgumentNullException()
	{
		List<(int Id, string Text)> items =
		[
						(1, "hello world"),
						(2, "hello"),
						(3, "world")
					];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Rank(items, null!, "helo").ToList());
	}

	[TestMethod]
	public void RankWithKeySelectorNullFilterThrowsArgumentNullException()
	{
		List<(int Id, string Text)> items =
		[
						(1, "hello world"),
						(2, "hello"),
						(3, "world")
					];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Rank(items, item => item.Text, null!).ToList());
	}

	[TestMethod]
	public void FilterGlobByWordAnyReturnsCorrectResults()
	{
		List<string> strings = ["hello world", "hello", "world"];
		List<string> result = [.. TextFilter.Filter(strings, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
		CollectionAssert.AreEqual(new List<string> { "hello world", "hello" }, result);
	}

	[TestMethod]
	public void FilterRegexByWordAnyReturnsCorrectResults()
	{
		List<string> strings = ["hello world", "hello", "world"];
		List<string> result = [.. TextFilter.Filter(strings, "^hello", TextFilterType.Regex, TextFilterMatchOptions.ByWordAny)];
		CollectionAssert.AreEqual(new List<string> { "hello world", "hello" }, result);
	}

	[TestMethod]
	public void FilterFuzzyByWordAnyReturnsCorrectResults()
	{
		List<string> strings = ["hello world", "hello", "world"];
		List<string> result = [.. TextFilter.Filter(strings, "helo", TextFilterType.Fuzzy, TextFilterMatchOptions.ByWordAny)];
		CollectionAssert.AreEqual(new List<string> { "hello", "hello world" }, result);
	}

	[TestMethod]
	public void IsMatchGlobByWordAnyReturnsTrue()
	{
		bool result = TextFilter.IsMatch("hello world", "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Glob pattern 'hello*' should match 'hello world'.");
	}

	[TestMethod]
	public void IsMatchRegexByWordAnyReturnsTrue()
	{
		bool result = TextFilter.IsMatch("hello world", "^hello", TextFilterType.Regex, TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Regex pattern '^hello' should match 'hello world'.");
	}

	[TestMethod]
	public void IsMatchFuzzyByWordAnyReturnsTrue()
	{
		bool result = TextFilter.IsMatch("hello world", "helo", TextFilterType.Fuzzy, TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Fuzzy pattern 'helo' should match 'hello world'.");
	}

	[TestMethod]
	public void DoesMatchGlobByWordAnyReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello*", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Glob pattern 'hello*' should match 'hello world' by word.");
	}

	[TestMethod]
	public void DoesMatchRegexByWordAnyReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^hello", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Regex pattern '^hello' should match 'hello world' by word.");
	}

	[TestMethod]
	public void AnyTokenMatchesGlobFilterReturnsTrue()
	{
		HashSet<string> textTokens = ["hello", "world"];
		bool result = TextFilter.AnyTokenMatchesGlobFilter("hello*", textTokens);
		Assert.IsTrue(result, "At least one token should match glob filter 'hello*'.");
	}

	[TestMethod]
	public void AllTokensMatchGlobFilterReturnsFalse()
	{
		HashSet<string> textTokens = ["hello", "world"];
		bool result = TextFilter.AllTokensMatchGlobFilter("hello*", textTokens);
		Assert.IsFalse(result, "Not all tokens match glob filter 'hello*' so should return false.");
	}

	[TestMethod]
	public void ExtractTextTokensByWholeStringReturnsCorrectTokens()
	{
		HashSet<string> result = TextFilter.ExtractTextTokens("hello world", TextFilterMatchOptions.ByWholeString);
		CollectionAssert.AreEqual(new List<string> { "hello world" }, result.ToList());
	}

	[TestMethod]
	public void ExtractTextTokensByWordAllReturnsCorrectTokens()
	{
		HashSet<string> result = TextFilter.ExtractTextTokens("hello world", TextFilterMatchOptions.ByWordAll);
		CollectionAssert.AreEqual(new List<string> { "hello", "world" }, result.ToList());
	}

	[TestMethod]
	public void ExtractTextTokensByWordAnyReturnsCorrectTokens()
	{
		HashSet<string> result = TextFilter.ExtractTextTokens("hello world", TextFilterMatchOptions.ByWordAny);
		CollectionAssert.AreEqual(new List<string> { "hello", "world" }, result.ToList());
	}

	[TestMethod]
	public void ExtractGlobFilterTokensReturnsCorrectTokens()
	{
		Dictionary<TextFilterTokenType, HashSet<string>> result = TextFilter.ExtractGlobFilterTokens("hello* +required -excluded");
		CollectionAssert.AreEqual(new List<string> { "hello*" }, result[TextFilterTokenType.Optional].ToList());
		CollectionAssert.AreEqual(new List<string> { "required" }, result[TextFilterTokenType.Required].ToList());
		CollectionAssert.AreEqual(new List<string> { "excluded" }, result[TextFilterTokenType.Excluded].ToList());
	}

	[TestMethod]
	public void DoesMatchGlobWithExcludedTokenReturnsFalse()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* -world", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result, "Text containing excluded token 'world' should return false.");
	}

	[TestMethod]
	public void DoesMatchGlobWithRequiredTokenReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* +world", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Text containing required token 'world' should return true.");
	}

	[TestMethod]
	public void DoesMatchGlobWithOptionalTokenReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* world", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Text matching optional tokens should return true.");
	}

	[TestMethod]
	public void DoesMatchRegexWithMultipleTokensReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^hello|world$", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Regex with multiple alternatives should match.");
	}

	[TestMethod]
	public void DoesMatchRegexWithNoMatchReturnsFalse()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^test", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result, "Non-matching regex pattern should return false.");
	}

	[TestMethod]
	public void FilterEmptyStringsReturnsEmpty()
	{
		List<string> strings = [];
		List<string> result = [.. TextFilter.Filter(strings, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
		Assert.IsEmpty(result);
	}

	[TestMethod]
	public void FilterNullStringsThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Filter(null!, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList());
	}

	[TestMethod]
	public void FilterEmptyFilterReturnsAllStrings()
	{
		List<string> strings = ["hello world", "hello", "world"];
		List<string> result = [.. TextFilter.Filter(strings, "", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
		CollectionAssert.AreEqual(strings, result);
	}

	[TestMethod]
	public void FilterNullFilterThrowsArgumentNullException()
	{
		List<string> strings = ["hello world", "hello", "world"];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Filter(strings, null!, TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList());
	}

	[TestMethod]
	public void FilterLargeDatasetPerformance()
	{
		List<string> strings = [.. Enumerable.Range(0, 100000).Select(i => "string" + i)];
		List<string> result = [.. TextFilter.Filter(strings, "string*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
		Assert.HasCount(100000, result);
	}

	[TestMethod]
	public void FilterConcurrentAccess()
	{
		List<string> strings = ["hello world", "hello", "world"];
		List<Task> tasks = [];

		for (int i = 0; i < 100; i++)
		{
			tasks.Add(Task.Run(() =>
			{
				List<string> result = [.. TextFilter.Filter(strings, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
				CollectionAssert.AreEqual(new List<string> { "hello world", "hello" }, result);
			}));
		}

		Task.WaitAll([.. tasks]);
	}

	[TestMethod]
	public void IsMatchNullTextThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.IsMatch(null!, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void IsMatchNullFilterThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.IsMatch("hello world", null!, TextFilterType.Glob, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void AnyTokenMatchesGlobFilterNullFilterTokenThrowsArgumentNullException()
	{
		HashSet<string> textTokens = ["hello", "world"];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.AnyTokenMatchesGlobFilter(null!, textTokens));
	}

	[TestMethod]
	public void AnyTokenMatchesGlobFilterNullTextTokensThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.AnyTokenMatchesGlobFilter("hello*", null!));
	}

	[TestMethod]
	public void AllTokensMatchGlobFilterNullFilterTokenThrowsArgumentNullException()
	{
		HashSet<string> textTokens = ["hello", "world"];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.AllTokensMatchGlobFilter(null!, textTokens));
	}

	[TestMethod]
	public void AllTokensMatchGlobFilterNullTextTokensThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.AllTokensMatchGlobFilter("hello*", null!));
	}

	[TestMethod]
	public void DoesMatchGlobNullTextThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.DoesMatchGlob(null!, "hello*", TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void DoesMatchGlobNullFilterThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.DoesMatchGlob("hello world", null!, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void DoesMatchRegexNullTextThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.DoesMatchRegex(null!, "^hello", TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void DoesMatchRegexNullFilterThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.DoesMatchRegex("hello world", null!, TextFilterMatchOptions.ByWordAny));
	}

	[TestMethod]
	public void RankFuzzyReturnsCorrectRanking()
	{
		List<string> strings = ["hello world", "hello", "world"];
		List<string> result = [.. TextFilter.Rank(strings, "helo")];
		CollectionAssert.AreEqual(new List<string> { "hello", "hello world", "world" }, result);
	}

	[TestMethod]
	public void RankEmptyStringsReturnsEmpty()
	{
		List<string> strings = [];
		List<string> result = [.. TextFilter.Rank(strings, "helo")];
		Assert.IsEmpty(result);
	}

	[TestMethod]
	public void RankNullStringsThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Rank(null!, "helo").ToList());
	}

	[TestMethod]
	public void RankNullFilterThrowsArgumentNullException()
	{
		List<string> strings = ["hello world", "hello", "world"];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Rank(strings, null!).ToList());
	}
	[TestMethod]
	public void FilterWithKeySelectorReturnsCorrectResults()
	{
		List<(int Id, string Text)> items =
		[
									(1, "hello world"),
									(2, "hello"),
									(3, "world")
								];
		List<(int Id, string Text)> result = [.. TextFilter.Filter(items, item => item.Text, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
		CollectionAssert.AreEqual(new List<(int, string)> { (1, "hello world"), (2, "hello") }, result);
	}

	[TestMethod]
	public void FilterWithKeySelectorEmptyItemsReturnsEmpty()
	{
		List<(int Id, string Text)> items = [];
		List<(int Id, string Text)> result = [.. TextFilter.Filter(items, item => item.Text, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny)];
		Assert.IsEmpty(result);
	}

	[TestMethod]
	public void FilterWithKeySelectorNullItemsThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Filter<(int, string)>(null!, item => item.Item2, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList());
	}

	[TestMethod]
	public void FilterWithKeySelectorNullKeySelectorThrowsArgumentNullException()
	{
		List<(int Id, string Text)> items =
		[
									(1, "hello world"),
									(2, "hello"),
									(3, "world")
								];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Filter(items, null!, "hello*", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny).ToList());
	}

	[TestMethod]
	public void FilterWithKeySelectorNullFilterThrowsArgumentNullException()
	{
		List<(int Id, string Text)> items =
		[
									(1, "hello world"),
									(2, "hello"),
									(3, "world")
								];
		Assert.ThrowsExactly<ArgumentNullException>(() => TextFilter.Filter(items, item => item.Text, null!).ToList());
	}
	[TestMethod]
	public void GetHintReturnsCorrectHintForGlob()
	{
		string result = TextFilter.GetHint(TextFilterType.Glob);
		Assert.AreEqual("glob pattern, 'optional1* opti?nal2 +required -excluded' etc, text must contain one of the optional tokens, all of the required tokens, and none of the excluded tokens", result);
	}

	[TestMethod]
	public void GetHintReturnsCorrectHintForRegex()
	{
		string result = TextFilter.GetHint(TextFilterType.Regex);
		Assert.AreEqual("regex pattern, text must match the regex pattern", result);
	}

	[TestMethod]
	public void GetHintReturnsCorrectHintForFuzzy()
	{
		string result = TextFilter.GetHint(TextFilterType.Fuzzy);
		Assert.AreEqual("fuzzy pattern, text is ranked by how well it matches the pattern", result);
	}

	[TestMethod]
	public void GetHintThrowsNotImplementedExceptionForUnknownFilterType()
	{
		Assert.ThrowsExactly<NotImplementedException>(() => TextFilter.GetHint((TextFilterType)999));
	}

	[TestMethod]
	public void DoesMatchRegexValidPatternReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^hello", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Valid regex pattern '^hello' should match 'hello world'.");
	}

	[TestMethod]
	public void DoesMatchRegexInvalidPatternReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "[", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Invalid regex pattern should return true as a fallback.");
	}

	[TestMethod]
	public void DoesMatchRegexByWholeStringReturnsTrue()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^hello world$", TextFilterMatchOptions.ByWholeString);
		Assert.IsTrue(result, "Regex matching whole string should return true.");
	}

	[TestMethod]
	public void DoesMatchRegexByWordAnyReturnsFalse()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^test", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result, "Non-matching regex should return false for ByWordAny.");
	}

	[TestMethod]
	public void DoesMatchRegexByWordAllReturnsFalse()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^test", TextFilterMatchOptions.ByWordAll);
		Assert.IsFalse(result, "Non-matching regex should return false for ByWordAll.");
	}

	[TestMethod]
	public void DoesMatchRegexByWholeStringReturnsFalse()
	{
		bool result = TextFilter.DoesMatchRegex("hello world", "^test$", TextFilterMatchOptions.ByWholeString);
		Assert.IsFalse(result, "Non-matching regex should return false for ByWholeString.");
	}

	[TestMethod]
	public void DoesMatchGlobWithEmptyFilterReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Empty filter should match any text.");
	}

	[TestMethod]
	public void DoesMatchGlobWithAllRequiredTokensReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* +hello +world", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Text containing all required tokens should return true.");
	}

	[TestMethod]
	public void DoesMatchGlobWithMissingRequiredTokenReturnsFalse()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* +missing", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result, "Text missing a required token should return false.");
	}

	[TestMethod]
	public void DoesMatchGlobWithExcludedAndRequiredTokensReturnsFalse()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* +world -hello", TextFilterMatchOptions.ByWordAny);
		Assert.IsFalse(result, "Text containing excluded token should return false even with required token present.");
	}

	[TestMethod]
	public void DoesMatchGlobWithExcludedAndRequiredTokensReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* +world -missing", TextFilterMatchOptions.ByWordAny);
		Assert.IsTrue(result, "Text with required token and not containing excluded token should return true.");
	}

	[TestMethod]
	public void DoesMatchGlobWithByWholeStringOptionReturnsFalse()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello", TextFilterMatchOptions.ByWholeString);
		Assert.IsFalse(result, "Partial match with ByWholeString option should return false.");
	}

	[TestMethod]
	public void DoesMatchGlobWithByWordAllOptionReturnsTrue()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* world*", TextFilterMatchOptions.ByWordAll);
		Assert.IsTrue(result, "All words matching with ByWordAll option should return true.");
	}

	[TestMethod]
	public void DoesMatchGlobWithByWordAllOptionReturnsFalse()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "hello* missing*", TextFilterMatchOptions.ByWordAll);
		Assert.IsFalse(result, "Not all words matching with ByWordAll option should return false.");
	}

	[TestMethod]
	public void DoesMatchGlobHandlesPartialFilter()
	{
		bool result = TextFilter.DoesMatchGlob("hello world", "-", TextFilterMatchOptions.ByWordAll);
		Assert.IsTrue(result, "Partial filter with only '-' should return true.");
	}

	// ---- Case sensitivity (issue #97) ----

	[TestMethod]
	public void GlobIsCaseSensitiveByDefault()
	{
		bool result = TextFilter.IsMatch("IMG_1234.JPG", "*.jpg", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString);
		Assert.IsFalse(result, "Glob matching should remain case sensitive when no sensitivity is requested.");
	}

	[TestMethod]
	public void GlobMatchesAcrossCaseWhenCaseInsensitiveIsRequested()
	{
		bool result = TextFilter.IsMatch("IMG_1234.JPG", "*.jpg", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive);
		Assert.IsTrue(result, "'*.jpg' should match 'IMG_1234.JPG' when case insensitive matching is requested.");
	}

	[TestMethod]
	public void GlobCaseInsensitivityAppliesToTheFilterAsWellAsTheText()
	{
		bool result = TextFilter.IsMatch("photo.png", "*.PNG", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive);
		Assert.IsTrue(result, "An uppercase filter should match lowercase text when case insensitive matching is requested.");
	}

	[TestMethod]
	public void GlobCaseInsensitivityDoesNotMatchUnrelatedText()
	{
		bool result = TextFilter.IsMatch("IMG_1234.PNG", "*.jpg", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive);
		Assert.IsFalse(result, "Case insensitivity should fold case only, not widen the match to a different extension.");
	}

	[TestMethod]
	public void RegexIsCaseSensitiveByDefault()
	{
		bool result = TextFilter.IsMatch("IMG_1234.JPG", @".*\.jpg", TextFilterType.Regex, TextFilterMatchOptions.ByWholeString);
		Assert.IsFalse(result, "Regex matching should remain case sensitive when no sensitivity is requested.");
	}

	[TestMethod]
	public void RegexMatchesAcrossCaseWhenCaseInsensitiveIsRequested()
	{
		bool result = TextFilter.IsMatch("IMG_1234.JPG", @".*\.jpg", TextFilterType.Regex, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive);
		Assert.IsTrue(result, "The sensitivity setting should reach the regex path, not only the glob path.");
	}

	[TestMethod]
	public void TheTwoSensitivitiesDoNotCollideInTheGlobCache()
	{
		// Both caches are keyed by pattern text. Without the sensitivity in the key, whichever of
		// these ran first would decide the answer for the other, in whichever order they ran.
		Assert.IsFalse(TextFilter.IsMatch("A.TXT", "*.txt", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseSensitive));
		Assert.IsTrue(TextFilter.IsMatch("A.TXT", "*.txt", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive));

		// Same check with the insensitive variant cached first, on a pattern used nowhere else, so
		// the isolation holds in both orders rather than only the one the pair above happens to take.
		Assert.IsTrue(TextFilter.IsMatch("B.MD", "*.md", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive));
		Assert.IsFalse(TextFilter.IsMatch("B.MD", "*.md", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseSensitive));
	}

	[TestMethod]
	public void TheTwoSensitivitiesDoNotCollideInTheRegexCache()
	{
		Assert.IsFalse(TextFilter.IsMatch("C.TXT", @".*\.txt", TextFilterType.Regex, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseSensitive));
		Assert.IsTrue(TextFilter.IsMatch("C.TXT", @".*\.txt", TextFilterType.Regex, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive));
	}

	[TestMethod]
	public void CaseInsensitivityReachesRequiredAndExcludedTokens()
	{
		// Required and excluded tokens go through their own call sites, so they need their own guard:
		// threading the sensitivity into the optional branch alone would leave these two behind.
		Assert.IsTrue(TextFilter.IsMatch("READ ME", "+read", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity.CaseInsensitive),
			"A required token should honour case insensitivity.");
		Assert.IsFalse(TextFilter.IsMatch("READ ME", "-read", TextFilterType.Glob, TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity.CaseInsensitive),
			"An excluded token should honour case insensitivity.");
	}

	[TestMethod]
	public void FilterHonoursCaseInsensitivity()
	{
		List<string> strings = ["IMG_1.JPG", "IMG_2.PNG", "notes.txt"];
		List<string> result = [.. TextFilter.Filter(strings, "*.jpg", TextFilterType.Glob, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive)];
		CollectionAssert.AreEqual(new List<string> { "IMG_1.JPG" }, result, "Filter should pass the sensitivity through to IsMatch.");
	}

	[TestMethod]
	public void FuzzyMatchingIsAlwaysCaseInsensitive()
	{
		// Pins the claim made in TextFilterCaseSensitivity's own docs. The setting is deliberately
		// ignored here, so both values must agree - and both must agree with today's behaviour.
		Assert.IsTrue(TextFilter.IsMatch("HELLO", "hello", TextFilterType.Fuzzy, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseSensitive));
		Assert.IsTrue(TextFilter.IsMatch("HELLO", "hello", TextFilterType.Fuzzy, TextFilterMatchOptions.ByWholeString, TextFilterCaseSensitivity.CaseInsensitive));
	}
}
