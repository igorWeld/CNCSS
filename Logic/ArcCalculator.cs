using CNCSS.Data;

namespace CNCSS.Logic
{
    /// <summary>
    /// Геометрия дуг G2/G3 в плоскости G17/G18/G19: центр по I,J,K относительно начала или по R.
    /// </summary>
    public static class ArcCalculator
    {
        private const double Eps = 1e-9;
        private const double Tol = 1e-6;

        public static bool TryComputeArc(
            double startX, double startY, double startZ,
            double endX, double endY, double endZ,
            GCodeTemplate plane,
            bool isG2,
            double? i, double? j, double? k,
            double? r,
            out ArcGeometry? arc)
        {
            arc = null;
            int planeNum = plane.Number;

            if (planeNum is not (17 or 18 or 19))
                return false;

            GetPlaneCoords(planeNum, startX, startY, startZ, out double su, out double sv);
            GetPlaneCoords(planeNum, endX, endY, endZ, out double eu, out double ev);

            double cu, cv, radius;

            bool hasIjk = HasIjkForPlane(planeNum, i, j, k);
            bool hasR = r.HasValue && Math.Abs(r.Value) > Eps;

            if (hasIjk)
            {
                if (!TryCenterFromIjk(planeNum, su, sv, i, j, k, out cu, out cv, out radius))
                    return false;

                if (!RadiiMatchEnd(cu, cv, su, sv, eu, ev, radius, out double rEnd))
                    return false;

                if (Math.Abs(radius - rEnd) > Tol * Math.Max(1, radius))
                    return false;
            }
            else if (hasR)
            {
                if (!TryCenterFromRadius(planeNum, su, sv, eu, ev, r!.Value, isG2, out cu, out cv, out radius))
                    return false;
            }
            else
            {
                return false;
            }

            double sweep = ComputeSweepRad(cu, cv, su, sv, eu, ev, isG2);

            arc = new ArcGeometry
            {
                Plane = planeNum,
                Radius = radius,
                CenterU = cu,
                CenterV = cv,
                StartAngleRad = AngleInPlane(cu, cv, su, sv),
                SweepAngleRad = sweep,
                IsClockwise = isG2,
                StartX = startX,
                StartY = startY,
                StartZ = startZ,
                EndX = endX,
                EndY = endY,
                EndZ = endZ
            };

            return true;
        }

        /// <summary>Углы дуги в машинной плоскости по концам и центру (после перевода из WCS).</summary>
        public static (double startAngleRad, double sweepAngleRad) ComputeMachinePlaneAngles(
            int plane,
            double startX, double startY, double startZ,
            double endX, double endY, double endZ,
            double centerU, double centerV,
            bool isG2)
        {
            GetPlaneCoords(plane, startX, startY, startZ, out double su, out double sv);
            GetPlaneCoords(plane, endX, endY, endZ, out double eu, out double ev);
            double startAngle = AngleInPlane(centerU, centerV, su, sv);
            double sweep = ComputeSweepRad(centerU, centerV, su, sv, eu, ev, isG2);
            return (startAngle, sweep);
        }

        private static bool HasIjkForPlane(int planeNum, double? i, double? j, double? k)
        {
            return planeNum switch
            {
                17 => i.HasValue || j.HasValue,
                18 => i.HasValue || k.HasValue,
                19 => j.HasValue || k.HasValue,
                _ => false
            };
        }

        /// <summary>I,J,K — приращения от начальной точки дуги до центра (как в ISO 6983).</summary>
        private static bool TryCenterFromIjk(
            int planeNum,
            double su, double sv,
            double? i, double? j, double? k,
            out double cu, out double cv, out double radius)
        {
            cu = cv = radius = 0;
            double di = i ?? 0;
            double dj = j ?? 0;
            double dk = k ?? 0;

            switch (planeNum)
            {
                case 17:
                    cu = su + di;
                    cv = sv + dj;
                    break;
                case 18:
                    cu = su + di;
                    cv = sv + dk;
                    break;
                case 19:
                    cu = su + dj;
                    cv = sv + dk;
                    break;
                default:
                    return false;
            }

            radius = Math.Sqrt((su - cu) * (su - cu) + (sv - cv) * (sv - cv));
            return radius > Eps;
        }

