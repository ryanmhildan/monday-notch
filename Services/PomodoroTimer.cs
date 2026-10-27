using System;
using System.Windows.Threading;

namespace MondayNotch.Services;

public enum PomodoroMode
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>
/// Classic Pomodoro: 25 minutes focus, 5 minutes break, and a 15 minute break after every fourth focus.
/// Runs on the UI thread. A finished phase moves to the next one but waits for the user to start it.
/// </summary>
public sealed class PomodoroTimer
{
    public const int SessionsPerCycle = 4;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _endsAt;

    public PomodoroTimer()
    {
        _timer.Tick += (_, _) => Tick();
        Remaining = LengthOf(Mode);
    }

    public event Action? Changed;
    public event Action<PomodoroMode>? Finished;

    public PomodoroMode Mode { get; private set; } = PomodoroMode.Focus;
    public TimeSpan Remaining { get; private set; }
    public bool IsRunning { get; private set; }
    public int CompletedFocusSessions { get; private set; }

    public double Progress => Math.Clamp(1 - Remaining / LengthOf(Mode), 0, 1);

    public static TimeSpan LengthOf(PomodoroMode mode) => mode switch
    {
        PomodoroMode.Focus => TimeSpan.FromMinutes(25),
        PomodoroMode.ShortBreak => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(15),
    };

    /// <summary>mm:ss, rounded up so a fresh timer reads 25:00 and only the very end reads 00:00.</summary>
    public static string Format(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    public void Start()
    {
        if (IsRunning)
            return;
        _endsAt = DateTime.Now + Remaining;
        IsRunning = true;
        _timer.Start();
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!IsRunning)
            return;
        Remaining = _endsAt - DateTime.Now;
        IsRunning = false;
        _timer.Stop();
        Changed?.Invoke();
    }

    public void Reset()
    {
        _timer.Stop();
        IsRunning = false;
        Remaining = LengthOf(Mode);
        Changed?.Invoke();
    }

    public void Skip()
    {
        _timer.Stop();
        IsRunning = false;
        Advance();
        Changed?.Invoke();
    }

    private void Tick()
    {
        Remaining = _endsAt - DateTime.Now;
        if (Remaining > TimeSpan.Zero)
        {
            Changed?.Invoke();
            return;
        }

        PomodoroMode finished = Mode;
        if (finished == PomodoroMode.Focus)
            CompletedFocusSessions++;

        _timer.Stop();
        IsRunning = false;
        Advance();
        Changed?.Invoke();
        Finished?.Invoke(finished);
    }

    private void Advance()
    {
        Mode = Mode != PomodoroMode.Focus
            ? PomodoroMode.Focus
            : CompletedFocusSessions > 0 && CompletedFocusSessions % SessionsPerCycle == 0
                ? PomodoroMode.LongBreak
                : PomodoroMode.ShortBreak;
        Remaining = LengthOf(Mode);
    }
}
