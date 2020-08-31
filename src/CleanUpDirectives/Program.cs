using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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

			var shouldListExpressions = args.ReadFlag("list-expr");
			var shouldListSymbols = args.ReadFlag("list-symbols");
			var shouldFormat = args.ReadFlag("format");

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

			var expressions = new HashSet<string>();
			var symbols = new HashSet<string>();

			Console.WriteLine("Files:");
			foreach (var path in paths)
			{
				Console.Write(path);

				string?[] lines = Regex.Split(File.ReadAllText(path), @"(?<=\n)");
				var needsSave = false;

				for (int index = 0; index < lines.Length; index++)
				{
					var line = lines[index]!;
					var match = Regex.Match(line, @"^\s*#(if|elif)(?=\W)\s*([^\r\n/]+?)\s*$");
					if (match.Success)
					{
						var expressionCapture = match.Groups[2];
						var expression = expressionCapture.Value;
						expressions.Add(expression);

						var node = ExpressionParser.Parse(expression);
						foreach (var symbol in GetSymbols(node))
							symbols.Add(symbol);

						if (shouldFormat)
						{
							var formatted = node.ToString();
							if (expression != formatted)
							{
								lines[index] = line.Substring(0, expressionCapture.Index) + formatted +
									line.Substring(expressionCapture.Index + expressionCapture.Length);
								needsSave = true;
							}
						}
					}
				}

				if (needsSave)
				{
					var newText = new StringBuilder();
					foreach (var line in lines)
					{
						if (line != null)
							newText.Append(line);
					}
					File.WriteAllText(path, newText.ToString(), s_utf8);
					Console.WriteLine(" [edited]");
				}
				else
				{
					Console.WriteLine(" [viewed]");
				}
			}
			Console.WriteLine();

			if (shouldListExpressions)
			{
				Console.WriteLine("Expressions:");
				foreach (var expression in expressions.OrderBy(x => x, StringComparer.Ordinal))
					Console.WriteLine(expression);
				Console.WriteLine();
			}

			if (shouldListSymbols)
			{
				Console.WriteLine("Symbols:");
				foreach (var symbol in symbols.OrderBy(x => x, StringComparer.Ordinal))
					Console.WriteLine(symbol);
				Console.WriteLine();
			}

			return 0;

			IEnumerable<string> GetSymbols(ExpressionNode node)
			{
				if (node.Children.Count == 0)
					yield return node.Value;
				foreach (var symbol in node.Children.SelectMany(GetSymbols))
					yield return symbol;
			}
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
				"  -x|--exclude <glob> : Exclude matching files and directories",
				"  --format : Reformat #if and #elif expressions",
				"  --list-expr : Lists all unique #if and #elif expressions",
				"  --list-sym : Lists all symbols found in #if and #elif expressions",
				""));

		private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
	}
}
