using System;

class Program
{
    static void Main()
    {
        var rnd = new Random(1101110);
        byte[] _p = new byte[512];
        for (int i=0; i<256; i++) _p[i] = (byte)i;
        for (int i=0; i<256; i++) { int j = rnd.Next(256); (_p[i], _p[j]) = (_p[j], _p[i]); }
        for (int i=0; i<256; i++) _p[256+i] = _p[i];

        float F2 = 0.5f * (MathF.Sqrt(3.0f) - 1.0f);
        float G2 = (3.0f - MathF.Sqrt(3.0f)) / 6.0f;

        float GetNoise2D(float x, float y) {
            float s = (x + y) * F2;
            int i = (x + s) >= 0 ? (int)(x + s) : (int)(x + s) - 1;
            int j = (y + s) >= 0 ? (int)(y + s) : (int)(y + s) - 1;
            float t = (i + j) * G2;
            float X0 = i - t, Y0 = j - t;
            float x0 = x - X0, y0 = y - Y0;
            int i1, j1; if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }
            float x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
            float x2 = x0 - 1.0f + 2.0f * G2, y2 = y0 - 1.0f + 2.0f * G2;
            int ii = i & 255, jj = j & 255;
            int gi0 = _p[ii + _p[jj]] % 12, gi1 = _p[ii + i1 + _p[jj + j1]] % 12, gi2 = _p[ii + 1 + _p[jj + 1]] % 12;
            float Grad(int h, float u, float v) {
                int h3 = h & 7;
                float uu = h3 < 4 ? u : v, vv = h3 < 4 ? v : u;
                return ((h3 & 1) != 0 ? -uu : uu) + ((h3 & 2) != 0 ? -2.0f * vv : 2.0f * vv);
            }
            float t0 = 0.5f - x0*x0 - y0*y0, n0 = t0<0 ? 0 : t0*t0*t0*t0*Grad(gi0,x0,y0);
            float t1 = 0.5f - x1*x1 - y1*y1, n1 = t1<0 ? 0 : t1*t1*t1*t1*Grad(gi1,x1,y1);
            float t2 = 0.5f - x2*x2 - y2*y2, n2 = t2<0 ? 0 : t2*t2*t2*t2*Grad(gi2,x2,y2);
            return 70.0f * (n0 + n1 + n2);
        }

        for (int z = 0; z < 16; z+=8) {
            for (int x = 0; x < 16; x+=8) {
                float n1 = GetNoise2D(x * 0.004f, z * 0.004f);
                float n2 = GetNoise2D(x * 0.008f, z * 0.008f) * 0.5f;
                float n3 = GetNoise2D(x * 0.016f, z * 0.016f) * 0.25f;
                float finalNoise = (n1 + n2 + n3) / 1.75f;
                int h = 40 + (int)(finalNoise * 20f);
                Console.WriteLine($"({x},{z}) -> h={h}");
            }
        }
    }
}
