// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.TextFilter;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using DotNet.Globbing;

using ktsu.FuzzySearch;

/// <summary>
/// Specifies the type of text filter to be used.
/// </summary>
public enum TextFilterType
{
	/// <summary>
	/// Specifies the type of text filter to be used.
	/// </summary>
	Glob,
	/// <summary>
	/// Specifies the type of text filter to be used.
	/// </summary>
	Regex,
	/// <summary>
	/// Specifies the type of text filter to be used.
	/// </summary>
	Fuzzy,
}

/// <summary>
/// Specifies the options for matching text filters.
/// </summary>
public enum TextFilterMatchOptions
{
	/// <summary>
	/// Specifies the options for matching text filters by whole string.
	/// </summary>
	ByWholeString,
	/// <summary>
	/// Specifies the options for matching text filters by all words.
	/// </summary>
	ByWordAll,
	/// <summary>
	/// Specifies the options for matching text filters by any word.
	/// </summary>
	ByWordAny,
}

/// <summary>
/// Specifies whether a filter distinguishes uppercase from lowercase characters.
/// </summary>
/// <remarks>
/// Applies to the <see cref="TextFilterType.Glob"/> and <see cref="TextFilterType.Regex"/> filter
/// types, which are case sensitive by default. <see cref="TextFilterType.Fuzzy"/> matching is
/// unaffected: it is always case insensitive, and there is no way to make it otherwise.
/// </remarks>
public enum TextFilterCaseSensitivity
{
	/// <summary>
	/// Uppercase and lowercase characters are distinct, so <c>*.jpg</c> does not match <c>IMG.JPG</c>.
	/// </summary>
	CaseSensitive,
	/// <summary>
	/// Uppercase and lowercase characters are equivalent, so <c>*.jpg</c> matches <c>IMG.JPG</c>.
	/// </summary>
	CaseInsensitive,
}

internal enum TextFilterTokenType
{
	Optional,
	Required,
	Excluded,
}

/// <summary>
/// Provides methods for filtering text based on different filter types and match options.
/// </summary>
public static partial class TextFilter
{
	private static HashSet<char> ExcludedTokenPrefixes { get; } = ['!', '-', '^'];
	private static HashSet<char> RequiredTokenPrefixes { get; } = ['+'];
	private static ConcurrentDictionary<string, Regex> RegexCache { get; } = [];
	private static ConcurrentDictionary<string, Glob> GlobCache { get; } = [];

	// Both caches are keyed by pattern text, so the same pattern compiled at two sensitivities would
	// otherwise collide on the first one cached. The sensitivity is folded into the key rather than
	// using a tuple key, which netstandard2.0 does not get for free.
	private static string CacheKey(string pattern, TextFilterCaseSensitivity caseSensitivity) =>
		caseSensitivity is TextFilterCaseSensitivity.CaseInsensitive ? "i:" + pattern : "s:" + pattern;

	private static readonly GlobOptions CaseInsensitiveGlobOptions = new() { Evaluation = { CaseInsensitive = true } };

