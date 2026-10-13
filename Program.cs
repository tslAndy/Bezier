using System.Numerics;
using Raylib_cs;

class Program
{
    public static void Main(string[] args)
    {
        Editor editor = new Editor();
        UI ui = new UI(editor);

        Raylib.InitWindow(1200, 800, "Bezier");
        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            ui.HandleTools();
            ui.Draw();

            Raylib.EndDrawing();
        }
        Raylib.CloseWindow();
    }
}

enum PointMode
{
    Broken, // red color
    Aligned, // yellow color
    Symmetric, // green color
    Control, // control point
}

record struct Point(Vector2 pos, PointMode mode);

// render curve
// render tools
class Editor
{
    public readonly List<Point> points = new List<Point>();

    public void AddPoint(Vector2 pos)
    {
        if (points.Count == 0)
        {
            points.Add(new Point(pos, PointMode.Broken));
            return;
        }

        Vector2 controlA,
            controlB;

        // if last point is broken
        // then control points are on 1/3 and 2/3 between new point and last one
        if (points.Count < 4 || points[^1].mode == PointMode.Broken)
        {
            Vector2 delta = pos - points[^1].pos;
            controlA = points[^1].pos + 1.0f / 3.0f * delta;
            controlB = points[^1].pos + 2.0f / 3.0f * delta;
        }
        else
        {
            Vector2 norm = Vector2.Normalize(pos - points[^1].pos);
            Vector2 delta = points[^1].pos - points[^2].pos;

            // by default assume that last point is symmetric
            // if not, it's aligned, then scale by 1/3 of distance between last point and new one
            if (points[^1].mode == PointMode.Aligned)
                delta = Vector2.Normalize(delta) * (pos - points[^1].pos).Length() * 1.0f / 3.0f;

            controlA = points[^1].pos + delta;
            controlB = pos + Vector2.Reflect(delta, norm);
        }

        points.Add(new Point(controlA, PointMode.Control));
        points.Add(new Point(controlB, PointMode.Control));
        points.Add(new Point(pos, PointMode.Broken));
    }

    public void RemovePoint(int index)
    {
        // can't remove control point
        if (index % 3 != 0)
            return;

        int start,
            length;

        if (index == 0)
        {
            start = index;
            length = Math.Min(points.Count, 3);
        }
        else if (index == points.Count - 1)
        {
            start = index - 2;
            length = 3;
        }
        else
        {
            start = index - 1;
            length = 3;
        }

        points.RemoveRange(start, length);

        if (points.Count == 0)
            return;

        // fist and last point should be broken
        if (points[0].mode != PointMode.Broken)
            points[0] = points[0] with { mode = PointMode.Broken };
        if (points[^1].mode != PointMode.Broken)
            points[^1] = points[^1] with { mode = PointMode.Broken };
    }

    public void ChangePointPos(int index, Vector2 pos)
    {
        // control points are moved with main point
        if (index % 3 == 0)
        {
            // prev control point
            if (index > 0)
            {
                Vector2 controlPos = pos + points[index - 1].pos - points[index].pos;
                points[index - 1] = points[index - 1] with { pos = controlPos };
            }

            // next control point
            if (index < points.Count - 1)
            {
                Vector2 controlPos = pos + points[index + 1].pos - points[index].pos;
                points[index + 1] = points[index + 1] with { pos = controlPos };
            }

            points[index] = points[index] with { pos = pos };
            return;
        }

        Point mainPnt = points[index % 3 == 1 ? (index - 1) : (index + 1)];
        if (index % 3 == 1 && index - 2 > 0 && mainPnt.mode != PointMode.Broken)
        {
            Point ctrlPoint = points[index - 2];
            float length =
                mainPnt.mode == PointMode.Aligned
                    ? (mainPnt.pos - ctrlPoint.pos).Length()
                    : (mainPnt.pos - pos).Length(); // Symmetric

            Vector2 controlPos = mainPnt.pos - Vector2.Normalize(pos - mainPnt.pos) * length;
            points[index - 2] = ctrlPoint with { pos = controlPos };
        }
        else if (index % 3 == 2 && index + 2 < points.Count && mainPnt.mode != PointMode.Broken)
        {
            Point ctrlPoint = points[index + 2];
            float length =
                mainPnt.mode == PointMode.Aligned
                    ? (mainPnt.pos - ctrlPoint.pos).Length()
                    : (mainPnt.pos - pos).Length(); // Symmetric

            Vector2 controlPos = mainPnt.pos - Vector2.Normalize(pos - mainPnt.pos) * length;
            points[index + 2] = ctrlPoint with { pos = controlPos };
        }

        points[index] = points[index] with { pos = pos };
    }

