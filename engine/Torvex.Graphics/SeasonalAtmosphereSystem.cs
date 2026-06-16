using System;
using System.Numerics;

namespace Torvex.Graphics;

public enum TorvexSeason
{
    Spring,
    Summer,
    Autumn,
    Winter
}

public readonly record struct SeasonalAtmosphereSnapshot(
    TorvexSeason Season,
    float SeasonDay,
    float TemperatureC,
    float CloudCover,
    float Humidity,
    float FogDensity,
    float PrecipitationIntensity,
    float WindStrength,
    Vector2 WindDirection
);

public sealed class SeasonalAtmosphereSystem
{
    private enum SeasonalWeatherEvent
    {
        Clear,
        Cloudy,
        Fog,
        Rain,
        Snow,
        Storm
    }

    private const float DayLengthSeconds = 180.0f;
    private const float SeasonLengthDays = 16.0f;

    private readonly Random _random = new(82941);

    private SeasonalWeatherEvent _currentEvent = SeasonalWeatherEvent.Clear;
    private float _eventRemainingDays;
    private float _eventAgeDays;
    private int _eventSerial;

    public TorvexSeason CurrentSeason { get; private set; } = TorvexSeason.Autumn;
    public float SeasonDay { get; private set; }

    public SeasonalAtmosphereSnapshot Update(float deltaTime, float timeOfDay, float timeScale)
    {
        float deltaDays = MathF.Max(0.0f, deltaTime / DayLengthSeconds * timeScale);

        SeasonDay += deltaDays;

        while (SeasonDay >= SeasonLengthDays)
        {
            SeasonDay -= SeasonLengthDays;
            CurrentSeason = NextSeason(CurrentSeason);
            _eventRemainingDays = 0.0f;
            _eventAgeDays = 0.0f;

            Console.WriteLine($"Season changed: {CurrentSeason}");
        }

        float seasonProgress = SeasonDay / SeasonLengthDays;
        SeasonClimateProfile profile = GetSeasonProfile(CurrentSeason, seasonProgress);

        float daylightWarmth = MathF.Max(0.0f, MathF.Sin((timeOfDay - 0.25f) * MathF.Tau));
        float dailyTemperatureSwing = Lerp(-3.5f, 5.0f, daylightWarmth);

        float baseTemperature = profile.TemperatureC + dailyTemperatureSwing;

        UpdateWeatherEvent(deltaDays, baseTemperature);

        EventModifier modifier = GetEventModifier(_currentEvent);
        float eventFade = GetEventFade();

        float temperature = baseTemperature + modifier.TemperatureDeltaC * eventFade;

        float cloudCover = Math.Clamp(profile.CloudCover + modifier.CloudCover * eventFade, 0.0f, 1.0f);
        float humidity = Math.Clamp(profile.Humidity + modifier.Humidity * eventFade, 0.0f, 1.0f);
        float windStrength = Math.Clamp(profile.WindStrength + modifier.WindStrength * eventFade, 0.0f, 1.35f);

        float lowWindFogBonus = SmoothStep(0.0f, 0.35f, 0.35f - windStrength) * humidity * 0.16f;
        float morningFogBonus = SmoothStep(0.18f, 0.32f, timeOfDay) * SmoothStep(0.55f, 0.85f, humidity) * 0.16f;

        float fogDensity = Math.Clamp(
            profile.FogDensity + modifier.FogDensity * eventFade + lowWindFogBonus + morningFogBonus,
            0.0f,
            1.0f
        );

        // Precipitation only exists during weather events.
        // Seasons affect the chance of events, not constant daily rain/snow.
        float precipitationIntensity = Math.Clamp(modifier.PrecipitationIntensity * eventFade, 0.0f, 1.0f);

        Vector2 windDirection = GetWindDirection(profile.WindAngleBase);

        return new SeasonalAtmosphereSnapshot(
            CurrentSeason,
            SeasonDay,
            temperature,
            cloudCover,
            humidity,
            fogDensity,
            precipitationIntensity,
            windStrength,
            windDirection
        );
    }

    private void UpdateWeatherEvent(float deltaDays, float currentTemperatureC)
    {
        _eventRemainingDays -= deltaDays;
        _eventAgeDays += deltaDays;

        if (_eventRemainingDays > 0.0f)
        {
            return;
        }

        _currentEvent = PickNextWeatherEvent(CurrentSeason, currentTemperatureC);
        _eventRemainingDays = GetEventDurationDays(_currentEvent);
        _eventAgeDays = 0.0f;
        _eventSerial++;

        Console.WriteLine($"Weather event: {_currentEvent} for {_eventRemainingDays:0.00} in-game days | {CurrentSeason} day {SeasonDay:0.0}");
    }

