// Copyright (c) 2023-2026 ktsu-dev contributors

namespace TextFilter.Test;

using DotNet.Globbing;
using ktsu.TextFilter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// TextFilter's case-sensitive globs stay case-sensitive when the host flips DotNet.Glob's
/// process-wide <see cref="GlobOptions.Default"/> (ktsu-dev/TextFilter#188).
/// </summary>
[TestClass]
public class GlobDefaultOptionsTests
{
	// Patterns no other test uses, so none of them is already in TextFilter's glob cache when the
	// default is flipped and each is parsed for the first time under it.
	[TestMethod]
	[DoNotParallelize]
	public void CaseSensitiveGlobIgnoresCaseInsensitiveGlobalDefault()
	{
		bool original = GlobOptions.Default.Evaluation.CaseInsensitive;
		GlobOptions.Default.Evaluation.CaseInsensitive = true;
		try
		{
			Assert.IsFalse(TextFilter.IsMatch("IMG.JPG188", "*.jpg188"),
				"the default sensitivity should still be case-sensitive");
			Assert.IsFalse(TextFilter.IsMatch("IMG.PNG188", "*.png188", TextFilterType.Glob,
				TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity.CaseSensitive),
				"an explicit CaseSensitive should still be case-sensitive");
			Assert.IsTrue(TextFilter.IsMatch("IMG.GIF188", "-*.gif188"),
				"an exclusion should not hide an item that differs only in case");
			Assert.IsTrue(TextFilter.IsMatch("IMG.BMP188", "*.bmp188", TextFilterType.Glob,
				TextFilterMatchOptions.ByWordAny, TextFilterCaseSensitivity.CaseInsensitive),
				"CaseInsensitive should still ignore case");
		}
		finally
		{
			GlobOptions.Default.Evaluation.CaseInsensitive = original;
		}
	}
}
