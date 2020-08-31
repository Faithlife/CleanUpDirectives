using System.Collections.Generic;

namespace CleanUpDirectives
{
	internal class ExpressionNode
	{
		public ExpressionNode(string value, params ExpressionNode[] children) => (Value, Children) = (value, children);

		public string Value { get; }

		public IReadOnlyList<ExpressionNode> Children { get; }

		public override string ToString()
		{
			if (Children.Count == 0)
				return Value;

			if (Children.Count == 1)
			{
				var child = Children[0];
				var childNeedsParens = child.Children.Count > 1;
				return Value + (childNeedsParens ? "(" : "") + child + (childNeedsParens ? ")" : "");
			}

			if (Children.Count == 2)
			{
				var left = Children[0];
				var leftNeedsParens = left.Children.Count > 1 && left.Value != Value;
				var right = Children[1];
				var rightNeedsParens = right.Children.Count > 1 && right.Value != Value;
				return (leftNeedsParens ? "(" : "") + left + (leftNeedsParens ? ")" : "") +
					" " + Value + " " +
					(rightNeedsParens ? "(" : "") + right + (rightNeedsParens ? ")" : "");
			}

			return base.ToString() ?? "";
		}
	}
}
