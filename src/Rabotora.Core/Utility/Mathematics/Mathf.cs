namespace Rabotora.Core.Utility.Mathematics;

public static class Mathf
{
	public static bool Approximately(float a, float b, float epsilon = float.Epsilon)
	{
		if (float.IsNaN(a) || float.IsNaN(b))
		{
			return false;
		}
		return Math.Abs(a - b) < epsilon;
	}
	
	public static bool Approximately(double a, double b, double epsilon = double.Epsilon)
	{
		if (double.IsNaN(a) || double.IsNaN(b))
		{
			return false;
		}
		return Math.Abs(a - b) < epsilon;
	}
}