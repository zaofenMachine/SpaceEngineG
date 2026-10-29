using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceEngine.Runtime
{
    /// <summary>
    /// Editor/runtime frame-time probe for art iteration: holds a view (optionally turning the camera
    /// so cached sky layers recompute every frame) and reports mean / 95th-percentile frame times.
    /// </summary>
    public sealed class FrameProbe : MonoBehaviour
    {
        public static string LastReport { get; private set; }

        public void Measure(string label, int frames, float turnDegreesPerFrame)
        {
            LastReport = null;
            StartCoroutine(Run(label, frames, turnDegreesPerFrame));
        }

        IEnumerator Run(string label, int frames, float turn)
        {
            var app = GetComponent<UniverseApp>();
            var times = new List<double>();
            Quaternion start = app.ViewCamera.transform.rotation;
            for (int i = 0; i < 30; i++) yield return null;
            for (int i = 0; i < frames; i++)
            {
                if (turn != 0) app.SetObserver(app.Observer, Quaternion.AngleAxis(Mathf.Sin(i * .1f) * turn * 10, Vector3.up) * start);
                yield return null;
                times.Add(Time.unscaledDeltaTime * 1000);
            }
            times.Sort();
            double sum = 0; foreach (double t in times) sum += t;
            LastReport = label + ": mean " + (sum / times.Count).ToString("F2") + " ms, p95 " + times[(int)((times.Count - 1) * .95)].ToString("F2") + " ms, max " + times[times.Count - 1].ToString("F2") + " ms";
            Debug.Log("FRAME_PROBE " + LastReport);
        }
    }
}
