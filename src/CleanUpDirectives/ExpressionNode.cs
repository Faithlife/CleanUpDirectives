using System.Collections.Generic;

namespace CleanUpDirectives
{
	internal class ExpressionNode
	{
		public ExpressionNode(string value, params ExpressionNode[] children) => (Value, Children) = (value, children);

		public string Value { get; }

		public IReadOnlyList<ExpressionNode> Children { get; }
	}
}
