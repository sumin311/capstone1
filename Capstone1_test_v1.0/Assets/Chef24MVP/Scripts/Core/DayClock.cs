using System;
using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// In-game time (SFR-008): business runs 08:00 - 19:00. One in-game hour lasts
    /// <see cref="secondsPerHour"/> real seconds. Also fades the sun from day to evening.
    /// </summary>
    public class DayClock : MonoBehaviour
    {
        public float openHour = 8f;
        public float closeHour = 19f;
        [Tooltip("Real seconds per in-game hour. 30 -> one day is 5.5 minutes.")]
        public float secondsPerHour = 30f;

        [Header("Day / night")]
        public Light sun;
        public Gradient sunColor;
        public AnimationCurve sunIntensity = AnimationCurve.EaseInOut(0f, 1.1f, 1f, 0.25f);
        public float noonAngle = 50f;

        public float Hour { get; private set; }
        public bool IsOpen => Hour < closeHour;
        public bool Running { get; private set; }
        /// <summary>0 at opening, 1 at closing.</summary>
        public float DayProgress => Mathf.InverseLerp(openHour, closeHour, Hour);

        public event Action Closed;

        void Awake()
        {
            Hour = openHour;
            if (sunColor == null || sunColor.colorKeys.Length < 2)
            {
                sunColor = new Gradient();
                sunColor.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.92f, 0.8f), 0f),
                        new GradientColorKey(new Color(1f, 1f, 0.97f), 0.4f),
                        new GradientColorKey(new Color(1f, 0.7f, 0.45f), 0.8f),
                        new GradientColorKey(new Color(0.45f, 0.5f, 0.8f), 1f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
            ApplyLighting();
        }

        public void StartDay()
        {
            Hour = openHour;
            Running = true;
            ApplyLighting();
        }

        void Update()
        {
            if (!Running) return;
            bool wasOpen = IsOpen;
            Hour = Mathf.Min(closeHour, Hour + Time.deltaTime / Mathf.Max(0.01f, secondsPerHour));
            ApplyLighting();
            if (wasOpen && !IsOpen)
            {
                Running = false;
                Closed?.Invoke();
            }
        }

        void ApplyLighting()
        {
            float t = DayProgress;
            if (sun != null)
            {
                sun.color = sunColor.Evaluate(t);
                sun.intensity = sunIntensity.Evaluate(t);
                // Sun travels from east-ish to west-ish and gets low in the evening.
                float elevation = Mathf.Lerp(noonAngle, 8f, Mathf.Clamp01((t - 0.3f) / 0.7f));
                sun.transform.rotation = Quaternion.Euler(elevation, Mathf.Lerp(-60f, 120f, t), 0f);
            }
            RenderSettings.ambientIntensity = Mathf.Lerp(1f, 0.45f, t * t);
        }

        /// <summary>"오전 9:30" style text.</summary>
        public string TimeText()
        {
            int h = Mathf.FloorToInt(Hour);
            int m = Mathf.FloorToInt((Hour - h) * 60f / 10f) * 10;
            string ampm = h < 12 ? "오전" : "오후";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return ampm + " " + h12 + ":" + m.ToString("00");
        }

        public bool IsLunchTime => Hour >= 11.5f && Hour < 13.5f;
    }
}
