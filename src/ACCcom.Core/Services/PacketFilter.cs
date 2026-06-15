using System.Globalization;
using System.Text;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class PacketFilterEngine
{
    private readonly FilterNode? _root;

    public PacketFilterEngine(string filterExpression)
    {
        if (string.IsNullOrWhiteSpace(filterExpression))
        {
            _root = null;
            return;
        }

        var tokens = Tokenizer.Tokenize(filterExpression);
        _root = Parser.Parse(tokens);
    }

    public bool Matches(LogEntry entry)
    {
        if (_root == null)
            return true;

        return _root.Evaluate(entry);
    }
}

internal abstract class FilterNode
{
    public abstract bool Evaluate(LogEntry entry);
}

internal class ComparisonNode : FilterNode
{
    public string Field { get; }
    public string Op { get; }
    public string Value { get; }

    public ComparisonNode(string field, string op, string value)
    {
        Field = field;
        Op = op;
        Value = value;
    }

    public override bool Evaluate(LogEntry entry)
    {
        var actual = GetFieldValue(entry);
        if (actual == null)
            return false;

        return Op switch
        {
            "==" => string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
            ">=" => CompareTimeOrString(actual, Value) >= 0,
            "<=" => CompareTimeOrString(actual, Value) <= 0,
            ">" => CompareTimeOrString(actual, Value) > 0,
            "<" => CompareTimeOrString(actual, Value) < 0,
            "contains" => actual.Contains(Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static int CompareTimeOrString(string a, string b)
    {
        if (TimeSpan.TryParse(a, CultureInfo.InvariantCulture, out var ta) &&
            TimeSpan.TryParse(b, CultureInfo.InvariantCulture, out var tb))
            return ta.CompareTo(tb);

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetFieldValue(LogEntry entry)
    {
        var field = Field.ToLowerInvariant();
        return field switch
        {
            "direction" => entry.Direction,
            "hex" => entry.RawHex,
            "text" => entry.Text,
            "port" => entry.PortTag,
            "modbus.func" => GetModbusFunc(entry),
            "modbus.slave" => GetModbusSlave(entry),
            "time" => entry.Timestamp.ToString("HH:mm:ss"),
            _ => null
        };
    }

    private static string? GetModbusFunc(LogEntry entry)
    {
        if (entry.Fields == null)
            return null;

        var funcField = entry.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, "FunctionCode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Name, "Func", StringComparison.OrdinalIgnoreCase));

        if (funcField == null)
            return null;

        if (byte.TryParse(funcField.DisplayValue, NumberStyles.HexNumber, null, out var code))
            return $"0x{code:X2}";

        return funcField.DisplayValue;
    }

    private static string? GetModbusSlave(LogEntry entry)
    {
        if (entry.Fields == null)
            return null;

        var slaveField = entry.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, "SlaveId", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Name, "Slave", StringComparison.OrdinalIgnoreCase));

        return slaveField?.DisplayValue;
    }
}

internal class LogicalNode : FilterNode
{
    public string Op { get; }
    public FilterNode Left { get; }
    public FilterNode Right { get; }

    public LogicalNode(string op, FilterNode left, FilterNode right)
    {
        Op = op;
        Left = left;
        Right = right;
    }

    public override bool Evaluate(LogEntry entry)
    {
        return Op switch
        {
            "and" => Left.Evaluate(entry) && Right.Evaluate(entry),
            "or" => Left.Evaluate(entry) || Right.Evaluate(entry),
            _ => false
        };
    }
}

internal class NotNode : FilterNode
{
    public FilterNode Child { get; }

    public NotNode(FilterNode child)
    {
        Child = child;
    }

    public override bool Evaluate(LogEntry entry)
    {
        return !Child.Evaluate(entry);
    }
}

internal class AlwaysTrueNode : FilterNode
{
    public override bool Evaluate(LogEntry entry) => true;
}

internal static class Tokenizer
{
    public static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var i = 0;

        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            if (input[i] == '(' || input[i] == ')')
            {
                tokens.Add(input[i].ToString());
                i++;
                continue;
            }

            if (input[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < input.Length && input[i] != '"')
                {
                    sb.Append(input[i]);
                    i++;
                }
                if (i < input.Length) i++;
                tokens.Add(sb.ToString());
                continue;
            }

            if (input[i] == '=' && i + 1 < input.Length && input[i + 1] == '=')
            {
                tokens.Add("==");
                i += 2;
                continue;
            }

            if (input[i] == '!' && i + 1 < input.Length && input[i + 1] == '=')
            {
                tokens.Add("!=");
                i += 2;
                continue;
            }

            if (input[i] == '>' && i + 1 < input.Length && input[i + 1] == '=')
            {
                tokens.Add(">=");
                i += 2;
                continue;
            }

            if (input[i] == '<' && i + 1 < input.Length && input[i + 1] == '=')
            {
                tokens.Add("<=");
                i += 2;
                continue;
            }

            if (input[i] == '>')
            {
                tokens.Add(">");
                i++;
                continue;
            }

            if (input[i] == '<')
            {
                tokens.Add("<");
                i++;
                continue;
            }

            if (input[i] == '=' || input[i] == '!')
            {
                tokens.Add(input[i].ToString());
                i++;
                continue;
            }

            var word = new StringBuilder();
            while (i < input.Length && !char.IsWhiteSpace(input[i]) &&
                   input[i] != '(' && input[i] != ')' && input[i] != '"' &&
                   input[i] != '=' && input[i] != '!' && input[i] != '>' && input[i] != '<')
            {
                word.Append(input[i]);
                i++;
            }

            if (word.Length > 0)
                tokens.Add(word.ToString());
        }

        return tokens;
    }
}

