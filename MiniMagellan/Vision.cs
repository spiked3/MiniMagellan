using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace MiniMagellan
{
    public class Vision
    {
        MqttClient Mq;

        // todo Sweep is not used/implemented yet
        public enum VisionState { Idle, Run, Sweep };

        public VisionState SubState = VisionState.Idle;

        public enum ConeState { Lost, LostWait, Found };
        public ConeState coneFlag = ConeState.Lost;

        public event EventHandler OnLostCone;
        public event EventHandler OnFoundCone;

        DateTime pidCycleTime = new DateTime(0);
        DateTime lastSeenTime = new DateTime(0);
        TimeSpan lostWaitTime = new TimeSpan(0, 0, 0, 1);

        // servo PID
        float kP = 0.1F, kI = 0.2F, kD = 0.01F;
        float prevErr, integral, derivative;
        public int servoPosition = 90;
        
        public void TaskRun()
        {
            Trace.t(cc.Norm, "Vision TaskRun started");

            Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });

            if (!Program.PilotString.Contains("com"))
            {
                Mq = new MqttClient(Program.PilotString);
                Trace.t(cc.Norm, string.Format("vision connecting to MQTT @ {0}", Program.PilotString));
                Mq.Connect("MMPXY");
                Trace.t(cc.Norm, "vision connected");

                Mq.MqttMsgPublishReceived += PixyMqRecvd;
                Mq.Subscribe(new string[] { "robot1/pixyCam" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
            }

            SubState = VisionState.Run;

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                if (SubState == VisionState.Run)
                    if (coneFlag != ConeState.Lost && DateTime.Now > lastSeenTime + lostWaitTime)
                        LostCone();

                Program.Delay(100).Wait();

                // todo - if navigation is turning, we would expect to need to adjust the servo with the turn
            }

            Trace.t(cc.Warn, "Vision exiting");
            SubState = VisionState.Idle;
            if (Mq != null && Mq.IsConnected)
                Mq.Disconnect();
        }

        private void LostCone()
        {
            coneFlag = ConeState.Lost;
            Trace.t(cc.Warn, "Vision::Cone lost"); Console.Beep();

            servoPosition = 90;
            Program.Pilot.Send(new { Cmd = "SRVO", Value = 90 });
            if (OnLostCone != null)
                OnLostCone(this, null);
        }

        private void FoundCone()
        {
            coneFlag = ConeState.Found;
            Trace.t(cc.Good, "Vision::Cone Found"); Console.Beep();

            prevErr = integral = derivative = 0;

            if (OnFoundCone != null)
                OnFoundCone(this, null);
        }

        void PixyMqRecvd(object sender, MqttMsgPublishEventArgs e)
        {
            //_T();
            if (SubState == VisionState.Run)
            {
                dynamic a = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(e.Message));
                DateTime nowTime = DateTime.Now;

                // lots or no rects is a good indication we dont have the cone
                // after some elapsed time of no cone, return servo to 90


                if (1.0 / (int)a["Count"] < .49)
                    switch (coneFlag)       // no cone
                    {
                        case ConeState.Found:
                            coneFlag = ConeState.LostWait;
                            break;
                    }
                else
                {
                    lastSeenTime = nowTime;
                    switch (coneFlag)   // cone
                    {
                        case ConeState.Lost:
                            FoundCone();
                            break;
                        case ConeState.LostWait:        // todo code smell
                            coneFlag = ConeState.Found;
                            goto case ConeState.Found;
                        case ConeState.Found:
                            float et = (float)((nowTime - pidCycleTime).Milliseconds) / 1000;
                            servoPosition -= (int)Pid(160F, (float)(a.Center), kP, kI, kD, ref prevErr, ref integral, ref derivative, et, .8F);
                            //Trace.t(cc.Norm, string.Format("Steering srvoPosition {0} e({1:F2}) i({2}) d({3})", servoPosition, prevErr, integral, derivative));
                            if (servoPosition < 0)
                                servoPosition = 0;
                            else if (servoPosition > 180)
                                servoPosition = 180;

                            Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
                            break;
                    }
                    pidCycleTime = nowTime;
                }
            }
        }

        public string GetStatus()
        {
            return string.Format("{0} SubState({2}) Servo Position({1})", 
                    Enum.GetName(typeof(VisionState), SubState), servoPosition, Enum.GetName(typeof(ConeState), coneFlag));
        }

        float Pid(float setPoint, float presentValue, float Kp, float Ki, float Kd,
            ref float previousError, ref float integral, ref float derivative, float dt, float errorSmooth)
        {
            float error = setPoint - presentValue;
            error = (float)(errorSmooth * error + (1.0 - errorSmooth) * previousError);  // complimentry filter smoothing
            integral = integral + error * dt;
            derivative = (error - previousError) / dt;
            //derivative = derivative > 100 ? 100 : derivative < -100 ? 100 : derivative;         // constrain derivative
            derivative = float.IsNaN(derivative) ? 0 : derivative;
            float output = Kp * error + Ki * integral + Kd * derivative;
            previousError = error;
            return output;
        }
    }
}
