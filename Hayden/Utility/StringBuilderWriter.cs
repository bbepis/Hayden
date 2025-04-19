using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Hayden;

public class StringBuilderWriter : TextWriter
{
	public StringBuilder StringBuilder { get; } = new StringBuilder();
	public override Encoding Encoding => throw new System.NotImplementedException();

	public StringBuilderWriter() { }

	public override void Write(string value)
	{
		StringBuilder.Append(value);
	}

	public override void Write(char value)
	{
		StringBuilder.Append(value);
	}

	public override void Write(char[] buffer)
	{
		StringBuilder.Append(buffer);
	}

	public override void Write(char[] buffer, int index, int count)
	{
		StringBuilder.Append(buffer, index, count);
	}

	public override void Write([StringSyntax("CompositeFormat")] string format, params object[] arg)
	{
		StringBuilder.AppendFormat(format, arg);
	}

	public override string ToString()
	{
		return StringBuilder.ToString();
	}
}