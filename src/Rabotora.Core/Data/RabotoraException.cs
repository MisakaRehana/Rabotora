namespace Rabotora.Core.Data;

public class RabotoraException : Exception
{
	public string Module { get; }

	public RabotoraException()
	{
		Module = string.Empty;
	}

	public RabotoraException(string message) : base(message)
	{
		Module = string.Empty;
	}
	
	public RabotoraException(string message, Exception innerException) : base(message, innerException)
	{
		Module = string.Empty;
	}
	
	public RabotoraException(string module, string message) : base(message)
	{
		Module = module;
	}
	
	public RabotoraException(string module, string message, Exception innerException) : base(message, innerException)
	{
		Module = module;
	}
}