using System;
using System.Collections.Generic;
using System.Linq;

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

		public IEnumerable<string> GetSymbols()
		{
			if (Children.Count == 0)
				yield return Value;
			foreach (var symbol in Children.SelectMany(x => x.GetSymbols()))
				yield return symbol;
		}

		public static (ExpressionNode? Node, bool? Value) TryRemoveSymbol(ExpressionNode node, string symbol, bool defined)
		{
			if (node.Children.Count == 0)
			{
				if (node.Value == symbol)
					return (null, defined);
			}
			else if (node.Children.Count == 1)
			{
				var (child, value) = TryRemoveSymbol(node.Children[0], symbol, defined);

				if (child != null)
					return (new ExpressionNode(node.Value, child), null);

				if (value != null)
				{
					if (node.Value == "!")
						return (null, !value.Value);
					throw new InvalidOperationException("Unexpected operator: " + node.Value);
				}
			}
			else if (node.Children.Count == 2)
			{
				var (leftChild, leftValue) = TryRemoveSymbol(node.Children[0], symbol, defined);
				var (rightChild, rightValue) = TryRemoveSymbol(node.Children[1], symbol, defined);

				if (leftValue == null && rightValue == null)
				{
					if (leftChild != null || rightChild != null)
						return (new ExpressionNode(node.Value, leftChild ?? node.Children[0], rightChild ?? node.Children[1]), null);
				}
				else if (leftValue != null && rightValue != null)
				{
					if (node.Value == "&&")
						return (null, leftValue.Value && rightValue.Value);
					if (node.Value == "||")
						return (null, leftValue.Value || rightValue.Value);
					throw new InvalidOperationException("Unexpected operator: " + node.Value);
				}
				else
				{
					var child = leftValue is null ? (leftChild ?? node.Children[0]) : (rightChild ?? node.Children[1]);
					var value = leftValue is null ? rightValue!.Value : leftValue!.Value;
					if (node.Value == "&&")
					{
						if (value)
							return (child, null);
						return (null, false);
					}
					if (node.Value == "||")
					{
						if (value)
							return (null, true);
						return (child, null);
					}
					throw new InvalidOperationException("Unexpected operator: " + node.Value);
				}
			}
			else
			{
				throw new InvalidOperationException("Unexpected operator: " + node.Value);
			}

			return default;
		}
	}
}
