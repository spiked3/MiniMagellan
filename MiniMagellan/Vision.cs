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
        public static bool ConeConfidence = false;

        public void TaskRun()
        {
            Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });

            if (!Program.PilotString.Contains("com"))
            {
                Mq = new MqttClient(Program.PilotString);
                xCon.WriteLine(string.Format("^yVision.connecting to MQTT @ {0}", Program.PilotString));
                Mq.Connect("MMPXY");
                xCon.WriteLine("^yVision.Connected");

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
                            if (coneFlag != lostNFound.LostWait && DateTime.Now > lastConeTime + lostWaitTime)
                            {
                                if (coneFlag != lostNFound.Lost)
                                    xCon.WriteLine("^rCone lost");
                                coneFlag = lostNFound.Lost;
                                servoPosition = 90;
                                Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
                            }

                        // umm yeah - things kind of work in the background magically
                        // this is just some entertainment
                        // if (coneFlag != lastConeFlag)
                        //    message
                        // lastConeFlag = coneFlag
                        break;
                }
                Thread.Sleep(200);
            }

            xCon.WriteLine("^wVision exiting");
            SubState = VisionState.Idle;
            if (Mq != null && Mq.IsConnected)
                Mq.Disconnect();
        }

        // ##################### Pixy Blob

        // working ok, probably needs tweaking to cones and sunlight
        float kP = 0.2F, kI = 0.5F, kD = 0.01F;
        float prevErr, integral, derivative;
        int servoPosition = 90;
        DateTime lastTime = DateTime.Now;
        DateTime lostTime, foundTime, lastConeTime;
        enum lostNFound {  Lost, LostWait, Found };
        lostNFound coneFlag = lostNFound.Lost;
        TimeSpan lostWaitTime = new TimeSpan(0,0,2);    // 2 seconds

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
                     if (coneFlag == lostNFound.Found)
                    { // we just lost it
                        lostTime = nowTime;
                        coneFlag = lostNFound.LostWait;
                    }
                }
                else
                {   // have cone
                    if (coneFlag != lostNFound.Found)
                    {
                        if (coneFlag == lostNFound.Lost)
                            xCon.WriteLine("^gCone Found");
                        coneFlag = lostNFound.Found;
                        foundTime = nowTime;                        
                    }

                    float et = (nowTime - lastTime).Milliseconds / 1000F;
                    servoPosition -= (int)Pid(160, middleX, kP, kI, kD, ref prevErr, ref integral, ref derivative, et, 1);
                    //xCon.WriteLine(string.Format("^wSteering srvoPosition {0} e({1:F2}) i({2}) d({3})",
                    //    servoPosition, prevErr, integral, derivative));
                    Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
                    lastConeTime = nowTime;
                }

                lastTime = nowTime;
            }
        }

        public string GetStatus()
        {
            string t = string.Format("{0} Servo Position({1})", Enum.GetName(typeof(VisionState), SubState), servoPosition);
            return ("^w" + t);
        }

        public static void _T(String t)
        {
            Trace.WriteLine("Program::" + t);
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