	// Filter patterns are caller-supplied text, so a pattern with catastrophic backtracking would
	// otherwise run unbounded on the calling thread. One second is far longer than any legitimate
	// filter needs and short enough that a pathological one cannot wedge a UI.
	private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "SYSLIB1045:Convert to 'GeneratedRegexAttribute'.", Justification = "Not available in older frameworks")]
	private static Regex RegexMatchAnything() => new(".*", RegexOptions.Compiled);

	/// <summary>
	/// Gets a hint for the specified filter type.
	/// </summary>
	/// <param name="filterType">The type of the filter.</param>
	/// <returns>A hint string describing the filter pattern.</returns>
	public static string GetHint(TextFilterType filterType)
	{
		return filterType switch
		{
			TextFilterType.Glob => "glob pattern, 'optional1* opti?nal2 +required -excluded' etc, text must contain one of the optional tokens, all of the required tokens, and none of the excluded tokens",
			TextFilterType.Regex => "regex pattern, text must match the regex pattern",
			TextFilterType.Fuzzy => "fuzzy pattern, text is ranked by how well it matches the pattern",
			_ => throw new NotImplementedException($"{nameof(TextFilterType)}.{filterType} has not been implemented"),
		};
	}

	/// <summary>
	/// Filters the specified collection of strings based on the provided filter and filter type.
	/// </summary>
	/// <param name="strings">The collection of strings to filter.</param>
	/// <param name="filter">The filter pattern.</param>
	/// <param name="filterType">The type of the filter.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase. Ignored by <see cref="TextFilterType.Fuzzy"/>.</param>
	/// <returns>A collection of strings that match the filter.</returns>
	/// <remarks>When using fuzzy matching, the strings are sorted by their match score.</remarks>
	public static IEnumerable<string> Filter(IEnumerable<string> strings, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive) =>
		Filter(strings, s => s, filter, filterType, textFilterMatchOptions, caseSensitivity);

	/// <summary>
	/// Filters the specified collection of items based on the provided filter and filter type.
	/// </summary>
	/// <typeparam name="TItem">The type of the items in the collection.</typeparam>
	/// <param name="items">The collection of items to filter.</param>
	/// <param name="keySelector">A function to extract the string key from an item.</param>
	/// <param name="filter">The filter pattern.</param>
	/// <param name="filterType">The type of the filter.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase. Ignored by <see cref="TextFilterType.Fuzzy"/>.</param>
	/// <returns>A collection of items that match the filter.</returns>
	/// <remarks>When using fuzzy matching, the items are sorted by their match score.</remarks>
	public static IEnumerable<TItem> Filter<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
	{
		Ensure.NotNull(items);
		Ensure.NotNull(keySelector);
		Ensure.NotNull(filter);

		return items.Select(item =>
		{
			bool isMatch = IsMatch(keySelector(item), filter, out int score, filterType, textFilterMatchOptions, caseSensitivity);
			return (item, isMatch, score);
		})
		.Where(t => t.isMatch)
		.OrderByDescending(t => t.score)
		.Select(t => t.item);
	}

	/// <summary>
	/// Ranks the specified collection of strings based on the provided fuzzy filter pattern.
	/// </summary>
	/// <param name="strings">The collection of strings to rank.</param>
	/// <param name="fuzzyFilter">The filter pattern.</param>
	/// <returns>The collection of strings sorted by their match score.</returns>
	/// <remarks>Uses fuzzy matching to rank the strings by their match score.</remarks>
	public static IEnumerable<string> Rank(IEnumerable<string> strings, string fuzzyFilter) =>
		Rank(strings, s => s, fuzzyFilter);

	/// <summary>
	/// Ranks the specified collection of items based on the provided fuzzy filter pattern.
	/// </summary>
	/// <typeparam name="TItem">The type of the items in the collection.</typeparam>
	/// <param name="items">The collection of items to rank.</param>
	/// <param name="keySelector">A function to extract the string key from an item.</param>
	/// <param name="fuzzyFilter">The fuzzy filter pattern.</param>
	/// <returns>The collection of items sorted by their match score.</returns>
	/// <remarks>Uses fuzzy matching to rank the items by their match score.</remarks>
	public static IEnumerable<TItem> Rank<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector, string fuzzyFilter)
	{
		Ensure.NotNull(items);
		Ensure.NotNull(keySelector);
		Ensure.NotNull(fuzzyFilter);

		return items.Select(item =>
		{
			bool isMatch = IsMatch(keySelector(item), fuzzyFilter, out int score, TextFilterType.Fuzzy);
			return (item, isMatch, score);
		})
		.OrderByDescending(t => t.score)
		.Select(t => t.item);
	}

	/// <summary>
	/// Determines whether the specified text matches the filter pattern.
	/// </summary>
	/// <param name="text">The text to match.</param>
	/// <param name="filter">The filter pattern.</param>
	/// <param name="score">The score of the match (used with fuzzy matching).</param>
	/// <param name="filterType">The type of the filter.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase. Ignored by <see cref="TextFilterType.Fuzzy"/>.</param>
	/// <returns><c>true</c> if the text matches the filter pattern; otherwise, <c>false</c>.</returns>
	public static bool IsMatch(string text, string filter, out int score, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
	{
		Ensure.NotNull(text);
		Ensure.NotNull(filter);

		score = int.MinValue;

		return string.IsNullOrWhiteSpace(filter)
			|| filterType switch
			{
				TextFilterType.Glob => DoesMatchGlob(text, filter, textFilterMatchOptions, caseSensitivity),
				TextFilterType.Regex => DoesMatchRegex(text, filter, textFilterMatchOptions, caseSensitivity),
				TextFilterType.Fuzzy => Fuzzy.Contains(text.AsSpan(), filter.AsSpan(), out score),
				_ => throw new NotImplementedException($"{nameof(TextFilterType)}.{filterType} has not been implemented"),
			};
	}

	/// <summary>
	/// Determines whether the specified text matches the filter pattern.
	/// </summary>
	/// <param name="text">The text to match.</param>
	/// <param name="filter">The filter pattern.</param>
	/// <param name="filterType">The type of the filter.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase. Ignored by <see cref="TextFilterType.Fuzzy"/>.</param>
	/// <returns><c>true</c> if the text matches the filter pattern; otherwise, <c>false</c>.</returns>
	public static bool IsMatch(string text, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
		=> IsMatch(text, filter, out _, filterType, textFilterMatchOptions, caseSensitivity);

	internal static HashSet<string> ExtractTextTokens(string text, TextFilterMatchOptions textFilterMatchOptions)
	{
		return textFilterMatchOptions switch
		{
			TextFilterMatchOptions.ByWholeString => [text],
			TextFilterMatchOptions.ByWordAll => [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())],
			TextFilterMatchOptions.ByWordAny => [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())],
			_ => throw new NotImplementedException($"{nameof(TextFilterMatchOptions)}.{textFilterMatchOptions} has not been implemented"),
		};
	}

	internal static Dictionary<TextFilterTokenType, HashSet<string>> ExtractGlobFilterTokens(string filter)
	{
		string[] filterTokens = [.. filter.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())];
		return filterTokens.GroupBy(t =>
		{
			char prefix = t.First();

			return ExcludedTokenPrefixes.Contains(prefix)
				? TextFilterTokenType.Excluded
				: RequiredTokenPrefixes.Contains(prefix)
				? TextFilterTokenType.Required
				: TextFilterTokenType.Optional;
		})
		.Select(g =>
		{
			bool removePrefix = g.Key is TextFilterTokenType.Required or TextFilterTokenType.Excluded;
			return new
			{
				g.Key,
				Value = (removePrefix ? g.Select(t => t[1..]) : g).Where(t => !string.IsNullOrEmpty(t)).ToHashSet(),
			};
		})
		.Where(g => g.Value.Count != 0)
		.ToDictionary(g => g.Key, g => g.Value);
	}

	/// <summary>
	/// Determines whether the specified text matches the glob filter pattern.
	/// </summary>
	/// <param name="text">The text to match.</param>
	/// <param name="filter">The glob filter pattern.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase.</param>
	/// <returns><c>true</c> if the text matches the glob filter pattern; otherwise, <c>false</c>.</returns>
	public static bool DoesMatchGlob(string text, string filter, TextFilterMatchOptions textFilterMatchOptions, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
	{
		Ensure.NotNull(text);
		Ensure.NotNull(filter);

		Dictionary<TextFilterTokenType, HashSet<string>> filterTokens = ExtractGlobFilterTokens(filter);
		HashSet<string> textTokens = ExtractTextTokens(text, textFilterMatchOptions);

		if (filterTokens.Count == 0)
		{
			return true; // empty filter matches all text
		}

		if (!filterTokens.TryGetValue(TextFilterTokenType.Excluded, out HashSet<string>? excludedTokens))
		{
			excludedTokens = [];
		}

		if (!filterTokens.TryGetValue(TextFilterTokenType.Required, out HashSet<string>? requiredTokens))
		{
			requiredTokens = [];
		}

		if (!filterTokens.TryGetValue(TextFilterTokenType.Optional, out HashSet<string>? optionalTokens))
		{
			optionalTokens = [];
		}

		bool anyExcludedMatches = excludedTokens.Any(filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens, caseSensitivity));

		if (anyExcludedMatches)
		{
			return false; // text contains an excluded token
		}

		Func<IEnumerable<string>, Func<string, bool>, bool> optionalMatchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAny
			? Enumerable.Any
			: Enumerable.All;

		bool anyOptionalMatches = optionalMatchFunc(optionalTokens, filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens, caseSensitivity));

		if (optionalTokens.Count != 0 && !anyOptionalMatches)
		{
			return false; // optional tokens were set but text does not contain any optional tokens
		}

		// Lambdas rather than method groups: a method group conversion will not bind the optional
		// caseSensitivity parameter, so the sensitivity has to be captured explicitly.
		Func<string, HashSet<string>, bool> requiredMatchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAny
			? (filterToken, tokens) => AnyTokenMatchesGlobFilter(filterToken, tokens, caseSensitivity)
			: (filterToken, tokens) => AllTokensMatchGlobFilter(filterToken, tokens, caseSensitivity);

		bool allRequiredMatches = requiredTokens.All(filterToken => requiredMatchFunc(filterToken, textTokens));

		if (!allRequiredMatches)
		{
			return false; // text does not contain all required tokens
		}

		return true;
	}

	/// <summary>
	/// Determines whether any token in the text matches the specified glob filter token.
	/// </summary>
	/// <param name="filterToken">The glob filter token.</param>
	/// <param name="textTokens">The set of text tokens to match against.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase.</param>
	/// <returns><c>true</c> if any token matches the glob filter token; otherwise, <c>false</c>.</returns>
	public static bool AnyTokenMatchesGlobFilter(string filterToken, HashSet<string> textTokens, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
	{
		Ensure.NotNull(filterToken);
		Ensure.NotNull(textTokens);

		Glob glob = ResolveGlob(filterToken, caseSensitivity);

		return textTokens.Any(glob.IsMatch);
	}

	/// <summary>
	/// Determines whether all tokens in the text match the specified glob filter token.
	/// </summary>
	/// <param name="filterToken">The glob filter token.</param>
	/// <param name="textTokens">The set of text tokens to match against.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase.</param>
	/// <returns><c>true</c> if all tokens match the glob filter token; otherwise, <c>false</c>.</returns>
	public static bool AllTokensMatchGlobFilter(string filterToken, HashSet<string> textTokens, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
	{
		Ensure.NotNull(filterToken);
		Ensure.NotNull(textTokens);

		Glob glob = ResolveGlob(filterToken, caseSensitivity);

		return textTokens.All(glob.IsMatch);
	}

	private static Glob ResolveGlob(string filterToken, TextFilterCaseSensitivity caseSensitivity)
	{
		string cacheKey = CacheKey(filterToken, caseSensitivity);

		if (!GlobCache.TryGetValue(cacheKey, out Glob? glob))
		{
			glob = caseSensitivity is TextFilterCaseSensitivity.CaseInsensitive
				? Glob.Parse(filterToken, CaseInsensitiveGlobOptions)
				: Glob.Parse(filterToken);

			GlobCache.TryAdd(cacheKey, glob);
		}

		return glob;
	}

	/// <summary>
	/// Determines whether the specified text matches the regex filter pattern.
	/// </summary>
	/// <param name="text">The text to match.</param>
	/// <param name="filter">The regex filter pattern.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <param name="caseSensitivity">Whether the match distinguishes uppercase from lowercase.</param>
	/// <returns><c>true</c> if the text matches the regex filter pattern; otherwise, <c>false</c>.</returns>
	/// <remarks>
	/// An invalid pattern matches everything. A pattern that cannot be evaluated within one second —
	/// catastrophic backtracking, for instance — reports no match for the token that timed out
	/// rather than throwing, so a caller-supplied pattern cannot hang the calling thread.
	/// </remarks>
	public static bool DoesMatchRegex(string text, string filter, TextFilterMatchOptions textFilterMatchOptions, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)
	{
		Ensure.NotNull(text);
		Ensure.NotNull(filter);

		// check if regex is valid
		HashSet<string> textTokens = ExtractTextTokens(text, textFilterMatchOptions);
		string cacheKey = CacheKey(filter, caseSensitivity);
		if (!RegexCache.TryGetValue(cacheKey, out Regex? regex))
		{
			RegexOptions regexOptions = caseSensitivity is TextFilterCaseSensitivity.CaseInsensitive
				? RegexOptions.Compiled | RegexOptions.IgnoreCase
				: RegexOptions.Compiled;

			try
			{
				regex = new Regex(filter, regexOptions, RegexMatchTimeout);
			}
			catch (ArgumentException)
			{
				// invalid regex pattern
				// match anything if the pattern is invalid and cache it so that we don't trigger the exception again
				regex = RegexMatchAnything();
			}

			RegexCache.TryAdd(cacheKey, regex);
		}

		Func<IEnumerable<string>, Func<string, bool>, bool> matchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAny
			? Enumerable.Any
			: Enumerable.All;

		return matchFunc(textTokens, textToken =>
		{
			try
			{
				return regex.IsMatch(textToken);
			}
			catch (RegexMatchTimeoutException)
			{
				// A pattern that cannot be evaluated within the timeout is treated as not matching
				// this token rather than thrown at the caller. Filtering is a predicate, and a list
				// that throws mid-keystroke on a pathological pattern is a worse contract than one
				// that returns nothing for it. This mirrors how an invalid pattern degrades above.
				return false;
			}
		});
	}
}