    private SeasonalWeatherEvent PickNextWeatherEvent(TorvexSeason season, float temperatureC)
    {
        return season switch
        {
            TorvexSeason.Spring => temperatureC < 1.0f
                ? PickWeighted([
                    (SeasonalWeatherEvent.Clear, 34),
                    (SeasonalWeatherEvent.Cloudy, 28),
                    (SeasonalWeatherEvent.Fog, 14),
                    (SeasonalWeatherEvent.Rain, 16),
                    (SeasonalWeatherEvent.Snow, 5),
                    (SeasonalWeatherEvent.Storm, 3)
                ])
                : PickWeighted([
                    (SeasonalWeatherEvent.Clear, 34),
                    (SeasonalWeatherEvent.Cloudy, 30),
                    (SeasonalWeatherEvent.Fog, 14),
                    (SeasonalWeatherEvent.Rain, 18),
                    (SeasonalWeatherEvent.Storm, 4)
                ]),

            TorvexSeason.Summer => PickWeighted([
                (SeasonalWeatherEvent.Clear, 58),
                (SeasonalWeatherEvent.Cloudy, 22),
                (SeasonalWeatherEvent.Fog, 4),
                (SeasonalWeatherEvent.Rain, 7),
                (SeasonalWeatherEvent.Storm, 9)
            ]),

            TorvexSeason.Autumn => temperatureC < 1.0f
                ? PickWeighted([
                    (SeasonalWeatherEvent.Clear, 28),
                    (SeasonalWeatherEvent.Cloudy, 30),
                    (SeasonalWeatherEvent.Fog, 18),
                    (SeasonalWeatherEvent.Rain, 14),
                    (SeasonalWeatherEvent.Snow, 6),
                    (SeasonalWeatherEvent.Storm, 4)
                ])
                : PickWeighted([
                    (SeasonalWeatherEvent.Clear, 28),
                    (SeasonalWeatherEvent.Cloudy, 32),
                    (SeasonalWeatherEvent.Fog, 18),
                    (SeasonalWeatherEvent.Rain, 18),
                    (SeasonalWeatherEvent.Storm, 4)
                ]),

            TorvexSeason.Winter => temperatureC <= 0.0f
                ? PickWeighted([
                    (SeasonalWeatherEvent.Clear, 32),
                    (SeasonalWeatherEvent.Cloudy, 29),
                    (SeasonalWeatherEvent.Fog, 10),
                    (SeasonalWeatherEvent.Snow, 25),
                    (SeasonalWeatherEvent.Storm, 4)
                ])
                : PickWeighted([
                    (SeasonalWeatherEvent.Clear, 34),
                    (SeasonalWeatherEvent.Cloudy, 30),
                    (SeasonalWeatherEvent.Fog, 10),
                    (SeasonalWeatherEvent.Rain, 12),
                    (SeasonalWeatherEvent.Snow, 10),
                    (SeasonalWeatherEvent.Storm, 4)
                ]),

            _ => SeasonalWeatherEvent.Clear
        };
    }

    private SeasonalWeatherEvent PickWeighted((SeasonalWeatherEvent Event, int Weight)[] options)
    {
        int totalWeight = 0;

        foreach ((_, int weight) in options)
        {
            totalWeight += weight;
        }

        int roll = _random.Next(totalWeight);
        int running = 0;

        foreach ((SeasonalWeatherEvent weatherEvent, int weight) in options)
        {
            running += weight;

            if (roll < running)
            {
                return weatherEvent;
            }
        }

        return SeasonalWeatherEvent.Clear;
    }

    private float GetEventDurationDays(SeasonalWeatherEvent weatherEvent)
    {
        return weatherEvent switch
        {
            SeasonalWeatherEvent.Clear => RandomRange(0.85f, 2.60f),
            SeasonalWeatherEvent.Cloudy => RandomRange(0.65f, 1.80f),
            SeasonalWeatherEvent.Fog => RandomRange(0.18f, 0.70f),
            SeasonalWeatherEvent.Rain => RandomRange(0.20f, 0.85f),
            SeasonalWeatherEvent.Snow => RandomRange(0.35f, 1.10f),
            SeasonalWeatherEvent.Storm => RandomRange(0.12f, 0.42f),
            _ => RandomRange(0.65f, 1.25f)
        };
    }

    private float GetEventFade()
    {
        float fadeIn = SmoothStep(0.0f, 0.08f, _eventAgeDays);
        float fadeOut = SmoothStep(0.0f, 0.08f, _eventRemainingDays);

        return Math.Clamp(fadeIn * fadeOut, 0.0f, 1.0f);
    }

