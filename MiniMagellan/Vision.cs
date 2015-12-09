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
    public class Vision : IBehavior
    {
        MqttClient Mq;

        public bool Lock { get; set; }

        enum VisionState { Idle, Run };

        VisionState SubState = VisionState.Idle;
        public static float ConeConfidence = 0;

        // working ok, probably needs tweaking to cones and sunlight
        float kP = 0.2F, kI = 0.3F, kD = 0.001F;
        float prevErr, integral, derivative;
        int servoPosition = 90;

        DateTime lastTime = DateTime.Now;
        DateTime lostTime, foundTime, lastConeTime;

        enum ConeState { Lost, LostWait, Found };
        ConeState coneFlag = ConeState.Lost;

        TimeSpan lostWaitTime = new TimeSpan(0, 0, 2);    // 2 seconds

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
                while (Lock)
                    Thread.SpinWait(100);

                switch (SubState)
                {
                    case VisionState.Run:
                        if (coneFlag != ConeState.LostWait && DateTime.Now > lastConeTime + lostWaitTime)
                        {
                            if (coneFlag != ConeState.Lost)
                            {
                                coneFlag = ConeState.Lost;
                                Trace.t(cc.Warn, "Cone lost"); Console.Beep(); Console.Beep();
                                servoPosition = 90;
                                Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
                            }
                        }
                        break;
                }
                Thread.Sleep(200);
            }

            Trace.t(cc.Warn, "Vision exiting");
            SubState = VisionState.Idle;
            if (Mq != null && Mq.IsConnected)
                Mq.Disconnect();
        }

        // ##################### Pixy Blob

        private void PixyMqRecvd(object sender, MqttMsgPublishEventArgs e)
        {
            //_T();
            if (SubState == VisionState.Run)
            {
                dynamic a = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(e.Message));
                int middleX = a.X + (a.Width / 2);      // 160 is pixy center
                DateTime nowTime = DateTime.Now;

                // lots or no rects is a good indication we dont have the cone
                // after some elapsed time of no cone, return servo to 90

                if (a.Count > 2)    
                {   // dont have cone
                     if (coneFlag == ConeState.Found)
                    { // we just lost it
                        lostTime = nowTime;
                        coneFlag = ConeState.LostWait;
                    }
                }
                else
                {   // have cone
                    if (coneFlag != ConeState.Found)
                    {
                        if (coneFlag == ConeState.Lost)
                        {   // we just found it
                            Trace.t(cc.Good, "Cone Found"); Console.Beep();
                            coneFlag = ConeState.Found;
                            prevErr = integral = derivative = 0;
                            foundTime = nowTime;
                        }
                    }

                    float et = (nowTime - lastTime).Milliseconds / 1000F;
                    servoPosition -= (int)Pid(160, middleX, kP, kI, kD, ref prevErr, ref integral, ref derivative, et, .8F);
                    //xCon.WriteLine(string.Format("^wSteering srvoPosition {0} e({1:F2}) i({2}) d({3})",
                    //    servoPosition, prevErr, integral, derivative));
                    if (servoPosition < -100)
                        servoPosition = 100;
                    else if (servoPosition > 100)
                        servoPosition = 100;
                    Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
                    lastConeTime = nowTime;
                }

                lastTime = nowTime;
            }
        }

        public string GetStatus()
        {
            string t = string.Format("{0} Servo Position({1})", Enum.GetName(typeof(VisionState), SubState), servoPosition);
            return ("^c" + t);
        }

        float Pid(float setPoint, float presentValue, float Kp, float Ki, float Kd,
            ref float previousError, ref float integral, ref float derivative, float dt, float errorSmooth)
        {
            float error = setPoint - presentValue;
            error = (float)(errorSmooth * error + (1.0 - errorSmooth) * previousError);  // complimentry filter smoothing
            integral += error * dt;
            if (integral > 10)
                integral = 10;
            if (integral < -10)
                integral = -10;
            derivative = (error - previousError) / dt;
            float output = Kp * error + Ki * integral + Kd * derivative;
            previousError = error;
            return output;
        }
    }
}
