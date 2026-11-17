using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// The cooking bar (AP-004 조리 진행 및 판정): a marker sweeps across a bar, the player stops it
    /// with Space / E. Inside the inner zone = Perfect, inside the zone = Good, outside = Bad.
    /// Runs several steps in a row and reports one grade per step. Drawing is done by UIManager.
    /// </summary>
    public class TimingMiniGame : MonoBehaviour
    {
        [Serializable]
        public class Step
        {
            public string title;
            [Range(0.05f, 0.8f)] public float zoneWidth = 0.25f;
            [Tooltip("Bar lengths per second.")]
            public float speed = 1f;
            public float timeLimit = 6f;

            public Step(string title, float zoneWidth, float speed)
            {
                this.title = title;
                this.zoneWidth = zoneWidth;
                this.speed = speed;
            }
        }

        public bool Running { get; private set; }

        Step[] steps;
        int index;
        float marker, dir, zoneCenter, stepTime, inputDelay, resultTimer;
        readonly List<CookGrade> grades = new List<CookGrade>();
        Action<List<CookGrade>> onDone;

        public void Run(Step[] runSteps, Action<List<CookGrade>> done)
        {
            steps = runSteps;
            onDone = done;
            grades.Clear();
            index = -1;
            Running = true;
            NextStep();
        }

        void NextStep()
        {
            index++;
            if (index >= steps.Length)
            {
                Running = false;
                UIManager.Instance?.HideCookBar();
                onDone?.Invoke(new List<CookGrade>(grades));
                return;
            }
            marker = 0f;
            dir = 1f;
            zoneCenter = UnityEngine.Random.Range(0.3f, 0.8f);
            stepTime = 0f;
            inputDelay = 0.25f;   // the E that started cooking must not stop the bar
            resultTimer = -1f;
            Draw(null);
        }

        void Update()
        {
            if (!Running || Time.timeScale == 0f) return;

            if (resultTimer >= 0f)
            {
                resultTimer -= Time.deltaTime;
                if (resultTimer < 0f) NextStep();
                return;
            }

            Step s = steps[index];
            stepTime += Time.deltaTime;
            inputDelay -= Time.deltaTime;
            marker += dir * s.speed * Time.deltaTime;
            if (marker > 1f) { marker = 2f - marker; dir = -1f; }
            if (marker < 0f) { marker = -marker; dir = 1f; }

            if (inputDelay <= 0f && GameInput.CookActionDown()) Judge(s);
            else if (stepTime > s.timeLimit) Finish(CookGrade.Bad);
            else Draw(null);
        }

        void Judge(Step s)
        {
            float d = Mathf.Abs(marker - zoneCenter);
            if (d <= s.zoneWidth * 0.2f) Finish(CookGrade.Perfect);
            else if (d <= s.zoneWidth * 0.5f) Finish(CookGrade.Good);
            else Finish(CookGrade.Bad);
        }

        void Finish(CookGrade g)
        {
            grades.Add(g);
            resultTimer = 0.6f;
            Draw(g);
        }

        void Draw(CookGrade? result)
        {
            Step s = steps[index];
            string title = "조리: " + s.title + "  (" + (index + 1) + " / " + steps.Length + "단계)";
            UIManager.Instance?.ShowCookBar(title, marker, zoneCenter, s.zoneWidth, result);
        }

        /// <summary>Two-step score: both Perfect = Perfect, at least two points = Good, else Bad.</summary>
        public static CookGrade Combine(List<CookGrade> list)
        {
            int score = 0;
            foreach (var g in list) score += g == CookGrade.Perfect ? 2 : g == CookGrade.Good ? 1 : 0;
            if (score >= list.Count * 2) return CookGrade.Perfect;
            if (score >= list.Count) return CookGrade.Good;
            return CookGrade.Bad;
        }
    }
}
