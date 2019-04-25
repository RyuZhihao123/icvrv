using System;
using System.Linq;

namespace MyCubicInterp
{

    public class CubicInterpolation
    {
        private double[] sourceX = null;
        private double[] sourceY = null;
        private long N = 0;
        private double[][] coefs = null;

        public CubicInterpolation()
        {}

        /// <summary>
        /// 计算样条曲线的系数coefficients
        /// </summary>
        /// <param name="sourceX">原始数据横坐标 X</param>
        /// <param name="sourceY">原始数据纵坐标 Y</param>
        public int CalcCoefficients(double[] sourceX, double[] sourceY)
        {

            N = sourceX.LongLength;
            if (sourceX.LongLength != sourceY.LongLength)
                return -1;

            if (sourceX.LongLength <= 3)
                return -2;

            this.sourceX = sourceX;
            this.sourceY = sourceY;

            long Nx = N - 1;
            double[] dx = new double[Nx];

            double[] b = new double[N];
            double[] alfa = new double[N];
            double[] beta = new double[N];
            double[] gama = new double[N];

            coefs = new double[4][];
            for (long i = 0; i < 4; i++)
                coefs[i] = new double[Nx];

            for (long i = 0; i + 1 <= Nx; i++)
            {
                dx[i] = sourceX[i + 1] - sourceX[i];
                if (dx[i] == 0.0)
                    return -1;
            }

            for (long i = 1; i + 1 <= Nx; i++)
            {
                b[i] = 3.0 * (dx[i] * ((sourceY[i] - sourceY[i - 1]) / dx[i - 1]) + dx[i - 1] * ((sourceY[i + 1] - sourceY[i]) / dx[i]));
            }

            b[0] = ((dx[0] + 2.0 * (sourceX[2] - sourceX[0])) * dx[1] * ((sourceY[1] - sourceY[0]) / dx[0]) +
                        Math.Pow(dx[0], 2.0) * ((sourceY[2] - sourceY[1]) / dx[1])) / (sourceX[2] - sourceX[0]);

            b[N - 1] = (Math.Pow(dx[Nx - 1], 2.0) * ((sourceY[N - 2] - sourceY[N - 3]) / dx[Nx - 2]) + (2.0 * (sourceX[N - 1] - sourceX[N - 3])
                + dx[Nx - 1]) * dx[Nx - 2] * ((sourceY[N - 1] - sourceY[N - 2]) / dx[Nx - 1])) / (sourceX[N - 1] - sourceX[N - 3]);

            beta[0] = dx[1];
            gama[0] = sourceX[2] - sourceX[0];
            beta[N - 1] = dx[Nx - 1];
            alfa[N - 1] = (sourceX[N - 1] - sourceX[N - 3]);
            for (long i = 1; i < N - 1; i++)
            {
                beta[i] = 2.0 * (dx[i] + dx[i - 1]);
                gama[i] = dx[i];
                alfa[i] = dx[i - 1];
            }
            double c = 0.0;
            for (long i = 0; i < N - 1; i++)
            {
                c = beta[i];
                b[i] /= c;
                beta[i] /= c;
                gama[i] /= c;

                c = alfa[i + 1];
                b[i + 1] -= c * b[i];
                alfa[i + 1] -= c * beta[i];
                beta[i + 1] -= c * gama[i];
            }

            b[N - 1] /= beta[N - 1];
            beta[N - 1] = 1.0;
            for (long i = N - 2; i >= 0; i--)
            {
                c = gama[i];
                b[i] -= c * b[i + 1];
                gama[i] -= c * beta[i];
            }

            for (long i = 0; i < Nx; i++)
            {
                double dzzdx = (sourceY[i + 1] - sourceY[i]) / Math.Pow(dx[i], 2.0) - b[i] / dx[i];
                double dzdxdx = b[i + 1] / dx[i] - (sourceY[i + 1] - sourceY[i]) / Math.Pow(dx[i], 2.0);
                coefs[0][i] = (dzdxdx - dzzdx) / dx[i];
                coefs[1][i] = (2.0 * dzzdx - dzdxdx);
                coefs[2][i] = b[i];
                coefs[3][i] = sourceY[i];
            }
            return 0;
        }

