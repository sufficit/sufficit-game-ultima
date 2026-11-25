namespace Ultima.Terrain;

internal sealed class PlayerInput
{
    [Flags]
    private enum Key
    {
        None = 0,
        W = 1 << 0,
        A = 1 << 1,
        S = 1 << 2,
        D = 1 << 3,
        Up = 1 << 4,
        Left = 1 << 5,
        Down = 1 << 6,
        Right = 1 << 7,
        ShiftLeft = 1 << 8,
        ShiftRight = 1 << 9,
        Forward = W | Up,
        Backward = S | Down,
        StrafeLeft = A | Left,
        StrafeRight = D | Right,
        Sprint = ShiftLeft | ShiftRight,
    }

    private Key _keys;
    private (double X, double Y) _touchMove, _touchLook, _padMove, _padLook;

    internal bool SetKey(string code, bool down)
    {
        var key = code switch
        {
            "KeyW" => Key.W, "KeyA" => Key.A, "KeyS" => Key.S, "KeyD" => Key.D,
            "ArrowUp" => Key.Up, "ArrowLeft" => Key.Left, "ArrowDown" => Key.Down, "ArrowRight" => Key.Right,
            "ShiftLeft" => Key.ShiftLeft, "ShiftRight" => Key.ShiftRight,
            _ => Key.None,
        };
        var next = down ? _keys | key : _keys & ~key;
        if (next == _keys) return false;
        _keys = next;
        return true;
    }

    internal bool SetTouch(bool look, double x, double y)
    {
        ref var stick = ref (look ? ref _touchLook : ref _touchMove);
        var next = PlayerMovement.Stick(x, y);
        if (next == stick) return false;
        stick = next;
        return true;
    }

    internal int SetGamepad(double x, double y, double lookX, double lookY, double dpadX, double dpadY)
    {
        var (moveX, moveY) = PlayerMovement.Stick(x, y);
        var move = (moveX + dpadX, -moveY + dpadY);
        var look = PlayerMovement.Stick(lookX, lookY);
        var changed = move != _padMove || look != _padLook;
        _padMove = move;
        _padLook = look;
        var active = move != (0d, 0d) || look != (0d, 0d);
        return (changed ? 1 : 0) | (active ? 2 : 0);
    }

    internal void Read(Span<double> result)
    {
        result[0] = Pressed(Key.StrafeRight) - Pressed(Key.StrafeLeft);
        result[1] = Pressed(Key.Forward) - Pressed(Key.Backward);
        result[2] = _padMove.X + _touchMove.X;
        result[3] = _padMove.Y + _touchMove.Y;
        result[4] = _padLook.X + _touchLook.X;
        result[5] = _padLook.Y + _touchLook.Y;
        result[6] = Pressed(Key.Sprint);
    }

    internal void Clear(bool keepPad = false)
    {
        _keys = Key.None;
        _touchMove = _touchLook = default;
        if (!keepPad) _padMove = _padLook = default;
    }

    private int Pressed(Key key) => (_keys & key) != 0 ? 1 : 0;
}