        private static bool RadiiMatchEnd(double cu, double cv, double su, double sv, double eu, double ev, double rStart, out double rEnd)
        {
            rEnd = Math.Sqrt((eu - cu) * (eu - cu) + (ev - cv) * (ev - cv));
            return rEnd > Eps;
        }

        /// <summary>
        /// R по модулю — радиус; знак выбирает короткую или длинную дугу окружности, центр согласно G2/G3.
        /// </summary>
        private static bool TryCenterFromRadius(
            int planeNum,
            double su, double sv,
            double eu, double ev,
            double rSigned,
            bool isG2,
            out double cu, out double cv,
            out double radius)
        {
            cu = cv = radius = 0;
            double dx = eu - su;
            double dy = ev - sv;
            double d2 = dx * dx + dy * dy;
            double chord = Math.Sqrt(d2);

            if (chord < Eps)
                return false;

            double absR = Math.Abs(rSigned);
            if (absR < chord * 0.5 - Eps)
                return false;

            double hSq = absR * absR - (d2 * 0.25);
            double h = Math.Sqrt(Math.Max(0, hSq));

            // Середина хорды
            double mx = (su + eu) * 0.5;
            double my = (sv + ev) * 0.5;

            // Нормаль к хорде
            double nx = -dy / chord;
            double ny = dx / chord;

            // Два возможных центра
            double c1u = mx + h * nx;
            double c1v = my + h * ny;
            double c2u = mx - h * nx;
            double c2v = my - h * ny;

            // Вычисляем размах для первого центра в нужном направлении
            double sweep1 = ComputeSweepRad(c1u, c1v, su, sv, eu, ev, isG2);
            double absSweep1 = Math.Abs(sweep1);

            // Правило для R в G-коде:
            // R > 0: дуга <= 180 градусов (absSweep <= PI)
            // R < 0: дуга > 180 градусов (absSweep > PI)
            bool c1IsMinor = absSweep1 <= Math.PI + 1e-7;

            if (rSigned > 0)
            {
                if (c1IsMinor) { cu = c1u; cv = c1v; }
                else { cu = c2u; cv = c2v; }
            }
            else
            {
                if (c1IsMinor) { cu = c2u; cv = c2v; }
                else { cu = c1u; cv = c1v; }
            }

            radius = absR;
            return true;
        }

        private static double Cross2d(double ux, double uy, double vx, double vy) => ux * vy - uy * vx;

        /// <summary>Кратчайший знаковый угол от радиуса к началу к радиусу к концу; G3 — положительный размах по CCW, G2 — отрицательный по CW.</summary>
        private static double ComputeSweepRad(double cu, double cv, double su, double sv, double eu, double ev, bool isG2)
        {
            double u1 = su - cu;
            double v1 = sv - cv;
            double u2 = eu - cu;
            double v2 = ev - cv;

            double cross = Cross2d(u1, v1, u2, v2);
            double dot = u1 * u2 + v1 * v2;
            double shortest = Math.Atan2(cross, dot);

            if (!isG2)
            {
                // G3 (CCW): угол должен быть положительным [0, 2PI]
                return shortest >= 0 ? shortest : shortest + 2 * Math.PI;
            }
            else
            {
                // G2 (CW): угол должен быть отрицательным [-2PI, 0]
                return shortest <= 0 ? shortest : shortest - 2 * Math.PI;
            }
        }

        private static double AngleInPlane(double cu, double cv, double u, double v)
        {
            return Math.Atan2(v - cv, u - cu);
        }

        private static void GetPlaneCoords(int planeNum, double x, double y, double z, out double u, out double v)
        {
            switch (planeNum)
            {
                case 17:
                    u = x;
                    v = y;
                    break;
                case 18:
                    u = x;
                    v = z;
                    break;
                case 19:
                    u = y;
                    v = z;
                    break;
                default:
                    u = v = 0;
                    break;
            }
        }
    }
}
