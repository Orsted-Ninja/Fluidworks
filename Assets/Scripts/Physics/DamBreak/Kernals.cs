 using System;
 
 
namespace SPHCalculations 
{
	
	public class HelperMath
	{
		const float PI = 3.1415926f;

		public static float SmoothingKernelPoly6(float dst, float radius)
		{
			if (dst < radius)
			{
				float scale = 315f / (64f * PI * (float)Math.Pow(Math.Abs(radius), 9));
				float v = radius * radius - dst * dst;
				return (float)Math.Pow(v,3) * scale;
			}
			return 0;
		}

		public static float SmoothingKernelSpiky(float dst, float radius)
		{
			if (dst < radius)
			{
				float scale = 15f / ( PI * (float)Math.Pow(Math.Abs(radius), 6));
				float v = radius - dst;
				return (float)Math.Pow(v,3) * scale;
			}
			return 0;
		}

		public static float SpikyFirstDerivative(float dst, float radius)
		{
			if (dst < radius)
			{
				float scale = -45f / ( PI * (float)Math.Pow(Math.Abs(radius), 6));
				float v = radius - dst;
				return (float)Math.Pow(v,2) * scale;
			}
			return 0;
		}

		public static float SpikySecondDerivative(float dst, float radius)
		{
			if (dst < radius)
			{
				float scale = 6 * 15f / ( PI * (float)Math.Pow(Math.Abs(radius), 6));
				float v = radius - dst;
				return v * scale;
			}
			return 0;
		}

		public static float Poly6KernelDerivative(float dst, float radius)
		{
			if (dst >= 0 && dst < radius)
			{
				float h2 = radius * radius;
				float r2 = dst * dst;
				float term = h2 - r2;
				return -945f / (32f * (float)Math.PI * (float)Math.Pow(radius, 9)) * dst * term * term;
			}
			else
			{
				return 0f;
			}
		}
	}


	
}

/*
// 3d conversion: done
float SpikyKernelPow3(float dst, float radius)
{
	if (dst < radius)
	{
		float scale = 15 / (PI * pow(radius, 6));
		float v = radius - dst;
		return v * v * v * scale;
	}
	return 0;
}

// 3d conversion: done
//Integrate[(h-r)^2 r^2 Sin[θ], {r, 0, h}, {θ, 0, π}, {φ, 0, 2*π}]
float SpikyKernelPow2(float dst, float radius)
{
	if (dst < radius)
	{
		float scale = 15 / (2 * PI * pow(radius, 5));
		float v = radius - dst;
		return v * v * scale;
	}
	return 0;
}

// 3d conversion: done
float DerivativeSpikyPow3(float dst, float radius)
{
	if (dst <= radius)
	{
		float scale = 45 / (pow(radius, 6) * PI);
		float v = radius - dst;
		return -v * v * scale;
	}
	return 0;
}

// 3d conversion: done
float DerivativeSpikyPow2(float dst, float radius)
{
	if (dst <= radius)
	{
		float scale = 15 / (pow(radius, 5) * PI);
		float v = radius - dst;
		return -v * scale;
	}
	return 0;
}

float DensityKernel(float dst, float radius)
{
	//return SmoothingKernelPoly6(dst, radius);
	return SpikyKernelPow2(dst, radius);
}

float NearDensityKernel(float dst, float radius)
{
	return SpikyKernelPow3(dst, radius);
}

float DensityDerivative(float dst, float radius)
{
	return DerivativeSpikyPow2(dst, radius);
}

float NearDensityDerivative(float dst, float radius)
{
	return DerivativeSpikyPow3(dst, radius);
}

*/