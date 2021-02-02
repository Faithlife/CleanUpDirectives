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
	}
}