    public void ChangePointMode(int index)
    {
        // don't touch control points
        if (index % 3 != 0)
            return;

        if (index == 0 || index == points.Count - 1)
            return;

        Point point = points[index];
        point.mode = point.mode switch
        {
            PointMode.Broken => PointMode.Aligned,
            PointMode.Aligned => PointMode.Symmetric,
            PointMode.Symmetric => PointMode.Broken,
            _ => throw new Exception("Wrong point mode"),
        };

        // prev control point isn't changed, only next one
        // same idea as in AddPoint, by default point assumed to be symmetric
        // if not, scaled by 1/3 distance between two main points
        Vector2 delta = points[index].pos - points[index - 1].pos;
        if (point.mode == PointMode.Aligned)
            delta =
                Vector2.Normalize(delta)
                * (points[index + 3].pos - points[index].pos).Length()
                * 1.0f
                / 3.0f;

        Point controlPoint = points[index + 1];
        controlPoint.pos = point.pos + delta;

        points[index] = point;
        points[index + 1] = controlPoint;
    }
}

class UI
{
    private readonly Color BrokenColor = Color.Red;
    private readonly Color AlignedColor = Color.Yellow;
    private readonly Color SymmetricColor = Color.Green;
    private readonly Color ControlColor = Color.Magenta;
    private readonly Color CurveColor = Color.Blue;
    private readonly Color DetectColor = Color.White;

    private const float CONTROL_POINT_RADIUS = 5.0f;
    private const float MAIN_POINT_RADIUS = 10.0f;
    private const float DETECT_RADIUS = 20.0f;
    private const float LINE_THICK = 2.0f;

    private const int CURVE_SPLITS = 10;
    private const float SPLIT_T = 1.0f / CURVE_SPLITS;
    private const int MAX_DEPTH = 10;
    private const float THRESHOLD = 2.0f;

    private Editor _editor;
    private List<Point> points => _editor.points;

    private Stack<(Segment seg, int depth)> stack = new Stack<(Segment seg, int depth)>();

    private int selectedIndex = -1;

    public UI(Editor editor)
    {
        _editor = editor;
    }

    public void HandleTools()
    {
        Vector2 mousePos = Raylib.GetMousePosition();

        if (selectedIndex >= 0)
        {
            Raylib.DrawCircleLinesV(points[selectedIndex].pos, DETECT_RADIUS, DetectColor);
            _editor.ChangePointPos(selectedIndex, mousePos);
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                selectedIndex = -1;
            return;
        }

        if (!TryDetectPoint(mousePos, out int index))
        {
            Raylib.DrawCircleLinesV(mousePos, DETECT_RADIUS, DetectColor);
            // add point on left click
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                _editor.AddPoint(mousePos);
            return;
        }

        // if detected point
        Raylib.DrawCircleLinesV(points[index].pos, DETECT_RADIUS, DetectColor);

        // select on right click
        if (Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            selectedIndex = index;
        }
        // remove on right click
        else if (Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            _editor.RemovePoint(index);
        }
        // change mode on middle click
        else if (Raylib.IsMouseButtonPressed(MouseButton.Middle))
        {
            _editor.ChangePointMode(index);
        }
    }

