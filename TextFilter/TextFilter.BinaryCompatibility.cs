// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.TextFilter;

using System.ComponentModel;

// 1.7 added a trailing optional caseSensitivity parameter to these methods. Optional parameters are
// filled in at the call site, so that changed their binary signatures, and an assembly compiled
// against 1.6 or earlier failed with MissingMethodException the first time it filtered anything.
// These overloads restore the old signatures and forward with the old case-sensitive behaviour.
// Their parameters are not optional, so new code still binds to the case-sensitivity overloads.
public static partial class TextFilter
{
	/// <inheritdoc cref="Filter(IEnumerable{string}, string, TextFilterType, TextFilterMatchOptions, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static IEnumerable<string> Filter(IEnumerable<string> strings, string filter, TextFilterType filterType, TextFilterMatchOptions textFilterMatchOptions) =>
		Filter(strings, filter, filterType, textFilterMatchOptions, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="Filter{TItem}(IEnumerable{TItem}, Func{TItem, string}, string, TextFilterType, TextFilterMatchOptions, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static IEnumerable<TItem> Filter<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector, string filter, TextFilterType filterType, TextFilterMatchOptions textFilterMatchOptions) =>
		Filter(items, keySelector, filter, filterType, textFilterMatchOptions, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="IsMatch(string, string, out int, TextFilterType, TextFilterMatchOptions, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static bool IsMatch(string text, string filter, out int score, TextFilterType filterType, TextFilterMatchOptions textFilterMatchOptions) =>
		IsMatch(text, filter, out score, filterType, textFilterMatchOptions, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="IsMatch(string, string, TextFilterType, TextFilterMatchOptions, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static bool IsMatch(string text, string filter, TextFilterType filterType, TextFilterMatchOptions textFilterMatchOptions) =>
		IsMatch(text, filter, filterType, textFilterMatchOptions, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="DoesMatchGlob(string, string, TextFilterMatchOptions, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static bool DoesMatchGlob(string text, string filter, TextFilterMatchOptions textFilterMatchOptions) =>
		DoesMatchGlob(text, filter, textFilterMatchOptions, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="DoesMatchRegex(string, string, TextFilterMatchOptions, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static bool DoesMatchRegex(string text, string filter, TextFilterMatchOptions textFilterMatchOptions) =>
		DoesMatchRegex(text, filter, textFilterMatchOptions, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="AnyTokenMatchesGlobFilter(string, HashSet{string}, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static bool AnyTokenMatchesGlobFilter(string filterToken, HashSet<string> textTokens) =>
		AnyTokenMatchesGlobFilter(filterToken, textTokens, TextFilterCaseSensitivity.CaseSensitive);

	/// <inheritdoc cref="AllTokensMatchGlobFilter(string, HashSet{string}, TextFilterCaseSensitivity)"/>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static bool AllTokensMatchGlobFilter(string filterToken, HashSet<string> textTokens) =>
		AllTokensMatchGlobFilter(filterToken, textTokens, TextFilterCaseSensitivity.CaseSensitive);
}
