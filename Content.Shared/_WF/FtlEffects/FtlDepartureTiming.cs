namespace Content.Shared._WF.FtlEffects;

/// <summary>Shapes a short build-up and departure flash on the authoritative jump clock.</summary>
public static class FtlDepartureTiming
{
    /// <summary>Maximum number of seconds of visible spool-up.</summary>
    public const float BuildDuration = 4f;

    /// <summary>Seconds the ripple takes to fade after entering hyperspace.</summary>
    public const float FadeDuration = 0.65f;

    /// <summary>Seconds occupied by the visible launch or arrival rush.</summary>
    public const float MotionDuration = 0.24f;

    /// <summary>Signed visual travel fraction; arrivals brake sharply and departures accelerate.</summary>
    public static float Motion(TimeSpan now, TimeSpan departure, bool entered, bool arriving)
    {
        var seconds = (float) (now - departure).TotalSeconds;
        if (arriving)
        {
            if (seconds < 0f || seconds >= MotionDuration)
                return 0f;
            var remaining = 1f - seconds / MotionDuration;
            return -remaining * remaining * remaining;
        }

        if (entered || seconds < -MotionDuration || seconds > 0f)
            return 0f;
        var progress = 1f + seconds / MotionDuration;
        return progress * progress * progress;
    }

    /// <summary>Returns an intensity from zero to one, including short and instant departures.</summary>
    public static float Intensity(TimeSpan now, TimeSpan started, TimeSpan departure, bool entered)
    {
        if (entered)
        {
            var fade = Math.Clamp((float) (now - departure).TotalSeconds / FadeDuration, 0f, 1f);
            return 1f - fade * fade * (3f - 2f * fade);
        }

        var duration = Math.Clamp((float) (departure - started).TotalSeconds, 0.001f, BuildDuration);
        var progress = Math.Clamp(1f - (float) (departure - now).TotalSeconds / duration, 0f, 1f);
        return progress * progress * (3f - 2f * progress);
    }
}
