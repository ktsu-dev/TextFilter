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
	/// <returns>A collection of strings that match the filter.</returns>
	/// <remarks>When using fuzzy matching, the strings are sorted by their match score.</remarks>
	public static IEnumerable<string> Filter(IEnumerable<string> strings, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny) =>
		Filter(strings, s => s, filter, filterType, textFilterMatchOptions);

	/// <summary>
	/// Filters the specified collection of items based on the provided filter and filter type.
	/// </summary>
	/// <typeparam name="TItem">The type of the items in the collection.</typeparam>
	/// <param name="items">The collection of items to filter.</param>
	/// <param name="keySelector">A function to extract the string key from an item.</param>
	/// <param name="filter">The filter pattern.</param>
	/// <param name="filterType">The type of the filter.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <returns>A collection of items that match the filter.</returns>
	/// <remarks>When using fuzzy matching, the items are sorted by their match score.</remarks>
	public static IEnumerable<TItem> Filter<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny)
	{
		Ensure.NotNull(items);
		Ensure.NotNull(keySelector);
		Ensure.NotNull(filter);

		return items.Select(item =>
		{
			bool isMatch = IsMatch(keySelector(item), filter, out int score, filterType, textFilterMatchOptions);
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
	/// <returns><c>true</c> if the text matches the filter pattern; otherwise, <c>false</c>.</returns>
	public static bool IsMatch(string text, string filter, out int score, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny)
	{
		Ensure.NotNull(text);
		Ensure.NotNull(filter);

		score = int.MinValue;

		return string.IsNullOrWhiteSpace(filter)
			|| filterType switch
			{
				TextFilterType.Glob => DoesMatchGlob(text, filter, textFilterMatchOptions),
				TextFilterType.Regex => DoesMatchRegex(text, filter, textFilterMatchOptions),
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
	/// <returns><c>true</c> if the text matches the filter pattern; otherwise, <c>false</c>.</returns>
	public static bool IsMatch(string text, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny)
		=> IsMatch(text, filter, out _, filterType, textFilterMatchOptions);

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
	/// <returns><c>true</c> if the text matches the glob filter pattern; otherwise, <c>false</c>.</returns>
	public static bool DoesMatchGlob(string text, string filter, TextFilterMatchOptions textFilterMatchOptions)
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

		bool anyExcludedMatches = excludedTokens.Any(filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens));

		if (anyExcludedMatches)
		{
			return false; // text contains an excluded token
		}

		Func<IEnumerable<string>, Func<string, bool>, bool> optionalMatchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAny
			? Enumerable.Any
			: Enumerable.All;

		bool anyOptionalMatches = optionalMatchFunc(optionalTokens, filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens));

		if (optionalTokens.Count != 0 && !anyOptionalMatches)
		{
			return false; // optional tokens were set but text does not contain any optional tokens
		}

		// A required token asks whether it appears among the text's words, which is the same
		// question the excluded tokens above ask, so it uses the same function under every match
		// option. Matching it with AllTokensMatchGlobFilter under ByWordAll would instead demand
		// that every word in the text match the one required token, which no multi-word text can do.
		bool allRequiredMatches = requiredTokens.All(filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens));

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
	/// <returns><c>true</c> if any token matches the glob filter token; otherwise, <c>false</c>.</returns>
	public static bool AnyTokenMatchesGlobFilter(string filterToken, HashSet<string> textTokens)
	{
		Ensure.NotNull(filterToken);
		Ensure.NotNull(textTokens);

		if (!GlobCache.TryGetValue(filterToken, out Glob? glob))
		{
			glob = Glob.Parse(filterToken);
			GlobCache.TryAdd(filterToken, glob);
		}

		return textTokens.Any(glob.IsMatch);
	}

	/// <summary>
	/// Determines whether all tokens in the text match the specified glob filter token.
	/// </summary>
	/// <param name="filterToken">The glob filter token.</param>
	/// <param name="textTokens">The set of text tokens to match against.</param>
	/// <returns><c>true</c> if all tokens match the glob filter token; otherwise, <c>false</c>.</returns>
	public static bool AllTokensMatchGlobFilter(string filterToken, HashSet<string> textTokens)
	{
		Ensure.NotNull(filterToken);
		Ensure.NotNull(textTokens);

		if (!GlobCache.TryGetValue(filterToken, out Glob? glob))
		{
			glob = Glob.Parse(filterToken);
			GlobCache.TryAdd(filterToken, glob);
		}

		return textTokens.All(glob.IsMatch);
	}

	/// <summary>
	/// Determines whether the specified text matches the regex filter pattern.
	/// </summary>
	/// <param name="text">The text to match.</param>
	/// <param name="filter">The regex filter pattern.</param>
	/// <param name="textFilterMatchOptions">The options for matching text filters.</param>
	/// <returns><c>true</c> if the text matches the regex filter pattern; otherwise, <c>false</c>.</returns>
	public static bool DoesMatchRegex(string text, string filter, TextFilterMatchOptions textFilterMatchOptions)
	{
		Ensure.NotNull(text);
		Ensure.NotNull(filter);

		// check if regex is valid
		HashSet<string> textTokens = ExtractTextTokens(text, textFilterMatchOptions);
		if (!RegexCache.TryGetValue(filter, out Regex? regex))
		{
			try
			{
				regex = new Regex(filter, RegexOptions.Compiled);
			}
			catch (ArgumentException)
			{
				// invalid regex pattern
				// match anything if the pattern is invalid and cache it so that we don't trigger the exception again
				regex = RegexMatchAnything();
			}

			RegexCache.TryAdd(filter, regex);
		}

		Func<IEnumerable<string>, Func<string, bool>, bool> matchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAny
			? Enumerable.Any
			: Enumerable.All;

		return matchFunc(textTokens, textToken => regex.IsMatch(textToken));
	}
}
