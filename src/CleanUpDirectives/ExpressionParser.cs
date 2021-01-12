using System;
using Faithlife.Parsing;

namespace CleanUpDirectives
{
	internal static class ExpressionParser
	{
		public static ExpressionNode Parse(string text) => Expression.End().Parse(text);

		private static IParser<ExpressionNode> Name { get; } =
			Parser.Regex("[A-Za-z0-9_]+").Select(x => new ExpressionNode(x.Value));

		private static IParser<string> Op(string op) =>
			Parser.String(op, StringComparison.Ordinal).Trim();

		private static IParser<ExpressionNode> Expression { get; } =
			Parser.Ref(() => Expression!).Bracketed(Op("("), Op(")")).Or(Name)
				.ChainUnary(Op("!").Trim(), (x, y) => new ExpressionNode(x, y))
				.ChainBinary(Op("&&").Trim(), (x, y, z) => new ExpressionNode(x, y, z))
				.ChainBinary(Op("||").Trim(), (x, y, z) => new ExpressionNode(x, y, z));

		private static IParser<T> ChainUnary<T, TOp>(this IParser<T> parser, IParser<TOp> opParser, Func<TOp, T, T> apply)
		{
			return opParser.Then(Next).Or(parser);

			IParser<T> Next(TOp outer) => opParser.Then(Next).Or(parser).Select(first => apply(outer, first));
		}

		private static IParser<T> ChainBinary<T, TOp>(this IParser<T> parser, IParser<TOp> opParser, Func<TOp, T, T, T> apply)
		{
			return parser.Then(Next);

			IParser<T> Next(T first) => opParser.Then(op => parser.Then(second => Next(apply(op, first, second)))).Or(Parser.Success(first));
		}
	}
}
