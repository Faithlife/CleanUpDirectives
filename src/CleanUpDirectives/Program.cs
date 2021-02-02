using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
			catch (Exception exception) when (exception is ArgsReaderException)
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

			var defines = new Dictionary<string, bool>();
			while (args.ReadOption("define") is string define)
				defines[define] = true;
			while (args.ReadOption("undef") is string undef)
				defines[undef] = false;

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
				throw new ArgsReaderException("No files found.");

			var excludePaths = GetFullPathsFromGlobs(excludes)
				.Select(x => Directory.Exists(x) ? (x + Path.DirectorySeparatorChar) : x)
				.ToList();
			paths = paths.Where(path => !excludePaths.Any(x => path.StartsWith(x, StringComparison.Ordinal))).ToList();
			if (paths.Count == 0)
				throw new ArgsReaderException("All files excluded.");

			var invalidPath = paths.FirstOrDefault(Directory.Exists);
			if (invalidPath != null)
				throw new ArgsReaderException($"Directories not supported; use glob to select files, e.g. **/*.cs{Environment.NewLine}Directory matched: {invalidPath}");

			var expressions = new HashSet<string>();
			var symbols = new HashSet<string>();
			var filesToSave = new List<(string Path, string Text)>();

			Console.WriteLine("Files:");
			foreach (var path in paths)
			{
				Console.Write($"{path} ");
				var fileText = File.ReadAllText(path);

				string?[] lines = Regex.Split(fileText, @"(?<=\n)");
				var needsSave = false;

				var stateStack = new Stack<State>();
				stateStack.Push(State.Normal);

				for (var index = 0; index < lines.Length; index++)
				{
					var line = lines[index]!;
					var state = stateStack.Peek();

					var match = Regex.Match(line, @"^\s*#((?'cmd'if|elif)(?=\W)\s*(?'expr'[^\r\n/]+?)|(?'cmd'else|endif))\s*([\r\n/]|$)", RegexOptions.ExplicitCapture);
					if (match.Success)
					{
						var commandCapture = match.Groups["cmd"];
						var command = commandCapture.Value;
						var isIf = command == "if";

						if (!isIf && stateStack.Count == 1)
							throw new ArgsReaderException($"Unexpected #{command}");

						if (isIf || command == "elif")
						{
							if (state == State.DeleteToEnd)
							{
								SetLine(null);

								if (isIf)
									stateStack.Push(State.DeleteToEnd);
							}
							else if (isIf && state == State.IfFalse)
							{
								SetLine(null);
								stateStack.Push(State.DeleteToEnd);
							}
							else if (!isIf && state == State.IfTrue)
							{
								SetLine(null);
								stateStack.Pop();
								stateStack.Push(State.DeleteToEnd);
							}
							else
							{
								var expressionCapture = match.Groups["expr"];
								var expression = expressionCapture.Value;

								var nodeBefore = ExpressionParser.Parse(expression);
								var nodeAfter = nodeBefore;
								bool? constant = null;

								foreach (var (symbol, defined) in defines)
								{
									var (node, value) = ExpressionNode.TryRemoveSymbol(nodeAfter, symbol, defined);
									if (node != null)
									{
										nodeAfter = node;
									}
									else if (value != null)
									{
										constant = value;
										break;
									}
								}

								if (constant is null)
								{
									if (shouldFormat || nodeBefore != nodeAfter)
									{
										var formatted = nodeAfter.ToString();
										if (expression != formatted)
										{
											SetLine(line.Substring(0, expressionCapture.Index) + formatted +
												line.Substring(expressionCapture.Index + expressionCapture.Length));
											expression = formatted;
										}
									}

									expressions.Add(expression);

									foreach (var symbol in nodeAfter.GetSymbols())
										symbols.Add(symbol);

									if (isIf)
									{
										stateStack.Push(State.Normal);
									}
									else if (state == State.IfFalse)
									{
										SetLine(line.Substring(0, commandCapture.Index) + "if" +
											line.Substring(commandCapture.Index + commandCapture.Length));
										stateStack.Pop();
										stateStack.Push(State.Normal);
									}
								}
								else if (isIf)
								{
									stateStack.Push(constant.Value ? State.IfTrue : State.IfFalse);
									SetLine(null);
								}
								else
								{
									stateStack.Pop();
									stateStack.Push(constant.Value ? State.IfTrue : State.IfFalse);
									SetLine("#else");
								}
							}
						}
						else if (command == "else")
						{
							if (state == State.DeleteToEnd)
							{
								SetLine(null);
							}
							else if (state == State.IfTrue)
							{
								stateStack.Pop();
								stateStack.Push(State.DeleteToEnd);
								SetLine(null);
							}
							else if (state == State.IfFalse)
							{
								stateStack.Pop();
								stateStack.Push(State.IfTrue);
								SetLine(null);
							}
						}
						else if (command == "endif")
						{
							if (state == State.DeleteToEnd || state == State.IfTrue || state == State.IfFalse)
								SetLine(null);

							stateStack.Pop();
						}
						else
						{
							throw new InvalidOperationException("Unexpected command from regex.");
						}
					}
					else if (state == State.DeleteToEnd || state == State.IfFalse)
					{
						SetLine(null);
					}

					void SetLine(string? text)
					{
						lines[index] = text;
						needsSave = true;
					}
				}

				if (needsSave)
				{
					filesToSave.Add((Path: path, Text: string.Concat(lines.Where(x => x != null))));
					Console.WriteLine("[edited]");
				}
				else
				{
					Console.WriteLine("[viewed]");
				}
			}
			Console.WriteLine();

			if (filesToSave.Count != 0)
			{
				Console.WriteLine("This tool will edit the specified files in place. This tool may have bugs,");
				Console.WriteLine("so your files should be backed up, and you should check the changes line by line.");
				Console.Write($"ARE YOU SURE you want to edit {filesToSave.Count} files? (yes/no): ");
				var answer = Console.ReadLine();
				if (string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
				{
					foreach (var fileToSave in filesToSave)
						File.WriteAllText(fileToSave.Path, fileToSave.Text, s_utf8);
				}
				else
				{
					Console.WriteLine("Changes not saved.");
				}
				Console.WriteLine();
			}

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
		}

		private static IReadOnlyList<string> GetFullPathsFromGlobs(IEnumerable<string> globs) =>
			globs
				.Select(x => (Path: x, Root: Path.GetPathRoot(x) ?? ""))
				.Select(x => (Path: x.Path.Substring(x.Root.Length), Directory: x.Root.Length != 0 ? x.Root : Environment.CurrentDirectory))
				.SelectMany(x => Glob.FilesAndDirectories(x.Directory, x.Path, GlobOptions.CaseInsensitive).Select(p => Path.Combine(x.Directory, p)))
				.Distinct()
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToList();

		private static ArgsReaderException CreateUsageException() =>
			new ArgsReaderException(string.Join(Environment.NewLine,
				"Usage: CleanUpDirectives <glob> ... [options]",
				"  <glob> : Clean up matching files, e.g. **/*.cs",
				"",
				"Options:",
				"  -x|--exclude <glob> : Exclude matching files and directories",
				"  --format : Reformat #if and #elif expressions",
				"  --define <symbol> : Remove uses of symbol as though it were defined",
				"  --undef <symbol> : Remove uses of symbol as though it were not defined",
				"  --list-expr : Lists all unique #if and #elif expressions",
				"  --list-symbols : Lists all symbols found in #if and #elif expressions",
				""));

		private enum State
		{
			Normal,
			DeleteToEnd,
			IfTrue,
			IfFalse,
		}

		private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
	}
}
