namespace Rabotora.Core.Data;

internal sealed class RabotoraInternalException : RabotoraException
{
	public string CodeFile { get; }
	
	public RabotoraInternalException(string codeFile)
	{
		CodeFile = codeFile;
	}

	public RabotoraInternalException(string codeFile, string message) : base(message)
	{
		CodeFile = codeFile;
	}
	
	public RabotoraInternalException(string codeFile, string message, Exception innerException) : base(message, innerException)
	{
		CodeFile = codeFile;
	}
}