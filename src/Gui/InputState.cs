using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// Per-frame input snapshot over FNA Keyboard/Mouse: edge detection,
/// key-repeat (initial 400ms, repeat 40ms), mouse buttons/wheel/double-click, and
/// character input via TextInputEXT (never map keycodes to chars by hand).
/// </summary>
public sealed class InputState
{
    private const double InitialRepeatMs = 400;
    private const double RepeatMs = 40;
    private const double DoubleClickMs = 400;
    private const int DoubleClickDist = 4;

    private KeyboardState _kb, _prevKb;
    private MouseState _mouse, _prevMouse;

    private readonly Dictionary<Keys, double> _heldMs = new();
    private readonly Dictionary<Keys, double> _nextRepeat = new();
    private readonly HashSet<Keys> _pressed = new();

    private readonly List<char> _typed = new();
    private readonly List<char> _pendingTyped = new();
    private readonly object _typedLock = new();

    private double _totalMs;
    private double _lastLeftDownMs = double.NegativeInfinity;
    private Point _lastLeftDownPos;
    private int _wheelRemainder; // sub-notch wheel movement carried to the next frame

    public Point Mouse { get; private set; }
    public Point PrevMouse { get; private set; }
    public int WheelDelta { get; private set; }
    public bool DoubleClicked { get; private set; }

    public InputState()
    {
        // TextInputEXT fires on the game thread during the message pump; buffer it.
        TextInputEXT.TextInput += OnTextInput;
    }

    private void OnTextInput(char c)
    {
        // Filter control chars except tab; Enter/Backspace handled as key events.
        if (c == '\t' || !char.IsControl(c))
        {
            lock (_typedLock) _pendingTyped.Add(c);
        }
    }

    public void Update(GameTime gt, bool windowActive)
    {
        double dt = gt.ElapsedGameTime.TotalMilliseconds;
        _totalMs += dt;

        _prevKb = _kb;
        _prevMouse = _mouse;

        if (windowActive)
        {
            _kb = Keyboard.GetState();
            _mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
        }
        else
        {
            // Not the focused window: drop live input so a click meant for a window IN FRONT of us is
            // not taken by our widgets. SDL reports mouse buttons globally, so without this a click on
            // another window lands on whatever of ours sits under the cursor. Present an all-released
            // snapshot with the pointer frozen, and set prev == current so regaining focus cannot
            // synthesize a stray press or release edge.
            _kb = default;
            _mouse = new MouseState(_prevMouse.X, _prevMouse.Y, _prevMouse.ScrollWheelValue,
                ButtonState.Released, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released);
            _prevKb = _kb;
            _prevMouse = _mouse;
        }

        PrevMouse = new Point(_prevMouse.X, _prevMouse.Y);
        Mouse = new Point(_mouse.X, _mouse.Y);
        // Carry sub-notch wheel movement instead of truncating it away, so a precision touchpad or
        // free-spin wheel that reports deltas under one 120-unit notch still scrolls.
        int wheel = _wheelRemainder + (_mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue);
        WheelDelta = wheel / 120;
        _wheelRemainder = wheel - WheelDelta * 120;

        // Key-repeat bookkeeping.
        _pressed.Clear();
        var down = _kb.GetPressedKeys();
        var stillDown = new HashSet<Keys>(down);
        foreach (var k in down)
        {
            if (!_heldMs.ContainsKey(k))
            {
                _heldMs[k] = 0;
                _nextRepeat[k] = InitialRepeatMs;
                _pressed.Add(k); // initial press
            }
            else
            {
                _heldMs[k] += dt;
                if (_heldMs[k] >= _nextRepeat[k])
                {
                    _pressed.Add(k);
                    _nextRepeat[k] += RepeatMs;
                }
            }
        }
        // Drop released keys.
        var toRemove = new List<Keys>();
        foreach (var k in _heldMs.Keys)
            if (!stillDown.Contains(k)) toRemove.Add(k);
        foreach (var k in toRemove) { _heldMs.Remove(k); _nextRepeat.Remove(k); }

        // Double-click detection on left button.
        DoubleClicked = false;
        if (LeftPressed)
        {
            double since = _totalMs - _lastLeftDownMs;
            int dx = Mouse.X - _lastLeftDownPos.X, dy = Mouse.Y - _lastLeftDownPos.Y;
            if (since <= DoubleClickMs && dx * dx + dy * dy <= DoubleClickDist * DoubleClickDist)
            {
                DoubleClicked = true;
                _lastLeftDownMs = double.NegativeInfinity; // consume; triple-click won't re-fire
            }
            else
            {
                _lastLeftDownMs = _totalMs;
                _lastLeftDownPos = Mouse;
            }
        }

        // Swap typed-char buffers.
        _typed.Clear();
        lock (_typedLock)
        {
            _typed.AddRange(_pendingTyped);
            _pendingTyped.Clear();
        }
    }

    // -- Keyboard ----------------------------------------------------------
    /// <summary>True on initial press and on each auto-repeat tick.</summary>
    public bool Pressed(Keys k) => _pressed.Contains(k);
    public bool Down(Keys k) => _kb.IsKeyDown(k);
    public bool Released(Keys k) => _prevKb.IsKeyDown(k) && _kb.IsKeyUp(k);

    public bool Ctrl => _kb.IsKeyDown(Keys.LeftControl) || _kb.IsKeyDown(Keys.RightControl);
    public bool Shift => _kb.IsKeyDown(Keys.LeftShift) || _kb.IsKeyDown(Keys.RightShift);
    public bool Alt => _kb.IsKeyDown(Keys.LeftAlt) || _kb.IsKeyDown(Keys.RightAlt);

    public IReadOnlyList<char> TypedChars => _typed;

    // -- Mouse --------------------------------------------------------------
    public bool LeftDown => _mouse.LeftButton == ButtonState.Pressed;
    public bool LeftPressed => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public bool LeftReleased => _mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
    public bool RightDown => _mouse.RightButton == ButtonState.Pressed;
    public bool RightPressed => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
    public bool RightReleased => _mouse.RightButton == ButtonState.Released && _prevMouse.RightButton == ButtonState.Pressed;

    public bool MiddleDown => _mouse.MiddleButton == ButtonState.Pressed;
    public bool MiddlePressed => _mouse.MiddleButton == ButtonState.Pressed && _prevMouse.MiddleButton == ButtonState.Released;
    public bool MiddleReleased => _mouse.MiddleButton == ButtonState.Released && _prevMouse.MiddleButton == ButtonState.Pressed;

    /// <summary>True if anything happened this frame (mouse moved/held, wheel, key, or text).
    /// Used to keep the render loop awake; when false and nothing requests a redraw, drawing idles.</summary>
    public bool AnyActivity =>
        Mouse != PrevMouse
        || _mouse.LeftButton == ButtonState.Pressed
        || _mouse.RightButton == ButtonState.Pressed
        || _mouse.MiddleButton == ButtonState.Pressed
        || WheelDelta != 0
        || _typed.Count > 0
        || _kb.GetPressedKeys().Length > 0;
}