    private EventModifier GetEventModifier(SeasonalWeatherEvent weatherEvent)
    {
        return weatherEvent switch
        {
            SeasonalWeatherEvent.Clear => new EventModifier(
                TemperatureDeltaC: 0.8f,
                CloudCover: -0.22f,
                Humidity: -0.14f,
                FogDensity: -0.10f,
                PrecipitationIntensity: 0.0f,
                WindStrength: -0.06f
            ),

            SeasonalWeatherEvent.Cloudy => new EventModifier(
                TemperatureDeltaC: -0.4f,
                CloudCover: 0.24f,
                Humidity: 0.08f,
                FogDensity: 0.04f,
                PrecipitationIntensity: 0.0f,
                WindStrength: 0.04f
            ),

            SeasonalWeatherEvent.Fog => new EventModifier(
                TemperatureDeltaC: -0.8f,
                CloudCover: 0.08f,
                Humidity: 0.32f,
                FogDensity: 0.62f,
                PrecipitationIntensity: 0.0f,
                WindStrength: -0.18f
            ),

            SeasonalWeatherEvent.Rain => new EventModifier(
                TemperatureDeltaC: -1.2f,
                CloudCover: 0.44f,
                Humidity: 0.34f,
                FogDensity: 0.18f,
                PrecipitationIntensity: 0.58f,
                WindStrength: 0.18f
            ),

            SeasonalWeatherEvent.Snow => new EventModifier(
                TemperatureDeltaC: -1.8f,
                CloudCover: 0.42f,
                Humidity: 0.28f,
                FogDensity: 0.14f,
                PrecipitationIntensity: 0.62f,
                WindStrength: 0.12f
            ),

            SeasonalWeatherEvent.Storm => new EventModifier(
                TemperatureDeltaC: -2.4f,
                CloudCover: 0.62f,
                Humidity: 0.38f,
                FogDensity: 0.26f,
                PrecipitationIntensity: 0.95f,
                WindStrength: 0.68f
            ),

            _ => default
        };
    }

    private SeasonClimateProfile GetSeasonProfile(TorvexSeason season, float progress)
    {
        return season switch
        {
            TorvexSeason.Spring => new SeasonClimateProfile(
                TemperatureC: Lerp(3.0f, 14.0f, progress),
                CloudCover: 0.38f,
                Humidity: 0.60f,
                FogDensity: 0.16f,
                WindStrength: 0.34f,
                WindAngleBase: 0.8f
            ),

            TorvexSeason.Summer => new SeasonClimateProfile(
                TemperatureC: 22.0f + MathF.Sin(progress * MathF.Tau) * 2.5f,
                CloudCover: 0.18f,
                Humidity: 0.38f,
                FogDensity: 0.04f,
                WindStrength: 0.22f,
                WindAngleBase: 1.7f
            ),

            TorvexSeason.Autumn => new SeasonClimateProfile(
                TemperatureC: Lerp(14.0f, 1.0f, progress),
                CloudCover: 0.44f,
                Humidity: 0.66f,
                FogDensity: 0.24f,
                WindStrength: 0.46f,
                WindAngleBase: 2.9f
            ),

            TorvexSeason.Winter => new SeasonClimateProfile(
                TemperatureC: -6.0f + MathF.Sin(progress * MathF.Tau) * 2.0f,
                CloudCover: 0.36f,
                Humidity: 0.54f,
                FogDensity: 0.12f,
                WindStrength: 0.38f,
                WindAngleBase: 4.1f
            ),

            _ => default
        };
    }

    private Vector2 GetWindDirection(float baseAngle)
    {
        float slowShift = MathF.Sin(SeasonDay * 0.31f + _eventSerial * 0.73f) * 0.45f;
        float angle = baseAngle + slowShift;

        return Vector2.Normalize(new Vector2(
            MathF.Cos(angle),
            MathF.Sin(angle)
        ));
    }

    private static TorvexSeason NextSeason(TorvexSeason season)
    {
        return season switch
        {
            TorvexSeason.Spring => TorvexSeason.Summer,
            TorvexSeason.Summer => TorvexSeason.Autumn,
            TorvexSeason.Autumn => TorvexSeason.Winter,
            TorvexSeason.Winter => TorvexSeason.Spring,
            _ => TorvexSeason.Spring
        };
    }

    private float RandomRange(float min, float max)
    {
        return min + (float)_random.NextDouble() * (max - min);
    }

    private static float Lerp(float a, float b, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return a + (b - a) * t;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private readonly record struct SeasonClimateProfile(
        float TemperatureC,
        float CloudCover,
        float Humidity,
        float FogDensity,
        float WindStrength,
        float WindAngleBase
    );

    private readonly record struct EventModifier(
        float TemperatureDeltaC,
        float CloudCover,
        float Humidity,
        float FogDensity,
        float PrecipitationIntensity,
        float WindStrength
    );
}
