using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using ArgsReading;
using GlobExpressions;

namespace CleanUpDirectives
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			try
			{
				return Run(new ArgsReader(args));
			}
			catch (Exception exception) when (exception is ApplicationException || exception is ArgsReaderException)
			{
				Console.Error.WriteLine(exception.Message);
				return 2;
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine(exception.ToString());
				return 2;
			}
		}

		private static int Run(ArgsReader args)
		{
			var excludes = new List<string>();
			while (args.ReadOption("x|exclude") is string exclude)
				excludes.Add(exclude);

			var globs = args.ReadArguments();
			if (globs.Count == 0)
				throw CreateUsageException();

			args.VerifyComplete();

			var paths = GetFullPathsFromGlobs(globs)
				.Except(GetFullPathsFromGlobs(excludes))
				.ToList();
			if (paths.Count == 0)
				throw new ApplicationException("No files found.");

			var excludePaths = GetFullPathsFromGlobs(excludes)
				.Select(x => Directory.Exists(x) ? (x + Path.DirectorySeparatorChar) : x)
				.ToList();
			paths = paths.Where(path => !excludePaths.Any(x => path.StartsWith(x, StringComparison.Ordinal))).ToList();
			if (paths.Count == 0)
				throw new ApplicationException("All files excluded.");

			var invalidPath = paths.FirstOrDefault(Directory.Exists);
			if (invalidPath != null)
				throw new ApplicationException($"Directories not supported; use glob to select files, e.g. **/*.cs{Environment.NewLine}Directory matched: {invalidPath}");

			foreach (var path in paths)
				Console.WriteLine(path);

			return 0;
		}

		private static IReadOnlyList<string> GetFullPathsFromGlobs(IEnumerable<string> globs) =>
			globs
				.Select(x => (Path: x, Root: Path.GetPathRoot(x) ?? ""))
				.Select(x => (Path: x.Path.Substring(x.Root.Length), Directory: x.Root.Length != 0 ? x.Root : Environment.CurrentDirectory))
				.SelectMany(x => Glob.FilesAndDirectories(x.Directory, x.Path, GlobOptions.CaseInsensitive).Select(p => Path.Combine(x.Directory, p)))
				.Distinct()
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToList();

		private static ApplicationException CreateUsageException() =>
			new ApplicationException(string.Join(Environment.NewLine,
				"Usage: CleanUpDirectives <glob> ... [options]",
				"  <glob> : Clean up matching files, e.g. **/*.cs",
				"",
				"Options:",
				"  -x|--exclude <glob> : Exclude matching files and directories"));
	}
}