    public void Draw()
    {
        for (int i = 0; i < points.Count; i++)
        {
            Point point = points[i];
            Color color = point.mode switch
            {
                PointMode.Broken => BrokenColor,
                PointMode.Aligned => AlignedColor,
                PointMode.Symmetric => SymmetricColor,
                PointMode.Control => ControlColor,
                _ => throw new Exception("Unknown point mode"),
            };

            float radius = point.mode switch
            {
                PointMode.Control => CONTROL_POINT_RADIUS,
                _ => MAIN_POINT_RADIUS,
            };

            Raylib.DrawCircleV(point.pos, radius, color);
        }

        for (int i = 0; i < points.Count - 3; i += 3)
        {
            Vector2 p1 = points[i].pos;
            Vector2 p2 = points[i + 1].pos;
            Vector2 p3 = points[i + 2].pos;
            Vector2 p4 = points[i + 3].pos;

            Segment seg = new Segment(p1, p2, p3, p4);

            // Split curve in several parts for reducing recursion
            for (int k = 0; k < CURVE_SPLITS; k++)
            {
                float t1 = SPLIT_T * k;
                float t2 = t1 + SPLIT_T;
                DrawSegment(Bezier.Split(seg, t1, t2));
            }
        }

        for (int i = 1; i < points.Count; i += 3)
        {
            Raylib.DrawLineV(points[i - 1].pos, points[i].pos, ControlColor);
            Raylib.DrawLineV(points[i + 1].pos, points[i + 2].pos, ControlColor);
        }
    }

    private void DrawSegment(Segment seg)
    {
        stack.Push((seg, 0));

        while (stack.Count > 0)
        {
            (seg, int depth) = stack.Pop();

            float d1 = (seg.p2 - Vector2.Lerp(seg.p1, seg.p4, 1.0f / 3.0f)).LengthSquared();
            float d2 = (seg.p3 - Vector2.Lerp(seg.p1, seg.p4, 2.0f / 3.0f)).LengthSquared();
            if ((d1 < THRESHOLD * THRESHOLD && d2 < THRESHOLD * THRESHOLD) || depth == MAX_DEPTH)
            {
                Raylib.DrawLineEx(seg.p1, seg.p4, LINE_THICK, CurveColor);
                continue;
            }

            stack.Push((Bezier.Split(seg, 0.0f, 0.5f), depth + 1));
            stack.Push((Bezier.Split(seg, 0.5f, 1.0f), depth + 1));
        }
    }

    private bool TryDetectPoint(Vector2 pos, out int index)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (Vector2.DistanceSquared(pos, points[i].pos) > DETECT_RADIUS * DETECT_RADIUS)
                continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
    }
}

record struct Segment(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4);

static class Bezier
{
    public static Vector2 BezierF(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)
    {
        return t * t * t * (p4 - p1 + 3.0f * (p2 - p3))
            + 3.0f * t * t * (p1 + p3 - 2.0f * p2)
            + 3.0f * t * (p2 - p1)
            + p1;
    }

    public static Vector2 BezierDF(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)
    {
        return 3.0f * t * t * (p4 - p1 + 3.0f * (p2 - p3))
            + 6.0f * t * (p1 + p3 - 2.0f * p2)
            + 3.0f * (p2 - p1);
    }

    public static Segment Split(Segment seg, float t1, float t2)
    {
        Vector2 q1 = BezierF(seg.p1, seg.p2, seg.p3, seg.p4, t1);
        Vector2 q4 = BezierF(seg.p1, seg.p2, seg.p3, seg.p4, t2);
        Vector2 q2 = q1 + (t2 - t1) / 3.0f * BezierDF(seg.p1, seg.p2, seg.p3, seg.p4, t1);
        Vector2 q3 = q4 - (t2 - t1) / 3.0f * BezierDF(seg.p1, seg.p2, seg.p3, seg.p4, t2);
        return new Segment(q1, q2, q3, q4);
    }
}
