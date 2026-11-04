# ktsu.TextFilter

> A .NET library for filtering text using glob patterns, regular expressions, and fuzzy matching

[![License](https://img.shields.io/github/license/ktsu-dev/TextFilter.svg?label=License&logo=nuget)](LICENSE.md)
[![NuGet Version](https://img.shields.io/nuget/v/ktsu.TextFilter?label=Stable&logo=nuget)](https://nuget.org/packages/ktsu.TextFilter)
[![NuGet Version](https://img.shields.io/nuget/vpre/ktsu.TextFilter?label=Latest&logo=nuget)](https://nuget.org/packages/ktsu.TextFilter)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ktsu.TextFilter?label=Downloads&logo=nuget)](https://nuget.org/packages/ktsu.TextFilter)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/TextFilter?label=Commits&logo=github)](https://github.com/ktsu-dev/TextFilter/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/TextFilter?label=Contributors&logo=github)](https://github.com/ktsu-dev/TextFilter/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/TextFilter/dotnet.yml?branch=main&label=Build&logo=github)](https://github.com/ktsu-dev/TextFilter/actions)

## Introduction

ktsu.TextFilter is a .NET library that provides methods for filtering text based on different filter types and match options. It supports glob patterns, regular expressions, and fuzzy matching to help you efficiently filter and search through collections of strings.

## Features

- **Glob Pattern Matching**: Filter text using glob patterns with optional, required, and excluded tokens.
- **Regular Expression Matching**: Filter text using regular expressions.
- **Fuzzy Matching**: Rank text based on how well it matches a fuzzy pattern.
- **Customizable Match Options**: Match by whole string, all words, or any word.
- **Case Sensitivity**: Opt into case-insensitive glob and regex matching; case sensitive by default.

## Installation

### Package Manager Console

```powershell
Install-Package ktsu.TextFilter
```

### .NET CLI

```bash
dotnet add package ktsu.TextFilter
```

### Package Reference

```xml
<PackageReference Include="ktsu.TextFilter" Version="x.y.z" />
```

## Usage Examples

### Basic Example

```csharp
using ktsu.TextFilter;

string text = "Hello, World!";
string pattern = "Hello*";

bool isMatch = TextFilter.IsMatch(text, pattern);
```

### Filtering Collections

```csharp
using ktsu.TextFilter;

string[] texts = new string[] { "Hello, World!", "Goodbye, World!" };
string pattern = "Hello*";

IEnumerable<string> matches = TextFilter.Filter(texts, pattern);
```

### Ranking Results

```csharp
using ktsu.TextFilter;

string[] texts = new string[] { "Hello, World!", "Goodbye, World!" };
string pattern = "Hello";

IEnumerable<string> ranked = TextFilter.Rank(texts, pattern);
```

## Advanced Usage

### Match Options

You can customize how matching is performed using the `TextFilterMatchOptions` enum. The default is
`ByWordAny`:

```csharp
using ktsu.TextFilter;

string text = "Hello beautiful world";
string pattern = "hello world";

// Match by any word in the pattern (the default)
bool anyWordMatch = TextFilter.IsMatch(text, pattern,
    TextFilterType.Glob, TextFilterMatchOptions.ByWordAny);                               // true

// Match by all words in the pattern
bool allWordsMatch = TextFilter.IsMatch(text, pattern,
    TextFilterType.Glob, TextFilterMatchOptions.ByWordAll);                               // false

// Match the entire string
bool wholeStringMatch = TextFilter.IsMatch(text, pattern,
    TextFilterType.Glob, TextFilterMatchOptions.ByWholeString);                           // false
```

### Case Sensitivity

Glob and regular expression matching are **case sensitive by default**. Pass
`TextFilterCaseSensitivity.CaseInsensitive` to fold case on both the text and the pattern:

```csharp
using ktsu.TextFilter;

// Case sensitive (the default) - a camera writes IMG_1234.JPG, so this does not match
bool sensitive = TextFilter.IsMatch("IMG_1234.JPG", "*.jpg",
    TextFilterType.Glob, TextFilterMatchOptions.ByWholeString);                           // false

// Case insensitive
bool insensitive = TextFilter.IsMatch("IMG_1234.JPG", "*.jpg",
    TextFilterType.Glob, TextFilterMatchOptions.ByWholeString,
    TextFilterCaseSensitivity.CaseInsensitive);                                           // true
```

The setting is available on `IsMatch`, `Filter`, `DoesMatchGlob`, `DoesMatchRegex`,
`AnyTokenMatchesGlobFilter` and `AllTokensMatchGlobFilter`, and applies to required and excluded
tokens as well as optional ones.

`TextFilterType.Fuzzy` does not take the setting: fuzzy matching is always case insensitive.

### Glob Character Classes

A glob token can contain a bracket class that matches one character: `[abc]`, a range such as
`[0-9]`, or a mix such as `[0-9a-f]`. A class that opens with `!` or `^` is negated, so `x[!a]` and
`x[^a]` both match `xb` but not `xa`. A `^` anywhere else in a class is an ordinary member: `x[a^]`
matches `x^`.

Case is folded invariantly, so the answer does not depend on the calling thread's `CurrentCulture`.
That matters for the dotted and dotless I: under a Turkish culture, `i` and `I` are not each other's
case pair, and a culture-sensitive fold would stop a filter of `img` matching `IMG_1234.JPG` on a
machine whose locale happens to be `tr-TR`. A filter pattern is machine text rather than prose, so
it folds the same way everywhere.

### Filter Types

TextFilter supports different filter types:

```csharp
using ktsu.TextFilter;

string text = "Hello, World!";
string globPattern = "Hello*";
string regexPattern = "^Hello";

// Glob pattern matching
bool globMatch = TextFilter.IsMatch(text, globPattern, filterType: TextFilterType.Glob);

// Regular expression matching
bool regexMatch = TextFilter.IsMatch(text, regexPattern, filterType: TextFilterType.Regex);

// Fuzzy matching
bool fuzzyMatch = TextFilter.IsMatch(text, "Helo", filterType: TextFilterType.Fuzzy);
```

## API Reference

### `TextFilter` Class

The primary class for text filtering operations.

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `IsMatch(string text, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)` | `bool` | Tests if the input text matches the specified filter |
| `IsMatch(string text, string filter, out int score, ...)` | `bool` | As above, and also returns the fuzzy score (`int.MinValue` for glob and regex) |
| `Filter(IEnumerable<string> strings, string filter, TextFilterType filterType = TextFilterType.Glob, TextFilterMatchOptions textFilterMatchOptions = TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity caseSensitivity = TextFilterCaseSensitivity.CaseSensitive)` | `IEnumerable<string>` | Returns all strings that match the filter |
| `Filter<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector, string filter, ...)` | `IEnumerable<TItem>` | Returns all items whose key matches the filter |
| `Rank(IEnumerable<string> strings, string fuzzyFilter)` | `IEnumerable<string>` | Returns the strings ranked by how well they fuzzy-match the filter |
| `Rank<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector, string fuzzyFilter)` | `IEnumerable<TItem>` | Returns the items ranked by how well their key fuzzy-matches the filter |
| `GetHint(TextFilterType filterType)` | `string` | Returns a short description of the syntax a filter type accepts |

### Enums

#### `TextFilterMatchOptions`

| Value | Description |
|-------|-------------|
| `ByWholeString` | Match the entire string against the pattern |
| `ByWordAll` | Match all words in the pattern against the string |
| `ByWordAny` | Match any word in the pattern against the string (the default) |

#### `TextFilterType`

| Value | Description |
|-------|-------------|
| `Glob` | Use glob pattern matching (the default) |
| `Regex` | Use regular expression matching |
| `Fuzzy` | Use fuzzy matching |

#### `TextFilterCaseSensitivity`

| Value | Description |
|-------|-------------|
| `CaseSensitive` | Uppercase and lowercase are distinct (the default) |
| `CaseInsensitive` | Uppercase and lowercase are equivalent, for glob and regex matching |

## Contributing

Contributions are welcome! For feature requests, bug reports, or questions, please open an issue on GitHub. If you would like to contribute code, please open a pull request with your changes.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.md) file for details.

## Acknowledgements

- [DotNet.Glob](https://github.com/dazinator/DotNet.Glob) for glob pattern matching.
- [ktsu.FuzzySearch](https://github.com/ktsu-dev/FuzzySearch) for fuzzy matching.
