namespace SngTrans;

/// <summary>Nhận ra thao tác bôi đen từ chuỗi nhấn/nhả chuột: kéo hoặc nhấp đúp/ba.</summary>
public sealed class ClickDetector
{
    readonly double _dragPx, _multiMs, _multiPx;
    double _px, _py;
    (double X, double Y, double T)? _last;
    int _clicks;

    public ClickDetector(double dragPx = 8, double multiMs = 450, double multiPx = 6)
    {
        _dragPx = dragPx; _multiMs = multiMs; _multiPx = multiPx;
    }

    public void Press(double x, double y, double t) { _px = x; _py = y; }

    /// <summary>Trả về "drag", "multi" hoặc null. t tính bằng giây.</summary>
    public string? Release(double x, double y, double t)
    {
        if (Math.Sqrt((x - _px) * (x - _px) + (y - _py) * (y - _py)) >= _dragPx)
        {
            _clicks = 0;
            _last = null;
            return "drag";
        }
        if (_last is { } l && t - l.T <= _multiMs / 1000.0
            && Math.Sqrt((x - l.X) * (x - l.X) + (y - l.Y) * (y - l.Y)) <= _multiPx)
            _clicks++;
        else
            _clicks = 1;
        _last = (x, y, t);
        return _clicks >= 2 ? "multi" : null;
    }
}