        /// <summary> 
        /// 不推荐使用这个函数， 当 <code>newX > sourceX[N - 1]</code> 并且 <code>newX &lt; sourceX[0]</code>时
        /// </summary>
        public double Interpolate(double newX)
        {
            double newY = 0.0;

            for (long i = 0; i < N; i++)
            {
                if (newX == sourceX[i])
                    return sourceY[i];
            }

            double h = 0.0;

            if (newX < sourceX[0])
            {
                h = newX - sourceX[0];
                newY = coefs[3][0] + h * (coefs[2][0] + h * (coefs[1][0] + h * coefs[0][0] / 3.0) / 2.0);
                return newY;
            }
            if (newX > sourceX[N - 1])
            {
                h = newX - sourceX[N - 1];
                newY = coefs[3][N - 2] + h * (coefs[2][N - 2] + h * (coefs[1][N - 2] + h * coefs[0][N - 2] / 3.0) / 2.0);
                return newY;
            }

            for (long i = 0; i < N - 1; i++)
            {
                if (newX < sourceX[i + 1])
                {
                    h = newX - sourceX[i];
                    newY = coefs[3][i] + h * (coefs[2][i] + h * (coefs[1][i] + h * coefs[0][i] / 3.0) / 2.0);
                    break;
                }
            }
            return newY;
        }

    }
    /// <summary>
    /// Interpolation
    /// </summary>
    public class Interpolation
    {
        public static double[] CubicInterpolation(double[] sourceX, double[] sourceY, double[] newX)
        {
            long N = sourceX.LongLength;
            if (sourceX.LongLength != sourceY.LongLength)
                return null;

            if (sourceX.LongLength <= 3)
                return null;

            if (sourceX.LongLength >= newX.LongLength)
                return null;

            if (sourceX[0] != newX[0])
                return null;

            if (sourceX[sourceX.LongLength - 1] != newX[newX.LongLength - 1])
                return null;

            long Nx = N - 1;
            double[] dx = new double[Nx];

            double[] b = new double[N];
            double[] alfa = new double[N];
            double[] beta = new double[N];
            double[] gama = new double[N];

            double[][] coefs = new double[4][];
            for (long i = 0; i < 4; i++)
                coefs[i] = new double[Nx];

            for (long i = 0; i + 1 <= Nx; i++)
            {
                dx[i] = sourceX[i + 1] - sourceX[i];
                if (dx[i] == 0.0)
                    return null;
            }

            for (long i = 1; i + 1 <= Nx; i++)
            {
                b[i] = 3.0 * (dx[i] * ((sourceY[i] - sourceY[i - 1]) / dx[i - 1]) + dx[i - 1] * ((sourceY[i + 1] - sourceY[i]) / dx[i]));
            }

            b[0] = ((dx[0] + 2.0 * (sourceX[2] - sourceX[0])) * dx[1] * ((sourceY[1] - sourceY[0]) / dx[0]) +
                        Math.Pow(dx[0], 2.0) * ((sourceY[2] - sourceY[1]) / dx[1])) / (sourceX[2] - sourceX[0]);

            b[N - 1] = (Math.Pow(dx[Nx - 1], 2.0) * ((sourceY[N - 2] - sourceY[N - 3]) / dx[Nx - 2]) + (2.0 * (sourceX[N - 1] - sourceX[N - 3])
                + dx[Nx - 1]) * dx[Nx - 2] * ((sourceY[N - 1] - sourceY[N - 2]) / dx[Nx - 1])) / (sourceX[N - 1] - sourceX[N - 3]);

            beta[0] = dx[1];
            gama[0] = sourceX[2] - sourceX[0];
            beta[N - 1] = dx[Nx - 1];
            alfa[N - 1] = (sourceX[N - 1] - sourceX[N - 3]);
            for (long i = 1; i < N - 1; i++)
            {
                beta[i] = 2.0 * (dx[i] + dx[i - 1]);
                gama[i] = dx[i];
                alfa[i] = dx[i - 1];
            }
            double c = 0.0;
            for (long i = 0; i < N - 1; i++)
            {
                c = beta[i];
                b[i] /= c;
                beta[i] /= c;
                gama[i] /= c;

                c = alfa[i + 1];
                b[i + 1] -= c * b[i];
                alfa[i + 1] -= c * beta[i];
                beta[i + 1] -= c * gama[i];
            }

            b[N - 1] /= beta[N - 1];
            beta[N - 1] = 1.0;
            for (long i = N - 2; i >= 0; i--)
            {
                c = gama[i];
                b[i] -= c * b[i + 1];
                gama[i] -= c * beta[i];
            }

            for (long i = 0; i < Nx; i++)
            {
                double dzzdx = (sourceY[i + 1] - sourceY[i]) / Math.Pow(dx[i], 2.0) - b[i] / dx[i];
                double dzdxdx = b[i + 1] / dx[i] - (sourceY[i + 1] - sourceY[i]) / Math.Pow(dx[i], 2.0);
                coefs[0][i] = (dzdxdx - dzzdx) / dx[i];
                coefs[1][i] = (2.0 * dzzdx - dzdxdx);
                coefs[2][i] = b[i];
                coefs[3][i] = sourceY[i];
            }

            double[] newY = new double[newX.LongLength];
            long j = 0;
            for (long i = 0; i < N - 1; i++)
            {
                double h = 0.0;
                if (j >= newX.LongLength)
                    break;
                while (newX[j] < sourceX[i + 1])
                {
                    h = newX[j] - sourceX[i];
                    newY[j] = coefs[3][i] + h * (coefs[2][i] + h * (coefs[1][i] + h * coefs[0][i] / 3.0) / 2.0);
                    j++;
                    if (j >= newX.LongLength)
                        break;
                }
                if (j >= newX.LongLength)
                    break;
            }

            newY[newY.LongLength - 1] = sourceY[N - 1];
            return newY;
        }
    }
}