/**
 * Copyright (c) 2019-2020 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Simulator.Bridge;
using Simulator.Bridge.Data;
using Simulator.Utilities;
using Simulator.Sensors.UI;
using System.Collections;
using Simulator.Analysis;

namespace Simulator.Sensors
{
    [SensorType("Clock", new[] { typeof(ClockData) })]
    public partial class ClockSensor : SensorBase
    {
        Queue<Tuple<double, float, Action>> MessageQueue =
            new Queue<Tuple<double, float, Action>>();

        BridgeInstance Bridge;
        Publisher<ClockData> Publish;

        bool Destroyed = false;
        bool IsFirstFixedUpdate = true;
        double LastTimestamp;
        ClockData latestData;
        float realTimeStart;
        
        ClockData data;
        private bool Sending = false;

        public override SensorDistributionType DistributionType => SensorDistributionType.LowLoad;

        public override void OnBridgeSetup(BridgeInstance bridge)
        {
            Bridge = bridge;
            Publish = bridge.AddPublisher<ClockData>(Topic);
        }

        public void Start()
        {
            Task.Run(Publisher);
            realTimeStart = Time.time;
        }

        void OnDestroy()
        {
            Destroyed = true;
        }

        void Publisher()
        {
            var nextPublish = Stopwatch.GetTimestamp();

            while (!Destroyed)
            {
                long now = Stopwatch.GetTimestamp();
                if (now < nextPublish)
                {
                    Thread.Sleep(0);
                    continue;
                }

                Tuple<double, float, Action> msg = null;
                lock (MessageQueue)
                {
                    if (MessageQueue.Count > 0)
                    {
                        msg = MessageQueue.Dequeue();
                    }
                }

                if (msg != null)
                {
                    if (!Sending) // Drop this message if previous sending has not finished.
                    {
                        try
                        {
                            Sending = true;
                            msg.Item3();
                        }
                        catch
                        {
                            Sending = false;
                        }
                    }
                    nextPublish = now + (long)(Stopwatch.Frequency * msg.Item2);
                    LastTimestamp = msg.Item1;
                }
            }
        }

        void FixedUpdate()
        {
            if (IsFirstFixedUpdate)
            {
                if (Bridge != null && Bridge.Status == Status.Connected)
                {
                    lock (MessageQueue)
                    {
                        MessageQueue.Clear();
                    }
                }
                IsFirstFixedUpdate = false;
            }

            var time = SimulatorManager.Instance.CurrentTime;
            if (time < LastTimestamp)
            {
                return;
            }

            var data = new ClockData()
            {
                Clock = time,
            };

            latestData = data;

            if (Bridge != null && Bridge.Status == Status.Connected)
            {
                lock (MessageQueue)
                {
                    MessageQueue.Enqueue(Tuple.Create(time, Time.fixedDeltaTime,
                        (Action)(() => Publish(data, () => Sending = false))));
                }
            }
        }

        void Update()
        {
            IsFirstFixedUpdate = true;
        }

        public override void SetAnalysisData()
        {
            SensorAnalysisData = new List<AnalysisReportItem>
            {
                new AnalysisReportItem {
                    name = "Simulation Duration",
                    type = "duration",
                    value = SimulatorManager.Instance.CurrentTime - SimulatorManager.Instance.SessionStartTime
                },
                new AnalysisReportItem {
                    name = "Realtime Duration",
                    type = "duration",
                    value = Time.time - realTimeStart
                },
            };
        }

        public override void OnVisualize(Visualizer visualizer)
        {
            UnityEngine.Debug.Assert(visualizer != null);

            if (latestData == null)
            {
                return;
            }

            var graphData = new Dictionary<string, object>()
            {
                {"Time", latestData.Clock},
                {"Fixed DeltaTime", Time.fixedDeltaTime}
            };
            visualizer.UpdateGraphValues(graphData);
        }

        public override void OnVisualizeToggle(bool state)
        {
            //
        }
    }
}
