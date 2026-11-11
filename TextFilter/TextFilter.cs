// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.TextFilter;

using System.Collections.Concurrent;
using System.Text;
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
	// A null entry records a token that could not be parsed, so the parse is not retried on every keystroke.
	private static ConcurrentDictionary<string, Glob[]?> GlobCache { get; } = [];

	// Filter patterns are caller-supplied, and in the keystroke-driven filter box this library exists
	// for, every prefix of what the user types becomes its own key. Unbounded, the caches therefore
	// grow with everything ever typed for the lifetime of the process, rather than with the size of
	// the data being filtered.
	internal const int MaxCacheEntries = 4096;

	internal static int RegexCacheCount => RegexCache.Count;

	internal static int GlobCacheCount => GlobCache.Count;

	// Clear-and-restart rather than LRU: ConcurrentDictionary keeps no eviction order, and tracking
	// one would put a write on every cache *hit* — the path the cache exists to keep cheap. This
	// only ever runs on a miss, where a compile is about to happen anyway, so the Count (which does
	// take the dictionary's locks) is off the hot path. At this cap a reset is rare, and costs one
	// recompile per pattern still in use.
	private static void AddBounded<TValue>(ConcurrentDictionary<string, TValue> cache, string cacheKey, TValue value)
	{
		if (cache.Count >= MaxCacheEntries)
		{
			cache.Clear();
		}

		cache.TryAdd(cacheKey, value);
	}

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

	// Stands in for a pattern that does not parse, such as "[" half-way through being typed. One
	// shared instance, like RegexMatchNothing below, so an invalid pattern costs a cache entry and
	// nothing more, rather than a fresh regex on every keystroke that produces one.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "SYSLIB1045:Convert to 'GeneratedRegexAttribute'.", Justification = "Not available in older frameworks")]
	private static readonly Regex RegexMatchAnything = new(".*", RegexOptions.None, RegexMatchTimeout);

	// Stands in for a pattern that has already run out of time once. The timeout bounds a single
	// IsMatch call, so without this the same pathological pattern would pay it again for every word
	// of every item and on every later call. "(?!)" fails at once on any input, which is the answer a
	// timeout already degrades to.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "SYSLIB1045:Convert to 'GeneratedRegexAttribute'.", Justification = "Not available in older frameworks")]
	private static readonly Regex RegexMatchNothing = new("(?!)", RegexOptions.None, RegexMatchTimeout);

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

		IEnumerable<(TItem item, bool isMatch, int score)> matches = items.Select(item =>
		{
			bool isMatch = IsMatch(keySelector(item), filter, out int score, filterType, textFilterMatchOptions, caseSensitivity);
			return (item, isMatch, score);
		})
		.Where(t => t.isMatch);

		// Only a non-blank fuzzy filter sets a score, so sorting anything else would reorder nothing
		// while still reading the whole source before yielding the first item. Skipping it lets glob
		// and regex results stream, so Take(n) or First() stops as soon as it has enough matches.
		if (filterType is TextFilterType.Fuzzy && !string.IsNullOrWhiteSpace(filter))
		{
			matches = matches.OrderByDescending(t => t.score);
		}

		return matches.Select(t => t.item);
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
	/// <remarks>Uses fuzzy matching to rank the items by their match score. Items that match the pattern always sort above items that don't, because a short near-miss can outscore a real match inside a longer string.</remarks>
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
		.OrderByDescending(t => t.isMatch)
		.ThenByDescending(t => t.score)
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
				// Whitespace would otherwise become a character the text must contain, so " hel" matched
				// nothing and "open file" missed OpenFile.cs, while glob and regex split on it instead.
				TextFilterType.Fuzzy => Fuzzy.Contains(text.AsSpan(), RemoveWhitespace(filter).AsSpan(), out score),
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

	// Filter is called once per item with the same filter, so a filter with no whitespace inside it,
	// the usual case, is returned as is rather than copied on every call.
	private static string RemoveWhitespace(string value) =>
		value.Any(char.IsWhiteSpace) ? new string([.. value.Where(c => !char.IsWhiteSpace(c))]) : value;

	internal static HashSet<string> ExtractTextTokens(string text, TextFilterMatchOptions textFilterMatchOptions)
	{
		return textFilterMatchOptions switch
		{
			TextFilterMatchOptions.ByWholeString => [text],
			TextFilterMatchOptions.ByWordAll => [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)],
			TextFilterMatchOptions.ByWordAny => [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)],
			_ => throw new NotImplementedException($"{nameof(TextFilterMatchOptions)}.{textFilterMatchOptions} has not been implemented"),
		};
	}

	internal static Dictionary<TextFilterTokenType, HashSet<string>> ExtractGlobFilterTokens(string filter)
	{
		string[] filterTokens = [.. filter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];
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
	/// <remarks>
	/// A token that cannot be parsed as a glob, such as the half-typed range <c>file[0-</c>, is ignored
	/// rather than throwing: as a plain token it is left out of the optional tokens, so it neither
	/// matches nor rejects anything; as a required token it matches everything; and as an excluded
	/// token it excludes nothing.
	/// </remarks>
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

		// An unparseable excluded token is skipped rather than treated as match-anything, which here
		// would exclude every item while the user is still typing the token.
		bool anyExcludedMatches = excludedTokens.Any(filterToken => ResolveGlob(filterToken, caseSensitivity) is Glob[] globs && textTokens.Any(token => IsGlobMatch(globs, token)));

		if (anyExcludedMatches)
		{
			return false; // text contains an excluded token
		}

		Func<IEnumerable<string>, Func<string, bool>, bool> optionalMatchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAll
			? Enumerable.All
			: Enumerable.Any;

		// An unparseable optional token is dropped before Any/All is applied. Left in, it would count
		// as a match, which under Any lets every item through while the user is still typing a second
		// token ("*.cs file[0-"). With none left, no optional constraint applies, as for one bad token.
		List<string> parseableOptionalTokens = [.. optionalTokens.Where(filterToken => ResolveGlob(filterToken, caseSensitivity) is not null)];

		bool anyOptionalMatches = optionalMatchFunc(parseableOptionalTokens, filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens, caseSensitivity));

		if (parseableOptionalTokens.Count != 0 && !anyOptionalMatches)
		{
			return false; // optional tokens were set but text does not contain any optional tokens
		}

		// A required token asks whether it appears among the text's words, which is the same
		// question the excluded tokens above ask, so it uses the same function under every match
		// option. Matching it with AllTokensMatchGlobFilter under ByWordAll would instead demand
		// that every word in the text match the one required token, which no multi-word text can do.
		bool allRequiredMatches = requiredTokens.All(filterToken => AnyTokenMatchesGlobFilter(filterToken, textTokens, caseSensitivity));

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

		Glob[]? globs = ResolveGlob(filterToken, caseSensitivity);

		return globs is null || textTokens.Any(token => IsGlobMatch(globs, token));
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

		Glob[]? globs = ResolveGlob(filterToken, caseSensitivity);

		return globs is null || textTokens.All(token => IsGlobMatch(globs, token));
	}

	// DotNet.Glob is a file-path glob, so its * and ? stop at / and \. TextFilter filters arbitrary
	// text, where a slash is an ordinary character, so both separators are swapped for a private-use
	// character in the pattern and in the text before DotNet.Glob sees either.
	private const char MaskedPathSeparator = '\uE000';

	private static string MaskPathSeparators(string value) =>
		value.Replace('/', MaskedPathSeparator).Replace('\\', MaskedPathSeparator);

	private static bool IsGlobMatch(Glob[] globs, string textToken)
	{
		string maskedText = MaskPathSeparators(textToken);
		return globs.Any(glob => glob.IsMatch(maskedText));
	}

	// Masking also hides the separators DotNet.Glob needs to recognise "**/" as "zero or more path
	// segments", leaving "**" followed by a literal character. Since * already crosses the masked
	// separator, each "**/" is expanded here instead: dropped, for the zero-segment case, or kept as
	// "*/", for "anything ending at a separator". A token matches if any expansion does. Only a "**/"
	// that starts the token or follows a separator is dropped, since one joined to a literal is a "*".
	private const string MaskedGlobStarSegment = "**\uE000";

	// Each "**/" doubles the expansions, so only this many are expanded both ways; any beyond it keep
	// only the "*/" form and so need at least one segment.
	private const int MaxExpandedGlobStarSegments = 4;

	internal static IReadOnlyList<string> ExpandGlobStarSegments(string maskedToken)
	{
		List<string> expansions = [string.Empty];
		List<(int Open, int Close)> classSpans = FindGlobClassSpans(maskedToken);
		int start = 0;
		int searchFrom = 0;
		int expanded = 0;
		int index;

		while ((index = maskedToken.IndexOf(MaskedGlobStarSegment, searchFrom, StringComparison.Ordinal)) >= 0)
		{
			// Inside a bracket class "**/" is not a globstar but the members "*" and "/". Dropping them
			// would shrink the class, which for a negated class lets through characters it excludes, so
			// the search resumes after the class and its contents stay literal.
			int classClose = ClassCloseContaining(classSpans, index);
			if (classClose >= 0)
			{
				searchFrom = classClose + 1;
				continue;
			}

			string literal = maskedToken[start..index];
			bool expandBothWays = expanded < MaxExpandedGlobStarSegments;
			List<string> next = new(expansions.Count * 2);

			foreach (string prefix in expansions)
			{
				string before = prefix + literal;
				next.Add(before + "*" + MaskedPathSeparator);

				// Only a "**" that is a whole segment can match zero segments; joined to a literal, as
				// in "foo**/bar", it is just "*", and dropping it would splice "foo" onto "bar".
				if (expandBothWays && (before.Length == 0 || before[^1] == MaskedPathSeparator))
				{
					next.Add(before);
				}
			}

			expansions = next;
			expanded++;
			start = index + MaskedGlobStarSegment.Length;
			searchFrom = start;
		}

		string tail = maskedToken[start..];
		return [.. expansions.Select(prefix => prefix + tail).Distinct(StringComparer.Ordinal)];
	}

	// The spans of the closed bracket classes in a token, read with the same boundary rules as
	// ExpandGlobClasses: a leading "]" is a member, and "!" or "^" negate. Scanning stops at a class
	// with no closing "]", as ExpandGlobClasses does.
	private static List<(int Open, int Close)> FindGlobClassSpans(string token)
	{
		List<(int Open, int Close)> spans = [];
		int index = 0;
		int open;
		while ((open = token.IndexOf('[', index)) >= 0)
		{
			int close = FindGlobClassEnd(token, open, out _, out _);
			if (close < 0)
			{
				break;
			}

			spans.Add((open, close));
			index = close + 1;
		}

		return spans;
	}

	private static int ClassCloseContaining(List<(int Open, int Close)> spans, int position)
	{
		foreach ((int open, int close) in spans)
		{
			if (open < position && position < close)
			{
				return close;
			}
		}

		return -1;
	}

	// DotNet.Glob reads a bracket class as exactly one token: a single range when it starts "x-y" with
	// two letters or two digits, otherwise a plain character list in which "-" is a member and no ranges
	// are read. It never combines the two, so "[0-9a-f]" stops at the 0-9 range and misses "c", while
	// "[-0-9]" and "[ab-d]" become lists that miss "5" and "c". A trailing dash ("[a-]") makes its
	// tokeniser throw outright. So every closed class it would misread is rewritten here into the
	// explicit character list it means, which it does read correctly, negation and case folding
	// included: "[0-9a-f]" becomes "[0123456789abcdef]" and "[a-z-]" becomes "[-abc...z]". A lone
	// letter or digit range is left for DotNet.Glob to match natively, since it reads that form
	// correctly and it may span far more characters than are worth listing. A class with no closing
	// "]" is left alone and stays unparseable.
	// A class that would list more than MaxGlobClassMembers characters ("[가-힣a-z]") is costly to match
	// as a list, so a class that is not negated is split instead: each letter or digit range in it is
	// matched natively, the rest is listed, and the token becomes one alternative per piece, matching if
	// any of them does. A negated class cannot be split that way, since it must miss every piece at once,
	// so it is listed in full however long it is, as is a class whose split would give the token more than
	// MaxGlobClassAlternatives alternatives.
	// Returns the token's alternatives, or null for a class that has no members at all because every
	// range is reversed.
	private const int MaxGlobClassMembers = 4096;

	private const int MaxGlobClassAlternatives = 16;

	internal static IReadOnlyList<string>? ExpandGlobClasses(string token)
	{
		if (!token.Contains(']'))
		{
			return [token];
		}

		List<StringBuilder> results = [new(token.Length)];
		int index = 0;
		while (index < token.Length)
		{
			int open = token.IndexOf('[', index);
			if (open < 0)
			{
				break;
			}

			int close = FindGlobClassEnd(token, open, out bool negated, out int contentStart);
			if (close < 0)
			{
				break;
			}

			string content = token[contentStart..close];
			List<string>? rewritten = RewriteGlobClass(content, negated, MaxGlobClassAlternatives / results.Count);
			if (rewritten is null)
			{
				return null;
			}

			string literal = token[index..open];
			results = [.. results.SelectMany(result => rewritten.Select(piece => new StringBuilder(result.ToString()).Append(literal).Append(piece)))];
			index = close + 1;

			// DotNet.Glob reads a "]" straight after a class as one more member of it, so "[a]]" would
			// match "a" and not "a]". Each such "]" is written as the one-member class "[]]", which it
			// reads as the literal the pattern means.
			while (index < token.Length && token[index] == ']')
			{
				results.ForEach(result => result.Append("[]]"));
				index++;
			}
		}

		string tail = token[index..];
		return [.. results.Select(result => result.Append(tail).ToString())];
	}

	// Returns the pieces a class is matched as, any of which may match, at most maxPieces of them; or
	// null if the class has no members.
	private static List<string>? RewriteGlobClass(string content, bool negated, int maxPieces)
	{
		if (!content.Contains('-') || IsNativeGlobRange(content))
		{
			return [(negated ? "[!" : "[") + content + "]"];
		}

		if (IsReversedLetterRange(content))
		{
			return [negated ? "[!9-0]" : "[9-0]"];
		}

		List<char> singles = [];
		List<(char First, char Last)> ranges = [];
		ReadGlobClassMembers(content, singles, ranges);

		long memberCount = singles.Count + ranges.Sum(range => Math.Max(0L, range.Last - range.First + 1L));
		if (!negated && memberCount > MaxGlobClassMembers && SplitGlobClass(singles, ranges, maxPieces) is List<string> pieces)
		{
			return pieces;
		}

		return ToGlobCharacterList(CollectGlobClassMembers(singles, ranges), negated) is string list ? [list] : null;
	}

	// Splits a large non-negated class into a piece per native range plus one listed piece for the rest,
	// or returns null if that gives more than maxPieces pieces.
	private static List<string>? SplitGlobClass(List<char> singles, List<(char First, char Last)> ranges, int maxPieces)
	{
		// A reversed range has no members, so it adds no piece.
		List<(char First, char Last)> listedRanges = [.. ranges.Where(range => range.First <= range.Last && !IsNativeGlobRange(range.First, range.Last))];
		List<string> pieces = [.. ranges
			.Where(range => range.First <= range.Last && IsNativeGlobRange(range.First, range.Last))
			.Select(range => $"[{range.First}-{range.Last}]")
			.Distinct(StringComparer.Ordinal)];

		string? rest = ToGlobCharacterList(CollectGlobClassMembers(singles, listedRanges), negated: false);
		if (rest is not null)
		{
			pieces.Add(rest);
		}

		return pieces.Count <= maxPieces ? pieces : null;
	}

	// Returns the index of the "]" closing the class opened at "open", or -1 if it is never closed.
	// A class opening with "^" is negated, as in bash, gitignore and regex, the same as one opening with
	// "!". DotNet.Glob only knows "!", so the caller writes the negation back out as "!".
	private static int FindGlobClassEnd(string token, int open, out bool negated, out int contentStart)
	{
		contentStart = open + 1;
		negated = contentStart < token.Length && token[contentStart] is '!' or '^';
		if (negated)
		{
			contentStart++;
		}

		// A "]" straight after the opening bracket is a member of the class rather than its end.
		return contentStart < token.Length ? token.IndexOf(']', contentStart + 1) : -1;
	}

	// A lone range is left for DotNet.Glob only when its endpoints are letters sharing a case (both upper,
	// both lower, or both uncased) or are both digits. Under
	// CaseInsensitive, DotNet.Glob lowercases the endpoints, which would turn the mixed-case "A-z" into
	// "a-z" (losing "_" and "["), so a mixed-case range is listed by ToGlobCharacterList instead.
	// A range that spans "/" or "\" is listed too, so the separator in it is masked like any other
	// literal one; left native, it would never contain the masked separator in the text.
	private static bool IsNativeGlobRange(string content) =>
		content.Length == 3 && content[1] == '-' && content[0] != ']' && IsNativeGlobRange(content[0], content[2]);

	private static bool IsNativeGlobRange(char first, char last) =>
		((char.IsLetter(first) && char.IsLetter(last) && char.IsUpper(first) == char.IsUpper(last) && char.IsLower(first) == char.IsLower(last))
			|| (char.IsAsciiDigit(first) && char.IsAsciiDigit(last)))
		&& !SpansPathSeparator(first, last);

	private static bool SpansPathSeparator(char first, char last) =>
		(first <= '/' && '/' <= last) || (first <= '\\' && '\\' <= last);

	// A lone reversed letter range, such as "a-Z", matches nothing, but DotNet.Glob would fold it to "a-z"
	// under CaseInsensitive. It is written as a reversed digit range, which case folding leaves alone.
	private static bool IsReversedLetterRange(string content) =>
		content.Length == 3 && content[1] == '-' && char.IsLetter(content[0]) && char.IsLetter(content[2]) && content[0] > content[2];

	// Reads the class members with the usual glob rules: a leading "]" and a leading or trailing "-" are
	// literal, and "x-y" is an inclusive range.
	private static void ReadGlobClassMembers(string content, List<char> singles, List<(char First, char Last)> ranges)
	{
		int i = 0;
		while (i < content.Length)
		{
			// A leading "]" can start a range too: "[]-a]" is "]".."a", as in POSIX.
			bool isRange = i + 2 < content.Length && content[i + 1] == '-';
			if (!isRange)
			{
				singles.Add(content[i]);
				i++;
				continue;
			}

			ranges.Add((content[i], content[i + 2]));
			i += 3;
		}
	}

	private static SortedSet<char> CollectGlobClassMembers(List<char> singles, List<(char First, char Last)> ranges)
	{
		SortedSet<char> members = [.. singles];
		foreach ((char first, char last) in ranges)
		{
			for (int member = first; member <= last; member++)
			{
				members.Add((char)member);
			}
		}

		return members;
	}

	// Writes the members in an order DotNet.Glob reads as a plain list: "]" first, so it does not close
	// the class, then "-", so no member pair looks like a range, and "!" never first, so it is not read
	// as negation. Returns null if there are no members.
	private static string? ToGlobCharacterList(SortedSet<char> members, bool negated)
	{
		if (members.Count == 0)
		{
			return null;
		}

		StringBuilder list = new(members.Count + 3);
		list.Append('[');
		if (negated)
		{
			list.Append('!');
		}

		if (members.Remove(']'))
		{
			list.Append(']');
		}

		if (members.Remove('-'))
		{
			list.Append('-');
		}

		bool hasBang = members.Remove('!');
		foreach (char member in members)
		{
			list.Append(member);
		}

		if (hasBang)
		{
			list.Append('!');
		}

		return list.Append(']').ToString();
	}

	// Returns null for a token that cannot be parsed, so each caller can decide what ignoring it means.
	private static Glob[]? ResolveGlob(string filterToken, TextFilterCaseSensitivity caseSensitivity)
	{
		string cacheKey = CacheKey(filterToken, caseSensitivity);

		if (!GlobCache.TryGetValue(cacheKey, out Glob[]? globs))
		{
			try
			{
				IReadOnlyList<string>? expandedTokens = ExpandGlobClasses(filterToken);
				// A token made only of "**/" segments expands to the empty string as well as to "*/".
				// Glob.Parse throws on an empty pattern, which would mark the whole token unparseable and
				// discard the valid expansions with it. An empty pattern could only match empty text, which
				// never reaches the matcher, so drop it; a token with nothing left is still unparseable.
				Glob[]? parsed = expandedTokens is null ? null : [.. expandedTokens
					.SelectMany(expandedToken => ExpandGlobStarSegments(MaskPathSeparators(expandedToken)))
					.Distinct(StringComparer.Ordinal)
					.Where(pattern => pattern.Length != 0)
					.Select(pattern => caseSensitivity is TextFilterCaseSensitivity.CaseInsensitive
						? Glob.Parse(pattern, CaseInsensitiveGlobOptions)
						: Glob.Parse(pattern))];
				globs = parsed is { Length: > 0 } ? parsed : null;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				// DotNet.Glob's tokeniser throws IndexOutOfRangeException on a range left open after the
				// dash ("file[0-"), which is ordinary intermediate input while someone types a range into
				// a filter box. Cache it as unparseable so the exception is not raised again on every
				// keystroke. Caught broadly so the next tokeniser bug is contained too.
				globs = null;
			}

			AddBounded(GlobCache, cacheKey, globs);
		}

		return globs;
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
	/// catastrophic backtracking, for instance — reports no match rather than throwing, so a
	/// caller-supplied pattern cannot hang the calling thread. Once a pattern has timed out it is
	/// remembered as matching nothing, for the remaining words and for later calls at the same case
	/// sensitivity, so a filter over many items pays the timeout once rather than once per word.
	/// <para>
	/// Under <see cref="TextFilterMatchOptions.ByWordAny"/> the pattern is tried against the whole text
	/// before each word, so a pattern that spans whitespace, such as <c>New York</c> or <c>\d+\s+items</c>,
	/// still matches. Under <see cref="TextFilterMatchOptions.ByWordAll"/> every word must match on its
	/// own, so a pattern that spans whitespace never matches there; use
	/// <see cref="TextFilterMatchOptions.ByWholeString"/> for those.
	/// </para>
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
			regex = CreateFilterRegex(filter, caseSensitivity);
			AddBounded(RegexCache, cacheKey, regex);
		}

		// Swapped for RegexMatchNothing if the pattern times out partway through
		Regex activeRegex = regex;

		// Text with no words -- "" or "   " split by word -- has nothing for the pattern to match.
		// Without this, Enumerable.All over the empty set is vacuously true, so under ByWordAll blank
		// text matched every pattern while the glob path reported no match for the same input.
		if (textTokens.Count == 0)
		{
			return false;
		}

		// No single word contains whitespace, so a pattern like "hello world" or "o\sw" can only ever
		// match the whole text. Under ByWordAny, try that first; the per-word pass below still runs,
		// so a word-anchored pattern like "^world" keeps matching "hello world".
		if (textFilterMatchOptions is TextFilterMatchOptions.ByWordAny && IsMatchWithinTimeout(ref activeRegex, cacheKey, text))
		{
			return true;
		}

		Func<IEnumerable<string>, Func<string, bool>, bool> matchFunc = textFilterMatchOptions is TextFilterMatchOptions.ByWordAny
			? Enumerable.Any
			: Enumerable.All;

		return matchFunc(textTokens, textToken => IsMatchWithinTimeout(ref activeRegex, cacheKey, textToken));
	}

	// Builds the regex DoesMatchRegex caches for a filter pattern.
	//
	// Interpreted rather than RegexOptions.Compiled. A filter box produces a new pattern on every
	// keystroke ("M", "Mo", "Mod", ...), and each one is matched against the visible items once and
	// then never again. Compiling to IL only pays off over many matches, so for these patterns it
	// made each keystroke about ten times slower and each cached entry several times larger, and on
	// .NET 7 and later the interpreter is fast for inputs as short as item names.
	internal static Regex CreateFilterRegex(string filter, TextFilterCaseSensitivity caseSensitivity)
	{
		// CultureInvariant, because IgnoreCase alone folds case using the thread's CurrentCulture.
		// Under tr-TR that stops "i" and "I" being the same letter, so a filter of "img" no longer
		// matches "IMG_1234.JPG" -- and the cache in DoesMatchRegex is keyed by pattern and sensitivity
		// only, so whichever culture happened to compile the pattern first decides the answer for every
		// later caller on any thread. A filter pattern is machine text, not prose, and the glob
		// path already folds invariantly, so the two paths now agree. It applies under CaseSensitive
		// too, because the pattern itself can ask for a fold with an inline (?i), and that fold is
		// just as culture-dependent and just as cached as the one the option asks for.
		RegexOptions regexOptions = caseSensitivity is TextFilterCaseSensitivity.CaseInsensitive
			? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
			: RegexOptions.CultureInvariant;

		try
		{
			return new Regex(filter, regexOptions, RegexMatchTimeout);
		}
		catch (ArgumentException)
		{
			// An invalid pattern matches anything, and DoesMatchRegex caches the result so the
			// exception is not triggered again.
			return RegexMatchAnything;
		}
	}

	private static bool IsMatchWithinTimeout(ref Regex regex, string cacheKey, string input)
	{
		try
		{
			return regex.IsMatch(input);
		}
		catch (RegexMatchTimeoutException)
		{
			// A pattern that cannot be evaluated within the timeout is treated as not matching
			// this input rather than thrown at the caller. Filtering is a predicate, and a list
			// that throws mid-keystroke on a pathological pattern is a worse contract than one
			// that returns nothing for it. This mirrors how an invalid pattern degrades in DoesMatchRegex.
			//
			// The pattern is then swapped for one that matches nothing, both for the rest of this
			// call and in the cache, so the timeout is paid once rather than per word and per item.
			regex = RegexMatchNothing;
			RegexCache[cacheKey] = RegexMatchNothing;
			return false;
		}
	}
}