internal static class Parser
{
    public static FilterNode Parse(List<string> tokens)
    {
        int pos = 0;
        var result = ParseOr(tokens, ref pos);
        return result ?? new AlwaysTrueNode();
    }

    private static FilterNode? ParseOr(List<string> tokens, ref int pos)
    {
        var left = ParseAnd(tokens, ref pos);

        while (pos < tokens.Count && tokens[pos] == "or")
        {
            pos++;
            var right = ParseAnd(tokens, ref pos);
            if (right != null)
                left = new LogicalNode("or", left ?? new AlwaysTrueNode(), right);
        }

        return left;
    }

    private static FilterNode? ParseAnd(List<string> tokens, ref int pos)
    {
        var left = ParseNot(tokens, ref pos);

        while (pos < tokens.Count && tokens[pos] == "and")
        {
            pos++;
            var right = ParseNot(tokens, ref pos);
            if (right != null)
                left = new LogicalNode("and", left ?? new AlwaysTrueNode(), right);
        }

        return left;
    }

    private static FilterNode? ParseNot(List<string> tokens, ref int pos)
    {
        if (pos < tokens.Count && tokens[pos] == "not")
        {
            pos++;
            var child = ParsePrimary(tokens, ref pos);
            if (child != null)
                return new NotNode(child);
        }

        return ParsePrimary(tokens, ref pos);
    }

    private static FilterNode? ParsePrimary(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            return null;

        if (tokens[pos] == "(")
        {
            pos++; // skip '('
            var node = ParseOr(tokens, ref pos);
            if (pos < tokens.Count && tokens[pos] == ")")
                pos++; // skip ')'
            return node;
        }

        if (tokens[pos] == "not")
        {
            pos++;
            var child = ParsePrimary(tokens, ref pos);
            if (child != null)
                return new NotNode(child);
        }

        return ParseComparison(tokens, ref pos);
    }

    private static FilterNode? ParseComparison(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            return null;

        var field = tokens[pos];
        pos++;

        if (pos >= tokens.Count)
            return null;

        var op = tokens[pos];
        pos++;

        if (pos >= tokens.Count)
            return null;

        var value = tokens[pos];
        pos++;

        return new ComparisonNode(field, op, value);
    }
}
