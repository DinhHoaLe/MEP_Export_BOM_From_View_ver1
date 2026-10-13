using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEP_Export_BOM_From_View_ver1
{
    // Conservative bounds tests: a passing box can still contain empty space.
    internal static class SpatialFilter
    {
        private const double Tolerance = 1e-7;

        public static Func<Element, bool> ForScopeBox(Element scopeBox, Transform placement = null)
        {
            var points = new List<XYZ>();
            var lines = new List<Line>();
            using (var options = new Options { IncludeNonVisibleObjects = true })
            {
                var geometry = scopeBox.get_Geometry(options);
                if (geometry != null) ReadGeometry(geometry, points, lines);
            }
            var horizontal = lines.FirstOrDefault(line => Math.Abs(line.Direction.Z) < Tolerance && line.Length > Tolerance);
            if (horizontal == null || points.Count < 8)
                throw new InvalidOperationException("Cannot read Scope Box edges. Select another Scope Box.");
            var x = new XYZ(horizontal.Direction.X, horizontal.Direction.Y, 0).Normalize();
            var frame = Transform.Identity;
            frame.Origin = points[0]; frame.BasisX = x; frame.BasisY = XYZ.BasisZ.CrossProduct(x); frame.BasisZ = XYZ.BasisZ;
            var local = points.Select(frame.Inverse.OfPoint).ToList();
            var min = new XYZ(local.Min(p => p.X), local.Min(p => p.Y), local.Min(p => p.Z));
            var max = new XYZ(local.Max(p => p.X), local.Max(p => p.Y), local.Max(p => p.Z));
            return element => IntersectsBox(element, frame, min, max, placement);
        }

        public static Func<Element, bool> ForView(View view, Transform placement = null)
        {
            var view3d = view as View3D;
            if (view3d != null)
            {
                if (view3d.IsPerspective)
                    throw new InvalidOperationException("Use an orthographic 3D view; perspective views are not supported.");
                if (!view3d.IsSectionBoxActive) return element => true;
                var section = view3d.GetSectionBox();
                var transform = section.Transform; var min = section.Min; var max = section.Max;
                // SAT in 3D handles tilted section boxes as well as rotated boxes.
                return element => IntersectsBox(element, transform, min, max, placement);
            }
            if (!view.CropBoxActive) return element => true;
            var frame = view.CropBox.Transform.Inverse;
            var polygons = new List<List<XYZ>>();
            using (var manager = view.GetCropRegionShapeManager())
            {
                if (manager.NumberOfSplitRegions > 1)
                    throw new InvalidOperationException("Split crop plans are not supported in this version. Use a floor plan with an unsplit crop region.");
                foreach (var loop in manager.GetCropShape())
                {
                    var polygon = new List<XYZ>();
                    foreach (var curve in loop)
                    {
                        var tessellated = curve.Tessellate();
                        for (int i = 0; i < tessellated.Count - 1; i++) polygon.Add(frame.OfPoint(tessellated[i]));
                    }
                    if (polygon.Count >= 3) polygons.Add(polygon);
                }
            }
            if (polygons.Count == 0) throw new InvalidOperationException("Cannot read this view's crop boundary.");
            return element =>
            {
                var box = PlacedBox(element, placement);
                if (box == null) return false;
                var points = Corners(box).Select(frame.OfPoint).ToList();
                var rectangle = Rectangle(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
                return polygons.Any(polygon => PolygonsIntersect(rectangle, polygon));
            };
        }

        private static void ReadGeometry(GeometryElement geometry, List<XYZ> points, List<Line> lines)
        {
            foreach (var item in geometry)
            {
                var curve = item as Curve;
                if (curve != null)
                {
                    points.Add(curve.GetEndPoint(0)); points.Add(curve.GetEndPoint(1));
                    if (curve is Line) lines.Add((Line)curve);
                }
                var instance = item as GeometryInstance;
                if (instance != null) ReadGeometry(instance.GetInstanceGeometry(), points, lines);
            }
        }

        private static BoundingBoxXYZ PlacedBox(Element element, Transform placement)
        {
            var box = element.get_BoundingBox(null);
            if (box != null && placement != null) box.Transform = placement.Multiply(box.Transform);
            return box;
        }

        private static bool IntersectsBox(Element element, Transform frame, XYZ min, XYZ max, Transform placement = null)
        {
            var elementBox = PlacedBox(element, placement);
            if (elementBox == null) return false;
            var a = Corners(elementBox);
            var b = Corners(new BoundingBoxXYZ { Min = min, Max = max, Transform = frame });
            var aAxes = new[] { elementBox.Transform.BasisX, elementBox.Transform.BasisY, elementBox.Transform.BasisZ };
            var bAxes = new[] { frame.BasisX, frame.BasisY, frame.BasisZ };
            var axes = new List<XYZ>(aAxes.Concat(bAxes));
            foreach (var axisA in aAxes)
                foreach (var axisB in bAxes)
                    axes.Add(axisA.CrossProduct(axisB));
            foreach (var axis in axes)
            {
                if (axis.GetLength() < Tolerance) continue;
                var normalized = axis.Normalize();
                var pa = a.Select(p => p.DotProduct(normalized)).ToList();
                var pb = b.Select(p => p.DotProduct(normalized)).ToList();
                if (pa.Max() < pb.Min() - Tolerance || pb.Max() < pa.Min() - Tolerance) return false;
            }
            return true;
        }

        private static List<XYZ> Corners(BoundingBoxXYZ box)
        {
            var points = new List<XYZ>();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                points.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return points;
        }

        private static List<XYZ> Rectangle(double minX, double minY, double maxX, double maxY)
        { return new List<XYZ> { new XYZ(minX, minY, 0), new XYZ(maxX, minY, 0), new XYZ(maxX, maxY, 0), new XYZ(minX, maxY, 0) }; }

        private static bool PolygonsIntersect(IList<XYZ> a, IList<XYZ> b)
        {
            if (a.Any(p => Inside(p, b)) || b.Any(p => Inside(p, a))) return true;
            for (int i = 0; i < a.Count; i++) for (int j = 0; j < b.Count; j++)
                if (SegmentsIntersect(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
            return false;
        }

        private static bool Inside(XYZ p, IList<XYZ> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i]; var b = polygon[j];
                if (OnSegment(a, b, p)) return true;
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static double Cross(XYZ a, XYZ b, XYZ p)
        { return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X); }

        private static bool OnSegment(XYZ a, XYZ b, XYZ p)
        { return Math.Abs(Cross(a, b, p)) <= Tolerance && p.X >= Math.Min(a.X, b.X) - Tolerance && p.X <= Math.Max(a.X, b.X) + Tolerance && p.Y >= Math.Min(a.Y, b.Y) - Tolerance && p.Y <= Math.Max(a.Y, b.Y) + Tolerance; }

        private static bool SegmentsIntersect(XYZ a, XYZ b, XYZ c, XYZ d)
        {
            double c1 = Cross(a, b, c), c2 = Cross(a, b, d), c3 = Cross(c, d, a), c4 = Cross(c, d, b);
            if (((c1 > Tolerance && c2 < -Tolerance) || (c1 < -Tolerance && c2 > Tolerance)) &&
                ((c3 > Tolerance && c4 < -Tolerance) || (c3 < -Tolerance && c4 > Tolerance))) return true;
            return OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b);
        }
    }
}
